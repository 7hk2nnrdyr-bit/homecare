using System;
using System.Linq;
using System.Text.Json;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class HomeImporterTests
    {
        private static readonly Recurrence Monthly = new Recurrence(1, RecurrenceUnit.Month);
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        private static HomeEditor NewEditor(DateTime utcNow) =>
            HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家", () => utcNow);

        private static DateTime At(int hour) => new DateTime(2026, 10, 6, hour, 0, 0, DateTimeKind.Utc);

        /// <summary>別の端末に渡したことにする（JSONにして読み直す）。</summary>
        private static HomeData SendToOtherDevice(HomeData home) =>
            JsonSerializer.Deserialize<HomeData>(JsonSerializer.Serialize(home, Json), Json);

        private static HomeEditor HomeWithOnePoint(out PointData point, out TaskData task)
        {
            var editor = NewEditor(At(1));
            var room = editor.AddRoom("リビング");
            editor.AddMarkerLocalizer(room.id, "M01", Vec3.Zero, 0f);
            point = editor.AddPoint(room.id, "エアコン", new Vec3(1f, 2f, 3f), Quat.AngleAxis(90f, Vec3.Up));
            task = editor.AddTask(point.id, "フィルター掃除", Monthly, new DateTime(2026, 11, 1));
            return editor;
        }

        [Fact]
        public void データの無い端末では受け取ったデータをそのまま使う()
        {
            var deviceA = HomeWithOnePoint(out _, out _);
            var deviceB = NewEditor(At(2));
            var bRoom = deviceB.AddRoom("リビング");
            deviceB.AddMarkerLocalizer(bRoom.id, "M01", Vec3.Zero, 0f);

            var result = HomeImporter.Import(deviceB.Home, SendToOtherDevice(deviceA.Home));

            Assert.Equal(ImportOutcome.Adopted, result.Outcome);
            Assert.Equal(deviceA.Home.id, result.Home.id);
            var point = result.Home.points.Single();
            Assert.Equal(new[] { 1f, 2f, 3f }, point.positionInRoom);
            Assert.Equal("M01", result.Home.rooms.Single().localizers.Single().markerId);
        }

        [Fact]
        public void 向きも位置と一緒に受け渡される()
        {
            var deviceA = HomeWithOnePoint(out var original, out _);

            var received = SendToOtherDevice(deviceA.Home).points.Single();

            Assert.Equal(original.rotationInRoom, received.rotationInRoom);
        }

        [Fact]
        public void 別の家のデータは読み込まない()
        {
            var deviceA = HomeWithOnePoint(out _, out _);
            var deviceB = HomeWithOnePoint(out _, out _);

            var result = HomeImporter.Import(deviceB.Home, SendToOtherDevice(deviceA.Home));

            Assert.Equal(ImportOutcome.Rejected, result.Outcome);
            Assert.Contains("別の家", result.Message);
            Assert.Same(deviceB.Home, result.Home);
        }

        [Fact]
        public void 新しい版のアプリで作ったデータは読み込まない()
        {
            var incoming = SendToOtherDevice(HomeWithOnePoint(out _, out _).Home);
            incoming.schemaVersion = HomeData.CurrentSchemaVersion + 1;

            var result = HomeImporter.Import(null, incoming);

            Assert.Equal(ImportOutcome.Rejected, result.Outcome);
        }

        [Fact]
        public void 同じ家なら相手が足した場所を取り込み更新時刻が新しい方を残す()
        {
            var deviceA = HomeWithOnePoint(out var point, out _);
            var deviceB = new HomeEditor(SendToOtherDevice(deviceA.Home), () => At(5));

            // Aで場所を足し、Bで既存の場所の名前を後から変える
            var room = deviceA.Home.rooms.Single();
            new HomeEditor(deviceA.Home, () => At(3)).AddPoint(room.id, "換気扇", Vec3.Zero);
            var renamed = deviceB.FindPoint(point.id);
            renamed.name = "リビングのエアコン";
            renamed.updatedAt = "2026-10-06T05:00:00.000Z";

            var result = HomeImporter.Import(deviceB.Home, SendToOtherDevice(deviceA.Home));

            Assert.Equal(ImportOutcome.Merged, result.Outcome);
            var names = result.Home.points.Select(p => p.name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "リビングのエアコン", "換気扇" }, names);
        }

        [Fact]
        public void 両方の端末で完了した記録は両方残り前回実施日は新しい方になる()
        {
            var deviceA = HomeWithOnePoint(out _, out var task);
            var deviceB = new HomeEditor(SendToOtherDevice(deviceA.Home), () => At(4));

            new HomeEditor(deviceA.Home, () => At(3)).CompleteTask(task.id, new DateTime(2026, 10, 10));
            deviceB.CompleteTask(task.id, new DateTime(2026, 10, 5));

            var result = HomeImporter.Import(deviceB.Home, SendToOtherDevice(deviceA.Home));

            Assert.Equal(2, result.Home.completions.Count);
            Assert.Equal("2026-10-10", result.Home.tasks.Single().lastDoneDate);
        }

        [Fact]
        public void 同じ内容をもう一度読み込んでも何も変わらない()
        {
            var deviceA = HomeWithOnePoint(out _, out _);
            var deviceB = new HomeEditor(SendToOtherDevice(deviceA.Home));

            var result = HomeImporter.Import(deviceB.Home, SendToOtherDevice(deviceA.Home));

            Assert.Equal(ImportOutcome.Merged, result.Outcome);
            Assert.Contains("新しい内容はありません", result.Message);
            Assert.Single(result.Home.points);
        }
    }
}
