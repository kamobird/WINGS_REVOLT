using UnityEngine;

namespace Wings
{
    public sealed partial class WingsGame
    {
        /// <summary>
        /// mapC専用の俯瞰観察。操作中の1羽もAIへ任せ、4羽の連携を観察できる。
        /// カメラに見える実体を、センサーの認識情報へ追加することはない。
        /// </summary>
        public bool ObservationMode { get; private set; }

        public void ToggleObservation()
        {
            if (Stage != 2 || State != GameState.Playing)
                return;
            ObservationMode = !ObservationMode;
            Viewport.orthographic = ObservationMode;
            if (ObservationMode)
            {
                ShowAIDebug = true;
                Active?.CancelCharge();
            }
            else if (Active)
            {
                RenderedVisionFans = 0;
                // 遠い俯瞰位置から数フレームかけて落下する表示を避け、操作カメラへ即座に戻す。
                Vector3 pivot = Active.transform.position + Vector3.up * 1.8f;
                Quaternion rotation = Quaternion.Euler(cameraPitch, CameraYaw, 0);
                Vector3 offset = rotation * new Vector3(.85f, .45f, -5.8f);
                float distance = offset.magnitude;
                if (Physics.SphereCast(pivot, .22f, offset.normalized, out var hit, distance, Arena.OcclusionMask))
                    distance = Mathf.Max(.5f, hit.distance - .2f);
                Viewport.transform.SetPositionAndRotation(pivot + offset.normalized * distance, rotation);
            }

            UpdateCursor();
            Notify(ObservationMode ? "俯瞰観察 / 全員AI操作。右クリックで部隊指示、F2で操作へ戻る" : "キャラクター操作へ復帰", 4);
        }

        void UpdateObservationCamera()
        {
            Viewport.orthographic = true;
            Viewport.orthographicSize = 78;
            Viewport.transform.position = new Vector3(0, 112, -78);
            Viewport.transform.LookAt(new Vector3(0, 2, 0));
        }

        /// <summary>既に計算された経路だけを描く。線を描くために新しい探索や認識を行わない。</summary>
        void MapCObservationUI()
        {
            if (Stage != 2 || !Active)
                return;
            Text(new Rect(45, 130, 1060, 28),
                ObservationMode ? "F2 操作へ戻る  /  全員AI  /  経路線 = F1で観察中の味方"
                : "F2 俯瞰観察・全員AI  /  F1 判断理由",
                17,
                Cyan);
            if (!ObservationMode)
                return;
            Text(new Rect(45, 165, 1000, 26), "西高台 +8m  /  北段丘 +4m  /  東低地 -4m  /  北西 +12m", 16, Muted);
            DrawSquadVision();
            Text(new Rect(45, 195, 1000, 26), "キャラ色の扇形＝視野の目安（遮蔽を反映） / 線＝経路", 16, Muted);
            var inspected = Party[(activeIndex + 1) % Party.Count];
            var corners = inspected.Brain.Navigation.Corners;
            for (int i = 1; i < corners.Count; i++)
                ObservationLine(corners[i - 1] + Vector3.up * .3f, corners[i] + Vector3.up * .3f, inspected.Definition.color);
            foreach (var pilot in Party)
            {
                var at = Viewport.WorldToViewportPoint(pilot.transform.position + Vector3.up * 2);
                if (at.z > 0)
                    Text(new Rect(at.x * 1600 - 70, (1 - at.y) * 900 - 23, 140, 26),
                        (Party.IndexOf(pilot) + 1)
                        + " " + pilot.Definition.displayName
                        + " / " + new[] { "追従", "防衛", "攻撃" }[(int)OrderFor(pilot)],
                        15,
                        pilot.Definition.color,
                        FontStyle.Bold,
                        TextAnchor.MiddleCenter);
            }
        }

        void ObservationLine(Vector3 a, Vector3 b, Color color, float width = 3)
        {
            Vector3 from = Viewport.WorldToViewportPoint(a), to = Viewport.WorldToViewportPoint(b);
            if (from.z <= 0 || to.z <= 0)
                return;
            Vector2 start = new Vector2(from.x * 1600, (1 - from.y) * 900);
            Vector2 end = new Vector2(to.x * 1600, (1 - to.y) * 900);
            Vector2 delta = end - start;
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            Box(new Rect(start.x, start.y, delta.magnitude, width), color);
            GUI.matrix = previous;
        }
    }
}
