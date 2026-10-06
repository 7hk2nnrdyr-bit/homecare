using System;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        void Awake()
        {
            m_Repository = new JsonFileHomeRepository();
            m_Editor = HomeEditor.LoadOrCreate(m_Repository, "わが家");
        }

        void Update()
        {
            // 画面を描いている途中で並び順が変わらないよう、完了の記録は描画の外で行う
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
                SceneManager.LoadScene(AppScenes.Camera);
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

        static string PlaceOf(DueItem item)
        {
            var room = item.Room != null ? item.Room.name : "（部屋なし）";
            return item.Point != null ? $"{room}・{item.Point.name}" : room;
        }
    }
}
