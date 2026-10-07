using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeCare.Core.Data
{
    /// <summary>1回分のお知らせ（スマホの通知）。</summary>
    public class Reminder
    {
        /// <summary>知らせる日時（端末の時刻）。</summary>
        public DateTime FireAt { get; }
        public string Title { get; }
        public string Body { get; }

        public Reminder(DateTime fireAt, string title, string body)
        {
            FireAt = fireAt;
            Title = title;
            Body = body;
        }
    }

    /// <summary>
    /// 期限のお知らせの予定を決める。1日に1回まで、決めた時刻にまとめて知らせる：
    /// ・その日が期限のやること
    /// ・期限が過ぎたまま残っているやること（期限の日から7日ごとにもう一度）
    /// 完了や修正、同期のたびに予定を作り直す前提なので、先の日は「今のまま完了しなかったら」で考える。
    /// 通知の予約はアプリが閉じていても届くよう端末に任せる（iPhoneは予約が64件までなので日数を絞る）。
    /// </summary>
    public static class ReminderPlanner
    {
        /// <summary>何日先まで予約するか。</summary>
        public const int DefaultDays = 30;

        /// <summary>期限切れを、何日ごとにもう一度知らせるか。</summary>
        public const int OverdueRepeatDays = 7;

        /// <summary>お知らせの本文に名前を出すやることの数。残りは「ほかN件」にする。</summary>
        public const int MaxNamesInBody = 3;

        /// <param name="now">今の日時（端末の時刻）。これより前の予定は作らない。</param>
        /// <param name="timeOfDay">知らせる時刻（例：9時なら 9:00）。</param>
        public static IReadOnlyList<Reminder> Plan(HomeEditor editor, DateTime now, TimeSpan timeOfDay, int days = DefaultDays)
        {
            var items = editor.DueList(now.Date);
            var reminders = new List<Reminder>();
            for (var i = 0; i < days; i++)
            {
                var day = now.Date.AddDays(i);
                var fireAt = day + timeOfDay;
                if (fireAt <= now)
                {
                    continue;
                }
                var dueToday = items.Where(item => item.NextDue == day).ToList();
                var overdue = items.Where(item => item.NextDue < day && (day - item.NextDue).Days % OverdueRepeatDays == 0).ToList();
                if (dueToday.Count == 0 && overdue.Count == 0)
                {
                    continue;
                }
                reminders.Add(new Reminder(fireAt, TitleOf(dueToday.Count, overdue.Count), BodyOf(dueToday.Concat(overdue).ToList())));
            }
            return reminders;
        }

        static string TitleOf(int dueToday, int overdue)
        {
            if (overdue == 0)
            {
                return $"今日が期限のやることが{dueToday}件あります";
            }
            if (dueToday == 0)
            {
                return $"期限が過ぎたやることが{overdue}件あります";
            }
            return $"今日が期限{dueToday}件・期限切れ{overdue}件";
        }

        static string BodyOf(IReadOnlyList<DueItem> items)
        {
            var names = items.Take(MaxNamesInBody).Select(NameOf);
            var body = string.Join("、", names);
            return items.Count > MaxNamesInBody ? $"{body} ほか{items.Count - MaxNamesInBody}件" : body;
        }

        static string NameOf(DueItem item) =>
            item.Point != null ? $"{item.Point.name}の{item.Task.title}" : item.Task.title;
    }
}
