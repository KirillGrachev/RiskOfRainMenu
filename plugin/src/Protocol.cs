using System.Collections.Generic;

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
    /// Чистая (без Unity и RoR2) логика сетевого протокола: версия, клампы
    /// параметров из сети и rate-limit. Вынесено из NetProtocol, чтобы гонять
    /// тестами без игровых сборок и чтобы серверные ограничения применялись
    /// в одном месте, а не по углам диспетчера.
    /// </summary>
    public static class Protocol
    {
        /// <summary>
        /// Версия формата SandboxRequest. Любое изменение состава полей обязано
        /// поднять её: сервер отклоняет запросы чужой версии с ack-ошибкой,
        /// вместо того чтобы молча распарсить мусор.
        /// </summary>
        public const byte ProtoVersion = 1;

        /// <summary>Предел длины строковых полей запроса (имя префаба/карты).</summary>
        public const int MaxNameLength = 128;

        /// <summary>
        /// Серверный кламп счётчиков: UI клампает сам, сырой запрос из сети -
        /// нет, поэтому границы применяются здесь для каждого действия.
        /// Возвращает значение, безопасное для исполнения.
        /// </summary>
        public static int ClampCount(byte action, int raw)
        {
            int max;
            switch (action)
            {
                case ActionCode.GiveItem:
                case ActionCode.RemoveItem:
                    max = 500;
                    break;
                case ActionCode.DropPickup:
                    max = 100;
                    break;
                case ActionCode.SpawnMaster:
                case ActionCode.SpawnProp:
                    max = 50;
                    break;
                case ActionCode.CloneSpawnNow:
                    max = 20;
                    break;
                default:
                    return raw;
            }
            if (raw < 1) return 1;
            return raw > max ? max : raw;
        }

        /// <summary>Обрезать строковое поле запроса до <see cref="MaxNameLength"/>.</summary>
        public static string ClampName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length <= MaxNameLength) return name;
            return name.Substring(0, MaxNameLength);
        }
    }

    /// <summary>
    /// Скользящее окно rate-limit на одного отправителя: не больше
    /// <see cref="MaxPerWindow"/> запросов за <see cref="WindowSeconds"/> секунд.
    /// Чистый класс с внешними часами - тестируется без Unity.
    /// </summary>
    public class RateLimiter
    {
        /// <summary>Длина окна, секунд.</summary>
        public const float WindowSeconds = 5f;

        /// <summary>Предел запросов в окне на одного отправителя.</summary>
        public const int MaxPerWindow = 25;

        private class Bucket
        {
            public float WindowStart;
            public int Count;
        }

        private readonly Dictionary<int, Bucket> _buckets = new Dictionary<int, Bucket>();

        /// <summary>Разрешить запрос от key в момент now; иначе отказать.</summary>
        public bool TryConsume(int key, float now)
        {
            Bucket b;
            if (!_buckets.TryGetValue(key, out b))
            {
                _buckets[key] = new Bucket { WindowStart = now, Count = 1 };
                return true;
            }
            if (now - b.WindowStart >= WindowSeconds)
            {
                b.WindowStart = now;
                b.Count = 1;
                return true;
            }
            if (b.Count >= MaxPerWindow) return false;
            b.Count++;
            return true;
        }

        /// <summary>Забыть отправителя (отключился): не копим мёртвые бакеты.</summary>
        public void Forget(int key)
        {
            _buckets.Remove(key);
        }

        /// <summary>Полный сброс (смена сессии).</summary>
        public void Reset()
        {
            _buckets.Clear();
        }
    }
}

