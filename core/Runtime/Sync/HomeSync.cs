using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HomeCare.Core.Data;

namespace HomeCare.Core.Sync
{
    public enum SyncOutcome
    {
        /// <summary>クラウドにまだ家が無かったので、この端末のデータで作った。</summary>
        Uploaded,

        /// <summary>この端末にデータが無かったので、クラウドの家を取得した。</summary>
        Downloaded,

        /// <summary>この端末とクラウドを合わせた。</summary>
        Synced,

        /// <summary>同期できなかった。理由は Message にある。この端末のデータは変えていない。</summary>
        Failed,
    }

    public class InviteResult
    {
        public bool Ok { get; }

        /// <summary>作った招待。失敗したときは null。</summary>
        public HomeInvite Invite { get; }
        public string Message { get; }

        public InviteResult(bool ok, HomeInvite invite, string message)
        {
            Ok = ok;
            Invite = invite;
            Message = message;
        }

        public static InviteResult Failed(string message) => new InviteResult(false, null, message);
    }

    public class SyncResult
    {
        public SyncOutcome Outcome { get; }

        /// <summary>同期後に端末に保存する家のデータ。失敗したときは元のまま。</summary>
        public HomeData Home { get; }
        public string Message { get; }

        public SyncResult(SyncOutcome outcome, HomeData home, string message)
        {
            Outcome = outcome;
            Home = home;
            Message = message;
        }
    }

    /// <summary>
    /// 端末の家のデータとクラウドを同期する手順。保存先の種類（Firebaseなど）には依存しない。
    /// 1. ログインする
    /// 2. クラウドの同じ家を取得する
    /// 3. 端末とクラウドを、データの受け渡しと同じ決まり（HomeImporter）で合わせる
    /// 4. クラウドより新しい分だけをクラウドに送る
    /// </summary>
    public class HomeSync
    {
        /// <summary>招待コードの有効期間。</summary>
        public static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(24);

        private readonly ICloudHomeStore _store;
        private readonly Func<DateTime> _utcNow;
        private string _savedProfile;

        /// <summary>
        /// この端末の呼び名（例：「パパのiPhone」）。同期や参加のときに、家のメンバー一覧へ書き込む。
        /// </summary>
        public string MyName { get; set; } = "";

