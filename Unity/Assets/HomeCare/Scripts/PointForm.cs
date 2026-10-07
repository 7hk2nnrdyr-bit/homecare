using System;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// ポイントを置いたときに、名前・やること・周期・最初の期限を入れる簡易な画面。
    /// 動作確認を優先して、Unityの古い仕組み（IMGUI）で作っている。見た目は後で作り直す。
    /// </summary>
    public class PointForm : MonoBehaviour
    {
        // 一覧画面の「修正」でも同じ選び方を使う
        internal static readonly string[] k_UnitLabels = { "日", "週", "か月", "年" };
        internal static readonly RecurrenceUnit[] k_Units =
            { RecurrenceUnit.Day, RecurrenceUnit.Week, RecurrenceUnit.Month, RecurrenceUnit.Year };

        const float k_Width = 320f;
        const float k_Height = 340f;

        Action<TaskInput> m_OnSave;
        Action m_OnCancel;
        string m_PointName;
        string m_TaskTitle;
        string m_Every;
        int m_UnitIndex;
        string m_FirstDue;
        string m_Error;

        public bool IsOpen { get; private set; }

        /// <summary>画面を開く。保存かキャンセルが押されたら、どちらかが呼ばれる。</summary>
        public void Open(Action<TaskInput> onSave, Action onCancel)
        {
            m_OnSave = onSave;
            m_OnCancel = onCancel;
            m_PointName = "";
            m_TaskTitle = "";
            m_Every = "3";
            m_UnitIndex = 2;
            m_FirstDue = DataFormat.FormatDate(DateTime.Today);
            m_Error = null;
            IsOpen = true;
        }

        void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            // 画面の細かいスマホでも小さくなりすぎないよう、画面密度に合わせて拡大する
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var area = new Rect(
                (Screen.width / scale - k_Width) / 2f,
                (Screen.height / scale - k_Height) / 2f,
                k_Width,
                k_Height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("ポイントを登録");

            GUILayout.Label("場所の名前（例：エアコン）");
            m_PointName = GUILayout.TextField(m_PointName, 30);

            GUILayout.Label("やること（例：フィルター掃除）");
            m_TaskTitle = GUILayout.TextField(m_TaskTitle, 30);

            GUILayout.Label("周期");
            GUILayout.BeginHorizontal();
            m_Every = GUILayout.TextField(m_Every, 3, GUILayout.Width(50f));
            GUILayout.Label("ごと", GUILayout.Width(30f));
            m_UnitIndex = GUILayout.SelectionGrid(m_UnitIndex, k_UnitLabels, k_UnitLabels.Length);
            GUILayout.EndHorizontal();

            GUILayout.Label("最初の期限（例：2026-11-03）");
            m_FirstDue = GUILayout.TextField(m_FirstDue, 10);

            if (!string.IsNullOrEmpty(m_Error))
            {
                var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
                style.normal.textColor = new Color(1f, 0.45f, 0.45f);
                GUILayout.Label(m_Error, style);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("キャンセル", GUILayout.Height(36f)))
            {
                IsOpen = false;
                m_OnCancel?.Invoke();
            }
            if (GUILayout.Button("保存", GUILayout.Height(36f)))
            {
                if (TaskInput.TryCreate(m_PointName, m_TaskTitle, m_Every, k_Units[m_UnitIndex], m_FirstDue,
                        out var input, out m_Error))
                {
                    IsOpen = false;
                    m_OnSave?.Invoke(input);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
