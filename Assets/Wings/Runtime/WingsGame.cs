using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Wings.AI;

namespace Wings
{
    /// <summary>
    /// 画面とゲーム進行の状態。戦闘AIのBrainStateとは別で、ポーズやリザルトを含む全体の状態を表す。
    /// </summary>
    public enum GameState
    {
        Title,
        Stages,
        Characters,
        Loading,
        Playing,
        Paused,
        Settings,
        Tutorial,
        Result
    }

    /// <summary>
    /// 味方への大命令。Follow=追従、Defend=タワー防衛、Assault=攻撃優先。下位の行動状態はAIが選ぶ。
    /// </summary>
    public enum SquadOrder
    {
        Follow,
        Defend,
        Assault
    }

    /// <summary>
    /// ゲーム全体の進行役。画面遷移、編成、任務開始/終了、ウェーブ、カメラ、設定を管理する。
    /// partialによりUI・AI観察・自動検証を別ファイルに分けている。
    /// </summary>
    public sealed partial class WingsGame : MonoBehaviour
    {
        public static WingsGame Instance { get; private set; }

        public const int SquadSize = 4;
        public List<CharacterDefinition> Roster = new List<CharacterDefinition>();
        public readonly List<int> Selected = new List<int>();
        public readonly List<PlayerAgent> Party = new List<PlayerAgent>();
        public GameState State { get; private set; } = GameState.Title;

        public SquadOrder Order = SquadOrder.Defend;
        public PlayerAgent Active => Party.Count > activeIndex ? Party[activeIndex] : null;
        public Camera Viewport { get; private set; }
        public float CameraYaw { get; private set; }

        public float TowerHealth = 1500, TowerMax = 1500, WaveTimer, Elapsed;
        public int Stage, Wave = 1, activeIndex, TotalKills;
        public int MaxWaves => Stage == 0 ? 3 : 4;
        public bool CommandWheel { get; private set; }

        public bool Won, Training;
        public float MusicVolume = .3f, EffectsVolume = .6f, Sensitivity = .12f;
        public bool InvertY;
        public string Notice { get; private set; } = "";

        public float NoticeTime;
        public string StageName => Stage == 0 ? "A / 凍てつく前哨基地"
            : Stage == 1 ? "B / 黄昏の侵攻跡地" : "C / 段丘の迷宮";
        public int Score { get; private set; }
        public string Rank { get; private set; } = "E";
        public bool SmokeTesting { get; private set; }
        public bool AITesting { get; private set; }

        public bool ShowAIDebug;
        Transform arena, preview;
        float cameraPitch = 14, spawnTimer, breakTimer = 4, noticeEnd;
        bool bossSpawned;
        bool verificationAutopilot;
        GameState settingsReturn = GameState.Title;
        AudioSource se, music;
        readonly Dictionary<int, AudioClip> sounds = new Dictionary<int, AudioClip>();
        readonly List<FloatingText> damageTexts = new List<FloatingText>();
        sealed class FloatingText
        {
            public Vector3 at;
            public string text;
            public Color color;
            public float time;
        }

        /// <summary>
        /// シーンに進行役が存在しない場合だけ生成し、どのシーンからでも起動できるようにする。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!FindFirstObjectByType<WingsGame>())
                new GameObject("WINGS REVOLT").AddComponent<WingsGame>();
        }

