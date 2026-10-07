using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 接続するFirebaseプロジェクトの設定。メニュー「HomeCare → Firebaseの設定を作る」で
    /// Assets/HomeCare/Resources/FirebaseSettings.asset を作り、インスペクターで値を入れる。
    /// このファイルは自分のプロジェクト専用なので、Gitには入れない（.gitignore 済み）。
    /// </summary>
    public class FirebaseSettings : ScriptableObject
    {
        public const string ResourceName = "FirebaseSettings";

        [Tooltip("Firebaseコンソール →（歯車）プロジェクトの設定 →「プロジェクトID」")]
        public string projectId;

        [Tooltip("Firebaseコンソール →（歯車）プロジェクトの設定 →「ウェブAPIキー」")]
        public string webApiKey;

        public static FirebaseSettings Load() => Resources.Load<FirebaseSettings>(ResourceName);
    }
}
