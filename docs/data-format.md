# 家のデータの形（共通データ形式）

iPhone・Android・将来のクラウドで、同じ形の家のデータを使う。
この形はUnityにもARKit・ARCoreにも依存しない。中身は文字列・数・配列だけで、どの言語でも読み書きできる。

- 端末内の保存：`home.json`（アプリのフォルダ）
- 端末間の受け渡し：同じJSONをファイルかクリップボードで渡す
- クラウド同期：同じ形をFirestoreに保存する（下の「クラウドでの置き場所」）

コードでは `core/Runtime/Data/HomeData.cs` がこの形の定義。

## 全体の形

```
家 home
├─ 部屋 rooms[]
│   └─ 基準点 localizers[]（マーカー番号と、部屋の座標の中の位置・向き）
├─ ポイント points[]（部屋の座標での位置・向き）
├─ タスク tasks[]
└─ 実施記録 completions[]
```

一覧はすべて家の直下に平らに並べ、`roomId` や `pointId` でつなぐ。入れ子にしないのは、クラウドで1件ずつ保存・同期しやすくするため。

## 例

```json
{
  "schemaVersion": 1,
  "id": "3f2c…",
  "name": "わが家",
  "rooms": [
    {
      "id": "a81e…", "name": "リビング", "sortOrder": 0,
      "localizers": [
        { "id": "77b0…", "type": "marker", "markerId": "M01",
          "positionInRoom": [0, 0, 0], "yawDeg": 0, "createdAt": "2026-10-06T05:00:00.000Z" }
      ],
      "createdAt": "2026-10-06T05:00:00.000Z", "updatedAt": "2026-10-06T05:00:00.000Z", "deletedAt": ""
    }
  ],
  "points": [
    { "id": "c4d9…", "roomId": "a81e…", "name": "エアコン",
      "positionInRoom": [1.2, 2.1, -0.4], "rotationInRoom": [0, 0, 0, 1], "icon": "",
      "createdAt": "…", "updatedAt": "…", "deletedAt": "" }
  ],
  "tasks": [
    { "id": "e510…", "roomId": "a81e…", "pointId": "c4d9…", "title": "フィルター掃除",
      "recurrence": { "every": 3, "unit": "month" },
      "firstDueDate": "2026-11-01", "lastDoneDate": "", "note": "",
      "createdAt": "…", "updatedAt": "…", "deletedAt": "" }
  ],
  "completions": [
    { "id": "0b6a…", "taskId": "e510…", "doneDate": "2026-10-06", "doneByUid": "", "note": "", "createdAt": "…" }
  ]
}
```

## AR位置の決めごと

ポイントの位置は「マーカーからの相対位置」ではなく、**部屋の座標**で持つ。
部屋の座標は最初に登録したマーカーを原点にしているので、マーカーが1枚なら「マーカーからの相対位置・向き」と同じ意味になる。
マーカーを2枚以上にしたり、将来クラウドアンカーで位置合わせしたりしても、ポイントのデータを変えずに済む。

| 項目 | 決めごと |
|---|---|
| 単位 | メートル |
| 原点 | 部屋の最初のマーカーの中心 |
| 上（Y） | 重力の真上。マーカーが少し傾いて貼られていても上下はずれない |
| 前（Z） | 壁のマーカーなら壁から部屋側へ向かう向き。床や棚に置いたマーカーなら画像の上辺の向き。境目は面の向きと真上の角度が45度 |
| 右（X） | Y と Z から決まる（Unityと同じ左手系） |
| `positionInRoom` | `[x, y, z]` |
| `rotationInRoom` | クォータニオン `[x, y, z, w]`。回転なしは `[0, 0, 0, 1]` |
| 基準点 `localizers` | `markerId`（例："M01"）、部屋の座標での `positionInRoom` と水平の向き `yawDeg`（度）。最初のマーカーは位置0・向き0 |

iPhoneとAndroidの違いはAR Foundationが吸収し、どちらもUnityと同じ向き（上がY、左手系）で姿勢を返す。
どの端末も同じマーカーを自分のカメラで見て部屋の座標を作り直すので、ARKitやARCoreが端末内で作る空間データは受け渡さない。

マーカー番号は、アプリに入れたマーカー画像の名前（`Unity/Assets/HomeCare/Markers/HomeCareMarkers.asset`）と同じにする。
同じマーカーを2つの部屋で使うことはできない。

## その他の決めごと

- **ID**：端末で作るUUID（32文字の16進数）。オフラインでも作れて、別の端末で作ったIDが重なることはない
- **期限・実施日**：`"2026-11-03"` の形の日付。時差で1日ずれないよう時刻を持たない
- **作成・更新・削除時刻**：UTCの `"2026-10-06T05:00:00.000Z"` の形。この形なら文字列の大小で新旧を比べられる
- **削除**：すぐには消さず `deletedAt` に時刻を入れる。ほかの端末やクラウドに削除を伝えるため
- **周期**：`{ "every": 3, "unit": "month" }`。`unit` は `day` / `week` / `month` / `year`
- **版番号**：`schemaVersion`。形を変えるときに上げ、古い版を読み替える処理を足す。アプリより新しい版のデータは読み込まない

## 受け取ったデータの合わせ方

`core/Runtime/Data/HomeImporter.cs`。クラウド同期でも同じ考え方を使う。

1. この端末に場所もやることも無ければ、受け取ったデータをそのまま使う
2. 家の `id` が違えば、別の家のデータなので読み込まない
3. 同じ家なら、部屋・ポイント・タスクはIDごとに `updatedAt` が新しい方を残す
4. 部屋の基準点は、どちらの端末で足したものも残す
5. 実施記録は書き換えないので両方を足し合わせ、前回実施日は記録の中で一番新しい日にする

## クラウドでの置き場所（Firestore）

Firestoreでは、家の下に一覧ごとのコレクションを作り、1件を1ドキュメントにする。中身の項目名は上と同じ。
ドキュメントIDは、そのデータの `id` と同じ。

```
homes/{homeId}                     … id, name, schemaVersion, ownerUid, memberUids
homes/{homeId}/rooms/{roomId}      … 部屋（localizers を含む）
homes/{homeId}/points/{pointId}
homes/{homeId}/tasks/{taskId}
homes/{homeId}/completions/{completionId}
invites/{code}                     … code, homeId, createdByUid, createdAt, expiresAtMillis
```

- `ownerUid`：家を作った利用者のID。`memberUids`：家のメンバーの利用者IDの一覧。どちらもクラウドにだけある。
- `invites`：家族を招待するコード。`expiresAtMillis` は期限（1970年1月1日からのミリ秒、UTC）。招待コードで参加すると、家の `joinCode` に使ったコードが残る。
- 数の配列（`positionInRoom` など）は数の配列、`recurrence` は入れ子の項目（map）として保存する。
- 日付・時刻は端末内と同じ文字列のまま保存する（並べ替えと新旧の比べ方を端末と同じにするため）。
- 同期の合わせ方は「受け取ったデータの合わせ方」と同じ。合わせた後、クラウドと中身が違うものだけを送る。

端末内の `home.json` はそのまま残し、オフラインでも使えるようにする。
接続のしくみと設定のしかたは [firebase/README.md](../firebase/README.md)。
