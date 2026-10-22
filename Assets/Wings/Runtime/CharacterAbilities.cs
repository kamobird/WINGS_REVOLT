using UnityEngine;
using Wings.AI;

namespace Wings
{
    public sealed partial class PlayerAgent
    {
        public bool LaserMode { get; private set; }
        public bool LaserActive => Definition.bird == Bird.Swallow && (LaserMode || UltimateTimer > 0);
        public bool LaserFiring { get; private set; }
        public float LaserRange => UltimateTimer > 0 ? AbilityTuning.Current.empoweredLaserRange : AbilityTuning.Current.laserRange;
        float laserTimer;

        public bool OwlFlying { get; private set; }
        public bool OwlBombarding => OwlFlying && NovaShots > 0 && UltimateTimer > 0 && !Downed;
        public float OwlFlightHeight => Mathf.Max(AbilityTuning.Current.minimumFlightHeight,
            (Arena.HalfExtent + 2) * AbilityTuning.Current.flightExtentMultiplier);
        public bool OwlAtAltitude => transform.position.y >= Arena.Ground(transform.position).y + OwlFlightHeight - .5f;
        public float BombardmentRange => OwlFlightHeight + (Arena.HalfExtent + 2) * 3;

        /// <summary>Qは武器モードの切替。ULTの強制レーザーはQで解除できず、通常時は残量0で解除する。</summary>
        void ToggleLaser()
        {
            if (UltimateTimer > 0)
                return;
            if (LaserMode)
                LaserMode = false;
            else if (Raiden > 0)
                LaserMode = true;
            SkillTimer = 0;
            if (Controlled)
                WingsGame.Instance.Notify(LaserMode ? "レーザーモード / 射撃長押しでライデン消費" : "ライトショットへ切替");
        }

        /// <summary>
        /// Fireが呼ばれたフレームだけ射撃中とする。弾倉や装填と独立してゲージを使い、一定周期で命中を検査する。
        /// レーザーの壁貫通は旧ツバメスキルの性質を維持し、各周期で射線上の敵へ一度ずつダメージを与える。
        /// </summary>
        void FireRaidenLaser()
        {
            if (Raiden <= 0 && UltimateTimer <= 0)
            {
                LaserMode = false;
                return;
            }
            LaserFiring = true;
            if (laserTimer > 0)
                return;
            var tuning = AbilityTuning.Current;
            laserTimer += tuning.laserTickInterval;
            float dps = UltimateTimer > 0 ? tuning.empoweredLaserDamagePerSecond : tuning.laserDamagePerSecond;
            float width = UltimateTimer > 0 ? tuning.empoweredLaserWidth : tuning.laserWidth;
            BeamAttack(LaserRange, dps * tuning.laserTickInterval, width, true, false);
        }

        /// <summary>
        /// 加速・レーザーの消費を1か所で合算する。モードを選ぶだけでは消費せず、射撃停止中は回復できる。
        /// 入力/AIの実行後に呼ぶため、同じフレームの射撃状態を使う。ポーズ中はUpdate自体が停止する。
        /// </summary>
        void UpdateRaiden(float dt)
        {
            if (Definition.bird != Bird.Swallow)
                return;
            var tuning = AbilityTuning.Current;
            if (UltimateTimer > 0)
            {
                Raiden = 100;
                return;
            }
            float drain = (Accelerating ? tuning.accelerationDrainPerSecond : 0)
                + (LaserFiring ? tuning.laserDrainPerSecond : 0);
            Raiden = Mathf.Clamp(Raiden + (drain > 0 ? -drain : tuning.raidenRecoveryPerSecond) * dt, 0, 100);
            if (Raiden <= 0)
            {
                LaserMode = false;
                LaserFiring = false;
                Accelerating = false;
            }
        }

        /// <summary>身体そのものを高空へ移す。爆撃中は水平移動せず、3発を撃ち終えるか時間切れで降下する。</summary>
        bool UpdateOwlFlight(float dt)
        {
            if (!OwlFlying)
                return false;
            var tuning = AbilityTuning.Current;
            Vector3 ground = Arena.Ground(transform.position);
            float targetY = ground.y + (OwlBombarding ? OwlFlightHeight : 0);
            float seconds = OwlBombarding ? tuning.ascentSeconds : tuning.descentSeconds;
            float nextY = Mathf.MoveTowards(transform.position.y, targetY, OwlFlightHeight / seconds * dt);
            motor.Move(Vector3.up * (nextY - transform.position.y));
            vertical = 0;
            if (!OwlBombarding && transform.position.y <= ground.y + .25f)
            {
                OwlFlying = false;
                invincible = Mathf.Min(invincible, .35f);
                Brain.Reset();
                WingsGame.Instance.RefreshCombatCamera();
            }
            return true;
        }

        void BeginOwlFlight()
        {
            NovaShots = 3;
            UltimateTimer = AbilityTuning.Current.novaDuration;
            invincible = UltimateTimer + AbilityTuning.Current.descentSeconds;
            OwlFlying = true;
            moveDirection = Vector3.zero;
            vertical = 0;
            Brain.Reset();
            WingsGame.Instance.RefreshCombatCamera();
        }

        void ClearReworkedAbilities()
        {
            LaserMode = LaserFiring = false;
            laserTimer = 0;
            if (OwlFlying)
                Teleport(Arena.Ground(transform.position));
            OwlFlying = false;
            WingsGame.Instance.RefreshCombatCamera();
        }
    }
}
