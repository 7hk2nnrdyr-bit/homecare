using System;
using System.Collections.Generic;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using HomeCare.Core.Sync;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace HomeCare.App
{
    /// <summary>
    /// タップで物が置かれたら入力画面を出し、名前・やること・周期とともに、
    /// 位置を部屋の座標に変換してファイルに保存する。
    /// 部屋ごとにマーカー（M01、M02…）があり、マーカーが映った部屋の位置合わせができたら、
    /// その部屋のポイントを同じ場所に、期限の状態の色（赤・黄・緑）の球で表示する。
    /// 新しいポイントは、最後にマーカーが映った部屋（今いる部屋）に登録する。
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

        [Tooltip("部屋がまだ1つも無いときに作る、最初の部屋の名前。")]
        [SerializeField]
        string m_RoomName = "リビング";

        [Tooltip("最初の部屋の原点にするマーカー番号。")]
        [SerializeField]
        string m_OriginMarker = "M01";

        [Tooltip("保存してあるポイントを表示する球の直径（m）。")]
        [SerializeField]
        float m_MarkerSize = 0.05f;

        [Tooltip("「目の前に置く」で、最初にカメラから何m先に置くか。")]
        [SerializeField]
        float m_PlaceDistance = 1f;

        JsonFileHomeRepository m_Repository;
        PointForm m_Form;
        PointDetailView m_Detail;
        ARInteractorSpawnTrigger m_SpawnTrigger;
        HomeEditor m_Editor;
        // 球をもう出した部屋（位置合わせができた時点で、その部屋の球をまとめて出す）
        readonly HashSet<string> m_RestoredRooms = new HashSet<string>();
        readonly Dictionary<string, Renderer> m_Markers = new Dictionary<string, Renderer>();

        // 自動同期：カメラ画面を開いたとき・登録や完了をしたとき・開いている間は1分ごとに同期する。
        // 家族が足した場所も、この画面を開いたまま球で出る
        const float AutoSyncDelayAfterChange = 3f;
        const float AutoSyncInterval = 60f;
        float m_AutoSyncAt = -1f;
        bool m_Syncing;

        // 「目の前に置く」：面が見つからない物（白い壁・エアコン・棚の上など）にも登録できるようにする。
        // カメラの前に仮の球を出し、距離を変えてから登録する
        const float MinPlaceDistance = 0.2f;
        const float MaxPlaceDistance = 4f;
        GameObject m_Preview;
        float m_PreviewDistance;
        int m_LocalVersion;
        string m_SyncStatus;

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            if (!m_Editor.ActiveRooms().Any())
            {
                // 部屋がまだ無ければ、最初の部屋を作り、最初のマーカーをその原点にする
                m_Editor.AddRoomWithMarker(m_RoomName, MarkerCatalog.Ids.Prepend(m_OriginMarker).Distinct());
                TrySave();
            }
            RegisterLocalizers();
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

        void Start()
        {
            RequestAutoSync(0f);
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                RequestAutoSync(0f);
            }
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
            // 入力画面や詳細を開いている間は、そこで使っているデータを入れ替えないよう待つ
            if (!m_Syncing && !IsAnyViewOpen() && m_AutoSyncAt >= 0f && Time.realtimeSinceStartup >= m_AutoSyncAt)
            {
                m_AutoSyncAt = -1f;
                AutoSync();
            }

            foreach (var room in m_Editor.ActiveRooms())
            {
                if (m_Localizer == null || !m_Localizer.TryGetRoomFrame(room.id, out var frame))
                {
                    continue;
                }
                // 位置合わせができた時点で一度だけ、その部屋の保存してあるポイントを表示する
                if (m_RestoredRooms.Add(room.id))
                {
                    var count = 0;
                    foreach (var point in m_Editor.PointsInRoom(room.id))
                    {
                        CreateMarker(point, WorldPositionOf(point, frame));
                        count++;
                    }
                    Debug.Log($"[HomeCare] 保存してあるポイント{count}個を表示しました（部屋「{room.name}」）。");
                }

                // マーカーが映るたびに部屋の座標が補正されるので、球の位置も合わせ直す
                foreach (var point in m_Editor.PointsInRoom(room.id))
                {
                    if (m_Markers.TryGetValue(point.id, out var renderer))
                    {
                        renderer.transform.SetPositionAndRotation(
                            WorldPositionOf(point, frame),
                            frame.RoomToWorld(DataFormat.ToQuat(point.rotationInRoom)).ToUnity());
                    }
                }
            }

            // 「目の前に置く」の仮の球は、カメラの向いている先に置き続ける
            if (m_Preview != null)
            {
                var camera = Camera.main;
                if (camera == null)
                {
                    CancelPreview();
                }
                else
                {
                    m_Preview.transform.SetPositionAndRotation(
                        camera.transform.position + camera.transform.forward * m_PreviewDistance,
                        Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f));
                }
            }

            // 球をタップしたら詳細を開く
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame && !IsAnyViewOpen() && m_Preview == null
                && TryGetTappedMarker(pointer.position.ReadValue(), out var marker))
            {
                OpenDetail(marker.PointId);
            }
        }

        void OnGUI()
        {
            if (IsAnyViewOpen())
            {
                return;
            }
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;

            if (GUI.Button(new Rect(10f, 40f, 80f, 36f), "一覧へ"))
            {
                AppScenes.OpenList();
                return;
            }

            // 位置合わせができるまでは、マーカーを映すよう案内する。できたら、今いる部屋を出す
            var current = CurrentRoom(out _);
            var guide = current == null
                ? "部屋のマーカーをカメラに映してください（" +
                  string.Join("、", m_Editor.ActiveRooms().Select(r => $"{m_Editor.MarkerOf(r.id)}：{r.name}")) + "）"
                : $"今の部屋：{current.name}（{m_Editor.MarkerOf(current.id)}）";
            GUI.Box(new Rect(100f, 40f, width - 110f, 36f), guide);

            var height = Screen.height / scale;
            var bottom = height - 56f;
            if (m_Preview == null)
            {
                // 面をタップできないとき（白い壁・家電・棚の上など）は、このボタンで目の前に置ける
                if (current != null && GUI.Button(new Rect(10f, bottom, width - 20f, 46f), "目の前に置く"))
                {
                    StartPreview(current);
                }
            }
            else
            {
                GUI.Box(new Rect(10f, bottom - 84f, width - 20f, 36f),
                    $"登録したい物にスマホを向けてください（{m_PreviewDistance:0.0}m先）");
                var third = (width - 20f) / 3f;
                if (GUI.Button(new Rect(10f, bottom - 44f, third - 4f, 40f), "近づける"))
                {
                    m_PreviewDistance = Mathf.Max(MinPlaceDistance, m_PreviewDistance - 0.1f);
                }
                if (GUI.Button(new Rect(10f + third, bottom - 44f, third - 4f, 40f), "遠ざける"))
                {
                    m_PreviewDistance = Mathf.Min(MaxPlaceDistance, m_PreviewDistance + 0.1f);
                }
                if (GUI.Button(new Rect(10f + third * 2f, bottom - 44f, third - 4f, 40f), "やめる"))
                {
                    CancelPreview();
                    return;
                }
                if (GUI.Button(new Rect(10f, bottom, width - 20f, 46f), "ここに登録"))
                {
                    RegisterPreview(current);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(m_SyncStatus))
            {
                GUI.Box(new Rect(10f, 86f, width - 20f, 36f), m_SyncStatus);
            }
        }

        /// <summary>カメラの前に仮の球を出す。登録するまで、カメラの向きに合わせて動き続ける。</summary>
        void StartPreview(RoomData room)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            // 仮の球を置いている間は、画面をタップしても物が置かれないようにする
            SetSpawnEnabled(false);
            m_PreviewDistance = Mathf.Clamp(m_PlaceDistance, MinPlaceDistance, MaxPlaceDistance);
            m_Preview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Preview.name = "Preview";
            m_Preview.transform.localScale = Vector3.one * m_MarkerSize;
            // 仮の球は、タップで詳細が開かないよう当たり判定を外す
            Destroy(m_Preview.GetComponent<SphereCollider>());
            m_Preview.GetComponent<Renderer>().material.color = Color.white;
            Debug.Log($"[HomeCare] 目の前に置く：部屋「{room.name}」");
        }

        void CancelPreview()
        {
            if (m_Preview != null)
            {
                Destroy(m_Preview);
                m_Preview = null;
            }
            EnableSpawnSoon();
        }

        /// <summary>仮の球の場所を、新しいポイントとして登録する。</summary>
        void RegisterPreview(RoomData room)
        {
            var preview = m_Preview;
            m_Preview = null;
            if (preview == null)
            {
                return;
            }
            // 登録する直前にマーカーを見失ったときは、部屋の座標に直せないので、置き直してもらう
            if (room == null || !m_Localizer.TryGetRoomFrame(room.id, out var frame))
            {
                Destroy(preview);
                EnableSpawnSoon();
                Debug.LogWarning("[HomeCare] 位置合わせができていないため、登録できませんでした。マーカーを映してからやり直してください。");
                return;
            }
            OpenFormFor(room, frame, preview.transform.position, preview.transform.rotation, preview);
        }

        /// <summary>少し後に自動同期する。続けて変更したときは、最後の変更から数秒待ってまとめて1回にする。</summary>
        void RequestAutoSync(float delaySeconds)
        {
            if (!CloudSync.IsConfigured(out _))
            {
                return;
            }
            m_AutoSyncAt = Time.realtimeSinceStartup + delaySeconds;
        }

        async void AutoSync()
        {
            m_Syncing = true;
            var versionBefore = m_LocalVersion;
            string status;
            try
            {
                // クラウドにまだ家が無ければ作らない（作るのは一覧画面の「クラウドと同期」）
                var result = await CloudSync.SyncAsync(m_Editor.Home, createIfMissing: false);
                if (this == null)
                {
                    return; // 待っている間に一覧画面へ戻った
                }
                if (result.Outcome == SyncOutcome.Skipped)
                {
                    status = null;
                }
                else if (result.Outcome == SyncOutcome.Failed)
                {
                    status = $"同期できませんでした（{DateTime.Now:H:mm}）";
                    Debug.Log($"[HomeCare] 自動同期：{result.Message}");
                }
                else if (m_LocalVersion != versionBefore || IsAnyViewOpen())
                {
                    // 通信している間にこの画面で登録・完了した（または入力中）なので、結果は使わずに少し後でもう一度同期する
                    status = m_SyncStatus;
                    RequestAutoSync(AutoSyncDelayAfterChange);
                }
                else
                {
                    UseHome(result.Home);
                    status = $"同期済み {DateTime.Now:H:mm}";
                    Debug.Log($"[HomeCare] 自動同期：{result.Message}");
                }
            }
            catch (Exception e)
            {
                if (this == null)
                {
                    return;
                }
                status = $"同期できませんでした（{DateTime.Now:H:mm}）";
                Debug.Log($"[HomeCare] 自動同期：{e.Message}");
            }
            m_SyncStatus = status;
            m_Syncing = false;
            if (m_AutoSyncAt < 0f)
            {
                RequestAutoSync(AutoSyncInterval);
            }
        }

        /// <summary>同期で受け取った家のデータに入れ替え、球を足したり消したり、色を塗り直したりする。</summary>
        void UseHome(HomeData home)
        {
            m_Editor = new HomeEditor(home);
            TrySave();
            // 家族が足した部屋のマーカーも見分けられるようにする
            RegisterLocalizers();

            var current = new HashSet<string>(m_Editor.ActiveRooms().SelectMany(r => m_Editor.PointsInRoom(r.id)).Select(p => p.id));
            foreach (var id in m_Markers.Keys.Where(id => !current.Contains(id)).ToList())
            {
                Destroy(m_Markers[id].gameObject);
                m_Markers.Remove(id);
            }
            foreach (var room in m_Editor.ActiveRooms())
            {
                // 位置合わせ前の部屋は、合ったときにまとめて出る（Update）
                if (!m_RestoredRooms.Contains(room.id) || m_Localizer == null || !m_Localizer.TryGetRoomFrame(room.id, out var frame))
                {
                    continue;
                }
                foreach (var point in m_Editor.PointsInRoom(room.id))
                {
                    if (m_Markers.TryGetValue(point.id, out var renderer))
                    {
                        renderer.material.color = StatusStyle.ColorOf(m_Editor.StatusOfPoint(point.id, DateTime.Today));
                    }
                    else
                    {
                        CreateMarker(point, WorldPositionOf(point, frame));
                    }
                }
            }
        }

        /// <summary>どのマーカーがどの部屋かを、位置合わせの部品に教える。</summary>
        void RegisterLocalizers()
        {
            if (m_Localizer == null)
            {
                return;
            }
            foreach (var room in m_Editor.ActiveRooms())
            {
                m_Localizer.SetLocalizers(room.id, m_Editor.LocalizersOf(room.id));
            }
        }

        /// <summary>
        /// 今いる部屋（最後にマーカーが映った部屋）と、その部屋の座標系。
        /// どの部屋も位置合わせができていなければ null。
        /// </summary>
        RoomData CurrentRoom(out RoomFrame frame)
        {
            frame = default;
            if (m_Localizer == null)
            {
                return null;
            }
            var latest = m_Localizer.LatestRoomId;
            var rooms = m_Editor.ActiveRooms().ToList();
            // 最後に映った部屋を優先し、分からなければ（仮の位置合わせ部品など）位置合わせできた最初の部屋
            foreach (var room in rooms.OrderBy(r => r.id == latest ? 0 : 1))
            {
                if (m_Localizer.TryGetRoomFrame(room.id, out frame))
                {
                    return room;
                }
            }
            return null;
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
            if (SaveChange())
            {
                Debug.Log($"[HomeCare] 「{task.title}」を完了しました。次回期限：{DataFormat.FormatDate(HomeEditor.NextDueDate(task))}");
            }
            if (m_Markers.TryGetValue(pointId, out var renderer))
            {
                renderer.material.color = StatusStyle.ColorOf(m_Editor.StatusOfPoint(pointId, DateTime.Today));
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
            var room = CurrentRoom(out var frame);
            if (room == null)
            {
                Debug.LogWarning("[HomeCare] まだ位置合わせができていないため、置けません。先に部屋のマーカーを映してください。");
                Destroy(spawned);
                return;
            }

            OpenFormFor(room, frame, spawned.transform.position, spawned.transform.rotation, spawned);
        }

        /// <summary>置いた場所を部屋の座標に直し、名前とやることの入力画面を出す。</summary>
        /// <param name="placed">置いてある仮の物。登録しても取り消しても消す（代わりに保存した球を出す）。</param>
        void OpenFormFor(RoomData room, RoomFrame frame, Vector3 world, Quaternion rotation, GameObject placed)
        {
            var inRoom = frame.WorldToRoom(world.ToCore());
            var rotationInRoom = frame.WorldToRoom(rotation.ToCore());

            // 入力中に画面をタップしても、物が置かれないようにする
            SetSpawnEnabled(false);
            m_Form.Open(
                input =>
                {
                    var point = m_Editor.AddPoint(room.id, input.PointName, inRoom, rotationInRoom);
                    m_Editor.AddTask(point.id, input.TaskTitle, input.Recurrence, input.FirstDueDate);
                    if (SaveChange())
                    {
                        CreateMarker(point, world);
                        Debug.Log($"[HomeCare] 「{point.name}：{input.TaskTitle}」を保存：部屋「{room.name}」の座標 {inRoom}");
                    }
                    Destroy(placed);
                    EnableSpawnSoon();
                },
                () =>
                {
                    Destroy(placed);
                    EnableSpawnSoon();
                });
        }

        /// <summary>この画面での変更（登録・完了）を保存し、少し後にクラウドへ送る。</summary>
        bool SaveChange()
        {
            m_LocalVersion++;
            RequestAutoSync(AutoSyncDelayAfterChange);
            return TrySave();
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
            renderer.material.color = StatusStyle.ColorOf(m_Editor.StatusOfPoint(point.id, DateTime.Today));
            m_Markers[point.id] = renderer;
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
