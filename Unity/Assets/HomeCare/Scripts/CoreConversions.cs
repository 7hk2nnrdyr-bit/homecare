using HomeCare.Core.Spatial;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// Unityの型（Vector3など）とコアの型（Vec3など）を相互に変換する。
    /// コアはUnityに依存しないので、境目でこの変換を使う。
    /// </summary>
    public static class CoreConversions
    {
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Quat ToCore(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);

        public static HomeCare.Core.Spatial.Pose ToCorePose(this Transform t) =>
            new HomeCare.Core.Spatial.Pose(t.position.ToCore(), t.rotation.ToCore());
    }
}