        public HomeSync(ICloudHomeStore store, Func<DateTime> utcNow = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task<SyncResult> SyncAsync(HomeData original)
        {
            // Unityでは通信の続きを画面と同じ流れで動かす必要があるので、ConfigureAwait(false) は使わない
            try
            {
                // 途中で失敗しても端末のデータが中途半端に変わらないよう、写しを使う
                var local = new HomeEditor(RecordFields.FromFields<HomeData>(RecordFields.ToFields(original))).Home;
                var uid = await _store.SignInAsync();
                var remote = await _store.LoadAsync(local.id);

                if (remote == null && HomeImporter.IsEmpty(local))
                {
                    // 新しい端末（まだ何も登録していない）なら、自分がメンバーの家をクラウドから探す
                    var myHomeId = await _store.FindMyHomeIdAsync();
                    remote = myHomeId == null ? null : await _store.LoadAsync(myHomeId);
                    if (remote != null)
                    {
                        var adopted = HomeImporter.Import(local, remote);
                        if (adopted.Outcome == ImportOutcome.Rejected)
                        {
                            return new SyncResult(SyncOutcome.Failed, original, adopted.Message);
                        }
                        await SaveProfileAsync(adopted.Home.id, uid);
                        return new SyncResult(SyncOutcome.Downloaded, adopted.Home, $"クラウドから取得しました（{adopted.ChangedCount}件）。");
                    }
                }

                if (remote == null)
                {
                    var all = HomeChanges.Between(local, null);
                    await _store.SaveAsync(all);
                    await SaveProfileAsync(local.id, uid);
                    return new SyncResult(SyncOutcome.Uploaded, local, $"クラウドに家のデータを作りました（{all.Count}件）。");
                }

                var before = new HomeSnapshot(remote);
                var merged = HomeImporter.Import(local, remote);
                if (merged.Outcome == ImportOutcome.Rejected)
                {
                    return new SyncResult(SyncOutcome.Failed, original, merged.Message);
                }
                var changes = HomeChanges.Between(merged.Home, before);
                if (changes.Count > 0)
                {
                    await _store.SaveAsync(changes);
                }
                var message = merged.ChangedCount == 0 && changes.Count == 0
                    ? "クラウドと同じ内容です。"
                    : $"同期しました（受け取り{merged.ChangedCount}件・送信{changes.Count}件）。";
                await SaveProfileAsync(merged.Home.id, uid);
                return new SyncResult(SyncOutcome.Synced, merged.Home, message);
            }
            catch (NotHomeMemberException)
            {
                return new SyncResult(SyncOutcome.Failed, original,
                    "この家のメンバーから外れています。家族に招待コードをもらって参加し直すか、「この家から抜ける」で新しく始めてください。");
            }
            catch (Exception e)
            {
                return new SyncResult(SyncOutcome.Failed, original, $"同期できませんでした。{e.Message}");
            }
        }

        /// <summary>
        /// 家族を招待するコードを作る。先に同期して、家がクラウドにある状態にしておく必要がある。
        /// </summary>
        public async Task<InviteResult> CreateInviteAsync(HomeData home)
        {
            try
            {
                var uid = await _store.SignInAsync();
                if (await _store.LoadAsync(home.id) == null)
                {
                    return InviteResult.Failed("先に「クラウドと同期」で、家をクラウドに保存してください。");
                }
                var now = _utcNow();
                var invite = new HomeInvite
                {
                    code = InviteCode.Generate(),
                    homeId = home.id,
                    createdByUid = uid,
                    createdAt = DataFormat.FormatTimestamp(now),
                    expiresAtMillis = new DateTimeOffset(DateTime.SpecifyKind(now + InviteLifetime, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                };
                await _store.CreateInviteAsync(invite);
                return new InviteResult(true, invite, $"招待コード：{InviteCode.Format(invite.code)}");
            }
            catch (Exception e)
            {
                return InviteResult.Failed($"招待コードを作れませんでした。{e.Message}");
            }
        }

        /// <summary>
        /// 招待コードで家に参加し、その家のデータを取得する。
        /// この端末の家のデータは、参加した家のデータに置き換わる（呼ぶ前に利用者に確かめる）。
        /// </summary>
        public async Task<SyncResult> JoinAsync(HomeData original, string codeInput)
        {
            var code = InviteCode.Normalize(codeInput);
            if (code == null)
            {
                return new SyncResult(SyncOutcome.Failed, original, $"招待コードは{InviteCode.Length}文字の英数字です。");
            }
            try
            {
                var uid = await _store.SignInAsync();
                var invite = await _store.FindInviteAsync(code);
                if (invite == null)
                {
                    return new SyncResult(SyncOutcome.Failed, original, "招待コードが見つかりません。入力を確かめてください。");
                }
                if (_utcNow() >= invite.ExpiresAtUtc)
                {
                    return new SyncResult(SyncOutcome.Failed, original, "招待コードの期限が切れています。新しいコードを作ってもらってください。");
                }
                await _store.JoinHomeAsync(invite);
                var joined = await _store.LoadAsync(invite.homeId);
                if (joined == null)
                {
                    return new SyncResult(SyncOutcome.Failed, original, "招待された家が見つかりません。");
                }
                if (joined.schemaVersion > HomeData.CurrentSchemaVersion)
                {
                    return new SyncResult(SyncOutcome.Failed, original,
                        $"新しい版のアプリで作られた家です（版{joined.schemaVersion}）。アプリを更新してください。");
                }
                var home = new HomeEditor(joined).Home;
                await SaveProfileAsync(home.id, uid);
                var count = home.rooms.Count + home.points.Count + home.tasks.Count + home.completions.Count;
                return new SyncResult(SyncOutcome.Downloaded, home, $"家「{home.name}」に参加しました（{count}件）。");
            }
            catch (Exception e)
            {
                return new SyncResult(SyncOutcome.Failed, original, $"参加できませんでした。{e.Message}");
            }
        }

        /// <summary>家のメンバー一覧を取得する。持ち主が先頭。</summary>
        public async Task<MembersResult> LoadMembersAsync(HomeData home)
        {
            try
            {
                var uid = await _store.SignInAsync();
                var membership = await _store.LoadMembershipAsync(home.id);
                if (membership == null)
                {
                    return MembersResult.Failed("この家はまだクラウドにありません。先に「クラウドと同期」を押してください。");
                }
                var names = new Dictionary<string, string>();
                foreach (var profile in membership.Profiles)
                {
                    if (profile.uid != null)
                    {
                        names[profile.uid] = profile.name;
                    }
                }
                var members = membership.MemberUids
                    .OrderBy(member => member == membership.OwnerUid ? 0 : 1)
                    .Select(member => new MemberView(
                        member,
                        names.TryGetValue(member, out var name) && !string.IsNullOrEmpty(name) ? name : MemberName.Unnamed(member),
                        member == membership.OwnerUid,
                        member == uid))
                    .ToList();
                return new MembersResult(true, $"メンバーは{members.Count}人です。", members, membership.OwnerUid == uid);
            }
            catch (NotHomeMemberException)
            {
                return MembersResult.Failed("この家のメンバーから外れています。");
            }
            catch (Exception e)
            {
                return MembersResult.Failed($"メンバーを取得できませんでした。{e.Message}");
            }
        }

        /// <summary>
        /// この端末が家から抜ける。クラウドの家はそのまま残り、ほかのメンバーは使い続けられる。
        /// 成功したら、呼び出し側でこの端末の家のデータを消す。家を作った持ち主は抜けられない。
        /// </summary>
        public async Task<MemberChangeResult> LeaveAsync(HomeData home)
        {
            try
            {
                var uid = await _store.SignInAsync();
                HomeMembership membership;
                try
                {
                    membership = await _store.LoadMembershipAsync(home.id);
                }
                catch (NotHomeMemberException)
                {
                    return new MemberChangeResult(true, "この家のメンバーではなくなっていたので、この端末のデータだけを片付けました。");
                }
                if (membership == null)
                {
                    return new MemberChangeResult(true, "この家はクラウドに無かったので、この端末のデータだけを片付けました。");
                }
                if (membership.OwnerUid == uid)
                {
                    return new MemberChangeResult(false, "この家を作った端末は抜けられません（家族が使えなくならないように）。");
                }
                await _store.RemoveMemberAsync(home.id, uid);
                _savedProfile = null;
                return new MemberChangeResult(true, $"家「{home.name}」から抜けました。");
            }
            catch (Exception e)
            {
                return new MemberChangeResult(false, $"抜けられませんでした。{e.Message}");
            }
        }

        /// <summary>ほかのメンバーを家から外す。外せるのは家の持ち主だけ。</summary>
        public async Task<MemberChangeResult> RemoveMemberAsync(HomeData home, MemberView member)
        {
            try
            {
                var uid = await _store.SignInAsync();
                if (member.Uid == uid)
                {
                    return new MemberChangeResult(false, "自分は外せません。");
                }
                var membership = await _store.LoadMembershipAsync(home.id);
                if (membership == null || membership.OwnerUid != uid)
                {
                    return new MemberChangeResult(false, "メンバーを外せるのは、家を作った端末だけです。");
                }
                if (!membership.MemberUids.Contains(member.Uid))
                {
                    return new MemberChangeResult(true, $"「{member.Name}」はもうメンバーではありません。");
                }
                await _store.RemoveMemberAsync(home.id, member.Uid);
                return new MemberChangeResult(true, $"「{member.Name}」をメンバーから外しました。");
            }
            catch (NotHomeMemberException)
            {
                return new MemberChangeResult(false, "この家のメンバーから外れています。");
            }
            catch (Exception e)
            {
                return new MemberChangeResult(false, $"外せませんでした。{e.Message}");
            }
        }

        /// <summary>
        /// この端末の呼び名を家のメンバー一覧に書き込む。同じ内容を書いたばかりなら送らない。
        /// 呼び名が書けなくても同期そのものは成功なので、失敗は無視する（次の同期でまた試す）。
        /// </summary>
        async Task SaveProfileAsync(string homeId, string uid)
        {
            var name = MemberName.Normalize(MyName);
            var key = $"{homeId}/{uid}/{name}";
            if (_savedProfile == key)
            {
                return;
            }
            try
            {
                await _store.SaveProfileAsync(homeId, new HomeMember { uid = uid, name = name, updatedAt = DataFormat.FormatTimestamp(_utcNow()) });
                _savedProfile = key;
            }
            catch (Exception)
            {
            }
        }
    }
}
