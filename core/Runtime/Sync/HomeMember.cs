using System;
using System.Collections.Generic;

namespace HomeCare.Core.Sync
{
    /// <summary>
    /// 家のメンバー1人分の呼び名。クラウドでは homes/{家ID}/members/{利用者ID} に置く。
    /// 匿名ログインの利用者IDは人には読めないので、一覧に出すときの呼び名（例：「パパのiPhone」）を各端末が書き込む。
    /// </summary>
    public class HomeMember
    {
        public string uid;
        public string name;
        public string updatedAt;
    }

    /// <summary>クラウドの家の、メンバーに関する情報。</summary>
    public class HomeMembership
    {
        /// <summary>家を作った利用者。</summary>
        public string OwnerUid;

        /// <summary>家のメンバーの利用者ID（読み書きを許すかどうかは、これで決まる）。</summary>
        public List<string> MemberUids = new List<string>();

        /// <summary>呼び名。まだ書き込んでいないメンバーの分は無い。</summary>
        public List<HomeMember> Profiles = new List<HomeMember>();
    }

    /// <summary>一覧に出すメンバー1人分。</summary>
    public class MemberView
    {
        public string Uid { get; }
        public string Name { get; }
        public bool IsOwner { get; }
        public bool IsMe { get; }

        public MemberView(string uid, string name, bool isOwner, bool isMe)
        {
            Uid = uid;
            Name = name;
            IsOwner = isOwner;
            IsMe = isMe;
        }
    }

    public class MembersResult
    {
        public bool Ok { get; }
        public string Message { get; }

        /// <summary>持ち主が先頭。失敗したときは空。</summary>
        public IReadOnlyList<MemberView> Members { get; }

        /// <summary>この端末が家の持ち主か（持ち主だけがほかのメンバーを外せる）。</summary>
        public bool IAmOwner { get; }

        public MembersResult(bool ok, string message, IReadOnlyList<MemberView> members, bool iAmOwner)
        {
            Ok = ok;
            Message = message;
            Members = members;
            IAmOwner = iAmOwner;
        }

        public static MembersResult Failed(string message) => new MembersResult(false, message, Array.Empty<MemberView>(), false);
    }

    /// <summary>家から抜ける・メンバーを外す、の結果。</summary>
    public class MemberChangeResult
    {
        public bool Ok { get; }
        public string Message { get; }

        public MemberChangeResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }
    }

    /// <summary>
    /// その家のメンバーではない（外された・抜けた）ので、クラウドに断られたときの例外。
    /// 保存先（Firebaseなど）がこれに置き換えて投げると、同期の手順が分かりやすい説明を出せる。
    /// </summary>
    public class NotHomeMemberException : Exception
    {
        public NotHomeMemberException(Exception inner)
            : base("この家のメンバーではありません。", inner)
        {
        }
    }

    /// <summary>呼び名の決まり。</summary>
    public static class MemberName
    {
        public const int MaxLength = 20;

        /// <summary>前後の空白を除き、長すぎれば切る。</summary>
        public static string Normalize(string input)
        {
            var name = (input ?? "").Trim();
            return name.Length > MaxLength ? name.Substring(0, MaxLength) : name;
        }

        /// <summary>呼び名が無いメンバーの表示。利用者IDの頭4文字で見分けられるようにする。</summary>
        public static string Unnamed(string uid) =>
            $"（呼び名なし・{(uid ?? "").Substring(0, Math.Min(4, (uid ?? "").Length))}）";
    }
}
