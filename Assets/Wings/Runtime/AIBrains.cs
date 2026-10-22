using UnityEngine;

namespace Wings.AI
{
    /// <summary>
    /// 有限状態機械（FSM）の下位行動。Guard=警戒、Patrol=巡回、Regroup=合流、Pursue=接近、
    /// Reposition=位置調整、Attack=攻撃、Investigate=捜索、Reload=装填、Down=ダウン。
    /// 大命令Follow/Defend/Assaultとは別の分類。Downは定義のみで、実際のダウン処理はPlayerAgentが担当する。
    /// </summary>
    public enum BrainState
    {
        Guard,
        Patrol,
        Regroup,
        Pursue,
        Reposition,
        Attack,
        Investigate,
        Reload,
        Down
    }

    /// <summary>
    /// 判断側が実行側へ渡す行動指示。移動・照準・発砲許可・能力の使用要求をまとめる。
    /// Brainが直接キャラクターを動かさず、PlayerAgentやEnemyがこの指示を既存の操作処理へ接続する。
    /// </summary>
    public sealed class BrainIntent
    {
        public BrainState State;
        public string Reason;

        /// <summary>
        /// Destination=移動先、Direction=今フレームの移動方向、Look=向きたい方向。目的地と操作量を分けて持つ。
        /// </summary>
        public Vector3 Destination, Direction, Look;

        /// <summary>
        /// 直接視認から選ばれた攻撃候補。記憶・報告の捜索ではnullのままにする。
        /// </summary>
        public Contact Target;

        /// <summary>
        /// Fireは毎フレーム再検査する発砲許可。能力の4フラグはPlayerAgentが実行後に一度だけ消費する。
        /// </summary>
        public bool Fire, Skill, Bomb, Ultimate, Move;
    }

    /// <summary>
    /// 味方AIの判断部。大命令の範囲制限＋優先順位付きルール＋FSMで行動を決める。
    /// 攻撃目標だけはユーティリティ評価（候補を点数化して比較）で選ぶ。GOAPによる計画や機械学習は使わない。
    /// 読む順序はTick→Observe→Decide→ChooseTarget/PlanAbilities。身体の実行はPlayerAgent.Thinkへ渡す。
    /// </summary>
    public sealed class SquadBrain
    {
        readonly PlayerAgent pilot;
        readonly AIBody body;

        /// <summary>
        /// 判断に使える相手情報を作る視覚センサー。未発見の敵を世界一覧から直接選ぶことはしない。
        /// </summary>
        public readonly VisionSensor Sensor;

        /// <summary>
        /// 目的地までの経路と局所回避を計算する移動部。判断部は行き先、移動部は進む方向を担当する。
        /// </summary>
        public readonly PathMotor Navigation = new PathMotor();
        public BrainIntent Intent { get; private set; } = new BrainIntent();

        /// <summary>
        /// 追従・防衛で基準点から離れてよい距離。leashは行動範囲をつなぎ留める制限を指す。
        /// </summary>
        public float FollowLeash = 12, DefenseLeash = 11;

        /// <summary>
        /// 武器の得意な戦闘距離。目標評価と、目標周囲の包囲位置の半径に使う。射程の上限とは別。
        /// </summary>
        public float PreferredRange => pilot.Definition.bird == Bird.Hawk ? 24
            : pilot.Definition.bird == Bird.Crow ? 19
            : pilot.Definition.bird == Bird.Owl ? 9
            : pilot.Definition.bird == Bird.Swallow ? 6
            : 14;

        /// <summary>
        /// CanFireへ渡すAIの射程確認値。実際の弾やビームの生成・命中処理はPlayerAgentが担当する。
        /// </summary>
        public float WeaponRange => pilot.Definition.bird == Bird.Hawk ? 65
            : pilot.Definition.bird == Bird.Crow ? 54
            : pilot.Definition.bird == Bird.Owl ? 14
            : pilot.Definition.bird == Bird.Swallow ? 13
            : 42;

