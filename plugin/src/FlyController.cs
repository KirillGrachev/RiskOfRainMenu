using System;
using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Свободный полёт локального игрока: WASD относительно камеры,
    /// Space/Ctrl - вверх/вниз, Shift - ускорение x3.
    ///
    /// Перемещение идёт через KinematicCharacterMotor.SetPosition -
    /// телепортационное движение, которое не проверяет коллизии, поэтому
    /// полёт заодно работает как noclip. Гравитация гасится множителем
    /// (CharacterMotor.gravityMultiplier = 0, useGravity в этой сборке
    /// read-only), исходное значение запоминается и возвращается при
    /// выключении и в Shutdown плагина - игрок не должен остаться висеть
    /// с чужой гравитацией. Скорость мотора обнуляется каждый кадр через
    /// публичное поле BaseVelocity, иначе импульсы падения тащат тело.
    ///
    /// Намеренно локальный, без серверного запроса: в RoR2 авторитет
    /// движения своего тела у самого игрока, поэтому полёт работает и на
    /// клиенте, у которого хост без мода.
    /// </summary>
    internal static class FlyController {

        /// <summary>Включён ли полёт (переключается вкладкой «Читы»).</summary>
        public static bool Enabled;

        /// <summary>Множитель гравитации текущего мотора обнулён нами.</summary>
        private static bool _gravityZeroed;

        /// <summary>Исходный множитель гравитации (вернуть при выключении).</summary>
        private static float _savedGravityMultiplier = 1f;

        /// <summary>Вызывается из Plugin.Update каждый кадр.</summary>
        public static void Tick() {

            var body = GameApi.GetBody();
            var motor = body != null ? body.characterMotor : null;

            if (motor == null) {

                // тела нет (меню/загрузка): новый мотор родится со своей
                // гравитацией - флаг снимаем, при включённом полёте следующий
                // кадр обнулит её уже у нового мотора
                _gravityZeroed = false;
                return;

            }

            if (!Enabled) {

                if (_gravityZeroed) {

                    _gravityZeroed = false;

                    try {
                        motor.gravityMultiplier = _savedGravityMultiplier;
                    } catch (Exception e) {
                        GameApi.WarnOnce("FlyController.gravityRestore", e);
                    }

                }

                return;

            }

            try {

                if (!_gravityZeroed) {

                    _gravityZeroed = true;
                    _savedGravityMultiplier = motor.gravityMultiplier;
                    motor.gravityMultiplier = 0f;

                }

                float dt = Time.deltaTime;

                if (dt <= 0f) {
                    return;
                }

                var wish = ReadWishDirection(body);

                if (wish == Vector3.zero) {
                    return;
                }

                float speed = FlySpeed();

                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) {
                    speed *= 3f;
                }

                var kcm = motor.Motor;

                if (kcm != null) {

                    // обнуляем собственную скорость мотора: иначе импульсы
                    // падения продолжают тащить тело между кадрами полёта
                    kcm.BaseVelocity = Vector3.zero;
                    kcm.SetPosition(motor.transform.position + wish.normalized * (speed * dt), true);

                }

            } catch (Exception e) {
                GameApi.WarnOnce("FlyController.Tick", e);
            }

        }

        /// <summary>Направление из клавиш относительно камеры (Space - вверх, Ctrl - вниз).</summary>
        private static Vector3 ReadWishDirection(CharacterBody body) {

            var cam = Camera.main != null ? Camera.main.transform : body.coreTransform;

            if (cam == null) {
                return Vector3.zero;
            }

            var wish = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) wish += cam.forward;
            if (Input.GetKey(KeyCode.S)) wish -= cam.forward;
            if (Input.GetKey(KeyCode.D)) wish += cam.right;
            if (Input.GetKey(KeyCode.A)) wish -= cam.right;
            if (Input.GetKey(KeyCode.Space)) wish += Vector3.up;

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) {
                wish += Vector3.down;
            }

            return wish;

        }

        /// <summary>Скорость полёта из конфига (м/с), с рабочими границами.</summary>
        private static float FlySpeed() {

            var cfg = SandboxMenuPlugin.FlySpeed;
            return cfg != null ? Mathf.Clamp(cfg.Value, 1f, 200f) : 20f;

        }

        /// <summary>Вызов при выгрузке плагина: вернуть гравитацию и погасить флаг.</summary>
        public static void Shutdown() {

            Enabled = false;

            try {

                var body = GameApi.GetBody();
                var motor = body != null ? body.characterMotor : null;

                if (motor != null && _gravityZeroed) {
                    motor.gravityMultiplier = _savedGravityMultiplier;
                }

            } catch { /* best effort */ }

            _gravityZeroed = false;

        }

    }

}
