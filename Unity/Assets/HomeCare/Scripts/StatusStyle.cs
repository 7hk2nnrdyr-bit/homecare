using HomeCare.Core.Scheduling;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>期限の状態（赤・黄・緑）の色と文字の印。AR画面とリスト画面で同じものを使う。</summary>
    public static class StatusStyle
    {
        static readonly Color k_OkColor = new Color(0.2f, 0.75f, 0.3f);
        static readonly Color k_DueSoonColor = new Color(0.95f, 0.8f, 0.1f);
        static readonly Color k_OverdueColor = new Color(0.9f, 0.2f, 0.2f);

        /// <summary>タスクが無いポイント（null）は白にする。</summary>
        public static Color ColorOf(DueStatus? status)
        {
            switch (status)
            {
                case DueStatus.Overdue:
                    return k_OverdueColor;
                case DueStatus.DueSoon:
                    return k_DueSoonColor;
                case DueStatus.Ok:
                    return k_OkColor;
                default:
                    return Color.white;
            }
        }

        /// <summary>色だけに頼らないよう、文字の印も付ける。</summary>
        public static string MarkOf(DueStatus status)
        {
            switch (status)
            {
                case DueStatus.Overdue:
                    return "【期限切れ】";
                case DueStatus.DueSoon:
                    return "【もうすぐ】";
                default:
                    return "【OK】";
            }
        }
    }
}
