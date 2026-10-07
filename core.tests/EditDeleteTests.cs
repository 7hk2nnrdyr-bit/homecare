using System;
using System.Linq;
using System.Text.Json;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class EditDeleteTests
    {
        private static readonly string[] Available = { "M01", "M02", "M03" };
        private static readonly Recurrence Monthly = new Recurrence(1, RecurrenceUnit.Month);
        private static readonly DateTime Today = new DateTime(2026, 10, 7);
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        private DateTime _now = new DateTime(2026, 10, 7, 1, 0, 0, DateTimeKind.Utc);

        private HomeEditor NewHome() => HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家", () => _now);

        private void Later() => _now = _now.AddMinutes(1);

        private static HomeData Copy(HomeData home) =>
            JsonSerializer.Deserialize<HomeData>(JsonSerializer.Serialize(home, Json), Json);

        private static TaskData AddPlace(HomeEditor editor, RoomData room, string place, string title)
        {
            var point = editor.AddPoint(room.id, place, Vec3.Zero);
            return editor.AddTask(point.id, title, Monthly, new DateTime(2026, 11, 1));
        }

        [Fact]
        public void やることの名前と周期と最初の期限を変えられる()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            var before = task.updatedAt;
            Later();

            editor.UpdateTask(task.id, " 掃除 ", new Recurrence(2, RecurrenceUnit.Week), new DateTime(2026, 10, 20));

            Assert.Equal("掃除", task.title);
            Assert.Equal(new DateTime(2026, 10, 20), HomeEditor.NextDueDate(task));
            Assert.Equal(new Recurrence(2, RecurrenceUnit.Week), DataFormat.ToRecurrence(task.recurrence));
            Assert.NotEqual(before, task.updatedAt);
        }

        [Fact]
        public void 何も変えずに保存しても更新日時は変わらない()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            var before = task.updatedAt;
            Later();

            editor.UpdateTask(task.id, "フィルター掃除", Monthly, new DateTime(2026, 11, 1));
            editor.RenamePoint(task.pointId, "エアコン");
            editor.RenameRoom(room.id, "リビング");

            Assert.Equal(before, task.updatedAt);
            Assert.Equal(before, editor.FindPoint(task.pointId).updatedAt);
            Assert.Equal(before, room.updatedAt);
        }

        [Fact]
        public void 完了したことのあるやることは周期を変えると前回実施日から数え直す()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            editor.CompleteTask(task.id, new DateTime(2026, 10, 1));

            editor.UpdateTask(task.id, task.title, new Recurrence(1, RecurrenceUnit.Week), new DateTime(2026, 11, 1));

            Assert.Equal(new DateTime(2026, 10, 8), HomeEditor.NextDueDate(task));
            Assert.Single(editor.CompletionsOf(task.id));
        }

        [Fact]
        public void 場所と部屋の名前を変えられる_空や同じ名前の部屋は断る()
        {
            var editor = NewHome();
            var living = editor.AddRoomWithMarker("リビング", Available);
            editor.AddRoomWithMarker("寝室", Available);
            var task = AddPlace(editor, living, "エアコン", "フィルター掃除");

            editor.RenamePoint(task.pointId, "リビングのエアコン");
            editor.RenameRoom(living.id, "居間");

            var item = editor.DueList(Today).Single();
            Assert.Equal("リビングのエアコン", item.Point.name);
            Assert.Equal("居間", item.Room.name);
            Assert.Throws<ArgumentException>(() => editor.RenameRoom(living.id, "寝室"));
            Assert.Throws<ArgumentException>(() => editor.RenameRoom(living.id, " "));
            Assert.Throws<ArgumentException>(() => editor.RenamePoint(task.pointId, ""));
        }

        [Fact]
        public void やることを削除すると一覧から消え_ほかにやることの無い場所も消える()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            var other = AddPlace(editor, room, "換気扇", "油汚れ");

            var pointDeleted = editor.DeleteTask(task.id);

            Assert.True(pointDeleted);
            Assert.Equal(new[] { "油汚れ" }, editor.DueList(Today).Select(i => i.Task.title));
            Assert.Null(editor.FindPoint(task.pointId));
            Assert.Single(editor.PointsInRoom(room.id));
            // 消さずに印を付けるだけ（同期でほかの端末へ伝えるため）
            Assert.NotNull(editor.Home.tasks.Single(t => t.id == task.id).deletedAt);
        }

        [Fact]
        public void ほかにやることのある場所はやることを削除しても残る()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            editor.AddTask(task.pointId, "室外機の点検", Monthly, new DateTime(2026, 11, 1));

            var pointDeleted = editor.DeleteTask(task.id);

            Assert.False(pointDeleted);
            Assert.NotNull(editor.FindPoint(task.pointId));
            Assert.Equal(new[] { "室外機の点検" }, editor.DueList(Today).Select(i => i.Task.title));
        }

        [Fact]
        public void 場所を削除するとその場所のやることも消える()
        {
            var editor = NewHome();
            var room = editor.AddRoomWithMarker("リビング", Available);
            var task = AddPlace(editor, room, "エアコン", "フィルター掃除");
            editor.AddTask(task.pointId, "室外機の点検", Monthly, new DateTime(2026, 11, 1));

            editor.DeletePoint(task.pointId);

            Assert.Empty(editor.DueList(Today));
            Assert.Empty(editor.PointsInRoom(room.id));
        }

        [Fact]
        public void 部屋を削除すると中の場所とやることも消え_マーカーがまた使える()
        {
            var editor = NewHome();
            var living = editor.AddRoomWithMarker("リビング", Available);
            var bedroom = editor.AddRoomWithMarker("寝室", Available);
            AddPlace(editor, living, "エアコン", "フィルター掃除");
            AddPlace(editor, bedroom, "窓", "サッシ掃除");

            editor.DeleteRoom(living.id);

            Assert.Equal(new[] { "寝室" }, editor.ActiveRooms().Select(r => r.name));
            Assert.Equal(new[] { "サッシ掃除" }, editor.DueList(Today).Select(i => i.Task.title));
            Assert.Equal(new[] { "M01", "M03" }, editor.UnusedMarkerIds(Available));
            var kitchen = editor.AddRoomWithMarker("キッチン", Available);
            Assert.Equal("M01", editor.MarkerOf(kitchen.id));
        }

        [Fact]
        public void 部屋が1つだけのときは削除できない()
        {
            var editor = NewHome();
            var living = editor.AddRoomWithMarker("リビング", Available);

            var error = Assert.Throws<ArgumentException>(() => editor.DeleteRoom(living.id));

            Assert.Contains("1つだけ", error.Message);
            Assert.Single(editor.ActiveRooms());
        }

        [Fact]
        public void 修正と削除はほかの端末に伝わる()
        {
            var deviceA = NewHome();
            var living = deviceA.AddRoomWithMarker("リビング", Available);
            var bedroom = deviceA.AddRoomWithMarker("寝室", Available);
            var aircon = AddPlace(deviceA, living, "エアコン", "フィルター掃除");
            var fan = AddPlace(deviceA, living, "換気扇", "油汚れ");
            AddPlace(deviceA, bedroom, "窓", "サッシ掃除");
            var deviceB = new HomeEditor(Copy(deviceA.Home), () => _now);
            Later();

            deviceA.UpdateTask(aircon.id, "フィルターを洗う", new Recurrence(2, RecurrenceUnit.Week), new DateTime(2026, 10, 20));
            deviceA.DeleteTask(fan.id);
            deviceA.DeleteRoom(bedroom.id);
            var result = HomeImporter.Import(deviceB.Home, Copy(deviceA.Home));
            deviceB = new HomeEditor(result.Home, () => _now);

            Assert.Equal(ImportOutcome.Merged, result.Outcome);
            Assert.Equal(new[] { "フィルターを洗う" }, deviceB.DueList(Today).Select(i => i.Task.title));
            Assert.Equal(new[] { "リビング" }, deviceB.ActiveRooms().Select(r => r.name));
            Assert.Single(deviceB.PointsInRoom(living.id));
        }

        [Fact]
        public void 削除された部屋にほかの端末で足した場所は一覧に出さない()
        {
            var deviceA = NewHome();
            var living = deviceA.AddRoomWithMarker("リビング", Available);
            var bedroom = deviceA.AddRoomWithMarker("寝室", Available);
            AddPlace(deviceA, living, "エアコン", "フィルター掃除");
            var deviceB = new HomeEditor(Copy(deviceA.Home), () => _now);
            Later();

            // 同じころに、Aは寝室を削除し、Bは寝室に場所を足した
            deviceA.DeleteRoom(bedroom.id);
            AddPlace(deviceB, deviceB.Home.rooms.Single(r => r.id == bedroom.id), "窓", "サッシ掃除");
            var result = HomeImporter.Import(deviceA.Home, Copy(deviceB.Home));
            var merged = new HomeEditor(result.Home, () => _now);

            Assert.Equal(ImportOutcome.Merged, result.Outcome);
            Assert.Equal(new[] { "フィルター掃除" }, merged.DueList(Today).Select(i => i.Task.title));
            Assert.Single(merged.ActiveRooms());
        }
    }
}
