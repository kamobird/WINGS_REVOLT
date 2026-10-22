using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Wings.AI;
using UnityEngine.AI;

namespace Wings
{
    /// <summary>
    /// 味方キャラの身体と能力の実行部。入力またはSquadBrainの指示を受け、同じ移動・攻撃処理を動かす。
    /// </summary>
    public sealed class PlayerAgent : MonoBehaviour
    {
        public CharacterDefinition Definition { get; private set; }

        public float Health, Ultimate = 35, Raiden = 100, BombTimer, SkillTimer, MoveTimer, ReloadTimer, Charge, UltimateTimer;
        public float TotalDamage;
        public int Kills, Ammo, ExplosiveShots, FullChargeShots, NovaShots;
        public float RespawnTimer { get; private set; }
        public bool Downed => Health <= 0;

        /// <summary>
        /// 現在の操作キャラかを判定する。操作対象以外の編成キャラはThinkでAIの指示を受ける。
        /// </summary>
        public bool Controlled => WingsGame.Instance && WingsGame.Instance.Active == this;
        public bool Accelerating { get; private set; }
        public int Magazine => Definition.bird == Bird.Penguin ? 36
            : Definition.bird == Bird.Hawk ? 6
            : Definition.bird == Bird.Crow ? 8
            : Definition.bird == Bird.Owl ? 10
            : 12;
        public Vector3 Aim { get; set; } = Vector3.forward;
        public Vector3 AimPoint { get; set; }
        public Transform Visual { get; private set; }
        public Decoy Decoy { get; private set; }

        /// <summary>
        /// 認識・身体回避のための窓口。見た目のモデルと、AIが参照する身体情報を分離する。
        /// </summary>
        public AIBody Identity { get; private set; }

        /// <summary>
        /// 認識と判断を担当する味方AI。実移動やダメージ適用はこのPlayerAgent側に残す。
        /// </summary>
        public SquadBrain Brain { get; private set; }

        CharacterController motor;
        Vector3 moveDirection;
        float vertical, fireTimer, invincible, aiCharge;
        bool busy;

        /// <summary>
        /// キャラ設定から身体・モデル・AIを生成する。モデルを差し替えてもCharacterControllerとAIはルートに残る。
        /// </summary>
        public static PlayerAgent Create(CharacterDefinition d, Vector3 at)
        {
            var go = new GameObject(d.displayName);
            go.layer = 9;
            go.transform.position = at;
            var p = go.AddComponent<PlayerAgent>();
            p.Definition = d;
            p.Health = d.health;
            p.Ammo = p.Magazine;
            p.motor = go.AddComponent<CharacterController>();
            p.motor.height = 1.85f;
            p.motor.radius = .42f;
            p.motor.center = Vector3.up * .93f;
            p.motor.stepOffset = .3f;
            p.Visual = WingsVisual.BirdModel(d, go.transform);
            p.AimPoint = at + Vector3.forward * 15;
            p.Identity = go.AddComponent<AIBody>();
            p.Identity.Faction = 1;
            p.Identity.Pilot = p;
            p.Identity.Radius = .42f;
            p.Brain = new SquadBrain(p, p.Identity);
            return p;
        }

