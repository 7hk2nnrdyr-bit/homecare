using System;

namespace HomeCare.Core.Spatial
{
    /// <summary>
    /// 部屋の座標系。ポイントの位置はすべてこの座標系で保存する。
    /// ・原点：部屋の基準点（最初に登録したマーカーなど）
    /// ・Y軸：重力と反対の真上（マーカーが少し傾いていても影響しない）
    /// ・向き：基準点の向きから求めた水平方向の回転（Yaw）だけを持つ
    ///
    /// ARの空間（ワールド座標）はアプリを起動するたびに変わるが、
    /// 部屋の座標は変わらない。この2つを相互に変換するのがこのクラスの役割。
    /// マーカーにもクラウドアンカーにも依存しない（どちらも「見つけた姿勢」を渡すだけ）。
    /// </summary>
    public readonly struct RoomFrame
    {
        /// <summary>
        /// マーカー面の向き（Y軸）と真上との角度が45度より小さければ「水平に置いた」、
        /// それ以上なら「壁に貼った」とみなす。値は cos(45度)。
        /// </summary>
        private const float FlatThreshold = 0.70710678f;

        /// <summary>部屋の原点のワールド座標。</summary>
        public Vec3 Origin { get; }

        /// <summary>部屋の向き（真上から見た回転角、ラジアン）。</summary>
        public float YawRadians { get; }

        public RoomFrame(Vec3 origin, float yawRadians)
        {
            Origin = origin;
            YawRadians = yawRadians;
        }

        /// <summary>
        /// ARで見つけた基準点の姿勢から、部屋の座標系を作る。
        /// </summary>
        /// <param name="observedInWorld">ARが返した基準点の姿勢（ワールド座標）。</param>
        /// <param name="localizerInRoom">
        /// その基準点が部屋の座標のどこにあるか。最初のマーカーは原点なので Pose.Identity。
        /// 2つ目以降のマーカーやクラウドアンカーを追加したときに使う。
        /// </param>
        public static RoomFrame FromObservedPose(Pose observedInWorld, Pose localizerInRoom)
        {
            var observedYaw = HeadingOf(observedInWorld.Rotation);
            var localizerYaw = HeadingOf(localizerInRoom.Rotation);

            var yaw = observedYaw - localizerYaw;
            var origin = observedInWorld.Position - RotateY(localizerInRoom.Position, yaw);
            return new RoomFrame(origin, yaw);
        }

        /// <summary>ワールド座標 → 部屋の座標。</summary>
        public Vec3 WorldToRoom(Vec3 world) => RotateY(world - Origin, -YawRadians);

        /// <summary>部屋の座標 → ワールド座標。</summary>
        public Vec3 RoomToWorld(Vec3 room) => Origin + RotateY(room, YawRadians);

        /// <summary>ワールドでの向き → 部屋の座標での向き。</summary>
        public Quat WorldToRoom(Quat world) => Quat.AngleAxis(-YawDegrees, Vec3.Up) * world;

        /// <summary>部屋の座標での向き → ワールドでの向き。</summary>
        public Quat RoomToWorld(Quat room) => Quat.AngleAxis(YawDegrees, Vec3.Up) * room;

        private float YawDegrees => YawRadians * 180f / MathF.PI;

        /// <summary>
        /// 傾きから水平方向の向き（Yaw）を取り出す。
        /// ARのマーカーは、画像の面の向きがY軸、画像の上辺の向きがZ軸。
        /// ・水平に置いたマーカー（床・棚の上・天井）：画像の上辺の向き（Z軸）で決める
        /// ・壁に貼ったマーカー：面の向き（Y軸、壁から部屋側）で決める
        /// </summary>
        private static float HeadingOf(Quat rotation)
        {
            var normal = rotation.Rotate(Vec3.Up);
            var heading = MathF.Abs(normal.Y) >= FlatThreshold
                ? rotation.Rotate(Vec3.Forward)
                : normal;
            return MathF.Atan2(heading.X, heading.Z);
        }

        /// <summary>真上の軸のまわりに回す（Unityと同じ向き：正の角度で上から見て時計回り）。</summary>
        private static Vec3 RotateY(Vec3 v, float radians)
        {
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);
            return new Vec3(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos);
        }
    }
}
