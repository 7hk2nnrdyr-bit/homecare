using System;
using System.Collections.Generic;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace HomeCare.App
{
    /// <summary>
    /// タップで物が置かれたら入力画面を出し、名前・やること・周期とともに、
    /// 位置を部屋の座標に変換してファイルに保存する。
    /// 起動したときは、保存してあるポイントを同じ場所に、期限の状態の色（赤・黄・緑）の球で表示する。
    /// </summary>
    public class RoomPointRecorder : MonoBehaviour
    {
        [Tooltip("タップで物を置く部品（シーンの Object Spawner）。")]
        [SerializeField]
        ObjectSpawner m_Spawner;

        [Tooltip("部屋の座標系を返す部品。今は MockRoomLocalizer。")]
        [SerializeField]
        RoomLocalizer m_Localizer;

        [Tooltip("ポイントを記録する部屋の名前。無ければ自動で作る。")]
        [SerializeField]
        string m_RoomName = "リビング";

        [Tooltip("保存してあるポイントを表示する球の直径（m）。")]
        [SerializeField]
        float m_MarkerSize = 0.05f;

        static readonly Color k_OkColor = new Color(0.2f, 0.75f, 0.3f);
        static readonly Color k_DueSoonColor = new Color(0.95f, 0.8f, 0.1f);
        static readonly Color k_OverdueColor = new Color(0.9f, 0.2f, 0.2f);

        JsonFileHomeRepository m_Repository;
        PointForm m_Form;
        ARInteractorSpawnTrigger m_SpawnTrigger;
        HomeEditor m_Editor;
        RoomData m_Room;
        bool m_Restored;
        readonly List<GameObject> m_Markers = new List<GameObject>();

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            m_Room = m_Editor.FindOrAddRoom(m_RoomName);
            m_Form = GetComponent<PointForm>();
            if (m_Form == null)
            {
                m_Form = gameObject.AddComponent<PointForm>();
            }
            m_SpawnTrigger = FindAnyObjectByType<ARInteractorSpawnTrigger>();
            Debug.Log($"[HomeCare] 保存先：{m_Repository.FilePath}");
        }

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

        void Update()
        {
            // 基準点が見つかった時点で一度だけ、保存してあるポイントを表示する
            if (!m_Restored && m_Localizer != null && m_Localizer.TryGetRoomFrame(m_Room.id, out var frame))
            {
                foreach (var point in m_Editor.PointsInRoom(m_Room.id))
                {
                    var world = frame.RoomToWorld(DataFormat.ToVec3(point.positionInRoom)).ToUnity();
                    m_Markers.Add(CreateMarker(point, world));
                }
                m_Restored = true;
                Debug.Log($"[HomeCare] 保存してあるポイント{m_Markers.Count}個を表示しました（部屋「{m_Room.name}」）。");
            }
        }

        void OnObjectSpawned(GameObject spawned)
        {
            if (m_Form.IsOpen)
            {
                Destroy(spawned);
                return;
            }
            if (m_Localizer == null || !m_Localizer.TryGetRoomFrame(m_Room.id, out var frame))
            {
                Debug.LogWarning("[HomeCare] 部屋の基準点が見つからないため、位置を記録できません。");
                Destroy(spawned);
                return;
            }

            var world = spawned.transform.position;
            var inRoom = frame.WorldToRoom(world.ToCore());

            // 入力中に画面をタップしても、物が置かれないようにする
            SetSpawnEnabled(false);
            m_Form.Open(
                input =>
                {
                    var point = m_Editor.AddPoint(m_Room.id, input.PointName, inRoom);
                    m_Editor.AddTask(point.id, input.TaskTitle, input.Recurrence, input.FirstDueDate);
                    try
                    {
                        m_Repository.Save(m_Editor.Home);
                        m_Markers.Add(CreateMarker(point, world));
                        Debug.Log($"[HomeCare] 「{point.name}：{input.TaskTitle}」を保存：部屋「{m_Room.name}」の座標 {inRoom}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[HomeCare] 保存に失敗しました：{e.Message}");
                    }
                    Destroy(spawned);
                    EnableSpawnSoon();
                },
                () =>
                {
                    Destroy(spawned);
                    EnableSpawnSoon();
                });
        }

        /// <summary>保存ボタンを押した指で物が置かれないよう、少し待ってから戻す。</summary>
        void EnableSpawnSoon() => Invoke(nameof(EnableSpawn), 0.3f);

        void EnableSpawn() => SetSpawnEnabled(true);

        void SetSpawnEnabled(bool enabled)
        {
            if (m_SpawnTrigger != null)
            {
                m_SpawnTrigger.enabled = enabled;
            }
        }

        GameObject CreateMarker(PointData point, Vector3 worldPosition)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Point: {point.name}";
            marker.transform.position = worldPosition;
            marker.transform.localScale = Vector3.one * m_MarkerSize;
            // タップの邪魔にならないよう、当たり判定は外す
            Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().material.color = ColorOf(m_Editor.StatusOfPoint(point.id, DateTime.Today));
            return marker;
        }

        /// <summary>タスクが無いポイントは白にする。</summary>
        static Color ColorOf(DueStatus? status)
        {
            switch (status)
            {
                case DueStatus.Overdue:
                    return k_OverdueColor;
                case DueStatus.DueSoon:
                    return k_DueSoonColor;
                case DueStatus.Ok:
                    return k_OkColor;
                default:
                    return Color.white;
            }
        }

        /// <summary>Inspectorの右上のメニューから実行できる。保存したポイントをすべて消す（動作確認用）。</summary>
        [ContextMenu("保存データを削除")]
        void DeleteSavedData()
        {
            new JsonFileHomeRepository().Delete();
            Debug.Log("[HomeCare] 保存データを削除しました。次にPlayしたときは空の状態から始まります。");
        }
    }
}
