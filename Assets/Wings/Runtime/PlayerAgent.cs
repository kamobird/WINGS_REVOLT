using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Wings.AI;
using UnityEngine.AI;

namespace Wings
{
    public sealed class PlayerAgent : MonoBehaviour
    {
        public CharacterDefinition Definition {get;private set;}
        public float Health, Ultimate=35, Raiden=100, BombTimer, SkillTimer, MoveTimer, ReloadTimer, Charge, UltimateTimer;
        public float TotalDamage; public int Kills, Ammo, ExplosiveShots, FullChargeShots, NovaShots;
        public float RespawnTimer {get;private set;}
        public bool Downed => Health<=0;
        public bool Controlled => WingsGame.Instance && WingsGame.Instance.Active==this;
        public bool Accelerating {get;private set;}
        public int Magazine => Definition.bird==Bird.Penguin?36:Definition.bird==Bird.Hawk?6:Definition.bird==Bird.Crow?8:Definition.bird==Bird.Owl?10:12;
        public Vector3 Aim {get;set;} = Vector3.forward;
        public Vector3 AimPoint {get;set;}
        public Transform Visual {get;private set;}
        public Decoy Decoy {get;private set;}
        public AIBody Identity {get;private set;}
        public SquadBrain Brain {get;private set;}
        CharacterController motor;
        Vector3 moveDirection;float vertical,fireTimer,invincible,aiCharge;
        bool busy;
        public static PlayerAgent Create(CharacterDefinition d,Vector3 at)
        {
            var go=new GameObject(d.displayName);go.layer=9;go.transform.position=at;
            var p=go.AddComponent<PlayerAgent>();p.Definition=d;p.Health=d.health;p.Ammo=p.Magazine;
            p.motor=go.AddComponent<CharacterController>();p.motor.height=1.85f;p.motor.radius=.42f;p.motor.center=Vector3.up*.93f;p.motor.stepOffset=.3f;
            p.Visual=WingsVisual.BirdModel(d,go.transform);p.AimPoint=at+Vector3.forward*15;
            p.Identity=go.AddComponent<AIBody>();p.Identity.Faction=1;p.Identity.Pilot=p;p.Identity.Radius=.42f;
            p.Brain=new SquadBrain(p,p.Identity);
            return p;
        }
        void Update()
        {
            var g=WingsGame.Instance;if(!g||g.State!=GameState.Playing)return;
            float dt=Time.deltaTime;
            if(Downed){RespawnTimer-=dt;if(RespawnTimer<=0){Health=Definition.health;Teleport(new Vector3(Random.Range(-3,3),0,-4));Visual.gameObject.SetActive(true);Ammo=Magazine;invincible=3;Brain.Reset();g.Notify(Definition.displayName+"、戦線に復帰");}return;}
            fireTimer-=dt;invincible-=dt;BombTimer=Mathf.Max(0,BombTimer-dt);SkillTimer=Mathf.Max(0,SkillTimer-dt);MoveTimer=Mathf.Max(0,MoveTimer-dt);
            if(ReloadTimer>0){ReloadTimer-=dt;if(ReloadTimer<=0)Ammo=Magazine;}
            Ultimate=Mathf.Min(100,Ultimate+dt*.7f);
            if(UltimateTimer>0){UltimateTimer-=dt;if(UltimateTimer<=0)NovaShots=0;}
            if(Definition.bird==Bird.Swallow){if(Accelerating&&UltimateTimer<=0){Raiden=Mathf.Max(0,Raiden-22*dt);if(Raiden<=0)Accelerating=false;}else Raiden=Mathf.Min(100,Raiden+14*dt);}
            if(Controlled){if(!g.SmokeTesting)Control();else if(g.VerificationAutopilot)Think();else moveDirection=Vector3.zero;}else Think();
            if(!busy)
            {
                float speed=Definition.speed*(Accelerating?(UltimateTimer>0?2.8f:2):1);
                if(Definition.bird==Bird.Penguin&&UltimateTimer>0)speed*=.35f;
                if(motor.isGrounded&&vertical<0)vertical=-2;vertical-=22*dt;
                motor.Move((moveDirection*speed+Vector3.up*vertical)*dt);
            }
            if(Aim.sqrMagnitude>.01f){var facing=new Vector3(Aim.x,0,Aim.z);if(facing.sqrMagnitude>.01f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(facing),dt*15);}
            if(Visual){float lift=Definition.bird==Bird.Owl&&NovaShots>0?3:0;Visual.localPosition=Vector3.up*(lift+Mathf.Sin(Time.time*(moveDirection.sqrMagnitude>.1f?16:3))*(moveDirection.sqrMagnitude>.1f?.06f:.018f));}
            if(Ammo<=0&&ReloadTimer<=0)Reload();
        }
        void Control()
        {
            var g=WingsGame.Instance;var kb=Keyboard.current;var mouse=Mouse.current;
            Brain.Observe(Time.time);
            if(g.CommandWheel){moveDirection=Vector3.zero;Charge=0;return;}
            Vector2 input=Vector2.zero;
            if(kb!=null){input.x=(kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0);input.y=(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0);}
            moveDirection=Quaternion.Euler(0,g.CameraYaw,0)*new Vector3(input.x,0,input.y).normalized;
            var ray=g.Viewport.ViewportPointToRay(new Vector3(.5f,.5f,0));
            AimPoint=ray.origin+ray.direction*60;
            if(Physics.Raycast(ray,out var hit,80,~(1<<9),QueryTriggerInteraction.Ignore))AimPoint=hit.point;
            Aim=(AimPoint-(transform.position+Vector3.up*1.2f)).normalized;
            if(mouse!=null) {
                if(Definition.bird==Bird.Crow){if(mouse.leftButton.isPressed&&ReloadTimer<=0&&Ammo>0)Charge=Mathf.Min(1,Charge+Time.deltaTime/1.3f);if(mouse.leftButton.wasReleasedThisFrame&&Charge>0){Fire(FullChargeShots>0?1:Charge);Charge=0;}}
                else if(mouse.leftButton.isPressed)Fire();
            }
            if(kb==null)return;
            if(kb.qKey.wasPressedThisFrame)Skill();if(kb.eKey.wasPressedThisFrame)Bomb();if(kb.xKey.wasPressedThisFrame)UseUltimate();
            if(kb.leftShiftKey.wasPressedThisFrame)MoveAbility();if(kb.rKey.wasPressedThisFrame)Reload();
            if(kb.spaceKey.wasPressedThisFrame&&motor.isGrounded&&!busy)vertical=8;
        }
        void Think()
        {
            var intent=Brain.Tick(Time.time);moveDirection=intent.Direction;Aim=intent.Look.sqrMagnitude>.01f?intent.Look:transform.forward;
            if(intent.Target!=null)AimPoint=intent.Target.Position;
            if(intent.Move)MoveAbility();if(intent.Bomb)Bomb();if(intent.Skill)Skill();if(intent.Ultimate)UseUltimate();
            intent.Move=intent.Bomb=intent.Skill=intent.Ultimate=false;
            if(!intent.Fire){aiCharge=0;Charge=0;return;}
            if(Definition.bird==Bird.Crow){aiCharge+=Time.deltaTime;Charge=FullChargeShots>0?1:Mathf.Clamp01(aiCharge/1.3f);if((aiCharge>=1.3f||FullChargeShots>0)&&ReloadTimer<=0&&fireTimer<=0){Fire(1);aiCharge=0;Charge=0;}}
            else Fire();
        }
        public void Fire(float charge=0)
        {
            if(Downed||busy||ReloadTimer>0||fireTimer>0||Ammo<=0)return;
            if(NovaShots>0){NovaShots--;AreaField.Explode(AimGround(),8,240,this,0,0,true);fireTimer=.7f;if(NovaShots==0)UltimateTimer=0;return;}
            if(Definition.bird==Bird.Penguin&&UltimateTimer>0)return;
            if(FullChargeShots>0){charge=1;FullChargeShots--;}
            Ammo--;fireTimer=Definition.fireInterval;
            Vector3 muzzle=transform.position+Vector3.up*1.2f+Aim*.7f;
            float damage=Definition.damage;Color c=Definition.color;
            switch(Definition.bird)
            {
                case Bird.Penguin:Projectile.Fire(muzzle,Aim,42,damage,42,this,c,slow:1.2f);break;
                case Bird.Hawk:BeamAttack(65,damage,.65f,UltimateTimer>0,true);break;
                case Bird.Crow:
                    float blast=ExplosiveShots>0?2+charge*3:0;if(ExplosiveShots>0)ExplosiveShots--;
                    Projectile.Fire(muzzle,Aim,35+charge*18,damage*(1+charge*3),16+charge*38,this,c,blast,execute:charge>=.99f,charge:charge);break;
                case Bird.Owl:Projectile.Fire(muzzle,Aim,20,damage,12,this,c,3.8f);break;
                case Bird.Swallow:
                    for(int i=0;i<7;i++)Projectile.Fire(muzzle,Quaternion.Euler(0,(i-3)*4,0)*Aim,32,damage,13,this,c);break;
            }
            WingsGame.Instance.Sound(Definition.bird==Bird.Hawk?260:550,.045f,.045f);
        }
        public void Reload(){if(Downed||ReloadTimer>0||Ammo>=Magazine)return;ReloadTimer=Definition.bird==Bird.Hawk?2.2f:1.5f;Charge=0;}
        public Vector3 AimGround()
        {
            var planar=Arena.Ground(AimPoint)-Arena.Ground(transform.position);
            return Arena.Ground(transform.position+Vector3.ClampMagnitude(planar,22));
        }
        public void Bomb()
        {
            if(Downed||busy)return;
            if(Definition.bird==Bird.Hawk&&Decoy){Decoy.Tornado();Decoy=null;return;}
            if(BombTimer>0)return;BombTimer=Definition.bombCooldown;
            switch(Definition.bird)
            {
                case Bird.Penguin:AreaField.Create(AimGround(),5,7,12,this,1.2f);break;
                case Bird.Hawk:Decoy=Wings.Decoy.Create(AimGround(),this);break;
                case Bird.Crow:ExplosiveShots=5;break;
                case Bird.Owl:
                    for(int i=0;i<8;i++)Projectile.Fire(transform.position+Vector3.up,Quaternion.Euler(0,i*45,0)*Vector3.forward,21,48,5,this,Definition.color,3);break;
                case Bird.Swallow:StartCoroutine(SplitVolley());break;
            }
            WingsGame.Instance.Sound(360,.1f,.1f);
        }
        IEnumerator SplitVolley(){for(int i=0;i<4;i++){if(Downed)yield break;Projectile.Fire(transform.position+Vector3.up,Aim,26,28,9,this,Definition.color,2,cluster:true);yield return new WaitForSeconds(.13f);}}
        public void Skill()
        {
            if(Downed||busy||SkillTimer>0)return;
            if(Definition.bird==Bird.Swallow&&Raiden<40&&UltimateTimer<=0){if(Controlled)WingsGame.Instance.Notify("ライデンゲージが40必要");return;}
            SkillTimer=Definition.skillCooldown;
            switch(Definition.bird)
            {
                case Bird.Penguin:
                    var forward=new Vector3(Aim.x,0,Aim.z).normalized;
                    var wall=Arena.Block("Ice wall",Arena.Ground(transform.position+forward*3)+Vector3.up*1.2f,new Vector3(6,2.4f,.8f),Definition.color,null);
                    wall.transform.rotation=Quaternion.LookRotation(forward);Physics.SyncTransforms();
                    var obstacle=wall.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;obstacle.carveOnlyStationary=false;
                    Destroy(wall,6);break;
                case Bird.Hawk:
                    AreaField.Explode(transform.position,5,45,this);
                    foreach(var e in Enemy.All)if(e&&Vector3.Distance(e.transform.position,transform.position)<6)e.Push((e.transform.position-transform.position).normalized*16);
                    vertical=12;invincible=.7f;break;
                case Bird.Crow:FullChargeShots=5;break;
                case Bird.Owl:AreaField.Create(AimGround(),6,7,48,this,meteor:true);break;
                case Bird.Swallow:
                    if(UltimateTimer<=0)Raiden-=40;
                    BeamAttack(UltimateTimer>0?55:34,UltimateTimer>0?220:130,UltimateTimer>0?1.7f:1,true,false);break;
            }
            WingsGame.Instance.Sound(700,.12f,.1f);
        }
        public void MoveAbility()
        {
            if(Downed||busy)return;
            if(Definition.bird==Bird.Swallow){if(Accelerating){Accelerating=false;return;}if(Raiden>=10||UltimateTimer>0)Accelerating=true;return;}
            if(MoveTimer>0)return;
            Vector3 direction=moveDirection.sqrMagnitude>.1f?moveDirection:new Vector3(Aim.x,0,Aim.z).normalized;
            if(Definition.bird==Bird.Hawk){
                if(!Physics.Raycast(transform.position+Vector3.up,Aim,out var hit,25,Arena.WallMask)){if(Controlled)WingsGame.Instance.Notify("ワイヤーを壁に向けて使用");return;}
                var delta=Arena.Ground(hit.point)-Arena.Ground(transform.position);WingsVisual.Beam(transform.position+Vector3.up,hit.point,Definition.color,.08f);StartCoroutine(Dash(delta.normalized,Mathf.Max(0,delta.magnitude-1),.27f));
            } else if(Definition.bird==Bird.Crow)StartCoroutine(Blink(direction));
            else {if(Definition.bird==Bird.Owl){AreaField.Explode(transform.position,3,32,this);vertical=6;}StartCoroutine(Dash(direction,Definition.bird==Bird.Owl?9:6,.2f));}
            MoveTimer=Definition.moveCooldown;invincible=.4f;
        }
        IEnumerator Dash(Vector3 direction,float distance,float seconds)
        {
            busy=true;float elapsed=0;
            while(elapsed<seconds){float dt=Mathf.Min(Time.deltaTime,seconds-elapsed);motor.Move(direction*(distance/seconds*dt));elapsed+=dt;yield return null;}
            busy=false;
        }
        IEnumerator Blink(Vector3 direction)
        {
            busy=true;WingsVisual.Ring(transform.position,1.5f,Definition.color);yield return new WaitForSeconds(.28f);
            if(!Downed){Vector3 target=Arena.Ground(transform.position+direction*9);
                for(float n=9;n>=1;n-=.5f){target=Arena.Ground(transform.position+direction*n);if(!Physics.CheckCapsule(target+Vector3.up*.5f,target+Vector3.up*1.4f,.5f,Arena.WallMask)){Teleport(target);break;}}
                WingsVisual.Burst(transform.position+Vector3.up,Definition.color);}
            busy=false;
        }
        public void UseUltimate()
        {
            if(Downed||busy||Ultimate<100)return;Ultimate=0;
            WingsGame.Instance.Notify(Definition.displayName+" : "+new[]{"この星は、守り抜く。","狩りの時間だ。","美しい終わりを。","生徒には、手を出させない。","雷より、速く！"}[(int)Definition.bird],3);
            switch(Definition.bird)
            {
                case Bird.Penguin:StartCoroutine(AbsoluteZero());break;
                case Bird.Hawk:UltimateTimer=12;break;
                case Bird.Crow:
                    foreach(var e in Enemy.All.ToArray())if(e){WingsVisual.Beam(e.transform.position+Vector3.up*15,e.transform.position,Definition.color,.15f);e.Hit(Definition.damage*4,this,0,0,Random.value<.3f);}break;
                case Bird.Owl:NovaShots=3;UltimateTimer=15;invincible=15;break;
                case Bird.Swallow:UltimateTimer=12;Raiden=100;Accelerating=true;SkillTimer=0;break;
            }
        }
        IEnumerator AbsoluteZero(){UltimateTimer=1.1f;WingsVisual.Ring(transform.position,12,Definition.color,1.1f);yield return new WaitForSeconds(1.1f);if(!Downed)AreaField.Explode(transform.position,14,200,this,0,4);UltimateTimer=0;}
        public void BeamAttack(float range,float damage,float width,bool throughWall,bool distanceBonus)
        {
            Vector3 origin=transform.position+Vector3.up*1.2f;
            if(!throughWall&&Physics.Raycast(origin,Aim,out var wall,range,Arena.WallMask))range=wall.distance;
            var targets=Enemy.All.FindAll(e=>e&&e.Health>0);targets.Sort((a,b)=>(a.transform.position-origin).sqrMagnitude.CompareTo((b.transform.position-origin).sqrMagnitude));
            foreach(var e in targets){Vector3 delta=e.transform.position+Vector3.up*(e.Boss?1.6f:1)-origin;float along=Vector3.Dot(delta,Aim);
                if(along<0||along>range||(delta-Aim*along).magnitude>width+(e.Boss?1.1f:.6f))continue;
                e.Hit(damage*(distanceBonus?1+along/30:1),this);if(!throughWall){range=along;break;}}
            WingsVisual.Beam(origin,origin+Aim*range,Definition.color,width*.3f);
        }
        public void Hit(float damage)
        {
            if(Downed||invincible>0)return;Health=Mathf.Max(0,Health-damage);invincible=.35f;
            if(Health<=0){StopAllCoroutines();busy=false;Accelerating=false;Charge=0;UltimateTimer=0;NovaShots=0;ReloadTimer=0;motor.enabled=false;RespawnTimer=12;Visual.gameObject.SetActive(false);Brain.Reset();Identity.DebugState="Down / 復帰待ち";WingsGame.Instance.Notify(Definition.displayName+"がダウン。12秒で復帰");}
        }
        public void Teleport(Vector3 p){motor.enabled=false;transform.position=p;motor.enabled=true;vertical=0;}
        public void CancelCharge(){Charge=0;aiCharge=0;}
    }
}
