using UnityEngine;

namespace Wings
{
    /// <summary>
    /// WingsGameの画面描画をまとめたpartial定義。UnityのOnGUIで各画面とHUDを描く。
    /// </summary>
    public sealed partial class WingsGame
    {
        static readonly Color Ink = new Color(.027f, .047f, .075f),
            Panel = new Color(.045f, .078f, .115f, .94f),
            Muted = new Color(.7f, .8f, .88f),
            White = new Color(.9f, .96f, 1),
            Cyan = new Color(.26f, .86f, .94f),
            Red = new Color(1, .34f, .4f);
        Font uiFont;
        Texture2D circle;
        GUIStyle textStyle, buttonStyle;
        int previewIndex;

        /// <summary>
        /// 日本語フォントと描画スタイル、丸いアイコン用のテクスチャを初回だけ準備する。
        /// </summary>
        void PrepareUI()
        {
            if (textStyle != null)
                return;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "Arial" }, 20);
            textStyle = new GUIStyle
            {
                font = uiFont,
                wordWrap = true,
                richText = false
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                font = uiFont
            };
            buttonStyle.normal.background = Texture2D.blackTexture;
            circle = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32));
                    circle.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(32 - d)));
                }

            circle.Apply();
        }

        /// <summary>
        /// 単色の矩形を描く。呼出し前のGUI色を最後に戻し、他の描画に色を持ち越さない。
        /// </summary>
        void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>
        /// 準備した円形テクスチャを指定色で描く。
        /// </summary>
        void Disc(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, circle);
            GUI.color = old;
        }

        /// <summary>
        /// 共通の文字スタイルにサイズ・色・配置を設定して描画する。
        /// </summary>
        void Text(Rect r, string s, int size = 20, Color? color = null, FontStyle weight = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            textStyle.fontSize = size;
            textStyle.fontStyle = weight;
            textStyle.alignment = align;
            textStyle.normal.textColor = color ?? White;
            GUI.Label(r, s, textStyle);
        }

        /// <summary>
        /// 選択可能なボタンを描き、クリックされたときだけtrueを返す。画面遷移などは呼出し元で行う。
        /// </summary>
        bool Button(Rect r, string label, bool primary = false, bool enabled = true, Color? accent = null)
        {
            Color c = accent ?? Cyan;
            bool hover = r.Contains(Event.current.mousePosition) && enabled;
            Box(r,
                enabled ? (primary ? (hover ? c
                : Color.Lerp(c, Ink, .15f))
                : hover ? new Color(.14f, .24f, .3f)
                : Panel)
                : new Color(.08f, .11f, .14f));
            Box(new Rect(r.x, r.y, primary ? 4 : 2, r.height), enabled ? c : Muted * .4f);
            Text(new Rect(r.x + 18, r.y, r.width - 35, r.height),
                label,
                20,
                enabled ? (primary ? Ink
                : White)
                : Muted,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            if (!enabled)
                return false;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                Sound(680, .07f, .1f);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 0〜1の比率を指定幅のバーとして描く。HPやゲージの表示に共通使用する。
        /// </summary>
        void Bar(Rect r, float value, Color color)
        {
            Box(r, new Color(.13f, .2f, .25f));
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), color);
        }

        /// <summary>
        /// プリミティブ図形でキャラ色付きの仮アイコンを描く。
        /// </summary>
        void Portrait(Rect r, CharacterDefinition d)
        {
            Disc(r, new Color(.12f, .2f, .27f));
            Disc(new Rect(r.x + r.width * .23f, r.y + r.height * .15f, r.width * .54f, r.height * .7f), new Color(.025f, .045f, .07f));
            Disc(new Rect(r.x + r.width * .32f, r.y + r.height * .44f, r.width * .36f, r.height * .36f), d.color);
            Box(new Rect(r.x + r.width * .25f, r.y + r.height * .31f, r.width * .5f, r.height * .1f), d.color);
            Box(new Rect(r.x + r.width * .45f, r.y + r.height * .42f, r.width * .14f, r.height * .1f), new Color(1, .68f, .25f));
        }

        /// <summary>
        /// 各メニュー画面に共通する見出しと説明文を描く。
        /// </summary>
        void Header(string index, string title, string subtitle)
        {
            Text(new Rect(65, 42, 900, 32), index, 17, Cyan, FontStyle.Bold);
            Text(new Rect(63, 77, 1400, 74), title, 44, White, FontStyle.Bold);
            Text(new Rect(66, 150, 1400, 45), subtitle, 19, Muted);
            Box(new Rect(65, 197, 1470, 1), new Color(.2f, .34f, .4f));
        }

        /// <summary>
        /// 1600×900を基準に表示倍率を合わせ、状態に応じた画面と重ね表示を描く入口。
        /// </summary>
        void OnGUI()
        {
            PrepareUI();
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1600 * scale) * .5f, (Screen.height - 900 * scale) * .5f, 0),
                Quaternion.identity,
                Vector3.one * scale);
            if (State == GameState.Playing || State == GameState.Paused || State == GameState.Settings && Party.Count > 0)
            {
                HUD();
                if (State == GameState.Playing)
                {
                    MapCObservationUI();
                    OwlBombardmentUI();
                }
                MinimapUI();
            }
            if (State != GameState.Playing && State != GameState.Paused && State != GameState.Result && State != GameState.Settings)
                Box(new Rect(0, 0, 1600, 900), new Color(.02f, .04f, .07f, .45f));
            switch (State)
            {
                case GameState.Title:
                    TitleUI();
                    break;
                case GameState.Stages:
                    StagesUI();
                    break;
                case GameState.Characters:
                    CharactersUI();
                    break;
                case GameState.Loading:
                    LoadingUI();
                    break;
                case GameState.Paused:
                    PauseUI();
                    break;
                case GameState.Settings:
                    SettingsUI();
                    break;
                case GameState.Tutorial:
                    TutorialUI();
                    break;
                case GameState.Result:
                    ResultUI();
                    break;
            }

            if (State == GameState.Playing && ShowAIDebug)
                AIDebugUI();
            if (State == GameState.Playing && CommandWheel)
                OrdersUI();
            if (State == GameState.Playing && NoticeTime > 0)
            {
                Box(new Rect(410, 157, 780, 54), Panel);
                Box(new Rect(410, 157, 4, 54), Cyan);
                Text(new Rect(425, 157, 750, 54), Notice, 21, White, FontStyle.Bold, TextAnchor.MiddleCenter);
            }

            GUI.matrix = previous;
        }

        /// <summary>
        /// タイトルの出撃準備・訓練・設定・終了ボタンを描く。
        /// </summary>
        void TitleUI()
        {
            Box(new Rect(0, 0, 665, 900), new Color(.025f, .045f, .072f, .96f));
            Text(new Rect(72, 65, 540, 35), "TACTICAL SQUAD DEFENSE", 18, Cyan, FontStyle.Bold);
            Text(new Rect(65, 156, 600, 145), "WINGS", 108, White, FontStyle.Bold);
            Text(new Rect(74, 285, 530, 70), "R E V O L T", 42, Cyan, FontStyle.Bold);
            Box(new Rect(76, 379, 62, 4), Cyan);
            Text(new Rect(76, 414, 490, 80), "コカが守った星を、\n今度は僕たちが守る。", 26, White, FontStyle.Bold);
            if (Button(new Rect(76, 546, 480, 65), "PLAY    出撃準備", true))
            {
                Selected.Clear();
                SetState(GameState.Stages);
            }

            if (Button(new Rect(76, 630, 230, 55), "TUTORIAL"))
                SetState(GameState.Tutorial);
            if (Button(new Rect(326, 630, 230, 55), "SETTINGS"))
                OpenSettings();
            if (Button(new Rect(76, 708, 480, 45), "ゲームを終了"))
                Application.Quit();
            Text(new Rect(76, 820, 540, 40), "α 0.1     /     5 PILOTS. ONE PLANET.", 16, Muted);
            Text(new Rect(1010, 760, 470, 30), "01  /  FROST", 18, Cyan, FontStyle.Bold);
            Text(new Rect(1010, 795, 470, 50), "ペンギン", 32, White, FontStyle.Bold);
        }

        /// <summary>
        /// 3つの防衛区域と最高記録を描き、選択した区域から編成画面へ進める。
        /// </summary>
        void StagesUI()
        {
            Box(new Rect(0, 0, 1600, 900), Ink);
            Header("MISSION SELECT / 01", "防衛区域を選択", "侵略軍の残党が接近中。4羽の部隊を編成してタワーを守り抜こう。");
            string[] names =
            {
                "A / 凍てつく前哨基地",
                "B / 黄昏の侵攻跡地",
                "C / 段丘の迷宮"
            };
            string[] notes =
            {
                "3ウェーブ / 平坦な前哨基地\n4方向の侵入と開けた射線。\n部隊指示と能力の基本を確認。",
                "4ウェーブ / 遮蔽物の多い跡地\n侵攻間隔が短い防衛戦。\n大型侵攻兵器を迎え撃つ。",
                "4ウェーブ / 広さ4倍・高低差16m\n坂道・高台・低地・曲がり角。\nF2の俯瞰でAIの経路を観察。"
            };
            for (int i = 0; i < 3; i++)
            {
                float x = 65 + i * 500;
                Color c = i == 0 ? Cyan : i == 1 ? new Color(1, .55f, .34f) : new Color(.98f, .78f, .35f);
                Box(new Rect(x, 237, 470, 482), Panel);
                Box(new Rect(x, 237, 470, 5), c);
                // 各防衛区域の入口とタワーを簡略化した俯瞰図。実際の移動地図とは別の表示。
                for (int n = 0; n < 7; n++)
                    Box(new Rect(x + 32 + n * 65, 275, 1, 135), new Color(.13f, .23f, .29f));
                Disc(new Rect(x + 203, 310, 54, 54), c);
                if (i == 2)
                {
                    Box(new Rect(x + 62, 295, 35, 90), new Color(.4f, .5f, .6f));
                    Box(new Rect(x + 135, 286, 120, 9), c);
                    Box(new Rect(x + 195, 380, 140, 9), c);
                    Box(new Rect(x + 330, 320, 7, 60), c);
                }

                for (int n = 0; n < 4; n++)
                {
                    float a = n * Mathf.PI * .5f;
                    Box(new Rect(x + 224 + Mathf.Cos(a) * 180, 330 + Mathf.Sin(a) * 70, 14, 14), Red);
                }

                Text(new Rect(x + 26, 438, 420, 45), names[i], 25, White, FontStyle.Bold);
                Text(new Rect(x + 26, 494, 420, 95), notes[i], 20, Muted);
                Text(new Rect(x + 26, 599, 420, 33), "BEST  " + PlayerPrefs.GetInt("Wings.Best." + i, 0).ToString("N0"), 18, c);
                if (Button(new Rect(x + 26, 650, 418, 50), "この区域へ出撃準備", true, accent: c))
                {
                    Stage = i;
                    SetState(GameState.Characters);
                    ShowPreview(previewIndex);
                }
            }

            if (Button(new Rect(65, 798, 220, 52), "戻る"))
                Back();
        }

        /// <summary>
        /// 5羽から4羽を選ぶ編成画面。選択順と、プレビュー中キャラの説明・能力を描く。
        /// </summary>
        void CharactersUI()
        {
            Header("SQUAD SELECT / 02", "WINGSを編成", "5羽から4羽を選択。選んだ順番が操作切替の番号になります。");
            for (int i = 0; i < Roster.Count; i++)
            {
                var d = Roster[i];
                float y = 222 + i * 105;
                bool selected = Selected.Contains(i);
                var r = new Rect(65, y, 300, 92);
                bool hover = r.Contains(Event.current.mousePosition);
                Box(r, selected ? Color.Lerp(Panel, d.color, .2f) : Panel);
                Box(new Rect(65, y, 4, 92), d.color);
                Portrait(new Rect(80, y + 12, 65, 65), d);
                Text(new Rect(160, y + 13, 197, 33), d.displayName, 24, White, FontStyle.Bold);
                Text(new Rect(160, y + 51, 197, 28), selected ? "MEMBER 0" + (Selected.IndexOf(i) + 1) : d.callSign, 15, d.color);
                if (hover && previewIndex != i)
                {
                    ShowPreview(i);
                }

                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    Select(i);
            }

            var p = Roster[previewIndex];
            Box(new Rect(953, 222, 582, 520), Panel);
            Box(new Rect(953, 222, 4, 520), p.color);
            Text(new Rect(981, 242, 520, 30), p.callSign + "    /    " + p.role, 17, p.color, FontStyle.Bold);
            Text(new Rect(979, 280, 520, 55), p.displayName, 39, White, FontStyle.Bold);
            Text(new Rect(981, 346, 520, 95), p.story, 18, Muted);
            string[] details =
            {
                p.weapon,
                "E  " + p.bomb,
                "Q  " + p.skill,
                "SHIFT  " + p.move,
                "X  " + p.ultimate
            };
            float yy = 451;
            foreach (var detail in details)
            {
                Text(new Rect(981, yy, 520, 53), detail, 17, White);
                yy += 55;
            }

            Text(new Rect(423, 727, 500, 32),
                "HP " + p.health
                + "     SPEED " + p.speed.ToString("0.0")
                + "     " + p.role,
                18,
                p.color,
                FontStyle.Bold);
            Text(new Rect(423, 774, 700, 31),
                "TEAM  " + Selected.Count
                + " / " + SquadSize
                + "     " + string.Join(" ・ ", Selected.ConvertAll(i => Roster[i].displayName)),
                17,
                White);
            if (Button(new Rect(65, 816, 300, 52), "ESC  選択を戻す"))
                Back();
            if (Button(new Rect(1120, 803, 415, 65),
                Selected.Count == SquadSize ? "DEPLOY    出撃"
                : "あと " + (SquadSize - Selected.Count)
                + " 羽選択",
                true,
                Selected.Count == SquadSize))
                Deploy();
        }

        /// <summary>
        /// 任務生成前に表示するロード画面を描く。
        /// </summary>
        void LoadingUI()
        {
            Box(new Rect(0, 0, 1600, 900), Ink);
            Text(new Rect(100, 370, 1400, 80), "WINGS / 出撃準備中", 42, Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text(new Rect(100, 470, 1400, 60), "防衛区域と部隊を配置しています", 23, Muted, align: TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// タワー・ウェーブ・編成・能力・照準・敵HPなど、戦闘中の状態を描く。
        /// 表示用に世界の実体を参照するが、その情報をAIのContactへ書き戻すことはない。
        /// </summary>
        void HUD()
        {
            if (!Active)
                return;
            var p = Active;
            var c = p.Definition.color;
            Box(new Rect(26, 25, 425, 94), Panel);
            Text(new Rect(45, 37, 380, 25), Training ? "TRAINING / 操作訓練" : StageName, 16, Muted);
            Text(new Rect(45, 71, 380, 35),
                Training ? "能力を自由に試そう"
                : "WAVE 0" + Wave
                + " / 0" + MaxWaves
                + "     " + (WaveTimer <= 0 ? "残敵を掃討"
                : Mathf.CeilToInt(WaveTimer)
                + " s"),
                24,
                White,
                FontStyle.Bold);
            Box(new Rect(525, 25, 550, 89), Panel);
            Text(new Rect(545, 35, 510, 28),
                "DEFENSE TOWER     " + Mathf.CeilToInt(TowerHealth)
                + " / " + TowerMax,
                17,
                White,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            Bar(new Rect(547, 78, 506, 10),
                TowerHealth / TowerMax,
                TowerHealth / TowerMax < .25f ? Red
                : TowerHealth / TowerMax < .55f ? new Color(1, .78f, .3f)
                : Cyan);
            Text(new Rect(1200, 31, 370, 35),
                "HOSTILES  " + Enemy.All.Count.ToString("00")
                + "      KILLS  " + TotalKills,
                18,
                White,
                FontStyle.Bold,
                TextAnchor.MiddleRight);
            Text(new Rect(1190, 73, 380, 30), "RMB 指示  /  ESC ポーズ", 16, Muted, align: TextAnchor.MiddleRight);
            if (breakTimer > 0 && !Training)
                Text(new Rect(550, 229, 500, 65),
                    "次の侵攻まで " + Mathf.CeilToInt(breakTimer),
                    30,
                    Cyan,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter);
            for (int i = 0; i < Party.Count; i++)
            {
                var a = Party[i];
                if (!a)
                    continue;
                float y = 579 + i * 69;
                Box(new Rect(26, y, 264, 60), a == p ? new Color(.09f, .2f, .26f, .95f) : Panel);
                Box(new Rect(26, y, 3, 60), a.Downed ? Red : a.Definition.color);
                Portrait(new Rect(37, y + 6, 47, 47), a.Definition);
                Text(new Rect(93, y + 5, 175, 25), (i + 1) + "  " + a.Definition.displayName, 18, a.Downed ? Muted : White, FontStyle.Bold);
                Text(new Rect(93, y + 31, 175, 22),
                    a.Downed ? "復帰まで " + Mathf.CeilToInt(a.RespawnTimer)
                    + " 秒"
                    : a == p ? "CONTROL / " + Mathf.CeilToInt(a.Health)
                    : "AI / " + new[] { "追従", "防衛", "攻撃" }[(int)OrderFor(a)],
                    14,
                    a.Downed ? Red
                    : Muted);
                Bar(new Rect(93, y + 56, 172, 3), a.Health / a.Definition.health, a.Definition.color);
            }

            Box(new Rect(351, 767, 899, 105), Panel);
            Text(new Rect(375, 780, 240, 35), p.Definition.displayName, 25, c, FontStyle.Bold);
            Text(new Rect(375, 823, 210, 25),
                p.Downed ? "DOWN / 復活待ち"
                : "HP " + Mathf.CeilToInt(p.Health)
                + " / " + p.Definition.health,
                16,
                White);
            Bar(new Rect(375, 856, 206, 5), p.Health / p.Definition.health, c);
            Text(new Rect(615, 781, 230, 35),
                p.LaserActive ? "RAIDEN LASER"
                : p.OwlFlying ? "NOVA / " + p.NovaShots
                : p.ReloadTimer > 0 ? "RELOADING " + p.ReloadTimer.ToString("0.0")
                : p.Ammo.ToString("00")
                + " / " + p.Magazine,
                25,
                White,
                FontStyle.Bold);
            Text(new Rect(615, 823, 240, 25), p.OwlFlying ? "カーソル照準 / LMB 爆撃"
                : p.LaserActive ? (p.UltimateTimer > 0 ? "LMB 長押し / ULT固定" : "LMB 長押し / Q 解除")
                : p.Definition.bird == Bird.Crow ? "LMB 長押し / R 装填" : "LMB 射撃 / R 装填", 15, Muted);
            AbilityUI(874, "Q", p.SkillTimer, p.LaserActive ? "レーザー中" : p.Definition.bird == Bird.Swallow ? "LASER" : "SKILL", c,
                p.OwlFlying ? "飛行中" : p.LaserActive ? (p.UltimateTimer > 0 ? "ULT" : "ON") : "READY");
            AbilityUI(964, "E", p.BombTimer, p.Decoy ? "再発動" : "BOMB", c, p.OwlFlying ? "飛行中" : "READY");
            AbilityUI(1054, "SHIFT", p.MoveTimer, p.Accelerating ? "加速中" : "MOVE", c, p.OwlFlying ? "飛行中" : "READY");
            Box(new Rect(1270, 767, 300, 105), Panel);
            Text(new Rect(1287, 778, 270, 38),
                "X  ULTIMATE   " + Mathf.FloorToInt(p.Ultimate)
                + "%",
                20,
                p.Ultimate >= 100 ? Cyan
                : Muted,
                FontStyle.Bold);
            Bar(new Rect(1289, 833, 260, 9), p.Ultimate / 100, c);
            if (p.NovaShots > 0)
                Text(new Rect(450, 693, 700, 36),
                    "SUPERNOVA / LMBで爆撃  残り " + p.NovaShots
                    + " 発",
                    22,
                    c,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter);
            if (p.Definition.bird == Bird.Swallow)
            {
                Text(new Rect(610, 705, 380, 28),
                    "RAIDEN " + Mathf.FloorToInt(p.Raiden) + (p.UltimateTimer > 0 ? " / ∞"
                    : ""),
                    17,
                    c,
                    align: TextAnchor.MiddleCenter);
                Bar(new Rect(650, 739, 300, 5), p.Raiden / 100, c);
            }

            if (p.Charge > 0)
            {
                Bar(new Rect(700, 500, 200, 7), p.Charge, c);
                Text(new Rect(650, 519, 300, 30), p.Charge >= .99f ? "FULL CHARGE" : "CHARGING", 16, c, align: TextAnchor.MiddleCenter);
            }

            if (p.ExplosiveShots > 0 || p.FullChargeShots > 0)
                Text(new Rect(520, 657, 570, 29),
                    "爆裂矢 " + p.ExplosiveShots
                    + "   /   即フルチャージ " + p.FullChargeShots,
                    18,
                    c,
                    align: TextAnchor.MiddleCenter);
            if (p.UltimateTimer > 0)
                Text(new Rect(1110, 707, 440, 32),
                    "ULT ACTIVE  " + p.UltimateTimer.ToString("0.0")
                    + "s",
                    20,
                    c,
                    FontStyle.Bold,
                    TextAnchor.MiddleRight);
            if (State == GameState.Playing && !CommandWheel && !p.OwlFlying)
            {
                Box(new Rect(788, 449, 8, 2), White);
                Box(new Rect(804, 449, 8, 2), White);
                Box(new Rect(799, 438, 2, 8), White);
                Box(new Rect(799, 454, 2, 8), White);
            }

            foreach (var e in Enemy.All)
            {
                if (!e || e.Health <= 0)
                    continue;
                bool hunterVision = p.Definition.bird == Bird.Hawk && p.UltimateTimer > 0;
                if (!hunterVision
                    && Physics.Linecast(Viewport.transform.position, e.transform.position + Vector3.up * (e.Boss ? 2
                    : 1.2f), Arena.OcclusionMask))
                    continue;
                var pos = Viewport.WorldToViewportPoint(e.transform.position + Vector3.up * (e.Boss ? 4 : 2.2f));
                if (pos.z <= 0 || pos.x < 0 || pos.x > 1 || pos.y < 0 || pos.y > 1)
                    continue;
                float x = pos.x * 1600, y = (1 - pos.y) * 900;
                if (p.Definition.bird == Bird.Hawk && p.UltimateTimer > 0)
                {
                    Disc(new Rect(x - 8, y - 25, 16, 16), Red);
                    Text(new Rect(x - 70, y - 60, 140, 25), "TARGET", 14, Red, FontStyle.Bold, TextAnchor.MiddleCenter);
                }

                Bar(new Rect(x - 32, y, 64, 4), e.Health / e.MaxHealth, e.frozen > 0 ? Cyan : Red);
            }

            for (int i = damageTexts.Count - 1; i >= 0; i--)
            {
                var t = damageTexts[i];
                float age = Time.time - t.time;
                if (age > .8f)
                {
                    damageTexts.RemoveAt(i);
                    continue;
                }

                var pos = Viewport.WorldToViewportPoint(t.at + Vector3.up * age);
                if (pos.z > 0)
                    Text(new Rect(pos.x * 1600 - 40, (1 - pos.y) * 900 - 30, 80, 40),
                        t.text,
                        22,
                        t.color,
                        FontStyle.Bold,
                        TextAnchor.MiddleCenter);
            }

            if (Training)
            {
                Text(new Rect(350, 127, 900, 35), "WASD 移動 / マウス 視点 / SPACE ジャンプ / 1～4 操作切替", 19, Cyan, align: TextAnchor.MiddleCenter);
                if (Button(new Rect(1320, 482, 250, 44), "訓練を終える"))
                    ToTitle();
            }
        }

        /// <summary>
        /// 能力キーと、使用可能か残りクールダウンかを描く共通部品。
        /// </summary>
        void AbilityUI(float x, string key, float cooldown, string title, Color c, string ready = "READY")
        {
            Box(new Rect(x, 782, 78, 56), cooldown > 0 ? new Color(.1f, .14f, .18f) : new Color(.12f, .27f, .32f));
            Text(new Rect(x, 785, 78, 25), key, 15, c, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text(new Rect(x, 812, 78, 23),
                cooldown > 0 ? cooldown.ToString("0.0")
                : ready,
                15,
                cooldown > 0 ? Muted
                : White,
                align: TextAnchor.MiddleCenter);
            Text(new Rect(x, 842, 78, 23), title, 12, Muted, align: TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// 対象を選んでから命令を出す。全員への命令は個別指示を解除し、個別命令は他の味方へ影響しない。
        /// </summary>
        void OrdersUI()
        {
            Box(new Rect(0, 0, 1600, 900), new Color(.02f, .04f, .07f, .55f));
            Text(new Rect(460, 185, 680, 50), "SQUAD COMMAND", 32, Cyan, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text(new Rect(360, 247, 880, 35), "RMBを押したまま、対象 → 作戦の順にクリック", 19, White, align: TextAnchor.MiddleCenter);
            for (int target = -1; target < Party.Count; target++)
            {
                string label = target < 0 ? "全員" : (target + 1) + "  " + Party[target].Definition.displayName;
                if (Button(new Rect(135 + (target + 1) * 270, 310, 250, 60), label, CommandRecipient == target))
                    CommandRecipient = target;
            }

            string[] labels =
            {
                "FOLLOW / 追従",
                "DEFEND / タワー防衛",
                "ASSAULT / 攻撃優先"
            };
            string[] notes =
            {
                "操作キャラの周囲へ集合",
                "タワーの周囲を守る",
                "近くの敵を積極的に攻撃"
            };
            for (int i = 0; i < 3; i++)
            {
                float x = 355 + i * 305;
                if (Button(new Rect(x, 421, 280, 85), labels[i], CommandMatches((SquadOrder)i)))
                {
                    IssueOrder(CommandRecipient, (SquadOrder)i);
                    CommandWheel = false;
                    UpdateCursor();
                    Notify((CommandRecipient < 0 ? "全員" : Party[CommandRecipient].Definition.displayName) + "：" + labels[i]);
                }

                Text(new Rect(x, 522, 280, 55), notes[i], 18, Muted, align: TextAnchor.MiddleCenter);
            }

            Text(new Rect(460, 621, 680, 30), "指示中も戦闘は進みます", 17, Muted, align: TextAnchor.MiddleCenter);
            Text(new Rect(300, 671, 1000, 30), "操作中の味方は、AI操作に切り替わると指示に従います", 17, Muted, align: TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// 再開・設定・ギブアップ・タイトルへ戻る操作を描く。
        /// </summary>
        void PauseUI()
        {
            Box(new Rect(0, 0, 1600, 900), new Color(.015f, .03f, .05f, .8f));
            Box(new Rect(550, 190, 500, 540), Panel);
            Text(new Rect(590, 225, 420, 70), "PAUSED", 42, White, FontStyle.Bold);
            if (Button(new Rect(590, 332, 420, 56), "再開", true))
                SetState(GameState.Playing);
            if (Button(new Rect(590, 409, 420, 56), "設定"))
                OpenSettings();
            if (Button(new Rect(590, 486, 420, 56), "ギブアップ"))
            {
                TowerHealth = 0;
                Finish(false);
            }

            if (Button(new Rect(590, 563, 420, 56), "タイトルへ戻る"))
                ToTitle();
            Text(new Rect(590, 656, 420, 30), "ESC で再開", 17, Muted);
        }

        /// <summary>
        /// 音量・感度・上下反転・画面モードの設定操作を描く。保存は戻る操作からSaveSettingsへ進む。
        /// </summary>
        void SettingsUI()
        {
            Box(new Rect(0, 0, 1600, 900), Ink);
            Header("CONFIGURATION", "設定", "音量・視点感度・表示モードはこのPCに保存されます。");
            Text(new Rect(150, 269, 500, 45), "BGM 音量", 25, White);
            MusicVolume = Slider(new Rect(650, 278, 650, 30), MusicVolume, 0, 1);
            Text(new Rect(1340, 270, 130, 35), Mathf.RoundToInt(MusicVolume * 100) + "%", 23, Cyan);
            Text(new Rect(150, 369, 500, 45), "効果音 音量", 25, White);
            EffectsVolume = Slider(new Rect(650, 378, 650, 30), EffectsVolume, 0, 1);
            Text(new Rect(1340, 370, 130, 35), Mathf.RoundToInt(EffectsVolume * 100) + "%", 23, Cyan);
            Text(new Rect(150, 469, 500, 45), "マウス視点感度", 25, White);
            Sensitivity = Slider(new Rect(650, 478, 650, 30), Sensitivity, .035f, .3f);
            Text(new Rect(1340, 470, 130, 35), Sensitivity.ToString("0.00"), 23, Cyan);
            if (Button(new Rect(150, 578, 550, 60), "上下反転  /  " + (InvertY ? "ON" : "OFF")))
                InvertY = !InvertY;
            if (Button(new Rect(800, 578, 550, 60), "画面  /  " + (Screen.fullScreen ? "フルスクリーン" : "ウィンドウ")))
            {
                Screen.fullScreen = !Screen.fullScreen;
            }

            if (Button(new Rect(150, 689, 550, 55), "効果音を試聴"))
                Sound(700, .2f, .4f);
            if (Button(new Rect(1100, 805, 400, 55), "保存して戻る", true))
                Back();
            if (music)
                music.volume = MusicVolume * .2f;
        }

        /// <summary>
        /// 値をバー表示し、横スライダーから変更後の値を返す。
        /// </summary>
        float Slider(Rect r, float value, float min, float max)
        {
            Bar(new Rect(r.x, r.y + 11, r.width, 5), (value - min) / (max - min), Cyan);
            return GUI.HorizontalSlider(r, value, min, max);
        }

        /// <summary>
        /// 基本操作の説明と、能力を試せる実地訓練への入口を描く。
        /// </summary>
        void TutorialUI()
        {
            Box(new Rect(0, 0, 1600, 900), Ink);
            Header("FIELD MANUAL", "タワーを守り抜け", "1羽を操作し、3羽の仲間と協力。ダウンした仲間は12秒後に復帰します。");
            string[] headings =
            {
                "01 / MOVE & FIRE",
                "02 / ABILITIES",
                "03 / SQUAD & DEFENSE"
            };
            string[] bodies =
            {
                "W A S D   視点基準で移動\nマウス   視点と照準\n左クリック   射撃\nカラスは長押しで溜めて離す\nR   リロード\nSPACE   ジャンプ",
                "Q   固有スキル\nE   ボム\nSHIFT   ムーブ\nX   アルティメット\n与ダメージと時間でULTが蓄積\nフクロウのULT中は左クリックで3回爆撃",
                "1～4 / 矢印   操作キャラ切替\n右クリック長押し＋左クリック   部隊指示\n追従 / タワー防衛 / 攻撃優先\nESC   ポーズ\nタワーの耐久値が0になると敗北\n最終ウェーブの残敵を倒すと勝利"
            };
            for (int i = 0; i < 3; i++)
            {
                float x = 65 + i * 500;
                Box(new Rect(x, 245, 470, 455), Panel);
                Text(new Rect(x + 27, 275, 420, 40), headings[i], 22, Cyan, FontStyle.Bold);
                Text(new Rect(x + 27, 343, 420, 330), bodies[i], 22, White);
            }

            if (Button(new Rect(65, 797, 260, 56), "戻る"))
                Back();
            if (Button(new Rect(1035, 797, 500, 56), "実地訓練を開始", true))
            {
                Stage = 0;
                BeginMission(true);
            }
        }

        /// <summary>
        /// 確定済みの勝敗・ランク・スコア・各キャラの与ダメージと撃破数を描く。
        /// </summary>
        void ResultUI()
        {
            Box(new Rect(0, 0, 1600, 900), Ink);
            Header(Won ? "MISSION COMPLETE"
                : "MISSION FAILED",
                Won ? "防衛成功"
                : "タワー陥落",
                StageName
                + "     /     WAVE " + Wave
                + "     /     TOWER " + Mathf.CeilToInt(TowerHealth)
                + " / " + TowerMax);
            Text(new Rect(85, 265, 400, 180), Rank, 130, Won ? Cyan : Red, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text(new Rect(95, 479, 380, 40), "SCORE", 19, Muted, align: TextAnchor.MiddleCenter);
            Text(new Rect(95, 529, 380, 70), Score.ToString("N0"), 46, White, FontStyle.Bold, TextAnchor.MiddleCenter);
            Text(new Rect(95, 637, 380, 80),
                "BEST  " + PlayerPrefs.GetInt("Wings.Best." + Stage).ToString("N0")
                + "\n撃破 " + TotalKills
                + "   /   防衛時間 " + Mathf.FloorToInt(Elapsed)
                + " 秒",
                19,
                Muted,
                align: TextAnchor.MiddleCenter);
            Text(new Rect(560, 247, 450, 36), "PILOT", 19, Muted, FontStyle.Bold);
            Text(new Rect(1095, 247, 250, 36), "DAMAGE", 19, Muted, FontStyle.Bold);
            Text(new Rect(1400, 247, 130, 36), "KILLS", 19, Muted, FontStyle.Bold);
            for (int i = 0; i < Party.Count; i++)
            {
                var p = Party[i];
                float y = 307 + i * 95;
                Box(new Rect(545, y, 990, 79), Panel);
                Portrait(new Rect(562, y + 9, 60, 60), p.Definition);
                Text(new Rect(650, y + 15, 430, 48), p.Definition.displayName, 25, p.Definition.color, FontStyle.Bold);
                Text(new Rect(1095, y + 15, 250, 48), Mathf.RoundToInt(p.TotalDamage).ToString("N0"), 25, White);
                Text(new Rect(1400, y + 15, 100, 48), p.Kills.ToString(), 25, White);
            }

            if (Button(new Rect(65, 803, 400, 56), "タイトルへ戻る"))
                ToTitle();
            if (Button(new Rect(1135, 803, 400, 56), "同じ編成で再出撃", true))
                BeginMission(false);
        }
    }
}
