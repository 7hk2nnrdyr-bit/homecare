using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace HomeCare.App
{
    /// <summary>
    /// アプリの画面（シーン）の切り替え。起動するとリスト画面が出て、
    /// 「カメラで見る」を押したときだけAR（カメラ）の画面を開く（設計書7.6）。
    /// リスト画面ではARの仕組みを止めて、カメラと電池を使わないようにする。
    /// AR画面を開くたびにARの仕組みを作り直すので、何度行き来しても前回の状態が残らない。
    /// </summary>
    public static class AppScenes
    {
        public const string List = "ListScene";
        public const string Camera = "SampleScene";

        public static void OpenCamera()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && !manager.isInitializationComplete)
            {
                manager.InitializeLoaderSync();
                if (manager.activeLoader == null)
                {
                    Debug.LogError("[HomeCare] ARを開始できませんでした。");
                }
            }
            SceneManager.LoadScene(Camera);
        }

        public static void OpenList()
        {
            // 先にARを止めてから画面を切り替える（AR画面の部品が消えたあとに、ARが古い部品を探さないように）
            StopAR();
            SceneManager.LoadScene(List);
        }

        /// <summary>ARの仕組みを止める。すでに止まっていれば何もしない。</summary>
        public static void StopAR()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }
    }
}
