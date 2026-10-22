using UnityEngine;

namespace Wings.AI
{
    [CreateAssetMenu(menuName="WINGS/AI Configuration")]
    public sealed class AIConfiguration : ScriptableObject
    {
        static AIConfiguration cached;
        public static AIConfiguration Current {get{if(!cached)cached=Resources.Load<AIConfiguration>("WingsAI");if(!cached)cached=CreateInstance<AIConfiguration>();return cached;}}
        [Header("Perception")]
        [Range(30,360)] public float allyFov=120,enemyFov=110;
        [Min(.03f)] public float scanInterval=.12f;
        [Min(.2f)] public float memorySeconds=3;
        [Min(.2f)] public float reportSeconds=1.5f;
        public LayerMask occlusionMask=Arena.WallMask;
        [Header("Commands")]
        [Min(5)] public float followLeash=12,defenseLeash=11;
        [Header("Target selection")]
        [Min(0)] public float targetHoldSeconds=.65f,targetSwitchAdvantage=8;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetCache(){cached=null;}
    }
}
