using System.Collections.Generic;
using HomeCare.Core.Data;
using HomeCare.Core.Spatial;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace HomeCare.App
{
    /// <summary>
    /// 印刷した画像マーカーをカメラで見つけて、部屋の座標系を作る。
    /// マーカーがもう一度映るたびに位置を補正し、少しずつずれるのを防ぐ。
    /// どのマーカーがどの部屋の基準点かは、SetLocalizers で受け取る（部屋ごとに呼ぶ）。
    /// 最後に映ったマーカーの部屋を「今いる部屋」とする。
    /// マーカーを探す部品（AR Tracked Image Manager）は、XR Origin に付けておく。
    /// アプリの起動と同時にマーカーを探し始める必要があるため、実行中に後から付けるのではなく、最初から付けておく。
    /// </summary>
    public class ImageMarkerRoomLocalizer : RoomLocalizer
    {
        // マーカー番号 → そのマーカーがある部屋と、部屋の座標のどこにあるか
        readonly Dictionary<string, (string roomId, HomeCare.Core.Spatial.Pose inRoom)> m_Markers =
            new Dictionary<string, (string, HomeCare.Core.Spatial.Pose)>();

        // 部屋ID → 最後にマーカーから作った部屋の座標系
        readonly Dictionary<string, RoomFrame> m_Frames = new Dictionary<string, RoomFrame>();

        // 登録に無い画像を見つけたことを、画像ごとに一度だけ知らせるための記録
        readonly HashSet<TrackableId> m_ReportedUnknown = new HashSet<TrackableId>();

        ARTrackedImageManager m_Manager;
        string m_LatestRoomId;

        public override string LatestRoomId => m_LatestRoomId;

        void Start()
        {
            m_Manager = FindAnyObjectByType<ARTrackedImageManager>();
            if (m_Manager == null)
            {
                Debug.LogWarning("[HomeCare] XR Origin に AR Tracked Image Manager が無いため、マーカーを探せません。");
            }
        }

        public override void SetLocalizers(string roomId, IEnumerable<LocalizerData> localizers)
        {
            foreach (var localizer in localizers)
            {
                if (localizer.type == "marker")
                {
                    m_Markers[localizer.markerId] = (roomId, DataFormat.ToPose(localizer));
                }
            }
        }

        public override bool TryGetRoomFrame(string roomId, out RoomFrame frame) =>
            m_Frames.TryGetValue(roomId, out frame);

        void Update()
        {
            if (m_Manager == null)
            {
                return;
            }

            foreach (var image in m_Manager.trackables)
            {
                // 見失ったときや不安定なときは、前の位置合わせをそのまま使う
                if (image.trackingState != TrackingState.Tracking)
                {
                    continue;
                }
                var markerId = image.referenceImage.name;
                if (string.IsNullOrEmpty(markerId) || !m_Markers.TryGetValue(markerId, out var marker))
                {
                    if (m_ReportedUnknown.Add(image.trackableId))
                    {
                        Debug.LogWarning($"[HomeCare] 画像を見つけましたが、部屋に登録したマーカーではありません（名前：{(string.IsNullOrEmpty(markerId) ? "なし" : markerId)}）。");
                    }
                    continue;
                }

                var isFirst = !m_Frames.ContainsKey(marker.roomId);
                m_Frames[marker.roomId] = RoomFrame.FromObservedPose(image.transform.ToCorePose(), marker.inRoom);
                m_LatestRoomId = marker.roomId;
                if (isFirst)
                {
                    Debug.Log($"[HomeCare] マーカー{markerId}で部屋の位置合わせができました。");
                }
            }
        }
    }
}
