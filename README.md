# DateCalc

C# / .NET 向けの、**ネットワーク非依存（完全オフライン）** で動作する稼働日・実稼働時間算出ライブラリです。

日勤・複数休憩・日跨ぎ稼働（夜勤）・日本の国民の祝日（法定計算＋ローカル CSV 差し替え）・休日出勤／特別休業の特例上書き・複数カレンダー管理・JSON／DB 連携に対応しています。

---

## 主な特徴

- **完全オフラインの日本祝日判定**: 「国民の祝日に関する法律」に基づく計算エンジン（`JapanHolidayProvider`）を内蔵し、ネットワーク接続なしで春分・秋分の日、ハッピーマンデー、振替休日、国民の休日、過去の特例祝日を判定
- **外部データ差し替え対応**: 内閣府 `syukujitsu.csv` 形式のローカルファイル読込（`CabinetOfficeCsvHolidayProvider`）や、CSV 収録年を優先しつつ未来年は内蔵計算へフォールバックする合成（`CompositeHolidayProvider.WithYearRangeFallback`）に対応
- **複数休憩・日跨ぎシフト（夜勤）対応**: `08:00-17:00`（休憩 `12:00-13:00`, `15:00-15:10`）のような日勤だけでなく、`22:00-06:00`（`22:00-30:00` 表記、深夜休憩 `02:00-03:00` や日跨ぎ休憩 `23:30-00:30`、24 時間連続稼働）を正確に算出
- **柔軟な特例上書き**: 休日出勤・祝日稼働（非稼働日→稼働日）、夏季休暇・臨時休業・毎年固定の年末年始（稼働日→非稼働日）を明確な優先順位で制御
- **マルチターゲット (`net8.0;netstandard2.0`)**:
  - `.NET 8.0+`: 外部 NuGet 依存ゼロ、`DateOnly` / `TimeOnly` オーバーロードを利用可能
  - `.NET Standard 2.0`: .NET Framework 4.8 等でも `DateTime` / `TimeSpan` ベースで同一機能を利用可能（`System.Text.Json 8.0.5` のみ参照）
- **イミュータブル・スレッドセーフ / DI・複数カレンダー対応**: 工場・部署・シフト別の複数カレンダーを `WorkCalendarRegistry`（`IWorkCalendarProvider`）で一元管理し、JSON / DB との相互変換用 DTO（`WorkCalendarDefinition`）を提供

---

## クイックスタート

### 1. 日勤カレンダー（複数休憩・休日出勤・特別休業）

```csharp
using System;
using DateCalc;
using DateCalc.Models;

WorkCalendar calendar = WorkCalendar.CreateBuilder()
    .WithId("factory-a-day")
    .WithName("第1工場 日勤")
    .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
    .AddBreak(TimeSpan.FromHours(12), TimeSpan.FromHours(13))
    .AddBreak(new TimeSpan(15, 0, 0), new TimeSpan(15, 10, 0))
    .AddWorkingDayOverride(new DateTime(2026, 5, 4), "GW祝日出勤")
    .AddNonWorkingDayOverride(new DateTime(2026, 8, 14), "夏季一斉休暇")
    .AddRecurringNonWorkingDay(12, 29, "年末年始")
    .Build();

// 稼働日判定・理由確認
bool isWork = calendar.IsWorkingDay(new DateTime(2026, 5, 4)); // true（祝日だが休出上書き）
DayStatus status = calendar.GetDayStatus(new DateTime(2026, 5, 6)); // false (PublicHoliday: 振替休日)

// 稼働日ベースの日付移動・集計（閉区間 [start, end]）
DateTime dueDay = calendar.AddWorkingDays(new DateTime(2026, 5, 1), 3);
int count = calendar.CountWorkingDays(new DateTime(2026, 5, 1), new DateTime(2026, 5, 10));

// 実稼働時間ベースの完了日時算出・逆算・実稼働時間計算（1日実働 7h50m）
DateTime finish = calendar.AddWorkingTime(new DateTime(2026, 5, 1, 10, 0, 0), TimeSpan.FromHours(8));
DateTime start = calendar.SubtractWorkingTime(finish, TimeSpan.FromHours(8));
TimeSpan worked = calendar.GetWorkingTime(new DateTime(2026, 5, 1, 11, 30, 0), new DateTime(2026, 5, 1, 15, 30, 0));
```

`.NET 8.0` 以上では `DateOnly` / `TimeOnly` オーバーロードも利用できます。

```csharp
DateOnly nextWork = calendar.GetNextWorkingDay(new DateOnly(2026, 5, 5));
```

---

### 2. 日跨ぎ稼働（夜勤）

夜勤シフトでは**始業日**を稼働日として扱います。たとえば「金曜稼働・土曜非稼働」で `22:00-06:00` シフトの場合、金曜始業の稼働は金曜 `22:00` 〜 土曜 `06:00` となります。

```csharp
WorkCalendar nightShift = WorkCalendar.CreateBuilder()
    .WithId("factory-a-night")
    .WithWorkingHours(TimeSpan.FromHours(22), TimeSpan.FromHours(6)) // 22:00-30:00 と等価
    .AddBreak(TimeSpan.FromHours(2), TimeSpan.FromHours(3))          // 深夜 02:00-03:00 は自動で翌日分に適用
    .Build();

// 金曜夜勤の繰り越しにより土曜 01:00 は稼働時間内（ただし土曜の IsWorkingDay は false）
bool satEarlyMorning = nightShift.IsWorkingTime(new DateTime(2026, 5, 9, 1, 0, 0)); // true
bool satBreak = nightShift.IsWorkingTime(new DateTime(2026, 5, 9, 2, 30, 0));       // false

// 土曜 00:00 境界を跨ぐ連続区間は結合されて返却
var intervals = nightShift.GetWorkingIntervals(
    new DateTime(2026, 5, 8, 21, 0, 0),
    new DateTime(2026, 5, 9, 7, 0, 0));
// -> [2026-05-08 22:00, 2026-05-09 02:00), [2026-05-09 03:00, 2026-05-09 06:00)
```

