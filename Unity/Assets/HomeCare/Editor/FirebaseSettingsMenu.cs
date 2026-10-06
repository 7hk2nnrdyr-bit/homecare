using HomeCare.App;
using UnityEditor;
using UnityEngine;

namespace HomeCare.EditorTools
{
    /// <summary>メニュー「HomeCare → Firebaseの設定を作る」。設定ファイルを作り（あれば開き）、インスペクターに表示する。</summary>
    public static class FirebaseSettingsMenu
    {
        const string Folder = "Assets/HomeCare/Resources";
        const string AssetPath = Folder + "/" + FirebaseSettings.ResourceName + ".asset";

        [MenuItem("HomeCare/Firebaseの設定を作る")]
        static void CreateOrSelect()
        {
            var settings = AssetDatabase.LoadAssetAtPath<FirebaseSettings>(AssetPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    AssetDatabase.CreateFolder("Assets/HomeCare", "Resources");
                }
                settings = ScriptableObject.CreateInstance<FirebaseSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[HomeCare] {AssetPath} を作りました。インスペクターでプロジェクトIDとウェブAPIキーを入れてください。");
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
