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
        private readonly Dictionary<string, string> _owners = new Dictionary<string, string>();

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
            Task.FromResult(_owners.Where(pair => pair.Value == CurrentUid).Select(pair => pair.Key).FirstOrDefault());

        public Task SaveAsync(HomeChanges changes)
        {
            Saved.Add(changes);
            var id = changes.Home.id;
            if (changes.IsNewHome)
            {
                _homes[id] = new HomeData { id = id, name = changes.Home.name };
                _owners[id] = CurrentUid;
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
    }
}
