using HomeCare.Core.Spatial;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// マーカーの代わりに、シーンに置いたオブジェクトの位置を基準点として使う仮の部品。
    /// XR Simulation や、マーカーを用意する前の動作確認に使う。
    /// どの部屋IDを渡しても、同じ基準点を返す。
    /// </summary>
    public class MockRoomLocalizer : RoomLocalizer
    {
        [Tooltip("基準点にするオブジェクト。空なら、この部品を付けたオブジェクト自身を使う。")]
        [SerializeField]
        Transform m_Origin;

        public override bool TryGetRoomFrame(string roomId, out RoomFrame frame)
        {
            var origin = m_Origin != null ? m_Origin : transform;
            frame = RoomFrame.FromObservedPose(origin.ToCorePose(), HomeCare.Core.Spatial.Pose.Identity);
            return true;
        }
    }
}
