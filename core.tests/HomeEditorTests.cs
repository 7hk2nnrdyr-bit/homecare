using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class HomeEditorTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc);
        private static readonly Recurrence EveryThreeMonths = new Recurrence(3, RecurrenceUnit.Month);
        private static readonly Recurrence Weekly = new Recurrence(1, RecurrenceUnit.Week);

        private static HomeEditor NewEditor() =>
            HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家", () => FixedNow);

        [Fact]
        public void 何も保存されていなければ空の家を作る()
        {
            var editor = NewEditor();

            Assert.Equal("わが家", editor.Home.name);
            Assert.Equal(HomeData.CurrentSchemaVersion, editor.Home.schemaVersion);
            Assert.False(string.IsNullOrEmpty(editor.Home.id));
            Assert.Empty(editor.Home.rooms);
        }

        [Fact]
        public void 保存したデータを読み直すとポイントの位置が残っている()
        {
            var repository = new InMemoryHomeRepository();
            var first = HomeEditor.LoadOrCreate(repository, "わが家", () => FixedNow);
            var room = first.AddRoom("リビング");
            first.AddPoint(room.id, "エアコン", new Vec3(0.5f, 2.1f, -1.2f));
            repository.Save(first.Home);

            var second = HomeEditor.LoadOrCreate(repository, "わが家", () => FixedNow);
            var point = second.PointsInRoom(room.id).Single();

            Assert.Equal("エアコン", point.name);
            Assert.Equal(new[] { 0.5f, 2.1f, -1.2f }, point.positionInRoom);
            Assert.Equal("2026-10-06T05:00:00.000Z", point.createdAt);
        }

        [Fact]
        public void 同じ名前の部屋は作り直さない()
        {
            var editor = NewEditor();

            var first = editor.FindOrAddRoom("リビング");
            var again = editor.FindOrAddRoom("リビング");

            Assert.Equal(first.id, again.id);
            Assert.Single(editor.Home.rooms);
        }

        [Fact]
        public void 存在しない部屋にはポイントを置けない()
        {
            var editor = NewEditor();

            Assert.Throws<ArgumentException>(() => editor.AddPoint("no-such-room", "換気扇", Vec3.Zero));
        }

        [Fact]
        public void タスクの周期と期限は文字で保存し計算に戻せる()
        {
            var editor = NewEditor();
            var room = editor.AddRoom("リビング");
            var point = editor.AddPoint(room.id, "エアコン", Vec3.Zero);

            var task = editor.AddTask(point.id, "フィルター掃除", EveryThreeMonths, new DateTime(2026, 11, 1));

            Assert.Equal(3, task.recurrence.every);
            Assert.Equal("month", task.recurrence.unit);
            Assert.Equal("2026-11-01", task.firstDueDate);
            Assert.Equal(room.id, task.roomId);
            Assert.Equal(new DateTime(2026, 11, 1), HomeEditor.NextDueDate(task));
        }

        [Fact]
        public void ポイントの色は一番悪いタスクに合わせる()
        {
            var editor = NewEditor();
            var room = editor.AddRoom("キッチン");
            var point = editor.AddPoint(room.id, "換気扇", Vec3.Zero);
            var today = new DateTime(2026, 10, 6);

            editor.AddTask(point.id, "フィルター交換", EveryThreeMonths, new DateTime(2026, 12, 1));
            Assert.Equal(DueStatus.Ok, editor.StatusOfPoint(point.id, today));

            editor.AddTask(point.id, "油汚れ拭き", Weekly, new DateTime(2026, 10, 3));
            Assert.Equal(DueStatus.Overdue, editor.StatusOfPoint(point.id, today));
        }

        [Fact]
        public void タスクの無いポイントは色を持たない()
        {
            var editor = NewEditor();
            var room = editor.AddRoom("玄関");
            var point = editor.AddPoint(room.id, "火災報知器", Vec3.Zero);

            Assert.Null(editor.StatusOfPoint(point.id, new DateTime(2026, 10, 6)));
        }
    }
}
