using UnityEngine;

namespace Wings
{
    /// <summary>
    /// WingsGameのAI観察画面をまとめたpartial定義。本体と同じクラスをファイル単位で分けている。
    /// </summary>
    public sealed partial class WingsGame
    {
        /// <summary>
        /// F1で味方AIの命令・FSM状態・判断理由・認識・経路を表示する。操作キャラの次の編成番号を観察する。
        /// 表示するだけの処理であり、表示のために得た情報をAIへ追加することはない。
        /// </summary>
        void AIDebugUI()
        {
            if (!Active || Active.Brain == null)
                return;
            // 右上のミニマップと重ならず、左下の編成表示より上に収める。
            Box(new Rect(26, 221, 460, 340), new Color(.025f, .055f, .08f, .96f));
            var inspected = Party[(activeIndex + 1) % Party.Count];
            var brain = inspected.Brain;
            var intent = brain.Intent;
            Text(new Rect(44, 237, 426, 34), "AI DEBUG / F1  " + inspected.Definition.displayName, 20, Cyan, FontStyle.Bold);
            string info = "命令: " + brain.Order
                + "\n状態: " + intent.State
                + "\n理由: " + intent.Reason
                + "\n直接目視: " + brain.Sensor.Visible.Count
                + "\n目標ID: " + (intent.Target?.Id ?? 0)
                + "\n射撃許可: " + intent.Fire
                + "\n経路: " + (brain.Navigation.HasPath ? "NavMesh"
                : "待機 / 近距離回避")
                + "\n目的地: " + intent.Destination.ToString("F1");
            Text(new Rect(44, 279, 426, 184), info, 15, White);
            int row = 0;
            foreach (var c in brain.Sensor.Known)
            {
                if (row++ >= 3)
                    break;
                Text(new Rect(44, 470 + (row - 1) * 20, 426, 20),
                    "記憶 " + c.Id
                    + "  " + c.Source
                    + "  信頼度 " + c.Confidence.ToString("0.00"),
                    13,
                    c.Visible ? Cyan
                    : Muted);
            }

            Text(new Rect(44, 536, 426, 22), "デバッグ表示はAIの入力には使いません", 12, Muted);
        }
    }
}
