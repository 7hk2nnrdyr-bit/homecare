using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class DueListTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 6);
        private static readonly Recurrence Monthly = new Recurrence(1, RecurrenceUnit.Month);

        private static (HomeEditor editor, PointData point) NewEditor()
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家",
                () => new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc));
            var room = editor.AddRoom("リビング");
            var point = editor.AddPoint(room.id, "エアコン", Vec3.Zero);
            return (editor, point);
        }

        [Fact]
        public void 期限の近い順に並び期限切れが先頭になる()
        {
            var (editor, point) = NewEditor();
            editor.AddTask(point.id, "室外機の点検", Monthly, new DateTime(2026, 12, 1));
            editor.AddTask(point.id, "フィルター掃除", Monthly, new DateTime(2026, 9, 30));
            editor.AddTask(point.id, "リモコンの電池", Monthly, new DateTime(2026, 10, 10));

            var titles = editor.DueList(Today).Select(item => item.Task.title).ToArray();

            Assert.Equal(new[] { "フィルター掃除", "リモコンの電池", "室外機の点検" }, titles);
        }

        [Fact]
        public void 各行に場所と部屋と状態が入る()
        {
            var (editor, point) = NewEditor();
            editor.AddTask(point.id, "フィルター掃除", Monthly, new DateTime(2026, 9, 30));

            var item = editor.DueList(Today).Single();

            Assert.Equal("エアコン", item.Point.name);
            Assert.Equal("リビング", item.Room.name);
            Assert.Equal(new DateTime(2026, 9, 30), item.NextDue);
            Assert.Equal(DueStatus.Overdue, item.Status);
        }

        [Fact]
        public void 完了すると次の期限に合わせて並び順が変わる()
        {
            var (editor, point) = NewEditor();
            var filter = editor.AddTask(point.id, "フィルター掃除", Monthly, new DateTime(2026, 9, 30));
            editor.AddTask(point.id, "リモコンの電池", Monthly, new DateTime(2026, 10, 10));

            editor.CompleteTask(filter.id, Today);

            var titles = editor.DueList(Today).Select(item => item.Task.title).ToArray();
            Assert.Equal(new[] { "リモコンの電池", "フィルター掃除" }, titles);
        }

        [Fact]
        public void 削除したタスクは出ない()
        {
            var (editor, point) = NewEditor();
            var task = editor.AddTask(point.id, "フィルター掃除", Monthly, new DateTime(2026, 9, 30));
            task.deletedAt = "2026-10-06T05:00:00.000Z";

            Assert.Empty(editor.DueList(Today));
        }
    }
}
