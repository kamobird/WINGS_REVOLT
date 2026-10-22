using UnityEngine;

namespace Wings
{
    /// <summary>Resources/WingsAbilitiesで調整するフクロウの空中爆撃とツバメの継続レーザーの設定。</summary>
    [CreateAssetMenu(menuName = "WINGS/Ability Tuning")]
    public sealed class AbilityTuning : ScriptableObject
    {
        static AbilityTuning cached;
        public static AbilityTuning Current
        {
            get
            {
                if (!cached)
                    cached = Resources.Load<AbilityTuning>("WingsAbilities");
                if (!cached)
                    cached = CreateInstance<AbilityTuning>();
                return cached;
            }
        }

        [Header("Owl / Supernova")]
        [Tooltip("爆撃1発の半径（m）。従来は8m。")]
        [Min(1)] public float novaRadius = 20;
        [Min(0)] public float novaDamage = 240;
        [Min(1)] public float novaDuration = 18;
        [Tooltip("地面からの最低高度。大きなステージでは地図の大きさに合わせてさらに高く飛ぶ。")]
        [Min(20)] public float minimumFlightHeight = 80;
        [Min(1)] public float flightExtentMultiplier = 1.8f;
        [Min(.1f)] public float ascentSeconds = .6f, descentSeconds = .75f;

        [Header("Swallow / Raiden Laser")]
        [Tooltip("レーザーの毎秒ダメージ。命中判定はlaserTickIntervalごとに、その秒数分の威力を適用する。")]
        [Min(0)] public float laserDamagePerSecond = 600, empoweredLaserDamagePerSecond = 900;
        [Min(.03f)] public float laserTickInterval = .1f;
        [Min(1)] public float laserRange = 34, empoweredLaserRange = 55;
        [Min(.1f)] public float laserWidth = 1, empoweredLaserWidth = 1.7f;
        [Tooltip("射撃している間だけ消費。加速中は加速の消費も足す。ULT中はどちらも消費しない。")]
        [Min(0)] public float laserDrainPerSecond = 30, accelerationDrainPerSecond = 22;
        [Min(0)] public float raidenRecoveryPerSecond = 14;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cached = null;
    }
}
