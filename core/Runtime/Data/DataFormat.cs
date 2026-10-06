using System;
using System.Globalization;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;

namespace HomeCare.Core.Data
{
    /// <summary>保存用の文字列や配列と、計算用の型（DateTime、Recurrence、Vec3）を相互に変換する。</summary>
    public static class DataFormat
    {
        private const string DateFormat = "yyyy-MM-dd";
        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>期限などの「日付」。時差で1日ずれないよう、時刻は持たない。</summary>
        public static string FormatDate(DateTime date) =>
            date.ToString(DateFormat, CultureInfo.InvariantCulture);

        /// <summary>空なら null（例：一度も実施していないタスクの lastDoneDate）。</summary>
        public static DateTime? ParseDate(string text) =>
            string.IsNullOrEmpty(text)
                ? (DateTime?)null
                : DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);

        /// <summary>作成・更新時刻。UTC（世界標準時）で持つ。</summary>
        public static string FormatTimestamp(DateTime utc) =>
            utc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

        public static RecurrenceData ToData(Recurrence recurrence) => new RecurrenceData
        {
            every = recurrence.Every,
            unit = recurrence.Unit.ToString().ToLowerInvariant(),
        };

        public static Recurrence ToRecurrence(RecurrenceData data)
        {
            if (!Enum.TryParse(data.unit, true, out RecurrenceUnit unit))
            {
                throw new FormatException($"周期の単位が読めません: {data.unit}");
            }
            return new Recurrence(data.every, unit);
        }

        public static float[] ToArray(Vec3 v) => new[] { v.X, v.Y, v.Z };

        public static Vec3 ToVec3(float[] values) => new Vec3(values[0], values[1], values[2]);
    }
}
