using System;
using System.Collections.Generic;

namespace HomeCare.Core.Firebase
{
    /// <summary>Firebaseから断られたときの例外。Message は利用者向けの日本語にする。</summary>
    public class FirebaseException : Exception
    {
        public int Status { get; }

        /// <summary>Firebaseが返した元のエラー（調べるとき用）。</summary>
        public string Detail { get; }

        public FirebaseException(int status, string message, string detail) : base(message)
        {
            Status = status;
            Detail = detail;
        }

        public override string ToString() => $"{Message}（{Status}）{Detail}";

        /// <summary>失敗した応答から、よくある原因を日本語で説明する。</summary>
        public static FirebaseException From(HttpResponseData response)
        {
            var detail = ErrorText(response.Body);
            string message;
            if (response.Status == 0)
            {
                message = "インターネットにつながっていないか、Firebaseに接続できません。";
            }
            else if (Contains(detail, "API_KEY_INVALID") || Contains(detail, "API key not valid"))
            {
                message = "ウェブAPIキーが正しくありません。Firebaseの設定を確認してください。";
            }
            else if (Contains(detail, "ADMIN_ONLY_OPERATION") || Contains(detail, "OPERATION_NOT_ALLOWED") || Contains(detail, "CONFIGURATION_NOT_FOUND"))
            {
                message = "Firebaseで匿名ログインが有効になっていません。";
            }
            else if (Contains(detail, "does not exist for project") || Contains(detail, "database (default) does not exist"))
            {
                message = "Firestoreのデータベースがまだ作られていません。";
            }
            else if (response.Status == 403 || Contains(detail, "PERMISSION_DENIED"))
            {
                message = "Firestoreに断られました。セキュリティルールを確認してください。";
            }
            else if (response.Status == 429 || Contains(detail, "RESOURCE_EXHAUSTED"))
            {
                message = "今日の無料枠を使い切ったか、通信が多すぎます。時間をおいてください。";
            }
            else
            {
                message = $"Firebaseでエラーが起きました（{response.Status}）。";
            }
            return new FirebaseException(response.Status, message, detail);
        }

        static bool Contains(string text, string word) =>
            text != null && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>{"error":{"message":"…"}} の形なら message を、そうでなければ中身をそのまま返す。</summary>
        static string ErrorText(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return "";
            }
            try
            {
                var parsed = MiniJson.Parse(body);
                if (parsed is List<object> list && list.Count > 0)
                {
                    parsed = list[0];
                }
                if (parsed is Dictionary<string, object> map
                    && map.TryGetValue("error", out var error) && error is Dictionary<string, object> errorMap
                    && errorMap.TryGetValue("message", out var text))
                {
                    var status = errorMap.TryGetValue("status", out var code) ? $" {code}" : "";
                    return $"{text}{status}";
                }
            }
            catch (FormatException)
            {
            }
            return body;
        }
    }
}
