using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HomeCare.Core.Data;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;
using HomeCare.Core.Sync;
using Xunit;

namespace HomeCare.Core.Tests
{
    /// <summary>メモリ上のクラウド。同期の手順だけを、Firebaseなしで確かめる。</summary>
    internal class FakeCloudHomeStore : ICloudHomeStore
    {
        private readonly Dictionary<string, HomeData> _homes = new Dictionary<string, HomeData>();
        private readonly Dictionary<string, List<string>> _members = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, HomeInvite> _invites = new Dictionary<string, HomeInvite>();
        private readonly Dictionary<string, string> _owners = new Dictionary<string, string>();
        private readonly Dictionary<string, Dictionary<string, HomeMember>> _profiles = new Dictionary<string, Dictionary<string, HomeMember>>();

        public string CurrentUid = "user-a";
        public bool Offline;
        public List<HomeChanges> Saved { get; } = new List<HomeChanges>();

        public Task<string> SignInAsync()
        {
            if (Offline)
            {
                throw new InvalidOperationException("通信できません。");
            }
            return Task.FromResult(CurrentUid);
        }

        public Task<HomeData> LoadAsync(string homeId)
        {
            if (!_homes.TryGetValue(homeId, out var home))
            {
                return Task.FromResult<HomeData>(null);
            }
            EnsureMember(homeId);
            return Task.FromResult(Copy(home));
        }

        void EnsureMember(string homeId)
        {
            if (!_members[homeId].Contains(CurrentUid))
            {
                throw new NotHomeMemberException(null);
            }
        }

        public Task<HomeMembership> LoadMembershipAsync(string homeId)
        {
            if (!_homes.ContainsKey(homeId))
            {
                return Task.FromResult<HomeMembership>(null);
            }
            EnsureMember(homeId);
            return Task.FromResult(new HomeMembership
            {
                OwnerUid = _owners[homeId],
                MemberUids = _members[homeId].ToList(),
                Profiles = _profiles.TryGetValue(homeId, out var profiles) ? profiles.Values.ToList() : new List<HomeMember>(),
            });
        }

        public Task SaveProfileAsync(string homeId, HomeMember profile)
        {
            EnsureMember(homeId);
            if (!_profiles.ContainsKey(homeId))
            {
                _profiles[homeId] = new Dictionary<string, HomeMember>();
            }
            _profiles[homeId][profile.uid] = profile;
            return Task.CompletedTask;
        }

        public Task RemoveMemberAsync(string homeId, string uid)
        {
            // クラウドのルールと同じ：自分が抜けるか、持ち主がほかの人を外すかだけ
            var leaving = uid == CurrentUid && uid != _owners[homeId];
            var removingByOwner = CurrentUid == _owners[homeId] && uid != CurrentUid;
            if (!leaving && !removingByOwner)
            {
                throw new InvalidOperationException("断られました。");
            }
            _members[homeId].Remove(uid);
            if (_profiles.TryGetValue(homeId, out var profiles))
            {
                profiles.Remove(uid);
            }
            return Task.CompletedTask;
        }

        public Task<string> FindMyHomeIdAsync() =>
            Task.FromResult(_members.Where(pair => pair.Value.Contains(CurrentUid)).Select(pair => pair.Key).FirstOrDefault());

        public IReadOnlyList<string> MembersOf(string homeId) => _members[homeId];

        public Task CreateInviteAsync(HomeInvite invite)
        {
            if (!_members[invite.homeId].Contains(CurrentUid))
            {
                throw new InvalidOperationException("メンバーではありません。");
            }
            _invites[invite.code] = invite;
            return Task.CompletedTask;
        }

        public Task<HomeInvite> FindInviteAsync(string code) =>
            Task.FromResult(_invites.TryGetValue(code, out var invite) ? invite : null);

        public Task JoinHomeAsync(HomeInvite invite)
        {
            if (!_members[invite.homeId].Contains(CurrentUid))
            {
                _members[invite.homeId].Add(CurrentUid);
            }
            return Task.CompletedTask;
        }

