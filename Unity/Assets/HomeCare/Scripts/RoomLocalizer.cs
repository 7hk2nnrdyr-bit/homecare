using System.Collections.Generic;
using HomeCare.Core.Data;
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

        /// <summary>
        /// 部屋の基準点の一覧を受け取る。マーカー版は、これで「どのマーカーがどの部屋か」を知る。
        /// 基準点を使わない部品（モック版）は何もしない。
        /// </summary>
        public virtual void SetLocalizers(string roomId, IEnumerable<LocalizerData> localizers)
        {
        }

        /// <summary>
        /// いちばん最近、基準点が見えた部屋のID（今いる部屋のつもり）。分からなければ null。
        /// マーカー版は、最後にカメラに映ったマーカーの部屋を返す。
        /// </summary>
        public virtual string LatestRoomId => null;
    }
}
