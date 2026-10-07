using System;
using System.Linq;
using HomeCare.Core.Data;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class RoomMarkerTests
    {
        private static readonly string[] Available = { "M01", "M02", "M03" };

        private static HomeEditor NewHome() => HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家");

        [Fact]
        public void 部屋を足すとまだ使っていないマーカーが順に割り当てられる()
        {
            var editor = NewHome();

            var living = editor.AddRoomWithMarker("リビング", Available);
            var bedroom = editor.AddRoomWithMarker(" 寝室 ", Available);

            Assert.Equal("M01", editor.MarkerOf(living.id));
            Assert.Equal("M02", editor.MarkerOf(bedroom.id));
            Assert.Equal("寝室", bedroom.name);
            Assert.Equal(new[] { "M03" }, editor.UnusedMarkerIds(Available));
        }

        [Fact]
        public void 前からある部屋のマーカーは使わない()
        {
            var editor = NewHome();
            var living = editor.AddRoom("リビング");
            editor.AddMarkerLocalizer(living.id, "M01", default, 0f);

            var kitchen = editor.AddRoomWithMarker("キッチン", Available);

            Assert.Equal("M02", editor.MarkerOf(kitchen.id));
        }

        [Fact]
        public void 名前が空や同じ名前やマーカー切れの部屋は作れない()
        {
            var editor = NewHome();
            editor.AddRoomWithMarker("リビング", Available);
            editor.AddRoomWithMarker("寝室", Available);
            editor.AddRoomWithMarker("キッチン", Available);

            Assert.Throws<ArgumentException>(() => editor.AddRoomWithMarker("  ", Available));
            Assert.Throws<ArgumentException>(() => editor.AddRoomWithMarker("寝室", Available));
            var full = Assert.Throws<ArgumentException>(() => editor.AddRoomWithMarker("浴室", Available));
            Assert.Contains("残っていません", full.Message);
            Assert.Equal(3, editor.ActiveRooms().Count());
        }
    }
}
