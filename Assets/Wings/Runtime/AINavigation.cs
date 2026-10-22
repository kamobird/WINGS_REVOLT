using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Wings.AI
{
    /// <summary>
    /// 身体を作るための共通の移動地図。実マップのColliderから任務開始時にNavMesh（歩ける領域）を生成する。
    /// 経路用の地図は既知情報として扱う。地図に敵の発見情報は含めない。
    /// </summary>
    public sealed class NavigationWorld : MonoBehaviour
    {
        public static NavigationWorld Instance { get; private set; }
        public NavMeshSurface Surface { get; private set; }

        /// <summary>
        /// 経路計算に使えるNavMeshデータが現在の任務に存在するかを返す。
        /// </summary>
        public static bool Ready => Instance && Instance.Surface && Instance.Surface.navMeshData;

        /// <summary>
        /// 前の地図を解除して、床と壁の物理形状を収集する。見た目だけの鳥モデルを経路の障害物にはしない。
        /// </summary>
        public static void Build(Transform arena)
        {
            if (Instance)
            {
                Instance.Surface.RemoveData();
                Instance.enabled = false;
            }

            var n = arena.gameObject.AddComponent<NavigationWorld>();
            Instance = n;
            n.Surface = arena.gameObject.AddComponent<NavMeshSurface>();
            // 【移動地図の生成】任務の子オブジェクトにある物理Colliderを収集する。
            // 床(Default)と壁(Wall)を対象にし、細かな領域を扱うためボクセル幅を0.16mに設定する。
            n.Surface.collectObjects = CollectObjects.Children;
            n.Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            n.Surface.layerMask = (1 << 0) | Arena.OcclusionMask;
            n.Surface.overrideVoxelSize = true;
            n.Surface.voxelSize = .16f;
            Physics.SyncTransforms();
            n.Surface.BuildNavMesh();
            Debug.Log("WINGS AI NAVMESH: " + (n.Surface.navMeshData ? "ready" : "failed"));
        }

        /// <summary>
        /// 指定位置の近くにある歩ける点を探す。失敗時は入力位置を返すが、戻り値falseで失敗も伝える。
        /// </summary>
        public static bool Sample(Vector3 p, out Vector3 at, float distance = 2)
        {
            bool ok = NavMesh.SamplePosition(p, out var hit, distance, NavMesh.AllAreas);
            at = ok ? hit.position : p;
            return ok;
        }

        /// <summary>
        /// 任務終了時にNavMeshを外し、次の任務から古い地図を参照しないようにする。
        /// </summary>
        void OnDestroy()
        {
            if (Surface)
                Surface.RemoveData();
            if (Instance == this)
                Instance = null;
        }
    }

    /// <summary>
    /// 個体ごとの経路追従。NavMeshの折れ点を順にたどり、近距離の壁回避と身体同士の分離を合成する。
    /// NavMeshAgentで移動させず方向だけを返し、実移動は既存のCharacterControllerへ任せる。
    /// </summary>
    public sealed class PathMotor
    {
        readonly NavMeshPath path = new NavMeshPath();
        Vector3[] corners = new Vector3[0];
        Vector3 lastGoal;
        int corner;
        float nextPath, stuckSince;
        Vector3 lastPosition;
        public bool HasPath { get; private set; }
        public int PathCalculations { get; private set; }
        public Vector3 Goal { get; private set; }
        public IReadOnlyList<Vector3> Corners => corners;

        /// <summary>
        /// キャッシュした経路を捨て、次のDirectionで経路を計算し直す。
        /// </summary>
        public void Reset()
        {
            nextPath = 0;
            corners = new Vector3[0];
            corner = 0;
            HasPath = false;
        }

        /// <summary>
        /// 目的地へ向かう今回の移動方向を計算する。経路計算は周期と目的地の変化に応じて行う。
        /// 経路を取得できない場合も直近の壁回避を使い、停滞時は再計算と横方向への試行を行う。
        /// </summary>
        public Vector3 Direction(AIBody body, Vector3 goal, float now, float radius, bool cooperative = false)
        {
            Goal = Arena.Ground(goal);
            Vector3 position = body.Position;
            if (now >= nextPath || (lastGoal - Goal).sqrMagnitude > 4)
            {
                // 【更新の分散】基準0.4秒に個体別の小さなずれを加え、全員の経路再計算の集中を抑える。
                // 目的地が2mより大きく変わった場合は、周期を待たずに計算する。
                nextPath = now + .4f + (Mathf.Abs(body.Id) % 5) * .025f;
                lastGoal = Goal;
                PathCalculations++;
                // 地図の準備、始点/終点の補正、経路計算、完全到達可能の4条件を確認する。
                // 途中までの経路は採用せず、その場合は後段の局所回避へ任せる。
                HasPath = NavigationWorld.Ready
                    && NavigationWorld.Sample(position, out var from)
                    && NavigationWorld.Sample(Goal, out var to)
                    && NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                corners = HasPath ? path.corners : new Vector3[0];
                corner = corners.Length > 1 ? 1 : 0;
            }

            // 【経路追従】近付いた折れ点を飛ばして、次の折れ点へ向かう。
            // 折れ点がなければ目的地そのものへ向かうため、経路取得失敗時にも移動方向は計算できる。
            while (corner < corners.Length - 1 && (Arena.Ground(corners[corner]) - Arena.Ground(position)).sqrMagnitude < .8f * .8f)
                corner++;
            Vector3 waypoint = corner < corners.Length ? corners[corner] : Goal;
            if (cooperative)
                waypoint = EnemyCoordination.TransitWaypoint(body, waypoint, Goal, now);
            Vector3 steering = Arena.Steer(position, waypoint, radius);
            steering = CrowdGrid.Steer(body, steering, radius, now);
            // 【停滞対策】一定距離進めたら記録を更新する。進めない状態が続けば再計算と左右への試行を行う。
            if ((position - lastPosition).sqrMagnitude > .12f)
            {
                stuckSince = now;
                lastPosition = position;
            }
            else if (now - stuckSince > 1.3f)
            {
                nextPath = 0;
                stuckSince = now;
                steering = Quaternion.Euler(0, body.Id % 2 == 0 ? 70 : -70, 0) * steering;
            }

            return steering;
        }
    }

    /// <summary>
    /// セルによる空間分割と分離操舵（separation）。近所の身体だけを調べ、重なりを避ける方向を加える。
    /// 近傍の身体位置は衝突回避専用。VisionSensorの発見・記憶へ書き込まない。
    /// </summary>
    public static class CrowdGrid
    {
        /// <summary>
        /// XZ平面を2.5m四方に区切る。身体の分離は周囲8セル、進行位置の協調は指定距離のセルを検索する。
        /// </summary>
        const float CellSize = 2.5f;
        static readonly Dictionary<Vector2Int, List<AIBody>> cells = new Dictionary<Vector2Int, List<AIBody>>();
        static int frame = -1;

        /// <summary>
        /// 任務変更時にセルの身体一覧と更新フレームを初期化する。
        /// </summary>
        public static void Clear()
        {
            cells.Clear();
            frame = -1;
        }

        /// <summary>
        /// マップ上の位置をセル番号へ変換する。高さYは使わず、床面上で近傍を検索する。
        /// </summary>
        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

        /// <summary>
        /// セル一覧は全個体で共有し、同じフレーム内では一度だけ作り直す。
        /// </summary>
        static void Rebuild()
        {
            if (frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            foreach (var cell in cells.Values)
                cell.Clear();
            foreach (var body in AIBody.Registry)
                if (body && body.Alive)
                {
                    var key = Cell(body.Position);
                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<AIBody>();
                    list.Add(body);
                }
        }

        /// <summary>
        /// 近い身体から離れる方向を本来の進行方向へ足す。壁越しの身体は回避対象から外す。
        /// これは簡易的な局所回避であり、全員の経路を最適化する群集シミュレーションではない。
        /// </summary>
        public static Vector3 Steer(AIBody body, Vector3 desired, float radius, float now)
        {
            Rebuild();
            Vector3 separation = Vector3.zero;
            int near = 0;
            Vector2Int key = Cell(body.Position);
            // 【近傍検索】周囲3×3セルの身体だけを調べる。遠くの全員を毎回比較するコストを避ける。
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (cells.TryGetValue(key + new Vector2Int(x, z), out var list))
                        foreach (var other in list)
                        {
                            if (!other || other == body || !other.Alive)
                                continue;
                            Vector3 delta = body.Position - other.Position;
                            delta.y = 0;
                            float distance = delta.magnitude;
                            if (distance > radius + other.Radius + 1.2f)
                                continue;
                            // 壁越しの身体では回避しない。近傍情報から戦闘相手を発見することもない。
                            if (Physics.Linecast(body.Center, other.Center, Arena.OcclusionMask))
                                continue;
                            if (distance < .01f)
                                delta = Quaternion.Euler(0, Mathf.Abs(body.Id % 360), 0) * Vector3.forward;
                            // 【分離操舵】近いほど強く離れる方向を合計する。これは身体の重なり回避で、攻撃対象の発見ではない。
                            separation += delta.normalized * Mathf.Clamp01((radius + other.Radius + 1.2f - distance) / 1.2f);
                            near++;
                        }

            Vector3 result = desired + separation * 1.8f;
            // 混雑時には個体IDによる小さな横向き成分も加え、正面同士で押し合う停滞を緩和する。
            if (near > 0 && desired.sqrMagnitude > .1f && separation.sqrMagnitude > .05f)
                result += Vector3.Cross(Vector3.up, desired) * ((body.Id % 2 == 0 ? 1 : -1) * .12f);
            return Vector3.ClampMagnitude(result, 1);
        }

        /// <summary>同じ空間分割を協調移動にも使う。近傍の身体情報を認識Contactへは変換しない。</summary>
        public static IEnumerable<AIBody> Neighbors(AIBody body, float radius)
        {
            Rebuild();
            Vector2Int key = Cell(body.Position);
            int reach = Mathf.CeilToInt(radius / CellSize);
            for (int x = -reach; x <= reach; x++)
                for (int z = -reach; z <= reach; z++)
                    if (cells.TryGetValue(key + new Vector2Int(x, z), out var list))
                        foreach (var other in list)
                            if (other
                                && other != body
                                && other.Alive
                                && Vector3.Distance(body.Position, other.Position) <= radius
                                && !Physics.Linecast(body.Center, other.Center, Arena.OcclusionMask))
                                yield return other;
        }
    }
}
