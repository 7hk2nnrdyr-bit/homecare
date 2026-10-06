using System.Collections.Generic;
using System.Linq;
using HomeCare.Core.Data;

namespace HomeCare.Core.Sync
{
    /// <summary>
    /// クラウドに送る分。クラウドに無いものと、クラウドと中身が違うものだけを入れる。
    /// 1件ずつ保存するので、毎回すべてを送るより書き込み回数（無料枠）を節約できる。
    /// </summary>
    public class HomeChanges
    {
        public HomeData Home { get; }

        /// <summary>家そのものをクラウドに新しく作るか。</summary>
        public bool IsNewHome { get; }

        public List<RoomData> Rooms { get; } = new List<RoomData>();
        public List<PointData> Points { get; } = new List<PointData>();
        public List<TaskData> Tasks { get; } = new List<TaskData>();
        public List<CompletionData> Completions { get; } = new List<CompletionData>();

        public int Count => (IsNewHome ? 1 : 0) + Rooms.Count + Points.Count + Tasks.Count + Completions.Count;

        HomeChanges(HomeData home, bool isNewHome)
        {
            Home = home;
            IsNewHome = isNewHome;
        }

        /// <summary>
        /// current のうち、クラウドの内容（remote）と違うものを集める。
        /// remote が null なら、家ごと新しく作るので全部を送る。
        /// </summary>
        public static HomeChanges Between(HomeData current, HomeSnapshot remote)
        {
            var changes = new HomeChanges(current, remote == null);
            changes.Rooms.AddRange(Changed(current.rooms, r => r.id, remote?.Rooms));
            changes.Points.AddRange(Changed(current.points, p => p.id, remote?.Points));
            changes.Tasks.AddRange(Changed(current.tasks, t => t.id, remote?.Tasks));
            changes.Completions.AddRange(Changed(current.completions, c => c.id, remote?.Completions));
            return changes;
        }

        static IEnumerable<T> Changed<T>(List<T> items, System.Func<T, string> idOf,
            Dictionary<string, Dictionary<string, object>> remote) =>
            items.Where(item => remote == null
                || !remote.TryGetValue(idOf(item), out var before)
                || !RecordFields.AreEqual(RecordFields.ToFields(item), before));
    }

    /// <summary>
    /// クラウドから取得した時点の中身の控え。
    /// 合わせる処理（HomeImporter）が中身を書き換えても、送る分を正しく判断できるように先に取っておく。
    /// </summary>
    public class HomeSnapshot
    {
        public Dictionary<string, Dictionary<string, object>> Rooms { get; }
        public Dictionary<string, Dictionary<string, object>> Points { get; }
        public Dictionary<string, Dictionary<string, object>> Tasks { get; }
        public Dictionary<string, Dictionary<string, object>> Completions { get; }

        public HomeSnapshot(HomeData home)
        {
            Rooms = home.rooms.ToDictionary(r => r.id, r => RecordFields.ToFields(r));
            Points = home.points.ToDictionary(p => p.id, p => RecordFields.ToFields(p));
            Tasks = home.tasks.ToDictionary(t => t.id, t => RecordFields.ToFields(t));
            Completions = home.completions.ToDictionary(c => c.id, c => RecordFields.ToFields(c));
        }
    }
}