---

### 3. オフライン祝日データの差し替え・合成

法改正等で最新の内閣府 `syukujitsu.csv` をローカルに配布し、収録年はその CSV を、未収録の将来年は内蔵計算エンジンへフォールバックさせる構成が可能です。

```csharp
using DateCalc.Holidays;

CabinetOfficeCsvHolidayProvider csvProvider =
    CabinetOfficeCsvHolidayProvider.FromFile("syukujitsu.csv");

var calendar = WorkCalendar.CreateBuilder()
    .WithCabinetOfficeCsv(csvProvider, fallbackToJapanHolidays: true)
    .AddCustomHoliday(new DateTime(2026, 10, 1), "特別創立記念休日")
    .Build();
```

その他のプロバイダ:
- `JapanHolidayProvider.Instance`: 法定ルール計算エンジン（既定）
- `InMemoryHolidayProvider`: DB 等から読み込んだ祝日辞書・リスト用
- `CompositeHolidayProvider`: 複数プロバイダの優先合成／年範囲フォールバック
- `NullHolidayProvider.Instance`: 祝日判定を行わない設定

---

### 4. JSON 設定からの複数カレンダー管理

```json
[
  {
    "id": "factory-day",
    "name": "第1工場 日勤",
    "workingPeriods": [{ "start": "08:00", "end": "17:00" }],
    "breaks": [{ "start": "12:00", "end": "13:00" }],
    "nonWorkingDaysOfWeek": ["Saturday", "Sunday"],
    "useJapanHolidays": true,
    "recurringNonWorkingDays": [
      { "month": 12, "day": 29, "reason": "年末年始" },
      { "month": 12, "day": 30, "reason": "年末年始" }
    ],
    "dateOverrides": [
      { "date": "2026-05-04", "isWorkingDay": true, "reason": "祝日稼働" }
    ]
  },
  {
    "id": "factory-night",
    "name": "第1工場 夜勤",
    "workingPeriods": [{ "start": "22:00", "end": "06:00" }],
    "breaks": [{ "start": "02:00", "end": "03:00" }]
  }
]
```

```csharp
using DateCalc;
using DateCalc.Serialization;

IWorkCalendarProvider registry = WorkCalendarSerializer.LoadRegistryFromJson(jsonArray);
IWorkCalendar dayCalendar = registry.GetCalendar("factory-day");
IWorkCalendar nightCalendar = registry.GetCalendar("factory-night");
```

DB 等から独自に読み込む場合は `WorkCalendarDefinition` を生成して `WorkCalendar.FromDefinition(definition)` または `WorkCalendarRegistry.FromDefinitions(definitions)` を呼び出します。`calendar.ToDefinition()` で現在の構成を DTO へ戻すこともできます。

---

## 稼働日判定の優先順位・境界仕様

### 稼働日判定の 4 段階優先順位
`IsWorkingDay` / `GetDayStatus` は上から順に評価し、最初に一致したルールを適用します。

1. **個別日付の特例上書き**（`OverrideWorkingDay` / `OverrideNonWorkingDay`）※土日・祝日・毎年固定休日よりも優先
2. **毎年固定の会社休日**（`RecurringHoliday`）
3. **祝日プロバイダ**（`PublicHoliday`）
4. **定休曜日**（`Weekend`、既定: 土曜日・日曜日）
5. 上記いずれにも該当しない場合は **通常稼働日**（`RegularWorkingDay`）

### 境界ルール
- **時刻区間**: 半開区間 `[Start, End)` として扱います（例: `08:00-17:00`, 休憩 `12:00-13:00` の場合、`IsWorkingTime(08:00) == true`, `IsWorkingTime(12:00) == false`, `IsWorkingTime(13:00) == true`, `IsWorkingTime(17:00) == false`）。
- **`AddWorkingTime` / `SubtractWorkingTime`**: ちょうど区間の終端（例: `08:00 + 4h = 12:00`）で完了する場合は次の休憩を跨がずその境界時刻を返します。加算時間 `TimeSpan.Zero` の場合は基準日時をそのまま返します（次の稼働開始時刻へ寄せる場合は `GetNextWorkingTime` を使用）。
- **`CountWorkingDays` / `GetWorkingDays`**: 開始日・終了日の両端を含む閉区間です（`endDate < startDate` の場合は `ArgumentOutOfRangeException`）。
- **`GetWorkingTime(start, end)`**: `end < start` の場合は負の `TimeSpan` を返します。

---

## ディレクトリ構成とビルド・テスト

```text
├── docs/
│   └── implementation-plan.md   # 設計・実装計画ドキュメント
└── src/
    ├── DateCalc.slnx            # ソリューションファイル (.slnx)
    ├── DateCalc/                # ライブラリ本体 (net8.0;netstandard2.0)
    └── DateCalc.Tests/          # xUnit 単体テスト (net8.0)
```

```powershell
dotnet build src/DateCalc.slnx -c Release
dotnet test src/DateCalc.slnx -c Release --no-build --verbosity normal
```

---

## ドキュメント・ライセンス

- 設計・実装詳細: [docs/implementation-plan.md](docs/implementation-plan.md)
- ライセンス: [LICENSE.txt](LICENSE.txt)（MIT License）
