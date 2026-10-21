using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Подсветка объектов (ESP): маркеры телепорта, сундуков, алтарей и боссов
    /// поверх экрана с подписью и дистанцией.
    ///
    /// Оверлей чисто клиентский и ничего не меняет в мире, поэтому работает
    /// даже когда у хоста нет мода (как <see cref="FlyController"/>). Список
    /// пересобирается 4 раза в секунду: обход InstanceTracker дешёвый, но не
    /// бесплатный, а подписи с дистанцией готовятся там же - в кадре остаётся
    /// только отрисовка кэша.
    ///
    /// Маркеры рисуются ДО окна меню (Plugin.OnGUI), поэтому открытое меню
    /// перекрывает их: прозрачных слоёв IMGUI не имеет, а читать карту сквозь
    /// панель всё равно невозможно.
    /// </summary>
    internal static class EspOverlay {

        /// <summary>Включён ли оверлей (переключается вкладкой «Читы»).</summary>
        public static bool Enabled;

        private const float RefreshInterval = 0.25f;
        private const int MaxMarkers = 48;
        private const float MaxDistance = 400f;

        private class Marker {

            public Vector3 Position;
            public string Label;
            public Color Color;
            public float Distance;

        }

        private static readonly List<Marker> Cache = new List<Marker>();
        private static float _nextRefresh;

        private static readonly Color TeleporterColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color ChestColor = new Color(1f, 0.84f, 0.35f);
        private static readonly Color ShrineColor = new Color(0.78f, 0.45f, 1f);
        private static readonly Color BossColor = new Color(1f, 0.32f, 0.28f);

        /// <summary>Отрисовка оверлея; вызывается из OnGUI плагина до окна меню.</summary>
        public static void Draw() {

            if (!Enabled) {
                return;
            }

            try {

                Theme.Ensure();

                float now = Time.unscaledTime;

                if (now >= _nextRefresh) {

                    _nextRefresh = now + RefreshInterval;
                    Refresh();

                }

                if (Cache.Count == 0) {
                    return;
                }

                var camera = Camera.main;

                if (camera == null) {
                    return;
                }

                float height = Screen.height;
                var tint = GUI.color;

                foreach (var marker in Cache) {

                    Vector3 screen = camera.WorldToScreenPoint(marker.Position);

                    // за спиной камеры WorldToScreenPoint даёт отрицательный z
                    if (screen.z <= 0.1f) {
                        continue;
                    }

                    float x = screen.x;
                    float y = height - screen.y;

                    if (x < -60f || x > Screen.width + 60f || y < -20f || y > height + 20f) {
                        continue;
                    }

                    GUI.color = marker.Color;
                    GUI.Box(new Rect(x - 4f, y - 4f, 8f, 8f), GUIContent.none, Theme.EspBox);
                    GUI.Label(new Rect(x + 8f, y - 9f, 220f, 18f), marker.Label, Theme.Small);

                }

                GUI.color = tint;

            } catch (Exception e) {
                GameApi.WarnOnce("EspOverlay.Draw", e);
            }

        }

        private static void Refresh() {

            Cache.Clear();

            var body = GameApi.GetBody();
            var camera = Camera.main;

            if (body == null && camera == null) {
                return;
            }

            Vector3 origin = body != null
                    ? body.footPosition
                    : camera.transform.position;

            AddTeleporter(origin);
            AddPurchases(origin);
            AddBosses(origin);

            Cache.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            if (Cache.Count > MaxMarkers) {
                Cache.RemoveRange(MaxMarkers, Cache.Count - MaxMarkers);
            }

        }

        private static void AddTeleporter(Vector3 origin) {

            try {

                var teleporter = TeleporterInteraction.instance;

                if (teleporter == null || teleporter.transform == null) {
                    return;
                }

                Add(origin, teleporter.transform.position,
                        Texts.T("Телепорт", "Teleporter"), TeleporterColor);

            } catch (Exception e) {
                GameApi.WarnOnce("EspOverlay.AddTeleporter", e);
            }

        }

        private static void AddPurchases(Vector3 origin) {

            try {

                foreach (var purchase in InstanceTracker.GetInstancesList<PurchaseInteraction>()) {

                    if (purchase == null || purchase.transform == null) {
                        continue;
                    }

                    bool chest = purchase.GetComponent<ChestBehavior>() != null;
                    bool shrine = !chest && purchase.GetComponent<ShrineBehavior>() != null;

                    if (!chest && !shrine) {
                        continue;
                    }

                    Add(origin, purchase.transform.position,
                            chest ? Texts.T("Сундук", "Chest") : Texts.T("Алтарь", "Shrine"),
                            chest ? ChestColor : ShrineColor);

                }

            } catch (Exception e) {
                GameApi.WarnOnce("EspOverlay.AddPurchases", e);
            }

        }

        private static void AddBosses(Vector3 origin) {

            try {

                foreach (var boss in CharacterBody.readOnlyInstancesList) {

                    if (boss == null || !boss.isBoss) {
                        continue;
                    }

                    var health = boss.healthComponent;

                    if (health == null || !health.alive) {
                        continue;
                    }

                    var team = boss.teamComponent;

                    if (team == null || team.teamIndex == TeamIndex.Player) {
                        continue;
                    }

                    string name;

                    try {
                        name = boss.GetDisplayName();
                    } catch {
                        name = boss.name;
                    }

                    Add(origin, boss.footPosition, name, BossColor);

                }

            } catch (Exception e) {
                GameApi.WarnOnce("EspOverlay.AddBosses", e);
            }

        }

        private static void Add(Vector3 origin, Vector3 position, string name, Color color) {

            float distance = Vector3.Distance(origin, position);

            if (distance > MaxDistance) {
                return;
            }

            Cache.Add(new Marker {
                Position = position,
                Label = name + "  " + Mathf.RoundToInt(distance) + "m",
                Color = color,
                Distance = distance
            });

        }

    }

}
