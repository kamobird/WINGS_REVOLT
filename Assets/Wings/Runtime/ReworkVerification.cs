using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Wings.AI;

namespace Wings
{
    public sealed partial class WingsGame
    {
        // 仮想入力を使い、手動操作と同じControl→Fire→ゲージ更新の経路で長押しを検証する。
        public bool VerificationInput { get; private set; }
        Mouse verificationMouse;
        Keyboard verificationKeyboard;

        IEnumerator RunReworkValidation()
        {
            verified.Clear();
            verificationMouse = InputSystem.AddDevice<Mouse>("WingsReworkMouse");
            verificationKeyboard = InputSystem.AddDevice<Keyboard>("WingsReworkKeyboard");
            var steps = ReworkSteps();
            string failure = null;
            while (true)
            {
                bool more;
                object current = null;
                try
                {
                    more = steps.MoveNext();
                    if (more)
                        current = steps.Current;
                }
                catch (Exception exception)
                {
                    failure = exception.ToString();
                    Debug.LogException(exception);
                    break;
                }
                if (!more)
                    break;
                yield return current;
            }
            VerificationInput = false;
            verificationAutopilot = false;
            Time.timeScale = 1;
            InputSystem.RemoveDevice(verificationMouse);
            InputSystem.RemoveDevice(verificationKeyboard);
            Directory.CreateDirectory(ReportDirectory());
            File.WriteAllText(Path.Combine(ReportDirectory(), "rework-report.json"),
                JsonUtility.ToJson(new VerificationReport { passed = failure == null, checks = verified.ToArray(), failure = failure }, true));
            Debug.Log(failure == null ? "WINGS REWORK VERIFICATION PASSED" : "WINGS REWORK VERIFICATION FAILED");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit(failure == null ? 0 : 1);
        }

        void ReworkMouse(bool held, Vector2? point = null)
        {
            InputSystem.QueueStateEvent(verificationMouse, new MouseState
            {
                position = point ?? new Vector2(Screen.width * .5f, Screen.height * .5f),
                buttons = held ? (ushort)1 : (ushort)0
            });
        }

        void StartReworkFixture(Bird bird, int stage = 0)
        {
            VerificationInput = true;
            verificationAutopilot = false;
            Selected.Clear();
            Selected.Add((int)bird);
            for (int index = 0; Selected.Count < 4; index++)
                if (index != (int)bird)
                    Selected.Add(index);
            Stage = stage;
            BeginMission(false);
            foreach (var pilot in Party)
                pilot.enabled = false;
            Active.Teleport(new Vector3(0, 0, -8));
            Active.enabled = true;
            ReworkMouse(false);
        }

        Enemy ReworkTarget(Vector3 point)
        {
            var enemy = Enemy.Spawn(point, 3, 1);
            enemy.enabled = false;
            enemy.Health = enemy.MaxHealth = 10000;
            return enemy;
        }

