namespace HomeCare.Core.Firebase
{
    /// <summary>
    /// 接続先のFirebaseプロジェクト。値はFirebaseコンソールの「プロジェクトの設定」で確認できる。
    /// ウェブAPIキーは「どのプロジェクトか」を示すもので、合い言葉ではない。
    /// データを守るのは、ログインとセキュリティルール（firebase/firestore.rules）。
    /// </summary>
    public class FirebaseConfig
    {
        public string ProjectId;
        public string ApiKey;

        public string AuthBaseUrl = "https://identitytoolkit.googleapis.com";
        public string TokenBaseUrl = "https://securetoken.googleapis.com";
        public string FirestoreBaseUrl = "https://firestore.googleapis.com";

        public bool IsComplete => !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>パソコンの中で動かすFirebaseエミュレーターにつなぐ（テスト用、料金はかからない）。</summary>
        public static FirebaseConfig ForEmulator(string projectId, string authHost, string firestoreHost) =>
            new FirebaseConfig
            {
                ProjectId = projectId,
                ApiKey = "emulator",
                AuthBaseUrl = $"http://{authHost}/identitytoolkit.googleapis.com",
                TokenBaseUrl = $"http://{authHost}/securetoken.googleapis.com",
                FirestoreBaseUrl = $"http://{firestoreHost}",
            };
    }
}
