using System;

namespace HomeCare.Core.Scheduling
{
    /// <summary>周期の単位。</summary>
    public enum RecurrenceUnit
    {
        Day,
        Week,
        Month,
        Year,
    }

    /// <summary>
    /// 「何日・何週・何か月・何年ごと」を表す周期。
    /// 例：3か月ごと = new Recurrence(3, RecurrenceUnit.Month)
    /// </summary>
    public readonly struct Recurrence
    {
        public int Every { get; }
        public RecurrenceUnit Unit { get; }

        public Recurrence(int every, RecurrenceUnit unit)
        {
            if (every < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(every), "周期は1以上にしてください。");
            }
            Every = every;
            Unit = unit;
        }

        /// <summary>
        /// 日付に周期を1回分足す。
        /// 月末をまたぐときはその月の最終日に合わせる（1月31日の1か月後は2月28日または29日）。
        /// </summary>
        public DateTime AddTo(DateTime date)
        {
            var day = date.Date;
            switch (Unit)
            {
                case RecurrenceUnit.Day: return day.AddDays(Every);
                case RecurrenceUnit.Week: return day.AddDays(7 * Every);
                case RecurrenceUnit.Month: return day.AddMonths(Every);
                case RecurrenceUnit.Year: return day.AddYears(Every);
                default: throw new InvalidOperationException($"未対応の単位です: {Unit}");
            }
        }

        /// <summary>
        /// 周期のおおよその日数。黄色にする期間の計算だけに使う。
        /// </summary>
        public int ApproximateDays
        {
            get
            {
                switch (Unit)
                {
                    case RecurrenceUnit.Day: return Every;
                    case RecurrenceUnit.Week: return 7 * Every;
                    case RecurrenceUnit.Month: return 30 * Every;
                    case RecurrenceUnit.Year: return 365 * Every;
                    default: throw new InvalidOperationException($"未対応の単位です: {Unit}");
                }
            }
        }
    }
}