        IEnumerator ReworkSteps()
        {
            UnityEngine.Random.InitState(726);
            yield return new WaitForSecondsRealtime(1);
            StartReworkFixture(Bird.Swallow);
            yield return new WaitForSeconds(.2f);
            var swallow = Active;
            var target = ReworkTarget(new Vector3(0, 0, -4));
            Check(swallow.Magazine == 30 && swallow.Definition.fireInterval == .18f,
                "Swallow loads 30 rounds and uses the faster 0.18-second firing interval");
            int projectiles = Projectile.SpawnCount;
            ReworkMouse(true);
            yield return new WaitForSeconds(.06f);
            ReworkMouse(false);
            yield return new WaitForSeconds(.25f);
            Check(Projectile.SpawnCount - projectiles == 7 && swallow.Ammo == 29,
                "One real trigger press launches seven independent pellets for one round");
            Check(10000 - target.Health >= swallow.Definition.damage * 3,
                "Multiple pellets from the same shot can damage one enemy");
            int ammoBefore = swallow.Ammo;
            ReworkMouse(true);
            yield return new WaitForSeconds(.65f);
            ReworkMouse(false);
            yield return null;
            Check(ammoBefore - swallow.Ammo >= 3, "Holding fire repeatedly shoots faster than the old Swallow weapon");

            InputSystem.QueueStateEvent(verificationKeyboard, new KeyboardState(Key.Q));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(verificationKeyboard, new KeyboardState());
            yield return new WaitForSeconds(.2f);
            Check(swallow.LaserMode && swallow.Raiden == 100, "Q arms laser mode without draining gauge while fire is released");
            float hpBefore = target.Health;
            ammoBefore = swallow.Ammo;
            ReworkMouse(true);
            yield return new WaitForSeconds(.55f);
            float gaugeAfterFire = swallow.Raiden;
            float normalLaserDamage = hpBefore - target.Health;
            Check(swallow.LaserFiring && gaugeAfterFire < 88 && normalLaserDamage > 150,
                "Holding the real fire input emits a damaging continuous laser and consumes gauge over time");
            Check(swallow.Ammo == ammoBefore, "Laser fire uses gauge instead of the normal magazine");
            yield return Capture("swallow-laser.png");
            gaugeAfterFire = swallow.Raiden;
            ReworkMouse(false);
            yield return new WaitForSeconds(.25f);
            Check(!swallow.LaserFiring && swallow.Raiden > gaugeAfterFire,
                "Releasing fire stops the laser and allows gauge recovery while the mode stays armed");
            swallow.Skill();
            Check(!swallow.LaserMode, "Reactivating Q immediately returns to normal fire");
            swallow.Raiden = 80;
            swallow.Skill();
            swallow.MoveAbility();
            hpBefore = swallow.Raiden;
            ReworkMouse(true);
            yield return new WaitForSeconds(.45f);
            Check(swallow.Accelerating && swallow.LaserFiring && hpBefore - swallow.Raiden > 20,
                "Acceleration and laser operate together and add both gauge costs");
            swallow.Raiden = 1;
            float depletionDeadline = Time.realtimeSinceStartup + 1;
            while (swallow.LaserActive && Time.realtimeSinceStartup < depletionDeadline)
                yield return null;
            Check(swallow.Raiden == 0 && !swallow.LaserMode && !swallow.Accelerating,
                "Gauge exhaustion turns off both laser mode and acceleration");
            ReworkMouse(false);
            yield return new WaitForSeconds(.2f);
            swallow.Ultimate = 100;
            swallow.UseUltimate();
            swallow.Skill();
            Check(swallow.LaserActive && swallow.UltimateTimer > 0 && swallow.Accelerating,
                "Ultimate forces empowered laser mode and Q cannot switch it off");
            ReworkMouse(true);
            hpBefore = target.Health;
            yield return new WaitForSeconds(.55f);
            Check(swallow.Raiden == 100 && swallow.LaserFiring && hpBefore - target.Health > 300,
                "Ultimate laser remains powerful with acceleration and does not consume gauge");
            yield return Capture("swallow-ultimate.png");
            hpBefore = target.Health;
            SetState(GameState.Paused);
            yield return new WaitForSecondsRealtime(.2f);
            Check(target.Health == hpBefore && swallow.Raiden == 100,
                "Pause stops continuous laser damage and gauge changes");
            SetState(GameState.Playing);
            ReworkMouse(false);
            swallow.UltimateTimer = .05f;
            yield return new WaitForSeconds(.12f);
            Check(!swallow.LaserActive, "Ultimate expiry restores the previous normal weapon mode");

            for (int stage = 0; stage < 3; stage++)
            {
                StartReworkFixture(Bird.Owl, stage);
                yield return new WaitForSeconds(.15f);
                var owl = Active;
                Vector3 aimAt = Arena.Ground(new Vector3(Arena.HalfExtent * .7f, 0, Arena.HalfExtent * .55f));
                var centerEnemy = ReworkTarget(aimAt);
                var wideEnemy = ReworkTarget(Arena.Ground(aimAt - Vector3.right * 17));
                owl.Ultimate = 100;
                owl.UseUltimate();
                yield return new WaitForSeconds(.75f);
                Check(owl.OwlAtAltitude && owl.transform.position.y - Arena.Ground(owl.transform.position).y >= 79,
                    "Owl body truly ascends to bombardment altitude in stage " + stage);
                bool wholeStage = Viewport.orthographic;
                foreach (float x in new[] { -Arena.HalfExtent, Arena.HalfExtent })
                    foreach (float z in new[] { -Arena.HalfExtent, Arena.HalfExtent })
                    {
                        Vector3 corner = Viewport.WorldToViewportPoint(Arena.Ground(new Vector3(x, 0, z)));
                        wholeStage &= corner.z > 0 && corner.x > .02f && corner.x < .98f && corner.y > .1f && corner.y < .9f;
                    }
                Check(wholeStage, "Bombardment camera fits every stage corner inside the HUD margins: stage " + stage);
                owl.Brain.Observe(Time.time);
                Check(owl.Brain.Sensor.CanSee(centerEnemy.Identity), "Aerial Owl sees ground targets from its real elevated eye: stage " + stage);
                var roofPoint = Arena.Ground(new Vector3(-Arena.HalfExtent * .5f, 0, -Arena.HalfExtent * .5f));
                var roofEnemy = ReworkTarget(roofPoint);
                var roof = Arena.Block("Owl sight test roof", roofPoint + Vector3.up * 6, new Vector3(8, 1, 8), Color.gray, null);
                Physics.SyncTransforms();
                Check(!owl.Brain.Sensor.CanSee(roofEnemy.Identity), "Aerial perception still respects solid cover: stage " + stage);
                Destroy(roof);
                Vector2 aimScreen = Viewport.WorldToScreenPoint(aimAt + Vector3.up);
                ReworkMouse(false, aimScreen);
                yield return null;
                yield return null;
                Check(Vector3.Distance(owl.AimGround(), aimAt) < 1
                    && Vector3.Distance(Arena.Ground(owl.transform.position), owl.AimGround()) > 22,
                    "Mouse aims at a distant stage point without the old 22m ability clamp: stage " + stage);
                owl.Ammo = 0;
                owl.ReloadTimer = 3;
                ReworkMouse(true, aimScreen);
                yield return new WaitForSeconds(.06f);
                ReworkMouse(false, aimScreen);
                Check(owl.NovaShots == 2 && centerEnemy.Health < 10000 && wideEnemy.Health < 10000,
                    "A real bomb consumes one charge and reaches targets 17m from its center even while reloading: stage " + stage);
                yield return Capture("owl-bombardment-" + (char)('a' + stage) + ".png");
                SwitchTo(1);
                Check(!Viewport.orthographic, "Switching away from flying Owl restores the normal camera: stage " + stage);
                SwitchTo(0);
                yield return null;
                yield return new WaitForSeconds(.4f);
                ReworkMouse(true, aimScreen);
                yield return new WaitForSeconds(.06f);
                ReworkMouse(false, aimScreen);
                yield return new WaitForSeconds(.75f);
                ReworkMouse(true, aimScreen);
                yield return new WaitForSeconds(.06f);
                ReworkMouse(false, aimScreen);
                yield return new WaitForSeconds(1);
                Check(!owl.OwlFlying && owl.NovaShots == 0 && !Viewport.orthographic
                    && Mathf.Abs(owl.transform.position.y - Arena.Ground(owl.transform.position).y) < .3f,
                    "Three bombs finish the ultimate, land the real body, and restore normal view: stage " + stage);
                Check(owl.Brain.Sensor.Settings.range == 34 && owl.Brain.Sensor.Settings.horizontalFov == AIConfiguration.Current.allyFov,
                    "Landing restores normal Owl perception settings: stage " + stage);
            }
            StartReworkFixture(Bird.Owl);
            yield return null;
            ReworkTarget(new Vector3(0, 0, 3));
            ReworkTarget(new Vector3(1.5f, 0, 3));
            ReworkTarget(new Vector3(0, 0, 5));
            var aiOwl = Active;
            aiOwl.Ultimate = 100;
            VerificationInput = false;
            verificationAutopilot = true;
            yield return new WaitForSeconds(1.4f);
            Check(aiOwl.OwlFlying && aiOwl.TotalDamage > 200,
                "Allied Owl AI starts aerial mode and bombs targets observed from altitude");
            Stage = 1;
            Selected.Clear();
            Selected.AddRange(new[] { 4, 0, 1, 3 });
            BeginMission(false);
            verificationAutopilot = true;
            Time.timeScale = 5;
            bool aiLaser = false;
            float deadline = Time.realtimeSinceStartup + 110;
            while (State == GameState.Playing && Time.realtimeSinceStartup < deadline)
            {
                foreach (var pilot in Party)
                {
                    aiLaser |= pilot.LaserFiring;
                }
                yield return null;
            }
            Time.timeScale = 1;
            verificationAutopilot = false;
            Check(State == GameState.Result && Won, "Reworked squad completes a natural stage B defense mission");
            Check(aiLaser, "Allied Swallow AI uses sustained laser during a mission");
            verified.Add("Rework stage B result=" + Won + ", kills=" + TotalKills + ", time=" + Elapsed.ToString("0.0") + ", tower=" + TowerHealth);
            yield return Capture("rework-result.png");
            ToTitle();
            Check(Party.Count == 0, "Reworked abilities clean up when returning to title");
        }
    }
}
