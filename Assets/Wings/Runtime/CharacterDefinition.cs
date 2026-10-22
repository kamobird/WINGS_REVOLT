using UnityEngine;

namespace Wings
{
    /// <summary>
    /// キャラの識別番号。設定読み込み・能力分岐・編成UIで共通に使う。
    /// </summary>
    public enum Bird
    {
        Penguin,
        Hawk,
        Crow,
        Owl,
        Swallow
    }

    /// <summary>
    /// ScriptableObjectに保存するキャラの説明・能力値・見た目。実行中のHPなどの状態はPlayerAgentが持つ。
    /// </summary>
    [CreateAssetMenu(menuName = "WINGS/Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        public Bird bird;
        public string displayName, callSign, role;
        [TextArea(3, 8)]
        public string story;
        [TextArea]
        public string weapon, bomb, skill, move, ultimate;
        public Color color = Color.cyan;
        public float health = 160, speed = 7, fireInterval = .18f, damage = 16;
        public float bombCooldown = 9, skillCooldown = 12, moveCooldown = 3;
        [Tooltip("Visual only. Keep physics and gameplay on the Player root. Faces local +Z, feet at Y=0.")]
        public GameObject visualPrefab;

        /// <summary>
        /// キャラ設定アセットがない場合や初期セットアップ時に使う、5羽分の初期設定を作る。
        /// </summary>
        public static CharacterDefinition Create(Bird bird)
        {
            var d = CreateInstance<CharacterDefinition>();
            d.bird = bird;
            switch (bird)
            {
                case Bird.Penguin:
                    d.displayName = "ペンギン";
                    d.callSign = "01 / FROST";
                    d.role = "氷結・オールラウンダー";
                    d.color = new Color(.22f, .83f, 1);
                    d.health = 180;
                    d.story = "親友コカが守ったこの星を、もう一度守る。石化から目覚めたペンギンは、石になった親友の意志を継ぎWINGSを結成した。能力を持たずとも、執念と薬剤投与で戦い続ける。";
                    d.weapon = "氷結弾：高連射・長射程。命中した敵を減速。";
                    d.bomb = "フロストフィールド：狙った地点に継続ダメージと減速。";
                    d.skill = "アイスウォール：前方に氷壁を生成し進路と射線を遮る。";
                    d.move = "ショートダッシュ：移動方向に素早く回避。";
                    d.ultimate = "アブソリュートゼロ：溜めた力を解放し周囲を大ダメージ・凍結。";
                    break;
                case Bird.Hawk:
                    d.displayName = "タカ";
                    d.callSign = "02 / GALE";
                    d.role = "風・ロングレンジ";
                    d.color = new Color(.38f, 1, .7f);
                    d.health = 140;
                    d.damage = 62;
                    d.fireInterval = .95f;
                    d.story = "ペンギンに誘われWINGSへ加わったクラスメイト。風を味方に遠くの獲物を射抜く。戦いを『狩り』と呼び、楽しむ一面も持つ。";
                    d.weapon = "ビームライフル：低連射・超長射程。遠い敵ほど高威力。";
                    d.bomb = "デコイ：敵を誘引。再発動でデコイを竜巻に変える。";
                    d.skill = "アップドラフト：飛び上がって周囲の敵を吹き飛ばす。";
                    d.move = "ワイヤー：最初に当たる壁まで高速移動。";
                    d.ultimate = "ハンターズアイ：敵を壁越しに捕捉し、射撃が壁を貫通。";
                    break;
                case Bird.Crow:
                    d.displayName = "カラス";
                    d.callSign = "03 / REQUIEM";
                    d.role = "死・チャージスナイパー";
                    d.color = new Color(.73f, .48f, 1);
                    d.health = 130;
                    d.damage = 30;
                    d.fireInterval = .35f;
                    d.story = "寡黙で怪しげな言葉を残す、つかみどころのないクラスメイト。死の力を矢に宿し、戦い続けるペンギンの姿を美しいと語る。";
                    d.weapon = "冥弓：長押しで溜める。最大溜めは30%で即死、ボスには追加ダメージ。";
                    d.bomb = "弔いの矢：次の5射が着弾地点で爆発。溜めで範囲と威力上昇。";
                    d.skill = "死の予告：次の5射を即フルチャージ。ボムと併用可能。";
                    d.move = "シャドウステップ：短い予備動作の後、移動方向へ転移。";
                    d.ultimate = "レクイエム：全ての敵にフルチャージの矢を降らせる。";
                    break;
                case Bird.Owl:
                    d.displayName = "フクロウ";
                    d.callSign = "04 / NOVA";
                    d.role = "星・エリアブラスト";
                    d.color = new Color(1, .71f, .31f);
                    d.health = 170;
                    d.speed = 6;
                    d.fireInterval = .68f;
                    d.damage = 38;
                    d.story = "星の力を持つ、ペンギンたちの教師。体を張って戦う生徒を気にかけ、特にペンギンを案じている。星の爆発で戦場を切り開く。";
                    d.weapon = "スターブラスター：時限爆発。複数の敵を巻き込むほど高威力。";
                    d.bomb = "オクタブラスト：周囲8方向に近距離ブラスターを放つ。";
                    d.skill = "メテオフィールド：狙った地点に隕石を繰り返し落とす。";
                    d.move = "ブラストジャンプ：爆風で移動方向へ高速移動。";
                    d.ultimate = "スーパーノヴァ：上空へ飛び、3回まで超大爆発を撃ち下ろす。";
                    break;
                case Bird.Swallow:
                    d.displayName = "ツバメ";
                    d.callSign = "05 / RAIDEN";
                    d.role = "神速・クロスレンジ";
                    d.color = new Color(1, .38f, .58f);
                    d.health = 125;
                    d.speed = 8.5f;
                    d.fireInterval = .48f;
                    d.damage = 12;
                    d.story = "ペンギンたちの一学年下の生徒。雷撃を速さへ変える神速の使い手。ライデンゲージを操り、至近距離で戦場を駆け抜ける。";
                    d.weapon = "ライトショット：一度に多数の光弾を扇状発射。近距離で強い。";
                    d.bomb = "スプリットライト：4連射。分裂した光弾が小爆発する。";
                    d.skill = "ライデンビーム：ゲージを消費して強力な貫通ビーム。";
                    d.move = "アクセル：ゲージを消費して加速。再使用か残量0で解除。";
                    d.ultimate = "リミットブレイク：ゲージが無限になりビームと加速を強化。";
                    break;
            }

            return d;
        }
    }
}
