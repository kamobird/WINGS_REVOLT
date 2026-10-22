using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wings
{
    /// <summary>
    /// 既存の操作・能力・画面遷移の回帰確認をまとめたpartial定義。AI検証のレポート補助もここにある。
    /// </summary>
    public sealed partial class WingsGame
    {
        /// <summary>
        /// 検証中に操作キャラもAIで動かすかを、PlayerAgent側へ公開する。
        /// </summary>
        public bool VerificationAutopilot => verificationAutopilot;

        /// <summary>
        /// JSONに保存する検証結果。checksには成功した確認項目と、ステージ結果の記録が入る。
        /// </summary>
        [Serializable]
        sealed class VerificationReport
        {
            public bool passed;
            public string[] checks;
            public string failure;
        }

        readonly List<string> verified = new List<string>();

        /// <summary>
        /// 条件が偽なら例外で検証を止め、真なら確認項目を記録する共通の検証関数。
        /// </summary>
        void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception(description);
            verified.Add(description);
            Debug.Log("WINGS CHECK PASS: " + description);
        }

        /// <summary>
        /// 起動引数の出力先を読み、指定がなければアプリの保存領域に検証フォルダーを作る。
        /// </summary>
        string ReportDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--wings-report");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "Verification");
        }

        /// <summary>
        /// 回帰確認を順に実行し、失敗理由も含めたJSONを保存して終了する。
        /// </summary>
        IEnumerator SmokeTest()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wings-clean-test-scores") >= 0)
            {
                PlayerPrefs.SetInt("Wings.Best.0", 0);
                PlayerPrefs.SetInt("Wings.Best.1", 0);
                PlayerPrefs.Save();
            }

            var steps = VerificationSteps();
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

            var report = new VerificationReport
            {
                passed = failure == null,
                checks = verified.ToArray(),
                failure = failure
            };
            string directory = ReportDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log(failure == null ? "WINGS VERIFICATION PASSED" : "WINGS VERIFICATION FAILED");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit(failure == null ? 0 : 1);
        }

        /// <summary>
        /// 画面の描画完了後に検証画像を保存し、保存処理のために短い実時間の待機を入れる。
        /// </summary>
        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(ReportDirectory());
            ScreenCapture.CaptureScreenshot(Path.Combine(ReportDirectory(), filename));
            yield return new WaitForSecondsRealtime(.3f);
        }

        /// <summary>
        /// 5羽の通常攻撃・能力・復帰、操作切替、ポーズ、壁と射撃、勝敗、2ステージの自然な進行を確認する。
        /// </summary>
        IEnumerator VerificationSteps()
        {
            UnityEngine.Random.InitState(42);
            yield return new WaitForSecondsRealtime(2);
            yield return Capture("01-title.png");
            Check(Roster.Count == 5, "Five character definitions loaded");
            Check(State == GameState.Title, "Boot opens title");
            SetState(GameState.Stages);
            yield return Capture("02-stages.png");
            SetState(GameState.Title);
            OpenSettings();
            yield return Capture("07-settings.png");
            Back();
            SetState(GameState.Tutorial);
            yield return Capture("08-tutorial.png");
            Selected.Clear();
            SetState(GameState.Characters);
            for (int i = 0; i < 4; i++)
                Select(i);
            yield return Capture("03-squad.png");
            Check(Selected.Count == SquadSize, "Four unique squad members selected");
            for (int bird = 0; bird < 5; bird++)
            {
                Selected.Clear();
                Selected.Add(bird);
                for (int i = 0; Selected.Count < 4; i++)
                    if (i != bird)
                        Selected.Add(i);
                BeginMission(false);
                yield return null;
                foreach (var a in Party)
                    a.enabled = false;
                var p = Active;
                p.enabled = true;
                p.Teleport(new Vector3(0, 0, -8));
                p.Aim = Vector3.forward;
                p.AimPoint = new Vector3(0, 0, 1);
                var target = Enemy.Spawn(new Vector3(0, 0, 0), 0, 1);
                target.enabled = false;
                float hp = target.Health;
                p.Fire(bird == 2 ? 1 : 0);
                yield return new WaitForSeconds(.7f);
                Check(!target || target.Health < hp, "Weapon damages target: " + p.Definition.displayName);
                foreach (var e in Enemy.All.ToArray())
                    if (e)
                        Destroy(e.gameObject);
                yield return null;
                if (bird == 0)
                {
                    p.Bomb();
                    Check(FindObjectsByType<AreaField>(FindObjectsSortMode.None).Length > 0, "Penguin creates frost field");
                    p.Skill();
                    yield return null;
                    Check(Physics.Raycast(p.transform.position + Vector3.up, Vector3.forward, 5, Arena.WallMask),
                        "Ice wall blocks projectile and movement ray");
                    p.Ultimate = 100;
                    p.UseUltimate();
                    target = Enemy.Spawn(p.transform.position + Vector3.right * 7, 3, 1);
                    target.enabled = false;
                    yield return new WaitForSeconds(1.2f);
                    Check(target.frozen > 0, "Penguin ultimate freezes boss");
                }

                if (bird == 1)
                {
                    p.Bomb();
                    Check(p.Decoy, "Hawk creates decoy");
                    p.Bomb();
                    yield return null;
                    Check(!p.Decoy, "Hawk recasts decoy into tornado");
                    p.Ultimate = 100;
                    p.UseUltimate();
                    Check(p.UltimateTimer > 0, "Hawk activates wall vision and piercing mode");
                }

                if (bird == 2)
                {
                    p.Bomb();
                    p.Skill();
                    Check(p.ExplosiveShots == 5 && p.FullChargeShots == 5, "Crow stacks five explosive and five full-charge shots");
                    yield return new WaitForSeconds(.4f);
                    p.Fire();
                    Check(p.ExplosiveShots == 4 && p.FullChargeShots == 4, "Crow consumes both buffs once per shot");
                    p.Ultimate = 100;
                    p.UseUltimate();
                    Check(p.Ultimate == 0, "Crow ultimate consumes gauge");
                }

                if (bird == 3)
                {
                    p.Skill();
                    Check(FindObjectsByType<AreaField>(FindObjectsSortMode.None).Length > 0, "Owl creates meteor field");
                    p.Ultimate = 100;
                    p.UseUltimate();
                    Check(p.NovaShots == 3, "Owl ultimate grants three bombs");
                    yield return new WaitForSeconds(.7f);
                    p.Fire();
                    Check(p.NovaShots == 2, "Owl firing consumes one nova charge");
                }

                if (bird == 4)
                {
                    p.MoveAbility();
                    Check(p.Accelerating, "Swallow toggles acceleration on");
                    p.MoveAbility();
                    Check(!p.Accelerating, "Swallow toggles acceleration off");
                    p.Skill();
                    Check(p.LaserMode && p.Raiden == 100, "Swallow Q selects laser mode without spending gauge until firing");
                    p.Ultimate = 100;
                    p.UseUltimate();
                    Check(p.UltimateTimer > 0 && p.Raiden == 100, "Swallow ultimate restores and unlocks gauge");
                    p.SkillTimer = 0;
                    p.Skill();
                    Check(p.Raiden == 100, "Empowered beam does not spend gauge");
                }

                if (bird == 3)
                {
                    float safeHp = p.Health;
                    p.Hit(99);
                    Check(p.Health == safeHp, "Owl aerial ultimate grants invulnerability");
                    Time.timeScale = 10;
                    yield return new WaitForSeconds(AbilityTuning.Current.novaDuration + AbilityTuning.Current.descentSeconds + .5f);
                    Time.timeScale = 1;
                }

                if (bird < 4)
                {
                    GameObject anchor = null;
                    if (bird == 1)
                        anchor = Arena.Block("Wire anchor",
                            p.transform.position + Vector3.forward * 8 + Vector3.up,
                            new Vector3(4, 3, 1),
                            Color.gray,
                            null);
                    Vector3 start = p.transform.position;
                    p.MoveAbility();
                    yield return new WaitForSeconds(.65f);
                    Check(Vector3.Distance(start, p.transform.position) > 1,
                        "Movement ability changes position: " + p.Definition.displayName);
                    if (anchor)
                        Destroy(anchor);
                }

                p.Health = p.Definition.health;
                p.Hit(9999);
                Check(p.Downed && p.RespawnTimer == 12, "Downed state starts respawn timer: " + p.Definition.displayName);
            }

            Time.timeScale = 10;
            yield return new WaitForSeconds(12.2f);
            Time.timeScale = 1;
            Check(!Active.Downed
                && Active.Health == Active.Definition.health,
                "Downed pilot revives with full health after twelve seconds");
            Selected.Clear();
            for (int i = 0; i < 4; i++)
                Selected.Add(i);
            BeginMission(false);
            yield return null;
            var first = Active;
            SwitchTo(1);
            Check(Active != first && Active == Party[1], "Switching preserves distinct squad agents");
            SwitchTo(0);
            float old = WaveTimer;
            SetState(GameState.Paused);
            Advance(10);
            yield return new WaitForSecondsRealtime(.1f);
            Check(WaveTimer == old && Time.timeScale == 0, "Pause freezes mission timer");
            OpenSettings();
            Back();
            Check(State == GameState.Paused, "Settings returns to paused state");
            Back();
            Check(State == GameState.Playing && Time.timeScale == 1, "Resume restores time");
            foreach (var p in Party)
                p.enabled = false;
            var wall = Arena.Block("Test wall", new Vector3(0, 1, -1), new Vector3(5, 3, 1), Color.gray, null);
            var hawk = Party[1];
            hawk.Teleport(new Vector3(0, 0, -6));
            hawk.Aim = Vector3.forward;
            var blocked = Enemy.Spawn(new Vector3(0, 0, 3), 3, 1);
            blocked.enabled = false;
            float before = blocked.Health;
            hawk.BeamAttack(30, 50, .7f, false, true);
            Check(blocked.Health == before, "Normal rifle is blocked by cover");
            hawk.BeamAttack(30, 50, .7f, true, true);
            Check(blocked.Health < before, "Hunter ultimate rifle penetrates cover");
            Destroy(wall);
            Destroy(blocked.gameObject);
            foreach (var p in Party)
                p.enabled = true;
            for (int i = 0; i < 8; i++)
                SpawnEnemy();
            yield return new WaitForSeconds(4);
            yield return Capture("04-combat.png");
            SetState(GameState.Paused);
            yield return Capture("05-pause.png");
            SetState(GameState.Playing);
            var pending = Enemy.All.ToArray();
            foreach (var e in pending)
                if (e)
                    Destroy(e.gameObject);
            yield return null;
            Wave = MaxWaves;
            WaveTimer = 0;
            breakTimer = 0;
            bossSpawned = true;
            Advance(.1f);
            Check(State == GameState.Result && Won, "Final cleared wave produces victory");
            yield return Capture("06-victory.png");
            BeginMission(false);
            yield return null;
            TowerHit(TowerMax);
            Check(State == GameState.Result && !Won && TowerHealth == 0, "Tower destruction produces defeat");
            BeginMission(false);
            yield return null;
            verificationAutopilot = true;
            Time.timeScale = 5;
            float deadline = Time.realtimeSinceStartup + 60;
            while (State == GameState.Playing && Time.realtimeSinceStartup < deadline)
                yield return null;
            verificationAutopilot = false;
            Time.timeScale = 1;
            Check(State == GameState.Result, "AI defense mission reaches a natural result without getting stuck");
            Check(TotalKills > 15 && Elapsed > 30, "Full mission exercises enemy waves and allied combat");
            verified.Add("Natural stage A result: " + (Won ? "victory"
                : "defeat")
                + ", wave " + Wave
                + ", kills " + TotalKills
                + ", time " + Elapsed.ToString("0.0")
                + "s, tower " + TowerHealth);
            yield return Capture("09-stage-a-result.png");
            Stage = 1;
            Selected.Clear();
            Selected.AddRange(new[] { 0, 1, 2, 4 });
            BeginMission(false);
            yield return null;
            verificationAutopilot = true;
            Time.timeScale = 5;
            deadline = Time.realtimeSinceStartup + 75;
            while (State == GameState.Playing && Time.realtimeSinceStartup < deadline)
                yield return null;
            verificationAutopilot = false;
            Time.timeScale = 1;
            Check(State == GameState.Result && Wave >= 2, "Stage B with Swallow squad reaches a natural result");
            verified.Add("Natural stage B result: " + (Won ? "victory"
                : "defeat")
                + ", wave " + Wave
                + ", kills " + TotalKills
                + ", time " + Elapsed.ToString("0.0")
                + "s, tower " + TowerHealth);
            yield return Capture("10-stage-b-result.png");
            ToTitle();
            Check(State == GameState.Title && Party.Count == 0, "Returning to title clears battle");
        }
    }
}