        public Task SaveAsync(HomeChanges changes)
        {
            Saved.Add(changes);
            var id = changes.Home.id;
            if (changes.IsNewHome)
            {
                _homes[id] = new HomeData { id = id, name = changes.Home.name };
                _members[id] = new List<string> { CurrentUid };
                _owners[id] = CurrentUid;
            }
            EnsureMember(id);
            var stored = _homes[id];
            Upsert(stored.rooms, changes.Rooms, r => r.id);
            Upsert(stored.points, changes.Points, p => p.id);
            Upsert(stored.tasks, changes.Tasks, t => t.id);
            Upsert(stored.completions, changes.Completions, c => c.id);
            return Task.CompletedTask;
        }

        static void Upsert<T>(List<T> stored, List<T> changed, Func<T, string> idOf) where T : new()
        {
            foreach (var item in changed)
            {
                stored.RemoveAll(x => idOf(x) == idOf(item));
                stored.Add(RecordFields.FromFields<T>(RecordFields.ToFields(item)));
            }
        }

        static HomeData Copy(HomeData home) => RecordFields.FromFields<HomeData>(RecordFields.ToFields(home));
    }

    public class HomeSyncTests
    {
        private static DateTime At(int hour) => new DateTime(2026, 10, 6, hour, 0, 0, DateTimeKind.Utc);

        private static HomeEditor HomeWithOnePoint(out TaskData task)
        {
            var editor = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家", () => At(1));
            var room = editor.AddRoom("リビング");
            editor.AddMarkerLocalizer(room.id, "M01", Vec3.Zero, 0f);
            var point = editor.AddPoint(room.id, "エアコン", new Vec3(1f, 2f, 3f), Quat.AngleAxis(90f, Vec3.Up));
            task = editor.AddTask(point.id, "フィルター掃除", new Recurrence(1, RecurrenceUnit.Month), new DateTime(2026, 11, 1));
            return editor;
        }

        private static HomeData EmptyHome() => HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家").Home;

        [Fact]
        public async Task 最初の同期ではクラウドに家を作り全部を送る()
        {
            var cloud = new FakeCloudHomeStore();
            var home = HomeWithOnePoint(out _).Home;

            var result = await new HomeSync(cloud).SyncAsync(home);

            Assert.Equal(SyncOutcome.Uploaded, result.Outcome);
            var saved = cloud.Saved.Single();
            Assert.True(saved.IsNewHome);
            Assert.Equal(4, saved.Count); // 家・部屋・ポイント・タスク
        }

        [Fact]
        public async Task 変わっていなければ何も送らない()
        {
            var cloud = new FakeCloudHomeStore();
            var home = HomeWithOnePoint(out _).Home;
            var sync = new HomeSync(cloud);
            await sync.SyncAsync(home);

            var result = await sync.SyncAsync(home);

            Assert.Equal(SyncOutcome.Synced, result.Outcome);
            Assert.Single(cloud.Saved);
            Assert.Contains("同じ内容", result.Message);
        }

        [Fact]
        public async Task 完了したら変わったタスクと実施記録だけを送る()
        {
            var cloud = new FakeCloudHomeStore();
            var editor = HomeWithOnePoint(out var task);
            var sync = new HomeSync(cloud);
            await sync.SyncAsync(editor.Home);

            new HomeEditor(editor.Home, () => At(2)).CompleteTask(task.id, new DateTime(2026, 10, 6));
            var result = await sync.SyncAsync(editor.Home);

            var sent = cloud.Saved.Last();
            Assert.Equal(2, sent.Count);
            Assert.Single(sent.Tasks);
            Assert.Single(sent.Completions);
            Assert.Contains("送信2件", result.Message);
        }

        [Fact]
        public async Task 何も無い端末は自分の家をクラウドから取得する()
        {
            var cloud = new FakeCloudHomeStore();
            var original = HomeWithOnePoint(out _).Home;
            await new HomeSync(cloud).SyncAsync(original);

            // アプリを入れ直した端末（同じ利用者で、家のデータは空）
            var result = await new HomeSync(cloud).SyncAsync(EmptyHome());

            Assert.Equal(SyncOutcome.Downloaded, result.Outcome);
            Assert.Equal(original.id, result.Home.id);
            Assert.Equal(new[] { 1f, 2f, 3f }, result.Home.points.Single().positionInRoom);
            Assert.Equal(original.points.Single().rotationInRoom, result.Home.points.Single().rotationInRoom);
            Assert.Equal("M01", result.Home.rooms.Single().localizers.Single().markerId);
        }

