using System;

namespace HomeCare.Core.Scheduling
{
    /// <summary>
    /// 期限の状態を文字で表す（例：「3日超過」「あと5日」）。
    /// 色だけに頼らず、文字でも状態が分かるようにするため。
    /// </summary>
    public static class DueLabel
    {
        public static string For(DateTime today, DateTime nextDueDate)
        {
            var daysLeft = (nextDueDate.Date - today.Date).Days;
            if (daysLeft < 0)
            {
                return $"{-daysLeft}日超過";
            }
            if (daysLeft == 0)
            {
                return "今日まで";
            }
            return $"あと{daysLeft}日";
        }

        /// <summary>周期を文字にする（例：「3か月ごと」）。</summary>
        public static string For(Recurrence recurrence)
        {
            switch (recurrence.Unit)
            {
                case RecurrenceUnit.Day:
                    return recurrence.Every == 1 ? "毎日" : $"{recurrence.Every}日ごと";
                case RecurrenceUnit.Week:
                    return recurrence.Every == 1 ? "毎週" : $"{recurrence.Every}週ごと";
                case RecurrenceUnit.Month:
                    return recurrence.Every == 1 ? "毎月" : $"{recurrence.Every}か月ごと";
                default:
                    return recurrence.Every == 1 ? "毎年" : $"{recurrence.Every}年ごと";
            }
        }
    }
}
