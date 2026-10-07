using System.Threading.Tasks;
using HomeCare.Core.Data;
using HomeCare.Core.Firebase;
using HomeCare.Core.Sync;

namespace HomeCare.App
{
    /// <summary>
    /// 画面からクラウド同期を使う入り口。Firebaseにつなぐのはここだけで、
    /// 同期の手順（HomeSync）や画面はFirebaseのことを知らない。
    /// </summary>
    public static class CloudSync
    {
        static HomeSync s_Sync;
        static string s_SyncFor;

        /// <summary>Firebaseの設定が済んでいるか。済んでいなければ、何をすればよいかを返す。</summary>
        public static bool IsConfigured(out string whatToDo)
        {
            var settings = FirebaseSettings.Load();
            if (settings == null)
            {
                whatToDo = "Firebaseの設定がまだです。Unityのメニュー「HomeCare → Firebaseの設定を作る」から設定してください。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(settings.projectId) || string.IsNullOrWhiteSpace(settings.webApiKey))
            {
                whatToDo = "FirebaseSettings にプロジェクトIDとウェブAPIキーを入れてください。";
                return false;
            }
            whatToDo = null;
            return true;
        }

        public static async Task<SyncResult> SyncAsync(HomeData home)
        {
            var sync = GetSync(out var whatToDo);
            return sync == null ? new SyncResult(SyncOutcome.Failed, home, whatToDo) : await sync.SyncAsync(home);
        }

        public static async Task<InviteResult> CreateInviteAsync(HomeData home)
        {
            var sync = GetSync(out var whatToDo);
            return sync == null ? InviteResult.Failed(whatToDo) : await sync.CreateInviteAsync(home);
        }

        public static async Task<SyncResult> JoinAsync(HomeData home, string code)
        {
            var sync = GetSync(out var whatToDo);
            return sync == null ? new SyncResult(SyncOutcome.Failed, home, whatToDo) : await sync.JoinAsync(home, code);
        }

        /// <summary>
        /// 同期の部品を返す。ログイン状態を使い回すため、設定と端末（エディターでの切り替え）が変わらない限り同じものを使う。
        /// </summary>
        static HomeSync GetSync(out string whatToDo)
        {
            if (!IsConfigured(out whatToDo))
            {
                return null;
            }
            var settings = FirebaseSettings.Load();
            var config = new FirebaseConfig { ProjectId = settings.projectId.Trim(), ApiKey = settings.webApiKey.Trim() };
            var key = config.ProjectId + "/" + config.ApiKey + "/" + DeviceSlot.Suffix;
            if (s_Sync == null || s_SyncFor != key)
            {
                s_Sync = new HomeSync(new FirestoreHomeStore(config, new UnityWebRequestTransport(), new PlayerPrefsTokenStore()));
                s_SyncFor = key;
            }
            return s_Sync;
        }
    }
}
