using System;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Коды действий сетевого запроса (байт, стабильны между версиями плагина).
    /// </summary>
    internal static class ActionCode
    {
        public const byte GiveItem = 1;
        public const byte RemoveItem = 2;
        public const byte ClearItems = 3;
        public const byte DropPickup = 4;
        public const byte ClearDrops = 5;
        public const byte SpawnMaster = 6;
        public const byte RemoveSpawnedMasters = 7;
        public const byte SpawnProp = 8;
        public const byte RemoveSpawnedProps = 9;
        public const byte CloneSetDesired = 10;
        public const byte CloneSpawnNow = 11;
        public const byte CloneClearTarget = 12;
        public const byte CloneClearAll = 13;
        public const byte CloneSetFlags = 14;
        public const byte CloneSetKeepAlive = 15;
        public const byte CleanupOrphans = 16;
        public const byte ClearDropsNear = 17;
        public const byte ClearMenuDrops = 18;
    }

    /// <summary>
    /// Запрос клиента на изменение мира. Одна MessageBase на все действия:
    /// код действия + цель (netId, 0 = «сам отправитель») + до двух чисел +
    /// команда + имя префаба/карты. Сервер разрешает отправителя по соединению,
    /// поэтому клиент не может представиться чужим именем соединения.
    /// </summary>
    internal class SandboxRequest : MessageBase
    {
        public byte Action;
        public uint TargetNetId;
        public int P1;
        public int P2;
        public byte Team;
        public string Name;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Action);
            writer.Write(TargetNetId);
            writer.Write(P1);
            writer.Write(P2);
            writer.Write(Team);
            writer.Write(Name != null);
            if (Name != null) writer.Write(Name);
        }

        public override void Deserialize(NetworkReader reader)
        {
            Action = reader.ReadByte();
            TargetNetId = reader.ReadUInt32();
            P1 = reader.ReadInt32();
            P2 = reader.ReadInt32();
            Team = reader.ReadByte();
            Name = reader.ReadBoolean() ? reader.ReadString() : null;
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
    /// игрока. Без этого канала кнопки клиента молча ничего не делали («не
    /// работает, когда я не хост»). Теперь действие клиента уезжает запросом на
    /// сервер, где исполняется тем же кодом, что и локальное нажатие, а цель
    /// разрешается из самого запроса.
    ///
    /// Транспорт - штатный HLAPI игры: клиент берётся из
    /// NetworkManagerSystem.singleton.client (публичное свойство базового
    /// NetworkManager), обработчики регистрируются стандартными
    /// RegisterHandler; номера сообщений выбраны вне диапазона игры (31337+).
    /// Если хост без мода, запросы просто не обрабатываются, а ping не получает
    /// pong - меню честно пишет «хост без мода» вместо молчания.
    /// </summary>
    internal static class NetProtocol
    {
        public const short MsgRequest = 31337;
        public const short MsgPing = 31338;
        public const short MsgPong = 31339;

        /// Канал 0 в HLAPI - reliable sequenced, порядок и доставка гарантированы.
        private const int Channel = 0;

        private static bool _serverRegistered;
        private static bool _clientRegistered;
        private static float _pongAt = -1f;
        private static float _pingAt = -1f;

        /// <summary>Есть ли мод на стороне сервера (получен pong на ping).</summary>
        public static bool HostHasMod { get; private set; }

        /// <summary>Мы сами сервер - запросы исполняются локально.</summary>
        public static bool IsServer { get { return NetworkServer.active; } }

        /// <summary>
        /// Вызывается из Plugin.Update: регистрирует обработчики, когда
        /// появляется сервер или клиент, и следит за актуальностью ping/pong.
        /// </summary>
        public static void Tick()
        {
            try
            {
                if (NetworkServer.active)
                {
                    if (!_serverRegistered)
                    {
                        NetworkServer.RegisterHandler(MsgRequest, OnRequest);
                        NetworkServer.RegisterHandler(MsgPing, OnPing);
                        _serverRegistered = true;
                        HostHasMod = true;
                        GameApi.Info("sandbox network: server handlers registered");
                    }
                }
                else if (_serverRegistered)
                {
                    _serverRegistered = false;
                }

                var client = CurrentClient();
                if (client != null)
                {
                    if (!_clientRegistered)
                    {
                        client.RegisterHandler(MsgPong, OnPong);
                        _clientRegistered = true;
                        GameApi.Info("sandbox network: client handlers registered");
                    }
                }
                else if (_clientRegistered)
                {
                    _clientRegistered = false;
                    HostHasMod = false;
                    _pongAt = -1f;
                    _pingAt = -1f;
                }

                // pong старше четырёх секунд или вовсе не пришёл - хост без мода
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

        /// <summary>Клиент HLAPI текущей сессии или null, если подключения нет.</summary>
        private static NetworkClient CurrentClient()
        {
            try
            {
                var nm = NetworkManagerSystem.singleton;
                if (nm == null) return null;
                var client = nm.client;
                if (client == null || !client.active) return null;
                return client;
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("NetworkManagerSystem.client", e);
                return null;
            }
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

        // ------------------------------------------------------------ server --
        /// <summary>Сервер: исполнить запрос от имени его отправителя.</summary>
        private static void OnRequest(NetworkMessage netMsg)
        {
            try
            {
                var req = netMsg.ReadMessage<SandboxRequest>();
                var requester = PlayerTargets.FindByConnection(netMsg.conn);
                var target = req.TargetNetId != 0u
                    ? PlayerTargets.FindByNetId(req.TargetNetId)
                    : requester;
                if (target == null) target = requester;
                SandboxActions.ExecuteRequest(req, target);
            }
            catch (Exception e)
            {
                GameApi.Warn("OnRequest", e);
            }
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

        // ------------------------------------------------------------ client --
        /// <summary>Клиент: pong получен - на хосте стоит мод.</summary>
        private static void OnPong(NetworkMessage netMsg)
        {
            HostHasMod = true;
            _pongAt = Time.unscaledTime;
        }
    }
}