        float nextDecision, lastTargetChanged, nextLog, investigatedAt;
        int currentTarget, patrolStep, lastInvestigated;
        SquadOrder lastOrder;
        BrainState lastState;

        /// <summary>
        /// キャラと身体を結び、キャラ別の視認距離と共通の行動範囲を設定する。
        /// </summary>
        public SquadBrain(PlayerAgent owner, AIBody identity)
        {
            pilot = owner;
            body = identity;
            Sensor = new VisionSensor(identity);
            Sensor.Settings.range = owner.Definition.bird == Bird.Hawk ? 55
                : owner.Definition.bird == Bird.Crow ? 43
                : owner.Definition.bird == Bird.Swallow ? 25
                : 34;
            identity.Sensor = Sensor;
            FollowLeash = AIConfiguration.Current.followLeash;
            DefenseLeash = AIConfiguration.Current.defenseLeash;
        }

        /// <summary>
        /// 復帰時などに認識・経路・予約・現在目標を初期化し、新しい行動判断を予約する。
        /// </summary>
        public void Reset()
        {
            Sensor.Clear();
            Navigation.Reset();
            SquadBlackboard.Release(body.Id);
            currentTarget = 0;
            nextDecision = 0;
            Intent = new BrainIntent();
        }

        /// <summary>
        /// 命令の基準点。追従中は操作キャラの位置、その他は原点にあるタワーを使う。
        /// リーダーやタワー位置は味方通信・任務情報として既知。未発見の敵位置とは扱いを分ける。
        /// </summary>
        public Vector3 Anchor()
        {
            var g = WingsGame.Instance;
            return g.Order == SquadOrder.Follow
                && g.Active
                && g.Active != pilot
                && !g.Active.Downed ? Arena.Ground(g.Active.transform.position)
                : Vector3.zero;
        }

        /// <summary>
        /// 目的地を命令の許可範囲へ収める。攻撃命令にはこの制限を掛けず、マップ内への補正だけを行う。
        /// </summary>
        public Vector3 ClampToOrder(Vector3 destination)
        {
            var g = WingsGame.Instance;
            destination = Arena.Ground(destination);
            if (g.Order == SquadOrder.Assault)
                return destination;
            var anchor = Anchor();
            float limit = g.Order == SquadOrder.Follow ? FollowLeash - 1 : DefenseLeash - 1;
            return Arena.Ground(anchor + Vector3.ClampMagnitude(destination - anchor, limit));
        }

        /// <summary>
        /// 編成番号を使って別々の待機位置を割り当てる。追従はリーダー後方、防衛はタワー周囲に配置する。
        /// </summary>
        Vector3 Formation()
        {
            var g = WingsGame.Instance;
            int index = Mathf.Max(0, g.Party.IndexOf(pilot));
            if (g.Order == SquadOrder.Follow)
            {
                var rotation = g.Active ? g.Active.transform.rotation : Quaternion.identity;
                return ClampToOrder(Anchor() + rotation * new Vector3((index % 3 - 1) * 2.5f, 0, -3.5f - (index == 3 ? 2 : 0)));
            }

            return Quaternion.Euler(0, index * 90 + 45, 0) * Vector3.forward * 6;
        }

        /// <summary>
        /// ユーティリティベースの目標選択。目視中の敵だけを、適正距離・タワーへの接近・担当の集中で点数化する。
        /// 目標保持時間と切替に必要な点差（ヒステリシス）を設け、わずかな点数差で狙いが揺れるのを抑える。
        /// </summary>
        Contact ChooseTarget(float now)
        {
            Contact best = null;
            float bestScore = float.NegativeInfinity, currentScore = float.NegativeInfinity;
            foreach (var contact in Sensor.Visible)
            {
                float distance = Vector3.Distance(body.Position, contact.Position);
                // 【効用評価】45は基礎点。適正距離との差を引き、タワーに近い敵へ最大25点を加える。
                // 原点がタワーなので、観測位置の原点からの距離を接近度の指標にできる。
                float score = 45 - Mathf.Abs(distance - PreferredRange) + Mathf.Clamp01(1 - Arena.Ground(contact.Position).magnitude / 28) * 25;
                // 【役割分散】既に味方が担当する相手は1体あたり8点を引く。近い敵だけへの過集中を緩和する。
                score -= SquadBlackboard.AssignedAttackers(contact.Id, body.Id, now) * 8;
                if (contact.Id == currentTarget)
                    currentScore = score;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = contact;
                }
            }

