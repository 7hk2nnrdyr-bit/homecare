using System;
using System.Security.Cryptography;
using System.Text;

namespace HomeCare.Core.Sync
{
    /// <summary>
    /// 家への招待。招待コードを知っている人は、期限までその家のメンバーに加われる。
    /// 1つのコードで何人でも参加できる（家族が何人いても1回の招待で済むように）。
    /// </summary>
    public class HomeInvite
    {
        /// <summary>招待コード（記号なしの8文字。例："ABCD2345"）。</summary>
        public string code;
        public string homeId;
        public string createdByUid;
        public string createdAt;

        /// <summary>期限（1970年1月1日からのミリ秒、UTC）。クラウドのルールでも期限を確かめるため数で持つ。</summary>
        public long expiresAtMillis;

        public DateTime ExpiresAtUtc => DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMillis).UtcDateTime;
    }

    /// <summary>招待コードの作り方と読み方。</summary>
    public static class InviteCode
    {
        public const int Length = 8;

        /// <summary>見間違えやすい文字（I・O・0・1）を除いた32文字。</summary>
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>推測されにくい、ランダムな8文字のコードを作る。</summary>
        public static string Generate()
        {
            var bytes = new byte[Length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            var code = new StringBuilder(Length);
            foreach (var b in bytes)
            {
                code.Append(Alphabet[b % Alphabet.Length]);
            }
            return code.ToString();
        }

        /// <summary>入力されたコードを整える（小文字・空白・ハイフンを許す）。8文字にならなければ null。</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }
            var code = new StringBuilder();
            foreach (var c in input.ToUpperInvariant())
            {
                if (char.IsWhiteSpace(c) || c == '-')
                {
                    continue;
                }
                if (Alphabet.IndexOf(c) < 0)
                {
                    return null;
                }
                code.Append(c);
            }
            return code.Length == Length ? code.ToString() : null;
        }

        /// <summary>読みやすく4文字ずつに区切る（例："ABCD-2345"）。</summary>
        public static string Format(string code) =>
            code != null && code.Length == Length ? code.Substring(0, 4) + "-" + code.Substring(4) : code;
    }
}
