using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeCare.Core.Data
{
    public enum ImportOutcome
    {
        /// <summary>この端末にまだデータが無かったので、読み込んだデータをそのまま使う。</summary>
        Adopted,

        /// <summary>同じ家のデータだったので、新しい方を残して合わせた。</summary>
        Merged,

        /// <summary>読み込まなかった。理由は Message にある。</summary>
        Rejected,
    }

    public class ImportResult
    {
        public ImportOutcome Outcome { get; }

        /// <summary>読み込み後に保存する家のデータ。断ったときは元のまま。</summary>
        public HomeData Home { get; }
        public string Message { get; }

        /// <summary>取り込んだ件数（足したものと、新しい方に置き換えたもの）。</summary>
        public int ChangedCount { get; }

        public ImportResult(ImportOutcome outcome, HomeData home, string message, int changedCount = 0)
        {
            Outcome = outcome;
            Home = home;
            Message = message;
            ChangedCount = changedCount;
        }
    }

    /// <summary>
    /// ほかの端末から受け取った家のデータを、この端末のデータに合わせる。
    /// 合わせ方はクラウド同期（第3段階）でも同じにする：
    /// ・IDで同じものを探す（IDは端末で作るUUIDなので、別の端末で作ったものが重なることはない）
    /// ・同じIDなら updatedAt が新しい方を残す（削除も deletedAt と updatedAt で伝わる）
    /// ・実施記録は書き換えないので、両方を足し合わせる
    /// </summary>
    public static class HomeImporter
    {
        public static ImportResult Import(HomeData local, HomeData incoming)
        {
            if (incoming == null || string.IsNullOrEmpty(incoming.id))
            {
                return new ImportResult(ImportOutcome.Rejected, local, "家のデータとして読めませんでした。");
            }
            if (incoming.schemaVersion > HomeData.CurrentSchemaVersion)
            {
                return new ImportResult(ImportOutcome.Rejected, local,
                    $"新しい版のアプリで作られたデータです（版{incoming.schemaVersion}）。アプリを更新してください。");
            }
            // 足りない一覧を補い、古い形式を今の形にそろえる
            incoming = new HomeEditor(incoming).Home;

            if (local == null || IsEmpty(local))
            {
                return new ImportResult(ImportOutcome.Adopted, incoming, $"読み込みました（{Summary(incoming)}）。",
                    incoming.rooms.Count + incoming.points.Count + incoming.tasks.Count + incoming.completions.Count);
            }
            if (local.id != incoming.id)
            {
                return new ImportResult(ImportOutcome.Rejected, local,
                    "別の家のデータです。この端末のデータを消してから読み込んでください。");
            }

            var changed = 0;
            changed += MergeById(local.rooms, incoming.rooms, r => r.id, r => r.updatedAt, MergeRoom);
            changed += MergeById(local.points, incoming.points, p => p.id, p => p.updatedAt);
            changed += MergeById(local.tasks, incoming.tasks, t => t.id, t => t.updatedAt);
            changed += MergeById(local.completions, incoming.completions, c => c.id, c => c.createdAt);
            RecalculateLastDone(local);

            return new ImportResult(ImportOutcome.Merged, local,
                changed == 0 ? "新しい内容はありませんでした。" : $"{changed}件を取り込みました（{Summary(local)}）。", changed);
        }

        /// <summary>場所もやることも無い（起動しただけの）状態。</summary>
        public static bool IsEmpty(HomeData home) =>
            (home.points == null || home.points.Count == 0) && (home.tasks == null || home.tasks.Count == 0);

        static string Summary(HomeData home) =>
            $"部屋{Active(home.rooms, r => r.deletedAt)}・場所{Active(home.points, p => p.deletedAt)}・やること{Active(home.tasks, t => t.deletedAt)}";

        static int Active<T>(List<T> items, Func<T, string> deletedAt) =>
            items.Count(item => string.IsNullOrEmpty(deletedAt(item)));

        /// <summary>同じIDのものは更新時刻が新しい方を残し、無いものは足す。変わった件数を返す。</summary>
        static int MergeById<T>(List<T> local, List<T> incoming, Func<T, string> idOf, Func<T, string> updatedAtOf,
            Func<T, T, T> merge = null)
        {
            var changed = 0;
            foreach (var item in incoming)
            {
                var index = local.FindIndex(x => idOf(x) == idOf(item));
                if (index < 0)
                {
                    local.Add(item);
                    changed++;
                }
                else if (string.CompareOrdinal(updatedAtOf(item) ?? "", updatedAtOf(local[index]) ?? "") > 0)
                {
                    local[index] = merge != null ? merge(local[index], item) : item;
                    changed++;
                }
                else if (merge != null)
                {
                    local[index] = merge(item, local[index]);
                }
            }
            return changed;
        }

        /// <summary>部屋は新しい方を残しつつ、基準点はどちらの端末で足したものも残す。</summary>
        static RoomData MergeRoom(RoomData older, RoomData newer)
        {
            foreach (var localizer in older.localizers)
            {
                if (!newer.localizers.Any(l => l.id == localizer.id))
                {
                    newer.localizers.Add(localizer);
                }
            }
            return newer;
        }

        /// <summary>実施記録を足し合わせたので、前回実施日を記録から求め直す（新しい方の日付を残す）。</summary>
        static void RecalculateLastDone(HomeData home)
        {
            foreach (var task in home.tasks)
            {
                foreach (var completion in home.completions.Where(c => c.taskId == task.id))
                {
                    if (string.CompareOrdinal(completion.doneDate, task.lastDoneDate ?? "") > 0)
                    {
                        task.lastDoneDate = completion.doneDate;
                    }
                }
            }
        }
    }
}
