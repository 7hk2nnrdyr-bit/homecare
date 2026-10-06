using HomeCare.Core.Spatial;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 「部屋IDを渡すと、その部屋の座標系を返す」部品の共通の形。
    /// マーカー版・クラウドアンカー版・モック版は、どれもこれを継承して作る。
    /// アプリの他の部分はこの形だけを使うので、基準点の方式を差し替えても影響しない。
    /// </summary>
    public abstract class RoomLocalizer : MonoBehaviour
    {
        /// <summary>
        /// 部屋の座標系を取得する。基準点がまだ見つかっていなければ false を返す。
        /// </summary>
        public abstract bool TryGetRoomFrame(string roomId, out RoomFrame frame);
    }
}
