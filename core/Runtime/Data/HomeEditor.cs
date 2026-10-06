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
            var point = Home.points.FirstOrDefault(p => p.id == pointId && string.IsNullOrEmpty(p.deletedAt))
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

        private void RequireRoom(string roomId)
        {
            if (!Home.rooms.Any(r => r.id == roomId && string.IsNullOrEmpty(r.deletedAt)))
            {
                throw new ArgumentException($"部屋が見つかりません: {roomId}", nameof(roomId));
            }
        }

        private string Now() => DataFormat.FormatTimestamp(_utcNow());

        private static string NewId() => Guid.NewGuid().ToString("N");
    }
}
