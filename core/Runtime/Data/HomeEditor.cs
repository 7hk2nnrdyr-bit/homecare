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
            foreach (var point in Home.points)
            {
                if (point.rotationInRoom == null || point.rotationInRoom.Length != 4)
                {
                    point.rotationInRoom = DataFormat.ToArray(Quat.Identity);
                }
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

        /// <summary>部屋の原点のマーカー番号（最初に登録したマーカー）。マーカーが無ければ null。</summary>
        public string MarkerOf(string roomId) =>
            RequireRoom(roomId).localizers.Where(l => l.type == "marker").Select(l => l.markerId).FirstOrDefault();

        /// <summary>まだどの部屋でも使っていないマーカー番号を、並び順で返す。</summary>
        public IEnumerable<string> UnusedMarkerIds(IEnumerable<string> available)
        {
            var used = new HashSet<string>(ActiveRooms().SelectMany(r => r.localizers).Select(l => l.markerId));
            return available.Where(id => !used.Contains(id));
        }

        /// <summary>
        /// 部屋を足し、まだ使っていないマーカーを1つ割り当てて、その部屋の原点にする。
        /// 名前が空・同じ名前の部屋がある・使えるマーカーが残っていないときは断る。
        /// </summary>
        /// <param name="available">アプリに入っているマーカー番号（例：M01〜M10）。</param>
        public RoomData AddRoomWithMarker(string name, IEnumerable<string> available)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("部屋の名前を入れてください。");
            }
            if (ActiveRooms().Any(r => r.name == name))
            {
                throw new ArgumentException($"部屋「{name}」はもうあります。");
            }
            var markerId = UnusedMarkerIds(available).FirstOrDefault()
                ?? throw new ArgumentException("使えるマーカーが残っていません（全部で10個）。");
            var room = AddRoom(name);
            AddMarkerLocalizer(room.id, markerId, Vec3.Zero, 0f);
            return room;
        }

        public PointData AddPoint(string roomId, string name, Vec3 positionInRoom, Quat? rotationInRoom = null)
        {
            RequireRoom(roomId);
            var now = Now();
            var point = new PointData
            {
                id = NewId(),
                roomId = roomId,
                name = name,
                positionInRoom = DataFormat.ToArray(positionInRoom),
                rotationInRoom = DataFormat.ToArray(rotationInRoom ?? Quat.Identity),
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

        /// <summary>部屋の名前を変える。名前が空・ほかの部屋と同じ名前のときは断る。</summary>
        public void RenameRoom(string roomId, string name)
        {
            var room = RequireRoom(roomId);
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("部屋の名前を入れてください。");
            }
            if (ActiveRooms().Any(r => r.id != roomId && r.name == name))
            {
                throw new ArgumentException($"部屋「{name}」はもうあります。");
            }
            if (room.name == name)
            {
                return;
            }
            room.name = name;
            room.updatedAt = Now();
        }

        /// <summary>
        /// 部屋を削除する。部屋の中の場所とやることも一緒に削除し、マーカーはほかの部屋で使えるようになる。
        /// 部屋が1つしかないときは断る（カメラの画面で登録する先が無くなるため）。
        /// 削除は消さずに印（deletedAt）を付けるだけなので、ほかの端末にも同期で伝わる。
        /// </summary>
        public void DeleteRoom(string roomId)
        {
            var room = RequireRoom(roomId);
            if (ActiveRooms().Count() <= 1)
            {
                throw new ArgumentException("部屋が1つだけのときは削除できません。名前を変えて使ってください。");
            }
            var now = Now();
            foreach (var point in PointsInRoom(roomId).ToList())
            {
                MarkPointDeleted(point, now);
            }
            // 場所の無いやること（古いデータ）も残さない
            foreach (var task in Home.tasks.Where(t => t.roomId == roomId && string.IsNullOrEmpty(t.deletedAt)))
            {
                MarkDeleted(task, now);
            }
            room.deletedAt = now;
            room.updatedAt = now;
        }

        /// <summary>場所の名前を変える。</summary>
        public void RenamePoint(string pointId, string name)
        {
            var point = RequirePoint(pointId);
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("場所の名前を入れてください。");
            }
            if (point.name == name)
            {
                return;
            }
            point.name = name;
            point.updatedAt = Now();
        }

        /// <summary>場所を削除する。その場所のやることも一緒に削除する。</summary>
        public void DeletePoint(string pointId) => MarkPointDeleted(RequirePoint(pointId), Now());

        /// <summary>
        /// やることの名前・周期・最初の期限を変える。実施記録と前回実施日はそのまま残る
        /// （一度でも完了していれば、次回期限は前回実施日と新しい周期から計算される）。
        /// </summary>
        public void UpdateTask(string taskId, string title, Recurrence recurrence, DateTime firstDueDate)
        {
            var task = FindTask(taskId)
                ?? throw new ArgumentException($"やることが見つかりません: {taskId}", nameof(taskId));
            title = (title ?? "").Trim();
            if (title.Length == 0)
            {
                throw new ArgumentException("やることを入れてください。");
            }
            var recurrenceData = DataFormat.ToData(recurrence);
            var firstDue = DataFormat.FormatDate(firstDueDate);
            if (task.title == title && task.firstDueDate == firstDue
                && task.recurrence != null
                && task.recurrence.every == recurrenceData.every && task.recurrence.unit == recurrenceData.unit)
            {
                return;
            }
            task.title = title;
            task.recurrence = recurrenceData;
            task.firstDueDate = firstDue;
            task.updatedAt = Now();
        }

        /// <summary>
        /// やることを削除する。その場所にほかのやることが無くなったら、場所も一緒に削除する
        /// （やることの無い場所は、カメラの画面で色の付かない球として残ってしまうため）。
        /// </summary>
        /// <returns>場所も削除したら true。</returns>
        public bool DeleteTask(string taskId)
        {
            var task = FindTask(taskId)
                ?? throw new ArgumentException($"やることが見つかりません: {taskId}", nameof(taskId));
            var now = Now();
            MarkDeleted(task, now);
            var point = string.IsNullOrEmpty(task.pointId) ? null : FindPoint(task.pointId);
            if (point == null || TasksOfPoint(point.id).Any())
            {
                return false;
            }
            MarkPointDeleted(point, now);
            return true;
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
        /// リスト画面に出す、すべてのタスクの一覧。期限の近い順（期限切れが先頭）に並べる。
        /// 期限が同じなら、状態の悪い順、名前の順にする。
        /// </summary>
        public IReadOnlyList<DueItem> DueList(DateTime today) =>
            Home.tasks
                .Where(IsShown)
                .Select(t => new DueItem(
                    t,
                    string.IsNullOrEmpty(t.pointId) ? null : FindPoint(t.pointId),
                    Home.rooms.FirstOrDefault(r => r.id == t.roomId),
                    NextDueDate(t),
                    StatusOf(t, today)))
                .OrderBy(item => item.NextDue)
                .ThenByDescending(item => item.Status)
                .ThenBy(item => item.Task.title, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// 一覧に出すやることか。やること自体が削除されていなくても、場所や部屋が削除されていれば出さない
        /// （別の端末で部屋を削除したのと同時に、その部屋へ場所を足したときなどに起きる）。
        /// </summary>
        private bool IsShown(TaskData task)
        {
            if (!string.IsNullOrEmpty(task.deletedAt))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(task.pointId) && FindPoint(task.pointId) == null)
            {
                return false;
            }
            var room = Home.rooms.FirstOrDefault(r => r.id == task.roomId);
            return room == null || string.IsNullOrEmpty(room.deletedAt);
        }

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

        private PointData RequirePoint(string pointId) =>
            FindPoint(pointId) ?? throw new ArgumentException($"場所が見つかりません: {pointId}", nameof(pointId));

        private void MarkPointDeleted(PointData point, string now)
        {
            foreach (var task in TasksOfPoint(point.id).ToList())
            {
                MarkDeleted(task, now);
            }
            point.deletedAt = now;
            point.updatedAt = now;
        }

        private static void MarkDeleted(TaskData task, string now)
        {
            task.deletedAt = now;
            task.updatedAt = now;
        }

        private RoomData RequireRoom(string roomId) =>
            Home.rooms.FirstOrDefault(r => r.id == roomId && string.IsNullOrEmpty(r.deletedAt))
            ?? throw new ArgumentException($"部屋が見つかりません: {roomId}", nameof(roomId));

        private string Now() => DataFormat.FormatTimestamp(_utcNow());

        private static string NewId() => Guid.NewGuid().ToString("N");
    }
}
