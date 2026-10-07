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

        public Task<HomeData> LoadAsync(string homeId) =>
            Task.FromResult(_homes.TryGetValue(homeId, out var home) ? Copy(home) : null);

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
            }
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
}
}