            // 【ヒステリシス】保持時間中、または点差が小さい間は現在目標を維持する。
            // ただし維持できるのは、現在目標が引き続きVisibleに含まれる場合だけ。
            if (best != null
                && currentTarget != 0
                && best.Id != currentTarget
                && (now - lastTargetChanged < AIConfiguration.Current.targetHoldSeconds
                || bestScore < currentScore + AIConfiguration.Current.targetSwitchAdvantage))
                foreach (var contact in Sensor.Visible)
                    if (contact.Id == currentTarget)
                        return contact;
            if (best != null && best.Id != currentTarget)
            {
                currentTarget = best.Id;
                lastTargetChanged = now;
            }

            return best;
        }

        /// <summary>
        /// 味方AIの入口。認識→一定周期の行動選択→毎フレームの移動・発砲条件の確認、の順で進む。
        /// 行動選択を間引いても、範囲外へ進まない制御と発砲の安全確認は毎フレーム更新する。
        /// </summary>
        public BrainIntent Tick(float now)
        {
            var g = WingsGame.Instance;
            Observe(now);
            if (now >= nextDecision || lastOrder != g.Order)
            {
                nextDecision = now + .15f;
                lastOrder = g.Order;
                Decide(now);
            }

            // 行動選択は周期更新だが、移動と射撃の条件は毎フレーム確認する。
            Intent.Destination = ClampToOrder(Intent.Destination);
            float distance = Vector3.Distance(Arena.Ground(body.Position), Intent.Destination);
            Intent.Direction = distance > .8f ? Navigation.Direction(body, Intent.Destination, now, .48f)
                : CrowdGrid.Steer(body, Vector3.zero, .48f, now);
            // 【毎フレームの範囲制御】目的地を丸めるだけでは、局所回避で外へ逸れることがある。
            // 範囲の縁では外向きの方向成分を取り除き、接線方向や帰還方向の動きは残す。
            if (g.Order != SquadOrder.Assault)
            {
                var away = Arena.Ground(body.Position) - Anchor();
                float limit = g.Order == SquadOrder.Follow ? FollowLeash : DefenseLeash;
                if (away.magnitude >= limit - .6f && Vector3.Dot(Intent.Direction, away) > 0)
                    Intent.Direction = Vector3.ProjectOnPlane(Intent.Direction, away.normalized);
            }

            Intent.Fire = Intent.Target != null
                && Sensor.CanFire(Intent.Target, WeaponRange, body.Position + Vector3.up * 1.2f, Sensor.WallVision);
            if (pilot.ReloadTimer > 0)
                Intent.Fire = false;
            body.DebugState = Intent.State + " / " + Intent.Reason;
            return Intent;
        }

        /// <summary>
        /// 自分で目視を更新し、その結果だけをブラックボードへ報告する。手動操作中のキャラからも呼ばれる。
        /// </summary>
        public void Observe(float now)
        {
            Sensor.WallVision = pilot.Definition.bird == Bird.Hawk && pilot.UltimateTimer > 0;
            Sensor.Tick(now);
            foreach (var c in Sensor.Visible)
                SquadBlackboard.Publish(body.Id, c);
        }

