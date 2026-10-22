using UnityEngine;

namespace Wings
{
    /// <summary>
    /// mapCは128m四方の高さ場。床の三角形とGroundの高さ補間を一致させ、配置と物理判定のずれを防ぐ。
    /// 高低差は実際のColliderを持ち、見た目だけの台座ではなくAIも歩いて上り下りできる。
    /// </summary>
    public static partial class Arena
    {
        const int MapCCells = 64;
        const float MapCStep = 2;
        static readonly Color MapCFloor = new Color(.16f, .23f, .25f);
        static readonly Color MapCAccent = new Color(.98f, .78f, .35f);
        static readonly Vector3[] MapCGates =
        {
            new Vector3(0, 0, 55), new Vector3(55, 0, 0),
            new Vector3(0, 0, -55), new Vector3(-55, 0, 0)
        };

        /// <summary>西8m、北4m、東-4mの地形を緩い勾配で結ぶ。中央のタワー周囲は平地を保つ。</summary>
        static float MapCHeight(float x, float z)
        {
            float west = 8 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-10, -30, x));
            float east = -4 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(10, 30, x));
            float north = 4 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(18, 38, z));
            return west + east + north;
        }

        /// <summary>描画・Colliderと同じ2m格子の三角形内で高さを補間する。レイで壁の上面を拾うことはない。</summary>
        public static float MapCSurfaceHeight(float x, float z)
        {
            float gx = Mathf.Clamp((x + 64) / MapCStep, 0, MapCCells - .001f);
            float gz = Mathf.Clamp((z + 64) / MapCStep, 0, MapCCells - .001f);
            int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
            float fx = gx - ix, fz = gz - iz;
            float x0 = ix * MapCStep - 64, z0 = iz * MapCStep - 64;
            float a = MapCHeight(x0, z0), b = MapCHeight(x0 + MapCStep, z0);
            float c = MapCHeight(x0, z0 + MapCStep), d = MapCHeight(x0 + MapCStep, z0 + MapCStep);
            return fx + fz <= 1 ? a + (b - a) * fx + (c - a) * fz
                : d + (c - d) * (1 - fx) + (b - d) * (1 - fz);
        }

        static Transform BuildMapC()
        {
            var root = new GameObject("Arena C - Terraced Labyrinth").transform;
            var floor = new GameObject("C walkable heightfield");
            floor.transform.SetParent(root, false);
            floor.layer = TerrainLayer;
            var mesh = CreateMapCMesh();
            floor.AddComponent<MeshFilter>().sharedMesh = mesh;
            floor.AddComponent<MeshRenderer>().sharedMaterial = WingsVisual.Material(MapCFloor);
            floor.AddComponent<MeshCollider>().sharedMesh = mesh;
            floor.AddComponent<ArenaMeshLifetime>();

            // 外周は四隅の高さ差を覆う。内側の通路は幅6m以上を基本にし、大型敵も通れるようにする。
            MapCWall(root, "C boundary north", new Vector2(-64, 64), new Vector2(64, 64), 4);
            MapCWall(root, "C boundary south", new Vector2(-64, -64), new Vector2(64, -64), 4);
            MapCWall(root, "C boundary west", new Vector2(-64, -64), new Vector2(-64, 64), 4);
            MapCWall(root, "C boundary east", new Vector2(64, -64), new Vector2(64, 64), 4);

            // 西の高台: 違う方向に開いた壁で、目撃後の回り込みと再発見を観察する。
            MapCWall(root, "West outer bend", new Vector2(-44, -22), new Vector2(-44, 22), 4);
            MapCWall(root, "West upper turn", new Vector2(-44, 22), new Vector2(-24, 22), 4);
            MapCWall(root, "West inner bend", new Vector2(-32, -30), new Vector2(-32, 8), 3.5f);
            MapCWall(root, "West lower turn", new Vector2(-32, -30), new Vector2(-16, -30), 3.5f);

            // 北の段丘: 壁の端を交互に回る経路。直線距離より長い経路と、坂の折れ点を観察できる。
            MapCWall(root, "North first baffle", new Vector2(-22, 40), new Vector2(12, 40), 4);
            MapCWall(root, "North second baffle", new Vector2(-8, 28), new Vector2(26, 28), 4);
            MapCWall(root, "North corner", new Vector2(26, 28), new Vector2(26, 42), 4);

            // 東の低地: 中央・北・南の迂回路を残し、コの字の遮蔽物に短い行き止まりを作る。
            MapCWall(root, "East north wing", new Vector2(34, 10), new Vector2(48, 10), 3.5f);
            MapCWall(root, "East enclosure", new Vector2(34, -10), new Vector2(34, 10), 3.5f);
            MapCWall(root, "East south wing", new Vector2(34, -10), new Vector2(48, -10), 3.5f);
            MapCWall(root, "East inner cover", new Vector2(20, -22), new Vector2(20, -6), 3);

            // 南の廃墟街: 広場と狭い曲がり角を混ぜ、隊列と局所回避の違いを見せる。
            MapCWall(root, "South west block", new Vector2(-20, -42), new Vector2(-6, -42), 4);
            MapCWall(root, "South east block", new Vector2(5, -30), new Vector2(24, -30), 4);
            MapCWall(root, "South alley", new Vector2(24, -46), new Vector2(24, -30), 4);
            MapCWall(root, "Central partial cover", new Vector2(-10, 12), new Vector2(-4, 12), 1.25f);

            // 床上の案内線は歩行判定を持たない。地図の分岐を俯瞰で追いやすくするための表示。
            MapCRoute(root, new[] { new Vector2(0, 55), new Vector2(20, 46), new Vector2(20, 35), new Vector2(-16, 35), new Vector2(-16, 18), Vector2.zero }, MapCAccent);
            MapCRoute(root, new[] { new Vector2(-55, 0), new Vector2(-50, -28), new Vector2(-39, -37), new Vector2(-24, -37), new Vector2(-20, -18), Vector2.zero }, new Color(.5f, .8f, 1));
            MapCRoute(root, new[] { new Vector2(55, 0), new Vector2(52, -18), new Vector2(28, -18), new Vector2(27, -4), Vector2.zero }, new Color(.45f, 1, .75f));
            MapCRoute(root, new[] { new Vector2(0, -55), new Vector2(0, -37), new Vector2(-3, -24), Vector2.zero }, new Color(1, .6f, .4f));
            for (int i = 0; i < MapCGates.Length; i++)
            {
                var point = Ground(MapCGates[i]);
                WingsVisual.Shape("C gate " + i, PrimitiveType.Cylinder, point + Vector3.up * .1f, new Vector3(4, .12f, 4), MapCAccent, root);
            }

            WingsVisual.Shape("Tower plinth", PrimitiveType.Cylinder, Vector3.up * .05f, new Vector3(5, .15f, 5), MapCFloor * .5f, root);
            WingsVisual.Shape("C tower core", PrimitiveType.Cylinder, Vector3.up * 1.5f, new Vector3(1.2f, 1.4f, 1.2f), MapCAccent, root);
            WingsVisual.Shape("C tower crown", PrimitiveType.Sphere, Vector3.up * 3.4f, Vector3.one * 1.7f, MapCAccent, root);
            MapCLabel(root, "WEST / +8m", new Vector2(-51, -40));
            MapCLabel(root, "NORTH / +4m", new Vector2(3, 50));
            MapCLabel(root, "EAST / -4m", new Vector2(43, -24));
            MapCLabel(root, "SOUTH / RUINS", new Vector2(-4, -50));
            MapCLabel(root, "C / TOWER", new Vector2(0, 5));
            Physics.SyncTransforms();
            return root;
        }

        static Mesh CreateMapCMesh()
        {
            int stride = MapCCells + 1;
            var vertices = new Vector3[stride * stride];
            var triangles = new int[MapCCells * MapCCells * 6];
            for (int z = 0; z <= MapCCells; z++)
                for (int x = 0; x <= MapCCells; x++)
                {
                    float px = x * MapCStep - 64, pz = z * MapCStep - 64;
                    vertices[z * stride + x] = new Vector3(px, MapCHeight(px, pz), pz);
                }
            int cursor = 0;
            for (int z = 0; z < MapCCells; z++)
                for (int x = 0; x < MapCCells; x++)
                {
                    int a = z * stride + x, b = a + 1, c = a + stride, d = c + 1;
                    triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                    triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
                }
            var mesh = new Mesh { name = "Map C heightfield" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void MapCWall(Transform root, string name, Vector2 a, Vector2 b, float height)
        {
            Vector3 start = Ground(new Vector3(a.x, 0, a.y)), end = Ground(new Vector3(b.x, 0, b.y));
            float low = Mathf.Min(start.y, end.y), high = Mathf.Max(start.y, end.y);
            Vector3 delta = new Vector3(b.x - a.x, 0, b.y - a.y);
            Vector3 center = new Vector3((a.x + b.x) * .5f, low + (height + high - low) * .5f - .2f, (a.y + b.y) * .5f);
            var wall = Block(name, center, new Vector3(1.4f, height + high - low, delta.magnitude), MapCFloor * 1.35f, root);
            wall.transform.rotation = Quaternion.LookRotation(delta);
            WingsVisual.Shape("C wall cap", PrimitiveType.Cube, Vector3.up * .5f, new Vector3(.85f, .025f, .98f), MapCAccent * .7f, wall.transform);
        }

        static void MapCRoute(Transform root, Vector2[] points, Color color)
        {
            var go = new GameObject("C route guide");
            go.transform.SetParent(root, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = WingsVisual.Material(color);
            line.widthMultiplier = .22f;
            var samples = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                int count = Mathf.CeilToInt(Vector2.Distance(points[i], points[i + 1]));
                for (int n = 0; n < count; n++)
                {
                    Vector2 p = Vector2.Lerp(points[i], points[i + 1], (float)n / count);
                    samples.Add(Ground(new Vector3(p.x, 0, p.y)) + Vector3.up * .08f);
                }
            }
            Vector2 last = points[points.Length - 1];
            samples.Add(Ground(new Vector3(last.x, 0, last.y)) + Vector3.up * .08f);
            line.positionCount = samples.Count;
            line.SetPositions(samples.ToArray());
        }

        static void MapCLabel(Transform root, string text, Vector2 at)
        {
            var go = new GameObject(text);
            go.transform.SetParent(root, false);
            go.transform.position = Ground(new Vector3(at.x, 0, at.y)) + Vector3.up * .15f;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 60;
            label.characterSize = .13f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = MapCAccent;
        }
    }

    /// <summary>任務ごとに生成した床メッシュを、ステージ破棄時に解放する。</summary>
    public sealed class ArenaMeshLifetime : MonoBehaviour
    {
        void OnDestroy()
        {
            var mesh = GetComponent<MeshFilter>().sharedMesh;
            if (mesh) Destroy(mesh);
        }
    }
}
