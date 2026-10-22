using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Wings.AI
{
    public sealed class NavigationWorld : MonoBehaviour
    {
        public static NavigationWorld Instance {get;private set;}
        public NavMeshSurface Surface {get;private set;}
        public static bool Ready=>Instance&&Instance.Surface&&Instance.Surface.navMeshData;
        public static void Build(Transform arena)
        {
            if(Instance){Instance.Surface.RemoveData();Instance.enabled=false;}
            var n=arena.gameObject.AddComponent<NavigationWorld>();Instance=n;
            n.Surface=arena.gameObject.AddComponent<NavMeshSurface>();
            n.Surface.collectObjects=CollectObjects.Children;n.Surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
            n.Surface.layerMask=(1<<0)|Arena.WallMask;n.Surface.overrideVoxelSize=true;n.Surface.voxelSize=.16f;
            Physics.SyncTransforms();n.Surface.BuildNavMesh();
            Debug.Log("WINGS AI NAVMESH: "+(n.Surface.navMeshData?"ready":"failed"));
        }
        public static bool Sample(Vector3 p,out Vector3 at,float distance=2)
        {bool ok=NavMesh.SamplePosition(p,out var hit,distance,NavMesh.AllAreas);at=ok?hit.position:p;return ok;}
        void OnDestroy(){if(Surface)Surface.RemoveData();if(Instance==this)Instance=null;}
    }

    public sealed class PathMotor
    {
        readonly NavMeshPath path=new NavMeshPath();
        Vector3[] corners=new Vector3[0];Vector3 lastGoal;int corner;
        float nextPath,stuckSince;Vector3 lastPosition;
        public bool HasPath {get;private set;}
        public int PathCalculations {get;private set;}
        public Vector3 Goal {get;private set;}
        public IReadOnlyList<Vector3> Corners=>corners;
        public void Reset(){nextPath=0;corners=new Vector3[0];corner=0;HasPath=false;}
        public Vector3 Direction(AIBody body,Vector3 goal,float now,float radius)
        {
            Goal=Arena.Ground(goal);Vector3 position=body.Position;
            if(now>=nextPath||(lastGoal-Goal).sqrMagnitude>4){
                nextPath=now+.4f+(Mathf.Abs(body.Id)%5)*.025f;lastGoal=Goal;PathCalculations++;
                HasPath=NavigationWorld.Ready&&NavigationWorld.Sample(position,out var from)&&NavigationWorld.Sample(Goal,out var to)&&NavMesh.CalculatePath(from,to,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                corners=HasPath?path.corners:new Vector3[0];corner=corners.Length>1?1:0;
            }
            while(corner<corners.Length-1&&(Arena.Ground(corners[corner])-Arena.Ground(position)).sqrMagnitude<.8f*.8f)corner++;
            Vector3 waypoint=corner<corners.Length?corners[corner]:Goal;
            Vector3 steering=Arena.Steer(position,waypoint,radius);
            steering=CrowdGrid.Steer(body,steering,radius,now);
            if((position-lastPosition).sqrMagnitude>.12f){stuckSince=now;lastPosition=position;}
            else if(now-stuckSince>1.3f){nextPath=0;stuckSince=now;steering=Quaternion.Euler(0,body.Id%2==0?70:-70,0)*steering;}
            return steering;
        }
    }

    public static class CrowdGrid
    {
        const float CellSize=2.5f;
        static readonly Dictionary<Vector2Int,List<AIBody>> cells=new Dictionary<Vector2Int,List<AIBody>>();
        static int frame=-1;
        public static void Clear(){cells.Clear();frame=-1;}
        static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));
        static void Rebuild()
        {
            if(frame==Time.frameCount)return;frame=Time.frameCount;
            foreach(var cell in cells.Values)cell.Clear();
            foreach(var body in AIBody.Registry)if(body&&body.Alive){var key=Cell(body.Position);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<AIBody>();list.Add(body);}
        }
        public static Vector3 Steer(AIBody body,Vector3 desired,float radius,float now)
        {
            Rebuild();Vector3 separation=Vector3.zero;int near=0;Vector2Int key=Cell(body.Position);
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(cells.TryGetValue(key+new Vector2Int(x,z),out var list))foreach(var other in list)
            {
                if(!other||other==body||!other.Alive)continue;
                Vector3 delta=body.Position-other.Position;delta.y=0;float distance=delta.magnitude;
                if(distance>radius+other.Radius+1.2f)continue;
                // Local collision avoidance does not reveal combat contacts through walls.
                if(Physics.Linecast(body.Center,other.Center,Arena.WallMask))continue;
                if(distance<.01f)delta=Quaternion.Euler(0,Mathf.Abs(body.Id%360),0)*Vector3.forward;
                separation+=delta.normalized*Mathf.Clamp01((radius+other.Radius+1.2f-distance)/1.2f);near++;
            }
            Vector3 result=desired+separation*1.8f;
            if(near>0&&desired.sqrMagnitude>.1f&&separation.sqrMagnitude>.05f)result+=Vector3.Cross(Vector3.up,desired)*((body.Id%2==0?1:-1)*.12f);
            return Vector3.ClampMagnitude(result,1);
        }
    }
}
