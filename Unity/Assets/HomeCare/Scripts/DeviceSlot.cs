using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// Unityエディターで、Mac1台のまま「2台の端末」を試すための切り替え。
    /// 端末Bに切り替えると、家のデータのファイルとログイン（利用者）が端末Aとは別になる。
    /// 実機のアプリでは使わず、いつも端末A（区別なし）になる。
    /// </summary>
    public static class DeviceSlot
    {
#if UNITY_EDITOR
        const string Key = "HomeCare.EditorDevice";

        /// <summary>今の端末の名前（"A" か "B"）。</summary>
        public static string Current => PlayerPrefs.GetString(Key, "A") == "B" ? "B" : "A";

        /// <summary>ファイル名や保存の名前に付ける印。端末Aは何も付けない（今までのデータをそのまま使う）。</summary>
        public static string Suffix => Current == "A" ? "" : "-" + Current;

        public static void Toggle()
        {
            PlayerPrefs.SetString(Key, Current == "A" ? "B" : "A");
            PlayerPrefs.Save();
        }
#else
        public static string Current => "A";
        public static string Suffix => "";
#endif
    }
}
