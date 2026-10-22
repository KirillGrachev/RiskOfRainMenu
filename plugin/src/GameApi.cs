using System;
using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Тонкая обёртка над RoR2 API.
    ///
    /// Два правила, из-за которых она существует:
    ///  1. любой вызов игры может вернуть null (нет локального игрока, нет тела,
    ///     нет сцены, идёт загрузка) — проверяем, а не надеемся;
    ///  2. любое действие, меняющее мир, допустимо только на сервере.
    ///
    /// Все имена членов сверены с метаданными RoR2.dll (tools/apidump.py):
    /// GetFirstLocalUser, cachedBody, cachedMaster, GetCurrentBody, GetBody,
    /// MasterSummon.Perform, MasterCatalog.FindMasterPrefab,
    /// PickupDropletController.CreatePickupDroplet, Inventory.GiveItem,
    /// Language.GetString, Chat.AddMessage.
    /// </summary>
    internal static class GameApi
    {
        internal static ManualLogSource Log;

        private static readonly List<string> _recentWarnings = new List<string>();
        private static readonly HashSet<string> _warnedOnce = new HashSet<string>();

        public const int MaxWarnings = 40;

        public static void Info(string msg)
        {
            if (Log != null) Log.LogInfo(msg);
        }

        public static void Warn(string where, Exception e)
        {
            var line = $"{where}: {e.GetType().Name}: {e.Message}";
            if (Log != null) Log.LogWarning(line);
            lock (_recentWarnings)
            {
                _recentWarnings.Insert(0, line);
                if (_recentWarnings.Count > MaxWarnings)
                    _recentWarnings.RemoveAt(_recentWarnings.Count - 1);
            }
        }

        /// Одинаковые ошибки не должны засыпать лог и вкладку Info.
        public static void WarnOnce(string where, Exception e)
        {
            // ключ включает сообщение: разные причины одного типа не схлопываются
            var key = where + "|" + e.GetType().Name + "|" + (e.Message ?? "").GetHashCode();
            if (_warnedOnce.Add(key)) Warn(where, e);
        }

        public static List<string> RecentWarnings()
        {
            lock (_recentWarnings) return new List<string>(_recentWarnings);
        }

        public static LocalUser GetLocalUser()
        {
            try { return LocalUserManager.GetFirstLocalUser(); }
            catch (Exception e) { WarnOnce("LocalUserManager.GetFirstLocalUser", e); return null; }
        }

        public static UserProfile GetProfile()
        {
            var lu = GetLocalUser();
            return lu != null ? lu.userProfile : null;
        }

        public static CharacterBody GetBody()
        {
            try
            {
                var lu = GetLocalUser();
                if (lu == null) return null;
                var nu = lu.currentNetworkUser;
                if (nu != null)
                {
                    var b = nu.GetCurrentBody();
                    if (b != null) return b;
                }
                return lu.cachedBody;
            }
            catch (Exception e) { WarnOnce("GetBody", e); return null; }
        }

        public static CharacterMaster GetMaster()
        {
            try
            {
                var body = GetBody();
                if (body != null)
                {
                    var m = body.master;
                    if (m != null) return m;
                }
                var lu = GetLocalUser();
                return lu != null ? lu.cachedMaster : null;
            }
            catch (Exception e) { WarnOnce("GetMaster", e); return null; }
        }

        public static bool IsServer => NetworkServer.active;

        public static bool InRun => Run.instance != null;

        public static string TeamName(TeamIndex t)
        {
            switch (t)
            {
                case TeamIndex.Player: return "Player";
                case TeamIndex.Monster: return "Monster";
                case TeamIndex.Neutral: return "Neutral";
                case TeamIndex.Lunar: return "Lunar";
                case TeamIndex.Void: return "Void";
                default: return t.ToString();
            }
        }

        /// Локализация токена; если токена нет — возвращаем его как есть.
        public static string Localize(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            try
            {
                var s = Language.GetString(token);
                return string.IsNullOrEmpty(s) ? token : s;
            }
            catch (Exception e) { WarnOnce("Language.GetString", e); return token; }
        }

        // Формат сообщений - как в остальных плагинах автора: префикс из двух
        // цветных букв, разделитель, одна строка сути. Цвета сути: успех зелёный,
        // ошибка красная, значение жёлтое, остальное светлое.
        public const string Prefix =
            "<color=#6EC1FF><b>S</b></color><color=#3D8BFD><b>M</b></color> <color=#9FB2CC>»</color> ";

        private const string ColBody = "<color=#E8EEF7>";
        private const string ColOk = "<color=#7EE787>";
        private const string ColErr = "<color=#FF7B72>";
        private const string ColVal = "<color=#FFD866>";
        private const string ColEnd = "</color>";

        public static void Say(string msg)
        {
            Send(ColBody + msg + ColEnd);
        }

        public static void SayOk(string msg)
        {
            Send(ColOk + msg + ColEnd);
        }

        public static void SayErr(string msg)
        {
            Send(ColErr + msg + ColEnd);
        }

        /// <summary>Подсветить значение внутри строки: Say("alive " + Val("3")).</summary>
        public static string Val(object value)
        {
            return ColVal + value + ColEnd;
        }

        private static void Send(string coloredBody)
        {
            if (SandboxMenuPlugin.ShowChatMessages != null && !SandboxMenuPlugin.ShowChatMessages.Value)
                return;
            try { Chat.AddMessage(Prefix + coloredBody); }
            catch (Exception e) { WarnOnce("Chat.AddMessage", e); }
        }

        /// Точка появления рядом с игроком: чуть впереди и выше, чтобы не
        /// заспавнить объект внутри геометрии или внутри самого игрока.
        public static Vector3 SpawnPointNearPlayer(float distance = 3f, float up = 1f)
        {
            var body = GetBody();
            if (body == null) return Vector3.zero;
            var t = body.coreTransform;
            var origin = t != null ? t.position : body.footPosition;
            var fwd = t != null ? t.forward : Vector3.forward;
            return origin + fwd.normalized * distance + Vector3.up * up;
        }

        /// Несколько точек по кругу вокруг игрока — для спавна группы.
        public static Vector3 SpawnPointOnRing(int index, int total, float radius = 4f)
        {
            var body = GetBody();
            if (body == null) return Vector3.zero;
            var t = body.coreTransform;
            var origin = t != null ? t.position : body.footPosition;
            if (total <= 1) return origin + Vector3.up;
            float angle = 360f * index / total;
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            return origin + dir * radius + Vector3.up * 0.5f;
        }
    }
}

