using System;
using System.Collections.Generic;

namespace HomeCare.Core.Data
{
    // 保存用のデータの形。設計書5章（家 → 部屋 → ポイント → タスク）に合わせている。
    // 端末内のJSONとクラウドで同じ形を使うため、中身は単純な値だけにする。
    // UnityのJsonUtilityで読み書きできるよう、プロパティではなく public なフィールドにしている。

    /// <summary>家1軒分のデータ。端末内のファイル1つに、これを丸ごと保存する。</summary>
    [Serializable]
    public class HomeData
    {
        /// <summary>現在のデータ形式の版。形式を変えたら上げ、古い版を読み替える処理を足す。</summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string id;
        public string name;
        public List<RoomData> rooms = new List<RoomData>();
        public List<PointData> points = new List<PointData>();
        public List<TaskData> tasks = new List<TaskData>();

        /// <summary>実施記録。追記だけで、書き換えはしない。</summary>
        public List<CompletionData> completions = new List<CompletionData>();
    }

    /// <summary>部屋。基準点（localizers）は、マーカーを入れる段階で追加する。</summary>
    [Serializable]
    public class RoomData
    {
        public string id;
        public string name;
        public int sortOrder;
        public string createdAt;
        public string updatedAt;
        public string deletedAt;
    }

    /// <summary>ARポイント。位置は部屋の座標（メートル）で持ち、マーカー番号は持たない。</summary>
    [Serializable]
    public class PointData
    {
        public string id;
        public string roomId;
        public string name;
        public float[] positionInRoom = new float[3];
        public string icon;
        public string createdAt;
        public string updatedAt;
        public string deletedAt;
    }

    /// <summary>家事・メンテナンスのタスク。pointId が空なら、ARに置かずリストだけで管理する。</summary>
    [Serializable]
    public class TaskData
    {
        public string id;
        public string roomId;
        public string pointId;
        public string title;
        public RecurrenceData recurrence = new RecurrenceData();

        /// <summary>日付は "2026-11-03" の形。一度も実施していなければ lastDoneDate は空。</summary>
        public string firstDueDate;
        public string lastDoneDate;
        public string note;
        public string createdAt;
        public string updatedAt;
        public string deletedAt;
    }

    /// <summary>
    /// 実施記録（完了ボタンを押した記録）。
    /// 家族2人がほぼ同時に完了しても記録が2件残るだけで、データは壊れない。
    /// </summary>
    [Serializable]
    public class CompletionData
    {
        public string id;
        public string taskId;

        /// <summary>実施した日（"2026-10-06" の形）。</summary>
        public string doneDate;

        /// <summary>実施した人。ログインを入れるまでは空。</summary>
        public string doneByUid;
        public string note;
        public string createdAt;
    }

    /// <summary>周期。例：3か月ごと = { every: 3, unit: "month" }</summary>
    [Serializable]
    public class RecurrenceData
    {
        public int every = 1;

        /// <summary>"day" / "week" / "month" / "year" のどれか。</summary>
        public string unit = "month";
    }
}
