using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class ReminderPlannerTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 7);
        private static readonly TimeSpan NineAm = TimeSpan.FromHours(9);
        private static readonly Recurrence Monthly = new Recurrence(1, RecurrenceUnit.Month);

        private static HomeEditor NewHome(out RoomData room)
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家");
            room = editor.AddRoom("リビング");
            return editor;
        }

        private static TaskData Add(HomeEditor editor, RoomData room, string place, string title, DateTime firstDue)
        {
            var point = editor.AddPoint(room.id, place, Vec3.Zero);
            return editor.AddTask(point.id, title, Monthly, firstDue);
        }

        [Fact]
        public void 期限の日の決めた時刻に知らせる()
        {
            var editor = NewHome(out var room);
            Add(editor, room, "エアコン", "フィルター掃除", Today.AddDays(3));

            var reminders = ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm);

            var first = reminders.First();
            Assert.Equal(Today.AddDays(3).AddHours(9), first.FireAt);
            Assert.Equal("今日が期限のやることが1件あります", first.Title);
            Assert.Equal("エアコンのフィルター掃除", first.Body);
        }

        [Fact]
        public void 同じ日のやることは1回にまとめ_名前は3件まで()
        {
            var editor = NewHome(out var room);
            var due = Today.AddDays(1);
            Add(editor, room, "エアコン", "フィルター掃除", due);
            Add(editor, room, "換気扇", "油汚れ", due);
            Add(editor, room, "窓", "サッシ掃除", due);
            Add(editor, room, "床", "ワックス", due);

            var reminder = ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm).Single(r => r.FireAt.Date == due);

            Assert.Equal("今日が期限のやることが4件あります", reminder.Title);
            Assert.EndsWith("ほか1件", reminder.Body);
        }

        [Fact]
        public void 期限切れは7日ごとにもう一度知らせる()
        {
            var editor = NewHome(out var room);
            Add(editor, room, "エアコン", "フィルター掃除", Today.AddDays(-7));

            var days = ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm).Select(r => r.FireAt.Date).ToList();

            Assert.Equal(new[] { Today, Today.AddDays(7), Today.AddDays(14), Today.AddDays(21), Today.AddDays(28) }, days);
        }

        [Fact]
        public void 期限の日と期限切れが重なった日は両方を書く()
        {
            var editor = NewHome(out var room);
            Add(editor, room, "エアコン", "フィルター掃除", Today);
            Add(editor, room, "換気扇", "油汚れ", Today.AddDays(-14));

            var reminder = ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm).First();

            Assert.Equal("今日が期限1件・期限切れ1件", reminder.Title);
            Assert.Equal("エアコンのフィルター掃除、換気扇の油汚れ", reminder.Body);
        }

        [Fact]
        public void 時刻を過ぎた今日の分は予約しない()
        {
            var editor = NewHome(out var room);
            Add(editor, room, "エアコン", "フィルター掃除", Today);

            var reminders = ReminderPlanner.Plan(editor, Today.AddHours(10), NineAm);

            Assert.DoesNotContain(reminders, r => r.FireAt.Date == Today);
            // 期限切れとして7日後にもう一度
            Assert.Equal(Today.AddDays(7).AddHours(9), reminders.First().FireAt);
        }

        [Fact]
        public void 完了や削除をしたやることは知らせない()
        {
            var editor = NewHome(out var room);
            var done = Add(editor, room, "エアコン", "フィルター掃除", Today.AddDays(2));
            var removed = Add(editor, room, "換気扇", "油汚れ", Today.AddDays(2));

            editor.CompleteTask(done.id, Today);
            editor.DeleteTask(removed.id);

            // 完了したやることは次回（1か月後）まで知らせない。30日先までなので予定は無い
            Assert.Empty(ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm));
        }

        [Fact]
        public void やることが無ければ予定も無い()
        {
            var editor = NewHome(out _);

            Assert.Empty(ReminderPlanner.Plan(editor, Today.AddHours(7), NineAm));
        }
    }
}
