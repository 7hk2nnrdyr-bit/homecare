using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HomeCare.Core.Data;
using HomeCare.Core.Firebase;
using HomeCare.Core.Spatial;
using HomeCare.Core.Sync;
using Xunit;

namespace HomeCare.Core.Tests
{
    /// <summary>テスト用の通信（.NET の HttpClient）。Unityでは UnityWebRequest 版を使う。</summary>
    internal class HttpClientTransport : IHttpTransport
    {
        private static readonly HttpClient Client = new HttpClient();

        public async Task<HttpResponseData> SendAsync(HttpRequestData request)
        {
            var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
            if (request.Body != null)
            {
                message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType);
            }
            if (request.BearerToken != null)
            {
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.BearerToken);
            }
            try
            {
                var response = await Client.SendAsync(message);
                return new HttpResponseData { Status = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync() };
            }
            catch (HttpRequestException e)
            {
                return new HttpResponseData { Status = 0, Body = e.Message };
            }
        }
    }

    /// <summary>
    /// 本物と同じ動きをするFirebaseエミュレーター（パソコンの中で動く。料金はかからない）に対して、
    /// 保存・取得・セキュリティルールを確かめる。
    /// エミュレーターを起動していないとき（環境変数が無いとき）は、何もせずに終わる。
    /// 起動のしかたは firebase/README.md。
    /// </summary>
    public class FirestoreEmulatorTests
    {
        private static readonly string FirestoreHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        private static readonly string AuthHost = Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST");
        private static bool EmulatorRunning => !string.IsNullOrEmpty(FirestoreHost) && !string.IsNullOrEmpty(AuthHost);

        private static FirebaseConfig Config => FirebaseConfig.ForEmulator("demo-homecare", AuthHost, FirestoreHost);

        private static FirestoreHomeStore NewDevice(ITokenStore tokens = null) =>
            new FirestoreHomeStore(Config, new HttpClientTransport(), tokens ?? new InMemoryTokenStore());

        /// <summary>アプリを通さず、Firestoreに直接書き込む（ルールが不正な書き込みを断るか確かめる）。</summary>
        private static async Task<(FirestoreClient Db, string Uid)> RawClientAsync(ITokenStore tokens = null)
        {
            var auth = new FirebaseAuthClient(Config, new HttpClientTransport(), tokens ?? new InMemoryTokenStore());
            var uid = await auth.SignInAsync();
            return (new FirestoreClient(Config, new HttpClientTransport(), auth.GetIdTokenAsync), uid);
        }

        private static async Task<(HomeData Home, InviteResult Invite, ITokenStore OwnerTokens)> SharedHomeAsync()
        {
            var ownerTokens = new InMemoryTokenStore();
            var home = RecordFieldsTests.FullHome();
            var owner = new HomeSync(NewDevice(ownerTokens));
            await owner.SyncAsync(home);
            return (home, await owner.CreateInviteAsync(home), ownerTokens);
        }

        private static HomeData EmptyHome() => HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家").Home;

        [Fact]
        public async Task 保存した家をそのまま取得できる()
        {
            if (!EmulatorRunning) return;
            var home = RecordFieldsTests.FullHome();
            var device = NewDevice();

            var result = await new HomeSync(device).SyncAsync(home);
            var loaded = await device.LoadAsync(home.id);

            Assert.Equal(SyncOutcome.Uploaded, result.Outcome);
            Assert.True(RecordFields.AreEqual(RecordFields.ToFields(home), RecordFields.ToFields(loaded)));
        }

        [Fact]
        public async Task 同じ利用者なら空の端末に家を取得できる()
        {
            if (!EmulatorRunning) return;
            var tokens = new InMemoryTokenStore();
            var home = RecordFieldsTests.FullHome();
            await new HomeSync(NewDevice(tokens)).SyncAsync(home);

            // アプリを入れ直した端末（ログインは残っていて、家のデータは空）
            var empty = HomeEditor.LoadOrCreate(new InMemoryHomeRepository(), "わが家").Home;
            var result = await new HomeSync(NewDevice(tokens)).SyncAsync(empty);

            Assert.Equal(SyncOutcome.Downloaded, result.Outcome);
            Assert.Equal(home.id, result.Home.id);
            Assert.Equal(home.points.Single().rotationInRoom, result.Home.points.Single().rotationInRoom);
        }

        [Fact]
        public async Task 変わった分だけを送り二回目は何も送らない()
        {
            if (!EmulatorRunning) return;
            var home = RecordFieldsTests.FullHome();
            var sync = new HomeSync(NewDevice());
            await sync.SyncAsync(home);

            var again = await sync.SyncAsync(home);
            new HomeEditor(home).AddPoint(home.rooms.Single().id, "換気扇", new Vec3(0.1f, 2.4f, 0f));
            var added = await sync.SyncAsync(home);

            Assert.Contains("同じ内容", again.Message);
            Assert.Contains("送信1件", added.Message);
        }

        [Fact]
        public async Task メンバーでない利用者は家を読めない()
        {
            if (!EmulatorRunning) return;
            var home = RecordFieldsTests.FullHome();
            await new HomeSync(NewDevice()).SyncAsync(home);

            var stranger = NewDevice();
            await stranger.SignInAsync();
            await Assert.ThrowsAsync<NotHomeMemberException>(() => stranger.LoadAsync(home.id));

            Assert.Null(await stranger.FindMyHomeIdAsync());
        }

        [Fact]
        public async Task メンバーでない利用者は家に書き込めない()
        {
            if (!EmulatorRunning) return;
            var home = RecordFieldsTests.FullHome();
            await new HomeSync(NewDevice()).SyncAsync(home);

            // 他人が同じ家IDで「新しい家」として書き込もうとしても断られる
            var result = await new HomeSync(NewDevice()).SyncAsync(home);

            Assert.Equal(SyncOutcome.Failed, result.Outcome);
        }

        [Fact]
        public async Task たくさんの場所も一度に保存できる()
        {
            if (!EmulatorRunning) return;
            var home = RecordFieldsTests.FullHome();
            var editor = new HomeEditor(home);
            for (var i = 0; i < 120; i++)
            {
                editor.AddPoint(home.rooms.Single().id, $"場所{i}", new Vec3(i * 0.1f, 1f, 0f));
            }
            var device = NewDevice();

            var result = await new HomeSync(device).SyncAsync(home);

            Assert.Equal(SyncOutcome.Uploaded, result.Outcome);
            Assert.Equal(121, (await device.LoadAsync(home.id)).points.Count);
        }

        [Fact]
        public async Task ログインは端末に残り次回も同じ利用者になる()
        {
            if (!EmulatorRunning) return;
            var tokens = new InMemoryTokenStore();
            var first = await NewDevice(tokens).SignInAsync();

            var second = await NewDevice(tokens).SignInAsync();

            Assert.Equal(first, second);
            Assert.NotEqual(first, await NewDevice().SignInAsync());
        }
    
        [Fact]
        public async Task 招待コードで参加した家族と変更を共有できる()
        {
            if (!EmulatorRunning) return;
            var (home, invite, ownerTokens) = await SharedHomeAsync();

            // 家族の端末（別の利用者）がコードで参加し、タスクを完了して同期する
            var family = new HomeSync(NewDevice());
            var joined = await family.JoinAsync(EmptyHome(), invite.Invite.code);
            var familyHome = joined.Home;
            new HomeEditor(familyHome).CompleteTask(familyHome.tasks.Single().id, new DateTime(2026, 10, 20));
            var familySync = await family.SyncAsync(familyHome);

            // 持ち主の端末で同期すると、家族の完了が届く
            var ownerSync = await new HomeSync(NewDevice(ownerTokens)).SyncAsync(home);

            Assert.True(invite.Ok, invite.Message);
            Assert.Equal(SyncOutcome.Downloaded, joined.Outcome);
            Assert.Equal(home.points.Single().rotationInRoom, familyHome.points.Single().rotationInRoom);
            Assert.Contains("送信2件", familySync.Message);
            Assert.Equal("2026-10-20", ownerSync.Home.tasks.Single().lastDoneDate);
        }

        [Fact]
        public async Task 招待コードなしでは家に参加できない()
        {
            if (!EmulatorRunning) return;
            var (home, _, _) = await SharedHomeAsync();

            // 存在しない招待をでっちあげて参加しようとする
            var fake = new HomeInvite { code = "ZZZZZZZZ", homeId = home.id, expiresAtMillis = long.MaxValue };
            var error = await Assert.ThrowsAsync<FirebaseException>(() => NewDevice().JoinHomeAsync(fake));

            Assert.Equal(403, error.Status);
        }

        [Fact]
        public async Task 別の家の招待コードでは参加できない()
        {
            if (!EmulatorRunning) return;
            var (homeA, _, _) = await SharedHomeAsync();
            var (_, inviteB, _) = await SharedHomeAsync();

            var misuse = new HomeInvite { code = inviteB.Invite.code, homeId = homeA.id };
            var error = await Assert.ThrowsAsync<FirebaseException>(() => NewDevice().JoinHomeAsync(misuse));

            Assert.Equal(403, error.Status);
        }

        [Fact]
        public async Task 参加した人もほかの人をメンバーに足せない()
        {
            if (!EmulatorRunning) return;
            var (home, invite, _) = await SharedHomeAsync();
            var familyTokens = new InMemoryTokenStore();
            await new HomeSync(NewDevice(familyTokens)).JoinAsync(EmptyHome(), invite.Invite.code);

            var (db, _) = await RawClientAsync(familyTokens);
            var error = await Assert.ThrowsAsync<FirebaseException>(() => db.CommitAsync(new[]
            {
                new FirestoreWrite
                {
                    Document = new FirestoreDocument { Path = $"homes/{home.id}", Fields = new System.Collections.Generic.Dictionary<string, object>() },
                    OnlyFields = new System.Collections.Generic.List<string>(),
                    MustExist = true,
                    AppendToArrayField = "memberUids",
                    AppendToArrayValues = new System.Collections.Generic.List<object> { "someone-else" },
                },
            }));

            Assert.Equal(403, error.Status);
        }

        [Fact]
        public async Task メンバーでない人は招待コードを作れず期限の長すぎる招待も作れない()
        {
            if (!EmulatorRunning) return;
            var (home, _, ownerTokens) = await SharedHomeAsync();

            var strangerInvite = await new HomeSync(NewDevice()).CreateInviteAsync(home);
            var (db, uid) = await RawClientAsync(ownerTokens);
            var tooLong = new HomeInvite
            {
                code = InviteCode.Generate(), homeId = home.id, createdByUid = uid, createdAt = "",
                expiresAtMillis = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds(),
            };
            var error = await Assert.ThrowsAsync<FirebaseException>(() => db.CommitAsync(new[]
            {
                new FirestoreWrite { Document = new FirestoreDocument { Path = $"invites/{tooLong.code}", Fields = RecordFields.ToFields(tooLong) } },
            }));

            Assert.False(strangerInvite.Ok);
            Assert.Equal(403, error.Status);
        }

        [Fact]
        public async Task 呼び名がメンバー一覧に出て家族は抜けられる()
        {
            if (!EmulatorRunning) return;
            var (home, invite, ownerTokens) = await SharedHomeAsync();
            var owner = new HomeSync(NewDevice(ownerTokens)) { MyName = "パパ" };
            await owner.SyncAsync(home);
            var family = new HomeSync(NewDevice()) { MyName = "ママ" };
            await family.JoinAsync(EmptyHome(), invite.Invite.code);

            var members = await family.LoadMembersAsync(home);
            var left = await family.LeaveAsync(home);
            var afterLeave = await owner.LoadMembersAsync(home);
            var familySync = await family.SyncAsync(home);

            Assert.Equal(new[] { "パパ", "ママ" }, members.Members.Select(m => m.Name));
            Assert.True(left.Ok, left.Message);
            Assert.Equal(new[] { "パパ" }, afterLeave.Members.Select(m => m.Name));
            Assert.Contains("メンバーから外れています", familySync.Message);
        }

        [Fact]
        public async Task 持ち主はほかのメンバーを外せるが自分は抜けられない()
        {
            if (!EmulatorRunning) return;
            var (home, invite, ownerTokens) = await SharedHomeAsync();
            var owner = new HomeSync(NewDevice(ownerTokens));
            var family = new HomeSync(NewDevice()) { MyName = "ママ" };
            await family.JoinAsync(EmptyHome(), invite.Invite.code);

            var ownerLeave = await owner.LeaveAsync(home);
            var removed = await owner.RemoveMemberAsync(home, (await owner.LoadMembersAsync(home)).Members[1]);
            var after = await owner.LoadMembersAsync(home);

            Assert.False(ownerLeave.Ok);
            Assert.True(removed.Ok, removed.Message);
            Assert.Single(after.Members);
        }

        [Fact]
        public async Task 家族はほかのメンバーを外せず持ち主を消すこともできない()
        {
            if (!EmulatorRunning) return;
            var (home, invite, ownerTokens) = await SharedHomeAsync();
            var familyA = new InMemoryTokenStore();
            var familyB = new InMemoryTokenStore();
            var a = new HomeSync(NewDevice(familyA));
            await a.JoinAsync(EmptyHome(), invite.Invite.code);
            await new HomeSync(NewDevice(familyB)).JoinAsync(EmptyHome(), invite.Invite.code);
            var members = (await a.LoadMembersAsync(home)).Members;

            // アプリを通さず、直接ほかの人を消そうとする
            var store = NewDevice(familyA);
            var removeOwner = await Assert.ThrowsAsync<FirebaseException>(() => store.RemoveMemberAsync(home.id, members[0].Uid));
            var removeOther = await Assert.ThrowsAsync<FirebaseException>(() => store.RemoveMemberAsync(home.id, members[2].Uid));

            Assert.Equal(403, removeOwner.Status);
            Assert.Equal(403, removeOther.Status);
            Assert.Equal(3, (await a.LoadMembersAsync(home)).Members.Count);
        }

        [Fact]
        public async Task ほかの人の呼び名は書き換えられない()
        {
            if (!EmulatorRunning) return;
            var (home, invite, ownerTokens) = await SharedHomeAsync();
            var familyTokens = new InMemoryTokenStore();
            await new HomeSync(NewDevice(familyTokens)).JoinAsync(EmptyHome(), invite.Invite.code);
            var ownerUid = await NewDevice(ownerTokens).SignInAsync();

            var error = await Assert.ThrowsAsync<FirebaseException>(() =>
                NewDevice(familyTokens).SaveProfileAsync(home.id, new HomeMember { uid = ownerUid, name = "なりすまし", updatedAt = "" }));

            Assert.Equal(403, error.Status);
        }
}
}
