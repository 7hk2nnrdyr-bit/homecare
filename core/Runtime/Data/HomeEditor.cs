using System;
using System.Collections.Generic;
using System.Linq;
using HomeCare.Core.Scheduling;
using HomeCare.Core.Spatial;

namespace HomeCare.Core.Data
{
    /// <summary>
    /// 家のデータを読み書きする窓口。部屋・ポイント・タスクの追加と、状態（赤・黄・緑）の判定をまとめる。
    /// IDは端末で作る（オフラインでも登録でき、後からクラウドへそのまま送れるため）。
    /// </summary>
    public class HomeEditor
    {
        private readonly Func<DateTime> _utcNow;

        public HomeData Home { get; }

        public HomeEditor(HomeData home, Func<DateTime> utcNow = null)
        {
            Home = home ?? throw new ArgumentNullException(nameof(home));
            // 古い版のファイルには無い一覧もあるので、空の一覧で補う
            Home.rooms = Home.rooms ?? new List<RoomData>();
            Home.points = Home.points ?? new List<PointData>();
            Home.tasks = Home.tasks ?? new List<TaskData>();
            Home.completions = Home.completions ?? new List<CompletionData>();
            foreach (var room in Home.rooms)
            {
                room.localizers = room.localizers ?? new List<LocalizerData>();
            }
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>保存場所から読む。何も保存されていなければ、空の家を作る。</summary>
        public static HomeEditor LoadOrCreate(IHomeRepository repository, string homeName, Func<DateTime> utcNow = null)
        {
            var home = repository.Load() ?? new HomeData { id = NewId(), name = homeName };
            return new HomeEditor(home, utcNow);
        }

        public RoomData AddRoom(string name)
        {
            var now = Now();
            var room = new RoomData
            {
                id = NewId(),
                name = name,
                sortOrder = Home.rooms.Count,
                createdAt = now,
                updatedAt = now,
            };
            Home.rooms.Add(room);
            return room;
        }

        /// <summary>名前の一致する部屋を返す。無ければ作る。</summary>
        public RoomData FindOrAddRoom(string name) =>
            ActiveRooms().FirstOrDefault(r => r.name == name) ?? AddRoom(name);

        /// <summary>
        /// 部屋にマーカーの基準点を追加する。最初のマーカーは原点（位置0・向き0）に置く。
        /// 同じマーカーを2つの部屋で使うと、どちらの部屋か区別できないので断る。
        /// </summary>
        public LocalizerData AddMarkerLocalizer(string roomId, string markerId, Vec3 positionInRoom, float yawDeg)
        {
            var room = RequireRoom(roomId);
            var usedBy = ActiveRooms().FirstOrDefault(r => r.localizers.Any(l => l.markerId == markerId));
            if (usedBy != null)
            {
                throw new ArgumentException($"マーカー{markerId}は、すでに部屋「{usedBy.name}」で使っています。", nameof(markerId));
            }
            var now = Now();
            var localizer = new LocalizerData
            {
                id = NewId(),
                markerId = markerId,
                positionInRoom = DataFormat.ToArray(positionInRoom),
                yawDeg = yawDeg,
                createdAt = now,
            };
            room.localizers.Add(localizer);
            room.updatedAt = now;
            return localizer;
        }

        public IEnumerable<LocalizerData> LocalizersOf(string roomId) => RequireRoom(roomId).localizers;

        public PointData AddPoint(string roomId, string name, Vec3 positionInRoom)
        {
            RequireRoom(roomId);
            var now = Now();
            var point = new PointData
            {
                id = NewId(),
                roomId = roomId,
                name = name,
                positionInRoom = DataFormat.ToArray(positionInRoom),
                createdAt = now,
                updatedAt = now,
            };
            Home.points.Add(point);
            return point;
        }

        public TaskData AddTask(string pointId, string title, Recurrence recurrence, DateTime firstDueDate)
        {
            var point = FindPoint(pointId)
                ?? throw new ArgumentException($"ポイントが見つかりません: {pointId}", nameof(pointId));
            var now = Now();
            var task = new TaskData
            {
                id = NewId(),
                roomId = point.roomId,
                pointId = pointId,
                title = title,
                recurrence = DataFormat.ToData(recurrence),
                firstDueDate = DataFormat.FormatDate(firstDueDate),
                createdAt = now,
                updatedAt = now,
            };
            Home.tasks.Add(task);
            return task;
        }

        /// <summary>
        /// タスクを完了する。実施記録を1件追加し、前回実施日を更新する。
        /// 次回期限は前回実施日から計算し直される（NextDueDate）。
        /// </summary>
        public CompletionData CompleteTask(string taskId, DateTime doneDate)
        {
            var task = FindTask(taskId)
                ?? throw new ArgumentException($"タスクが見つかりません: {taskId}", nameof(taskId));
            var now = Now();
            var completion = new CompletionData
            {
                id = NewId(),
                taskId = taskId,
                doneDate = DataFormat.FormatDate(doneDate),
                createdAt = now,
            };
            Home.completions.Add(completion);

            // 過去の日付で記録したときに、より新しい前回実施日を巻き戻さない
            var lastDone = DataFormat.ParseDate(task.lastDoneDate);
            if (lastDone == null || doneDate.Date > lastDone.Value)
            {
                task.lastDoneDate = DataFormat.FormatDate(doneDate);
            }
            task.updatedAt = now;
            return completion;
        }

        public PointData FindPoint(string pointId) =>
            Home.points.FirstOrDefault(p => p.id == pointId && string.IsNullOrEmpty(p.deletedAt));

        public TaskData FindTask(string taskId) =>
            Home.tasks.FirstOrDefault(t => t.id == taskId && string.IsNullOrEmpty(t.deletedAt));

        /// <summary>タスクの実施記録。新しい順。</summary>
        public IEnumerable<CompletionData> CompletionsOf(string taskId) =>
            Home.completions.Where(c => c.taskId == taskId).OrderByDescending(c => c.doneDate);

        public IEnumerable<RoomData> ActiveRooms() =>
            Home.rooms.Where(r => string.IsNullOrEmpty(r.deletedAt)).OrderBy(r => r.sortOrder);

        public IEnumerable<PointData> PointsInRoom(string roomId) =>
            Home.points.Where(p => p.roomId == roomId && string.IsNullOrEmpty(p.deletedAt));

        public IEnumerable<TaskData> TasksOfPoint(string pointId) =>
            Home.tasks.Where(t => t.pointId == pointId && string.IsNullOrEmpty(t.deletedAt));

        /// <summary>タスクの次回期限。</summary>
        public static DateTime NextDueDate(TaskData task) =>
            DueDateCalculator.NextDueDate(
                DataFormat.ToRecurrence(task.recurrence),
                DataFormat.ParseDate(task.firstDueDate).Value,
                DataFormat.ParseDate(task.lastDoneDate));

        /// <summary>タスクの状態（赤・黄・緑）。</summary>
        public static DueStatus StatusOf(TaskData task, DateTime today) =>
            DueDateCalculator.StatusOn(today, NextDueDate(task), DataFormat.ToRecurrence(task.recurrence));

        /// <summary>
        /// ポイントの色。そのポイントのタスクのうち、一番悪い状態にする（赤が1つでもあれば赤）。
        /// タスクが無ければ null。
        /// </summary>
        public DueStatus? StatusOfPoint(string pointId, DateTime today)
        {
            DueStatus? worst = null;
            foreach (var task in TasksOfPoint(pointId))
            {
                var status = StatusOf(task, today);
                if (worst == null || status > worst)
                {
                    worst = status;
                }
            }
            return worst;
        }

        private RoomData RequireRoom(string roomId) =>
            Home.rooms.FirstOrDefault(r => r.id == roomId && string.IsNullOrEmpty(r.deletedAt))
            ?? throw new ArgumentException($"部屋が見つかりません: {roomId}", nameof(roomId));

        private string Now() => DataFormat.FormatTimestamp(_utcNow());

        private static string NewId() => Guid.NewGuid().ToString("N");
    }
}
