using System.Collections.Generic;
using UnityEngine;
using Wings.AI;

namespace Wings
{
    /// <summary>
    /// 北を上にした共通地図。地形・味方位置は任務情報、敵は各味方の認識済みContactだけを表示する。
    /// 敵一覧や敵の現在のTransformを地図の入力にしない。描画からセンサーの更新も行わない。
    /// </summary>
    public sealed partial class WingsGame
    {
        Texture2D minimapTerrain;
        readonly List<BoxCollider> minimapWalls = new List<BoxCollider>();
        readonly Dictionary<int, Contact> minimapContacts = new Dictionary<int, Contact>();
        float nextMinimapWalls, nextMinimapContacts;
        static readonly Rect MinimapMapRect = new Rect(1282, 150, 270, 270);

        void ClearMinimap()
        {
            if (minimapTerrain)
                Destroy(minimapTerrain);
            minimapTerrain = null;
            minimapWalls.Clear();
            minimapContacts.Clear();
            nextMinimapWalls = 0;
            nextMinimapContacts = 0;
        }

        /// <summary>地形の高さを色へ変換。高台を明るく、低地を青くし、mapCの段差も判別しやすくする。</summary>
        static Color MinimapHeightColor(float height)
        {
            return Color.Lerp(new Color(.08f, .16f, .24f), new Color(.34f, .39f, .32f),
                Mathf.InverseLerp(-4, 12, height));
        }

        void PrepareMinimapTerrain()
        {
            if (minimapTerrain)
                return;
            const int resolution = 192;
            minimapTerrain = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            minimapTerrain.name = "WINGS minimap heightfield";
            minimapTerrain.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[resolution * resolution];
            float extent = Arena.HalfExtent + 2;
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    var point = new Vector3((x + .5f) / resolution * extent * 2 - extent, 0,
                        (z + .5f) / resolution * extent * 2 - extent);
                    pixels[z * resolution + x] = MinimapHeightColor(Arena.Ground(point).y);
                }
            minimapTerrain.SetPixels(pixels);
            minimapTerrain.Apply(false, true);
        }

        /// <summary>
        /// 全味方の認識を統合する。同じ敵の重複は目視を優先し、次に元の観測時刻が新しい情報を採用する。
        /// 記憶の座標を実体から読み直さず、表示時にも有効期限を確認する。ポーズ中はTime.timeも止まる。
        /// </summary>
        void CollectMinimapContacts(float now)
        {
            minimapContacts.Clear();
            foreach (var pilot in Party)
            {
                if (!pilot || pilot.Downed || pilot.Brain == null)
                    continue;
                var sensor = pilot.Brain.Sensor;
                foreach (var contact in sensor.Known)
                {
                    if (!contact.Subject || !contact.Subject.Alive || !contact.Subject.Invader)
                        continue;
                    float age = Mathf.Max(0, now - contact.SeenAt);
                    if (age >= sensor.Settings.memorySeconds)
                        continue;
                    bool visible = contact.Visible && age <= sensor.Settings.scanInterval + .05f;
                    if (minimapContacts.TryGetValue(contact.Id, out var previous)
                        && (previous.Visible && !visible
                        || previous.Visible == visible && previous.SeenAt >= contact.SeenAt))
                        continue;
                    var marker = contact.Snapshot();
                    marker.Visible = visible;
                    marker.Confidence = Mathf.Clamp01(1 - age / sensor.Settings.memorySeconds);
                    minimapContacts[marker.Id] = marker;
                }
            }
        }

        /// <summary>+Xが右、+Zが上。A/Bは64m、Cは128mの地形全体を同じ枠へ収める。</summary>
        static Vector2 MinimapPoint(Vector3 world)
        {
            float extent = Arena.HalfExtent + 2;
            return new Vector2((world.x / extent + 1) * .5f * MinimapMapRect.width,
                (1 - world.z / extent) * .5f * MinimapMapRect.height);
        }

        void DrawMinimapLine(Vector2 from, Vector2 to, Color color, float width = 1)
        {
            Vector2 delta = to - from;
            var previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            Box(new Rect(from.x, from.y, delta.magnitude, width), color);
            GUI.matrix = previous;
        }

