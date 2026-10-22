using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Wings.AI;

namespace Wings
{
    public sealed partial class WingsGame
    {
        /// <summary>認識と表示の境界、実際の射撃による複数命中を、Windows実行版でも確認する。</summary>
        IEnumerator RunMinimapValidation()
        {
            verified.Clear();
            var steps = MinimapValidationSteps();
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
            Time.timeScale = 1;
            verificationAutopilot = false;
            Directory.CreateDirectory(ReportDirectory());
            File.WriteAllText(Path.Combine(ReportDirectory(), "minimap-report.json"),
                JsonUtility.ToJson(new VerificationReport { passed = failure == null, checks = verified.ToArray(), failure = failure }, true));
            Debug.Log(failure == null ? "WINGS MINIMAP VERIFICATION PASSED" : "WINGS MINIMAP VERIFICATION FAILED");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit(failure == null ? 0 : 1);
        }

        // 通常任務を使い、訓練の自動補充を検証に混ぜない。身体だけ止めて実際の弾・物理・UIは動かす。
        void StartMinimapFixture(int stage = 0)
        {
            Selected.Clear();
            Selected.AddRange(new[] { 0, 1, 2, 3 });
            Stage = stage;
            BeginMission(false);
            foreach (var pilot in Party)
            {
                pilot.enabled = false;
                pilot.Teleport(new Vector3(-25, 0, -25));
                pilot.Brain.Reset();
            }
        }

        Enemy MinimapTarget(Vector3 at)
        {
            var enemy = Enemy.Spawn(at, 3, 1);
            enemy.enabled = false;
            enemy.Health = enemy.MaxHealth = 10000;
            return enemy;
        }

        Enemy[] PenetrationTargets()
        {
            return new[]
            {
                MinimapTarget(new Vector3(0, 0, 0)),
                MinimapTarget(new Vector3(0, 0, 5)),
                MinimapTarget(new Vector3(0, 0, 10)),
                MinimapTarget(new Vector3(0, 0, 19))
            };
        }

        void PenetrationWall()
        {
            Arena.Block("Penetration test cover", new Vector3(0, 2, 14), new Vector3(12, 4, 1), Color.gray, null);
        }

