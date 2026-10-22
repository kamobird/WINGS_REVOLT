using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wings.AI;

namespace Wings
{
    public sealed partial class WingsGame
    {
        IEnumerator RunTacticsValidation()
        {
            verified.Clear();
            var steps = TacticsSteps();
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
                catch (Exception e)
                {
                    failure = e.ToString();
                    Debug.LogException(e);
                    break;
                }

                if (!more)
                    break;
                yield return current;
            }

            Time.timeScale = 1;
            verificationAutopilot = false;
            Directory.CreateDirectory(ReportDirectory());
            File.WriteAllText(Path.Combine(ReportDirectory(), "tactics-report.json"),
                JsonUtility.ToJson(new VerificationReport { passed = failure == null, checks = verified.ToArray(), failure = failure }, true));
            Debug.Log(failure == null ? "WINGS TACTICS VERIFICATION PASSED" : "WINGS TACTICS VERIFICATION FAILED");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit(failure == null ? 0 : 1);
        }

        // 通常任務を使い、訓練の敵補充コルーチンが単体検証へ入り込まないようにする。
        void StartTacticsFixture()
        {
            Selected.Clear();
            Selected.AddRange(new[] { 0, 1, 2, 3 });
            Stage = 0;
            BeginMission(false);
            foreach (var pilot in Party)
                pilot.enabled = false;
        }

        Enemy TacticsEnemy(Vector3 at, int kind = 2)
        {
            var enemy = Enemy.Spawn(at, kind, 1);
            enemy.enabled = false;
            enemy.Health = enemy.MaxHealth = 100000;
            return enemy;
        }

        IEnumerator TacticsSteps()
        {
            UnityEngine.Random.InitState(441);
            yield return new WaitForSecondsRealtime(1);
            StartTacticsFixture();
            yield return null;
            for (int i = 0; i < Party.Count; i++)
                Party[i].Teleport(new Vector3(25, 0, 25));
            var player = Party[0];
            player.Teleport(new Vector3(0, 0, -8));
            var enemies = new List<Enemy>();
            for (int i = 0; i < 10; i++)
            {
                var enemy = TacticsEnemy(player.transform.position + Quaternion.Euler(0, i * 36, 0) * Vector3.forward * 6);
                enemy.transform.rotation = Quaternion.LookRotation(player.Identity.Center - enemy.Identity.Center);
                enemies.Add(enemy);
            }

            Physics.SyncTransforms();
            float now = Time.time;
            int attacking = 0, firing = 0, supporting = 0;
            foreach (var enemy in enemies)
            {
                enemy.Brain.Sensor.Tick(now, true);
                var intent = enemy.Brain.Tick(now);
                if (intent.State == BrainState.Attack)
                    attacking++;
                if (intent.Fire)
                    firing++;
                if (intent.State == BrainState.Support)
                    supporting++;
            }

            Check(attacking == 4 && firing == 4, "Ten visible enemies grant exactly four Attack and Fire permissions to one player");
            Check(supporting == 6, "Enemies without attack capacity choose a non-attacking support position");
            enemies[0].frozen = 1;
            enemies[0].enabled = true;
            yield return null;
            Check(enemies[0].Brain.Intent.State != BrainState.Attack
                && EnemyCoordination.AttackCount(player.Identity.Id, Time.time) == 3,
                "Actual frozen enemy update immediately returns its attack slot");
            enemies[4].Brain.Tick(Time.time + .2f);
            Check(EnemyCoordination.AttackCount(player.Identity.Id, Time.time) == 4, "A waiting enemy can occupy the returned fourth slot");
            foreach (var enemy in enemies)
                enemy.enabled = true;
            bool alwaysCapped = true;
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                int count = 0, shots = 0;
                foreach (var enemy in enemies)
                    if (enemy)
                    {
                        if (enemy.Brain.Intent.State == BrainState.Attack && enemy.Brain.Intent.Target?.Id == player.Identity.Id)
                            count++;
                        if (enemy.Brain.Intent.Fire && enemy.Brain.Intent.Target?.Id == player.Identity.Id)
                            shots++;
                    }

                if (count > 4 || shots > 4)
                    alwaysCapped = false;
            }

            Check(alwaysCapped, "Actual enemy updates keep Attack and Fire counts at four or below across frames");
            foreach (var enemy in enemies)
                if (enemy)
                    enemy.enabled = false;
            int before = EnemyCoordination.AttackCount(player.Identity.Id, Time.time);
            foreach (var enemy in enemies)
                if (enemy && enemy.Brain.Intent.State == BrainState.Attack)
                {
                    enemy.Hit(200000, null);
                    break;
                }

            Check(EnemyCoordination.AttackCount(player.Identity.Id, Time.time) < before,
                "Death returns an attack slot before the destroyed object is collected");
            StartTacticsFixture();
            yield return null;
            for (int i = 0; i < Party.Count; i++)
                Party[i].Teleport(new Vector3(25, 0, -25));
            Party[0].Teleport(new Vector3(-4, 0, -6));
            Party[1].Teleport(new Vector3(4, 0, -6));
            enemies.Clear();
            now = Time.time;
            for (int i = 0; i < 12; i++)
            {
                var enemy = TacticsEnemy(new Vector3((i % 6 - 2.5f) * 1.4f, 0, i < 6 ? 0 : 2));
                enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
                enemies.Add(enemy);
            }

