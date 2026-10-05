using System;

namespace HomeCare.Core.Spatial
{
    /// <summary>
    /// 3次元の点または向き。単位はメートル。
    /// UnityのVector3と同じ並び（Xは右、Yは上、Zは前）。
    /// Unityに依存しないよう、コアでは自前の型を使う。
    /// </summary>
    public readonly struct Vec3
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>長さ（メートル）。</summary>
        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
