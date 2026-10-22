using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wings.AI
{
    /// <summary>
    /// 認識情報の出所。自分の目視・過去の記憶・味方の報告を区別するための分類。
    /// </summary>
    public enum EvidenceSource
    {
        Sight,
        Memory,
        AllyReport,
        Damage,
        TowerAlert
    }

    // 戦闘候補の列挙と、AIが相手を知っていることは別。判断側にはセンサーの観測結果を渡す。

    /// <summary>
    /// キャラクターやデコイを、視覚センサーと身体回避から扱うための窓口。
    /// Registryは世界に存在する身体の一覧であり、AIが発見済みの敵一覧ではない。
    /// </summary>
    public sealed class AIBody : MonoBehaviour
    {
        /// <summary>
        /// センサーの候補列挙と近距離の身体回避に使う実体一覧。戦闘目標を直接選ぶ用途には使わない。
        /// </summary>
        public static readonly List<AIBody> Registry = new List<AIBody>();
        public int Faction; // 陣営番号: 味方とデコイ=1、侵略軍=2。
        public float Height = 1.8f, Radius = .5f;
        public PlayerAgent Pilot;
        public Enemy Invader;
        public Decoy Lure;
        public bool IsDecoy;
        public VisionSensor Sensor;
        public string DebugState = "";

        /// <summary>
        /// プレイヤーならダウンしていないこと、敵ならHP、デコイなら実体の存在を確認する。
        /// </summary>
        public bool Alive => Pilot ? !Pilot.Downed : Invader ? Invader.Health > 0 : !IsDecoy || Lure;
        public int Id => GetInstanceID();
        public Vector3 Position => transform.position;

        /// <summary>
        /// 目視情報として記録する胴体付近の代表位置。足元のPositionとは用途を分ける。
        /// </summary>
        public Vector3 Center => Position + Vector3.up * Height * .55f;

        /// <summary>
        /// Unityが身体を生成したときに登録する。陣営などの個別設定は生成元が続けて行う。
        /// </summary>
        void Awake()
        {
            Registry.Add(this);
        }

        /// <summary>
        /// 消滅した身体と、その身体が確保していた包囲位置を一覧から外す。
        /// </summary>
        void OnDestroy()
        {
            Registry.Remove(this);
            SquadBlackboard.Release(Id);
            EnemyCoordination.Release(Id);
        }

        /// <summary>
        /// Unityで選択中の身体の視野と認識情報を描く観察用処理。描画結果をAI判断へ戻すことはない。
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (Sensor == null)
                return;
            Gizmos.color = Faction == 1 ? Color.cyan : Color.red;
            Vector3 eye = Sensor.Eye;
            for (int i = 0; i <= 24; i++)
            {
                float angle = -Sensor.Settings.horizontalFov * .5f + Sensor.Settings.horizontalFov * i / 24;
                Vector3 point = eye + Quaternion.Euler(0, angle, 0) * transform.forward * Sensor.Settings.range;
                if (i == 0 || i == 24)
                    Gizmos.DrawLine(eye, point);
                if (i > 0)
                {
                    float last = -Sensor.Settings.horizontalFov * .5f + Sensor.Settings.horizontalFov * (i - 1) / 24;
                    Gizmos.DrawLine(eye + Quaternion.Euler(0, last, 0) * transform.forward * Sensor.Settings.range, point);
                }
            }

            foreach (var c in Sensor.Known)
            {
                Gizmos.color = c.Visible ? Color.green : Color.yellow;
                Gizmos.DrawLine(eye, c.Position);
                Gizmos.DrawWireSphere(c.Position, .35f);
            }
        }

        /// <summary>
        /// Play開始時に静的な一覧を初期化し、前回の実行情報を持ち越さない。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Registry.Clear();
            SquadBlackboard.Clear();
            CrowdGrid.Clear();
            EnemyCoordination.Clear();
        }
    }

    /// <summary>
    /// 一度の観測から得た敵情報。位置・観測時刻・信頼度・出所を一組にして扱う。
    /// Subjectは発砲直前の実在・視認確認に使う。見失った後の追跡には記録済みPositionを使う。
    /// </summary>
    public sealed class Contact
    {
        public int Id;
        public AIBody Subject;

        /// <summary>
        /// 最後に観測した位置。遮蔽中の相手の現在位置へ自動で更新しない。
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// SeenAtは元の目撃時刻、Confidenceは情報の鮮度。確率推論や学習による確信度ではない。
        /// </summary>
        public float SeenAt, Confidence;
        public bool Visible, IsDecoy;
        public EvidenceSource Source;

        /// <summary>
        /// 記録を値としてコピーする。後のセンサー更新で、捜索用の観測位置が書き換わるのを防ぐ。
        /// </summary>
        public Contact Snapshot() => new Contact
        {
            Id = Id,
            Subject = Subject,
            Position = Position,
            SeenAt = SeenAt,
            Confidence = Confidence,
            Visible = Visible,
            IsDecoy = IsDecoy,
            Source = Source
        };
    }

    /// <summary>
    /// 視認距離・水平/上下の視野・記憶時間・検査周期・遮蔽物レイヤーをまとめた設定。
    /// </summary>
    [Serializable]
    public sealed class PerceptionSettings
    {
        public float range = 32, horizontalFov = 120, verticalFov = 120;
        public float memorySeconds = 3, scanInterval = .12f;
        public int occlusionMask = Arena.WallMask;
    }

    /// <summary>
    /// 認識を作る担当。距離→視野角→遮蔽の幾何判定で発見し、見失った情報を期限付き記憶へ変える。
    /// 聴覚や学習モデルは使用しない。判断側はVisibleやBestMemoryを通して観測結果を受け取る。
    /// </summary>
    public sealed class VisionSensor
    {
        readonly AIBody owner;
        readonly Dictionary<int, Contact> memories = new Dictionary<int, Contact>();
        readonly List<Contact> visible = new List<Contact>();
        readonly List<int> expired = new List<int>();
        float nextScan;
        public readonly PerceptionSettings Settings = new PerceptionSettings();

        /// <summary>
        /// 直近のセンサー検査で目視できた相手。検査間に状況が変わるため、実際の発砲ではCanFireで再確認する。
        /// </summary>
        public IReadOnlyList<Contact> Visible => visible;

        /// <summary>
        /// 目視情報と失効前の記憶をまとめた一覧。主にデバッグ表示に使い、全件への射撃許可を意味しない。
        /// </summary>
        public IEnumerable<Contact> Known => memories.Values;

        public bool WallVision;
        public int ScanCount { get; private set; }
        public Vector3 Eye => owner.Position + Vector3.up * owner.Height * .82f;

        /// <summary>
        /// 共通設定を読み込み、個体IDで最初の検査時刻を少しずらす。同じフレームに検査が集中するのを緩和する。
        /// </summary>
        public VisionSensor(AIBody body)
        {
            owner = body;
            var config = AIConfiguration.Current;
            Settings.horizontalFov = config.allyFov;
            Settings.scanInterval = config.scanInterval;
            Settings.memorySeconds = config.memorySeconds;
            // 地形の尾根も実際に視界を遮る。壁の設定値にmapCの床レイヤーを必ず加える。
            Settings.occlusionMask = config.occlusionMask | Arena.TerrainMask;
            nextScan = Time.time + (Mathf.Abs(body.Id) % 7) * .015f;
        }

        /// <summary>
        /// 視認・記憶を捨て、次のTickで再検査する。復帰や検証開始時の情報リセットに使う。
        /// </summary>
        public void Clear()
        {
            memories.Clear();
            visible.Clear();
            nextScan = 0;
        }

        /// <summary>
        /// 一定周期で候補を検査し、現在の目視と過去の記憶を更新する。forceは検証で即時検査するための指定。
        /// 位置を更新できるのはCanSeeを通った相手だけ。見えない相手は古い位置を残して鮮度だけを下げる。
        /// </summary>
        public void Tick(float now, bool force = false)
        {
            if (!force && now < nextScan)
                return;
            nextScan = now + Settings.scanInterval;
            ScanCount++;
            visible.Clear();
            foreach (var c in memories.Values)
            {
                c.Visible = false;
                if (c.Source == EvidenceSource.Sight)
                    c.Source = EvidenceSource.Memory;
            }

            // 【候補と知識の境界】存在する身体を調べるのはこの段階だけ。
            // 陣営・生存状態・CanSeeの検査を通って初めて、判断側へ渡す目視情報になる。
            foreach (var candidate in AIBody.Registry)
            {
                if (!candidate || candidate == owner || candidate.Faction == owner.Faction || !candidate.Alive)
                    continue;
                if (!CanSee(candidate))
                    continue;
                if (!memories.TryGetValue(candidate.Id, out var c))
                    memories[candidate.Id] = c = new Contact
                    {
                        Id = candidate.Id
                    };
                // 【観測の更新】現在位置を保存するのは目視に成功したときだけ。
                // Subjectは実体の確認用で、見えない間に位置を読み直すための近道にはしない。
                c.Subject = candidate;
                c.Position = candidate.Center;
                c.SeenAt = now;
                c.Confidence = 1;
                c.Visible = true;
                c.IsDecoy = candidate.IsDecoy;
                c.Source = EvidenceSource.Sight;
                visible.Add(c);
            }

            // 【短期記憶】未再発見の相手の位置は固定し、経過秒数でConfidenceを1から0へ下げる。
            // これは情報の古さの尺度。削除は走査中に行わず、対象IDを集めてから行う。
            expired.Clear();
            foreach (var item in memories)
            {
                var c = item.Value;
                if (!c.Visible)
                {
                    c.Confidence = Mathf.Clamp01(1 - (now - c.SeenAt) / Settings.memorySeconds);
                    if (now - c.SeenAt >= Settings.memorySeconds)
                        expired.Add(item.Key);
                }
            }

            foreach (int id in expired)
                memories.Remove(id);
        }

        /// <summary>
        /// 現在の身体が視界内に入るかを調べる。距離と視野角を先に確認し、最後に物理的な遮蔽を検査する。
        /// タカの壁越し視認も距離・方向の条件は守り、遮蔽判定だけを省略する。
        /// </summary>
        public bool CanSee(AIBody subject, bool ignoreHorizontalFov = false)
        {
            if (!subject || !subject.Alive)
                return false;
            // 【視野モデル】中心までの距離、水平角、仰俯角の順に、安い計算で候補を絞る。
            // ここでは画像認識をせず、3D座標を幾何学的な視野へ通して認識を模擬する。
            Vector3 center = subject.Center, delta = center - Eye;
            if (delta.sqrMagnitude > Settings.range * Settings.range)
                return false;
            Vector3 flat = new Vector3(delta.x, 0, delta.z), forward = owner.transform.forward;
            forward.y = 0;
            if (!ignoreHorizontalFov && flat.sqrMagnitude > .001f && Vector3.Angle(forward, flat) > Settings.horizontalFov * .5f)
                return false;
            // フクロウの高空爆撃中は、水平面ではなく真下を基準にした視野。実際の距離・遮蔽は通常どおり検査する。
            float elevation = owner.Pilot && owner.Pilot.OwlBombarding
                ? Vector3.Angle(Vector3.down, delta)
                : Mathf.Atan2(Mathf.Abs(delta.y), Mathf.Max(.01f, flat.magnitude)) * Mathf.Rad2Deg;
            if (elevation > Settings.verticalFov * .5f)
                return false;
            // 能力による壁越し視認は遮蔽だけの例外。距離と視野角の検査を終えた後に適用する。
            if (WallVision)
                return true;
            // 胴・肩・頭の3点のいずれかが見えれば発見する。低い壁から頭が出る状況も扱う。
            foreach (float fraction in new[]
            {
                .35f,
                .58f,
                .85f
            }

            )
                if (!Physics.Linecast(Eye,
                    subject.Position + Vector3.up * subject.Height * fraction,
                    Settings.occlusionMask,
                    QueryTriggerInteraction.Ignore))
                    return true;
            return false;
        }

        /// <summary>
        /// 発砲の最終条件。現在も相手が見えること、武器射程内であること、銃口の射線が通ることを確認する。
        /// 頭だけが見えることと銃口から撃てることは別。記憶や味方報告だけの相手には許可を出さない。
        /// </summary>
        public bool CanFire(Contact c, float weaponRange, Vector3 muzzle, bool throughWall = false)
        {
            // 定期センサーの結果は検査間に古くなり得るため、射撃前にCanSeeをもう一度呼ぶ。
            // その後も狙いはContact.Positionへ向ける。遮蔽中の相手位置への自動照準は行わない。
            if (c == null || !c.Visible || !c.Subject || !CanSee(c.Subject))
                return false;
            // 狙う点は最後に観測した位置。隠れた相手の現在位置で記憶を更新しない。
            if (Vector3.Distance(muzzle, c.Position) > weaponRange)
                return false;
            return throughWall || !Physics.Linecast(muzzle, c.Position, Arena.OcclusionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 失効前の非目視情報から、最後に見た時刻が最も新しい記憶をコピーして返す。nowは現在時刻。
        /// </summary>
        public Contact BestMemory(float now)
        {
            Contact best = null;
            foreach (var c in memories.Values)
                if (!c.Visible && now - c.SeenAt < Settings.memorySeconds && (best == null || c.SeenAt > best.SeenAt))
                    best = c;
            return best?.Snapshot();
        }

        /// <summary>
        /// 被弾やタワー通知で許可された瞬間の位置を記録する。Visibleには入れず、射撃には通常の目視を必要とする。
        /// 値をコピーし、以後の相手のTransform移動で手がかりを更新しない。
        /// </summary>
        public Contact RememberEvent(AIBody subject, Vector3 position, float now, EvidenceSource source)
        {
            if (!subject || !subject.Alive || subject.Faction == owner.Faction)
                return null;
            if (source != EvidenceSource.Damage && source != EvidenceSource.TowerAlert)
                return null;
            var contact = new Contact
            {
                Id = subject.Id,
                Subject = subject,
                Position = position,
                SeenAt = now,
                Confidence = 1,
                Visible = false,
                IsDecoy = subject.IsDecoy,
                Source = source
            };
            if (memories.TryGetValue(subject.Id, out var previous))
                visible.Remove(previous);
            memories[subject.Id] = contact;
            nextScan = 0;
            return contact.Snapshot();
        }

        public Contact KnownContact(int id) => memories.TryGetValue(id, out var contact) ? contact.Snapshot() : null;
    }

    /// <summary>
    /// 協調を作るブラックボード。味方の目撃報告と、各個体の包囲位置の予約を共有する。
    /// 共有するのは期限付きの観測値と担当情報。敵の現在位置を常時配信する仕組みにはしない。
    /// </summary>
    public static class SquadBlackboard
    {
        /// <summary>
        /// 味方から届く観測値。位置と元の観測時刻だけを持ち、相手のTransformを持たせない。
        /// </summary>
        public sealed class Report
        {
            public int subject, sender;
            public Vector3 position;
            public float observedAt;
        }

        /// <summary>
        /// 誰が・どの陣営で・どの目標の周囲の何番を、いつまで使うかを表す期限付き予約。
        /// </summary>
        sealed class Reservation
        {
            public int owner, target, faction, slot;
            public float until;
        }

        static readonly Dictionary<int, Report> reports = new Dictionary<int, Report>();
        static readonly List<Reservation> reservations = new List<Reservation>();

        /// <summary>
        /// 任務をまたいで報告・位置予約が残らないよう、共有情報をまとめて初期化する。
        /// </summary>
        public static void Clear()
        {
            reports.Clear();
            reservations.Clear();
        }

        /// <summary>
        /// 直接の目視だけを報告する。伝聞を再報告して目撃時刻を新しくすることは認めない。
        /// </summary>
        public static void Publish(int sender, Contact sight)
        {
            if (sight == null || !sight.Visible || sight.Source != EvidenceSource.Sight)
                return;
            // 元の目撃時刻を保持する。受信や再送で情報の鮮度を復活させない。
            reports[sight.Id] = new Report
            {
                subject = sight.Id,
                sender = sender,
                position = sight.Position,
                observedAt = sight.SeenAt
            };
        }

        /// <summary>
        /// 他の味方から届いた期限内の報告のうち、最も新しいものをコピーして返す。捜索の手がかりとして使う。
        /// </summary>
        public static Report LatestReport(int receiver, float now, float maxAge = -1)
        {
            if (maxAge < 0)
                maxAge = AIConfiguration.Current.reportSeconds;
            Report best = null;
            foreach (var r in reports.Values)
                if (r.sender != receiver && now - r.observedAt < maxAge && (best == null || r.observedAt > best.observedAt))
                    best = r;
            return best == null ? null : new Report
            {
                subject = best.subject,
                sender = best.sender,
                position = best.position,
                observedAt = best.observedAt
            };
        }

        /// <summary>
        /// 指定目標に対して有効な予約を持つ、自分以外の個体数。目標選択の集中ペナルティに使う。
        /// </summary>
        public static int AssignedAttackers(int target, int except, float now)
        {
            int n = 0;
            foreach (var r in reservations)
                if (r.target == target && r.owner != except && r.until > now)
                    n++;
            return n;
        }

        /// <summary>
        /// 包囲位置の予約による協調。同じ目標へ向かう個体を円周上の別々の位置へ分散する。
        /// 同じ予約を更新して安定させ、空きがあれば近い位置を優先する。全て埋まった場合は重複も許容する。
        /// </summary>
        public static int ReserveSlot(int owner, int target, int faction, Vector3 position, Vector3 center, float radius, float now, int count = 8)
        {
            reservations.RemoveAll(r => r.until <= now);
            var existing = reservations.Find(r => r.owner == owner && r.target == target && r.faction == faction);
            // 同じ目標への担当が続く間は位置を取り直さず、期限だけ延長して移動先の揺れを抑える。
            if (existing != null)
            {
                existing.until = now + .8f;
                return existing.slot;
            }

            reservations.RemoveAll(r => r.owner == owner);
            int best = 0;
            float score = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                bool used = reservations.Exists(r => r.target == target && r.faction == faction && r.slot == i);
                // 近さの二乗をコストにし、使用中の位置には大きな罰点を足す。小さいコストほど選ばれやすい。
                // 空きがなくても選択自体は止めないため、これは完全な衝突防止保証ではない。
                float value = (position - Slot(center, radius, i, count)).sqrMagnitude + (used ? 10000 : 0);
                if (value < score)
                {
                    score = value;
                    best = i;
                }
            }

            reservations.Add(new Reservation { owner = owner, target = target, faction = faction, slot = best, until = now + .8f });
            return best;
        }

        /// <summary>
        /// 円周上の番号をマップ上の目的地へ変換する。通常8方向、タワーの包囲では12方向を使う。
        /// </summary>
        public static Vector3 Slot(Vector3 center, float radius, int slot, int count = 8) => Arena.Ground(center + Quaternion.Euler(0, slot * 360f / count, 0) * Vector3.forward * radius);

        /// <summary>
        /// 目標を失ったときや身体の消滅時に、自分が確保していた位置を解放する。
        /// </summary>
        public static void Release(int owner)
        {
            reservations.RemoveAll(r => r.owner == owner);
        }
    }
}