        void DrawMinimapWalls()
        {
            // 壁・氷壁だけを収集し、キャラクターを含むカメラ映像は地図に使わない。
            // 0.25秒ごとの再収集で、一時的な氷壁の出現・消滅も反映する。
            if (Time.unscaledTime >= nextMinimapWalls)
            {
                nextMinimapWalls = Time.unscaledTime + .25f;
                minimapWalls.Clear();
                foreach (var wall in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
                    if (wall.enabled && !wall.isTrigger && wall.gameObject.layer == Arena.WallLayer)
                        minimapWalls.Add(wall);
            }
            foreach (var wall in minimapWalls)
            {
                if (!wall || !wall.enabled)
                    continue;
                var half = wall.size * .5f;
                Vector2 corner = MinimapPoint(wall.transform.TransformPoint(wall.center + new Vector3(-half.x, 0, -half.z)));
                Vector2 alongX = MinimapPoint(wall.transform.TransformPoint(wall.center + new Vector3(half.x, 0, -half.z))) - corner;
                Vector2 alongZ = MinimapPoint(wall.transform.TransformPoint(wall.center + new Vector3(-half.x, 0, half.z))) - corner;
                var previous = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(alongX.y, alongX.x) * Mathf.Rad2Deg, corner);
                Box(new Rect(corner.x, corner.y - alongZ.magnitude, alongX.magnitude, alongZ.magnitude),
                    new Color(.62f, .68f, .66f));
                GUI.matrix = previous;
            }
        }

        void MinimapUI()
        {
            PrepareMinimapTerrain();
            // OnGUIは1フレームに複数回呼ばれる。認識の統合は10Hzに抑え、描画のたびにコピーを作らない。
            if (Time.unscaledTime >= nextMinimapContacts)
            {
                nextMinimapContacts = Time.unscaledTime + .1f;
                CollectMinimapContacts(Time.time);
            }
            Box(new Rect(1264, 112, 306, 356), Panel);
            Text(new Rect(1282, 119, 235, 26), "SQUAD RADAR", 17, Cyan, FontStyle.Bold);
            Text(new Rect(1516, 119, 36, 26), "N ↑", 13, White);
            GUI.BeginGroup(MinimapMapRect);
            GUI.DrawTexture(new Rect(0, 0, 270, 270), minimapTerrain);
            for (int line = 1; line < 4; line++)
            {
                float at = line * MinimapMapRect.width / 4;
                Box(new Rect(at, 0, 1, 270), new Color(1, 1, 1, .08f));
                Box(new Rect(0, at, 270, 1), new Color(1, 1, 1, .08f));
            }
            DrawMinimapWalls();
            Vector2 tower = MinimapPoint(Vector3.zero);
            Box(new Rect(tower.x - 4, tower.y - 4, 8, 8), Cyan);
            foreach (var contact in minimapContacts.Values)
            {
                Vector2 point = MinimapPoint(contact.Position);
                Color color = contact.Visible ? Red : new Color(1, .7f, .28f, .35f + contact.Confidence * .6f);
                Disc(new Rect(point.x - 4, point.y - 4, 8, 8), color);
                if (!contact.Visible)
                    Disc(new Rect(point.x - 2, point.y - 2, 4, 4), Ink);
            }
            for (int index = 0; index < Party.Count; index++)
            {
                var pilot = Party[index];
                if (!pilot)
                    continue;
                Vector2 point = MinimapPoint(pilot.transform.position);
                Color color = pilot.Downed ? Muted : pilot.Definition.color;
                if (pilot == Active)
                    Disc(new Rect(point.x - 6, point.y - 6, 12, 12), White);
                Disc(new Rect(point.x - 4, point.y - 4, 8, 8), color);
                DrawMinimapLine(point, point + new Vector2(pilot.transform.forward.x, -pilot.transform.forward.z) * 9, color, 2);
                Text(new Rect(point.x + 5, point.y - 9, 20, 20), (index + 1).ToString(), 12, color, FontStyle.Bold);
            }
            GUI.EndGroup();
            Text(new Rect(1282, 426, 270, 18), "色付き＝味方  /  水色の四角＝タワー", 12, Muted);
            Text(new Rect(1282, 446, 270, 18), "赤＝目視敵  /  橙の輪＝最終認識位置", 12, Muted);
        }
    }
}
