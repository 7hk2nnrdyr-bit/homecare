using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace HomeCare.Core.Firebase
{
    /// <summary>ログイン状態を端末に残す場所。Unityでは PlayerPrefs に保存する。</summary>
    public interface ITokenStore
    {
        /// <summary>前回のログインの更新用トークン。無ければ null か空。</summary>
        string LoadRefreshToken();

        void SaveRefreshToken(string refreshToken);
    }

    public class InMemoryTokenStore : ITokenStore
    {
        private string _token;

        public string LoadRefreshToken() => _token;

        public void SaveRefreshToken(string refreshToken) => _token = refreshToken;
    }

    /// <summary>
    /// Firebase Authentication へのログイン（REST API）。
    /// 今は匿名ログインだけ。端末ごとに利用者IDが作られ、アプリを消すまで同じIDを使い続ける。
    /// 後でメールやGoogle・Appleのログインを足すときは、この匿名の利用者に紐づけられるので、データは引き継げる。
    /// </summary>
    public class FirebaseAuthClient
    {
        private readonly FirebaseConfig _config;
        private readonly IHttpTransport _http;
        private readonly ITokenStore _tokens;
        private readonly Func<DateTime> _utcNow;
        private string _idToken;
        private DateTime _idTokenExpiresAt;

        /// <summary>ログイン中の利用者ID。ログイン前は null。</summary>
        public string Uid { get; private set; }

        public FirebaseAuthClient(FirebaseConfig config, IHttpTransport http, ITokenStore tokens, Func<DateTime> utcNow = null)
        {
            _config = config;
            _http = http;
            _tokens = tokens;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>ログインして利用者IDを返す。前回のログインが残っていれば、同じ利用者で続ける。</summary>
        public async Task<string> SignInAsync()
        {
            await GetIdTokenAsync();
            return Uid;
        }

        /// <summary>Firestoreに渡すIDトークン。期限（1時間）が近ければ取り直す。</summary>
        public async Task<string> GetIdTokenAsync()
        {
            if (_idToken != null && _utcNow() < _idTokenExpiresAt)
            {
                return _idToken;
            }
            var refreshToken = _tokens.LoadRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var refreshed = await RefreshAsync(refreshToken);
                if (refreshed)
                {
                    return _idToken;
                }
            }
            await SignUpAnonymouslyAsync();
            return _idToken;
        }

        async Task SignUpAnonymouslyAsync()
        {
            var response = await _http.SendAsync(new HttpRequestData
            {
                Method = "POST",
                Url = $"{_config.AuthBaseUrl}/v1/accounts:signUp?key={Uri.EscapeDataString(_config.ApiKey)}",
                Body = MiniJson.Write(new Dictionary<string, object> { ["returnSecureToken"] = true }),
            });
            if (!response.IsSuccess)
            {
                throw FirebaseException.From(response);
            }
            var body = (Dictionary<string, object>)MiniJson.Parse(response.Body);
            Remember((string)body["idToken"], (string)body["refreshToken"], (string)body["localId"], body["expiresIn"]);
        }

        /// <summary>前回のログインを更新する。利用者が消された・無効になったなど、使えなければ false。</summary>
        async Task<bool> RefreshAsync(string refreshToken)
        {
            var response = await _http.SendAsync(new HttpRequestData
            {
                Method = "POST",
                Url = $"{_config.TokenBaseUrl}/v1/token?key={Uri.EscapeDataString(_config.ApiKey)}",
                Body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken),
                ContentType = "application/x-www-form-urlencoded",
            });
            if (response.Status == 400)
            {
                // 更新用トークンが無効になっている。新しく匿名ログインし直す
                return false;
            }
            if (!response.IsSuccess)
            {
                throw FirebaseException.From(response);
            }
            var body = (Dictionary<string, object>)MiniJson.Parse(response.Body);
            Remember((string)body["id_token"], (string)body["refresh_token"], (string)body["user_id"], body["expires_in"]);
            return true;
        }

        void Remember(string idToken, string refreshToken, string uid, object expiresInSeconds)
        {
            _idToken = idToken;
            Uid = uid;
            var seconds = Convert.ToDouble(expiresInSeconds, CultureInfo.InvariantCulture);
            // 期限の少し前に取り直す
            _idTokenExpiresAt = _utcNow().AddSeconds(Math.Max(0, seconds - 300));
            _tokens.SaveRefreshToken(refreshToken);
        }
    }
}
