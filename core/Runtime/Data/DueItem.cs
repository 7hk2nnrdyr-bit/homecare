using System;
using HomeCare.Core.Scheduling;

namespace HomeCare.Core.Data
{
    /// <summary>リスト画面の1行分。タスクと、その場所・部屋・次回期限・状態をまとめたもの。</summary>
    public class DueItem
    {
        public TaskData Task { get; }

        /// <summary>ARに置いていないタスクなら null。</summary>
        public PointData Point { get; }
        public RoomData Room { get; }
        public DateTime NextDue { get; }
        public DueStatus Status { get; }

        public DueItem(TaskData task, PointData point, RoomData room, DateTime nextDue, DueStatus status)
        {
            Task = task;
            Point = point;
            Room = room;
            NextDue = nextDue;
            Status = status;
        }
    }
}
