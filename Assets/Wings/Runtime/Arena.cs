using UnityEngine;

namespace Wings
{
    public static class Arena
    {
        public const int WallLayer = 8;
        public const int WallMask = 1 << WallLayer;
        public static readonly Vector3[] Gates = { new Vector3(0,0,29),new Vector3(29,0,0),new Vector3(0,0,-29),new Vector3(-29,0,0) };
        public static Transform Build(int stage)
        {
            var root = new GameObject("Arena").transform;
            Color floor = stage==0 ? new Color(.13f,.22f,.28f) : new Color(.24f,.18f,.23f);
            Color accent = stage==0 ? new Color(.15f,.75f,.87f) : new Color(1,.42f,.3f);
            WingsVisual.Shape("Foundation",PrimitiveType.Cube,new Vector3(0,-.6f,0),new Vector3(64,1,64),floor,root,true);
            for(int i=-30;i<=30;i+=4)
            {
                WingsVisual.Shape("Grid",PrimitiveType.Cube,new Vector3(i,-.085f,0),new Vector3(.025f,.02f,62),floor*1.5f,root);
                WingsVisual.Shape("Grid",PrimitiveType.Cube,new Vector3(0,-.085f,i),new Vector3(62,.02f,.025f),floor*1.5f,root);
            }
            for(int i=0;i<4;i++)
            {
                float a=i*90;
                var wall=Block("Perimeter", Quaternion.Euler(0,a,0)*new Vector3(0,1,32),new Vector3(65,2,1),floor*.6f,root);
                wall.transform.rotation=Quaternion.Euler(0,a,0);
                var gate=Gates[i];
                WingsVisual.Shape("Gate beacon",PrimitiveType.Cylinder,gate+Vector3.up*.1f,new Vector3(4,.12f,4),accent,root);
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 p=Quaternion.Euler(0,a,0)*new Vector3(side*10,1.1f,14);
                    Block("Cover",p,new Vector3(5,2.2f,2.5f),floor*1.3f,root);
                    WingsVisual.Shape("Cover light",PrimitiveType.Cube,p+Vector3.up*1.12f,new Vector3(4.6f,.05f,2.1f),accent*.7f,root);
                }
            }
            if(stage==1)
            {
                for(int i=0;i<4;i++)
                {
                    Vector3 p=Quaternion.Euler(0,i*90+45,0)*new Vector3(0,1.6f,20);
                    Block("Ruined column",p,new Vector3(2.5f,3.2f,2.5f),floor*1.5f,root);
                }
            }
            WingsVisual.Shape("Tower plinth",PrimitiveType.Cylinder,new Vector3(0,.05f,0),new Vector3(5,.15f,5),new Color(.07f,.12f,.18f),root);
            WingsVisual.Shape("Core ring",PrimitiveType.Cylinder,new Vector3(0,.25f,0),new Vector3(3,.12f,3),accent,root);
            var core=WingsVisual.Shape("WINGS beacon",PrimitiveType.Cylinder,new Vector3(0,1.5f,0),new Vector3(1.2f,1.4f,1.2f),accent,root);
            var light=core.AddComponent<Light>();light.color=accent;light.range=12;light.intensity=3;
            WingsVisual.Shape("Tower crown",PrimitiveType.Sphere,new Vector3(0,3.4f,0),Vector3.one*1.7f,accent,root);
            for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI/8;
                var p=new Vector3(Mathf.Cos(angle)*39,-2,Mathf.Sin(angle)*39);
                WingsVisual.Shape("Distant ruins",PrimitiveType.Cube,p,new Vector3(5,Random.Range(4,14),5),floor*.55f,root);
            }
            Physics.SyncTransforms();return root;
        }
        public static GameObject Block(string name,Vector3 p,Vector3 size,Color c,Transform root)
        {
            var go=WingsVisual.Shape(name,PrimitiveType.Cube,p,size,c,root,true);go.layer=WallLayer;Physics.SyncTransforms();return go;
        }
        public static Vector3 Ground(Vector3 p) { p.x=Mathf.Clamp(p.x,-30,30);p.z=Mathf.Clamp(p.z,-30,30);p.y=0;return p; }
        public static Vector3 Steer(Vector3 position,Vector3 target,float radius=.65f)
        {
            var d=target-position;d.y=0;
            if(d.magnitude<.2f)return Vector3.zero;
            d.Normalize();
            if(!Physics.SphereCast(position+Vector3.up, radius,d,out _,2.4f,WallMask))return d;
            for(int a=30;a<=150;a+=30)
                foreach(int side in new[]{1,-1}) {
                    var n=Quaternion.Euler(0,a*side,0)*d;
                    if(!Physics.SphereCast(position+Vector3.up,radius,n,out _,2.4f,WallMask))return n;
                }
            return -d;
        }
    }
}
