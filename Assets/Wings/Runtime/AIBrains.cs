using UnityEngine;

namespace Wings.AI
{
    public enum BrainState { Guard, Patrol, Regroup, Pursue, Reposition, Attack, Investigate, Reload, Down }
    public sealed class BrainIntent
    {
        public BrainState State;
        public string Reason;
        public Vector3 Destination,Direction,Look;
        public Contact Target;
        public bool Fire,Skill,Bomb,Ultimate,Move;
    }

    public sealed class SquadBrain
    {
        readonly PlayerAgent pilot;
        readonly AIBody body;
        public readonly VisionSensor Sensor;
        public readonly PathMotor Navigation=new PathMotor();
        public BrainIntent Intent {get;private set;}=new BrainIntent();
        public float FollowLeash=12,DefenseLeash=11;
        public float PreferredRange=>pilot.Definition.bird==Bird.Hawk?24:pilot.Definition.bird==Bird.Crow?19:pilot.Definition.bird==Bird.Owl?9:pilot.Definition.bird==Bird.Swallow?6:14;
        public float WeaponRange=>pilot.Definition.bird==Bird.Hawk?65:pilot.Definition.bird==Bird.Crow?54:pilot.Definition.bird==Bird.Owl?14:pilot.Definition.bird==Bird.Swallow?13:42;
        float nextDecision,lastTargetChanged,nextLog,investigatedAt;
        int currentTarget,patrolStep,lastInvestigated;
        SquadOrder lastOrder;
        BrainState lastState;
        public SquadBrain(PlayerAgent owner,AIBody identity){pilot=owner;body=identity;Sensor=new VisionSensor(identity);Sensor.Settings.range=owner.Definition.bird==Bird.Hawk?55:owner.Definition.bird==Bird.Crow?43:owner.Definition.bird==Bird.Swallow?25:34;identity.Sensor=Sensor;FollowLeash=AIConfiguration.Current.followLeash;DefenseLeash=AIConfiguration.Current.defenseLeash;}
        public void Reset(){Sensor.Clear();Navigation.Reset();SquadBlackboard.Release(body.Id);currentTarget=0;nextDecision=0;Intent=new BrainIntent();}
        public Vector3 Anchor()
        {
            var g=WingsGame.Instance;return g.Order==SquadOrder.Follow&&g.Active&&g.Active!=pilot&&!g.Active.Downed?Arena.Ground(g.Active.transform.position):Vector3.zero;
        }
        public Vector3 ClampToOrder(Vector3 destination)
        {
            var g=WingsGame.Instance;destination=Arena.Ground(destination);
            if(g.Order==SquadOrder.Assault)return destination;
            var anchor=Anchor();float limit=g.Order==SquadOrder.Follow?FollowLeash-1:DefenseLeash-1;
            return anchor+Vector3.ClampMagnitude(destination-anchor,limit);
        }
        Vector3 Formation()
        {
            var g=WingsGame.Instance;int index=Mathf.Max(0,g.Party.IndexOf(pilot));
            if(g.Order==SquadOrder.Follow){var rotation=g.Active?g.Active.transform.rotation:Quaternion.identity;return ClampToOrder(Anchor()+rotation*new Vector3((index%3-1)*2.5f,0,-3.5f-(index==3?2:0)));}
            return Quaternion.Euler(0,index*90+45,0)*Vector3.forward*6;
        }
        Contact ChooseTarget(float now)
        {
            Contact best=null;float bestScore=float.NegativeInfinity,currentScore=float.NegativeInfinity;
            foreach(var contact in Sensor.Visible){
                float distance=Vector3.Distance(body.Position,contact.Position);
                float score=45-Mathf.Abs(distance-PreferredRange)+Mathf.Clamp01(1-Arena.Ground(contact.Position).magnitude/28)*25;
                score-=SquadBlackboard.AssignedAttackers(contact.Id,body.Id,now)*8;
                if(contact.Id==currentTarget)currentScore=score;
                if(score>bestScore){bestScore=score;best=contact;}
            }
            if(best!=null&&currentTarget!=0&&best.Id!=currentTarget&&(now-lastTargetChanged<AIConfiguration.Current.targetHoldSeconds||bestScore<currentScore+AIConfiguration.Current.targetSwitchAdvantage))
                foreach(var contact in Sensor.Visible)if(contact.Id==currentTarget)return contact;
            if(best!=null&&best.Id!=currentTarget){currentTarget=best.Id;lastTargetChanged=now;}
            return best;
        }
        public BrainIntent Tick(float now)
        {
            var g=WingsGame.Instance;Observe(now);
            if(now>=nextDecision||lastOrder!=g.Order){nextDecision=now+.15f;lastOrder=g.Order;Decide(now);}
            // Movement and fire safety are evaluated every frame, not just at the decision rate.
            Intent.Destination=ClampToOrder(Intent.Destination);
            float distance=Vector3.Distance(Arena.Ground(body.Position),Intent.Destination);
            Intent.Direction=distance>.8f?Navigation.Direction(body,Intent.Destination,now,.48f):CrowdGrid.Steer(body,Vector3.zero,.48f,now);
            if(g.Order!=SquadOrder.Assault){var away=Arena.Ground(body.Position)-Anchor();float limit=g.Order==SquadOrder.Follow?FollowLeash:DefenseLeash;
                if(away.magnitude>=limit-.6f&&Vector3.Dot(Intent.Direction,away)>0)Intent.Direction=Vector3.ProjectOnPlane(Intent.Direction,away.normalized);}
            Intent.Fire=Intent.Target!=null&&Sensor.CanFire(Intent.Target,WeaponRange,body.Position+Vector3.up*1.2f,Sensor.WallVision);
            if(pilot.ReloadTimer>0)Intent.Fire=false;
            body.DebugState=Intent.State+" / "+Intent.Reason;return Intent;
        }
        public void Observe(float now){Sensor.WallVision=pilot.Definition.bird==Bird.Hawk&&pilot.UltimateTimer>0;Sensor.Tick(now);foreach(var c in Sensor.Visible)SquadBlackboard.Publish(body.Id,c);}
        void Decide(float now)
        {
            var g=WingsGame.Instance;var next=new BrainIntent{Destination=Formation(),State=BrainState.Guard,Reason="防衛位置を維持"};
            var anchor=Anchor();float homeDistance=Vector3.Distance(Arena.Ground(body.Position),anchor);
            bool regroup=g.Order==SquadOrder.Follow&&homeDistance>FollowLeash||g.Order==SquadOrder.Defend&&homeDistance>DefenseLeash;
            Contact target=ChooseTarget(now);
            if(regroup){next.State=BrainState.Regroup;next.Reason="命令の行動範囲へ帰還を最優先";next.Destination=Formation();next.Target=target;}
            else if(target!=null){
                next.Target=target;
                float distance=Vector3.Distance(body.Position,target.Position);
                bool clear=Sensor.CanFire(target,WeaponRange,body.Position+Vector3.up*1.2f,Sensor.WallVision);
                int slot=SquadBlackboard.ReserveSlot(body.Id,target.Id,body.Faction,body.Position,target.Position,PreferredRange,now);
                var attackPosition=SquadBlackboard.Slot(target.Position,PreferredRange,slot);
                if(!clear){next.State=BrainState.Reposition;next.Reason="認識済みだが銃口の射線・射程が不足";next.Destination=attackPosition;}
                else if(distance<PreferredRange*.5f){next.State=BrainState.Reposition;next.Reason="武器の適正距離まで離脱";next.Destination=attackPosition;}
                else if(g.Order==SquadOrder.Assault&&distance>PreferredRange*1.3f){next.State=BrainState.Pursue;next.Reason="目視した敵へ接近";next.Destination=attackPosition;}
                else if(g.Order==SquadOrder.Follow&&homeDistance>7){next.State=BrainState.Regroup;next.Reason="射撃しながら編隊へ戻る";next.Destination=Formation();}
                else{next.State=BrainState.Attack;next.Reason="視界・射線・射程を確認して攻撃";next.Destination=Arena.Ground(body.Position);}
            } else {
                SquadBlackboard.Release(body.Id);currentTarget=0;
                Contact memory=Sensor.BestMemory(now);var report=SquadBlackboard.LatestReport(body.Id,now);
                Vector3 evidence=Vector3.zero;float observedAt=0,trust=0;int evidenceId=0;bool hasEvidence=false,fromReport=false;
                if(memory!=null&&memory.Confidence>=.2f){evidence=memory.Position;observedAt=memory.SeenAt;evidenceId=memory.Id;trust=memory.Confidence;hasEvidence=true;}
                float reportTrust=report==null?0:.65f*Mathf.Clamp01(1-(now-report.observedAt)/AIConfiguration.Current.reportSeconds);
                if(report!=null&&reportTrust>=.2f&&reportTrust>trust){evidence=report.position;observedAt=report.observedAt;evidenceId=report.subject;hasEvidence=true;fromReport=true;}
                if(hasEvidence&&(evidenceId!=lastInvestigated||observedAt>investigatedAt)&&Vector3.Distance(body.Position,evidence)>2){
                    next.State=BrainState.Investigate;next.Reason=fromReport?"味方の報告地点を確認（再発見まで射撃禁止）":"最後に見た地点を確認（射撃禁止）";next.Destination=evidence;
                } else {
                    if(hasEvidence){lastInvestigated=evidenceId;investigatedAt=observedAt;}
                    if(g.Order==SquadOrder.Assault){next.State=BrainState.Patrol;next.Reason="敵を知らないため巡回して捜索";float angle=patrolStep*72+(Mathf.Abs(body.Id)%5)*72;var point=Quaternion.Euler(0,angle,0)*Vector3.forward*19;next.Destination=point;if(Vector3.Distance(body.Position,point)<2)patrolStep++;}
                    else {next.State=g.Order==SquadOrder.Follow?BrainState.Regroup:BrainState.Guard;next.Reason=g.Order==SquadOrder.Follow?"編隊間隔を保って追従":"拠点の周囲を警戒";}
                }
            }
            next.Destination=ClampToOrder(next.Destination);
            if(pilot.ReloadTimer>0){next.State=BrainState.Reload;next.Reason="装填しながら命令内の移動を継続";}
            if(next.Target!=null){next.Look=(next.Target.Position-body.Center).normalized;PlanAbilities(next);}
            else if((next.Destination-Arena.Ground(body.Position)).sqrMagnitude>2)next.Look=(next.Destination-Arena.Ground(body.Position)).normalized;
            else next.Look=Quaternion.Euler(0,now*42+(Mathf.Abs(body.Id)%4)*90,0)*Vector3.forward;
            if(g.Order==SquadOrder.Follow&&homeDistance>8&&pilot.Definition.bird==Bird.Swallow)next.Move=!pilot.Accelerating&&pilot.Raiden>25;
            else if(pilot.Definition.bird==Bird.Swallow&&pilot.Accelerating&&next.State!=BrainState.Pursue)next.Move=true;
            Intent=next;
            if(lastState!=next.State&&now>=nextLog){Debug.Log("WINGS ALLY AI "+pilot.Definition.displayName+" order="+g.Order+" state="+next.State+" visible="+Sensor.Visible.Count+" target="+(next.Target?.Id??0)+" reason="+next.Reason);lastState=next.State;nextLog=now+.7f;}
        }
        void PlanAbilities(BrainIntent intent)
        {
            float distance=Vector3.Distance(body.Position,intent.Target.Position);int clustered=0,close=0;
            foreach(var c in Sensor.Visible){if(Vector3.Distance(c.Position,intent.Target.Position)<5)clustered++;if(Vector3.Distance(c.Position,body.Position)<5)close++;}
            bool engage=Sensor.CanFire(intent.Target,WeaponRange,body.Position+Vector3.up*1.2f,Sensor.WallVision);
            if(!engage||intent.State==BrainState.Regroup)return;
            switch(pilot.Definition.bird){
                case Bird.Penguin:intent.Bomb=distance<22&&clustered>=2;intent.Skill=close>=2&&distance<6;intent.Ultimate=close>=3;break;
                case Bird.Hawk:
                    int lureTargets=0;if(pilot.Decoy)foreach(var c in Sensor.Visible)if(Vector3.Distance(c.Position,pilot.Decoy.transform.position)<6)lureTargets++;
                    intent.Bomb=pilot.Decoy?lureTargets>=2:distance<18&&clustered>=2;intent.Skill=close>=1;intent.Ultimate=clustered>=3;break;
                case Bird.Crow:intent.Bomb=pilot.ExplosiveShots==0&&clustered>=2;intent.Skill=pilot.FullChargeShots==0;intent.Ultimate=Sensor.Visible.Count>=5;break;
                case Bird.Owl:intent.Bomb=close>=2;intent.Skill=distance<22&&clustered>=2;intent.Ultimate=clustered>=3;break;
                case Bird.Swallow:intent.Bomb=distance<13&&clustered>=2;intent.Skill=distance<32&&clustered>=2&&pilot.Raiden>=55;intent.Ultimate=close>=2;break;
            }
        }
    }

