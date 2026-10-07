using System;
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
        }

        void Update()
        {
            // 同期の途中でデータを変えると、同期の結果で上書きされてしまうので待つ
            if (m_Syncing)
            {
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
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
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
            m_InviteText = null;
            m_ConfirmJoin = false;
            m_Members = null;
            m_ConfirmLeave = false;
            m_ConfirmRemove = null;
            m_NameInput = CloudSync.MyName;
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
