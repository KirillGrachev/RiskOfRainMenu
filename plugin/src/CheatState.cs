using System;
using System.Collections.Generic;

namespace SandboxMenu {

    /// <summary>
    /// Идентификаторы переключаемых читов (байт, стабильны между версиями).
    /// Первые три - на игрока (netId), GodModeAll - глобальный.
    /// </summary>
    public enum CheatFlag : byte {

        GodMode = 1,
        OneHitKill = 2,
        NoCooldown = 3,
        GodModeAll = 4

    }

    /// <summary>
    /// Идентификаторы числовых читов-множителей (проценты, 100 = без изменений).
    /// </summary>
    public enum CheatValue : byte {

        DamagePct = 1,
        AttackSpeedPct = 2,
        MoveSpeedPct = 3

    }

    /// <summary>
    /// Состояние читов: какие флаги и множители включены и для кого.
    ///
    /// Чистый класс без Unity и RoR2 (как <see cref="Protocol"/>): ключи -
    /// netId игроков (uint), поэтому состояние тестируется и сериализуется
    /// без игровых сборок, а исполняющий код резолвит netId в NetworkUser
    /// сам. Правда всегда на сервере: клиент держит здесь же оптимистичное
    /// зеркало своих нажатий (тот же приём, что CloneManager.KeepAlive),
    /// а исполняются флаги только серверными хуками и тиками.
    ///
    /// Жизненный цикл: полный сброс на старте забега (флаги бессмысленны
    /// между забегами - тела и netId пересоздаются).
    /// </summary>
    internal static class CheatState {

        /// <summary>Нижняя граница множителей, проценты.</summary>
        public const int MinPct = 10;

        /// <summary>Верхняя граница множителей, проценты.</summary>
        public const int MaxPct = 1000;

        /// <summary>Множитель по умолчанию (без изменений), проценты.</summary>
        public const int DefaultPct = 100;

        /// <summary>Предел стаков бафа за одно действие.</summary>
        public const int MaxBuffStacks = 50;

        /// <summary>Предел денег/лунных монет за одно действие.</summary>
        public const int MaxMoney = 1000000;

        /// <summary>Предел уровня команды.</summary>
        public const int MaxLevel = 200;

        /// <summary>Предел выдаваемого опыта за одно действие.</summary>
        public const int MaxExperience = 100000000;

        /// <summary>Предел радиуса «убить врагов» (0 = вся карта).</summary>
        public const int MaxKillRadius = 2000;

        /// <summary>Предел установки таймера забега, секунд.</summary>
        public const int MaxTimerSeconds = 86400;

        private static readonly Dictionary<uint, HashSet<CheatFlag>> _flags =
                new Dictionary<uint, HashSet<CheatFlag>>();

        private static readonly Dictionary<uint, Dictionary<CheatValue, int>> _values =
                new Dictionary<uint, Dictionary<CheatValue, int>>();

        /// <summary>Глобальное бессмертие всех игроков (флаг GodModeAll).</summary>
        public static bool GodModeAll;

        /// <summary>Заморозка таймера забега (зеркало серверного состояния).</summary>
        public static bool FreezeTimer;

        /// <summary>Кламп процентов множителя в рабочие границы.</summary>
        public static int ClampPct(int raw) {

            if (raw < MinPct) {
                return MinPct;
            }

            return raw > MaxPct ? MaxPct : raw;

        }

        /// <summary>Кламп стаков бафа: 1..<see cref="MaxBuffStacks"/>.</summary>
        public static int ClampStacks(int raw) {

            if (raw < 1) {
                return 1;
            }

            return raw > MaxBuffStacks ? MaxBuffStacks : raw;

        }

        /// <summary>Значение id коррежно (защита от мусора из сети).</summary>
        public static bool IsKnownFlag(int raw) {

            return Enum.IsDefined(typeof(CheatFlag), (byte)raw);

        }

        /// <summary>Значение id корректно (защита от мусора из сети).</summary>
        public static bool IsKnownValue(int raw) {

            return Enum.IsDefined(typeof(CheatValue), (byte)raw);

        }

