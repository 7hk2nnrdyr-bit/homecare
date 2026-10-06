using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class CompleteTaskTests
    {
        private static readonly Recurrence EveryThreeMonths = new Recurrence(3, RecurrenceUnit.Month);

        private static (HomeEditor editor, TaskData task) NewTask(DateTime firstDue)
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家",
                () => new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc));
            var room = editor.AddRoom("リビング");
            var point = editor.AddPoint(room.id, "エアコン", Vec3.Zero);
            var task = editor.AddTask(point.id, "フィルター掃除", EveryThreeMonths, firstDue);
            return (editor, task);
        }

        [Fact]
        public void 完了すると次の期限は完了日に周期を足した日になり緑に戻る()
        {
            var (editor, task) = NewTask(new DateTime(2026, 9, 1));
            var today = new DateTime(2026, 10, 6);
            Assert.Equal(DueStatus.Overdue, HomeEditor.StatusOf(task, today));

            editor.CompleteTask(task.id, today);

            Assert.Equal("2026-10-06", task.lastDoneDate);
            Assert.Equal(new DateTime(2027, 1, 6), HomeEditor.NextDueDate(task));
            Assert.Equal(DueStatus.Ok, HomeEditor.StatusOf(task, today));
        }

        [Fact]
        public void 完了するたびに実施記録が追加され新しい順に並ぶ()
        {
            var (editor, task) = NewTask(new DateTime(2026, 9, 1));

            editor.CompleteTask(task.id, new DateTime(2026, 9, 2));
            editor.CompleteTask(task.id, new DateTime(2026, 10, 6));

            var dates = editor.CompletionsOf(task.id).Select(c => c.doneDate).ToArray();
            Assert.Equal(new[] { "2026-10-06", "2026-09-02" }, dates);
        }

        [Fact]
        public void 過去の日付で記録しても前回実施日は巻き戻らない()
        {
            var (editor, task) = NewTask(new DateTime(2026, 9, 1));

            editor.CompleteTask(task.id, new DateTime(2026, 10, 6));
            editor.CompleteTask(task.id, new DateTime(2026, 8, 1));

            Assert.Equal("2026-10-06", task.lastDoneDate);
            Assert.Equal(2, editor.CompletionsOf(task.id).Count());
        }

        [Fact]
        public void 実施記録の一覧が無い古いデータでも完了できる()
        {
            var old = new HomeData { id = "h1", name = "わが家", completions = null };
            var editor = new HomeEditor(old);
            var room = editor.AddRoom("リビング");
            var point = editor.AddPoint(room.id, "エアコン", Vec3.Zero);
            var task = editor.AddTask(point.id, "フィルター掃除", EveryThreeMonths, new DateTime(2026, 9, 1));

            editor.CompleteTask(task.id, new DateTime(2026, 10, 6));

            Assert.Single(old.completions);
        }

        [Fact]
        public void 存在しないタスクは完了できない()
        {
            var (editor, _) = NewTask(new DateTime(2026, 9, 1));

            Assert.Throws<ArgumentException>(() => editor.CompleteTask("no-such-task", DateTime.Today));
        }

        [Theory]
        [InlineData(2026, 10, 3, "3日超過")]
        [InlineData(2026, 10, 6, "今日まで")]
        [InlineData(2026, 10, 11, "あと5日")]
        public void 期限の状態を文字で表す(int y, int m, int d, string expected)
        {
            Assert.Equal(expected, DueLabel.For(new DateTime(2026, 10, 6), new DateTime(y, m, d)));
        }

        [Fact]
        public void 周期を文字で表す()
        {
            Assert.Equal("3か月ごと", DueLabel.For(EveryThreeMonths));
            Assert.Equal("毎週", DueLabel.For(new Recurrence(1, RecurrenceUnit.Week)));
        }
    }
}
