namespace HomeCare.App
{
    /// <summary>
    /// アプリの画面（シーン）の名前。起動するとリスト画面が出て、
    /// 「カメラで見る」を押したときだけAR（カメラ）の画面を開く（設計書7.6）。
    /// </summary>
    public static class AppScenes
    {
        public const string List = "ListScene";
        public const string Camera = "SampleScene";
    }
}
