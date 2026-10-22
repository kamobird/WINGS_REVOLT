using System.Collections.Generic;
using UnityEngine;
using Wings.AI;

namespace Wings
{
    /// <summary>
    /// 敵の身体・被弾・攻撃を実行する。どこへ進み誰と戦うかはEnemyBrainへ委ねる。
    /// </summary>
    public sealed class Enemy : MonoBehaviour
    {
        /// <summary>
        /// 実体への命中や任務の残敵確認に使う一覧。EnemyBrainが未発見の相手を選ぶ用途には使わない。
        /// </summary>
        public static readonly List<Enemy> All = new List<Enemy>();
        public float Health, MaxHealth, frozen, slowed;
        public bool Boss => kind == 3;

        /// <summary>
        /// 敵種別。0=通常近接、1=高速近接、2=射撃型、3=大型ボス。能力値と判断条件の分岐に使う。
        /// </summary>
        public int kind;
        public AIBody Identity { get; private set; }
        public EnemyBrain Brain { get; private set; }

        CharacterController motor;
        float attackTimer, stagger;
        Vector3 knock;
        Renderer body;

        /// <summary>
        /// ウェーブに応じたHPと敵種別の身体を作り、敵用の認識・判断を結び付ける。
        /// </summary>
        public static Enemy Spawn(Vector3 at, int kind, int wave)
        {
            var go = new GameObject(kind == 3 ? "Siege commander" : "Invader");
            go.transform.position = Arena.Ground(at);
            var e = go.AddComponent<Enemy>();
            e.kind = kind;
            e.MaxHealth = e.Health = (kind == 3 ? 1500 : kind == 2 ? 160 : kind == 1 ? 75 : 100) * (1 + (wave - 1) * .16f);
            e.motor = go.AddComponent<CharacterController>();
            e.motor.height = kind == 3 ? 3.5f : 1.7f;
            e.motor.radius = kind == 3 ? 1.2f : .6f;
            e.motor.center = Vector3.up * e.motor.height * .5f;
            Color c = kind == 3 ? new Color(.9f, .15f, .3f) : kind == 2 ? new Color(1, .5f, .17f) : new Color(.72f, .22f, .28f);
            var model = WingsVisual.Shape("Armour",
                PrimitiveType.Cube,
                Vector3.up * (kind == 3 ? 1.6f
                : .85f),
                kind == 3 ? new Vector3(2.4f, 3, 2)
                : new Vector3(1, 1.4f, 1),
                c,
                go.transform);
            e.body = model.GetComponent<Renderer>();
            WingsVisual.Shape("Optic",
                PrimitiveType.Cube,
                new Vector3(0, kind == 3 ? 2.4f
                : 1.1f, kind == 3 ? 1.03f
                : .53f),
                new Vector3(kind == 3 ? 1.8f
                : .7f, .18f, .07f),
                new Color(1, .85f, .4f),
                go.transform);
            e.Identity = go.AddComponent<AIBody>();
            e.Identity.Faction = 2;
            e.Identity.Invader = e;
            e.Identity.Height = e.motor.height;
            e.Identity.Radius = e.motor.radius;
            e.Brain = new EnemyBrain(e, e.Identity);
            All.Add(e);
            WingsVisual.Ring(at, 1.2f, c, .8f);
            return e;
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>
        /// ダメージ、減速・凍結、即死/ボス追加ダメージを適用し、撃破時に成績と任務へ通知する。
        /// </summary>
        public void Hit(float damage, PlayerAgent source, float slow = 0, float freeze = 0, bool execute = false)
        {
            if (Health <= 0)
                return;
            if (execute)
                damage = Boss ? damage + 180 : Health;
            var actual = Mathf.Min(Health, damage);
            Health -= damage;
            slowed = Mathf.Max(slowed, slow);
            frozen = Mathf.Max(frozen, freeze);
            if (source)
            {
                source.TotalDamage += actual;
                source.Ultimate = Mathf.Min(100, source.Ultimate + actual * .035f);
            }

            WingsGame.Instance?.DamageText(transform.position + Vector3.up * 1.8f,
                Mathf.CeilToInt(actual).ToString(),
                source ? source.Definition.color
                : Color.white);
            if (Health <= 0)
            {
                if (source)
                    source.Kills++;
                WingsGame.Instance?.EnemyKilled(this);
                WingsVisual.Burst(transform.position + Vector3.up, body.sharedMaterial.color, Boss ? 26 : 7);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 一定時間、AI移動より優先するノックバックを設定する。
        /// </summary>
        public void Push(Vector3 v)
        {
            knock = v;
            stagger = .35f;
        }

        /// <summary>
        /// 凍結・ノックバックを先に処理し、通常時はEnemyBrainの方向と攻撃許可を実行する。
        /// </summary>
        void Update()
        {
            var game = WingsGame.Instance;
            if (!game || game.State != GameState.Playing || Health <= 0)
                return;
            float dt = Time.deltaTime;
            attackTimer -= dt;
            frozen -= dt;
            slowed -= dt;
            if (frozen > 0)
            {
                body.sharedMaterial = WingsVisual.Material(new Color(.4f, .85f, 1));
                return;
            }

            body.sharedMaterial = WingsVisual.Material(kind == 3 ? new Color(.9f, .15f, .3f)
                : kind == 2 ? new Color(1, .5f, .17f)
                : new Color(.72f, .22f, .28f));
            if (stagger > 0)
            {
                stagger -= dt;
                motor.Move(knock * dt + Vector3.down * dt);
                return;
            }

            // 【判断→身体】センサーとFSMが決めた方向を既存CharacterControllerへ渡す。ここで別の標的を探さない。
            var intent = Brain.Tick(Time.time);
            Vector3 dir = intent.Direction;
            motor.Move((dir * (kind == 1 ? 4.8f : Boss ? 1.8f : 2.6f) * (slowed > 0 ? .4f : 1) + Vector3.down * 2) * dt);
            var look = new Vector3(intent.Look.x, 0, intent.Look.z);
            if (look.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 7);
            if (intent.Fire && attackTimer <= 0)
            {
                attackTimer = Boss ? 1.8f : 1.3f;
                var subject = intent.Target?.Subject;
                if (kind == 2 && subject)
                {
                    Projectile.Fire(transform.position + Vector3.up,
                        (intent.Target.Position - transform.position - Vector3.up).normalized,
                        13,
                        14,
                        20,
                        null,
                        new Color(1, .3f, .2f),
                        hostile: true);
                }
                else if (subject && subject.Lure)
                    subject.Lure.Hit(Boss ? 45 : 15);
                else if (subject && subject.Pilot)
                    subject.Pilot.Hit(Boss ? 30 : 12);
                // 目標の実体がない攻撃許可はタワー向け。攻撃許可自体はEnemyBrain.Tickで確認済み。
                else
                    game.TowerHit(Boss ? 42 : 9);
                WingsVisual.Ring(transform.position, kind == 2 ? 11 : Boss ? 3.7f : 2, new Color(1, .2f, .25f), .22f);
            }
        }
    }

    /// <summary>
    /// 弾の実移動と命中処理。AIの判断から独立し、通過した区間に実際の身体があるかを検査する。
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public static int SpawnCount { get; private set; }

        Vector3 direction, origin;
        float speed, damage, range, travel, blast, slow, charge;
        PlayerAgent owner;
        bool hostile, pierce, execute, cluster;
        HashSet<Enemy> hit = new HashSet<Enemy>();

        /// <summary>
        /// 弾・軌跡を作り、射程、威力、貫通、爆発など今回の発射パラメーターを保持する。
        /// </summary>
        public static Projectile Fire(Vector3 at, Vector3 direction, float speed, float damage, float range, PlayerAgent owner, Color color, float blast = 0, float slow = 0, bool pierce = false, bool execute = false, float charge = 0, bool hostile = false, bool cluster = false)
        {
            var go = WingsVisual.Shape("Projectile", PrimitiveType.Sphere, at, Vector3.one * (hostile ? .28f : .2f), color);
            var p = go.AddComponent<Projectile>();
            p.origin = at;
            p.direction = direction.normalized;
            p.speed = speed;
            p.damage = damage;
            p.range = range;
            p.owner = owner;
            p.blast = blast;
            p.slow = slow;
            p.pierce = pierce;
            p.execute = execute;
            p.charge = charge;
            p.hostile = hostile;
            p.cluster = cluster;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = .12f;
            trail.startWidth = .16f;
            trail.endWidth = 0;
            trail.sharedMaterial = WingsVisual.Material(color);
            SpawnCount++;
            return p;
        }

        /// <summary>
        /// 今回の移動区間で壁と対象への命中を調べ、着弾か射程終了で爆発・分裂・消滅を行う。
        /// </summary>
        void Update()
        {
            var g = WingsGame.Instance;
            if (!g || g.State != GameState.Playing)
                return;
            float step = speed * Time.deltaTime;
            var from = transform.position;
            var to = from + direction * step;
            bool impact = false;
            if (!pierce && Physics.SphereCast(from, .12f, direction, out var wallHit, step, Arena.WallMask))
            {
                to = wallHit.point;
                impact = true;
            }

            // 点ではなく移動線分で判定し、高速弾がフレーム間に身体をすり抜けるのを防ぐ。
            if (hostile)
            {
                foreach (var p in g.Party)
                    if (p && !p.Downed && DistanceToSegment(p.transform.position + Vector3.up, from, to) < .65f)
                    {
                        p.Hit(damage);
                        impact = true;
                        break;
                    }

                if (!impact)
                    foreach (var b in AIBody.Registry)
                        if (b && b.IsDecoy && b.Lure && DistanceToSegment(b.Center, from, to) < .7f)
                        {
                            b.Lure.Hit(damage);
                            impact = true;
                            break;
                        }
            }
            else
            {
                foreach (var e in Enemy.All.ToArray())
                {
                    if (!e || e.Health <= 0 || hit.Contains(e))
                        continue;
                    if (DistanceToSegment(e.transform.position + Vector3.up * (e.Boss ? 1.6f : .9f), from, to) < (e.Boss ? 1.5f : .8f))
                    {
                        hit.Add(e);
                        e.Hit(damage, owner, slow, 0, execute && Random.value < .3f);
                        if (!pierce)
                        {
                            impact = true;
                            break;
                        }
                    }
                }
            }

            transform.position = to;
            travel += step;
            if (impact || travel >= range)
            {
                if (blast > 0)
                    AreaField.Explode(to, blast, damage, owner, slow, 0, owner && owner.Definition.bird == Bird.Owl);
                if (cluster && owner)
                    for (int i = 0; i < 5; i++)
                        Fire(to,
                            Quaternion.Euler(0, i * 72, 0) * Vector3.forward,
                            17,
                            damage * .5f,
                            3.5f,
                            owner,
                            owner.Definition.color,
                            1.5f);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 対象点と弾が進む線分の最短距離。フレーム間に高速弾が身体を飛び越えるのを検出する。
        /// </summary>
        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = b - a;
            float t = d.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude) : 0;
            return Vector3.Distance(p, a + d * t);
        }
    }

    /// <summary>
    /// 凍結フィールドや隕石など、一定時間・一定周期で範囲効果を発生させる実行部。
    /// </summary>
    public sealed class AreaField : MonoBehaviour
    {
        PlayerAgent owner;
        float radius, damage, slow, freeze, remaining, tick, interval;
        bool meteor;

        /// <summary>
        /// 効果の範囲・残り時間・発生周期を設定し、地面に範囲の仮表示を作る。
        /// </summary>
        public static AreaField Create(Vector3 at, float radius, float seconds, float damage, PlayerAgent owner, float slow = 0, float freeze = 0, bool meteor = false)
        {
            var go = new GameObject(meteor ? "Meteor field" : "Frost field");
            go.transform.position = Arena.Ground(at);
            var f = go.AddComponent<AreaField>();
            f.owner = owner;
            f.radius = radius;
            f.remaining = seconds;
            f.damage = damage;
            f.slow = slow;
            f.freeze = freeze;
            f.meteor = meteor;
            f.interval = meteor ? .85f : .5f;
            WingsVisual.Shape("Field footprint",
                PrimitiveType.Cylinder,
                Vector3.up * .035f,
                new Vector3(radius * 2, .025f, radius * 2),
                owner.Definition.color * .45f,
                go.transform);
            return f;
        }

        /// <summary>
        /// 範囲内に実在する敵へ効果を適用する。synergy時は巻き込み数に応じて威力を増やす。
        /// 全実体の列挙は着弾効果を適用するためであり、その結果をAIの目撃情報へは追加しない。
        /// </summary>
        public static int Explode(Vector3 at, float radius, float damage, PlayerAgent owner, float slow = 0, float freeze = 0, bool synergy = false)
        {
            // 範囲効果は物理的に届く相手を列挙して適用する。これは認識とは別のゲームルールの実行。
            var enemies = Enemy.All.FindAll(e => e
                && e.Health > 0
                && Vector3.Distance(Arena.Ground(e.transform.position), Arena.Ground(at)) <= radius + (e.Boss ? 1
                : 0));
            float bonus = synergy ? 1 + Mathf.Min(6, enemies.Count - 1) * .2f : 1;
            foreach (var e in enemies)
                e.Hit(damage * bonus, owner, slow, freeze);
            WingsVisual.Ring(at, radius, owner ? owner.Definition.color : Color.cyan);
            WingsVisual.Burst(at + Vector3.up, owner ? owner.Definition.color : Color.cyan, 12);
            WingsGame.Instance?.Sound(150 + radius * 20, .13f, .13f);
            return enemies.Count;
        }

        /// <summary>
        /// ポーズ中は進めず、効果の残り時間と次の発生までの時間を更新する。
        /// </summary>
        void Update()
        {
            if (!WingsGame.Instance || WingsGame.Instance.State != GameState.Playing)
                return;
            remaining -= Time.deltaTime;
            tick -= Time.deltaTime;
            if (tick <= 0)
            {
                tick = interval;
                var point = meteor ? transform.position + new Vector3(Random.Range(-radius * .5f, radius * .5f), 0, Random.Range(-radius * .5f, radius * .5f))
                    : transform.position;
                if (meteor)
                    WingsVisual.Beam(point + Vector3.up * 18, point, owner.Definition.color, .5f);
                Explode(point, meteor ? radius * .65f : radius, damage, owner, slow, freeze, meteor);
            }

            if (remaining <= 0)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// タカが設置する囮。敵はAIBodyを目視したときにだけ攻撃候補にできる。
    /// </summary>
    public sealed class Decoy : MonoBehaviour
    {
        static readonly List<Decoy> all = new List<Decoy>();
        public PlayerAgent Owner;
        float hp = 100, life = 12;

        /// <summary>
        /// 囮を味方陣営の身体として登録する。設置しただけで敵AIへ位置を通知することはない。
        /// </summary>
        public static Decoy Create(Vector3 p, PlayerAgent owner)
        {
            var go = WingsVisual.Shape("Wind decoy",
                PrimitiveType.Capsule,
                Arena.Ground(p) + Vector3.up,
                new Vector3(.8f, 1, .8f),
                owner.Definition.color);
            var d = go.AddComponent<Decoy>();
            d.Owner = owner;
            all.Add(d);
            var identity = go.AddComponent<AIBody>();
            identity.Faction = 1;
            identity.IsDecoy = true;
            identity.Lure = d;
            identity.Height = 1;
            return d;
        }

        /// <summary>
        /// 実体一覧から近い囮を探す補助関数。現在のEnemyBrainの目標選択には使用していない。
        /// </summary>
        public static Decoy Nearest(Vector3 p)
        {
            Decoy best = null;
            float dist = 17;
            foreach (var d in all)
                if (d)
                {
                    float n = Vector3.Distance(d.transform.position, p);
                    if (n < dist)
                    {
                        best = d;
                        dist = n;
                    }
                }

            return best;
        }

        /// <summary>
        /// 囮のHPを減らし、破壊されたら実体を消す。
        /// </summary>
        public void Hit(float damage)
        {
            hp -= damage;
            if (hp <= 0)
                Destroy(gameObject);
        }

        /// <summary>
        /// 再発動で竜巻フィールドと吹き飛ばしを発生させ、元の囮を消す。
        /// </summary>
        public void Tornado()
        {
            AreaField.Create(transform.position, 6, 3.5f, 28, Owner, 1.2f);
            foreach (var e in Enemy.All)
                if (e && Vector3.Distance(e.transform.position, transform.position) < 7)
                    e.Push((e.transform.position - transform.position).normalized * 10);
            Destroy(gameObject);
        }

        /// <summary>
        /// 戦闘中だけ囮の寿命を進め、時間切れで消す。
        /// </summary>
        void Update()
        {
            if (WingsGame.Instance && WingsGame.Instance.State == GameState.Playing)
            {
                life -= Time.deltaTime;
                if (life <= 0)
                    Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            all.Remove(this);
        }
    }
}
