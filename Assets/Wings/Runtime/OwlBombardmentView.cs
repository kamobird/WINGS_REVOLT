using UnityEngine;

namespace Wings
{
    public sealed partial class WingsGame
    {
        /// <summary>能力の開始・着地・操作切替でカーソルとカメラを再評価する。</summary>
        public void RefreshCombatCamera()
        {
            UpdateCursor();
            if (!Viewport || !Active || Active.OwlFlying || ObservationMode || !Viewport.orthographic)
                return;
            // 着地・操作切替後に、上空から通常カメラまでゆっくり落下する表示を避ける。
            Viewport.orthographic = false;
            var rotation = Quaternion.Euler(cameraPitch, CameraYaw, 0);
            var pivot = Active.transform.position + Vector3.up * 1.8f;
            var offset = rotation * new Vector3(.85f, .45f, -5.8f);
            float distance = offset.magnitude;
            if (Physics.SphereCast(pivot, .22f, offset.normalized, out var hit, distance, Arena.OcclusionMask))
                distance = Mathf.Max(.5f, hit.distance - .2f);
            Viewport.transform.SetPositionAndRotation(pivot + offset.normalized * distance, rotation);
        }

        void UpdateBombardmentCamera()
        {
            // 画面上部と下部のHUDを避け、ステージ四隅を含む地面全体が映る余白を取る。
            Viewport.orthographic = true;
            float ascent = Mathf.Clamp01((Active.transform.position.y - Arena.Ground(Active.transform.position).y) / Active.OwlFlightHeight);
            float fullSize = (Arena.HalfExtent + 2) * 1.45f * Mathf.Max(1, 1 / Viewport.aspect);
            Viewport.orthographicSize = Mathf.Lerp(12, fullSize, ascent);
            Viewport.farClipPlane = Mathf.Max(Viewport.farClipPlane, Active.OwlFlightHeight + 100);
            Vector3 center = Vector3.Lerp(Arena.Ground(Active.transform.position), Vector3.zero, ascent);
            Viewport.transform.SetPositionAndRotation(new Vector3(center.x, Active.OwlFlightHeight + 52, center.z), Quaternion.Euler(90, 0, 0));
        }

        void OwlBombardmentUI()
        {
            if (!Active || !Active.OwlFlying)
                return;
            Text(new Rect(450, 123, 700, 32),
                Active.OwlBombarding ? "SUPERNOVA / カーソルで照準・左クリックで広域爆撃" : "SUPERNOVA / 降下中",
                19, Active.Definition.color, FontStyle.Bold, TextAnchor.MiddleCenter);
            if (!Active.OwlBombarding || CommandWheel)
                return;
            var center = Active.AimGround();
            float radius = AbilityTuning.Current.novaRadius;
            for (int segment = 0; segment < 48; segment++)
            {
                float a = segment * Mathf.PI * 2 / 48;
                float b = (segment + 1) * Mathf.PI * 2 / 48;
                ObservationLine(Arena.Ground(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius) + Vector3.up * .2f,
                    Arena.Ground(center + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius) + Vector3.up * .2f,
                    Active.Definition.color, 2);
            }
        }
    }
}