        /// <summary>Включён ли флаг у игрока. GodModeAll дублирует GodMode всем.</summary>
        public static bool HasFlag(uint netId, CheatFlag flag) {

            if (flag == CheatFlag.GodMode && GodModeAll) {
                return true;
            }

            HashSet<CheatFlag> set;

            return netId != 0u && _flags.TryGetValue(netId, out set) && set.Contains(flag);

        }

        /// <summary>Включить/выключить флаг игрока (или глобальный GodModeAll).</summary>
        public static void SetFlag(uint netId, CheatFlag flag, bool on) {

            if (flag == CheatFlag.GodModeAll) {

                GodModeAll = on;
                return;

            }

            if (netId == 0u) {
                return;
            }

            HashSet<CheatFlag> set;

            if (!_flags.TryGetValue(netId, out set)) {

                set = new HashSet<CheatFlag>();
                _flags[netId] = set;

            }

            if (on) {
                set.Add(flag);
            } else {
                set.Remove(flag);
            }

        }

        /// <summary>Текущий множитель игрока в процентах (по умолчанию 100).</summary>
        public static int ValueOf(uint netId, CheatValue value) {

            Dictionary<CheatValue, int> map;

            if (netId != 0u && _values.TryGetValue(netId, out map)) {

                int pct;

                if (map.TryGetValue(value, out pct)) {
                    return pct;
                }

            }

            return DefaultPct;

        }

        /// <summary>Установить множитель игрока; значение по умолчанию убирает запись.</summary>
        public static void SetValue(uint netId, CheatValue value, int pct) {

            if (netId == 0u) {
                return;
            }

            pct = ClampPct(pct);

            Dictionary<CheatValue, int> map;

            if (!_values.TryGetValue(netId, out map)) {

                map = new Dictionary<CheatValue, int>();
                _values[netId] = map;

            }

            if (pct == DefaultPct) {
                map.Remove(value);
            } else {
                map[value] = pct;
            }

            RecountSpeedTuning();

        }

        /// <summary>Сколько игроков несут хотя бы один флаг (для вкладки «Инфо»).</summary>
        public static int FlaggedPlayerCount {

            get {

                int count = 0;

                foreach (var pair in _flags) {

                    if (pair.Value.Count > 0) {
                        count++;
                    }

                }

                return count;

            }

        }

        /// <summary>Сколько игроков несут множитель, отличный от 100%.</summary>
        public static int TunedPlayerCount {

            get {

                int count = 0;

                foreach (var pair in _values) {

                    if (pair.Value.Count > 0) {
                        count++;
                    }

                }

                return count;

            }

        }

        private static int _speedTuningCount;

        /// <summary>
        /// Есть ли хоть у кого-то множитель скорости (атаки или движения).
        /// Флаг читается Harmony-postfix'ами геттеров CharacterBody каждый вызов:
        /// без активных множителей патч выходит сразу, без резолва владельца.
        /// </summary>
        public static bool AnySpeedTuning {

            get {
                return _speedTuningCount > 0;
            }

        }

        private static void RecountSpeedTuning() {

            int count = 0;

            foreach (var map in _values.Values) {

                foreach (var pair in map) {

                    if (pair.Key == CheatValue.AttackSpeedPct || pair.Key == CheatValue.MoveSpeedPct) {
                        count++;
                    }

                }

            }

            _speedTuningCount = count;

        }

        /// <summary>Забыть игрока (отключился): не копим мёртвые записи.</summary>
        public static void Forget(uint netId) {

            _flags.Remove(netId);
            _values.Remove(netId);
            RecountSpeedTuning();

        }

        /// <summary>Полный сброс: новый забег - старые netId и флаги недействительны.</summary>
        public static void ResetRun() {

            _flags.Clear();
            _values.Clear();
            GodModeAll = false;
            FreezeTimer = false;
            RecountSpeedTuning();

        }

    }

}
