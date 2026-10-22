using UnityEngine;

namespace Wings.AI
{
    /// <summary>
    /// ScriptableObjectで保持するAIの調整値。視覚・記憶・通信・命令範囲・目標切替をまとめる。
    /// Resources/WingsAI.assetを編集して調整する。ここに書かれた値はアセットがない場合の初期値。
    /// </summary>
    [CreateAssetMenu(menuName = "WINGS/AI Configuration")]
    public sealed class AIConfiguration : ScriptableObject
    {
        static AIConfiguration cached;

        /// <summary>
        /// 設定アセットを最初の一度だけ読み込む。存在しない場合はメモリ上に初期値の設定を作る。
        /// </summary>
        public static AIConfiguration Current
        {
            get
            {
                if (!cached)
                    cached = Resources.Load<AIConfiguration>("WingsAI");
                if (!cached)
                    cached = CreateInstance<AIConfiguration>();
                return cached;
            }
        }

        /// <summary>
        /// 水平視野の全角（度）。実際の判定では正面から左右それぞれ半分ずつを視野内とする。
        /// </summary>
        [Header("Perception")]
        [Range(30, 360)]
        public float allyFov = 120, enemyFov = 110;

        /// <summary>
        /// 視覚検査の周期（秒）。小さいほど反応は細かくなるが、遮蔽の物理検査も増える。
        /// </summary>
        [Min(.03f)]
        public float scanInterval = .12f;

        /// <summary>
        /// 自分の最後の目撃情報を残す秒数。位置を予知する時間ではなく、古い観測を捜索に使える期限。
        /// </summary>
        [Min(.2f)]
        public float memorySeconds = 3;

        /// <summary>
        /// 味方報告の有効秒数。受信した時刻ではなく、報告に含まれる元の目撃時刻から数える。
        /// </summary>
        [Min(.2f)]
        public float reportSeconds = 1.5f;

        /// <summary>
        /// 視線を遮るColliderのレイヤー集合。通常の壁はArena.WallMask（レイヤー8）を使う。
        /// </summary>
        public LayerMask occlusionMask = Arena.WallMask;

        /// <summary>
        /// 追従時のリーダー、防衛時のタワーを基準にした許容距離（m）。
        /// </summary>
        [Header("Commands")]
        [Min(5)]
        public float followLeash = 12, defenseLeash = 11;

        /// <summary>
        /// 目標を保持する最小時間（秒）と切替に必要な点数差。目視を失った相手を撃ち続ける指定ではない。
        /// </summary>
        [Header("Target selection")]
        [Min(0)]
        public float targetHoldSeconds = .65f, targetSwitchAdvantage = 8;

        /// <summary>
        /// UnityのPlay開始時に設定のキャッシュを外し、前回実行した設定参照を持ち越さない。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            cached = null;
        }
    }
}
