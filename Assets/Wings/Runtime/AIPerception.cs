using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wings.AI
{
    public enum EvidenceSource { Sight, Memory, AllyReport }

    // Only this sensor enumerates combat candidates. A registry entry is not knowledge.
    public sealed class AIBody : MonoBehaviour
    {
        public static readonly List<AIBody> Registry=new List<AIBody>();
        public int Faction; // WINGS/decoys=1, invaders=2.
        public float Height=1.8f, Radius=.5f;
        public PlayerAgent Pilot;
        public Enemy Invader;
        public Decoy Lure;
        public bool IsDecoy;
        public VisionSensor Sensor;
        public string DebugState="";
        public bool Alive => Pilot ? !Pilot.Downed : Invader ? Invader.Health>0 : !IsDecoy || Lure;
        public int Id => GetInstanceID();
        public Vector3 Position => transform.position;
        public Vector3 Center => Position+Vector3.up*Height*.55f;
        void Awake(){Registry.Add(this);}
        void OnDestroy(){Registry.Remove(this);SquadBlackboard.Release(Id);}
        void OnDrawGizmosSelected()
        {
            if(Sensor==null)return;
            Gizmos.color=Faction==1?Color.cyan:Color.red;Vector3 eye=Sensor.Eye;
            for(int i=0;i<=24;i++){float angle=-Sensor.Settings.horizontalFov*.5f+Sensor.Settings.horizontalFov*i/24;Vector3 point=eye+Quaternion.Euler(0,angle,0)*transform.forward*Sensor.Settings.range;if(i==0||i==24)Gizmos.DrawLine(eye,point);if(i>0){float last=-Sensor.Settings.horizontalFov*.5f+Sensor.Settings.horizontalFov*(i-1)/24;Gizmos.DrawLine(eye+Quaternion.Euler(0,last,0)*transform.forward*Sensor.Settings.range,point);}}
            foreach(var c in Sensor.Known){Gizmos.color=c.Visible?Color.green:Color.yellow;Gizmos.DrawLine(eye,c.Position);Gizmos.DrawWireSphere(c.Position,.35f);}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){Registry.Clear();SquadBlackboard.Clear();CrowdGrid.Clear();}
    }

    public sealed class Contact
    {
        public int Id;
        public AIBody Subject;
        public Vector3 Position;
        public float SeenAt,Confidence;
        public bool Visible,IsDecoy;
        public EvidenceSource Source;
        public Contact Snapshot()=>new Contact{Id=Id,Subject=Subject,Position=Position,SeenAt=SeenAt,Confidence=Confidence,Visible=Visible,IsDecoy=IsDecoy,Source=Source};
    }

    [Serializable]
    public sealed class PerceptionSettings
    {
        public float range=32, horizontalFov=120, verticalFov=120;
        public float memorySeconds=3, scanInterval=.12f;
        public int occlusionMask=Arena.WallMask;
    }

    public sealed class VisionSensor
    {
        readonly AIBody owner;
        readonly Dictionary<int,Contact> memories=new Dictionary<int,Contact>();
        readonly List<Contact> visible=new List<Contact>();
        readonly List<int> expired=new List<int>();
        float nextScan;
        public readonly PerceptionSettings Settings=new PerceptionSettings();
        public IReadOnlyList<Contact> Visible=>visible;
        public IEnumerable<Contact> Known=>memories.Values;
        public bool WallVision;
        public int ScanCount {get;private set;}
        public Vector3 Eye=>owner.Position+Vector3.up*owner.Height*.82f;
        public VisionSensor(AIBody body){owner=body;var config=AIConfiguration.Current;Settings.horizontalFov=config.allyFov;Settings.scanInterval=config.scanInterval;Settings.memorySeconds=config.memorySeconds;Settings.occlusionMask=config.occlusionMask;nextScan=Time.time+(Mathf.Abs(body.Id)%7)*.015f;}
        public void Clear(){memories.Clear();visible.Clear();nextScan=0;}
        public void Tick(float now,bool force=false)
        {
            if(!force&&now<nextScan)return;
            nextScan=now+Settings.scanInterval;ScanCount++;visible.Clear();
            foreach(var c in memories.Values){c.Visible=false;c.Source=EvidenceSource.Memory;}
            foreach(var candidate in AIBody.Registry)
            {
                if(!candidate||candidate==owner||candidate.Faction==owner.Faction||!candidate.Alive)continue;
                if(!CanSee(candidate))continue;
                if(!memories.TryGetValue(candidate.Id,out var c))memories[candidate.Id]=c=new Contact{Id=candidate.Id};
                c.Subject=candidate;c.Position=candidate.Center;c.SeenAt=now;c.Confidence=1;c.Visible=true;c.IsDecoy=candidate.IsDecoy;c.Source=EvidenceSource.Sight;
                visible.Add(c);
            }
            expired.Clear();
            foreach(var item in memories){var c=item.Value;if(!c.Visible){c.Confidence=Mathf.Clamp01(1-(now-c.SeenAt)/Settings.memorySeconds);if(now-c.SeenAt>=Settings.memorySeconds)expired.Add(item.Key);}}
            foreach(int id in expired)memories.Remove(id);
        }
        public bool CanSee(AIBody subject)
        {
            if(!subject||!subject.Alive)return false;
            Vector3 center=subject.Center,delta=center-Eye;
            if(delta.sqrMagnitude>Settings.range*Settings.range)return false;
            Vector3 flat=new Vector3(delta.x,0,delta.z),forward=owner.transform.forward;forward.y=0;
            if(flat.sqrMagnitude>.001f&&Vector3.Angle(forward,flat)>Settings.horizontalFov*.5f)return false;
            float elevation=Mathf.Atan2(Mathf.Abs(delta.y),Mathf.Max(.01f,flat.magnitude))*Mathf.Rad2Deg;
            if(elevation>Settings.verticalFov*.5f)return false;
            if(WallVision)return true;
            // Three actual body samples: a visible head/shoulder can be noticed above cover.
            foreach(float fraction in new[]{.35f,.58f,.85f})
                if(!Physics.Linecast(Eye,subject.Position+Vector3.up*subject.Height*fraction,Settings.occlusionMask,QueryTriggerInteraction.Ignore))return true;
            return false;
        }
        public bool CanFire(Contact c,float weaponRange,Vector3 muzzle,bool throughWall=false)
        {
            if(c==null||!c.Visible||!c.Subject||!CanSee(c.Subject))return false;
            // Decisions aim at the latest observed sample, never a hidden live transform.
            if(Vector3.Distance(muzzle,c.Position)>weaponRange)return false;
            return throughWall||!Physics.Linecast(muzzle,c.Position,Arena.WallMask,QueryTriggerInteraction.Ignore);
        }
        public Contact BestMemory(float now)
        {
            Contact best=null;
            foreach(var c in memories.Values)if(!c.Visible&&now-c.SeenAt<Settings.memorySeconds&&(best==null||c.SeenAt>best.SeenAt))best=c;
            return best?.Snapshot();
        }
    }

    public static class SquadBlackboard
    {
        public sealed class Report {public int subject,sender;public Vector3 position;public float observedAt;}
        sealed class Reservation {public int owner,target,faction,slot;public float until;}
        static readonly Dictionary<int,Report> reports=new Dictionary<int,Report>();
        static readonly List<Reservation> reservations=new List<Reservation>();
        public static void Clear(){reports.Clear();reservations.Clear();}
        public static void Publish(int sender,Contact sight)
        {
            if(sight==null||!sight.Visible||sight.Source!=EvidenceSource.Sight)return;
            // Original observation time is preserved. Reports cannot refresh other reports.
            reports[sight.Id]=new Report{subject=sight.Id,sender=sender,position=sight.Position,observedAt=sight.SeenAt};
        }
        public static Report LatestReport(int receiver,float now,float maxAge=-1)
        {
            if(maxAge<0)maxAge=AIConfiguration.Current.reportSeconds;
            Report best=null;foreach(var r in reports.Values)if(r.sender!=receiver&&now-r.observedAt<maxAge&&(best==null||r.observedAt>best.observedAt))best=r;
            return best==null?null:new Report{subject=best.subject,sender=best.sender,position=best.position,observedAt=best.observedAt};
        }
        public static int AssignedAttackers(int target,int except,float now)
        {int n=0;foreach(var r in reservations)if(r.target==target&&r.owner!=except&&r.until>now)n++;return n;}
        public static int ReserveSlot(int owner,int target,int faction,Vector3 position,Vector3 center,float radius,float now,int count=8)
        {
            reservations.RemoveAll(r=>r.until<=now);
            var existing=reservations.Find(r=>r.owner==owner&&r.target==target&&r.faction==faction);
            if(existing!=null){existing.until=now+.8f;return existing.slot;}
            reservations.RemoveAll(r=>r.owner==owner);
            int best=0;float score=float.PositiveInfinity;
            for(int i=0;i<count;i++){
                bool used=reservations.Exists(r=>r.target==target&&r.faction==faction&&r.slot==i);
                float value=(position-Slot(center,radius,i,count)).sqrMagnitude+(used?10000:0);
                if(value<score){score=value;best=i;}
            }
            reservations.Add(new Reservation{owner=owner,target=target,faction=faction,slot=best,until=now+.8f});return best;
        }
        public static Vector3 Slot(Vector3 center,float radius,int slot,int count=8)=>Arena.Ground(center+Quaternion.Euler(0,slot*360f/count,0)*Vector3.forward*radius);
        public static void Release(int owner){reservations.RemoveAll(r=>r.owner==owner);}
    }
}
