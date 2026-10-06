using System;
using System.Collections.Generic;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace HomeCare.App
{
    /// <summary>
    /// タップで物が置かれたら入力画面を出し、名前・やること・周期とともに、
    /// 位置を部屋の座標に変換してファイルに保存する。
    /// 部屋の位置合わせができたら、保存してあるポイントを同じ場所に、期限の状態の色（赤・黄・緑）の球で表示する。
    /// 球をタップすると詳細を開き、完了を記録できる。
    /// </summary>
    public class RoomPointRecorder : MonoBehaviour
    {
        [Tooltip("タップで物を置く部品（シーンの Object Spawner）。")]
        [SerializeField]
        ObjectSpawner m_Spawner;

        [Tooltip("部屋の座標系を返す部品。マーカー版（ImageMarkerRoomLocalizer）か、仮の MockRoomLocalizer。")]
        [SerializeField]
        RoomLocalizer m_Localizer;

        [Tooltip("ポイントを記録する部屋の名前。無ければ自動で作る。")]
        [SerializeField]
        string m_RoomName = "リビング";

        [Tooltip("部屋に基準点がまだ無いとき、原点として登録するマーカー番号。")]
        [SerializeField]
        string m_OriginMarker = "M01";

        [Tooltip("保存してあるポイントを表示する球の直径（m）。")]
        [SerializeField]
        float m_MarkerSize = 0.05f;

        static readonly Color k_OkColor = new Color(0.2f, 0.75f, 0.3f);
        static readonly Color k_DueSoonColor = new Color(0.95f, 0.8f, 0.1f);
        static readonly Color k_OverdueColor = new Color(0.9f, 0.2f, 0.2f);

        JsonFileHomeRepository m_Repository;
        PointForm m_Form;
        PointDetailView m_Detail;
        ARInteractorSpawnTrigger m_SpawnTrigger;
        HomeEditor m_Editor;
        RoomData m_Room;
        bool m_Restored;
        readonly Dictionary<string, Renderer> m_Markers = new Dictionary<string, Renderer>();

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            m_Room = m_Editor.FindOrAddRoom(m_RoomName);
            if (!m_Editor.LocalizersOf(m_Room.id).Any())
            {
                // 最初のマーカーを部屋の原点にする
                m_Editor.AddMarkerLocalizer(m_Room.id, m_OriginMarker, Vec3.Zero, 0f);
                TrySave();
            }
            if (m_Localizer != null)
            {
                m_Localizer.SetLocalizers(m_Room.id, m_Editor.LocalizersOf(m_Room.id));
            }
            m_Form = GetComponent<PointForm>();
            if (m_Form == null)
            {
                m_Form = gameObject.AddComponent<PointForm>();
            }
            m_Detail = GetComponent<PointDetailView>();
            if (m_Detail == null)
            {
                m_Detail = gameObject.AddComponent<PointDetailView>();
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
            if (m_Localizer != null && m_Localizer.TryGetRoomFrame(m_Room.id, out var frame))
            {
                // 位置合わせができた時点で一度だけ、保存してあるポイントを表示する
                if (!m_Restored)
                {
                    foreach (var point in m_Editor.PointsInRoom(m_Room.id))
                    {
                        CreateMarker(point, WorldPositionOf(point, frame));
                    }
                    m_Restored = true;
                    Debug.Log($"[HomeCare] 保存してあるポイント{m_Markers.Count}個を表示しました（部屋「{m_Room.name}」）。");
                }

                // マーカーが映るたびに部屋の座標が補正されるので、球の位置も合わせ直す
                foreach (var point in m_Editor.PointsInRoom(m_Room.id))
                {
                    if (m_Markers.TryGetValue(point.id, out var renderer))
                    {
                        renderer.transform.position = WorldPositionOf(point, frame);
                    }
                }
            }

            // 球をタップしたら詳細を開く
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame && !IsAnyViewOpen()
                && TryGetTappedMarker(pointer.position.ReadValue(), out var marker))
            {
                OpenDetail(marker.PointId);
            }
        }

        void OnGUI()
        {
            // 位置合わせができるまでは、マーカーを映すよう案内する
            if (m_Localizer == null || m_Localizer.TryGetRoomFrame(m_Room.id, out _))
            {
                return;
            }
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            GUI.Box(new Rect(10f, 40f, width - 20f, 30f), $"部屋のマーカー（{m_OriginMarker}）をカメラに映してください");
        }

        static Vector3 WorldPositionOf(PointData point, RoomFrame frame) =>
            frame.RoomToWorld(DataFormat.ToVec3(point.positionInRoom)).ToUnity();

        bool IsAnyViewOpen() => m_Form.IsOpen || m_Detail.IsOpen;

        static bool TryGetTappedMarker(Vector2 screenPosition, out PointMarker marker)
        {
            marker = null;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var ray = camera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out var hit, 20f))
            {
                marker = hit.collider.GetComponent<PointMarker>();
            }
            return marker != null;
        }

        void OpenDetail(string pointId)
        {
            SetSpawnEnabled(false);
            m_Detail.Open(m_Editor, pointId, taskId => CompleteTask(pointId, taskId), EnableSpawnSoon);
        }

        void CompleteTask(string pointId, string taskId)
        {
            var task = m_Editor.FindTask(taskId);
            m_Editor.CompleteTask(taskId, DateTime.Today);
            if (TrySave())
            {
                Debug.Log($"[HomeCare] 「{task.title}」を完了しました。次回期限：{DataFormat.FormatDate(HomeEditor.NextDueDate(task))}");
            }
            if (m_Markers.TryGetValue(pointId, out var renderer))
            {
                renderer.material.color = ColorOf(m_Editor.StatusOfPoint(pointId, DateTime.Today));
            }
        }

        void OnObjectSpawned(GameObject spawned)
        {
            // 入力中・詳細表示中や、球をタップしたときは、新しいポイントにしない
            var pointer = Pointer.current;
            if (IsAnyViewOpen()
                || (pointer != null && TryGetTappedMarker(pointer.position.ReadValue(), out _)))
            {
                Destroy(spawned);
                return;
            }
            if (m_Localizer == null || !m_Localizer.TryGetRoomFrame(m_Room.id, out var frame))
            {
                Debug.LogWarning($"[HomeCare] まだ位置合わせができていないため、置けません。先にマーカー（{m_OriginMarker}）を映してください。");
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
                    if (TrySave())
                    {
                        CreateMarker(point, world);
                        Debug.Log($"[HomeCare] 「{point.name}：{input.TaskTitle}」を保存：部屋「{m_Room.name}」の座標 {inRoom}");
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

        bool TrySave()
        {
            try
            {
                m_Repository.Save(m_Editor.Home);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HomeCare] 保存に失敗しました：{e.Message}");
                return false;
            }
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

        void CreateMarker(PointData point, Vector3 worldPosition)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Point: {point.name}";
            marker.transform.position = worldPosition;
            marker.transform.localScale = Vector3.one * m_MarkerSize;
            // 小さい球でもタップしやすいよう、当たり判定は見た目の3倍の大きさにする
            marker.GetComponent<SphereCollider>().radius = 1.5f;
            marker.AddComponent<PointMarker>().PointId = point.id;
            var renderer = marker.GetComponent<Renderer>();
            renderer.material.color = ColorOf(m_Editor.StatusOfPoint(point.id, DateTime.Today));
            m_Markers[point.id] = renderer;
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
