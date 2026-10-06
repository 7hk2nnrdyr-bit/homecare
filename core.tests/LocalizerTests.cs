using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class LocalizerTests
    {
        private static readonly DateTime FixedNow = new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc);

        private static void AssertClose(Vec3 expected, Vec3 actual)
        {
            const float tolerance = 1e-4f;
            Assert.True((expected - actual).Length < tolerance, $"expected {expected} but was {actual}");
        }

        [Fact]
        public void 部屋にマーカーを登録すると保存して読み直しても残っている()
        {
            var repository = new InMemoryHomeRepository();
            var first = HomeEditor.LoadOrCreate(repository, "わが家", () => FixedNow);
            var room = first.AddRoom("リビング");
            first.AddMarkerLocalizer(room.id, "M01", Vec3.Zero, 0f);
            repository.Save(first.Home);

            var second = HomeEditor.LoadOrCreate(repository, "わが家", () => FixedNow);
            var localizer = second.LocalizersOf(room.id).Single();

            Assert.Equal("marker", localizer.type);
            Assert.Equal("M01", localizer.markerId);
            Assert.Equal(new[] { 0f, 0f, 0f }, localizer.positionInRoom);
        }

        [Fact]
        public void 同じマーカーを別の部屋に登録しようとすると断る()
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家", () => FixedNow);
            var living = editor.AddRoom("リビング");
            var kitchen = editor.AddRoom("キッチン");
            editor.AddMarkerLocalizer(living.id, "M01", Vec3.Zero, 0f);

            var error = Assert.Throws<ArgumentException>(
                () => editor.AddMarkerLocalizer(kitchen.id, "M01", Vec3.Zero, 0f));
            Assert.Contains("リビング", error.Message);
        }

        [Fact]
        public void 基準点の一覧が無い古いファイルでも空の一覧として読める()
        {
            var old = new HomeData { id = "h1", name = "わが家" };
            old.rooms.Add(new RoomData { id = "r1", name = "リビング", localizers = null });

            var editor = new HomeEditor(old, () => FixedNow);

            Assert.Empty(editor.LocalizersOf("r1"));
        }

        [Fact]
        public void 原点以外に置いた2つ目のマーカーからでも同じ場所にポイントが出る()
        {
            var actual = new RoomFrame(new Vec3(1f, 0f, 2f), 30f * MathF.PI / 180f);
            var second = new LocalizerData
            {
                markerId = "M02",
                positionInRoom = new[] { 3f, 0f, 0f },
                yawDeg = 90f,
            };

            // カメラが見つけたM02の姿勢（床に置いたマーカー）
            var observed = new Pose(
                actual.RoomToWorld(new Vec3(3f, 0f, 0f)),
                Quat.AngleAxis(30f + 90f, Vec3.Up));
            var frame = RoomFrame.FromObservedPose(observed, DataFormat.ToPose(second));

            var point = new Vec3(-1f, 0.8f, 2.5f);
            AssertClose(actual.RoomToWorld(point), frame.RoomToWorld(point));
        }
    }
}