        /// <summary>
        /// 戦闘中の毎フレーム更新。復帰、各タイマー、入力/AI、身体移動、向き・見た目の順に進める。
        /// </summary>
        void Update()
        {
            var g = WingsGame.Instance;
            if (!g || g.State != GameState.Playing)
                return;
            float dt = Time.deltaTime;
            if (Downed)
            {
                RespawnTimer -= dt;
                if (RespawnTimer <= 0)
                {
                    Health = Definition.health;
                    Teleport(new Vector3(Random.Range(-3, 3), 0, -4));
                    Visual.gameObject.SetActive(true);
                    Ammo = Magazine;
                    invincible = 3;
                    Brain.Reset();
                    g.Notify(Definition.displayName + "、戦線に復帰");
                }

                return;
            }

            fireTimer -= dt;
            invincible -= dt;
            BombTimer = Mathf.Max(0, BombTimer - dt);
            SkillTimer = Mathf.Max(0, SkillTimer - dt);
            MoveTimer = Mathf.Max(0, MoveTimer - dt);
            if (ReloadTimer > 0)
            {
                ReloadTimer -= dt;
                if (ReloadTimer <= 0)
                    Ammo = Magazine;
            }

            Ultimate = Mathf.Min(100, Ultimate + dt * .7f);
            if (UltimateTimer > 0)
            {
                UltimateTimer -= dt;
                if (UltimateTimer <= 0)
                    NovaShots = 0;
            }

            if (Definition.bird == Bird.Swallow)
            {
                if (Accelerating && UltimateTimer <= 0)
                {
                    Raiden = Mathf.Max(0, Raiden - 22 * dt);
                    if (Raiden <= 0)
                        Accelerating = false;
                }
                else
                    Raiden = Mathf.Min(100, Raiden + 14 * dt);
            }

            // 操作中なら入力、未操作ならAIへ振り分ける。自動検証では操作中のキャラもAIへ任せられる。
            if (Controlled)
            {
                if (g.ObservationMode)
                    Think();
                else if (!g.SmokeTesting)
                    Control();
                else if (g.VerificationAutopilot)
                    Think();
                else
                    moveDirection = Vector3.zero;
            }
            else
                Think();
            if (!busy)
            {
                float speed = Definition.speed * (Accelerating ? (UltimateTimer > 0 ? 2.8f : 2) : 1);
                if (Definition.bird == Bird.Penguin && UltimateTimer > 0)
                    speed *= .35f;
                if (motor.isGrounded && vertical < 0)
                    vertical = -2;
                vertical -= 22 * dt;
                motor.Move((moveDirection * speed + Vector3.up * vertical) * dt);
            }

            if (Aim.sqrMagnitude > .01f)
            {
                var facing = new Vector3(Aim.x, 0, Aim.z);
                if (facing.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), dt * 15);
            }

            if (Visual)
            {
                float lift = Definition.bird == Bird.Owl && NovaShots > 0 ? 3 : 0;
                Visual.localPosition = Vector3.up * (lift + Mathf.Sin(Time.time * (moveDirection.sqrMagnitude > .1f ? 16
                    : 3)) * (moveDirection.sqrMagnitude > .1f ? .06f
                    : .018f));
            }

            if (Ammo <= 0 && ReloadTimer <= 0)
                Reload();
        }

        /// <summary>
        /// 操作キャラの入力を移動・照準・各能力へ変換する。手動操作中も自分のセンサーで目視を報告する。
        /// </summary>
        void Control()
        {
            var g = WingsGame.Instance;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Brain.Observe(Time.time);
            if (g.CommandWheel)
            {
                moveDirection = Vector3.zero;
                Charge = 0;
                return;
            }

            Vector2 input = Vector2.zero;
            if (kb != null)
            {
                input.x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                input.y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            }

            moveDirection = Quaternion.Euler(0, g.CameraYaw, 0) * new Vector3(input.x, 0, input.y).normalized;
            var ray = g.Viewport.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            AimPoint = ray.origin + ray.direction * 60;
            if (Physics.Raycast(ray, out var hit, 80, ~(1 << 9), QueryTriggerInteraction.Ignore))
                AimPoint = hit.point;
            Aim = (AimPoint - (transform.position + Vector3.up * 1.2f)).normalized;
            if (mouse != null)
            {
                if (Definition.bird == Bird.Crow)
                {
                    if (mouse.leftButton.isPressed && ReloadTimer <= 0 && Ammo > 0)
                        Charge = Mathf.Min(1, Charge + Time.deltaTime / 1.3f);
                    if (mouse.leftButton.wasReleasedThisFrame && Charge > 0)
                    {
                        Fire(FullChargeShots > 0 ? 1 : Charge);
                        Charge = 0;
                    }
                }
                else if (mouse.leftButton.isPressed)
                    Fire();
            }

            if (kb == null)
                return;
            if (kb.qKey.wasPressedThisFrame)
                Skill();
            if (kb.eKey.wasPressedThisFrame)
                Bomb();
            if (kb.xKey.wasPressedThisFrame)
                UseUltimate();
            if (kb.leftShiftKey.wasPressedThisFrame)
                MoveAbility();
            if (kb.rKey.wasPressedThisFrame)
                Reload();
            if (kb.spaceKey.wasPressedThisFrame && motor.isGrounded && !busy)
                vertical = 8;
        }

