using System;
using HomeCare.Core.Data;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 家のデータとJSONの文字列を相互に変換する。端末内の保存、端末間の受け渡しの両方で同じ形を使う。
    /// 形の説明は docs/data-format.md。
    /// </summary>
    public static class HomeJson
    {
        public static string Write(HomeData home) => JsonUtility.ToJson(home, true);

        /// <summary>JSONとして読めなければ null。</summary>
        public static HomeData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<HomeData>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
