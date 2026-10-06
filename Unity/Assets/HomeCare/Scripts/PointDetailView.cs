using System;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 球をタップしたときに出す詳細画面。タスクごとに、周期・次回期限・前回実施日と完了ボタンを出す。
    /// PointForm と同じく、動作確認を優先して IMGUI で作っている。
    /// </summary>
    public class PointDetailView : MonoBehaviour
    {
        const float k_Width = 340f;
        const float k_Height = 360f;

        HomeEditor m_Editor;
        string m_PointId;
        Action<string> m_OnComplete;
        Action m_OnClose;
        Vector2 m_Scroll;

        public bool IsOpen { get; private set; }

        /// <summary>詳細を開く。完了が押されたらタスクIDを渡して onComplete を呼ぶ。</summary>
        public void Open(HomeEditor editor, string pointId, Action<string> onComplete, Action onClose)
        {
            m_Editor = editor;
            m_PointId = pointId;
            m_OnComplete = onComplete;
            m_OnClose = onClose;
            m_Scroll = Vector2.zero;
            IsOpen = true;
        }

        void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }
            var point = m_Editor.FindPoint(m_PointId);
            if (point == null)
            {
                Close();
                return;
            }

            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var area = new Rect(
                (Screen.width / scale - k_Width) / 2f,
                (Screen.height / scale - k_Height) / 2f,
                k_Width,
                k_Height);
            var today = DateTime.Today;

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label(point.name);

            m_Scroll = GUILayout.BeginScrollView(m_Scroll);
            foreach (var task in m_Editor.TasksOfPoint(point.id))
            {
                var next = HomeEditor.NextDueDate(task);
                var status = HomeEditor.StatusOf(task, today);
                var recurrence = DataFormat.ToRecurrence(task.recurrence);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{StatusStyle.MarkOf(status)} {task.title}（{DueLabel.For(today, next)}）");
                GUILayout.Label($"周期：{DueLabel.For(recurrence)}");
                GUILayout.Label($"次回期限：{DataFormat.FormatDate(next)}");
                GUILayout.Label($"前回実施：{(string.IsNullOrEmpty(task.lastDoneDate) ? "まだ" : task.lastDoneDate)}");
                if (GUILayout.Button("今日、完了した", GUILayout.Height(36f)))
                {
                    m_OnComplete?.Invoke(task.id);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("閉じる", GUILayout.Height(36f)))
            {
                Close();
            }
            GUILayout.EndArea();
        }

        void Close()
        {
            IsOpen = false;
            m_OnClose?.Invoke();
        }
    }
}
