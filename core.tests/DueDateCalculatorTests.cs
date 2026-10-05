using System;
using HomeCare.Core.Scheduling;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class DueDateCalculatorTests
    {
        private static readonly Recurrence Weekly = new Recurrence(1, RecurrenceUnit.Week);
        private static readonly Recurrence EveryThreeMonths = new Recurrence(3, RecurrenceUnit.Month);

        [Fact]
        public void 一度も実施していなければ最初の期限を使う()
        {
            var next = DueDateCalculator.NextDueDate(EveryThreeMonths, new DateTime(2026, 11, 1), null);

            Assert.Equal(new DateTime(2026, 11, 1), next);
        }

        [Fact]
        public void 実施済みなら最後に実施した日に周期を足す()
        {
            var next = DueDateCalculator.NextDueDate(EveryThreeMonths, new DateTime(2026, 1, 1), new DateTime(2026, 10, 5));

            Assert.Equal(new DateTime(2027, 1, 5), next);
        }

        [Fact]
        public void 月末をまたぐときはその月の最終日に合わせる()
        {
            var monthly = new Recurrence(1, RecurrenceUnit.Month);

            Assert.Equal(new DateTime(2027, 2, 28), monthly.AddTo(new DateTime(2027, 1, 31)));
            Assert.Equal(new DateTime(2028, 2, 29), monthly.AddTo(new DateTime(2028, 1, 31)));
        }

        [Fact]
        public void 時刻は無視して日付だけで計算する()
        {
            var next = DueDateCalculator.NextDueDate(Weekly, new DateTime(2026, 1, 1), new DateTime(2026, 10, 5, 23, 59, 0));

            Assert.Equal(new DateTime(2026, 10, 12), next);
        }

        [Theory]
        [InlineData(1, RecurrenceUnit.Week, 3)]   // 7日の20% = 1.4日 → 最短の3日
        [InlineData(1, RecurrenceUnit.Month, 6)]  // 30日の20% = 6日
        [InlineData(3, RecurrenceUnit.Month, 14)] // 90日の20% = 18日 → 最長の14日
        [InlineData(1, RecurrenceUnit.Year, 14)]
        public void 黄色の期間は周期の20パーセントを3日から14日に収める(int every, RecurrenceUnit unit, int expectedDays)
        {
            Assert.Equal(expectedDays, DueDateCalculator.DueSoonWindowDays(new Recurrence(every, unit)));
        }

        [Fact]
        public void 期限を過ぎたら赤()
        {
            var status = DueDateCalculator.StatusOn(new DateTime(2026, 10, 6), new DateTime(2026, 10, 5), Weekly);

            Assert.Equal(DueStatus.Overdue, status);
        }

        [Fact]
        public void 期限当日は黄()
        {
            var status = DueDateCalculator.StatusOn(new DateTime(2026, 10, 5), new DateTime(2026, 10, 5), Weekly);

            Assert.Equal(DueStatus.DueSoon, status);
        }

        [Fact]
        public void 黄色の期間の境目()
        {
            var due = new DateTime(2026, 10, 15);

            // 1週間ごと → 黄色は期限の3日前から
            Assert.Equal(DueStatus.DueSoon, DueDateCalculator.StatusOn(new DateTime(2026, 10, 12), due, Weekly));
            Assert.Equal(DueStatus.Ok, DueDateCalculator.StatusOn(new DateTime(2026, 10, 11), due, Weekly));
        }

        [Fact]
        public void 周期が0以下ならエラー()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Recurrence(0, RecurrenceUnit.Day));
        }
    }
}
