using System;
using System.Linq;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Sync;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// アプリを開くと最初に出るリスト画面。すべてのタスクを期限の近い順に並べ、ここから完了も記録できる。
    /// カメラを使わないので、ARが使えない端末や、電池を節約したいときにも使える。
    /// PointForm と同じく、動作確認を優先して IMGUI で作っている。
    /// </summary>
    public class TaskListScreen : MonoBehaviour
    {
        JsonFileHomeRepository m_Repository;
        HomeEditor m_Editor;
        Vector2 m_Scroll;
        string m_PendingCompleteTaskId;
        Action m_PendingTransfer;
        bool m_ShowTransfer;
        string m_TransferMessage;
        bool m_ShowCloud;
        bool m_ShowRooms;
        bool m_ShowReminders;
        string m_ReminderMessage;
        string m_NewRoomName = "";
        string m_RoomMessage;
        bool m_Syncing;
#if UNITY_EDITOR
        bool m_ConfirmDelete;
#endif
        string m_CloudMessage;
        string m_InviteText;
        string m_JoinCode = "";
        bool m_ConfirmJoin;
        string m_NameInput;
        MembersResult m_Members;
        bool m_ConfirmLeave;
        MemberView m_ConfirmRemove;

        // 登録した内容の修正・削除（一度に1件だけ開く）
        string m_EditTaskId;
        string m_EditPointName;
        string m_EditTitle;
        string m_EditEvery;
        int m_EditUnitIndex;
        string m_EditFirstDue;
        string m_EditError;
        bool m_ConfirmDeleteTask;
        string m_ListMessage;
        string m_EditRoomId;
        string m_EditRoomName;
        string m_ConfirmDeleteRoomId;

        // 自動同期：アプリを開いたとき・戻ってきたとき・この画面で変更したときに、ボタンを押さなくても同期する
        const float AutoSyncDelayAfterChange = 3f;
        float m_AutoSyncAt = -1f;
        string m_AutoSyncStatus;

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            m_NameInput = CloudSync.MyName;
        }

        void Start()
        {
            // アプリの起動時はARが自動で動き出すので、リスト画面では止めておく
            AppScenes.StopAR();

            // 起動したときと、カメラの画面から戻ってきたとき（カメラで登録した分を送る）
            RequestAutoSync(0f);

            // 日付が変わっているかもしれないので、期限のお知らせを作り直す。初めてなら通知の許可を聞く
            ReminderScheduler.Reschedule(m_Editor.Home);
            if (ReminderScheduler.Enabled)
            {
                StartCoroutine(ReminderScheduler.RequestPermission());
            }
        }

        void OnApplicationPause(bool paused)
        {
            // ほかのアプリから戻ってきたとき（その間に家族が変えた分を受け取る）
            if (!paused)
            {
                RequestAutoSync(0f);
                ReminderScheduler.Reschedule(m_Editor.Home);
            }
        }

        /// <summary>
        /// 少し後に自動同期する。続けて変更したときは、最後の変更から数秒待ってまとめて1回にする。
        /// Firebaseの設定が済んでいなければ何もしない。
        /// </summary>
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
            m_AutoSyncStatus = "自動同期：通信しています…";
            try
            {
                // クラウドにまだ家が無ければ作らない（作るのは「クラウドと同期」を押したとき）
                var result = await CloudSync.SyncAsync(m_Editor.Home, createIfMissing: false);
                switch (result.Outcome)
                {
                    case SyncOutcome.Failed:
                        m_AutoSyncStatus = $"自動同期：できませんでした（{DateTime.Now:H:mm}）。{result.Message}";
                        break;
                    case SyncOutcome.Skipped:
                        m_AutoSyncStatus = null;
                        break;
                    default:
                        UseHome(result.Home);
                        m_AutoSyncStatus = $"自動同期：{DateTime.Now:H:mm} {result.Message}";
                        break;
                }
            }
            catch (Exception e)
            {
                m_AutoSyncStatus = $"自動同期：できませんでした。{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            if (m_AutoSyncStatus != null)
            {
                Debug.Log($"[HomeCare] {m_AutoSyncStatus}");
            }
        }

        void Update()
        {
            // 同期の途中でデータを変えると、同期の結果で上書きされてしまうので待つ
            if (m_Syncing)
            {
                return;
            }

            if (m_AutoSyncAt >= 0f && Time.realtimeSinceStartup >= m_AutoSyncAt)
            {
                m_AutoSyncAt = -1f;
                AutoSync();
                return;
            }

            // 画面を描いている途中で中身が変わらないよう、完了の記録やデータの受け渡しは描画の外で行う
            if (m_PendingTransfer != null)
            {
                var transfer = m_PendingTransfer;
                m_PendingTransfer = null;
                transfer();
            }

            if (m_PendingCompleteTaskId == null)
            {
                return;
            }
            var task = m_Editor.FindTask(m_PendingCompleteTaskId);
            m_PendingCompleteTaskId = null;
            if (task == null)
            {
                return;
            }
            m_Editor.CompleteTask(task.id, DateTime.Today);
            try
            {
                m_Repository.Save(m_Editor.Home);
                Debug.Log($"[HomeCare] 「{task.title}」を完了しました。次回期限：{DataFormat.FormatDate(HomeEditor.NextDueDate(task))}");
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HomeCare] 保存に失敗しました：{e.Message}");
            }
        }

        void OnGUI()
        {
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var area = new Rect(10f, 30f, Screen.width / scale - 20f, Screen.height / scale - 40f);
            var today = DateTime.Today;
            var items = m_Editor.DueList(today);

            GUILayout.BeginArea(area);
            // 受け渡しやクラウドの欄を開くと画面に収まらないことがあるので、画面全体をスクロールできるようにする
            m_Scroll = GUILayout.BeginScrollView(m_Scroll);
            GUILayout.Label("やること一覧（期限の近い順）");
            if (!string.IsNullOrEmpty(m_AutoSyncStatus))
            {
                GUILayout.Label(m_AutoSyncStatus);
            }
            if (!string.IsNullOrEmpty(m_ListMessage))
            {
                GUILayout.Label(m_ListMessage);
            }
            if (GUILayout.Button("カメラで見る（場所の登録・確認）", GUILayout.Height(44f)))
            {
                AppScenes.OpenCamera();
            }

            if (GUILayout.Button(m_ShowTransfer ? "データの受け渡し ▲" : "データの受け渡し ▼", GUILayout.Height(32f)))
            {
                m_ShowTransfer = !m_ShowTransfer;
            }
            if (m_ShowTransfer)
            {
                DrawTransfer();
            }

            if (GUILayout.Button(m_ShowRooms ? "部屋とマーカー ▲" : "部屋とマーカー ▼", GUILayout.Height(32f)))
            {
                m_ShowRooms = !m_ShowRooms;
            }
            if (m_ShowRooms)
            {
                DrawRooms();
            }

            if (GUILayout.Button(m_ShowReminders ? "お知らせ ▲" : "お知らせ ▼", GUILayout.Height(32f)))
            {
                m_ShowReminders = !m_ShowReminders;
            }
            if (m_ShowReminders)
            {
                DrawReminders();
            }

            if (GUILayout.Button(m_ShowCloud ? "クラウド ▲" : "クラウド ▼", GUILayout.Height(32f)))
            {
                m_ShowCloud = !m_ShowCloud;
            }
            if (m_ShowCloud)
            {
                DrawCloud();
            }

            if (items.Count == 0)
            {
                GUILayout.Label("まだ何も登録されていません。「カメラで見る」から、場所とやることを登録しましょう。");
            }

            foreach (var item in items)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);

                // 状態の色の四角
                var dot = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f), GUILayout.Height(14f));
                var previousColor = GUI.color;
                GUI.color = StatusStyle.ColorOf(item.Status);
                GUI.DrawTexture(dot, Texture2D.whiteTexture);
                GUI.color = previousColor;

                GUILayout.BeginVertical();
                GUILayout.Label($"{StatusStyle.MarkOf(item.Status)} {item.Task.title}（{DueLabel.For(today, item.NextDue)}）");
                var recurrence = DueLabel.For(DataFormat.ToRecurrence(item.Task.recurrence));
                GUILayout.Label($"{PlaceOf(item)}　次回 {DataFormat.FormatDate(item.NextDue)}・{recurrence}");
                GUILayout.EndVertical();

                if (GUILayout.Button("完了", GUILayout.Width(64f), GUILayout.Height(40f)))
                {
                    m_PendingCompleteTaskId = item.Task.id;
                }
                var editing = m_EditTaskId == item.Task.id;
                if (GUILayout.Button(editing ? "閉じる" : "修正", GUILayout.Width(64f), GUILayout.Height(40f)))
                {
                    if (editing)
                    {
                        CloseTaskEditor();
                    }
                    else
                    {
                        OpenTaskEditor(item);
                    }
                }
                GUILayout.EndHorizontal();
                if (editing)
                {
                    DrawTaskEditor(item);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void OpenTaskEditor(DueItem item)
        {
            var recurrence = DataFormat.ToRecurrence(item.Task.recurrence);
            m_EditTaskId = item.Task.id;
            m_EditPointName = item.Point != null ? item.Point.name : "";
            m_EditTitle = item.Task.title;
            m_EditEvery = recurrence.Every.ToString();
            m_EditUnitIndex = Math.Max(0, Array.IndexOf(PointForm.k_Units, recurrence.Unit));
            m_EditFirstDue = item.Task.firstDueDate;
            m_EditError = null;
            m_ConfirmDeleteTask = false;
            m_ListMessage = null;
        }

        void CloseTaskEditor()
        {
            m_EditTaskId = null;
            m_EditError = null;
            m_ConfirmDeleteTask = false;
        }

        /// <summary>一覧の行の下に出す、やることの修正・削除の欄。</summary>
        void DrawTaskEditor(DueItem item)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (item.Point != null)
            {
                GUILayout.Label("場所の名前（例：エアコン）");
                m_EditPointName = GUILayout.TextField(m_EditPointName ?? "", 30, GUILayout.Height(32f));
            }
            GUILayout.Label("やること（例：フィルター掃除）");
            m_EditTitle = GUILayout.TextField(m_EditTitle ?? "", 30, GUILayout.Height(32f));

            GUILayout.Label("周期");
            GUILayout.BeginHorizontal();
            m_EditEvery = GUILayout.TextField(m_EditEvery ?? "", 3, GUILayout.Width(50f), GUILayout.Height(32f));
            GUILayout.Label("ごと", GUILayout.Width(30f));
            m_EditUnitIndex = GUILayout.SelectionGrid(m_EditUnitIndex, PointForm.k_UnitLabels, PointForm.k_UnitLabels.Length,
                GUILayout.Height(32f));
            GUILayout.EndHorizontal();

            // 一度でも完了していれば、次回期限は前回実施日から数えるので、最初の期限は使われない
            GUILayout.Label(string.IsNullOrEmpty(item.Task.lastDoneDate)
                ? "最初の期限（例：2026-11-03）"
                : "最初の期限（完了したことがあるので、次回期限は前回実施日から数えます）");
            m_EditFirstDue = GUILayout.TextField(m_EditFirstDue ?? "", 10, GUILayout.Height(32f));

            if (!string.IsNullOrEmpty(m_EditError))
            {
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                style.normal.textColor = new Color(1f, 0.45f, 0.45f);
                GUILayout.Label(m_EditError, style);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存", GUILayout.Height(36f)))
            {
                m_PendingTransfer = SaveTaskEdit;
            }
            if (GUILayout.Button("やめる", GUILayout.Height(36f)))
            {
                CloseTaskEditor();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            if (!m_ConfirmDeleteTask)
            {
                if (GUILayout.Button("このやることを削除", GUILayout.Height(32f)))
                {
                    m_ConfirmDeleteTask = true;
                }
            }
            else
            {
                // やることの無い場所は残しても使わないので、最後の1件なら場所も一緒に消える
                var withPoint = item.Point != null && m_Editor.TasksOfPoint(item.Point.id).Count() == 1;
                GUILayout.Label(withPoint
                    ? $"「{item.Task.title}」と、場所「{item.Point.name}」を削除します。実施記録も見られなくなります。"
                    : $"「{item.Task.title}」を削除します。実施記録も見られなくなります。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("削除する", GUILayout.Height(32f)))
                {
                    m_PendingTransfer = DeleteTask;
                }
                if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                {
                    m_ConfirmDeleteTask = false;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        void SaveTaskEdit()
        {
            var task = m_Editor.FindTask(m_EditTaskId);
            if (task == null)
            {
                CloseTaskEditor();
                m_ListMessage = "このやることは、ほかの端末で削除されました。";
                return;
            }
            var point = string.IsNullOrEmpty(task.pointId) ? null : m_Editor.FindPoint(task.pointId);
            // 場所の無いやること（古いデータ）は、場所の名前を確かめない
            var pointName = point != null ? m_EditPointName : "-";
            if (!TaskInput.TryCreate(pointName, m_EditTitle, m_EditEvery, PointForm.k_Units[m_EditUnitIndex], m_EditFirstDue,
                    out var input, out m_EditError))
            {
                return;
            }
            try
            {
                if (point != null)
                {
                    m_Editor.RenamePoint(point.id, input.PointName);
                }
                m_Editor.UpdateTask(task.id, input.TaskTitle, input.Recurrence, input.FirstDueDate);
                m_Repository.Save(m_Editor.Home);
                m_ListMessage = $"「{task.title}」を修正しました。";
                CloseTaskEditor();
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (ArgumentException e)
            {
                m_EditError = e.Message;
            }
            catch (Exception e)
            {
                m_EditError = $"保存に失敗しました：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_EditError ?? m_ListMessage}");
        }

        void DeleteTask()
        {
            var task = m_Editor.FindTask(m_EditTaskId);
            CloseTaskEditor();
            if (task == null)
            {
                return;
            }
            var point = string.IsNullOrEmpty(task.pointId) ? null : m_Editor.FindPoint(task.pointId);
            try
            {
                var pointDeleted = m_Editor.DeleteTask(task.id);
                m_Repository.Save(m_Editor.Home);
                m_ListMessage = pointDeleted
                    ? $"「{task.title}」と、場所「{point.name}」を削除しました。"
                    : $"「{task.title}」を削除しました。";
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (Exception e)
            {
                m_ListMessage = $"削除できませんでした：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_ListMessage}");
        }

        void DrawTransfer()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ほかの端末と同じ家のデータを使うときに使います。");
            if (GUILayout.Button("書き出す（ファイルとクリップボード）", GUILayout.Height(36f)))
            {
                m_PendingTransfer = Export;
            }
            if (GUILayout.Button("ファイルから読み込む", GUILayout.Height(36f)))
            {
                m_PendingTransfer = () => ApplyImport(HomeTransfer.ImportFromFile(m_Editor.Home));
            }
            if (GUILayout.Button("クリップボードから読み込む", GUILayout.Height(36f)))
            {
                m_PendingTransfer = () => ApplyImport(HomeTransfer.ImportFromClipboard(m_Editor.Home));
            }
#if UNITY_EDITOR
            if (GUILayout.Button("（Unity）ファイルの場所を開く", GUILayout.Height(28f)))
            {
                Application.OpenURL("file://" + Application.persistentDataPath);
            }
#endif
            if (!string.IsNullOrEmpty(m_TransferMessage))
            {
                GUILayout.Label(m_TransferMessage);
            }
            GUILayout.EndVertical();
        }

        void DrawRooms()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("部屋ごとに印刷したマーカーを貼ります。カメラ画面では、最後に映したマーカーの部屋に登録されます。");
            foreach (var room in m_Editor.ActiveRooms())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"・{room.name}　マーカー {m_Editor.MarkerOf(room.id) ?? "なし"}");
                if (m_EditRoomId == null && m_ConfirmDeleteRoomId == null)
                {
                    if (GUILayout.Button("名前を変える", GUILayout.Width(110f), GUILayout.Height(28f)))
                    {
                        m_EditRoomId = room.id;
                        m_EditRoomName = room.name;
                        m_RoomMessage = null;
                    }
                    if (GUILayout.Button("削除", GUILayout.Width(60f), GUILayout.Height(28f)))
                    {
                        m_ConfirmDeleteRoomId = room.id;
                        m_RoomMessage = null;
                    }
                }
                GUILayout.EndHorizontal();

                if (m_EditRoomId == room.id)
                {
                    m_EditRoomName = GUILayout.TextField(m_EditRoomName ?? "", 20, GUILayout.Height(32f));
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("保存", GUILayout.Height(32f)))
                    {
                        m_PendingTransfer = RenameRoom;
                    }
                    if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                    {
                        m_EditRoomId = null;
                    }
                    GUILayout.EndHorizontal();
                }
                if (m_ConfirmDeleteRoomId == room.id)
                {
                    var points = m_Editor.PointsInRoom(room.id).ToList();
                    var tasks = points.Sum(p => m_Editor.TasksOfPoint(p.id).Count());
                    GUILayout.Label($"部屋「{room.name}」を削除します。中の場所{points.Count}件・やること{tasks}件も削除されます。");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("削除する", GUILayout.Height(32f)))
                    {
                        m_PendingTransfer = DeleteRoom;
                    }
                    if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                    {
                        m_ConfirmDeleteRoomId = null;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Space(8f);
            GUILayout.Label("■ 部屋を追加する（例：寝室）");
            m_NewRoomName = GUILayout.TextField(m_NewRoomName ?? "", 20, GUILayout.Height(32f));
            if (GUILayout.Button("部屋を追加", GUILayout.Height(36f)))
            {
                m_PendingTransfer = AddRoom;
            }
            if (!string.IsNullOrEmpty(m_RoomMessage))
            {
                GUILayout.Label(m_RoomMessage);
            }
            GUILayout.EndVertical();
        }

        void AddRoom()
        {
            try
            {
                var room = m_Editor.AddRoomWithMarker(m_NewRoomName, MarkerCatalog.Ids);
                m_Repository.Save(m_Editor.Home);
                var marker = m_Editor.MarkerOf(room.id);
                m_RoomMessage = $"部屋「{room.name}」を追加しました。マーカー{marker}を印刷して、{room.name}の壁などに貼ってください" +
                    $"（印刷用：markers/{marker}-print.pdf）。";
                m_NewRoomName = "";
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (ArgumentException e)
            {
                m_RoomMessage = e.Message;
            }
            catch (Exception e)
            {
                m_RoomMessage = $"保存に失敗しました：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_RoomMessage}");
        }

        void RenameRoom()
        {
            var roomId = m_EditRoomId;
            try
            {
                m_Editor.RenameRoom(roomId, m_EditRoomName);
                m_Repository.Save(m_Editor.Home);
                m_EditRoomId = null;
                m_RoomMessage = $"部屋の名前を「{m_EditRoomName.Trim()}」にしました。";
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (ArgumentException e)
            {
                m_RoomMessage = e.Message;
            }
            catch (Exception e)
            {
                m_RoomMessage = $"保存に失敗しました：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_RoomMessage}");
        }

        void DeleteRoom()
        {
            var roomId = m_ConfirmDeleteRoomId;
            m_ConfirmDeleteRoomId = null;
            var room = m_Editor.ActiveRooms().FirstOrDefault(r => r.id == roomId);
            if (room == null)
            {
                return;
            }
            var marker = m_Editor.MarkerOf(room.id);
            try
            {
                m_Editor.DeleteRoom(room.id);
                m_Repository.Save(m_Editor.Home);
                m_RoomMessage = $"部屋「{room.name}」を削除しました。" +
                    (marker != null ? $"マーカー{marker}は、次に追加する部屋で使われます（貼ってあるマーカーははがしてください）。" : "");
                RequestAutoSync(AutoSyncDelayAfterChange);
            }
            catch (ArgumentException e)
            {
                m_RoomMessage = e.Message;
            }
            catch (Exception e)
            {
                m_RoomMessage = $"削除できませんでした：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_RoomMessage}");
        }

        /// <summary>
        /// 家のデータを入れ替えたあと（同期・参加・端末の切り替えなど）、開いていた修正の欄の相手が
        /// もう無ければ閉じる（ほかの端末で削除されたときなど）。
        /// </summary>
        void ForgetMissingEdits()
        {
            if (m_EditTaskId != null && m_Editor.FindTask(m_EditTaskId) == null)
            {
                CloseTaskEditor();
            }
            if (m_EditRoomId != null && !m_Editor.ActiveRooms().Any(r => r.id == m_EditRoomId))
            {
                m_EditRoomId = null;
            }
            if (m_ConfirmDeleteRoomId != null && !m_Editor.ActiveRooms().Any(r => r.id == m_ConfirmDeleteRoomId))
            {
                m_ConfirmDeleteRoomId = null;
            }
        }

        void DrawReminders()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("期限の日に、スマホの通知で知らせます。期限が過ぎたままのやることは、7日ごとにもう一度知らせます。");
            var enabled = GUILayout.Toggle(ReminderScheduler.Enabled, " 期限のお知らせを使う", GUILayout.Height(32f));
            if (enabled != ReminderScheduler.Enabled)
            {
                m_PendingTransfer = () => SetRemindersEnabled(enabled);
            }
            if (!string.IsNullOrEmpty(ReminderScheduler.PermissionText))
            {
                GUILayout.Label(ReminderScheduler.PermissionText);
            }

            if (ReminderScheduler.Enabled)
            {
                GUILayout.Label("知らせる時刻");
                var hours = ReminderScheduler.Hours;
                var current = Array.IndexOf(hours, ReminderScheduler.Hour);
                var selected = GUILayout.SelectionGrid(current, hours.Select(h => $"{h}時").ToArray(), 4, GUILayout.Height(64f));
                if (selected != current && selected >= 0)
                {
                    m_PendingTransfer = () => SetReminderHour(hours[selected]);
                }

                GUILayout.Label("これからのお知らせ（先の5回分）");
                var planned = ReminderScheduler.Planned;
                if (planned.Count == 0)
                {
                    GUILayout.Label("・30日以内に知らせることはありません。");
                }
                foreach (var reminder in planned.Take(5))
                {
                    GUILayout.Label($"・{reminder.FireAt:M月d日 H:mm}　{reminder.Title}\n　{reminder.Body}");
                }

                if (GUILayout.Button("試しに10秒後に通知する", GUILayout.Height(32f)))
                {
                    m_PendingTransfer = () => m_ReminderMessage = ReminderScheduler.SendTest();
                }
            }
            if (!string.IsNullOrEmpty(m_ReminderMessage))
            {
                GUILayout.Label(m_ReminderMessage);
            }
            GUILayout.EndVertical();
        }

        void SetRemindersEnabled(bool enabled)
        {
            ReminderScheduler.Enabled = enabled;
            ReminderScheduler.Reschedule(m_Editor.Home);
            m_ReminderMessage = enabled ? "期限のお知らせを使います。" : "期限のお知らせを止めました。";
            if (enabled)
            {
                StartCoroutine(ReminderScheduler.RequestPermission());
            }
            Debug.Log($"[HomeCare] {m_ReminderMessage}");
        }

        void SetReminderHour(int hour)
        {
            ReminderScheduler.Hour = hour;
            ReminderScheduler.Reschedule(m_Editor.Home);
            m_ReminderMessage = $"毎日{hour}時に知らせます（その日に知らせることがあるときだけ）。";
            Debug.Log($"[HomeCare] {m_ReminderMessage}");
        }

        void DrawCloud()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("この端末の家のデータを、クラウド（Firebase）と同期します。");
            var previousEnabled = GUI.enabled;
            GUI.enabled = !m_Syncing;
            if (GUILayout.Button(m_Syncing ? "通信しています…" : "クラウドと同期", GUILayout.Height(36f)))
            {
                m_PendingTransfer = SyncWithCloud;
            }
            if (!string.IsNullOrEmpty(m_CloudMessage))
            {
                GUILayout.Label(m_CloudMessage);
            }

            // ---- 家族と共有する ----
            GUILayout.Space(8f);
            GUILayout.Label("■ 家族を招待する");
            if (GUILayout.Button("招待コードを作る", GUILayout.Height(36f)))
            {
                m_PendingTransfer = CreateInvite;
            }
            if (!string.IsNullOrEmpty(m_InviteText))
            {
                GUILayout.Label(m_InviteText);
            }

            GUILayout.Space(8f);
            GUILayout.Label("■ 招待コードで家に参加する");
            m_JoinCode = GUILayout.TextField(m_JoinCode ?? "", 12, GUILayout.Height(32f));
            if (!m_ConfirmJoin && GUILayout.Button("参加する", GUILayout.Height(36f)))
            {
                // この端末にすでに家のデータがあれば、置き換わることを先に確かめる
                if (HomeImporter.IsEmpty(m_Editor.Home))
                {
                    m_PendingTransfer = JoinHome;
                }
                else
                {
                    m_ConfirmJoin = true;
                }
            }
            if (m_ConfirmJoin)
            {
                GUILayout.Label("この端末の家のデータは、参加する家のデータに置き換わります。この端末だけにある内容は消えます。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("置き換えて参加する", GUILayout.Height(32f)))
                {
                    m_ConfirmJoin = false;
                    m_PendingTransfer = JoinHome;
                }
                if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                {
                    m_ConfirmJoin = false;
                }
                GUILayout.EndHorizontal();
            }

            DrawMembers();

#if UNITY_EDITOR
            // ---- Unityでの動作確認用 ----
            GUILayout.Space(8f);
            GUILayout.Label($"■（Unity）動作確認用　今は「端末{DeviceSlot.Current}」");
            if (GUILayout.Button(DeviceSlot.Current == "A" ? "端末Bに切り替える（別の家族の端末のつもり）" : "端末Aに切り替える", GUILayout.Height(28f)))
            {
                m_PendingTransfer = SwitchDevice;
            }
            // クラウドからの取得を試すために、端末のデータだけを消す（ログインは残す）
            if (!m_ConfirmDelete && GUILayout.Button("この端末の家のデータを消す", GUILayout.Height(28f)))
            {
                m_ConfirmDelete = true;
            }
            if (m_ConfirmDelete)
            {
                GUILayout.Label("この端末の家のデータを消します。クラウドに同期していない内容は戻せません。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("消す", GUILayout.Height(28f)))
                {
                    m_ConfirmDelete = false;
                    m_PendingTransfer = DeleteLocalHome;
                }
                if (GUILayout.Button("やめる", GUILayout.Height(28f)))
                {
                    m_ConfirmDelete = false;
                }
                GUILayout.EndHorizontal();
            }
#endif
            GUI.enabled = previousEnabled;
            GUILayout.EndVertical();
        }

        void DrawMembers()
        {
            GUILayout.Space(8f);
            GUILayout.Label("■ この家のメンバー");
            GUILayout.Label($"この端末の呼び名（{MemberName.MaxLength}文字まで。例：パパのiPhone）");
            m_NameInput = GUILayout.TextField(m_NameInput ?? "", MemberName.MaxLength, GUILayout.Height(32f));
            if (GUILayout.Button("呼び名を保存してメンバーを表示", GUILayout.Height(36f)))
            {
                m_PendingTransfer = ShowMembers;
            }
            if (m_Members == null || !m_Members.Ok)
            {
                return;
            }

            foreach (var member in m_Members.Members)
            {
                var marks = (member.IsOwner ? "（持ち主）" : "") + (member.IsMe ? "（この端末）" : "");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"・{member.Name}{marks}");
                // 持ち主だけが、ほかのメンバーを外せる（なくした端末やアプリを入れ直す前の端末など）
                if (m_Members.IAmOwner && !member.IsMe && m_ConfirmRemove == null
                    && GUILayout.Button("外す", GUILayout.Width(80f), GUILayout.Height(28f)))
                {
                    m_ConfirmRemove = member;
                }
                GUILayout.EndHorizontal();
            }
            if (m_ConfirmRemove != null)
            {
                GUILayout.Label($"「{m_ConfirmRemove.Name}」をこの家のメンバーから外します。その端末では、この家を見られなくなります。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("外す", GUILayout.Height(32f)))
                {
                    m_PendingTransfer = RemoveMember;
                }
                if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                {
                    m_ConfirmRemove = null;
                }
                GUILayout.EndHorizontal();
            }

            // 持ち主は抜けられない（家族が使えなくならないように）
            if (m_Members.IAmOwner)
            {
                return;
            }
            if (!m_ConfirmLeave && GUILayout.Button("この家から抜ける", GUILayout.Height(32f)))
            {
                m_ConfirmLeave = true;
            }
            if (m_ConfirmLeave)
            {
                GUILayout.Label("この家から抜けます。この端末の家のデータは消えます（クラウドの家は残り、家族は使い続けられます）。");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("抜ける", GUILayout.Height(32f)))
                {
                    m_ConfirmLeave = false;
                    m_PendingTransfer = LeaveHome;
                }
                if (GUILayout.Button("やめる", GUILayout.Height(32f)))
                {
                    m_ConfirmLeave = false;
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>呼び名を保存し、同期して（呼び名がクラウドに届く）、メンバー一覧を取得する。</summary>
        async void ShowMembers()
        {
            m_Syncing = true;
            m_Members = null;
            m_ConfirmLeave = false;
            m_ConfirmRemove = null;
            m_CloudMessage = "メンバーを取得しています…";
            try
            {
                CloudSync.MyName = m_NameInput;
                m_NameInput = CloudSync.MyName;
                var synced = await CloudSync.SyncAsync(m_Editor.Home);
                if (synced.Outcome == SyncOutcome.Failed)
                {
                    m_CloudMessage = synced.Message;
                    return;
                }
                UseHome(synced.Home);
                m_Members = await CloudSync.LoadMembersAsync(m_Editor.Home);
                m_CloudMessage = m_Members.Message;
            }
            catch (Exception e)
            {
                m_CloudMessage = $"メンバーを取得できませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

        async void LeaveHome()
        {
            m_Syncing = true;
            m_CloudMessage = "家から抜けています…";
            try
            {
                var result = await CloudSync.LeaveAsync(m_Editor.Home);
                m_CloudMessage = result.Message;
                if (result.Ok)
                {
                    // 抜けた家のデータは端末に残さず、新しい空の家から始める
                    m_Repository.Delete();
                    m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
                    m_Members = null;
                    ForgetMissingEdits();
                }
            }
            catch (Exception e)
            {
                m_CloudMessage = $"抜けられませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

        async void RemoveMember()
        {
            var member = m_ConfirmRemove;
            m_ConfirmRemove = null;
            m_Syncing = true;
            m_CloudMessage = "メンバーを外しています…";
            try
            {
                var result = await CloudSync.RemoveMemberAsync(m_Editor.Home, member);
                m_CloudMessage = result.Message;
                if (result.Ok)
                {
                    var members = await CloudSync.LoadMembersAsync(m_Editor.Home);
                    m_Members = members.Ok ? members : null;
                }
            }
            catch (Exception e)
            {
                m_CloudMessage = $"外せませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

        /// <summary>同期や参加で受け取った家のデータを、この端末に保存して画面に出す。</summary>
        void UseHome(HomeData home)
        {
            m_Repository.Save(home);
            m_Editor = new HomeEditor(home);
            ForgetMissingEdits();
        }

        async void CreateInvite()
        {
            m_Syncing = true;
            m_InviteText = null;
            m_CloudMessage = "招待コードを作っています…";
            try
            {
                // 招待する家がクラウドに最新の状態であるよう、先に同期する
                var synced = await CloudSync.SyncAsync(m_Editor.Home);
                if (synced.Outcome == SyncOutcome.Failed)
                {
                    m_CloudMessage = synced.Message;
                    return;
                }
                UseHome(synced.Home);

                var result = await CloudSync.CreateInviteAsync(m_Editor.Home);
                m_CloudMessage = result.Message;
                if (result.Ok)
                {
                    var code = InviteCode.Format(result.Invite.code);
                    GUIUtility.systemCopyBuffer = code;
                    var expires = result.Invite.ExpiresAtUtc.ToLocalTime();
                    m_InviteText = $"招待コード：{code}\n{expires:M月d日 H:mm}まで使えます。クリップボードにも入れました。\n" +
                        "家族の端末の「招待コードで家に参加する」に入力してもらってください。";
                    m_CloudMessage = null;
                }
            }
            catch (Exception e)
            {
                m_CloudMessage = $"招待コードを作れませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
                Debug.Log($"[HomeCare] {m_CloudMessage ?? m_InviteText}");
            }
        }

        async void JoinHome()
        {
            m_Syncing = true;
            m_CloudMessage = "家に参加しています…";
            try
            {
                var result = await CloudSync.JoinAsync(m_Editor.Home, m_JoinCode);
                m_CloudMessage = result.Message;
                if (result.Outcome != SyncOutcome.Failed)
                {
                    UseHome(result.Home);
                    m_JoinCode = "";
                    m_Members = null;
                }
            }
            catch (Exception e)
            {
                m_CloudMessage = $"参加できませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

#if UNITY_EDITOR
        void SwitchDevice()
        {
            DeviceSlot.Toggle();
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            ForgetMissingEdits();
            m_InviteText = null;
            m_ConfirmJoin = false;
            m_Members = null;
            m_ConfirmLeave = false;
            m_ConfirmRemove = null;
            m_NameInput = CloudSync.MyName;
            m_AutoSyncStatus = null;
            RequestAutoSync(0f);
            m_CloudMessage = $"端末{DeviceSlot.Current}に切り替えました。家のデータとログインは、端末ごとに別になります。";
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }
#endif

        async void SyncWithCloud()
        {
            m_Syncing = true;
            m_CloudMessage = "同期しています…";
            try
            {
                var result = await CloudSync.SyncAsync(m_Editor.Home);
                m_CloudMessage = result.Message;
                if (result.Outcome != SyncOutcome.Failed)
                {
                    UseHome(result.Home);
                }
            }
            catch (Exception e)
            {
                m_CloudMessage = $"同期できませんでした：{e.Message}";
            }
            finally
            {
                m_Syncing = false;
            }
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

        void DeleteLocalHome()
        {
            m_Repository.Delete();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
            m_Members = null;
            ForgetMissingEdits();
            m_CloudMessage = "この端末の家のデータを消しました。「クラウドと同期」でクラウドから取得できます。";
            Debug.Log($"[HomeCare] {m_CloudMessage}");
        }

        void Export()
        {
            try
            {
                m_TransferMessage = HomeTransfer.Export(m_Editor.Home);
            }
            catch (Exception e)
            {
                m_TransferMessage = $"書き出せませんでした：{e.Message}";
            }
            Debug.Log($"[HomeCare] {m_TransferMessage}");
        }

        void ApplyImport(ImportResult result)
        {
            m_TransferMessage = result.Message;
            if (result.Outcome != ImportOutcome.Rejected)
            {
                try
                {
                    m_Repository.Save(result.Home);
                    m_Editor = new HomeEditor(result.Home);
                    ForgetMissingEdits();
                    RequestAutoSync(AutoSyncDelayAfterChange);
                }
                catch (Exception e)
                {
                    m_TransferMessage = $"保存に失敗しました：{e.Message}";
                }
            }
            Debug.Log($"[HomeCare] {m_TransferMessage}");
        }

        static string PlaceOf(DueItem item)
        {
            var room = item.Room != null ? item.Room.name : "（部屋なし）";
            return item.Point != null ? $"{room}・{item.Point.name}" : room;
        }
    }
}
