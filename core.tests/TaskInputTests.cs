using System;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class TaskInputTests
    {
        [Fact]
        public void 正しい入力なら値に変換できる()
        {
            var ok = TaskInput.TryCreate(" エアコン ", "フィルター掃除", "3", RecurrenceUnit.Month, "2026-11-03",
                out var input, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal("エアコン", input.PointName);
            Assert.Equal("フィルター掃除", input.TaskTitle);
            Assert.Equal(3, input.Recurrence.Every);
            Assert.Equal(RecurrenceUnit.Month, input.Recurrence.Unit);
            Assert.Equal(new DateTime(2026, 11, 3), input.FirstDueDate);
        }

        [Theory]
        [InlineData("", "掃除", "3", "2026-11-03", "場所の名前")]
        [InlineData("エアコン", " ", "3", "2026-11-03", "やること")]
        [InlineData("エアコン", "掃除", "0", "2026-11-03", "周期")]
        [InlineData("エアコン", "掃除", "-1", "2026-11-03", "周期")]
        [InlineData("エアコン", "掃除", "三", "2026-11-03", "周期")]
        [InlineData("エアコン", "掃除", "3", "2026/11/03", "最初の期限")]
        [InlineData("エアコン", "掃除", "3", "2026-02-30", "最初の期限")]
        public void 入力に問題があれば理由を返す(string name, string title, string every, string due, string expectedWord)
        {
            var ok = TaskInput.TryCreate(name, title, every, RecurrenceUnit.Month, due, out var input, out var error);

            Assert.False(ok);
            Assert.Null(input);
            Assert.Contains(expectedWord, error);
        }
    }
}
