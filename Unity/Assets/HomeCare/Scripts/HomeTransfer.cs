using System;
using System.IO;
using HomeCare.Core.Data;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 家のデータを端末間で受け渡す（第3段階でクラウド同期に置き換えるまでの仕組み）。
    /// ・書き出し：アプリのフォルダの homecare-export.json に書き、同じ内容をクリップボードにも入れる
    /// ・読み込み：アプリのフォルダの homecare-import.json か、クリップボードの内容を読む
    /// 読み込んだデータの合わせ方は HomeImporter（コア）が決める。
    /// </summary>
    public static class HomeTransfer
    {
        public static string ExportPath => Path.Combine(Application.persistentDataPath, "homecare-export.json");
        public static string ImportPath => Path.Combine(Application.persistentDataPath, "homecare-import.json");

        /// <summary>書き出して、結果の説明を返す。</summary>
        public static string Export(HomeData home)
        {
            var json = HomeJson.Write(home);
            File.WriteAllText(ExportPath, json);
            GUIUtility.systemCopyBuffer = json;
            return $"書き出しました。クリップボードにも入れました。\nファイル：{ExportPath}";
        }

        public static ImportResult ImportFromFile(HomeData local)
        {
            if (!File.Exists(ImportPath))
            {
                return new ImportResult(ImportOutcome.Rejected, local,
                    $"読み込むファイルがありません。次の場所に置いてください。\n{ImportPath}");
            }
            return Import(local, File.ReadAllText(ImportPath));
        }

        public static ImportResult ImportFromClipboard(HomeData local) =>
            Import(local, GUIUtility.systemCopyBuffer);

        static ImportResult Import(HomeData local, string json)
        {
            try
            {
                return HomeImporter.Import(local, HomeJson.Parse(json));
            }
            catch (Exception e)
            {
                return new ImportResult(ImportOutcome.Rejected, local, $"読み込めませんでした：{e.Message}");
            }
        }
    }
}