        [Fact]
        public async Task 二台で別々に変えた内容が両方に届く()
        {
            var cloud = new FakeCloudHomeStore();
            var deviceA = HomeWithOnePoint(out var task);
            await new HomeSync(cloud).SyncAsync(deviceA.Home);
            var deviceB = new HomeEditor((await new HomeSync(cloud).SyncAsync(EmptyHome())).Home, () => At(3));

            // Aで完了し、Bで場所を足す
            new HomeEditor(deviceA.Home, () => At(2)).CompleteTask(task.id, new DateTime(2026, 10, 6));
            deviceB.AddPoint(deviceB.Home.rooms.Single().id, "換気扇", Vec3.Zero);
            var afterA = (await new HomeSync(cloud).SyncAsync(deviceA.Home)).Home;
            var afterB = (await new HomeSync(cloud).SyncAsync(deviceB.Home)).Home;
            afterA = (await new HomeSync(cloud).SyncAsync(afterA)).Home;

            foreach (var home in new[] { afterA, afterB })
            {
                Assert.Equal(2, home.points.Count);
                Assert.Single(home.completions);
                Assert.Equal("2026-10-06", home.tasks.Single().lastDoneDate);
            }
        }

        [Fact]
        public async Task 通信できなければ端末のデータは変えない()
        {
            var cloud = new FakeCloudHomeStore { Offline = true };
            var home = HomeWithOnePoint(out _).Home;

            var result = await new HomeSync(cloud).SyncAsync(home);

            Assert.Equal(SyncOutcome.Failed, result.Outcome);
            Assert.Same(home, result.Home);
            Assert.Contains("通信できません", result.Message);
        }
    
        [Fact]
        public async Task 招待コードで別の利用者が同じ家に参加できる()
        {
            var cloud = new FakeCloudHomeStore();
            var home = HomeWithOnePoint(out _).Home;
            var sync = new HomeSync(cloud, () => At(1));
            await sync.SyncAsync(home);
            var invite = await sync.CreateInviteAsync(home);

            cloud.CurrentUid = "user-b";
            var joined = await sync.JoinAsync(EmptyHome(), invite.Invite.code.ToLowerInvariant().Insert(4, "-"));

            Assert.True(invite.Ok);
            Assert.Equal(SyncOutcome.Downloaded, joined.Outcome);
            Assert.Equal(home.id, joined.Home.id);
            Assert.Equal(home.points.Single().positionInRoom, joined.Home.points.Single().positionInRoom);
            Assert.Equal(new[] { "user-a", "user-b" }, cloud.MembersOf(home.id));
        }

        [Fact]
        public async Task 期限切れの招待コードでは参加できない()
        {
            var cloud = new FakeCloudHomeStore();
            var home = HomeWithOnePoint(out _).Home;
            await new HomeSync(cloud, () => At(1)).SyncAsync(home);
            var invite = await new HomeSync(cloud, () => At(1)).CreateInviteAsync(home);

            cloud.CurrentUid = "user-b";
            var result = await new HomeSync(cloud, () => At(1).AddHours(25)).JoinAsync(EmptyHome(), invite.Invite.code);

            Assert.Equal(SyncOutcome.Failed, result.Outcome);
            Assert.Contains("期限", result.Message);
            Assert.Single(cloud.MembersOf(home.id));
        }

        [Fact]
        public async Task 知らないコードや形の違うコードでは参加できない()
        {
            var cloud = new FakeCloudHomeStore();
            var sync = new HomeSync(cloud);

            var unknown = await sync.JoinAsync(EmptyHome(), "ABCD-EFGH");
            var malformed = await sync.JoinAsync(EmptyHome(), "ABC");

            Assert.Contains("見つかりません", unknown.Message);
            Assert.Contains("8文字", malformed.Message);
        }

        [Fact]
        public async Task クラウドに無い家の招待コードは作れない()
        {
            var result = await new HomeSync(new FakeCloudHomeStore()).CreateInviteAsync(HomeWithOnePoint(out _).Home);

            Assert.False(result.Ok);
            Assert.Contains("先に", result.Message);
        }

