using System;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Запрос клиента на изменение мира. Одна MessageBase на все действия:
    /// версия протокола, код действия, цель (netId, 0 = «сам отправитель»),
    /// до двух чисел, команда и имя префаба/карты.
    ///
    /// Сервер разрешает отправителя по соединению (клиент не может представиться
    /// чужим именем соединения), проверяет версию протокола, клампует счётчики
    /// (<see cref="Protocol.ClampCount"/>), режет rate-limit'ом
    /// (<see cref="RateLimiter"/>) и по настройке прав может ограничить цель
    /// самим отправителем. Результат исполнения возвращается ack'ом отправителю,
    /// поэтому клиент отличает «сервер исполнил» от «запрос потерялся».
    /// </summary>
    internal class SandboxRequest : MessageBase
    {
        public byte Version;
        public byte Action;
        public uint TargetNetId;
        public int P1;
        public int P2;
        public byte Team;
        public string Name;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Protocol.ProtoVersion);
            writer.Write(Action);
            writer.Write(TargetNetId);
            writer.Write(P1);
            writer.Write(P2);
            writer.Write(Team);
            writer.Write(Name != null);
            if (Name != null) writer.Write(Protocol.ClampName(Name));
        }

        public override void Deserialize(NetworkReader reader)
        {
            Version = reader.ReadByte();
            Action = reader.ReadByte();
            TargetNetId = reader.ReadUInt32();
            P1 = reader.ReadInt32();
            P2 = reader.ReadInt32();
            Team = reader.ReadByte();
            Name = reader.ReadBoolean() ? reader.ReadString() : null;
        }
    }

    /// <summary>
    /// Подтверждение сервера: запрос исполнен или отклонён (версия, rate-limit,
    /// права). Позволяет клиенту не гадать по чат-сообщениям, дошёл ли запрос.
    /// </summary>
    internal class SandboxAck : MessageBase
    {
        public byte Action;
        public byte Ok;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Action);
            writer.Write(Ok);
        }

        public override void Deserialize(NetworkReader reader)
        {
            Action = reader.ReadByte();
            Ok = reader.ReadByte();
        }
    }

    /// <summary>Пустое сообщение ping/pong: им клиент узнаёт, стоит ли на хосте мод.</summary>
    internal class SandboxPing : MessageBase
    {
    }

    /// <summary>
    /// Клиент-серверный канал меню.
    ///
    /// Зачем: мир меняет только сервер, но меню открыто может быть у любого
    /// игрока. Действие клиента уезжает запросом на сервер и исполняется там
    /// тем же кодом, что и локальное нажатие (<see cref="SandboxActions"/>).
    ///
    /// Транспорт - штатный HLAPI игры: клиент берётся из
    /// NetworkManagerSystem.singleton.client, обработчики регистрируются по
    /// событиям onStartServerGlobal/onStartClientGlobal (а не поллингом),
    /// номера сообщений выводятся из настройки General.MessageBaseId
    /// (по умолчанию 31337..31340 - вне диапазона игры; при коллизии с другим
    /// модом базовый номер меняется конфигом без пересборки).
    ///
    /// Если хост без мода, запросы не обрабатываются, а ping не получает pong -
    /// меню честно пишет «хост без мода» вместо молчания.
    /// </summary>
    internal static class NetProtocol
    {
        /// <summary>Канал 0 в HLAPI - reliable sequenced: порядок и доставка.</summary>
        private const int Channel = 0;

        private static short _base = 31337;
        private static short MsgRequest { get { return _base; } }
        private static short MsgPing { get { return (short)(_base + 1); } }
        private static short MsgPong { get { return (short)(_base + 2); } }
        private static short MsgAck { get { return (short)(_base + 3); } }

        private static bool _serverRegistered;
        private static bool _clientRegistered;
        private static float _pongAt = -1f;
        private static float _pingAt = -1f;
        private static readonly RateLimiter _limiter = new RateLimiter();
        private static bool _eventsSubscribed;

        /// <summary>Есть ли мод на стороне сервера (получен pong на ping).</summary>
        public static bool HostHasMod { get; private set; }

        /// <summary>Мы сами сервер - запросы исполняются локально.</summary>
        public static bool IsServer { get { return NetworkServer.active; } }

        /// <summary>
        /// Подписка на сетевые события игры один раз; вызывается из Plugin.Awake.
        /// Регистрация обработчиков дальше происходит по событиям, а не поллингом.
        /// </summary>
        public static void SubscribeEvents()
        {
            if (_eventsSubscribed) return;
            _eventsSubscribed = true;
            try
            {
                NetworkManagerSystem.onStartServerGlobal += OnServerStart;
                NetworkManagerSystem.onStartClientGlobal += OnClientStart;
                NetworkManagerSystem.onStopClientGlobal += OnClientStop;
            }
            catch (Exception e) { GameApi.WarnOnce("NetProtocol.SubscribeEvents", e); }
        }

        private static void OnServerStart()
        {
            try
            {
                ReadBaseId();
                NetworkServer.RegisterHandler(MsgRequest, OnRequest);
                NetworkServer.RegisterHandler(MsgPing, OnPing);
                _serverRegistered = true;
                HostHasMod = true;
                _limiter.Reset();
                GameApi.Info($"sandbox network: server handlers on msg {MsgRequest}..{MsgAck}");
            }
            catch (Exception e) { GameApi.Warn("OnServerStart", e); }
        }

        private static void OnClientStart(NetworkClient client)
        {
            try
            {
                if (client == null) return;
                ReadBaseId();
                client.RegisterHandler(MsgPong, OnPong);
                client.RegisterHandler(MsgAck, OnAck);
                _clientRegistered = true;
                GameApi.Info("sandbox network: client handlers registered");
            }
            catch (Exception e) { GameApi.Warn("OnClientStart", e); }
        }

        private static void OnClientStop()
        {
            _clientRegistered = false;
            HostHasMod = false;
            _pongAt = -1f;
            _pingAt = -1f;
        }

        /// <summary>Базовый номер сообщений из конфига (защита от коллизий с модами).</summary>
        private static void ReadBaseId()
        {
            var cfg = SandboxMenuPlugin.MessageBaseId;
            int v = cfg != null ? cfg.Value : 31337;
            if (v < 1000 || v > 32760) v = 31337;
            _base = (short)v;
        }

        /// <summary>
        /// Вызывается из Plugin.Update: держит ping/pong-диагностику и сбрасывает
        /// флаги регистрации, если сервер/клиент реально исчезли.
        /// </summary>
        public static void Tick()
        {
            try
            {
                if (!NetworkServer.active && _serverRegistered)
                {
                    TryUnregisterServer();
                    _serverRegistered = false;
                }

                var nm = NetworkManagerSystem.singleton;
                // NetworkClient.active в игровой сборке hlapi статический
                // (instance-доступ не компилируется против com.unity.multiplayer-hlapi.Runtime)
                bool clientAlive = nm != null && nm.client != null && NetworkClient.active;
                if (!clientAlive && _clientRegistered) OnClientStop();

                if (_clientRegistered && !_serverRegistered)
                {
                    float now = Time.unscaledTime;
                    if (_pingAt > 0f && now - _pingAt > 4f) HostHasMod = false;
                    if (_pongAt >= 0f && now - _pongAt > 8f)
                    {
                        HostHasMod = false;
                        _pingAt = 0f; // пинганём ещё раз при следующем открытии меню
                    }
                }
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("NetProtocol.Tick", e);
            }
        }

        private static void TryUnregisterServer()
        {
            try { NetworkServer.UnregisterHandler(MsgRequest); } catch { }
            try { NetworkServer.UnregisterHandler(MsgPing); } catch { }
        }

        /// <summary>Спросить хоста, есть ли у него мод (вызывается при открытии меню).</summary>
        public static void PingHost()
        {
            if (_serverRegistered) { HostHasMod = true; return; }
            var client = CurrentClient();
            if (client == null) return;
            try
            {
                client.SendByChannel(MsgPing, new SandboxPing(), Channel);
                _pingAt = Time.unscaledTime;
            }
            catch (Exception e) { GameApi.WarnOnce("ping", e); }
        }

        /// <summary>Отправить запрос действия на сервер. false - нечем отправить.</summary>
        public static bool Send(SandboxRequest req)
        {
            req.Version = Protocol.ProtoVersion;
            req.Name = Protocol.ClampName(req.Name);
            var client = CurrentClient();
            if (client == null) return false;
            try
            {
                return client.SendByChannel(MsgRequest, req, Channel);
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("send request", e);
                return false;
            }
        }

        /// <summary>Клиент HLAPI текущей сессии или null, если подключения нет.</summary>
        private static NetworkClient CurrentClient()
        {
            try
            {
                var nm = NetworkManagerSystem.singleton;
                if (nm == null) return null;
                var client = nm.client;
                if (client == null || !NetworkClient.active) return null;
                return client;
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("NetworkManagerSystem.client", e);
                return null;
            }
        }

        /// <summary>Сервер: проверить запрос и исполнить от имени отправителя.</summary>
        private static void OnRequest(NetworkMessage netMsg)
        {
            SandboxRequest req;
            try
            {
                req = netMsg.ReadMessage<SandboxRequest>();
            }
            catch (Exception e)
            {
                GameApi.Warn("OnRequest parse", e);
                return;
            }

            byte fail = 0;
            if (req.Version != Protocol.ProtoVersion)
            {
                GameApi.Warn("OnRequest", new InvalidOperationException(
                    $"protocol version {req.Version}, expected {Protocol.ProtoVersion}"));
                fail = 1;
            }
            else if (netMsg.conn != null &&
                     !_limiter.TryConsume(netMsg.conn.GetHashCode(), Time.unscaledTime))
            {
                fail = 2;
            }

            if (fail != 0)
            {
                Ack(netMsg.conn, req.Action, 0);
                return;
            }

            var requester = PlayerTargets.FindByConnection(netMsg.conn);
            var target = req.TargetNetId != 0u
                ? PlayerTargets.FindByNetId(req.TargetNetId)
                : requester;
            if (target == null) target = requester;

            // режим прав: self запрещает действия над чужими целями
            var mode = SandboxMenuPlugin.RemoteTargets;
            bool selfOnly = mode != null &&
                            string.Equals(mode.Value, "self", StringComparison.OrdinalIgnoreCase);
            if (selfOnly && target != null && requester != null &&
                !ReferenceEquals(target, requester))
            {
                Ack(netMsg.conn, req.Action, 0);
                return;
            }

            req.P1 = Protocol.ClampCount(req.Action, req.P1);
            req.P2 = Protocol.ClampCount(req.Action, req.P2);
            req.Name = Protocol.ClampName(req.Name);
            SandboxActions.ExecuteRequest(req, target);
            Ack(netMsg.conn, req.Action, 1);
        }

        private static void Ack(NetworkConnection conn, byte action, byte ok)
        {
            if (conn == null) return;
            try
            {
                conn.SendByChannel(MsgAck, new SandboxAck { Action = action, Ok = ok }, Channel);
            }
            catch (Exception e) { GameApi.WarnOnce("ack", e); }
        }

        /// <summary>Сервер: ответить pong'ом - клиент узнает, что мод есть.</summary>
        private static void OnPing(NetworkMessage netMsg)
        {
            try
            {
                if (netMsg.conn != null) netMsg.conn.SendByChannel(MsgPong, new SandboxPing(), Channel);
            }
            catch (Exception e) { GameApi.WarnOnce("pong", e); }
        }

        /// <summary>Клиент: pong получен - на хосте стоит мод.</summary>
        private static void OnPong(NetworkMessage netMsg)
        {
            HostHasMod = true;
            _pongAt = Time.unscaledTime;
        }

        /// <summary>Клиент: сервер отклонил запрос - сказать об этом игроку.</summary>
        private static void OnAck(NetworkMessage netMsg)
        {
            try
            {
                var ack = netMsg.ReadMessage<SandboxAck>();
                if (ack.Ok != 0) return;
                GameApi.SayErr(Texts.T(
                    $"сервер отклонил запрос (действие {ack.Action}): версия, лимит или права",
                    $"server rejected the request (action {ack.Action}): version, rate limit or rights"));
            }
            catch (Exception e) { GameApi.WarnOnce("OnAck", e); }
        }
    }
}

