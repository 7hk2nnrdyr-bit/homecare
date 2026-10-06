using System;
using HomeCare.Core.Spatial;
using Xunit;

namespace HomeCare.Core.Tests
{
    public class RoomFrameTests
    {
        private static readonly Vec3 Right = new Vec3(1f, 0f, 0f);

        private static Pose MarkerAt(Vec3 position, Quat rotation) => new Pose(position, rotation);

        private static void AssertClose(Vec3 expected, Vec3 actual)
        {
            const float tolerance = 1e-4f;
            Assert.True((expected - actual).Length < tolerance, $"expected {expected} but was {actual}");
        }

        [Fact]
        public void 回転していないマーカーなら位置をずらすだけ()
        {
            var frame = RoomFrame.FromObservedPose(MarkerAt(new Vec3(1f, 0.5f, 2f), Quat.Identity), Pose.Identity);

            AssertClose(new Vec3(1f, 0.5f, 3f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void マーカーが90度回っていれば部屋の前方向も90度回る()
        {
            var marker = MarkerAt(new Vec3(1f, 0f, 2f), Quat.AngleAxis(90f, Vec3.Up));
            var frame = RoomFrame.FromObservedPose(marker, Pose.Identity);

            // マーカーの1m前は、ワールドでは右（+X）に1m
            AssertClose(new Vec3(2f, 0f, 2f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void マーカーが少し傾いていても部屋の上方向は真上のまま()
        {
            var tilted = Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(10f, Right);
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, tilted), Pose.Identity);

            AssertClose(new Vec3(0f, 1f, 0f), frame.RoomToWorld(new Vec3(0f, 1f, 0f)));
        }

        [Fact]
        public void 壁に貼ったマーカーは面の向きを部屋の前方向にする()
        {
            // 床に置いた状態から90度起こすと、面の向き（Y軸）が水平の+Zを向く
            var onWall = Quat.AngleAxis(90f, Vec3.Up) * Quat.AngleAxis(90f, Right);
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, onWall), Pose.Identity);

            AssertClose(new Vec3(1f, 0f, 0f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void 面の傾きが45度より小さければ水平に置いたマーカーとして上辺の向きを使う()
        {
            // 前後の軸（Z）のまわりに40度傾ける：面の向きは左上を向くが、上辺は+Zのまま
            var leaning = Quat.AngleAxis(40f, Vec3.Forward);
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, leaning), Pose.Identity);

            AssertClose(new Vec3(0f, 0f, 1f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void 面の傾きが45度以上なら壁に貼ったマーカーとして面の向きを使う()
        {
            // 同じ軸で50度傾ける：面の向きは左（-X）寄りになる
            var leaning = Quat.AngleAxis(50f, Vec3.Forward);
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, leaning), Pose.Identity);

            AssertClose(new Vec3(-1f, 0f, 0f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void 天井に下向きに貼ったマーカーも上辺の向きを使う()
        {
            // 左右の軸（X）のまわりに180度回す：面は真下、上辺は-Zを向く
            var onCeiling = Quat.AngleAxis(180f, Right);
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, onCeiling), Pose.Identity);

            AssertClose(new Vec3(0f, 0f, -1f), frame.RoomToWorld(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void 向きも部屋の座標に変換して戻すと元の向きになる()
        {
            var frame = RoomFrame.FromObservedPose(MarkerAt(Vec3.Zero, Quat.AngleAxis(30f, Vec3.Up)), Pose.Identity);
            var world = Quat.AngleAxis(75f, Vec3.Up) * Quat.AngleAxis(20f, Right);

            var inRoom = frame.WorldToRoom(world);
            AssertClose(Quat.AngleAxis(45f, Vec3.Up).Rotate(Quat.AngleAxis(20f, Right).Rotate(Vec3.Forward)),
                inRoom.Rotate(Vec3.Forward));
            AssertClose(world.Rotate(Vec3.Forward), frame.RoomToWorld(inRoom).Rotate(Vec3.Forward));
        }

        [Fact]
        public void ワールドから部屋に変換して戻すと元の位置になる()
        {
            var frame = new RoomFrame(new Vec3(1f, 0f, -2f), 0.7f);
            var world = new Vec3(3f, 1.2f, 4f);

            AssertClose(world, frame.RoomToWorld(frame.WorldToRoom(world)));
        }

        [Fact]
        public void 起動のたびにARの空間がずれても部屋の座標は同じになる()
        {
            var pointInRoom = new Vec3(0.5f, 1f, 2f);

            // 1回目の起動
            var first = RoomFrame.FromObservedPose(MarkerAt(new Vec3(1f, 0f, 1f), Quat.AngleAxis(20f, Vec3.Up)), Pose.Identity);
            var worldFirst = first.RoomToWorld(pointInRoom);

            // 2回目の起動：ARの空間の原点と向きが変わっている
            var second = RoomFrame.FromObservedPose(MarkerAt(new Vec3(-3f, 0f, 4f), Quat.AngleAxis(-70f, Vec3.Up)), Pose.Identity);
            var worldSecond = second.RoomToWorld(pointInRoom);

            Assert.True((worldFirst - worldSecond).Length > 1f);
            AssertClose(pointInRoom, first.WorldToRoom(worldFirst));
            AssertClose(pointInRoom, second.WorldToRoom(worldSecond));
        }

        [Fact]
        public void 原点以外の基準点からでも同じ部屋の座標系になる()
        {
            var actual = new RoomFrame(new Vec3(1f, 0f, 2f), 30f * MathF.PI / 180f);

            // 部屋の(3, 0, 0)に、90度回して置いた2つ目の基準点
            var localizerInRoom = new Pose(new Vec3(3f, 0f, 0f), Quat.AngleAxis(90f, Vec3.Up));
            var observed = new Pose(actual.RoomToWorld(localizerInRoom.Position), Quat.AngleAxis(30f + 90f, Vec3.Up));

            var frame = RoomFrame.FromObservedPose(observed, localizerInRoom);

            var point = new Vec3(-1f, 0.8f, 2.5f);
            AssertClose(actual.RoomToWorld(point), frame.RoomToWorld(point));
        }
    }
}
