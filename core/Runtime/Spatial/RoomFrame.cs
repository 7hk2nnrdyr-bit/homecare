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
        /// 向きを決める軸がこれより水平から外れていたら、もう一方の軸で向きを決める。
        /// （0.5 = 水平から約60度以上傾いている）
        /// </summary>
        private const float MinHorizontalLength = 0.5f;

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

        /// <summary>
        /// 傾きから水平方向の向き（Yaw）を取り出す。
        /// 基本は前方向（Z軸）を使う。床に置いたマーカーはこれで決まる。
        /// Z軸がほぼ真上・真下を向いているとき（壁に貼ったマーカー）は、
        /// 面の向き（Y軸、壁から部屋側）で決める。
        /// </summary>
        private static float HeadingOf(Quat rotation)
        {
            var forward = rotation.Rotate(Vec3.Forward);
            if (HorizontalLength(forward) < MinHorizontalLength)
            {
                forward = rotation.Rotate(Vec3.Up);
            }
            return MathF.Atan2(forward.X, forward.Z);
        }

        private static float HorizontalLength(Vec3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

        /// <summary>真上の軸のまわりに回す（Unityと同じ向き：正の角度で上から見て時計回り）。</summary>
        private static Vec3 RotateY(Vec3 v, float radians)
        {
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);
            return new Vec3(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos);
        }
    }
}
