using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Wings.AI
{
    /// <summary>
    /// 敵同士の協調。プレイヤーごとの攻撃許可枠と、近傍の仲間の状態を考えた進行位置を管理する。
    /// 相手を発見する仕組みではない。標的はEnemyBrainが目視済みのContactから選ぶ。
    /// </summary>
    public static class EnemyCoordination
    {
        public const int MaxPlayerAttackers = 4;
        sealed class AttackLease
        {
            public AIBody owner, target;
            public float until;
        }

        sealed class LanePlan
        {
            public Vector3 point, goal;
            public float until;
            public readonly List<AIBody> neighbors = new List<AIBody>();
        }

        static readonly Dictionary<int, AttackLease> attacks = new Dictionary<int, AttackLease>();
        static readonly Dictionary<int, LanePlan> lanes = new Dictionary<int, LanePlan>();
        static readonly List<int> expired = new List<int>();
        public static void Clear()
        {
            foreach (var lease in attacks.Values)
                Revoke(lease);
            attacks.Clear();
            lanes.Clear();
        }

        static void Revoke(AttackLease lease)
        {
            if (!lease.owner || !lease.owner.Invader || lease.owner.Invader.Brain == null)
                return;
            var intent = lease.owner.Invader.Brain.Intent;
            if (intent.State == BrainState.Attack)
            {
                intent.State = BrainState.Reposition;
                intent.Fire = false;
                intent.Reason = "攻撃枠を解放・位置を再評価";
            }
        }

        static void Prune(float now)
        {
            expired.Clear();
            foreach (var entry in attacks)
            {
                var lease = entry.Value;
                if (!lease.owner || !lease.owner.Alive || !lease.target || !lease.target.Alive || lease.until <= now)
                {
                    Revoke(lease);
                    expired.Add(entry.Key);
                }
            }

            foreach (int id in expired)
                attacks.Remove(id);
        }

        public static int AttackCount(int target, float now)
        {
            Prune(now);
            int count = 0;
            foreach (var lease in attacks.Values)
                if (lease.target.Id == target)
                    count++;
            return count;
        }

        public static bool HasCapacity(AIBody owner, Contact target, float now)
        {
            if (target == null || !target.Subject || !target.Subject.Pilot)
                return true;
            Prune(now);
            if (attacks.TryGetValue(owner.Id, out var own) && own.target == target.Subject)
                return true;
            return AttackCount(target.Id, now) < MaxPlayerAttackers;
        }

        /// <summary>
        /// Unityメインスレッドで枠を一つずつ取得するため、同一フレームに複数の敵が判定しても上限を超えない。
        /// 期限切れの敵の状態も先に降格させ、「古いAttack状態＋新しい4体」が残るのを防ぐ。
        /// </summary>
        public static bool TryAttack(AIBody owner, Contact target, float now)
        {
            if (!owner || !owner.Alive)
                return false;
            if (target == null || !target.Subject || !target.Subject.Pilot)
            {
                ReleaseAttack(owner.Id);
                return true;
            }

            if (!target.Subject.Alive || !HasCapacity(owner, target, now))
            {
                ReleaseAttack(owner.Id);
                return false;
            }

            // 枠の更新は毎フレーム行うため、既存の予約を再利用して不要な割り当てを避ける。
            if (!attacks.TryGetValue(owner.Id, out var lease))
                attacks[owner.Id] = lease = new AttackLease();
            lease.owner = owner;
            lease.target = target.Subject;
            lease.until = now + .45f;
            return true;
        }

        public static void ReleaseAttack(int owner)
        {
            if (attacks.TryGetValue(owner, out var lease))
                Revoke(lease);
            attacks.Remove(owner);
        }

        public static void Release(int owner)
        {
            ReleaseAttack(owner);
            lanes.Remove(owner);
        }

        /// <summary>
        /// NavMesh経路の次の折れ点へ進みながら、近傍の仲間の予測位置・攻撃中の立ち位置を避ける。
        /// 5本の横方向候補を比較し、通れる候補のみを選ぶ。狭い通路では無理に横へ広がらない。
        /// </summary>
        public static Vector3 TransitWaypoint(AIBody body, Vector3 corner, Vector3 goal, float now)
        {
            if (Vector3.Distance(Arena.Ground(body.Position), goal) < 5.5f)
                return corner;
            if (!lanes.TryGetValue(body.Id, out var plan))
                lanes[body.Id] = plan = new LanePlan();
            if (now < plan.until && (plan.goal - goal).sqrMagnitude < 4 && (body.Position - plan.point).sqrMagnitude > 2)
                return plan.point;
            Vector3 forward = corner - body.Position;
            forward.y = 0;
            float distance = Mathf.Min(5, forward.magnitude);
            if (distance < 1.2f || !NavigationWorld.Sample(body.Position, out var from))
                return corner;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float width = Mathf.Min(4, distance * .7f);
            float preferred = (Mathf.Abs(body.Id) % 5 - 2) * width * .5f;
            plan.neighbors.Clear();
            foreach (var neighbor in CrowdGrid.Neighbors(body, 9))
                if (neighbor.Faction == body.Faction && neighbor.Invader)
                    plan.neighbors.Add(neighbor);
            Vector3 best = corner;
            float bestCost = float.PositiveInfinity;
            for (int lane = -2; lane <= 2; lane++)
            {
                float offset = lane * width * .5f;
                Vector3 requested = Arena.Ground(body.Position + forward * distance + right * offset);
                if (!NavigationWorld.Sample(requested, out var candidate, 1) || NavMesh.Raycast(from, candidate, out _, NavMesh.AllAreas))
                    continue;
                float cost = Mathf.Abs(offset - preferred) * .8f;
                foreach (var neighbor in plan.neighbors)
                {
                    var intent = neighbor.Invader.Brain.Intent;
                    bool stationary = neighbor.Invader.frozen > 0 || intent.State == BrainState.Attack;
                    float speed = stationary ? 0 : neighbor.Invader.kind == 1 ? 4.8f : 2.6f;
                    Vector3 predicted = neighbor.Position + intent.Direction * speed * .5f;
                    Vector3 line = candidate - body.Position;
                    float t = Mathf.Clamp01(Vector3.Dot(predicted - body.Position, line) / Mathf.Max(.01f, line.sqrMagnitude));
                    float clearance = Vector3.Distance(body.Position + line * t, predicted);
                    cost += Mathf.Max(0, 2.3f - clearance) * (stationary ? 8 : 5);
                    if (stationary
                        && Vector3.Dot(neighbor.transform.forward, (candidate - neighbor.Position).normalized) > .8f
                        && Vector3.Distance(candidate, neighbor.Position) < 6)
                        cost += 4;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = candidate;
                }
            }

            plan.point = best;
            plan.goal = goal;
            plan.until = now + .55f;
            return best;
        }
    }
}