        /// <summary>
        /// AIの行動指示を、手動操作と共通の移動・能力処理へ接続する。判断と実行を分ける境界。
        /// 能力要求は一度実行したら消費し、次の判断まで毎フレーム再発動しないようにする。
        /// </summary>
        void Think()
        {
            // 【判断→実行】BrainIntentから今回の操作量を取り出す。ここで新しい敵を探し直すことはしない。
            var intent = Brain.Tick(Time.time);
            moveDirection = intent.Direction;
            Aim = intent.Look.sqrMagnitude > .01f ? intent.Look : transform.forward;
            if (intent.Target != null)
                AimPoint = intent.Target.Position;
            if (intent.Move)
                MoveAbility();
            if (intent.Bomb)
                Bomb();
            if (intent.Skill)
                Skill();
            if (intent.Ultimate)
                UseUltimate();
            // 能力要求を一度で消費する。特にトグル式の加速やデコイ再発動が連続して反転するのを防ぐ。
            intent.Move = intent.Bomb = intent.Skill = intent.Ultimate = false;
            if (!intent.Fire)
            {
                aiCharge = 0;
                Charge = 0;
                return;
            }

            // カラスAIは発砲許可が続いている間だけ溜める。視認を失った場合は直前の分岐で溜めを破棄する。
            if (Definition.bird == Bird.Crow)
            {
                aiCharge += Time.deltaTime;
                Charge = FullChargeShots > 0 ? 1 : Mathf.Clamp01(aiCharge / 1.3f);
                if ((aiCharge >= 1.3f || FullChargeShots > 0) && ReloadTimer <= 0 && fireTimer <= 0)
                {
                    Fire(1);
                    aiCharge = 0;
                    Charge = 0;
                }
            }
            else
                Fire();
        }

        /// <summary>
        /// 発射条件と弾数を確認して、キャラ別の通常攻撃を生成する。カラスの溜めやフクロウのULT爆撃もここで実行する。
        /// </summary>
        public void Fire(float charge = 0)
        {
            if (Downed || busy || ReloadTimer > 0 || fireTimer > 0 || Ammo <= 0)
                return;
            if (NovaShots > 0)
            {
                NovaShots--;
                AreaField.Explode(AimGround(), 8, 240, this, 0, 0, true);
                fireTimer = .7f;
                if (NovaShots == 0)
                    UltimateTimer = 0;
                return;
            }

            if (Definition.bird == Bird.Penguin && UltimateTimer > 0)
                return;
            if (FullChargeShots > 0)
            {
                charge = 1;
                FullChargeShots--;
            }

            Ammo--;
            fireTimer = Definition.fireInterval;
            Vector3 muzzle = transform.position + Vector3.up * 1.2f + Aim * .7f;
            float damage = Definition.damage;
            Color c = Definition.color;
            switch (Definition.bird)
            {
                case Bird.Penguin:
                    Projectile.Fire(muzzle, Aim, 42, damage, 42, this, c, slow: 1.2f);
                    break;
                case Bird.Hawk:
                    BeamAttack(65, damage, .65f, UltimateTimer > 0, true);
                    break;
                case Bird.Crow:
                    float blast = ExplosiveShots > 0 ? 2 + charge * 3 : 0;
                    if (ExplosiveShots > 0)
                        ExplosiveShots--;
                    Projectile.Fire(muzzle,
                        Aim,
                        35 + charge * 18,
                        damage * (1 + charge * 3),
                        16 + charge * 38,
                        this,
                        c,
                        blast,
                        execute: charge >= .99f,
                        charge: charge);
                    break;
                case Bird.Owl:
                    Projectile.Fire(muzzle, Aim, 20, damage, 12, this, c, 3.8f);
                    break;
                case Bird.Swallow:
                    for (int i = 0; i < 7; i++)
                        Projectile.Fire(muzzle, Quaternion.Euler(0, (i - 3) * 4, 0) * Aim, 32, damage, 13, this, c);
                    break;
            }

            WingsGame.Instance.Sound(Definition.bird == Bird.Hawk ? 260 : 550, .045f, .045f);
        }