        IEnumerator MinimapValidationSteps()
        {
            UnityEngine.Random.InitState(842);
            yield return new WaitForSecondsRealtime(1);
            StartMinimapFixture();
            yield return null;
            var observer = Party[1];
            observer.Teleport(new Vector3(0, 0, -8));
            observer.transform.rotation = Quaternion.identity;
            var detected = MinimapTarget(new Vector3(0, 0, 4));
            var unknown = MinimapTarget(new Vector3(24, 0, -29));
            Physics.SyncTransforms();
            CollectMinimapContacts(Time.time);
            Check(minimapContacts.Count == 0, "Registering real enemies cannot reveal them on the minimap");
            observer.Brain.Sensor.Tick(Time.time, true);
            CollectMinimapContacts(Time.time);
            Check(minimapContacts.Count == 1 && minimapContacts.ContainsKey(detected.Identity.Id)
                && !minimapContacts.ContainsKey(unknown.Identity.Id),
                "An enemy seen by a non-controlled ally appears while an unknown enemy stays hidden");
            var second = Party[2];
            second.Teleport(new Vector3(2, 0, -8));
            second.transform.rotation = Quaternion.identity;
            second.Brain.Sensor.Tick(Time.time, true);
            CollectMinimapContacts(Time.time);
            Check(minimapContacts.Count == 1, "Two allies observing the same enemy produce a single marker");
            int scansBefore = observer.Brain.Sensor.ScanCount + second.Brain.Sensor.ScanCount;
            CollectMinimapContacts(Time.time);
            Check(scansBefore == observer.Brain.Sensor.ScanCount + second.Brain.Sensor.ScanCount,
                "Collecting map markers never scans or changes AI perception");
            SwitchTo(2);
            CollectMinimapContacts(Time.time);
            Check(minimapContacts.ContainsKey(detected.Identity.Id),
                "Switching the controlled pilot preserves squad-wide minimap knowledge");
            yield return Capture("minimap-visible-enemy.png");
            Vector3 lastSeen = minimapContacts[detected.Identity.Id].Position;
            second.Brain.Sensor.Clear();
            var cover = Arena.Block("Minimap hidden movement", new Vector3(0, 2, 10), new Vector3(14, 4, 1), Color.gray, null);
            detected.transform.position = new Vector3(0, 0, 16);
            Physics.SyncTransforms();
            observer.Brain.Sensor.Tick(Time.time + .2f, true);
            CollectMinimapContacts(Time.time + .2f);
            Check(!minimapContacts[detected.Identity.Id].Visible
                && minimapContacts[detected.Identity.Id].Position == lastSeen,
                "Losing sight leaves a memory marker at the last observed coordinate");
            detected.transform.position = new Vector3(5, 0, 18);
            CollectMinimapContacts(Time.time + .3f);
            Check(minimapContacts[detected.Identity.Id].Position == lastSeen,
                "Hidden enemy movement cannot move a remembered map marker");
            yield return Capture("minimap-remembered-enemy.png");
            float eventTime = Time.time + .5f;
            var reported = new Vector3(12, 1, 18);
            observer.Brain.OnTowerAlert(detected.Identity, reported, eventTime);
            CollectMinimapContacts(eventTime);
            Check(!minimapContacts[detected.Identity.Id].Visible && minimapContacts[detected.Identity.Id].Position == reported,
                "Tower alerts show the event coordinate as memory instead of a live sighting");
            CollectMinimapContacts(eventTime + observer.Brain.Sensor.Settings.memorySeconds + .1f);
            Check(minimapContacts.Count == 0, "Expired knowledge disappears even without a new sensor scan");
            Check(MinimapPoint(Vector3.forward * 10).y < MinimapPoint(Vector3.zero).y
                && MinimapPoint(Vector3.right * 10).x > MinimapPoint(Vector3.zero).x,
                "Minimap keeps north up and east right");
            Destroy(cover);

            // タカの通常射撃: 手前の敵で止まらず、遮蔽物までの3体へ当たる。
            StartMinimapFixture();
            yield return null;
            var hawk = Party[1];
            hawk.Teleport(new Vector3(0, 0, -8));
            hawk.Aim = Vector3.forward;
            var targets = PenetrationTargets();
            PenetrationWall();
            Physics.SyncTransforms();
            hawk.Fire();
            Check(targets[0].Health < 10000 && targets[1].Health < 10000 && targets[2].Health < 10000,
                "Actual normal Hawk fire pierces three aligned enemies with one shot");
            Check(targets[3].Health == 10000, "Normal Hawk fire still stops at cover after piercing enemies");
            Check(10000 - targets[2].Health > 10000 - targets[0].Health,
                "Hawk distance damage bonus remains active after enemy penetration");
            Check(hawk.Ammo == hawk.Magazine - 1, "A penetrating Hawk shot consumes only one round");
            StartMinimapFixture();
            yield return null;
            hawk = Party[1];
            hawk.Teleport(new Vector3(0, 0, -8));
            hawk.Aim = Vector3.forward;
            hawk.UltimateTimer = 5;
            targets = PenetrationTargets();
            PenetrationWall();
            Physics.SyncTransforms();
            hawk.Fire();
            Check(targets[0].Health < 10000 && targets[3].Health < 10000,
                "Hawk ultimate still pierces both enemies and cover");

            // カラス: 実際のProjectile.Updateを待ち、フルチャージだけが身体を貫通することを確認する。
            StartMinimapFixture();
            yield return null;
            var crow = Party[2];
            crow.Teleport(new Vector3(0, 0, -8));
            crow.Aim = Vector3.forward;
            targets = PenetrationTargets();
            var side = MinimapTarget(new Vector3(6, 0, 5));
            PenetrationWall();
            Physics.SyncTransforms();
            crow.Fire(1);
            yield return new WaitForSeconds(.7f);
            Check(targets[0].Health < 10000 && targets[1].Health < 10000 && targets[2].Health < 10000,
                "An actual full-charge Crow projectile pierces three aligned enemies");
            Check(targets[3].Health == 10000 && side.Health == 10000,
                "Full-charge Crow shots stop at cover and do not damage enemies outside the trajectory");
            Check(targets[0].Health >= 9700 && targets[1].Health >= 9700 && targets[2].Health >= 9700,
                "A penetrating Crow projectile applies direct damage at most once per enemy");
            StartMinimapFixture();
            yield return null;
            crow = Party[2];
            crow.Teleport(new Vector3(0, 0, -8));
            crow.Aim = Vector3.forward;
            targets = PenetrationTargets();
            crow.Fire(.5f);
            yield return new WaitForSeconds(.5f);
            Check(targets[0].Health < 10000 && targets[1].Health == 10000 && targets[2].Health == 10000,
                "Partially charged Crow shots still stop at the first enemy");
            StartMinimapFixture();
            yield return null;
            crow = Party[2];
            crow.Teleport(new Vector3(0, 0, -8));
            crow.Aim = Vector3.forward;
            crow.FullChargeShots = 5;
            targets = PenetrationTargets();
            crow.Fire(0);
            yield return new WaitForSeconds(.5f);
            Check(targets[0].Health < 10000 && targets[2].Health < 10000 && crow.FullChargeShots == 4,
                "Crow skill-enhanced instant full-charge shots also pierce enemies");
            StartMinimapFixture();
            yield return null;
            crow = Party[2];
            crow.Teleport(new Vector3(0, 0, -8));
            crow.Aim = Vector3.forward;
            crow.FullChargeShots = 5;
            crow.ExplosiveShots = 5;
            targets = PenetrationTargets();
            side = MinimapTarget(new Vector3(3, 0, 5));
            crow.Fire(0);
            yield return new WaitForSeconds(.5f);
            Check(targets[2].Health < 10000 && side.Health < 10000
                && crow.FullChargeShots == 4 && crow.ExplosiveShots == 4,
                "Crow full-charge and explosive enhancements combine penetration with explosions along the path");

            // A/B/Cの実際のHUDと、俯瞰・AI表示・命令画面の重なりを画像で確認する。
            for (int stage = 0; stage < 3; stage++)
            {
                Stage = stage;
                BeginMission(false);
                verificationAutopilot = true;
                yield return new WaitForSeconds(6);
                yield return Capture("minimap-stage-" + (char)('a' + stage) + ".png");
                Check(minimapTerrain && minimapTerrain.width == 192, "Terrain minimap renders in stage " + stage);
                if (stage == 2)
                {
                    Check(MinimapHeightColor(Arena.Ground(new Vector3(-45, 0, 0)).y)
                        != MinimapHeightColor(Arena.Ground(new Vector3(45, 0, 0)).y),
                        "Map C minimap distinguishes high and low terrain");
                    ToggleObservation();
                    ShowAIDebug = true;
                    yield return Capture("minimap-overhead-and-debug.png");
                    CommandWheel = true;
                    CommandRecipient = 1;
                    yield return Capture("minimap-individual-orders.png");
                    CommandWheel = false;
                    ShowAIDebug = false;
                }
            }
            ToTitle();
            Check(!minimapTerrain && minimapContacts.Count == 0 && minimapWalls.Count == 0,
                "Returning to title releases terrain textures and all minimap information");
        }
    }
}
