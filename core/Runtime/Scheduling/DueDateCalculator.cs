using System;

namespace HomeCare.Core.Scheduling
{
    /// <summary>
    /// 次回期限と、赤・黄・緑の判定を計算する。
    /// 日付は時刻を無視して「日」単位で扱う。
    /// </summary>
    public static class DueDateCalculator
    {
        /// <summary>黄色にする期間の最短日数。</summary>
        public const int MinDueSoonDays = 3;

        /// <summary>黄色にする期間の最長日数。</summary>
        public const int MaxDueSoonDays = 14;

        /// <summary>
        /// 次回期限を求める。
        /// 実施したことがあれば「最後に実施した日 + 周期」、なければ最初の期限をそのまま使う。
        /// </summary>
        public static DateTime NextDueDate(Recurrence recurrence, DateTime firstDueDate, DateTime? lastDoneDate)
        {
            return lastDoneDate.HasValue
                ? recurrence.AddTo(lastDoneDate.Value)
                : firstDueDate.Date;
        }

        /// <summary>
        /// 黄色にする期間の日数。周期の20%を、3日〜14日の範囲に収める。
        /// 例：1週間ごと → 3日、3か月ごと → 14日。
        /// </summary>
        public static int DueSoonWindowDays(Recurrence recurrence)
        {
            var days = (int)Math.Round(recurrence.ApproximateDays * 0.2);
            return Math.Min(MaxDueSoonDays, Math.Max(MinDueSoonDays, days));
        }

        /// <summary>今日の日付と次回期限から、赤・黄・緑を判定する。</summary>
        public static DueStatus StatusOn(DateTime today, DateTime nextDueDate, Recurrence recurrence)
        {
            var daysLeft = (nextDueDate.Date - today.Date).Days;
            if (daysLeft < 0)
            {
                return DueStatus.Overdue;
            }
            return daysLeft <= DueSoonWindowDays(recurrence) ? DueStatus.DueSoon : DueStatus.Ok;
        }
    }
}
