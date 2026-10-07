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

        /// <summary>家のデータを丸ごと取得する。クラウドにまだ無ければ null。メンバーでなければ NotHomeMemberException。</summary>
        Task<HomeData> LoadAsync(string homeId);

        /// <summary>ログイン中の利用者がメンバーになっている家のIDを1つ返す。無ければ null。</summary>
        Task<string> FindMyHomeIdAsync();

        /// <summary>変わった分だけを保存する。</summary>
        Task SaveAsync(HomeChanges changes);

        /// <summary>招待を保存する。招待する人は、その家のメンバーでなければならない。</summary>
        Task CreateInviteAsync(HomeInvite invite);

        /// <summary>招待コードから招待を探す。無ければ null。</summary>
        Task<HomeInvite> FindInviteAsync(string code);

        /// <summary>招待を使って、ログイン中の利用者を家のメンバーに加える。</summary>
        Task JoinHomeAsync(HomeInvite invite);

        /// <summary>
        /// 家のメンバーの情報（持ち主・メンバー一覧・呼び名）を取得する。家が無ければ null。
        /// メンバーでなければ NotHomeMemberException を投げる。
        /// </summary>
        Task<HomeMembership> LoadMembershipAsync(string homeId);

        /// <summary>ログイン中の利用者の呼び名を保存する。</summary>
        Task SaveProfileAsync(string homeId, HomeMember profile);

        /// <summary>
        /// メンバーを家から外す（呼び名も消す）。自分なら「抜ける」、持ち主ならほかの人を外せる。
        /// </summary>
        Task RemoveMemberAsync(string homeId, string uid);
    }
}