        /// <summary>
        /// 必要なときだけ装填時間を開始する。満装填になる処理はUpdateのタイマー終了時に行う。
        /// </summary>
        public void Reload()
        {
            if (Downed || ReloadTimer > 0 || Ammo >= Magazine)
                return;
            ReloadTimer = Definition.bird == Bird.Hawk ? 2.2f : 1.5f;
            Charge = 0;
        }

        /// <summary>
        /// 照準位置を地面へ投影し、キャラから22mまでの能力の着弾位置へ収める。
        /// </summary>
        public Vector3 AimGround()
        {
            var planar = Arena.Ground(AimPoint) - Arena.Ground(transform.position);
            return Arena.Ground(transform.position + Vector3.ClampMagnitude(planar, 22));
        }

        /// <summary>
        /// キャラ別のボムを使用する。タカの設置済みデコイの再発動は、新規設置のクールダウンより先に扱う。
        /// </summary>
        public void Bomb()
        {
            if (Downed || busy)
                return;
            if (Definition.bird == Bird.Hawk && Decoy)
            {
                Decoy.Tornado();
                Decoy = null;
                return;
            }

            if (BombTimer > 0)
                return;
            BombTimer = Definition.bombCooldown;
            switch (Definition.bird)
            {
                case Bird.Penguin:
                    AreaField.Create(AimGround(), 5, 7, 12, this, 1.2f);
                    break;
                case Bird.Hawk:
                    Decoy = Wings.Decoy.Create(AimGround(), this);
                    break;
                case Bird.Crow:
                    ExplosiveShots = 5;
                    break;
                case Bird.Owl:
                    for (int i = 0; i < 8; i++)
                        Projectile.Fire(transform.position + Vector3.up,
                            Quaternion.Euler(0, i * 45, 0) * Vector3.forward,
                            21,
                            48,
                            5,
                            this,
                            Definition.color,
                            3);
                    break;
                case Bird.Swallow:
                    StartCoroutine(SplitVolley());
                    break;
            }

            WingsGame.Instance.Sound(360, .1f, .1f);
        }

        /// <summary>
        /// ツバメの4連射を一定間隔で生成するコルーチン。途中でダウンしたら残りの射撃を中止する。
        /// </summary>
        IEnumerator SplitVolley()
        {
            for (int i = 0; i < 4; i++)
            {
                if (Downed)
                    yield break;
                Projectile.Fire(transform.position + Vector3.up, Aim, 26, 28, 9, this, Definition.color, 2, cluster: true);
                yield return new WaitForSeconds(.13f);
            }
        }

        /// <summary>
        /// キャラ別のスキルを実行する。ペンギンの氷壁は物理遮蔽に加え、NavMeshを切り抜く障害物にもする。
        /// </summary>
        public void Skill()
        {
            if (Downed || busy || SkillTimer > 0)
                return;
            if (Definition.bird == Bird.Swallow && Raiden < 40 && UltimateTimer <= 0)
            {
                if (Controlled)
                    WingsGame.Instance.Notify("ライデンゲージが40必要");
                return;
            }

            SkillTimer = Definition.skillCooldown;
            switch (Definition.bird)
            {
                case Bird.Penguin:
                    var forward = new Vector3(Aim.x, 0, Aim.z).normalized;
                    var wall = Arena.Block("Ice wall",
                        Arena.Ground(transform.position + forward * 3) + Vector3.up * 1.2f,
                        new Vector3(6, 2.4f, .8f),
                        Definition.color,
                        null);
                    wall.transform.rotation = Quaternion.LookRotation(forward);
                    Physics.SyncTransforms();
                    // 氷壁は視線を遮るだけでなく歩行経路も塞ぐ。carvingで既存NavMeshから占有部分を切り抜く。
                    var obstacle = wall.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = Vector3.one;
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = false;
                    Destroy(wall, 6);
                    break;
                case Bird.Hawk:
                    AreaField.Explode(transform.position, 5, 45, this);
                    foreach (var e in Enemy.All)
                        if (e && Vector3.Distance(e.transform.position, transform.position) < 6)
                            e.Push((e.transform.position - transform.position).normalized * 16);
                    vertical = 12;
                    invincible = .7f;
                    break;
                case Bird.Crow:
                    FullChargeShots = 5;
                    break;
                case Bird.Owl:
                    AreaField.Create(AimGround(), 6, 7, 48, this, meteor: true);
                    break;
                case Bird.Swallow:
                    if (UltimateTimer <= 0)
                        Raiden -= 40;
                    BeamAttack(UltimateTimer > 0 ? 55 : 34, UltimateTimer > 0 ? 220 : 130, UltimateTimer > 0 ? 1.7f : 1, true, false);
                    break;
            }

            WingsGame.Instance.Sound(700, .12f, .1f);
        }