            Physics.SyncTransforms();
            foreach (var enemy in enemies)
            {
                enemy.Brain.Sensor.Tick(now, true);
                enemy.Brain.Tick(now);
            }

            Check(EnemyCoordination.AttackCount(Party[0].Identity.Id, now) == 4
                && EnemyCoordination.AttackCount(Party[1].Identity.Id, now) == 4,
                "Attack caps are per player and permit independent four-enemy groups");
            StartTacticsFixture();
            yield return null;
            foreach (var pilot in Party)
                pilot.Teleport(new Vector3(-25, 0, -25));
            enemies.Clear();
            for (int i = 0; i < 5; i++)
                enemies.Add(TacticsEnemy(new Vector3(-24 - i * 1.1f, 0, -20), 0));
            CrowdGrid.Clear();
            float minZ = 100, maxZ = -100;
            foreach (var enemy in enemies)
            {
                var waypoint = EnemyCoordination.TransitWaypoint(enemy.Identity, new Vector3(10, 0, -20), new Vector3(20, 0, -20), Time.time);
                minZ = Mathf.Min(minZ, waypoint.z);
                maxZ = Mathf.Max(maxZ, waypoint.z);
            }

            Check(maxZ - minZ > 2, "Enemies approaching on one line choose laterally separated walkable transit positions");
            foreach (var enemy in enemies)
                Destroy(enemy.gameObject);
            yield return null;
            enemies.Clear();
            for (int i = 0; i < 5; i++)
            {
                var enemy = TacticsEnemy(new Vector3(0, 0, 27 - i * 1.2f), 0);
                enemy.enabled = true;
                enemies.Add(enemy);
            }

            Time.timeScale = 3;
            yield return new WaitForSeconds(4);
            Time.timeScale = 1;
            float minX = 100, maxX = -100;
            foreach (var enemy in enemies)
            {
                enemy.enabled = false;
                minX = Mathf.Min(minX, enemy.transform.position.x);
                maxX = Mathf.Max(maxX, enemy.transform.position.x);
            }

            Check(maxX - minX > 1.5f, "Actual advancing enemies spread sideways instead of preserving their initial single file");
            StartTacticsFixture();
            yield return null;
            foreach (var pilot in Party)
                pilot.Teleport(new Vector3(25, 0, 25));
            var ally = Party[1];
            ally.Teleport(new Vector3(0, 0, -8));
            ally.transform.rotation = Quaternion.identity;
            var attacker = TacticsEnemy(new Vector3(0, 0, -18));
            Physics.SyncTransforms();
            ally.Hit(1, attacker.Identity);
            var eventContact = ally.Brain.Sensor.KnownContact(attacker.Identity.Id);
            Check(eventContact != null
                && eventContact.Source == EvidenceSource.Damage
                && !eventContact.Visible,
                "A real rear hit records an in-range unobstructed damage snapshot without granting visibility");
            Vector3 observedAtHit = eventContact.Position;
            attacker.transform.position += Vector3.right * 3;
            Check(ally.Brain.Sensor.KnownContact(attacker.Identity.Id).Position == observedAtHit,
                "An attacker moving after the hit cannot update the stored damage location");
            attacker.transform.position -= Vector3.right * 3;
            Physics.SyncTransforms();
            foreach (SquadOrder order in Enum.GetValues(typeof(SquadOrder)))
            {
                IssueOrder(1, order);
                var response = ally.Brain.Tick(Time.time);
                Check(response.SelfDefense
                    && response.State == BrainState.React
                    && !response.Fire,
                    "Rear-hit reaction overrides " + order
                    + " without firing at an unseen attacker");
            }