        /// <summary>
        /// キャラ設定・保存設定・カメラ・仮音源を用意し、起動引数に応じて検証を開始する。
        /// </summary>
        void Awake()
        {
            if (Instance && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Application.targetFrameRate = 90;
            if (Roster.Count == 0)
            {
                var definitions = Resources.LoadAll<CharacterDefinition>("Characters");
                foreach (Bird b in Enum.GetValues(typeof(Bird)))
                {
                    var d = Array.Find(definitions, x => x.bird == b);
                    Roster.Add(d ? d : CharacterDefinition.Create(b));
                }
            }

            MusicVolume = PlayerPrefs.GetFloat("Wings.Music", .3f);
            EffectsVolume = PlayerPrefs.GetFloat("Wings.Effects", .6f);
            Sensitivity = PlayerPrefs.GetFloat("Wings.Sensitivity", .12f);
            InvertY = PlayerPrefs.GetInt("Wings.InvertY", 0) == 1;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                c.gameObject.SetActive(false);
            Viewport = new GameObject("WINGS Camera").AddComponent<Camera>();
            Viewport.tag = "MainCamera";
            Viewport.fieldOfView = 65;
            Viewport.nearClipPlane = .15f;
            Viewport.farClipPlane = 150;
            Viewport.backgroundColor = new Color(.045f, .08f, .14f);
            Viewport.clearFlags = CameraClearFlags.SolidColor;
            Viewport.gameObject.AddComponent<AudioListener>();
            RenderSettings.ambientLight = new Color(.48f, .57f, .7f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Viewport.backgroundColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .009f;
            var light = new GameObject("Key light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(.83f, .9f, 1);
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            light.shadows = LightShadows.Soft;
            se = gameObject.AddComponent<AudioSource>();
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.clip = CreateMusic();
            music.volume = MusicVolume * .2f;
            music.Play();
            arena = Arena.Build(0);
            ShowPreview(0);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wings-smoke") >= 0)
            {
                SmokeTesting = true;
                StartCoroutine(SmokeTest());
            }

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wings-ai-test") >= 0)
            {
                SmokeTesting = true;
                AITesting = true;
                StartCoroutine(RunAIValidation());
            }

            SetState(GameState.Title);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wings-mapc-test") >= 0)
            {
                SmokeTesting = true;
                StartCoroutine(RunMapCValidation());
            }
        }

        /// <summary>
        /// 終了時に時間倍率とカーソルを戻す。EditorでPlayを終えた後の操作を妨げないための後片付け。
        /// </summary>
        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 通常プレイでウィンドウのフォーカスが外れたとき、自動的にポーズへ移る。
        /// </summary>
        void OnApplicationFocus(bool focused)
        {
            if (!focused && !SmokeTesting && State == GameState.Playing)
                SetState(GameState.Paused);
        }

        /// <summary>
        /// 画面状態を変更し、時間停止とカーソルの状態を揃える。
        /// </summary>
        public void SetState(GameState next)
        {
            State = next;
            Time.timeScale = next == GameState.Paused || next == GameState.Settings ? 0 : 1;
            CommandWheel = false;
            UpdateCursor();
        }

        /// <summary>
        /// 通常の戦闘中だけ視点操作用にカーソルを固定する。指示選択や検証では固定を解除する。
        /// </summary>
        void UpdateCursor()
        {
            bool locked = State == GameState.Playing && !CommandWheel && !SmokeTesting && !ObservationMode;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// 通知文と、実時間を基準にした表示終了時刻を記録する。
        /// </summary>
        public void Notify(string message, float seconds = 2.3f)
        {
            Notice = message;
            noticeEnd = Time.unscaledTime + seconds;
        }

        /// <summary>
        /// 画面共通の入力、操作キャラ切替、指示選択、戦闘進行を更新する。個体のAIは各キャラから更新する。
        /// </summary>
        void Update()
        {
            NoticeTime = Mathf.Max(0, noticeEnd - Time.unscaledTime);
            if (State != GameState.Playing)
            {
                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                    Back();
                return;
            }

            if (SmokeTesting)
            {
                if (verificationAutopilot)
                    Advance(Time.deltaTime);
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb?.escapeKey.wasPressedThisFrame == true)
            {
                SetState(GameState.Paused);
                return;
            }

            if (kb?.f1Key.wasPressedThisFrame == true)
                ShowAIDebug = !ShowAIDebug;
            if (Stage == 2 && kb?.f2Key.wasPressedThisFrame == true)
                ToggleObservation();
            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    CommandWheel = true;
                    UpdateCursor();
                }

                if (mouse.rightButton.wasReleasedThisFrame && CommandWheel)
                {
                    CommandWheel = false;
                    UpdateCursor();
                }
            }

            if (!CommandWheel && !ObservationMode && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                CameraYaw += delta.x * Sensitivity;
                cameraPitch = Mathf.Clamp(cameraPitch + delta.y * Sensitivity * (InvertY ? 1 : -1), -18, 62);
            }

            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
                    SwitchTo(0);
                if (kb.digit2Key.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                    SwitchTo(1);
                if (kb.digit3Key.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
                    SwitchTo(2);
                if (kb.digit4Key.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
                    SwitchTo(3);
            }

            Advance(Time.deltaTime);
        }

        /// <summary>
        /// 任務のタイマー、敵の出現、ウェーブ切替、自然な勝利条件を進める。訓練中は通常ウェーブを進めない。
        /// </summary>
        public void Advance(float dt)
        {
            if (State != GameState.Playing || Training)
                return;
            Elapsed += dt;
            if (breakTimer > 0)
            {
                breakTimer -= dt;
                if (breakTimer <= 0)
                    Notify("WAVE " + Wave + " / 侵攻開始", 2);
                return;
            }

            WaveTimer = Mathf.Max(0, WaveTimer - dt);
            spawnTimer -= dt;
            if (Wave == MaxWaves && !bossSpawned)
            {
                bossSpawned = true;
                Enemy.Spawn(Arena.Gates[0], 3, Wave);
                Notify("SIEGE COMMANDER / 大型侵攻兵器接近", 4);
            }

            if (spawnTimer <= 0 && WaveTimer > 0 && Enemy.All.Count < 12 + Wave * 4)
            {
                spawnTimer = Mathf.Max(.65f, 2.3f - Wave * .3f - (Stage == 1 ? .25f : 0));
                for (int i = 0; i < (Wave >= 3 ? 2 : 1); i++)
                    SpawnEnemy();
            }

            if (WaveTimer <= 0)
            {
                if (Wave < MaxWaves)
                {
                    Wave++;
                    WaveTimer = 38;
                    breakTimer = 5;
                    Notify("WAVE CLEAR / 次の侵攻に備えよう", 4);
                    foreach (var p in Party)
                        if (p && !p.Downed)
                            p.Health = Mathf.Min(p.Definition.health, p.Health + 25);
                }
                else if (Enemy.All.Count == 0)
                    Finish(true);
            }
        }

        /// <summary>
        /// 出現ゲートと敵種別を抽選して生成する。敵AIの判断とは別の、ゲーム進行側の出現処理。
        /// </summary>
        public Enemy SpawnEnemy()
        {
            int gate = UnityEngine.Random.Range(0, 4);
            int kind = UnityEngine.Random.value < .18f ? 2 : UnityEngine.Random.value < .32f ? 1 : 0;
            return Enemy.Spawn(Arena.Gates[gate] + new Vector3(UnityEngine.Random.Range(-2, 2), 0, UnityEngine.Random.Range(-2, 2)),
                kind,
                Wave);
        }

        /// <summary>
        /// 身体の移動後に追従カメラを更新する。壁へのめり込みを避け、非戦闘画面ではプレビューを映す。
        /// </summary>
        void LateUpdate()
        {
            if (!Viewport)
                return;
            if (ObservationMode && Active)
            {
                UpdateObservationCamera();
                return;
            }
            if ((State == GameState.Playing
                || State == GameState.Paused
                || State == GameState.Settings
                && settingsReturn == GameState.Paused
                || State == GameState.Result)
                && Active)
            {
                var pivot = Active.transform.position + Vector3.up * 1.8f;
                var rotation = Quaternion.Euler(cameraPitch, CameraYaw, 0);
                var offset = rotation * new Vector3(.85f, .45f, -5.8f);
                var desired = pivot + offset;
                if (Physics.SphereCast(pivot, .22f, offset.normalized, out var hit, offset.magnitude, Arena.OcclusionMask))
                    desired = pivot + offset.normalized * Mathf.Max(.5f, hit.distance - .2f);
                Viewport.transform.SetPositionAndRotation(Vector3.Lerp(Viewport.transform.position, desired, 1 - Mathf.Exp(-16 * Time.unscaledDeltaTime)), rotation);
            }
            else
            {
                var at = preview ? preview.position + Vector3.up * 1.1f : Vector3.up;
                float a = Time.unscaledTime * .06f;
                var position = at + new Vector3(Mathf.Sin(a) * 1.2f + 3.5f, 2, -6.7f);
                Viewport.transform.position = position;
                Viewport.transform.LookAt(at + (State == GameState.Characters ? Vector3.right * .6f : Vector3.left * 1.4f));
            }
        }

        /// <summary>
        /// ダウンしていない編成キャラへ操作を切り替える。切替前のキャラの溜めを解除する。
        /// </summary>
        public void SwitchTo(int index)
        {
            if (index < 0 || index >= Party.Count || Party[index].Downed)
                return;
            Active?.CancelCharge();
            activeIndex = index;
            Sound(600, .06f, .07f);
        }

        /// <summary>
        /// 出撃前の編成を追加/解除し、プレビュー表示を選択キャラへ変える。
        /// </summary>
        public void Select(int index)
        {
            if (Selected.Contains(index))
                Selected.Remove(index);
            else if (Selected.Count < SquadSize)
                Selected.Add(index);
            ShowPreview(index);
            Sound(520 + index * 100, .07f, .08f);
        }

        /// <summary>
        /// 選択中キャラの見た目だけをメニュー用に生成する。戦闘用の身体やAIは作らない。
        /// </summary>
        public void ShowPreview(int index)
        {
            if (preview)
                Destroy(preview.gameObject);
            var root = new GameObject("Character preview").transform;
            root.position = new Vector3(0, 0, -6);
            root.rotation = Quaternion.Euler(0, 153, 0);
            root.localScale = Vector3.one * 1.7f;
            preview = root;
            WingsVisual.BirdModel(Roster[index], root);
            previewIndex = index;
        }

        /// <summary>
        /// 編成人数が揃っていればロード処理を開始する。
        /// </summary>
        public void Deploy()
        {
            if (Selected.Count != SquadSize)
                return;
            StartCoroutine(LoadMission());
        }

        /// <summary>
        /// ロード画面を一度描画してから任務を開始する。
        /// </summary>
        IEnumerator LoadMission()
        {
            SetState(GameState.Loading);
            yield return null;
            yield return new WaitForSecondsRealtime(.15f);
            BeginMission(false);
        }

        /// <summary>
        /// 前の任務を片付け、ステージ・NavMesh・編成キャラを作り直して戦闘を開始する。
        /// </summary>
        public void BeginMission(bool training)
        {
            ClearBattle();
            ObservationMode = false;
            Viewport.orthographic = false;
            Viewport.farClipPlane = Stage == 2 ? 300 : 150;
            RenderSettings.fogDensity = Stage == 2 ? .003f : .009f;
            Training = training;
            TotalKills = 0;
            Elapsed = 0;
            Wave = 1;
            WaveTimer = 38;
            spawnTimer = 1;
            breakTimer = training ? 0 : 4;
            bossSpawned = false;
            TowerHealth = TowerMax;
            Won = false;
            Order = SquadOrder.Defend;
            activeIndex = 0;
            CameraYaw = 0;
            cameraPitch = 14;
            // 地形を生成してから経路地図を作る。キャラの生成前にNavMeshを用意し、初回の移動判断から利用できるようにする。
            if (arena)
                Destroy(arena.gameObject);
            arena = Arena.Build(Stage);
            NavigationWorld.Build(arena);
            if (preview)
            {
                Destroy(preview.gameObject);
                preview = null;
            }

            if (Selected.Count != SquadSize)
            {
                Selected.Clear();
                for (int i = 0; i < SquadSize; i++)
                    Selected.Add(i);
            }

            for (int i = 0; i < Selected.Count; i++)
                Party.Add(PlayerAgent.Create(Roster[Selected[i]], new Vector3((i - 1.5f) * 1.6f, 0, -5)));
            SetState(GameState.Playing);
            if (training)
            {
                foreach (var p in Party)
                    p.Ultimate = 100;
                for (int i = 0; i < 5; i++)
                    Enemy.Spawn(new Vector3((i - 2) * 3, 0, 12), 0, 1);
                Notify("訓練開始 / タワーを守り、各能力と操作切替を試そう", 6);
            }
            else
                Notify("タワーを守り抜け。WINGS、出撃。", 3);
        }

        /// <summary>
        /// 身体・弾・一時フィールド・氷壁・共有AI情報を解除する。次の任務へ情報を持ち越さない。
        /// </summary>
        public void ClearBattle()
        {
            ObservationMode = false;
            if (Viewport) Viewport.orthographic = false;
            foreach (var p in Party)
                if (p)
                    Destroy(p.gameObject);
            Party.Clear();
            foreach (var e in Enemy.All.ToArray())
                if (e)
                    Destroy(e.gameObject);
            Enemy.All.Clear();
            foreach (var p in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                Destroy(p.gameObject);
            foreach (var f in FindObjectsByType<AreaField>(FindObjectsSortMode.None))
                Destroy(f.gameObject);
            foreach (var d in FindObjectsByType<Decoy>(FindObjectsSortMode.None))
                Destroy(d.gameObject);
            // 実体だけでなく共有情報も破棄する。前の任務の報告や位置予約を次の任務の知識として使わない。
            SquadBlackboard.Clear();
            CrowdGrid.Clear();
            foreach (var fx in FindObjectsByType<FadeEffect>(FindObjectsSortMode.None))
                Destroy(fx.gameObject);
            foreach (var block in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
                if (block.name == "Ice wall")
                    Destroy(block.gameObject);
            damageTexts.Clear();
        }

        /// <summary>
        /// 通常戦闘中のタワー被弾を適用し、耐久値0で敗北にする。訓練では耐久値を減らさない。
        /// </summary>
        public void TowerHit(float damage)
        {
            if (State != GameState.Playing || Training)
                return;
            TowerHealth = Mathf.Max(0, TowerHealth - damage);
            if (TowerHealth <= 0)
                Finish(false);
        }

        /// <summary>
        /// 撃破数を増やし、訓練中なら次の練習相手の出現を予約する。
        /// </summary>
        public void EnemyKilled(Enemy enemy)
        {
            TotalKills++;
            if (Training)
                StartCoroutine(TrainingRespawn());
        }

        /// <summary>
        /// 訓練が継続中なら一定時間後に練習相手を補充する。
        /// </summary>
        IEnumerator TrainingRespawn()
        {
            yield return new WaitForSeconds(3);
            if (Training && State == GameState.Playing)
                SpawnEnemy();
        }

        /// <summary>
        /// 勝敗・スコア・ランクを確定する。自動検証では通常プレイの最高記録を書き換えない。
        /// </summary>
        public void Finish(bool won)
        {
            if (State != GameState.Playing && State != GameState.Paused)
                return;
            Won = won;
            Score = Mathf.RoundToInt((won ? 5000 : 0) + TowerHealth * 4 + Mathf.Max(0, Wave - 1) * 1000 + TotalKills * 75);
            Rank = won ? (TowerHealth / TowerMax >= .8f ? "S"
                : TowerHealth / TowerMax >= .55f ? "A"
                : "B")
                : (Wave >= MaxWaves ? "C"
                : Wave >= 2 ? "D"
                : "E");
            string key = "Wings.Best." + Stage;
            if (!SmokeTesting && Score > PlayerPrefs.GetInt(key, 0))
            {
                PlayerPrefs.SetInt(key, Score);
                PlayerPrefs.Save();
            }

            SetState(GameState.Result);
            Sound(won ? 900 : 160, .5f, .15f);
        }

        /// <summary>
        /// 任務を片付けてタイトルとキャラプレビューへ戻る。
        /// </summary>
        public void ToTitle()
        {
            if (!SmokeTesting)
                StopAllCoroutines();
            Time.timeScale = 1;
            ClearBattle();
            Training = false;
            SetState(GameState.Title);
            ShowPreview(0);
        }

        /// <summary>
        /// 設定画面へ入る前の状態を覚え、戻り先を保つ。
        /// </summary>
        public void OpenSettings()
        {
            settingsReturn = State;
            SetState(GameState.Settings);
        }

        /// <summary>
        /// 音量・感度・上下反転をPlayerPrefsへ保存する。
        /// </summary>
        public void SaveSettings()
        {
            PlayerPrefs.SetFloat("Wings.Music", MusicVolume);
            PlayerPrefs.SetFloat("Wings.Effects", EffectsVolume);
            PlayerPrefs.SetFloat("Wings.Sensitivity", Sensitivity);
            PlayerPrefs.SetInt("Wings.InvertY", InvertY ? 1 : 0);
            PlayerPrefs.Save();
            music.volume = MusicVolume * .2f;
        }

        /// <summary>
        /// Escや戻るボタンに対し、現在の画面に合った戻り先を選ぶ。
        /// </summary>
        public void Back()
        {
            Sound(380, .07f, .06f);
            switch (State)
            {
                case GameState.Playing:
                    SetState(GameState.Paused);
                    break;
                case GameState.Paused:
                    SetState(GameState.Playing);
                    break;
                case GameState.Settings:
                    SaveSettings();
                    SetState(settingsReturn);
                    break;
                case GameState.Characters:
                    if (Selected.Count > 0)
                        Selected.RemoveAt(Selected.Count - 1);
                    else
                        SetState(GameState.Stages);
                    break;
                case GameState.Stages:
                case GameState.Tutorial:
                    SetState(GameState.Title);
                    break;
                case GameState.Result:
                    ToTitle();
                    break;
            }
        }

        /// <summary>
        /// ダメージ表示を一定数まで保持する。表示位置はワールド座標で記録する。
        /// </summary>
        public void DamageText(Vector3 at, string text, Color color)
        {
            if (damageTexts.Count > 100)
                damageTexts.RemoveAt(0);
            damageTexts.Add(new FloatingText { at = at, text = text, color = color, time = Time.time });
        }

        /// <summary>
        /// 短い合成効果音を生成・キャッシュして再生する。外部音声アセットがないα版用の仮音。
        /// </summary>
        public void Sound(float frequency, float seconds, float volume)
        {
            if (!se || EffectsVolume <= 0)
                return;
            int key = Mathf.RoundToInt(frequency) + Mathf.RoundToInt(seconds * 1000) * 2000;
            if (!sounds.TryGetValue(key, out var clip))
            {
                const int rate = 22050;
                var data = new float[Mathf.Max(1, (int)(seconds * rate))];
                for (int i = 0; i < data.Length; i++)
                {
                    float t = (float)i / rate;
                    data[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * Mathf.Exp(-t / seconds * 5) * .5f;
                }

                clip = AudioClip.Create("Wings tone", data.Length, 1, rate, false);
                clip.SetData(data, 0);
                sounds[key] = clip;
            }

            se.PlayOneShot(clip, volume * EffectsVolume);
        }

        /// <summary>
        /// 波形を合成してループBGMを作る。ゲームルールやAIには影響しない。
        /// </summary>
        AudioClip CreateMusic()
        {
            const int rate = 22050;
            int n = rate * 16;
            var data = new float[n];
            int[] notes =
            {
                45,
                45,
                48,
                43,
                41,
                41,
                43,
                40
            };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float beat = t % .5f;
                int note = notes[(int)(t / 2) % notes.Length];
                float f = 440 * Mathf.Pow(2, (note - 69) / 12f);
                float bass = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-beat * 8);
                float pad = Mathf.Sin(2 * Mathf.PI * f * 2 * t) * .25f + Mathf.Sin(2 * Mathf.PI * f * 3 * t) * .12f;
                float kick = Mathf.Sin(2 * Mathf.PI * (45 + 60 * Mathf.Exp(-beat * 20)) * beat) * Mathf.Exp(-beat * 18);
                data[i] = (bass * .17f + pad * .13f + kick * .2f) * Mathf.Min(1, t * 2);
            }

            var clip = AudioClip.Create("Revolt pulse", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