    public sealed class EnemyBrain
    {
        readonly Enemy enemy;readonly AIBody body;
        public readonly VisionSensor Sensor;
        public readonly PathMotor Navigation=new PathMotor();
        public BrainIntent Intent {get;private set;}=new BrainIntent();
        float nextDecision;Contact remembered;
        public EnemyBrain(Enemy owner,AIBody identity){enemy=owner;body=identity;Sensor=new VisionSensor(body);Sensor.Settings.range=owner.kind==2?25:20;Sensor.Settings.horizontalFov=AIConfiguration.Current.enemyFov;identity.Sensor=Sensor;}
        public BrainIntent Tick(float now)
        {
            Sensor.Tick(now);
            if(now>=nextDecision){nextDecision=now+.18f;Decide(now);}
            Intent.Direction=Vector3.Distance(Arena.Ground(body.Position),Intent.Destination)>.6f?Navigation.Direction(body,Intent.Destination,now,enemy.Boss?1.2f:.62f):CrowdGrid.Steer(body,Vector3.zero,body.Radius,now);
            if(Intent.Target!=null){
                float range=enemy.kind==2?12:enemy.Boss?3.8f:2.2f;
                Intent.Fire=Sensor.CanFire(Intent.Target,range,body.Center,false);
            }else Intent.Fire=Intent.State==BrainState.Attack&&Vector3.Distance(Arena.Ground(body.Position),Vector3.zero)<(enemy.Boss?4:enemy.kind==2?12:2.8f)&&!Physics.Linecast(body.Center,Vector3.up,Arena.WallMask);
            body.DebugState=Intent.State+" / "+Intent.Reason;return Intent;
        }
        void Decide(float now)
        {
            var next=new BrainIntent{State=BrainState.Pursue,Reason="既知の任務目標・タワーへ進行"};Contact best=null;float bestScore=float.NegativeInfinity;
            foreach(var c in Sensor.Visible){float d=Vector3.Distance(body.Position,c.Position);float score=(c.IsDecoy?100:0)+35-d;if(enemy.kind==0&&d>8&&!c.IsDecoy)continue;if(enemy.Boss&&d>5&&!c.IsDecoy)continue;if(score>bestScore){best=c;bestScore=score;}}
            if(best!=null){
                remembered=best.Snapshot();next.Target=best;
                float radius=enemy.kind==2?9:enemy.Boss?3:1.65f;
                int slot=SquadBlackboard.ReserveSlot(body.Id,best.Id,body.Faction,body.Position,best.Position,radius,now);
                next.Destination=SquadBlackboard.Slot(best.Position,radius,slot);
                bool can=Sensor.CanFire(best,enemy.kind==2?12:enemy.Boss?3.8f:2.2f,body.Center,false);
                if(can){next.State=BrainState.Attack;next.Reason="目視した相手へ射線と距離を確認";if(enemy.kind!=2||Vector3.Distance(body.Position,best.Position)>6)next.Destination=Arena.Ground(body.Position);}
                else{next.State=BrainState.Pursue;next.Reason="目視した相手の包囲位置へ移動";}
                next.Look=(best.Position-body.Center).normalized;
            }else if(remembered!=null&&now-remembered.SeenAt<2&&Vector3.Distance(body.Position,remembered.Position)>2){
                next.State=BrainState.Investigate;next.Reason="失った相手の最終目撃地点だけを確認";next.Destination=Arena.Ground(remembered.Position);next.Look=(next.Destination-body.Position).normalized;
            }else{
                remembered=null;float radius=enemy.kind==2?9:enemy.Boss?3.2f:2.1f;
                int slot=SquadBlackboard.ReserveSlot(body.Id,0,body.Faction,body.Position,Vector3.zero,radius,now,12);
                next.Destination=SquadBlackboard.Slot(Vector3.zero,radius,slot,12);
                if(Vector3.Distance(body.Position,next.Destination)<1.3f){next.State=BrainState.Attack;next.Reason="任務目標へ到着・タワー攻撃";}
                next.Look=(Vector3.zero-body.Position).normalized;
            }
            next.Destination=Arena.Ground(next.Destination);Intent=next;
        }
    }
}