        /// <summary>
        /// 優先順位付きのルールでFSMの次の状態を決める。まず命令範囲への帰還、次に目視した敵への対応を選ぶ。
        /// 敵が見えない場合は記憶・報告の捜索へ進み、手がかりもなければ巡回または命令の待機位置へ戻る。
        /// </summary>
        void Decide(float now)
        {
            // 【FSMの選択順】基本の待機指示を作り、優先する条件で上書きする。
            // 帰還→目視相手への位置調整/攻撃→記憶の捜索→巡回/警戒、の順。
            var g = WingsGame.Instance;
            var next = new BrainIntent
            {
                Destination = Formation(),
                State = BrainState.Guard,
                Reason = "防衛位置を維持"
            };
            var anchor = Anchor();
            float homeDistance = Vector3.Distance(Arena.Ground(body.Position), anchor);
            // 追従/防衛の許容距離を超えたら敵より帰還を優先する。帰還中も直接見える敵の発砲確認は続ける。
            bool regroup = g.Order == SquadOrder.Follow
                && homeDistance > FollowLeash
                || g.Order == SquadOrder.Defend
                && homeDistance > DefenseLeash;
            Contact target = ChooseTarget(now);
            if (regroup)
            {
                next.State = BrainState.Regroup;
                next.Reason = "命令の行動範囲へ帰還を最優先";
                next.Destination = Formation();
                next.Target = target;
            }
            else if (target != null)
            {
                next.Target = target;
                float distance = Vector3.Distance(body.Position, target.Position);
                bool clear = Sensor.CanFire(target, WeaponRange, body.Position + Vector3.up * 1.2f, Sensor.WallVision);
                // 【協調】適正距離の円周上に攻撃位置を予約する。全員が敵の中心へ直進するのを避ける。
                int slot = SquadBlackboard.ReserveSlot(body.Id, target.Id, body.Faction, body.Position, target.Position, PreferredRange, now);
                var attackPosition = SquadBlackboard.Slot(target.Position, PreferredRange, slot);
                if (!clear)
                {
                    next.State = BrainState.Reposition;
                    next.Reason = "認識済みだが銃口の射線・射程が不足";
                    next.Destination = attackPosition;
                }
                else if (distance < PreferredRange * .5f)
                {
                    next.State = BrainState.Reposition;
                    next.Reason = "武器の適正距離まで離脱";
                    next.Destination = attackPosition;
                }
                else if (g.Order == SquadOrder.Assault && distance > PreferredRange * 1.3f)
                {
                    next.State = BrainState.Pursue;
                    next.Reason = "目視した敵へ接近";
                    next.Destination = attackPosition;
                }
                else if (g.Order == SquadOrder.Follow && homeDistance > 7)
                {
                    next.State = BrainState.Regroup;
                    next.Reason = "射撃しながら編隊へ戻る";
                    next.Destination = Formation();
                }
                else
                {
                    next.State = BrainState.Attack;
                    next.Reason = "視界・射線・射程を確認して攻撃";
                    next.Destination = Arena.Ground(body.Position);
                }
            }
            else
            {
                SquadBlackboard.Release(body.Id);
                currentTarget = 0;
                // 【知識の使い分け】直接の目視がないときだけ、過去の目撃と味方の報告を調べる。
                // これは捜索先の候補であり、next.Targetには入れない。発砲許可へ昇格させないための境界。
                Contact memory = Sensor.BestMemory(now);
                var report = SquadBlackboard.LatestReport(body.Id, now);
                Vector3 evidence = Vector3.zero;
                float observedAt = 0, trust = 0;
                int evidenceId = 0;
                bool hasEvidence = false, fromReport = false;
                if (memory != null && memory.Confidence >= .2f)
                {
                    evidence = memory.Position;
                    observedAt = memory.SeenAt;
                    evidenceId = memory.Id;
                    trust = memory.Confidence;
                    hasEvidence = true;
                }

                // 報告は直接の目視より弱い0.65を上限にし、元の目撃時刻から線形に鮮度を下げる。
                // 記憶と最新報告を比較し、0.2以上かつより信頼度の高い手がかりを選ぶ。
                float reportTrust = report == null ? 0
                    : .65f * Mathf.Clamp01(1 - (now - report.observedAt) / AIConfiguration.Current.reportSeconds);
                if (report != null && reportTrust >= .2f && reportTrust > trust)
                {
                    evidence = report.position;
                    observedAt = report.observedAt;
                    evidenceId = report.subject;
                    hasEvidence = true;
                    fromReport = true;
                }

                // 同じ観測を到着後に何度も調べ直さない。対象IDか観測時刻が更新されたときだけ再捜索する。
                if (hasEvidence
                    && (evidenceId != lastInvestigated
                    || observedAt > investigatedAt)
                    && Vector3.Distance(body.Position, evidence) > 2)
                {
                    next.State = BrainState.Investigate;
                    next.Reason = fromReport ? "味方の報告地点を確認（再発見まで射撃禁止）" : "最後に見た地点を確認（射撃禁止）";
                    next.Destination = evidence;
                }
                else
                {
                    if (hasEvidence)
                    {
                        lastInvestigated = evidenceId;
                        investigatedAt = observedAt;
                    }

                    // 未発見の敵位置を巡回先に使わず、決まった円周上の点を順番に調べる。個体IDで開始方向を分散する。
                    if (g.Order == SquadOrder.Assault)
                    {
                        next.State = BrainState.Patrol;
                        next.Reason = "敵を知らないため巡回して捜索";
                        float angle = patrolStep * 72 + (Mathf.Abs(body.Id) % 5) * 72;
                        var point = Quaternion.Euler(0, angle, 0) * Vector3.forward * Arena.PatrolRadius;
                        next.Destination = point;
                        if (Vector3.Distance(body.Position, point) < 2)
                            patrolStep++;
                    }
                    else
                    {
                        next.State = g.Order == SquadOrder.Follow ? BrainState.Regroup : BrainState.Guard;
                        next.Reason = g.Order == SquadOrder.Follow ? "編隊間隔を保って追従" : "拠点の周囲を警戒";
                    }
                }
            }

            next.Destination = ClampToOrder(next.Destination);
            // 装填中は状態表示をReloadにするが、既に選んだ移動先を残す。攻撃不能でも帰還や回避は続ける。
            if (pilot.ReloadTimer > 0)
            {
                next.State = BrainState.Reload;
                next.Reason = "装填しながら命令内の移動を継続";
            }

            if (next.Target != null)
            {
                next.Look = (next.Target.Position - body.Center).normalized;
                PlanAbilities(next);
            }
            else if ((next.Destination - Arena.Ground(body.Position)).sqrMagnitude > 2)
                next.Look = (next.Destination - Arena.Ground(body.Position)).normalized;
            else
                next.Look = Quaternion.Euler(0, now * 42 + (Mathf.Abs(body.Id) % 4) * 90, 0) * Vector3.forward;
            if (g.Order == SquadOrder.Follow && homeDistance > 8 && pilot.Definition.bird == Bird.Swallow)
                next.Move = !pilot.Accelerating && pilot.Raiden > 25;
            else if (pilot.Definition.bird == Bird.Swallow && pilot.Accelerating && next.State != BrainState.Pursue)
                next.Move = true;
            Intent = next;
            if (lastState != next.State && now >= nextLog)
            {
                Debug.Log("WINGS ALLY AI " + pilot.Definition.displayName
                    + " order=" + g.Order
                    + " state=" + next.State
                    + " visible=" + Sensor.Visible.Count
                    + " target=" + (next.Target?.Id ?? 0)
                    + " reason=" + next.Reason);
                lastState = next.State;
                nextLog = now + .7f;
            }
        }

