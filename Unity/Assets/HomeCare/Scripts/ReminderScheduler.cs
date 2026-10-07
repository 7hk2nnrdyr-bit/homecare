using System;
using System.Collections;
using System.Collections.Generic;
using HomeCare.Core.Data;
using UnityEngine;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Unity.Notifications;
#endif

namespace HomeCare.App
{
    /// <summary>
    /// 期限のお知らせ（スマホの通知）を端末に予約する。予定の中身は ReminderPlanner（コア）が決める。
    /// 家のデータを保存するたびに予約を全部作り直すので、完了・修正・削除・家族の変更（同期）がすぐ反映される。
    /// 通知を出せるのはスマホ（Android・iPhone）だけ。Unityのエディターでは予定を作って画面に見せるだけにする。
    /// </summary>
    public static class ReminderScheduler
    {
        const string EnabledKey = "HomeCare.Reminders.Enabled";
        const string HourKey = "HomeCare.Reminders.Hour";
        const int DefaultHour = 9;

        /// <summary>知らせる時刻の選択肢（時）。</summary>
        public static readonly int[] Hours = { 7, 8, 9, 12, 18, 20, 21 };

        static readonly IReadOnlyList<Reminder> k_None = new Reminder[0];

        /// <summary>最後に予約したお知らせの予定（画面に見せる用）。</summary>
        public static IReadOnlyList<Reminder> Planned { get; private set; } = k_None;

        /// <summary>通知の許可の状態（画面に見せる用）。</summary>
        public static string PermissionText { get; private set; } = "";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static int Hour
        {
            get => PlayerPrefs.GetInt(HourKey, DefaultHour);
            set
            {
                PlayerPrefs.SetInt(HourKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>お知らせの予約を、家のデータの今の状態で作り直す。</summary>
        public static void Reschedule(HomeData home)
        {
            try
            {
                Planned = Enabled && home != null
                    ? ReminderPlanner.Plan(new HomeEditor(home), DateTime.Now, TimeSpan.FromHours(Hour))
                    : k_None;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                Initialize();
                NotificationCenter.CancelAllScheduledNotifications();
                foreach (var reminder in Planned)
                {
                    NotificationCenter.ScheduleNotification(
                        new Notification { Title = reminder.Title, Text = reminder.Body },
                        new NotificationDateTimeSchedule(reminder.FireAt));
                }
#endif
            }
            catch (Exception e)
            {
                // お知らせの予約に失敗しても、保存などの本来の操作は止めない
                Debug.LogWarning($"[HomeCare] お知らせを予約できませんでした：{e.Message}");
            }
        }

        /// <summary>予約したお知らせを全部取り消す（家から抜けたときなど）。</summary>
        public static void CancelAll()
        {
            Planned = k_None;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            try
            {
                Initialize();
                NotificationCenter.CancelAllScheduledNotifications();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HomeCare] お知らせを取り消せませんでした：{e.Message}");
            }
#endif
        }

        /// <summary>
        /// 通知を出してよいか、利用者に聞く（Android 13以上とiPhone。一度答えると、次からは聞かれない）。
        /// コルーチンとして StartCoroutine で動かす。
        /// </summary>
        public static IEnumerator RequestPermission()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            Initialize();
            var request = NotificationCenter.RequestPermission();
            PermissionText = "通知の許可を確認しています…";
            yield return request;
            PermissionText = request.Status == NotificationsPermissionStatus.Granted
                ? "通知は許可されています。"
                : "通知が許可されていません。スマホの「設定」→ アプリ → HomeCare → 通知 から許可してください。";
#else
            PermissionText = "（Unityのエディターでは通知は出ません。予定だけ下に表示します）";
            yield break;
#endif
        }

        /// <summary>動作確認用：10秒後に通知を1つ出す。</summary>
        public static string SendTest()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            try
            {
                Initialize();
                NotificationCenter.ScheduleNotification(
                    new Notification { Title = "HomeCare のお知らせ（お試し）", Text = "通知はこのように届きます。" },
                    new NotificationIntervalSchedule(TimeSpan.FromSeconds(10)));
                return "10秒後に通知します。ホーム画面に戻って待ってください。";
            }
            catch (Exception e)
            {
                return $"通知を予約できませんでした：{e.Message}";
            }
#else
            return "Unityのエディターでは通知は出ません。スマホで試してください。";
#endif
        }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        static bool s_Initialized;

        static void Initialize()
        {
            if (s_Initialized)
            {
                return;
            }
            var args = NotificationCenterArgs.Default;
            // 画面に表示する（Alert）を足さないと、iPhoneでは通知の表示を許可してもらえない
            args.PresentationOptions = NotificationPresentation.Alert | NotificationPresentation.Badge | NotificationPresentation.Sound;
            // Androidでは、通知の種類（チャンネル）ごとに利用者が設定でオン・オフできる
            args.AndroidChannelId = "due-reminders";
            args.AndroidChannelName = "期限のお知らせ";
            args.AndroidChannelDescription = "やることの期限の日に知らせます";
            NotificationCenter.Initialize(args);
            s_Initialized = true;
        }
#endif
    }
}