            ally.transform.rotation = Quaternion.Euler(0, 180, 0);
            ally.Brain.Sensor.Tick(Time.time, true);
            var kite = ally.Brain.Tick(Time.time + .2f);
            Check(kite.State == BrainState.Kite
                && kite.Fire
                && Vector3.Dot(kite.Direction, kite.Look) < -.5f,
                "After turning to see the attacker, the ally fires while moving backwards");
            float startZ = ally.transform.position.z, enemyHp = attacker.Health;
            ally.Brain.OnAttacked(attacker.Identity, Time.time);
            ally.enabled = true;
            yield return new WaitForSeconds(1.2f);
            ally.enabled = false;
            Check(ally.transform.position.z > startZ + 1
                && attacker.Health < enemyHp,
                "Actual PlayerAgent movement retreats while its weapon damages the observed attacker");
            ally.Brain.Reset();
            attacker.transform.position = new Vector3(0, 0, -80);
            Physics.SyncTransforms();
            Check(!ally.Brain.OnAttacked(attacker.Identity, Time.time)
                && ally.Brain.Sensor.KnownContact(attacker.Identity.Id) == null,
                "Damage beyond sight distance causes evasion without revealing an enemy");
            ally.Teleport(new Vector3(0, 0, -8));
            attacker.transform.position = new Vector3(0, 0, -18);
            var wall = Arena.Block("Tactics test wall", new Vector3(0, 2, -13), new Vector3(8, 4, 1), Color.gray, null);
            Physics.SyncTransforms();
            Check(!ally.Brain.OnAttacked(attacker.Identity, Time.time),
                "A rear damage event does not reveal an attacker behind solid cover");
            ClearVisionFans();
            int scansBeforeDisplay = ally.Brain.Sensor.ScanCount;
            var outline = VisionOutline(ally);
            Check(outline.Count == 37
                && Vector3.Distance(outline[18], Arena.Ground(ally.transform.position)) < 6,
                "The observed vision outline is clipped by a real wall instead of drawing through it");
            Check(ally.Brain.Sensor.ScanCount == scansBeforeDisplay, "Drawing a vision outline does not run or refresh AI perception");
            Destroy(wall);
            StartTacticsFixture();
            yield return null;
            foreach (var pilot in Party)
            {
                pilot.Teleport(new Vector3(-25, 0, -25));
                pilot.Brain.Reset();
            }

            attacker = TacticsEnemy(new Vector3(0, 0, 2), 0);
            attacker.enabled = true;
            float towerBefore = TowerHealth;
            yield return new WaitForSeconds(.3f);
            attacker.enabled = false;
            bool allReceived = TowerHealth < towerBefore;
            Vector3 towerEventPosition = attacker.Identity.Center;
            foreach (var pilot in Party)
            {
                var contact = pilot.Brain.Sensor.KnownContact(attacker.Identity.Id);
                allReceived &= contact != null && contact.Source == EvidenceSource.TowerAlert && !contact.Visible;
                if (contact != null)
                    towerEventPosition = contact.Position;
            }

            Check(allReceived, "An actual enemy tower attack broadcasts an instantaneous non-visual location to all allies");
            attacker.transform.position = new Vector3(20, 0, 20);
            bool fixedPosition = true, noFire = true;
            foreach (var pilot in Party)
            {
                var contact = pilot.Brain.Sensor.KnownContact(attacker.Identity.Id);
                fixedPosition &= contact.Position == towerEventPosition;
                noFire &= !pilot.Brain.Sensor.CanFire(contact, 100, pilot.Identity.Center);
            }

            Check(fixedPosition && noFire, "Tower alerts retain the attack-time coordinate and cannot directly authorize fire");
            Order = SquadOrder.Defend;
            IssueOrder(1, SquadOrder.Assault);
            IssueOrder(2, SquadOrder.Follow);
            Check(Party[1].Brain.Order == SquadOrder.Assault
                && Party[2].Brain.Order == SquadOrder.Follow
                && Party[0].Brain.Order == SquadOrder.Defend
                && Party[3].Brain.Order == SquadOrder.Defend,
                "Individual commands affect only the selected pilots");
            SwitchTo(2);
            Check(OrderFor(Party[1]) == SquadOrder.Assault
                && OrderFor(Party[2]) == SquadOrder.Follow,
                "Switching the controlled pilot preserves individual commands");
            IssueOrder(-1, SquadOrder.Follow);
            bool allFollow = true;
            foreach (var pilot in Party)
                allFollow &= pilot.Brain.Order == SquadOrder.Follow;
            Check(allFollow, "A whole-squad command replaces all individual orders");
            Stage = 2;
            BeginMission(false);
            yield return null;
            IssueOrder(1, SquadOrder.Assault);
            IssueOrder(2, SquadOrder.Defend);
            IssueOrder(3, SquadOrder.Follow);
            ToggleObservation();
            verificationAutopilot = true;
            Time.timeScale = 2;
            yield return new WaitForSeconds(8);
            Time.timeScale = 1;
            yield return Capture("tactics-vision-and-orders.png");
            Check(RenderedVisionFans == 4, "Overhead rendering draws a separate colored vision outline for all four allies");
            CommandWheel = true;
            CommandRecipient = 2;
            yield return Capture("tactics-individual-command.png");
            CommandWheel = false;
            Time.timeScale = 5;
            float deadline = Time.realtimeSinceStartup + 100;
            while (State == GameState.Playing && Time.realtimeSinceStartup < deadline)
                yield return null;
            Time.timeScale = 1;
            verificationAutopilot = false;
            Check(State == GameState.Result && Elapsed > 60, "A full map C mission with mixed individual orders reaches a natural result");
            verified.Add("Tactics stage C result=" + Won
                + ", wave=" + Wave
                + ", kills=" + TotalKills
                + ", time=" + Elapsed.ToString("0.0")
                + ", tower=" + TowerHealth);
            yield return Capture("tactics-result.png");
            ToTitle();
            Check(Party.Count == 0, "Tactical state and individual orders clean up when returning to title");
        }
    }
}
