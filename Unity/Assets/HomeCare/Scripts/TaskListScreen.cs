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

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
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

            m_Scroll = GUILayout.BeginScrollView(m_Scroll);
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
            if (GUILayout.Button(m_Syncing ? "同期しています…" : "クラウドと同期", GUILayout.Height(36f)))
            {
                m_PendingTransfer = SyncWithCloud;
            }
#if UNITY_EDITOR
            // クラウドからの取得を試すために、端末のデータだけを消す（ログインは残す）
            if (!m_ConfirmDelete && GUILayout.Button("（Unity）この端末の家のデータを消す", GUILayout.Height(28f)))
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
            if (!string.IsNullOrEmpty(m_CloudMessage))
            {
                GUILayout.Label(m_CloudMessage);
            }
            GUILayout.EndVertical();
        }

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
                    m_Repository.Save(result.Home);
                    m_Editor = new HomeEditor(result.Home);
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