        [Fact]
        public void 招待コードは見間違えやすい文字を使わない()
        {
            for (var i = 0; i < 200; i++)
            {
                var code = InviteCode.Generate();
                Assert.Equal(8, code.Length);
                Assert.DoesNotContain(code, c => "IO01".IndexOf(c) >= 0);
                Assert.Equal(code, InviteCode.Normalize(InviteCode.Format(code).ToLowerInvariant()));
            }
        }

        private static async Task<(FakeCloudHomeStore Cloud, HomeData Home, HomeSync Owner, HomeSync Family)> SharedHomeAsync()
        {
            var cloud = new FakeCloudHomeStore();
            var home = HomeWithOnePoint(out _).Home;
            var owner = new HomeSync(cloud, () => At(1)) { MyName = "パパのiPhone" };
            await owner.SyncAsync(home);
            var invite = await owner.CreateInviteAsync(home);
            cloud.CurrentUid = "user-b";
            var family = new HomeSync(cloud, () => At(1)) { MyName = "  ママのGalaxy  " };
            await family.JoinAsync(EmptyHome(), invite.Invite.code);
            return (cloud, home, owner, family);
        }

        [Fact]
        public async Task メンバー一覧に呼び名と持ち主が出る()
        {
            var (cloud, home, _, family) = await SharedHomeAsync();

            var result = await family.LoadMembersAsync(home);

            Assert.True(result.Ok, result.Message);
            Assert.False(result.IAmOwner);
            Assert.Equal(new[] { "パパのiPhone", "ママのGalaxy" }, result.Members.Select(m => m.Name));
            Assert.True(result.Members[0].IsOwner);
            Assert.True(result.Members[1].IsMe);
        }

        [Fact]
        public async Task 呼び名を変えて同期すると一覧も変わり呼び名が無ければIDの頭で見分ける()
        {
            var (cloud, home, _, family) = await SharedHomeAsync();
            cloud.CurrentUid = "user-a";
            var ownerWithoutName = new HomeSync(cloud);
            await ownerWithoutName.SyncAsync(home);

            cloud.CurrentUid = "user-b";
            family.MyName = "ママ";
            await family.SyncAsync(home);
            var result = await family.LoadMembersAsync(home);

            Assert.Equal(new[] { "（呼び名なし・user）", "ママ" }, result.Members.Select(m => m.Name));
        }

        [Fact]
        public async Task 家族は家から抜けられ抜けた後は同期できない()
        {
            var (cloud, home, _, family) = await SharedHomeAsync();

            var left = await family.LeaveAsync(home);
            var sync = await family.SyncAsync(home);

            Assert.True(left.Ok, left.Message);
            Assert.Equal(new[] { "user-a" }, cloud.MembersOf(home.id));
            Assert.Equal(SyncOutcome.Failed, sync.Outcome);
            Assert.Contains("メンバーから外れています", sync.Message);
        }

        [Fact]
        public async Task 持ち主は抜けられない()
        {
            var (cloud, home, owner, _) = await SharedHomeAsync();
            cloud.CurrentUid = "user-a";

            var result = await owner.LeaveAsync(home);

            Assert.False(result.Ok);
            Assert.Equal(2, cloud.MembersOf(home.id).Count);
        }

        [Fact]
        public async Task 持ち主だけがほかのメンバーを外せる()
        {
            var (cloud, home, owner, family) = await SharedHomeAsync();
            var membersSeenByFamily = (await family.LoadMembersAsync(home)).Members;

            var byFamily = await family.RemoveMemberAsync(home, membersSeenByFamily[0]);
            cloud.CurrentUid = "user-a";
            var byOwner = await owner.RemoveMemberAsync(home, membersSeenByFamily[1]);
            var after = await owner.LoadMembersAsync(home);

            Assert.False(byFamily.Ok);
            Assert.True(byOwner.Ok, byOwner.Message);
            Assert.Equal(new[] { "パパのiPhone" }, after.Members.Select(m => m.Name));
        }

        [Fact]
        public async Task 外された後に抜けると端末のデータだけ片付ける()
        {
            var (cloud, home, owner, family) = await SharedHomeAsync();
            cloud.CurrentUid = "user-a";
            await owner.RemoveMemberAsync(home, (await owner.LoadMembersAsync(home)).Members[1]);

            cloud.CurrentUid = "user-b";
            var result = await family.LeaveAsync(home);

            Assert.True(result.Ok);
            Assert.Contains("片付け", result.Message);
        }
}
}
