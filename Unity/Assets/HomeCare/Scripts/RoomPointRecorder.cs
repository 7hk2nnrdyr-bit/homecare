using System.Collections.Generic;
using HomeCare.Core.Spatial;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace HomeCare.App
{
    /// <summary>
    /// タップで物が置かれたら、その位置を部屋の座標に変換して記録する。
    /// 今はConsoleに表示するだけ。保存は次の段階で追加する。
    /// </summary>
    public class RoomPointRecorder : MonoBehaviour
    {
        [Tooltip("タップで物を置く部品（シーンの Object Spawner）。")]
        [SerializeField]
        ObjectSpawner m_Spawner;

        [Tooltip("部屋の座標系を返す部品。今は MockRoomLocalizer。")]
        [SerializeField]
        RoomLocalizer m_Localizer;

        [SerializeField]
        string m_RoomId = "living";

        readonly List<Vec3> m_PointsInRoom = new List<Vec3>();

        /// <summary>記録したポイント（部屋の座標、単位はm）。</summary>
        public IReadOnlyList<Vec3> PointsInRoom => m_PointsInRoom;

        void OnEnable()
        {
            if (m_Spawner != null)
            {
                m_Spawner.objectSpawned += OnObjectSpawned;
            }
            else
            {
                Debug.LogWarning("[HomeCare] RoomPointRecorder に Object Spawner が設定されていません。");
            }
        }

        void OnDisable()
        {
            if (m_Spawner != null)
            {
                m_Spawner.objectSpawned -= OnObjectSpawned;
            }
        }

        void OnObjectSpawned(GameObject spawned)
        {
            if (m_Localizer == null || !m_Localizer.TryGetRoomFrame(m_RoomId, out var frame))
            {
                Debug.LogWarning("[HomeCare] 部屋の基準点が見つからないため、位置を記録できません。");
                return;
            }

            var world = spawned.transform.position;
            var inRoom = frame.WorldToRoom(world.ToCore());
            m_PointsInRoom.Add(inRoom);

            Debug.Log($"[HomeCare] ポイント{m_PointsInRoom.Count}を記録：部屋「{m_RoomId}」の座標 {inRoom}（ARの空間では {world}）");
        }
    }
}
