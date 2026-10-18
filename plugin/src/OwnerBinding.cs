using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Привязка призванного существа к конкретному игроку.
    ///
    /// Без этого в кооперативе миньон на TeamIndex.Player получается «общим»:
    /// команда у всех игроков одна, и охрана бегает за всеми подряд. Привязка
    /// делается тремя штатными механизмами игры:
    ///
    ///   1. MinionOwnership.SetOwner(ownerMaster)  - публичный метод, ровно тот,
    ///      который MasterSummon.Perform вызывает сам, когда выставлен
    ///      propogateMinionOwnership (видно в IL: IL_01ae -> IL_01e8). Мы
    ///      вызываем его явно, чтобы не полагаться на то, что summonerBodyObject
    ///      всегда указывает на нужного игрока.
    ///
    ///   2. BaseAI.leader + copyLeaderTarget - у BaseAI геттер leader публичный,
    ///      а у BaseAI.Target публичный сеттер gameObject. Сама игра так и
    ///      делает: в DoppelgangerSpawnCard она пишет ai.currentEnemy.gameObject.
    ///      Сеттер самого leader приватный, поэтому если Target ещё не создан -
    ///      создаём и проставляем через Harmony Traverse.
    ///
    ///   3. master.minionOwnership проверяется повторно, пока тело не появится:
    ///      Perform() возвращает master сразу, а CharacterBody может родиться
    ///      на кадр позже, поэтому привязка AI ставится в очередь и доделывается
    ///      в OwnerBinding.Tick().
    ///
    /// Компонент Deployable намеренно НЕ добавляется: это NetworkBehaviour-подобная
    /// логика слотов, и навешивать её после NetworkServer.Spawn - способ получить
    /// рассинхрон. Владельца игре достаточно через MinionOwnership.
    /// </summary>
    internal static class OwnerBinding
    {
        private struct Pending
        {
            public CharacterMaster Minion;
            public CharacterMaster Owner;
            public CharacterBody OwnerBody;
            public float Deadline;
        }

        private static readonly List<Pending> _queue = new List<Pending>();
        private const float RetrySeconds = 6f;

        public static int PendingCount { get { return _queue.Count; } }

        /// <summary>Привязать миньона к владельцу. Безопасно при null.</summary>
        public static void Bind(CharacterMaster minion, CharacterMaster owner, CharacterBody ownerBody)
        {
            if (minion == null) return;
            if (owner == null) return;

            SetMinionOwner(minion, owner);

            // тело может ещё не существовать - пробуем сейчас и оставляем в очереди
            if (!TryBindAi(minion, ownerBody))
            {
                _queue.Add(new Pending
                {
                    Minion = minion,
                    Owner = owner,
                    OwnerBody = ownerBody,
                    Deadline = Time.unscaledTime + RetrySeconds
                });
            }
        }

        /// Вызывается из Plugin.Update. Доделывает привязку AI, когда тело появилось.
        public static void Tick()
        {
            if (_queue.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                var p = _queue[i];
                bool dead = p.Minion == null || p.Owner == null || now > p.Deadline;
                if (dead)
                {
                    _queue.RemoveAt(i);
                    continue;
                }
                // владельца могло сменить (смерть и респаун) - перепроверяем
                SetMinionOwner(p.Minion, p.Owner);
                if (TryBindAi(p.Minion, p.OwnerBody)) _queue.RemoveAt(i);
            }
        }

        /// <summary>
        /// Держит follow-поведение живого клона в актуальном состоянии.
        ///
        /// Тело владельца - объект одноразовый: смерть, респаун и переход между
        /// стадиями создают новый GameObject, а старый лидер AI указывает в
        /// пустоту, из-за чего клон «зависал» на месте. Поэтому лидер сверяется
        /// с текущим телом владельца и переставляется при несовпадении.
        /// follow = false снимает лидера: клон остаётся на месте охраной.
        /// </summary>
        public static void EnsureLeader(CharacterMaster minion, CharacterBody ownerBody, bool follow)
        {
            if (minion == null || minion.gameObject == null) return;

            CharacterBody body;
            try { body = minion.GetBody(); }
            catch (Exception e) { GameApi.WarnOnce("CharacterMaster.GetBody", e); return; }
            if (body == null || body.gameObject == null) return;

            BaseAI ai;
            try { ai = body.GetComponent<BaseAI>(); }
            catch (Exception e) { GameApi.WarnOnce("GetComponent<BaseAI>", e); return; }
            if (ai == null) return;

            try
            {
                var leader = ai.leader;

                if (!follow)
                {
                    if (leader != null && leader.gameObject != null) leader.gameObject = null;
                    ai.copyLeaderTarget = false;
                    return;
                }

                if (ownerBody == null || ownerBody.gameObject == null) return;

                if (leader == null)
                {
                    leader = new BaseAI.Target(ai);
                    if (!SetLeaderPrivate(ai, leader)) return;
                }

                if (!ReferenceEquals(leader.gameObject, ownerBody.gameObject))
                {
                    leader.gameObject = ownerBody.gameObject;
                }
                ai.copyLeaderTarget = true;
            }
            catch (Exception e) { GameApi.WarnOnce("BaseAI.EnsureLeader", e); }
        }

        public static void Clear()
        {
            _queue.Clear();
        }

        private static void SetMinionOwner(CharacterMaster minion, CharacterMaster owner)
        {
            try
            {
                var mo = minion.minionOwnership;
                if (mo == null) return;
                if (mo.ownerMaster == owner) return;
                mo.SetOwner(owner);
            }
            catch (Exception e) { GameApi.WarnOnce("MinionOwnership.SetOwner", e); }
        }

        /// <summary>true, если привязка AI выполнена (тело найдено и leader выставлен).</summary>
        private static bool TryBindAi(CharacterMaster minion, CharacterBody ownerBody)
        {
            if (ownerBody == null) return false;
            CharacterBody body;
            try { body = minion.GetBody(); }
            catch (Exception e) { GameApi.WarnOnce("CharacterMaster.GetBody", e); return false; }
            if (body == null) return false;

            BaseAI ai;
            try { ai = body.GetComponent<BaseAI>(); }
            catch (Exception e) { GameApi.WarnOnce("GetComponent<BaseAI>", e); return false; }
            if (ai == null) return true;   // у тела нет AI - привязывать нечего, это успех

            try
            {
                var leader = ai.leader;
                if (leader == null)
                {
                    // Target создаётся публичным конструктором Target(BaseAI),
                    // а вот сеттер leader приватный - ставим через Traverse
                    leader = new BaseAI.Target(ai);
                    if (!SetLeaderPrivate(ai, leader)) return false;
                }
                leader.gameObject = ownerBody.gameObject;

                // копировать цель лидера: охрана бьёт того же, кого бьёт владелец
                ai.copyLeaderTarget = true;
                return true;
            }
            catch (Exception e) { GameApi.WarnOnce("BaseAI.leader", e); return false; }
        }

        private static bool SetLeaderPrivate(BaseAI ai, BaseAI.Target target)
        {
            try
            {
                var t = Traverse.Create(ai).Property("leader");
                if (t != null && t.PropertyExists())
                {
                    t.SetValue(target);
                    if (ai.leader != null) return true;
                }
            }
            catch (Exception e) { GameApi.Warn("Traverse(leader)", e); }

            try
            {
                var f = Traverse.Create(ai).Field("<leader>k__BackingField");
                if (f != null && f.FieldExists())
                {
                    f.SetValue(target);
                    return ai.leader != null;
                }
            }
            catch (Exception e) { GameApi.Warn("Traverse(<leader>k__BackingField)", e); }

            return false;
        }
    }
}

