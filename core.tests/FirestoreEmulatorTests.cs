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

        private static FirestoreHomeStore NewDevice(ITokenStore tokens = null) =>
            new FirestoreHomeStore(FirebaseConfig.ForEmulator("demo-homecare", AuthHost, FirestoreHost),
                new HttpClientTransport(), tokens ?? new InMemoryTokenStore());

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
            var error = await Assert.ThrowsAsync<FirebaseException>(() => stranger.LoadAsync(home.id));

            Assert.Equal(403, error.Status);
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
    }
}
