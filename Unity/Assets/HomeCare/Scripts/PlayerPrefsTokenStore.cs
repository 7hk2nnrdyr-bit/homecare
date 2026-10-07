using HomeCare.Core.Firebase;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>ログイン状態（更新用トークン）を端末に残す。アプリを消すまで、同じ利用者でログインし続ける。</summary>
    public class PlayerPrefsTokenStore : ITokenStore
    {
        const string Key = "HomeCare.FirebaseRefreshToken";

        public string LoadRefreshToken() => PlayerPrefs.GetString(Key, "");

        public void SaveRefreshToken(string refreshToken)
        {
            PlayerPrefs.SetString(Key, refreshToken ?? "");
            PlayerPrefs.Save();
        }
    }
}
