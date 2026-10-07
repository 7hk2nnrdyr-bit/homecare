using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HomeCare.Core.Data;
using HomeCare.Core.Firebase;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class RecordFieldsTests
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        internal static HomeData FullHome()
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家",
                () => new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc));
            var room = editor.AddRoom("リビング");
            editor.AddMarkerLocalizer(room.id, "M01", new Vec3(0.5f, 1.2f, -0.3f), 12.5f);
            var point = editor.AddPoint(room.id, "エアコン \"上\"", new Vec3(1.2f, 2.1f, -0.4f), Quat.AngleAxis(30f, Vec3.Up));
            var task = editor.AddTask(point.id, "フィルター掃除", new Recurrence(3, RecurrenceUnit.Month), new DateTime(2026, 11, 1));
            editor.CompleteTask(task.id, new DateTime(2026, 10, 6));
            task.note = "改行\nあり";
            return editor.Home;
        }

        [Fact]
        public void 家のデータを辞書にして戻しても中身は変わらない()
        {
            var home = FullHome();

            var copy = RecordFields.FromFields<HomeData>(RecordFields.ToFields(home));

            Assert.Equal(JsonSerializer.Serialize(home, Json), JsonSerializer.Serialize(copy, Json));
        }

        [Fact]
        public void JSONとFirestoreの形を通しても中身は変わらない()
        {
            var home = FullHome();
            var point = home.points.Single();

            var encoded = MiniJson.Write(FirestoreClient.EncodeFields(RecordFields.ToFields(point)));
            var decoded = FirestoreClient.DecodeFields((Dictionary<string, object>)MiniJson.Parse(encoded));
            var copy = RecordFields.FromFields<PointData>(decoded);

            Assert.Equal(JsonSerializer.Serialize(point, Json), JsonSerializer.Serialize(copy, Json));
            Assert.True(RecordFields.AreEqual(RecordFields.ToFields(point), decoded));
        }

        [Fact]
        public void 小数は見た目どおりの値で保存する()
        {
            var fields = RecordFields.ToFields(new PointData { positionInRoom = new[] { 1.2f, 0f, -0.4f } });

            Assert.Equal(new object[] { 1.2, 0.0, -0.4 }, ((List<object>)fields["positionInRoom"]).ToArray());
        }

        [Fact]
        public void 知らない項目は無視し無い項目は初期値のままにする()
        {
            var fields = new Dictionary<string, object> { ["id"] = "x", ["futureField"] = "新しい版の項目" };

            var point = RecordFields.FromFields<PointData>(fields);

            Assert.Equal("x", point.id);
            Assert.Equal(new[] { 0f, 0f, 0f, 1f }, point.rotationInRoom);
        }

        [Fact]
        public void 中身が違えば等しくない()
        {
            var a = RecordFields.ToFields(new TaskData { id = "t", recurrence = new RecurrenceData { every = 3 } });
            var b = RecordFields.ToFields(new TaskData { id = "t", recurrence = new RecurrenceData { every = 4 } });

            Assert.False(RecordFields.AreEqual(a, b));
            Assert.True(RecordFields.AreEqual(a, RecordFields.ToFields(new TaskData { id = "t", recurrence = new RecurrenceData { every = 3 } })));
        }
    }

    public class MiniJsonTests
    {
        [Fact]
        public void 書いて読むと同じ値に戻る()
        {
            var value = new Dictionary<string, object>
            {
                ["text"] = "日本語と\"引用\"と\\と\n改行",
                ["whole"] = 42L,
                ["number"] = -0.25,
                ["flag"] = true,
                ["nothing"] = null,
                ["list"] = new List<object> { 1L, "a", new Dictionary<string, object>() },
            };

            var parsed = MiniJson.Parse(MiniJson.Write(value));

            Assert.True(RecordFields.AreEqual(value, parsed));
        }

        [Fact]
        public void 空白やエスケープを含むJSONを読める()
        {
            var parsed = (Dictionary<string, object>)MiniJson.Parse(" { \"a\" : [ 1 , 2.5e1 ] , \"b\" : \"\\u3042\\/\" } ");

            Assert.Equal(new object[] { 1L, 25.0 }, ((List<object>)parsed["a"]).ToArray());
            Assert.Equal("あ/", parsed["b"]);
        }

        [Fact]
        public void 壊れたJSONは例外になる()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("[1,2] x"));
        }
    }
}
