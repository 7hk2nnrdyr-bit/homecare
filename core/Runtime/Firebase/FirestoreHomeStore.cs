using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HomeCare.Core.Data;
using HomeCare.Core.Sync;

namespace HomeCare.Core.Firebase
{
    /// <summary>
    /// 家のデータをFirestoreに保存する（ICloudHomeStore のFirestore版）。
    /// 置き場所は docs/data-format.md の「クラウドへの移し方」のとおり：
    ///   homes/{家ID}                        … id, name, schemaVersion, ownerUid, memberUids
    ///   homes/{家ID}/rooms/{部屋ID}          … 部屋（基準点 localizers を含む）
    ///   homes/{家ID}/points/{ポイントID}
    ///   homes/{家ID}/tasks/{タスクID}
    ///   homes/{家ID}/completions/{記録ID}
    /// 中身の項目名は端末内のJSONと同じ。memberUids（家のメンバーの利用者ID）が、家族共有の入り口になる。
    /// </summary>
    public class FirestoreHomeStore : ICloudHomeStore
    {
        public const string Homes = "homes";
        public const string Rooms = "rooms";
        public const string Points = "points";
        public const string Tasks = "tasks";
        public const string Completions = "completions";
        public const string Invites = "invites";

        private readonly FirebaseAuthClient _auth;
        private readonly FirestoreClient _db;

        public FirestoreHomeStore(FirebaseConfig config, IHttpTransport http, ITokenStore tokens)
        {
            _auth = new FirebaseAuthClient(config, http, tokens);
            _db = new FirestoreClient(config, http, _auth.GetIdTokenAsync);
        }

        public string Uid => _auth.Uid;

        public Task<string> SignInAsync() => _auth.SignInAsync();

        public async Task<HomeData> LoadAsync(string homeId)
        {
            var homeDoc = await _db.GetAsync($"{Homes}/{homeId}");
            if (homeDoc == null)
            {
                return null;
            }
            var home = new HomeData
            {
                schemaVersion = homeDoc.Fields.TryGetValue("schemaVersion", out var version) ? System.Convert.ToInt32(version) : 1,
                id = homeId,
                name = homeDoc.Fields.TryGetValue("name", out var name) ? name as string : null,
                rooms = await LoadListAsync<RoomData>(homeId, Rooms),
                points = await LoadListAsync<PointData>(homeId, Points),
                tasks = await LoadListAsync<TaskData>(homeId, Tasks),
                completions = await LoadListAsync<CompletionData>(homeId, Completions),
            };
            return home;
        }

        public async Task<string> FindMyHomeIdAsync()
        {
            var uid = await _auth.SignInAsync();
            var homes = await _db.FindArrayContainsAsync(Homes, "memberUids", uid, 1);
            return homes.Count == 0 ? null : homes[0].Id;
        }

        public async Task SaveAsync(HomeChanges changes)
        {
            var uid = await _auth.SignInAsync();
            var homeId = changes.Home.id;
            var writes = new List<FirestoreWrite>();

            // 家そのもの。作るときだけ持ち主とメンバーを入れ、後から書き換えない（家族共有で別に扱う）
            var homeFields = new Dictionary<string, object>
            {
                ["id"] = homeId,
                ["name"] = changes.Home.name,
                ["schemaVersion"] = (long)changes.Home.schemaVersion,
            };
            if (changes.IsNewHome)
            {
                homeFields["ownerUid"] = uid;
                homeFields["memberUids"] = new List<object> { uid };
                writes.Add(new FirestoreWrite { Document = Doc($"{Homes}/{homeId}", homeFields), MustNotExist = true });
            }

            writes.AddRange(changes.Rooms.Select(r => Write(homeId, Rooms, r.id, r)));
            writes.AddRange(changes.Points.Select(p => Write(homeId, Points, p.id, p)));
            writes.AddRange(changes.Tasks.Select(t => Write(homeId, Tasks, t.id, t)));
            writes.AddRange(changes.Completions.Select(c => Write(homeId, Completions, c.id, c)));
            if (writes.Count > 0)
            {
                await _db.CommitAsync(writes);
            }
        }

        public async Task CreateInviteAsync(HomeInvite invite)
        {
            await _auth.SignInAsync();
            await _db.CommitAsync(new[]
            {
                new FirestoreWrite { Document = Doc($"{Invites}/{invite.code}", RecordFields.ToFields(invite)), MustNotExist = true },
            });
        }

        public async Task<HomeInvite> FindInviteAsync(string code)
        {
            var doc = await _db.GetAsync($"{Invites}/{code}");
            return doc == null ? null : RecordFields.FromFields<HomeInvite>(doc.Fields);
        }

        /// <summary>
        /// 家のメンバー一覧に自分を足す。どの招待で参加したかを joinCode に残し、ルールで招待が本物か確かめる。
        /// 家のほかの項目には触れない。
        /// </summary>
        public async Task JoinHomeAsync(HomeInvite invite)
        {
            var uid = await _auth.SignInAsync();
            await _db.CommitAsync(new[]
            {
                new FirestoreWrite
                {
                    Document = Doc($"{Homes}/{invite.homeId}", new Dictionary<string, object> { ["joinCode"] = invite.code }),
                    OnlyFields = new List<string> { "joinCode" },
                    MustExist = true,
                    AppendToArrayField = "memberUids",
                    AppendToArrayValues = new List<object> { uid },
                },
            });
        }

        async Task<List<T>> LoadListAsync<T>(string homeId, string collection) where T : new() =>
            (await _db.ListAsync($"{Homes}/{homeId}/{collection}"))
                .Select(doc => RecordFields.FromFields<T>(doc.Fields))
                .ToList();

        static FirestoreWrite Write(string homeId, string collection, string id, object record) =>
            new FirestoreWrite { Document = Doc($"{Homes}/{homeId}/{collection}/{id}", RecordFields.ToFields(record)) };

        static FirestoreDocument Doc(string path, Dictionary<string, object> fields) =>
            new FirestoreDocument { Path = path, Fields = fields };
    }
}