        /// <summary>
        /// 移動能力の使用条件を確認し、ダッシュ・転移・ワイヤー・加速切替へ振り分ける。
        /// </summary>
        public void MoveAbility()
        {
            if (Downed || busy)
                return;
            if (Definition.bird == Bird.Swallow)
            {
                if (Accelerating)
                {
                    Accelerating = false;
                    return;
                }

                if (Raiden >= 10 || UltimateTimer > 0)
                    Accelerating = true;
                return;
            }

            if (MoveTimer > 0)
                return;
            Vector3 direction = moveDirection.sqrMagnitude > .1f ? moveDirection : new Vector3(Aim.x, 0, Aim.z).normalized;
            if (Definition.bird == Bird.Hawk)
            {
                if (!Physics.Raycast(transform.position + Vector3.up, Aim, out var hit, 25, Arena.OcclusionMask))
                {
                    if (Controlled)
                        WingsGame.Instance.Notify("ワイヤーを壁に向けて使用");
                    return;
                }

                var delta = Arena.Ground(hit.point) - Arena.Ground(transform.position);
                WingsVisual.Beam(transform.position + Vector3.up, hit.point, Definition.color, .08f);
                StartCoroutine(Dash(delta.normalized, Mathf.Max(0, delta.magnitude - 1), .27f));
            }
            else if (Definition.bird == Bird.Crow)
                StartCoroutine(Blink(direction));
            else
            {
                if (Definition.bird == Bird.Owl)
                {
                    AreaField.Explode(transform.position, 3, 32, this);
                    vertical = 6;
                }

                StartCoroutine(Dash(direction, Definition.bird == Bird.Owl ? 9 : 6, .2f));
            }

            MoveTimer = Definition.moveCooldown;
            invincible = .4f;
        }

        /// <summary>
        /// 指定時間に距離を分割してCharacterControllerを動かす。busyで通常移動との二重適用を防ぐ。
        /// </summary>
        IEnumerator Dash(Vector3 direction, float distance, float seconds)
        {
            busy = true;
            float elapsed = 0;
            while (elapsed < seconds)
            {
                float dt = Mathf.Min(Time.deltaTime, seconds - elapsed);
                motor.Move(direction * (distance / seconds * dt));
                elapsed += dt;
                yield return null;
            }

            busy = false;
        }

        /// <summary>
        /// 予備動作の後、遠い候補から順に壁と重ならない転移先を探す。
        /// </summary>
        IEnumerator Blink(Vector3 direction)
        {
            busy = true;
            WingsVisual.Ring(transform.position, 1.5f, Definition.color);
            yield return new WaitForSeconds(.28f);
            if (!Downed)
            {
                Vector3 target = Arena.Ground(transform.position + direction * 9);
                for (float n = 9; n >= 1; n -= .5f)
                {
                    target = Arena.Ground(transform.position + direction * n);
                    if (!Physics.CheckCapsule(target + Vector3.up * .5f, target + Vector3.up * 1.4f, .5f, Arena.OcclusionMask))
                    {
                        Teleport(target);
                        break;
                    }
                }

                WingsVisual.Burst(transform.position + Vector3.up, Definition.color);
            }

            busy = false;
        }

