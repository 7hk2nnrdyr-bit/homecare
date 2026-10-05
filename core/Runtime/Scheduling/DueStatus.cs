namespace HomeCare.Core.Scheduling
{
    /// <summary>AR表示やリストで使う、期限の状態（赤・黄・緑）。</summary>
    public enum DueStatus
    {
        /// <summary>🟢 問題なし</summary>
        Ok,

        /// <summary>🟡 期限が近い（期限当日を含む）</summary>
        DueSoon,

        /// <summary>🔴 期限切れ</summary>
        Overdue,
    }
}
