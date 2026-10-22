using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using Wings.AI;

namespace Wings
{
    public sealed partial class WingsGame
    {
        /// <summary>mapCの地形・視界・実移動を確認する。通常のプレイでは呼ばれない検証専用の入口。</summary>
        IEnumerator RunMapCValidation()
        {
            verified.Clear();
            var steps = MapCValidationSteps();
            string failure = null;
            while (true)
            {
                object current = null;
                bool more;
                try
                {
                    more = steps.MoveNext();
                    if (more) current = steps.Current;
                }
                catch (Exception e)
                {
                    failure = e.ToString();
                    Debug.LogException(e);
                    break;
                }
                if (!more) break;
                yield return current;
            }
            Time.timeScale = 1;
            verificationAutopilot = false;
            Directory.CreateDirectory(ReportDirectory());
            File.WriteAllText(Path.Combine(ReportDirectory(), "mapc-report.json"),
                JsonUtility.ToJson(new VerificationReport { passed = failure == null, checks = verified.ToArray(), failure = failure }, true));
            Debug.Log(failure == null ? "WINGS MAP C VERIFICATION PASSED" : "WINGS MAP C VERIFICATION FAILED");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit(failure == null ? 0 : 1);
        }

        IEnumerator MapCValidationSteps()
        {
            UnityEngine.Random.InitState(621);
            yield return new WaitForSecondsRealtime(1);
            SetState(GameState.Stages);
            yield return Capture("mapc-stage-select.png");
            Stage = 2;
            Selected.Clear();
            Selected.AddRange(new[] { 0, 1, 2, 3 });
            BeginMission(true);
            yield return null;
            foreach (var p in Party) p.enabled = false;
            foreach (var e in Enemy.All.ToArray()) if (e) Destroy(e.gameObject);
            yield return null;

            Check(Arena.HalfExtent > 60 && Arena.Ground(new Vector3(55, 0, 0)).x == 55,
                "Map C uses expanded bounds instead of clamping to the old 30m area");
            Check(Mathf.Abs(Arena.Ground(new Vector3(-50, 0, 0)).y - 8) < .01f,
                "Western plateau is eight meters above the tower");
            Check(Mathf.Abs(Arena.Ground(new Vector3(50, 0, 0)).y + 4) < .01f,
                "Eastern basin is four meters below the tower");
            Check(Mathf.Abs(Arena.Ground(new Vector3(-50, 0, 50)).y - 12) < .01f,
                "Northwest terrace and eastern basin provide sixteen meters of relief");
            bool surfaceMatches = true;
            for (int i = 0; i < 30; i++)
            {
                Vector3 point = Arena.Ground(new Vector3(-57.7f + i * 3.9f, 0, -45.3f + i * 2.7f));
                if (!Physics.Raycast(point + Vector3.up * 40, Vector3.down, out var hit, 80, Arena.TerrainMask)
                    || Mathf.Abs(hit.point.y - point.y) > .02f) surfaceMatches = false;
            }
            Check(surfaceMatches, "Ground projection matches the actual terrain triangles at non-grid positions");

            bool allGatesReachTower = true, elevatedCorners = false;
            int longestCorners = 0;
            foreach (var gate in Arena.Gates)
            {
                var path = new NavMeshPath();
                bool complete = NavigationWorld.Sample(Arena.Ground(gate), out var from)
                    && NavigationWorld.Sample(Vector3.zero, out var to)
                    && NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                allGatesReachTower &= complete;
                longestCorners = Mathf.Max(longestCorners, path.corners.Length);
                foreach (var corner in path.corners) if (corner.y > 6) elevatedCorners = true;
            }
            Check(allGatesReachTower, "All four map C gates have complete NavMesh routes to the tower");
            Check(elevatedCorners && longestCorners >= 4, "NavMesh routes preserve elevation and bend around the labyrinth");

            var observer = Party[1];
            observer.Teleport(Arena.Ground(new Vector3(-50, 0, 0)));
            var target = Enemy.Spawn(new Vector3(50, 0, 0), 0, 1);
            target.enabled = false;
            observer.transform.rotation = Quaternion.LookRotation(target.Identity.Center - observer.Identity.Center);
            Physics.SyncTransforms();
            var sensor = observer.Brain.Sensor;
            sensor.Settings.range = 140;
            int previousMask = sensor.Settings.occlusionMask;
            sensor.Settings.occlusionMask = Arena.TerrainMask;
            Check(!sensor.CanSee(target.Identity), "The terrain itself blocks sight across the plateau edge");
            sensor.Clear();
            sensor.Tick(Time.time, true);
            Check(sensor.Visible.Count == 0, "Terrain-occluded enemies do not enter the sensor contact list");
            sensor.Settings.occlusionMask = previousMask;
            sensor.Settings.range = 55;
            Destroy(target.gameObject);
            yield return null;

            // 実際のPlayerAgentとCharacterControllerで上り坂を越えられることを確認する。
            Party[0].Teleport(Arena.Ground(new Vector3(-50, 0, -38)));
            observer.Teleport(Arena.Ground(new Vector3(-8, 0, -38)));
            observer.Brain.Reset();
            Order = SquadOrder.Follow;
            observer.enabled = true;
            Time.timeScale = 4;
            yield return new WaitForSeconds(12);
            Time.timeScale = 1;
            observer.enabled = false;
            Check(observer.transform.position.x < -32 && observer.transform.position.y > 7,
                "A following ally actually climbs from the lower route to the western plateau");
            yield return Capture("mapc-follow-uphill.png");

            // 射撃側を止め、敵が地形・曲がり角を越えて低い中央へ降りる様子を確認する。
            foreach (var p in Party) p.Teleport(new Vector3(0, 0, -5));
            var invader = Enemy.Spawn(new Vector3(-55, 0, 0), 1, 1);
            invader.Health = invader.MaxHealth = 100000;
            Time.timeScale = 4;
            yield return new WaitForSeconds(36);
            Time.timeScale = 1;
            Check(invader.transform.position.x > -22 && invader.transform.position.y < 6,
                "An enemy actually follows the winding route down from the plateau");
            Check(Mathf.Abs(invader.transform.position.y - Arena.Ground(invader.transform.position).y) < .65f,
                "Enemy gravity keeps its feet on descending terrain");

            BeginMission(false);
            yield return null;
            verificationAutopilot = true;
            ToggleObservation();
            ShowAIDebug = true;
            Order = SquadOrder.Assault;
            Check(ObservationMode && Viewport.orthographic, "Map C observation switches to an overhead camera and AI control");
            Time.timeScale = 4;
            yield return new WaitForSeconds(18);
            Time.timeScale = 1;
            yield return Capture("mapc-overview.png");
            ToggleObservation();
            Check(!ObservationMode && !Viewport.orthographic, "Observation can return to the normal character camera");
            Check(Vector3.Distance(Viewport.transform.position, Active.transform.position) < 12,
                "Leaving observation immediately restores a nearby character camera");
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("mapc-character-view.png");
            ToggleObservation();
            verificationAutopilot = true;
            Order = SquadOrder.Defend;
            Time.timeScale = 5;
            float deadline = Time.realtimeSinceStartup + 100;
            while (State == GameState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Time.timeScale = 1;
            verificationAutopilot = false;
            Check(State == GameState.Result && Elapsed > 60 && TotalKills > 5,
                "A complete map C mission reaches a natural result with elevated terrain and four waves");
            verified.Add("Stage C result=" + Won + ", wave=" + Wave + ", kills=" + TotalKills + ", time=" + Elapsed.ToString("0.0") + ", tower=" + TowerHealth);
            yield return Capture("mapc-result.png");
            ToTitle();
            Check(Party.Count == 0, "Map C mission cleanup releases its squad");
        }

    }
}
