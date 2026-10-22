using System.Collections.Generic;
using UnityEngine;

namespace Wings
{
    public sealed partial class WingsGame
    {
        sealed class VisionFan
        {
            public readonly List<Vector3> outline = new List<Vector3>();
            public float until;
        }

        readonly Dictionary<int, VisionFan> visionFans = new Dictionary<int, VisionFan>();
        public int RenderedVisionFans { get; private set; }

        void ClearVisionFans()
        {
            visionFans.Clear();
            RenderedVisionFans = 0;
        }

        /// <summary>
        /// 水平方向の視野角と距離を扇形で示し、壁・地形までで切り詰める観察用表示。
        /// 上下角と身体3点による実際の発見判定を完全な2D領域へ置き換えるものではない。
        /// 観察用レイは遮蔽物だけを検査し、相手の発見・記憶へ情報を追加しない。
        /// </summary>
        public IReadOnlyList<Vector3> VisionOutline(PlayerAgent pilot)
        {
            int id = pilot.Identity.Id;
            if (!visionFans.TryGetValue(id, out var fan))
                visionFans[id] = fan = new VisionFan();
            if (Time.unscaledTime < fan.until)
                return fan.outline;
            fan.until = Time.unscaledTime + .12f;
            fan.outline.Clear();
            var sensor = pilot.Brain.Sensor;
            const int segments = 36;
            for (int i = 0; i <= segments; i++)
            {
                float angle = -sensor.Settings.horizontalFov * .5f + sensor.Settings.horizontalFov * i / segments;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * pilot.transform.forward;
                direction.y = 0;
                direction.Normalize();
                float range = sensor.Settings.range;
                if (!sensor.WallVision
                    && Physics.Raycast(sensor.Eye, direction, out var hit, range, sensor.Settings.occlusionMask, QueryTriggerInteraction.Ignore))
                    range = hit.distance;
                fan.outline.Add(Arena.Ground(pilot.transform.position + direction * range) + Vector3.up * .2f);
            }

            return fan.outline;
        }

        void DrawSquadVision()
        {
            RenderedVisionFans = 0;
            foreach (var pilot in Party)
            {
                if (!pilot || pilot.Downed)
                    continue;
                var points = VisionOutline(pilot);
                Vector3 origin = Arena.Ground(pilot.transform.position) + Vector3.up * .2f;
                Color edge = pilot.Definition.color;
                Color inner = new Color(edge.r, edge.g, edge.b, .2f);
                ObservationLine(origin, points[0], edge, 2);
                ObservationLine(origin, points[points.Count - 1], edge, 2);
                for (int i = 1; i < points.Count; i++)
                {
                    ObservationLine(points[i - 1], points[i], edge, 1.5f);
                    if (i % 3 == 0)
                        ObservationLine(origin, points[i], inner, 1);
                }

                RenderedVisionFans++;
            }
        }
    }
}
