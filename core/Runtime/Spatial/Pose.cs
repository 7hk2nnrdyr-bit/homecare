namespace HomeCare.Core.Spatial
{
    /// <summary>位置と傾きの組。ARが見つけたマーカーなどの姿勢を表す。</summary>
    public readonly struct Pose
    {
        public Vec3 Position { get; }
        public Quat Rotation { get; }

        public Pose(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static readonly Pose Identity = new Pose(Vec3.Zero, Quat.Identity);
    }
}
