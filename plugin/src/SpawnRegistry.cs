using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Учёт того, что меню породило вручную, чтобы у каждой функции выдачи была
    /// зеркальная функция удаления.
    ///
    /// Реестр хранит только слабые по сути ссылки (Unity-объекты, которые игра
    /// может уничтожить сама): перед операцией список prune'ится от null,
    /// поэтому мёртвые записи не накапливаются и не мешают.
    /// </summary>
    internal static class SpawnRegistry
    {
        private class Tracked
        {
            public CharacterMaster Master;
            public GameObject BodyGo;
        }

        /// <summary>Капля дропа меню и пикап, в который она приземлилась.</summary>
        private class DropEntry
        {
            public GameObject Droplet;
            public GameObject Pickup;
            public Vector3 LastPos;
            public float CreatedAt;
            public float LandAt = -1f;
        }

        private static readonly List<DropEntry> _drops = new List<DropEntry>();
        private static int _dropArm;

        private static readonly List<Tracked> _masters = new List<Tracked>();
        private static readonly List<GameObject> _props = new List<GameObject>();

        /// <summary>Сколько персонажей заспавнено меню и ещё живы.</summary>
        public static int MasterCount { get { Prune(); return _masters.Count; } }

        /// <summary>Сколько объектов сцены заспавнено меню и ещё живы.</summary>
        public static int PropCount { get { Prune(); return _props.Count; } }

        /// <summary>
        /// Зарегистрировать призванного персонажа; тело запоминаем сразу:
        /// к моменту удаления master.GetBody() может уже не разрешиться.
        /// </summary>
        public static void Track(CharacterMaster master)
        {
            if (master == null) return;
            GameObject bodyGo = null;
            try { var b = master.GetBody(); if (b != null) bodyGo = b.gameObject; }
            catch (Exception e) { GameApi.WarnOnce("Track.GetBody", e); }
            _masters.Add(new Tracked { Master = master, BodyGo = bodyGo });
        }

        /// <summary>Взвести ожидание N капель дропа меню (вызывает Spawning).</summary>
        public static void ArmDrops(int n)
        {
            _dropArm = n;
        }

        /// <summary>Снять ожидание капель (окно учёта закрывается).</summary>
        public static void DisarmDrops()
        {
            _dropArm = 0;
        }

        /// <summary>Взята ли одна единица ожидания; true - капля наша.</summary>
        public static bool ConsumeDropArm()
        {
            if (_dropArm <= 0) return false;
            _dropArm--;
            return true;
        }

        /// <summary>Капля меню: запомнить объект и позицию рождения.</summary>
        public static void TrackDroplet(GameObject go)
        {
            if (go == null) return;
            _drops.Add(new DropEntry
            {
                Droplet = go,
                LastPos = go.transform.position,
                CreatedAt = Time.unscaledTime
            });
        }

        /// <summary>
        /// Родился пикап: усыновляем его ближайшей записью, капля которой ещё
        /// в воздухе или села не раньше 0.25 с назад и не дальше 6 метров.
        /// </summary>
        public static void AdoptPickup(GameObject go)
        {
            if (go == null || _drops.Count == 0) return;
            float now = Time.unscaledTime;
            var pos = go.transform.position;
            foreach (var e in _drops)
            {
                if (e.Pickup != null) continue;
                if (now - e.CreatedAt < 0.25f) continue;
                var anchor = e.Droplet != null ? e.Droplet.transform.position : e.LastPos;
                if ((pos - anchor).sqrMagnitude > 36f) continue;
                e.Pickup = go;
                return;
            }
        }

        /// <summary>Вызывается из Plugin.Update раз в 0.5 с: вести записи дропа.</summary>
        public static void SweepDrops()
        {
            if (_drops.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var e = _drops[i];
                if (e.Droplet != null)
                {
                    e.LastPos = e.Droplet.transform.position;
                    continue;
                }
                if (e.Pickup != null) continue;          // приземлился и живёт
                if (e.LandAt < 0f) { e.LandAt = now; continue; }
                if (now - e.LandAt > 3f) _drops.RemoveAt(i);   // капля исчезла без пикапа
            }
        }

        /// <summary>Сколько записей дропа меню живы (капля или пикап).</summary>
        public static int MenuDropCount
        {
            get
            {
                int n = 0;
                foreach (var e in _drops)
                {
                    if (e.Droplet != null || e.Pickup != null) n++;
                }
                return n;
            }
        }

        /// <summary>Убрать дроп, созданный меню: капли и приземлившиеся пикапы.</summary>
        public static int RemoveMenuDrops()
        {
            int n = 0;
            foreach (var e in _drops)
            {
                if (e.Droplet != null) { Spawning.NetDestroy(e.Droplet); n++; }
                if (e.Pickup != null) { Spawning.NetDestroy(e.Pickup); n++; }
            }
            _drops.Clear();
            return n;
        }

        /// <summary>
        /// Убрать «лишний» дроп рядом с целью: капли и пикапы в радиусе.
        /// Режим по умолчанию: чистит поле вокруг игрока, не трогая дальний дроп.
        /// </summary>
        public static int RemoveDropsNear(Vector3 origin, float radius)
        {
            if (!NetworkServer.active) return 0;
            int n = 0;
            float sq = radius * radius;
            try
            {
                foreach (var d in UnityEngine.Object.FindObjectsOfType<PickupDropletController>())
                {
                    if (d == null || d.gameObject == null) continue;
                    if ((d.transform.position - origin).sqrMagnitude > sq) continue;
                    Spawning.NetDestroy(d.gameObject);
                    n++;
                }
            }
            catch (Exception e) { GameApi.Warn("RemoveDropsNear(droplets)", e); }
            try
            {
                foreach (var pk in UnityEngine.Object.FindObjectsOfType<GenericPickupController>())
                {
                    if (pk == null || pk.gameObject == null) continue;
                    if ((pk.transform.position - origin).sqrMagnitude > sq) continue;
                    Spawning.NetDestroy(pk.gameObject);
                    n++;
                }
            }
            catch (Exception e) { GameApi.Warn("RemoveDropsNear(pickups)", e); }
            return n;
        }

        /// <summary>Зарегистрировать заспавненный объект сцены.</summary>
        public static void Track(GameObject go)
        {
            if (go != null) _props.Add(go);
        }

        /// <summary>
        /// Убрать всех призванных меню персонажей ВМЕСТЕ с их миньонами:
        /// уничтожение одного master'а оставляло свиту (миньонов с
        /// MinionOwnership.ownerMaster от призванного) живой в ростере команды.
        /// Сбор идёт волнами: миньоны миньонов тоже снимаются.
        /// Возвращает число уничтоженных голов (без учёта свиты).
        /// </summary>
        public static int RemoveMasters()
        {
            Prune();
            var toRemove = new List<CharacterMaster>();
            foreach (var t in _masters)
            {
                if (t.Master != null) CollectWithMinions(t.Master, toRemove);
            }
            int n = _masters.Count;
            foreach (var m in toRemove) Spawning.DestroyCharacter(m);
            foreach (var t in _masters)
            {
                if (t.BodyGo != null) Spawning.NetDestroy(t.BodyGo);
            }
            _masters.Clear();
            Spawning.CleanupOrphanBodies();
            return n;
        }

        /// <summary>Добавить master'а и всю его свиту (транзитивно) в список.</summary>
        private static void CollectWithMinions(CharacterMaster owner, List<CharacterMaster> into)
        {
            // один сняпок сцены на весь обход: instance-список игры не аллоцирует
            // новый массив каждый проход, в отличие от FindObjectsOfType
            var all = new List<CharacterMaster>(CharacterMaster.readOnlyInstancesList);
            var frontier = new List<CharacterMaster> { owner };
            while (frontier.Count > 0)
            {
                var next = new List<CharacterMaster>();
                foreach (var o in frontier)
                {
                    if (o == null || into.Contains(o)) continue;
                    into.Add(o);
                    foreach (var m in all)
                    {
                        if (m == null || into.Contains(m) || next.Contains(m)) continue;
                        var mo = m.minionOwnership;
                        if (mo != null && ReferenceEquals(mo.ownerMaster, o)) next.Add(m);
                    }
                }
                frontier = next;
            }
        }

        /// <summary>
        /// Снять все дебафы с тел команды игрока: призывные союзники и их свита
        /// успевали навесить дебафы до удаления, а после уничтожения моделей те
        /// оставались висеть на игроке. Вызывается после массовых удалений.
        /// </summary>
        public static int CleanseDebuffs()
        {
            if (!NetworkServer.active) return 0;
            int removed = 0;
            try
            {
                foreach (var body in CharacterBody.readOnlyInstancesList)
                {
                    if (body == null || body.teamComponent == null) continue;
                    if (body.teamComponent.teamIndex != TeamIndex.Player) continue;
                    for (int i = 1; i < BuffCatalog.buffCount; i++)
                    {
                        var idx = (BuffIndex)i;
                        BuffDef def;
                        try { def = BuffCatalog.GetBuffDef(idx); }
                        catch (Exception e) { GameApi.WarnOnce("BuffCatalog.GetBuffDef", e); continue; }
                        if (def == null || !def.isDebuff) continue;
                        int stacks = body.GetBuffCount(idx);
                        for (int k = 0; k < stacks; k++) body.RemoveBuff(idx);
                        removed += stacks;
                    }
                }
            }
            catch (Exception e) { GameApi.Warn("CleanseDebuffs", e); }
            return removed;
        }

        /// <summary>Убрать все заспавненные меню объекты сцены. Возвращает число.</summary>
        public static int RemoveProps()
        {
            Prune();
            int n = _props.Count;
            foreach (var go in _props) Spawning.NetDestroy(go);
            _props.Clear();
            return n;
        }

        /// <summary>Полный сброс реестра (без уничтожения объектов).</summary>
        public static void Reset()
        {
            _masters.Clear();
            _props.Clear();
            _drops.Clear();
            _dropArm = 0;
        }

        /// <summary>Вычистить записи об уничтоженных игрой объектах.</summary>
        private static void Prune()
        {
            for (int i = _masters.Count - 1; i >= 0; i--)
            {
                var t = _masters[i];
                bool masterGone = t.Master == null || t.Master.gameObject == null;
                bool bodyGone = t.BodyGo == null;
                if (masterGone && bodyGone) _masters.RemoveAt(i);
                else if (masterGone) { Spawning.NetDestroy(t.BodyGo); _masters.RemoveAt(i); }
            }
            for (int i = _props.Count - 1; i >= 0; i--)
            {
                if (_props[i] == null) _props.RemoveAt(i);
            }
        }
    }
}