        /// <summary>
        /// ルールベースの能力選択。目視中の敵の距離と集まり方、自分のキャラ・資源・強化残数から使用要求を出す。
        /// ここでは使用したいかを決め、クールダウンや消費を含む実際の使用可否はPlayerAgentで再確認する。
        /// </summary>
        void PlanAbilities(BrainIntent intent)
        {
            float distance = Vector3.Distance(body.Position, intent.Target.Position);
            int clustered = 0, close = 0;
            // clusteredは目標付近の目視敵数、closeは自分の近くの目視敵数。世界全体の敵数は使わない。
            foreach (var c in Sensor.Visible)
            {
                if (Vector3.Distance(c.Position, intent.Target.Position) < 5)
                    clustered++;
                if (Vector3.Distance(c.Position, body.Position) < 5)
                    close++;
            }

            bool engage = Sensor.CanFire(intent.Target, WeaponRange, body.Position + Vector3.up * 1.2f, Sensor.WallVision);
            if (!engage || intent.State == BrainState.Regroup)
                return;
            // 【キャラ別ルール】範囲攻撃は集団、近距離の拒否能力は接近した敵を基準にする。
            // 要求フラグを出すだけなので、既にクールダウン中なら実行側が使用を拒否する。
            switch (pilot.Definition.bird)
            {
                case Bird.Penguin:
                    intent.Bomb = distance < 22 && clustered >= 2;
                    intent.Skill = close >= 2 && distance < 6;
                    intent.Ultimate = close >= 3;
                    break;
                case Bird.Hawk:
                    int lureTargets = 0;
                    if (pilot.Decoy)
                        foreach (var c in Sensor.Visible)
                            if (Vector3.Distance(c.Position, pilot.Decoy.transform.position) < 6)
                                lureTargets++;
                    intent.Bomb = pilot.Decoy ? lureTargets >= 2 : distance < 18 && clustered >= 2;
                    intent.Skill = close >= 1;
                    intent.Ultimate = clustered >= 3;
                    break;
                case Bird.Crow:
                    intent.Bomb = pilot.ExplosiveShots == 0 && clustered >= 2;
                    intent.Skill = pilot.FullChargeShots == 0;
                    intent.Ultimate = Sensor.Visible.Count >= 5;
                    break;
                case Bird.Owl:
                    intent.Bomb = close >= 2;
                    intent.Skill = distance < 22 && clustered >= 2;
                    intent.Ultimate = clustered >= 3;
                    break;
                case Bird.Swallow:
                    intent.Bomb = distance < 13 && clustered >= 2;
                    intent.Skill = distance < 32 && clustered >= 2 && pilot.Raiden >= 55;
                    intent.Ultimate = close >= 2;
                    break;
            }
        }
    }

    /// <summary>
    /// 敵AIの判断部。タワーへ向かう任務を基本に、目視した相手への交戦と短時間の捜索をFSMで切り替える。
    /// タワーの位置は既知の任務情報。プレイヤーやデコイは視覚センサーで発見したものだけを選ぶ。
    /// </summary>
    public sealed class EnemyBrain
    {
        readonly Enemy enemy;
        readonly AIBody body;
        public readonly VisionSensor Sensor;
        public readonly PathMotor Navigation = new PathMotor();
        public BrainIntent Intent { get; private set; } = new BrainIntent();

        float nextDecision;
        Contact remembered;

        /// <summary>
        /// 敵種別に視認距離を変え、敵用の視野角を設定する。kindの対応はEnemy.kindの説明を参照。
        /// </summary>
        public EnemyBrain(Enemy owner, AIBody identity)
        {
            enemy = owner;
            body = identity;
            Sensor = new VisionSensor(body);
            Sensor.Settings.range = owner.kind == 2 ? 25 : 20;
            Sensor.Settings.horizontalFov = AIConfiguration.Current.enemyFov;
            identity.Sensor = Sensor;
        }

        /// <summary>
        /// 認識と約0.18秒周期の判断を更新し、毎フレームの移動方向・攻撃許可を作る。
        /// </summary>
        public BrainIntent Tick(float now)
        {
            Sensor.Tick(now);
            if (now >= nextDecision)
            {
                nextDecision = now + .18f;
                Decide(now);
            }

            Intent.Direction = Vector3.Distance(Arena.Ground(body.Position), Intent.Destination) > .6f ? Navigation.Direction(body, Intent.Destination, now, enemy.Boss ? 1.2f
                : .62f)
                : CrowdGrid.Steer(body, Vector3.zero, body.Radius, now);
            if (Intent.Target != null)
            {
                float range = enemy.kind == 2 ? 12 : enemy.Boss ? 3.8f : 2.2f;
                Intent.Fire = Sensor.CanFire(Intent.Target, range, body.Center, false);
                // 目標ContactがないAttackは既知の任務目標であるタワーへの攻撃。距離と遮蔽はここでも検査する。
            }
            else
                Intent.Fire = Intent.State == BrainState.Attack
                    && Vector3.Distance(Arena.Ground(body.Position), Vector3.zero) < (enemy.Boss ? 4
                    : enemy.kind == 2 ? 12
                    : 2.8f)
                    && !Physics.Linecast(body.Center, Vector3.up, Arena.OcclusionMask);
            body.DebugState = Intent.State + " / " + Intent.Reason;
            return Intent;
        }

        /// <summary>
        /// 見える相手を距離とデコイへの優先度で評価する。見失えば最後の目撃地点を短時間調べ、タワー任務へ戻る。
        /// 攻撃相手やタワーの周囲の位置を予約し、一つの目的地へ集まり続けるのを緩和する。
        /// </summary>
        void Decide(float now)
        {
            var next = new BrainIntent
            {
                State = BrainState.Pursue,
                Reason = "既知の任務目標・タワーへ進行"
            };
            Contact best = null;
            float bestScore = float.NegativeInfinity;
            // 【敵の選好】デコイには100点を加えて誘引効果を表す。近接型とボスは遠い通常相手へ寄り道しない。
            // 候補そのものはVisible限定なので、未発見の囮に反応することはない。
            foreach (var c in Sensor.Visible)
            {
                float d = Vector3.Distance(body.Position, c.Position);
                float score = (c.IsDecoy ? 100 : 0) + 35 - d;
                if (enemy.kind == 0 && d > 8 && !c.IsDecoy)
                    continue;
                if (enemy.Boss && d > 5 && !c.IsDecoy)
                    continue;
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                // 追跡用に最後の目撃情報をコピーする。見失った後はこの位置に固定し、生きたTransformを追わない。
                remembered = best.Snapshot();
                next.Target = best;
                float radius = enemy.kind == 2 ? 9 : enemy.Boss ? 3 : 1.65f;
                int slot = SquadBlackboard.ReserveSlot(body.Id, best.Id, body.Faction, body.Position, best.Position, radius, now);
                next.Destination = SquadBlackboard.Slot(best.Position, radius, slot);
                bool can = Sensor.CanFire(best, enemy.kind == 2 ? 12 : enemy.Boss ? 3.8f : 2.2f, body.Center, false);
                if (can)
                {
                    next.State = BrainState.Attack;
                    next.Reason = "目視した相手へ射線と距離を確認";
                    if (enemy.kind != 2 || Vector3.Distance(body.Position, best.Position) > 6)
                        next.Destination = Arena.Ground(body.Position);
                }
                else
                {
                    next.State = BrainState.Pursue;
                    next.Reason = "目視した相手の包囲位置へ移動";
                }

                next.Look = (best.Position - body.Center).normalized;
                // 2秒以内の最後の目撃地点だけを確認する。到着した、または時間が過ぎたらタワー任務へ復帰する。
            }
            else if (remembered != null && now - remembered.SeenAt < 2 && Vector3.Distance(body.Position, remembered.Position) > 2)
            {
                next.State = BrainState.Investigate;
                next.Reason = "失った相手の最終目撃地点だけを確認";
                next.Destination = Arena.Ground(remembered.Position);
                next.Look = (next.Destination - body.Position).normalized;
            }
            else
            {
                remembered = null;
                float radius = enemy.kind == 2 ? 9 : enemy.Boss ? 3.2f : 2.1f;
                // タワーは目標ID=0とし、12方向の到達点を予約する。接近経路はPathMotorが計算する。
                int slot = SquadBlackboard.ReserveSlot(body.Id, 0, body.Faction, body.Position, Vector3.zero, radius, now, 12);
                next.Destination = SquadBlackboard.Slot(Vector3.zero, radius, slot, 12);
                if (Vector3.Distance(body.Position, next.Destination) < 1.3f)
                {
                    next.State = BrainState.Attack;
                    next.Reason = "任務目標へ到着・タワー攻撃";
                }

                next.Look = (Vector3.zero - body.Position).normalized;
            }

            next.Destination = Arena.Ground(next.Destination);
            Intent = next;
        }
    }
}
