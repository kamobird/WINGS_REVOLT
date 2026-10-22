using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using Wings.AI;

namespace Wings
{
    public sealed partial class WingsGame
    {
        IEnumerator RunAIValidation()
        {
            verified.Clear();var steps=AIValidationSteps();string failure=null;
            while(true){bool more;object current=null;try{more=steps.MoveNext();if(more)current=steps.Current;}catch(Exception e){failure=e.ToString();Debug.LogException(e);break;}if(!more)break;yield return current;}
            Time.timeScale=1;verificationAutopilot=false;
            Directory.CreateDirectory(ReportDirectory());File.WriteAllText(Path.Combine(ReportDirectory(),"ai-report.json"),JsonUtility.ToJson(new VerificationReport{passed=failure==null,checks=verified.ToArray(),failure=failure},true));
            Debug.Log(failure==null?"WINGS AI VERIFICATION PASSED":"WINGS AI VERIFICATION FAILED");yield return new WaitForSecondsRealtime(.3f);Application.Quit(failure==null?0:1);
        }
        IEnumerator AIValidationSteps()
        {
            UnityEngine.Random.InitState(173);yield return new WaitForSecondsRealtime(1);
            Selected.Clear();Selected.AddRange(new[]{0,1,2,3});Stage=0;BeginMission(true);yield return null;
            foreach(var e in Enemy.All.ToArray())if(e)Destroy(e.gameObject);foreach(var p in Party)p.enabled=false;yield return null;
            var observer=Party[1];observer.Teleport(new Vector3(0,0,-8));observer.transform.rotation=Quaternion.identity;
            var subject=Enemy.Spawn(Vector3.zero,0,1);subject.enabled=false;
            var sensor=observer.Brain.Sensor;float now=Time.time;sensor.Clear();Physics.SyncTransforms();sensor.Tick(now,true);
            Check(sensor.Visible.Count==1,"Perception: target in front and within range is seen");
            var sight=sensor.Visible[0].Snapshot();Vector3 lastSeen=sight.Position;
            subject.transform.position=new Vector3(0,0,-18);Physics.SyncTransforms();sensor.Tick(now+.2f,true);
            Check(sensor.Visible.Count==0,"Perception: target behind observer is not seen");
            Check(sensor.BestMemory(now+.2f)!=null&&sensor.BestMemory(now+.2f).Position==lastSeen,"Memory keeps observed position, not hidden live position");
            Check(!sensor.CanFire(sight,65,observer.transform.position+Vector3.up*1.2f),"Fire safety rechecks sight when target moves behind observer");
            subject.transform.position=new Vector3(0,0,60);Physics.SyncTransforms();sensor.Tick(now+.3f,true);Check(sensor.Visible.Count==0,"Perception: target beyond sight distance is not seen");
            subject.transform.position=Vector3.zero;Physics.SyncTransforms();sensor.Clear();sensor.Tick(Time.time,true);
            var wall=Arena.Block("AI test occluder",new Vector3(0,1.7f,-4),new Vector3(7,3.4f,1),Color.gray,null);Physics.SyncTransforms();sensor.Tick(Time.time+.2f,true);
            Check(sensor.Visible.Count==0,"Perception: solid cover blocks all sight samples");
            subject.transform.position=new Vector3(0,0,4);Physics.SyncTransforms();sensor.Tick(Time.time+.3f,true);
            Check(sensor.BestMemory(Time.time+.3f).Position==lastSeen,"Occluded moving target cannot refresh remembered position");
            sensor.Tick(Time.time+3.5f,true);Check(sensor.BestMemory(Time.time+3.5f)==null,"Memory expires after the configured lifetime");
            subject.transform.position=Vector3.zero;wall.transform.position=new Vector3(0,.65f,-4);wall.transform.localScale=new Vector3(7,1.3f,1);Physics.SyncTransforms();sensor.Clear();sensor.Tick(Time.time,true);
            Check(sensor.Visible.Count==1,"Head visible above low cover can be detected");
            Check(!sensor.CanFire(sensor.Visible[0],65,observer.transform.position+Vector3.up*1.2f),"Visible head does not imply an unobstructed muzzle line");
            wall.transform.position=new Vector3(0,1.7f,-4);wall.transform.localScale=new Vector3(7,3.4f,1);Physics.SyncTransforms();sensor.WallVision=true;sensor.Tick(Time.time+.2f,true);
            Check(sensor.Visible.Count==1,"Hawk ability explicitly permits wall vision");observer.transform.rotation=Quaternion.Euler(0,180,0);sensor.Tick(Time.time+.4f,true);
            Check(sensor.Visible.Count==0,"Wall vision still obeys direction and sight distance");sensor.WallVision=false;
            SquadBlackboard.Clear();SquadBlackboard.Publish(observer.Identity.Id,sight);
            var report=SquadBlackboard.LatestReport(Party[2].Identity.Id,sight.SeenAt+.5f);
            Check(report!=null&&report.position==lastSeen&&report.observedAt==sight.SeenAt,"Ally report copies position and original observation time");
            Check(SquadBlackboard.LatestReport(Party[2].Identity.Id,sight.SeenAt+2)==null,"Ally reports expire without being refreshed by recipients");
            SquadBlackboard.Clear();var fabricated=sight.Snapshot();fabricated.Source=EvidenceSource.AllyReport;SquadBlackboard.Publish(observer.Identity.Id,fabricated);
            Check(SquadBlackboard.LatestReport(Party[2].Identity.Id,Time.time)==null,"Second-hand reports cannot be republished as fresh sightings");
            Destroy(wall);Destroy(subject.gameObject);yield return null;
            observer.Brain.Reset();observer.transform.rotation=Quaternion.Euler(0,180,0);
            subject=Enemy.Spawn(Vector3.zero,0,1);subject.enabled=false;Physics.SyncTransforms();observer.Brain.Sensor.Tick(Time.time,true);
            Order=SquadOrder.Assault;var unseen=observer.Brain.Tick(Time.time);
            Check(unseen.Target==null&&!unseen.Fire,"Brain cannot select or attack an unseen registered enemy");
            Check(unseen.State==BrainState.Patrol,"Assault patrols when no enemy has been perceived");
            observer.Teleport(new Vector3(22,0,-4));Party[0].Teleport(new Vector3(0,0,-4));Order=SquadOrder.Follow;var follow=observer.Brain.Tick(Time.time+.2f);
            Check(follow.State==BrainState.Regroup&&Vector3.Distance(follow.Destination,observer.Brain.Anchor())<observer.Brain.FollowLeash,"Follow prioritizes regroup and clamps its destination to the leader leash");
            Order=SquadOrder.Defend;var defense=observer.Brain.Tick(Time.time+.4f);
            Check(defense.State==BrainState.Regroup&&defense.Destination.magnitude<observer.Brain.DefenseLeash,"Defense returns to the tower leash instead of chasing");
            SquadBlackboard.Clear();int slot1=SquadBlackboard.ReserveSlot(10001,80001,2,new Vector3(0,0,8),Vector3.zero,3,Time.time);int slot2=SquadBlackboard.ReserveSlot(10002,80001,2,new Vector3(0,0,8),Vector3.zero,3,Time.time);
            Check(slot1!=slot2,"Encirclement assigns different slots to two attackers");
            Check(Vector3.Distance(SquadBlackboard.Slot(Vector3.zero,3,slot1),SquadBlackboard.Slot(Vector3.zero,3,slot2))>1,"Encirclement slots preserve physical spacing");
            SquadBlackboard.Clear();
            observer.Teleport(new Vector3(0,0,-8));Party[2].Teleport(new Vector3(.3f,0,-8));CrowdGrid.Clear();
            Check(CrowdGrid.Steer(observer.Identity,Vector3.forward,.5f,Time.time).x<0,"Cell crowd avoidance separates close neighbors");
            Check(NavigationWorld.Ready,"Runtime arena produces a real NavMesh");var route=new NavMeshPath();
            Check(NavigationWorld.Sample(new Vector3(-10,0,9),out var from)&&NavigationWorld.Sample(new Vector3(-10,0,19),out var to)&&NavMesh.CalculatePath(from,to,NavMesh.AllAreas,route)&&route.status==NavMeshPathStatus.PathComplete&&route.corners.Length>2,"NavMesh finds a complete detour around static cover");
            Party[0].Teleport(new Vector3(0,0,-8));Party[0].Aim=Vector3.forward;Party[0].Skill();yield return new WaitForSeconds(.5f);
            bool carver=false;foreach(var o in FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None))if(o.carving)carver=true;Check(carver,"Ice wall contributes a runtime carving obstacle");
            if(subject)Destroy(subject.gameObject);yield return null;
            foreach(var e in Enemy.All.ToArray())if(e)Destroy(e.gameObject);yield return null;
            observer.Teleport(new Vector3(0,0,-8));observer.transform.rotation=Quaternion.identity;observer.Brain.Reset();SquadBlackboard.Clear();Order=SquadOrder.Assault;
            var candidateA=Enemy.Spawn(new Vector3(-2,0,12),0,1);var candidateB=Enemy.Spawn(new Vector3(2,0,12),0,1);candidateA.enabled=candidateB.enabled=false;Physics.SyncTransforms();
            now=Time.time;observer.Brain.Sensor.Tick(now,true);var initial=observer.Brain.Tick(now);int initialId=initial.Target?.Id??0;
            Check(initialId!=0,"Utility selects one of the actually visible candidates");
            var alternate=initialId==candidateA.Identity.Id?candidateB:candidateA;alternate.transform.position+=Vector3.forward;Physics.SyncTransforms();observer.Brain.Sensor.Tick(now+.8f,true);
            Check(observer.Brain.Tick(now+.8f).Target.Id==initialId,"Target hysteresis suppresses small score fluctuations after hold time");
            for(int i=0;i<4;i++)SquadBlackboard.ReserveSlot(90000+i,initialId,1,observer.transform.position,Vector3.zero,4,now+1.2f);
            observer.Brain.Sensor.Tick(now+1.2f,true);var reassigned=observer.Brain.Tick(now+1.2f);
            Check(reassigned.Target.Id!=initialId,"Utility changes target when allied assignment load gives a clear advantage");
            var observed=reassigned.Target.Snapshot();observer.Brain.Reset();SquadBlackboard.Clear();SquadBlackboard.Publish(Party[2].Identity.Id,observed);observer.transform.rotation=Quaternion.Euler(0,180,0);observer.Brain.Sensor.Tick(now+1.3f,true);
            var reportOnly=observer.Brain.Tick(now+1.3f);
            Check(reportOnly.Target==null&&!reportOnly.Fire&&reportOnly.State==BrainState.Investigate,"Ally report can cause investigation but cannot grant firing permission");
            foreach(var e in Enemy.All.ToArray())if(e)Destroy(e.gameObject);SquadBlackboard.Clear();observer.Brain.Reset();yield return null;
            Order=SquadOrder.Follow;Party[0].Teleport(new Vector3(0,0,-4));observer.Teleport(new Vector3(22,0,-4));observer.Brain.Reset();observer.enabled=true;Time.timeScale=4;yield return new WaitForSeconds(3);Time.timeScale=1;
            Check(Vector3.Distance(Arena.Ground(observer.transform.position),Arena.Ground(Party[0].transform.position))<=12.5f,"Actual follow movement brings a detached ally back inside its leash");
            BeginMission(false);yield return null;verificationAutopilot=true;ShowAIDebug=true;Time.timeScale=4;
            yield return new WaitForSeconds(12);Time.timeScale=1;yield return Capture("ai-debug-combat.png");Time.timeScale=5;
            float deadline=Time.realtimeSinceStartup+75;while(State==GameState.Playing&&Time.realtimeSinceStartup<deadline)yield return null;
            Time.timeScale=1;verificationAutopilot=false;
            Check(State==GameState.Result&&Elapsed>30&&TotalKills>10,"Refined squad and invaders finish a full stage A mission");
            verified.Add("Stage A result="+Won+", wave="+Wave+", kills="+TotalKills+", time="+Elapsed.ToString("0.0")+", tower="+TowerHealth);
            yield return Capture("ai-stage-a-result.png");
            Stage=1;Selected.Clear();Selected.AddRange(new[]{0,1,2,4});BeginMission(false);yield return null;verificationAutopilot=true;Time.timeScale=5;deadline=Time.realtimeSinceStartup+90;
            while(State==GameState.Playing&&Time.realtimeSinceStartup<deadline)yield return null;
            Time.timeScale=1;verificationAutopilot=false;
            Check(State==GameState.Result&&Elapsed>30&&TotalKills>10,"Refined AI with Swallow squad finishes a full stage B mission");
            verified.Add("Stage B result="+Won+", wave="+Wave+", kills="+TotalKills+", time="+Elapsed.ToString("0.0")+", tower="+TowerHealth);
            yield return Capture("ai-stage-b-result.png");ToTitle();Check(Party.Count==0,"AI mission cleanup returns to title");
        }
    }
}