        /// <summary>
        /// ゲージを消費してキャラ別ULTを発動する。範囲効果の対象列挙は能力の実行であり、AIの認識情報とは別。
        /// </summary>
        public void UseUltimate()
        {
            if (Downed || busy || Ultimate < 100)
                return;
            Ultimate = 0;
            WingsGame.Instance.Notify(Definition.displayName
                + " : " + new[] { "この星は、守り抜く。", "狩りの時間だ。", "美しい終わりを。", "生徒には、手を出させない。", "雷より、速く！" }[(int)Definition.bird],
                3);
            switch (Definition.bird)
            {
                case Bird.Penguin:
                    StartCoroutine(AbsoluteZero());
                    break;
                case Bird.Hawk:
                    UltimateTimer = 12;
                    break;
                case Bird.Crow:
                    // カラスULTは仕様上の全敵効果。ここでの全実体への適用を、AIの目標認識には戻さない。
                    foreach (var e in Enemy.All.ToArray())
                        if (e)
                        {
                            WingsVisual.Beam(e.transform.position + Vector3.up * 15, e.transform.position, Definition.color, .15f);
                            e.Hit(Definition.damage * 4, this, 0, 0, Random.value < .3f);
                        }

                    break;
                case Bird.Owl:
                    NovaShots = 3;
                    UltimateTimer = 15;
                    invincible = 15;
                    break;
                case Bird.Swallow:
                    UltimateTimer = 12;
                    Raiden = 100;
                    Accelerating = true;
                    SkillTimer = 0;
                    break;
            }
        }

        /// <summary>
        /// 溜めの待機後に周囲へダメージと凍結を与える。待機中にダウンした場合は攻撃を出さない。
        /// </summary>
        IEnumerator AbsoluteZero()
        {
            UltimateTimer = 1.1f;
            WingsVisual.Ring(transform.position, 12, Definition.color, 1.1f);
            yield return new WaitForSeconds(1.1f);
            if (!Downed)
                AreaField.Explode(transform.position, 14, 200, this, 0, 4);
            UltimateTimer = 0;
        }

        /// <summary>
        /// 射線上の敵へビームの命中を適用する。貫通しない場合は壁と最初の命中で射程を止める。
        /// </summary>
        public void BeamAttack(float range, float damage, float width, bool throughWall, bool distanceBonus)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            if (!throughWall && Physics.Raycast(origin, Aim, out var wall, range, Arena.OcclusionMask))
                range = wall.distance;
            var targets = Enemy.All.FindAll(e => e && e.Health > 0);
            targets.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
            foreach (var e in targets)
            {
                Vector3 delta = e.transform.position + Vector3.up * (e.Boss ? 1.6f : 1) - origin;
                float along = Vector3.Dot(delta, Aim);
                if (along < 0 || along > range || (delta - Aim * along).magnitude > width + (e.Boss ? 1.1f : .6f))
                    continue;
                e.Hit(damage * (distanceBonus ? 1 + along / 30 : 1), this);
                if (!throughWall)
                {
                    range = along;
                    break;
                }
            }

            WingsVisual.Beam(origin, origin + Aim * range, Definition.color, width * .3f);
        }

        /// <summary>
        /// 無敵時間を考慮して被弾・ダウンを処理する。ダウン時は能力とAIの一時情報を解除して復帰待ちへ移る。
        /// </summary>
        public void Hit(float damage, AIBody attacker = null)
        {
            if (Downed || invincible > 0)
                return;
            Health = Mathf.Max(0, Health - damage);
            invincible = .35f;
            if (Health > 0)
                Brain.OnAttacked(attacker, Time.time);
            if (Health <= 0)
            {
                StopAllCoroutines();
                busy = false;
                Accelerating = false;
                Charge = 0;
                UltimateTimer = 0;
                NovaShots = 0;
                ReloadTimer = 0;
                motor.enabled = false;
                RespawnTimer = 12;
                Visual.gameObject.SetActive(false);
                Brain.Reset();
                Identity.DebugState = "Down / 復帰待ち";
                WingsGame.Instance.Notify(Definition.displayName + "がダウン。12秒で復帰");
            }
        }

        /// <summary>
        /// CharacterControllerを一時的に無効にして位置を設定し、通常の移動判定へ戻す。
        /// </summary>
        public void Teleport(Vector3 p)
        {
            motor.enabled = false;
            transform.position = p;
            motor.enabled = true;
            vertical = 0;
        }

        /// <summary>
        /// 操作切替時に手動とAIの溜めを解除し、別キャラへ溜め状態を持ち越さない。
        /// </summary>
        public void CancelCharge()
        {
            Charge = 0;
            aiCharge = 0;
        }
    }
}
