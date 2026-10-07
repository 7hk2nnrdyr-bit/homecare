using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Simulation;

namespace HomeCare.EditorTools
{
    /// <summary>
    /// マーカーが認識されないときの確認用。メニューの HomeCare → マーカーの設定を確認 で、
    /// 関係する設定をまとめて Console に出す。
    /// </summary>
    public static class MarkerDiagnostics
    {
        const string k_PreferencesPath = "Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset";
        const string k_EnvironmentPath = "Assets/HomeCare/Simulation/HomeCareSimulationEnvironment.prefab";
        const string k_LibraryPath = "Assets/HomeCare/Markers/HomeCareMarkers.asset";
        const string k_TexturePath = "Assets/HomeCare/Markers/M01.png";

        [MenuItem("HomeCare/マーカーの設定を確認")]
        static void Run()
        {
            var log = new StringBuilder("[HomeCare] マーカーの設定\n");

            var preferences = AssetDatabase.LoadAssetAtPath<Object>(k_PreferencesPath);
            var environment = preferences != null
                ? new SerializedObject(preferences).FindProperty("m_EnvironmentPrefab")?.objectReferenceValue
                : null;
            log.AppendLine($"1. XR Simulationで使う部屋：{Describe(environment)}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_EnvironmentPath);
            var simulated = prefab != null ? prefab.GetComponentInChildren<SimulatedTrackedImage>(true) : null;
            if (simulated == null)
            {
                log.AppendLine($"2. 仮想マーカー：見つかりません（部屋={Describe(prefab)}）");
            }
            else
            {
                var t = simulated.transform;
                log.AppendLine($"2. 仮想マーカー：画像={Describe(simulated.texture)} 大きさ={simulated.size} 位置={t.position} 面の向き={t.up}");
            }

            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(k_LibraryPath);
            if (library == null)
            {
                log.AppendLine("3. マーカー画像の一覧：読み込めません");
            }
            else
            {
                log.AppendLine($"3. マーカー画像の一覧：{library.count}件");
                for (var i = 0; i < library.count; i++)
                {
                    var image = library[i];
                    log.AppendLine($"   - 名前={image.name} 画像={Describe(image.texture)} 画像のID={image.textureGuid:N} 大きさ={image.size}");
                }
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
            log.AppendLine($"4. M01.png：ID={AssetDatabase.AssetPathToGUID(k_TexturePath)} 形式={(texture != null ? texture.format.ToString() : "読み込めません")}");

            var manager = Object.FindAnyObjectByType<ARTrackedImageManager>();
            log.AppendLine($"5. シーンのAR Tracked Image Manager：{(manager != null ? $"あり（一覧={Describe(manager.referenceLibrary as Object)}）" : "なし")}");

            Debug.Log(log.ToString());
        }

        /// <summary>
        /// XR Simulationの仮想の部屋の壁に貼るマーカーを、次の番号（M01→M02→…→M10→M01）に替える。
        /// 部屋を増やしたときに、別の部屋のマーカーが映ったつもりで試すため。次に再生したときから変わる。
        /// </summary>
        [MenuItem("HomeCare/XR Simulationのマーカーを次の番号にする")]
        static void NextSimulationMarker() => SetSimulationMarker(next: true);

        [MenuItem("HomeCare/XR Simulationのマーカーを M01 に戻す")]
        static void ResetSimulationMarker() => SetSimulationMarker(next: false);

        static void SetSimulationMarker(bool next)
        {
            var root = PrefabUtility.LoadPrefabContents(k_EnvironmentPath);
            try
            {
                var simulated = root.GetComponentInChildren<SimulatedTrackedImage>(true);
                if (simulated == null)
                {
                    Debug.LogWarning("[HomeCare] 仮想マーカーが見つかりません。");
                    return;
                }
                var image = new SerializedObject(simulated);
                var texture = image.FindProperty("m_Image");
                var current = texture.objectReferenceValue != null ? texture.objectReferenceValue.name : "M01";
                var number = 1;
                if (next && current.StartsWith("M") && int.TryParse(current.Substring(1), out var n))
                {
                    number = n % 10 + 1;
                }
                var markerId = $"M{number:00}";
                texture.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/HomeCare/Markers/{markerId}.png");
                image.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, k_EnvironmentPath);
                Debug.Log($"[HomeCare] XR Simulationの壁のマーカーを {markerId} にしました。次に再生したときから使われます。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static string Describe(Object obj) =>
            obj == null ? "なし" : $"{obj.name}（{AssetDatabase.GetAssetPath(obj)}）";
    }
}
