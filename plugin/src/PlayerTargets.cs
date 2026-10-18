using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Кого именно касается действие меню.
    ///
    /// Зачем это отдельная сущность: в кооперативе все игроки находятся в одной
    /// команде TeamIndex.Player, поэтому «заспавнить союзника» без явного
    /// владельца означает общего миньона для всей группы - он появляется у того,
    /// кто спавнил, и бегает за всеми. Здесь каждое действие привязано к
    /// конкретному NetworkUser: его тело, его master, его команда, его позиция,
    /// его инвентарь.
    ///
    /// Весь API принимает цель явно (BodyOf/MasterOf/...); свойства Body/Master и
    /// прочие - тонкие обёртки над текущим выбором меню. Клиентский запрос несёт
    /// netId цели, и сервер разрешает его через FindByNetId, а владельца запроса -
    /// через FindByConnection: действия клиента исполняются от его имени, даже
    /// когда меню открыто не у хоста.
    ///
    /// Источник списка - NetworkUser.readOnlyInstancesList (публичное поле).
    /// </summary>
    internal static class PlayerTargets
    {
        private static readonly List<NetworkUser> _users = new List<NetworkUser>();
        private static int _selected;

        public static int Count { get { return _users.Count; } }

        public static IReadOnlyList<NetworkUser> Users { get { return _users; } }

        public static int Selected
        {
            get { return _selected; }
            set { _selected = Mathf.Clamp(value, 0, Math.Max(0, _users.Count - 1)); }
        }

        public static void Refresh()
        {
            _users.Clear();
            try
            {
                ReadOnlyCollection<NetworkUser> list = NetworkUser.readOnlyInstancesList;
                if (list != null)
                {
                    foreach (var nu in list)
                    {
                        if (nu != null) _users.Add(nu);
                    }
                }
            }
            catch (Exception e) { GameApi.WarnOnce("NetworkUser.readOnlyInstancesList", e); }

            if (_users.Count == 0)
            {
                // вне забега списка нет - оставим локального пользователя, если есть
                var lu = GameApi.GetLocalUser();
                if (lu != null && lu.currentNetworkUser != null) _users.Add(lu.currentNetworkUser);
            }
            Selected = _selected;
        }

        public static NetworkUser SelectedUser
        {
            get
            {
                if (_selected < 0 || _selected >= _users.Count) return null;
                return _users[_selected];
            }
        }

        public static string DisplayName(NetworkUser nu)
        {
            if (nu == null) return Texts.T("(нет)", "(none)");
            int idx = _users.IndexOf(nu);
            string name = null;
            try
            {
                var npn = nu.GetNetworkPlayerName();
                name = npn.GetResolvedName();
            }
            catch (Exception e) { GameApi.WarnOnce("GetNetworkPlayerName", e); }

            if (string.IsNullOrEmpty(name))
            {
                // NetworkUser.userName - публичное поле (не метод)
                try { name = nu.userName; } catch { }
            }
            if (string.IsNullOrEmpty(name)) name = Texts.T("игрок", "player") + " " + (idx >= 0 ? idx + 1 : "?");

            var local = GameApi.GetLocalUser();
            bool isLocal = local != null && ReferenceEquals(nu, local.currentNetworkUser);
            return isLocal ? name + Texts.T(" (ты)", " (you)") : name;
        }

        public static string[] DisplayNames()
        {
            var arr = new string[_users.Count];
            for (int i = 0; i < _users.Count; i++) arr[i] = DisplayName(_users[i]);
            return arr;
        }

        // -------------------------------------------------------- resolution --
        /// netId цели для сетевого запроса (0 - «сам отправитель»).
        public static uint NetIdOf(NetworkUser nu)
        {
            if (nu == null) return 0u;
            try { return nu.netId.Value; }
            catch (Exception e) { GameApi.WarnOnce("NetworkUser.netId", e); return 0u; }
        }

        public static NetworkUser FindByNetId(uint netId)
        {
            if (netId == 0u) return null;
            foreach (var nu in _users)
            {
                if (nu != null && NetIdOf(nu) == netId) return nu;
            }
            return null;
        }

        /// Владелец запроса: соединение, с которого пришло сообщение.
        public static NetworkUser FindByConnection(NetworkConnection conn)
        {
            if (conn == null) return null;
            foreach (var nu in _users)
            {
                if (nu == null) continue;
                try { if (ReferenceEquals(nu.connectionToClient, conn)) return nu; }
                catch (Exception e) { GameApi.WarnOnce("connectionToClient", e); }
            }
            return null;
        }

        // --------------------------------------------------------- derived ----
        public static CharacterBody BodyOf(NetworkUser nu)
        {
            if (nu == null) return GameApi.GetBody();
            try
            {
                var b = nu.GetCurrentBody();
                if (b != null) return b;
            }
            catch (Exception e) { GameApi.WarnOnce("NetworkUser.GetCurrentBody", e); }
            return GameApi.GetBody();
        }

        public static CharacterMaster MasterOf(NetworkUser nu)
        {
            if (nu == null) return GameApi.GetMaster();
            try
            {
                var m = nu.master;
                if (m != null) return m;
            }
            catch (Exception e) { GameApi.WarnOnce("NetworkUser.master", e); }
            var b = BodyOf(nu);
            return b != null ? b.master : GameApi.GetMaster();
        }

        public static Vector3 PositionOf(NetworkUser nu)
        {
            var b = BodyOf(nu);
            if (b == null) return Vector3.zero;
            var t = b.coreTransform;
            if (t != null) return t.position;
            return b.footPosition;
        }

        public static Quaternion RotationOf(NetworkUser nu)
        {
            var b = BodyOf(nu);
            if (b == null) return Quaternion.identity;
            var t = b.coreTransform;
            return t != null ? t.rotation : Quaternion.identity;
        }

        public static TeamIndex TeamOf(NetworkUser nu)
        {
            var b = BodyOf(nu);
            try
            {
                if (b != null && b.teamComponent != null) return b.teamComponent.teamIndex;
            }
            catch (Exception e) { GameApi.WarnOnce("PlayerTargets.TeamOf", e); }
            return TeamIndex.Player;
        }

        /// Точка рядом с целью, а не рядом с тем, кто открыл меню.
        public static Vector3 PointNear(NetworkUser nu, float distance, float up)
        {
            var b = BodyOf(nu);
            if (b == null) return Vector3.zero;
            var t = b.coreTransform;
            var origin = t != null ? t.position : b.footPosition;
            var fwd = t != null ? t.forward : Vector3.forward;
            return origin + fwd.normalized * distance + Vector3.up * up;
        }

        /// Кольцо точек вокруг цели - для спавна группы.
        public static Vector3 PointOnRing(NetworkUser nu, int index, int total, float radius)
        {
            var origin = PositionOf(nu);
            if (origin == Vector3.zero) return Vector3.zero;
            if (total <= 1) return origin + Vector3.up;
            float angle = 360f * index / total;
            return origin + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius + Vector3.up * 0.5f;
        }

        // ------------------------------------------- wrappers for selection ---
        public static CharacterBody Body { get { return BodyOf(SelectedUser); } }

        public static CharacterMaster Master { get { return MasterOf(SelectedUser); } }

        public static Vector3 Position { get { return PositionOf(SelectedUser); } }

        public static Quaternion Rotation { get { return RotationOf(SelectedUser); } }

        public static TeamIndex Team { get { return TeamOf(SelectedUser); } }

        public static Vector3 PointNear(float distance, float up)
        {
            return PointNear(SelectedUser, distance, up);
        }

        public static Vector3 PointOnRing(int index, int total, float radius)
        {
            return PointOnRing(SelectedUser, index, total, radius);
        }
    }
}
