using System;

namespace HomeCare.Core.Spatial
{
    /// <summary>
    /// 3次元の回転（クォータニオン）。UnityのQuaternionと同じ並び（X, Y, Z, W）。
    /// ARが返す「傾き」をそのまま受け取るために使う。
    /// </summary>
    public readonly struct Quat
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        /// <summary>軸のまわりに角度（度）だけ回す回転を作る。</summary>
        public static Quat AngleAxis(float degrees, Vec3 axis)
        {
            var length = axis.Length;
            if (length < 1e-6f)
            {
                return Identity;
            }
            var half = degrees * MathF.PI / 360f;
            var s = MathF.Sin(half) / length;
            return new Quat(axis.X * s, axis.Y * s, axis.Z * s, MathF.Cos(half));
        }

        /// <summary>回転を合成する（先にbを回し、そのあとaを回す）。</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>向き（ベクトル）を回す。</summary>
        public Vec3 Rotate(Vec3 v)
        {
            // v' = v + 2w(q×v) + 2q×(q×v)
            var tx = 2f * (Y * v.Z - Z * v.Y);
            var ty = 2f * (Z * v.X - X * v.Z);
            var tz = 2f * (X * v.Y - Y * v.X);
            return new Vec3(
                v.X + W * tx + (Y * tz - Z * ty),
                v.Y + W * ty + (Z * tx - X * tz),
                v.Z + W * tz + (X * ty - Y * tx));
        }
    }
}
