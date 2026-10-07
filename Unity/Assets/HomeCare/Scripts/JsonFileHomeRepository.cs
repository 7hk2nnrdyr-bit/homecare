using System.IO;
using HomeCare.Core.Data;
using UnityEngine;

namespace HomeCare.App
{
    /// <summary>
    /// 家のデータを、端末内のJSONファイル1つに保存する。
    /// 保存先はアプリ専用のフォルダ（Application.persistentDataPath）で、アプリを消すまで残る。
    /// </summary>
    public class JsonFileHomeRepository : IHomeRepository
    {
        public string FilePath { get; }

        /// <param name="fileName">省略すると home.json（エディターで端末Bに切り替えているときは home-B.json）。</param>
        public JsonFileHomeRepository(string fileName = null)
        {
            FilePath = Path.Combine(Application.persistentDataPath, fileName ?? $"home{DeviceSlot.Suffix}.json");
        }

        public HomeData Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            var home = HomeJson.Parse(File.ReadAllText(FilePath));
            if (home.schemaVersion > HomeData.CurrentSchemaVersion)
            {
                Debug.LogWarning($"[HomeCare] 新しい版のデータです（版{home.schemaVersion}）。アプリを更新してください。");
            }
            return home;
        }

        public void Save(HomeData home)
        {
            // 書き込み中に電源が切れても元のファイルが壊れないよう、別名で書いてから置き換える
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, HomeJson.Write(home));
            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, null);
            }
            else
            {
                File.Move(temp, FilePath);
            }
            // 完了・修正・削除・同期のどれで保存しても、期限のお知らせを作り直す
            ReminderScheduler.Reschedule(home);
        }

        /// <summary>保存したデータを消す（動作確認用）。</summary>
        public void Delete()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
            ReminderScheduler.CancelAll();
        }
    }
}
