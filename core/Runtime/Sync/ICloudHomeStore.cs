using System.Threading.Tasks;
using HomeCare.Core.Data;

namespace HomeCare.Core.Sync
{
    /// <summary>
    /// クラウドの保存場所の共通の形。同期の手順（HomeSync）はこの形だけを使い、Firebaseのことは知らない。
    /// 今は Firestore 版（FirestoreHomeStore）。保存先を変えるときは、この形の別の版を作ればよい。
    /// </summary>
    public interface ICloudHomeStore
    {
        /// <summary>ログインして、利用者のIDを返す。</summary>
        Task<string> SignInAsync();

        /// <summary>家のデータを丸ごと取得する。クラウドにまだ無ければ null。</summary>
        Task<HomeData> LoadAsync(string homeId);

        /// <summary>ログイン中の利用者がメンバーになっている家のIDを1つ返す。無ければ null。</summary>
        Task<string> FindMyHomeIdAsync();

        /// <summary>変わった分だけを保存する。</summary>
        Task SaveAsync(HomeChanges changes);
    }
}
