using System;
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
        private readonly ICloudHomeStore _store;

        public HomeSync(ICloudHomeStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<SyncResult> SyncAsync(HomeData original)
        {
            // Unityでは通信の続きを画面と同じ流れで動かす必要があるので、ConfigureAwait(false) は使わない
            try
            {
                // 途中で失敗しても端末のデータが中途半端に変わらないよう、写しを使う
                var local = new HomeEditor(RecordFields.FromFields<HomeData>(RecordFields.ToFields(original))).Home;
                await _store.SignInAsync();
                var remote = await _store.LoadAsync(local.id);

                if (remote == null && HomeImporter.IsEmpty(local))
                {
                    // 新しい端末（まだ何も登録していない）なら、自分がメンバーの家をクラウドから探す
                    var myHomeId = await _store.FindMyHomeIdAsync();
                    remote = myHomeId == null ? null : await _store.LoadAsync(myHomeId);
                    if (remote != null)
                    {
                        var adopted = HomeImporter.Import(local, remote);
                        return adopted.Outcome == ImportOutcome.Rejected
                            ? new SyncResult(SyncOutcome.Failed, original, adopted.Message)
                            : new SyncResult(SyncOutcome.Downloaded, adopted.Home, $"クラウドから取得しました（{adopted.ChangedCount}件）。");
                    }
                }

                if (remote == null)
                {
                    var all = HomeChanges.Between(local, null);
                    await _store.SaveAsync(all);
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
                return new SyncResult(SyncOutcome.Synced, merged.Home, message);
            }
            catch (Exception e)
            {
                return new SyncResult(SyncOutcome.Failed, original, $"同期できませんでした。{e.Message}");
            }
        }
    }
}
