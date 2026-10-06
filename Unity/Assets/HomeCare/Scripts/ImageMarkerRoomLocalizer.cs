using System.Collections.Generic;
using HomeCare.Core.Data;
using HomeCare.Core.Spatial;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace HomeCare.App
{
    /// <summary>
    /// 印刷した画像マーカーをカメラで見つけて、部屋の座標系を作る。
    /// マーカーがもう一度映るたびに位置を補正し、少しずつずれるのを防ぐ。
    /// どのマーカーがどの部屋の基準点かは、SetLocalizers で受け取る。
    /// </summary>
    public class ImageMarkerRoomLocalizer : RoomLocalizer
    {
        [Tooltip("アプリが探すマーカー画像の一覧（Reference Image Library）。")]
        [SerializeField]
        XRReferenceImageLibrary m_Library;

        // マーカー番号 → そのマーカーがある部屋と、部屋の座標のどこにあるか
        readonly Dictionary<string, (string roomId, HomeCare.Core.Spatial.Pose inRoom)> m_Markers =
            new Dictionary<string, (string, HomeCare.Core.Spatial.Pose)>();

        // 部屋ID → 最後にマーカーから作った部屋の座標系
        readonly Dictionary<string, RoomFrame> m_Frames = new Dictionary<string, RoomFrame>();

        ARTrackedImageManager m_Manager;

        void Start()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null || m_Library == null)
            {
                Debug.LogWarning("[HomeCare] XR Origin かマーカー画像の一覧が見つからないため、マーカーを探せません。");
                return;
            }

            // 画像を探す部品は XR Origin に付ける。付けた直後は画像の一覧が無いので自動で止まるため、
            // 一覧を渡してから動かす。
            m_Manager = origin.GetComponent<ARTrackedImageManager>();
            if (m_Manager == null)
            {
                m_Manager = origin.gameObject.AddComponent<ARTrackedImageManager>();
            }
            if (m_Manager.referenceLibrary == null)
            {
                m_Manager.referenceLibrary = m_Library;
            }
            m_Manager.enabled = true;
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
                    continue;
                }

                var isFirst = !m_Frames.ContainsKey(marker.roomId);
                m_Frames[marker.roomId] = RoomFrame.FromObservedPose(image.transform.ToCorePose(), marker.inRoom);
                if (isFirst)
                {
                    Debug.Log($"[HomeCare] マーカー{markerId}で部屋の位置合わせができました。");
                }
            }
        }
    }
}
