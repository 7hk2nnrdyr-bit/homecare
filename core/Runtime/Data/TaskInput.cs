using System;
using System.Globalization;
using HomeCare.Core.Scheduling;

namespace HomeCare.Core.Data
{
    /// <summary>
    /// 入力画面で入れた文字を確かめて、ポイントとタスクを作るための値にする。
    /// 画面（Unity）から切り離しておくことで、入力の確認を自動テストできる。
    /// </summary>
    public class TaskInput
    {
        public string PointName { get; }
        public string TaskTitle { get; }
        public Recurrence Recurrence { get; }
        public DateTime FirstDueDate { get; }

        private TaskInput(string pointName, string taskTitle, Recurrence recurrence, DateTime firstDueDate)
        {
            PointName = pointName;
            TaskTitle = taskTitle;
            Recurrence = recurrence;
            FirstDueDate = firstDueDate;
        }

        /// <summary>
        /// 入力を確かめる。問題があれば false を返し、error に画面に出す説明を入れる。
        /// </summary>
        /// <param name="everyText">周期の数字（例："3"）。</param>
        /// <param name="firstDueText">最初の期限（例："2026-11-03"）。</param>
        public static bool TryCreate(
            string pointName,
            string taskTitle,
            string everyText,
            RecurrenceUnit unit,
            string firstDueText,
            out TaskInput input,
            out string error)
        {
            input = null;
            pointName = pointName?.Trim();
            taskTitle = taskTitle?.Trim();

            if (string.IsNullOrEmpty(pointName))
            {
                error = "場所の名前を入れてください（例：エアコン）。";
                return false;
            }
            if (string.IsNullOrEmpty(taskTitle))
            {
                error = "やることを入れてください（例：フィルター掃除）。";
                return false;
            }
            if (!int.TryParse(everyText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var every)
                || every < 1 || every > 999)
            {
                error = "周期は1〜999の数字で入れてください。";
                return false;
            }
            if (!DateTime.TryParseExact(firstDueText?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var firstDue))
            {
                error = "最初の期限は 2026-11-03 の形で入れてください。";
                return false;
            }

            input = new TaskInput(pointName, taskTitle, new Recurrence(every, unit), firstDue);
            error = null;
            return true;
        }
    }
}
