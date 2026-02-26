# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）を MySQL データベースで管理するためのアプリケーション集です。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data                  … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
│
├── PrecureDataStars.Episodes              … エピソード管理 GUI（WinForms）
├── PrecureDataStars.TitleCharStatsJson    … 文字統計一括再計算（コンソール）
├── PrecureDataStars.YouTubeCrawler        … YouTube URL 自動抽出（コンソール）
│
├── PrecureDataStars.BDAnalyzer            … Blu-ray/DVD チャプター解析（WinForms）
├── PrecureDataStars.CDAnalyzer            … CD-DA トラック解析（WinForms）
│
└── db/
    └── schema.sql                         … MySQL スキーマ定義（DDL）
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインの管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。 |

---

## 動作要件

- **OS**: Windows 10 以降（CDAnalyzer / BDAnalyzer はドライブ P/Invoke のため Windows 専用）
- **ランタイム**: .NET 9 SDK
- **データベース**: MySQL 8.0+
- **外部ライブラリ（NuGet）**:
  - Dapper / MySqlConnector（データアクセス）
  - MeCab.DotNet（形態素解析 — Episodes GUI のみ）
  - System.Configuration.ConfigurationManager

---

## セットアップ

### 1. データベース作成

```bash
mysql -u root -p < db/schema.sql
```

`db/schema.sql` によりデータベース `precure_datastars` と全テーブルが作成されます。

### 2. 接続文字列の設定

DB 接続が必要なプロジェクト（Episodes / TitleCharStatsJson / YouTubeCrawler）の `App.config` に接続文字列を設定してください。

```xml
<connectionStrings>
  <add name="DatastarsMySql"
       connectionString="Server=localhost;Port=3306;Database=precure_datastars;Uid=YOUR_USER;Pwd=YOUR_PASSWORD;CharSet=utf8mb4;"
       providerName="MySqlConnector" />
</connectionStrings>
```

### 3. ビルド・実行

```bash
dotnet build precure-datastars-wintools.sln
dotnet run --project PrecureDataStars.Episodes
```

---

## データベーススキーマ

DDL ファイル: [`db/schema.sql`](db/schema.sql)

### ER 概要

```
series_kinds ──┐
               ├── series ──┬── episodes ──── episode_parts
series_relation_kinds ──┘   │                      │
                            └── (self-ref)    part_types ──┘
```

### テーブル一覧

#### `series_kinds` — シリーズ種別マスタ

シリーズの分類コードを定義するマスタテーブル。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード（例: `TV`, `MOVIE`, `OVA`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) | 英語名 |

#### `series_relation_kinds` — シリーズ関係種別マスタ

親子シリーズ間の関係を定義するマスタテーブル。

| 列名 | 型 | 説明 |
|---|---|---|
| `relation_code` | VARCHAR(32) PK | 関係コード（例: `COFEATURE`, `SEGMENT`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) | 英語名 |

#### `series` — シリーズ

プリキュア各作品（TV シリーズ・劇場版等）の情報を管理する中核テーブル。

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK AUTO_INCREMENT | シリーズ ID |
| `kind_code` | VARCHAR(32) FK | シリーズ種別（→ `series_kinds`） |
| `parent_series_id` | INT FK NULL | 親シリーズ ID（→ `series` 自己参照） |
| `relation_to_parent` | VARCHAR(32) FK NULL | 親との関係種別（→ `series_relation_kinds`） |
| `seq_in_parent` | TINYINT UNSIGNED NULL | 親内での並び順（COFEATURE / SEGMENT 時は必須、CHECK 制約あり） |
| `title` | VARCHAR(255) | 正式タイトル（日本語） |
| `title_kana` | VARCHAR(255) NULL | タイトル読み（ひらがな） |
| `title_short` | VARCHAR(128) NULL | 略称（例: 「キミプリ」） |
| `title_short_kana` | VARCHAR(255) NULL | 略称読み |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
| `title_short_en` | VARCHAR(128) NULL | 英語略称 |
| `slug` | VARCHAR(128) UNIQUE | URL 用スラッグ（CHECK: `^[a-z0-9-]+$`） |
| `start_date` | DATE | 放送/公開開始日 |
| `end_date` | DATE NULL | 放送終了日（CHECK: `start_date ≤ end_date`） |
| `episodes` | SMALLINT UNSIGNED NULL | 話数 |
| `run_time_seconds` | SMALLINT UNSIGNED NULL | 1 話あたりの標準尺（秒） |
| `toei_anim_official_site_url` | VARCHAR(1024) NULL | 東映アニメーション公式サイト URL |
| `toei_anim_lineup_url` | VARCHAR(1024) NULL | 東映ラインナップ URL |
| `abc_official_site_url` | VARCHAR(1024) NULL | ABC（テレビ朝日系）公式サイト URL |
| `amazon_prime_distribution_url` | VARCHAR(1024) NULL | Amazon Prime Video 配信 URL |
| `vod_intro` | SMALLINT UNSIGNED NULL | 配信版の東映動画タイトル尺（秒）。配信版合計尺の算出に加算する |
| `font_subtitle` | VARCHAR(64) NULL | サブタイトル表示用フォント名（暫定フィールド） |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**CHECK 制約:**
- `ck_parent_relation`: 親 ID・関係種別・順序は「全 NULL」か「親 ID + 関係種別が両方 NOT NULL」のいずれか
- `ck_seq_cofeature` / `ck_seq_segment`: COFEATURE / SEGMENT 関係時は seq_in_parent が 1 以上必須
- `ck_dates_order`: 終了日は開始日以降
- `ck_slug_format`: スラッグは小文字英数字とハイフンのみ

#### `episodes` — エピソード

各話の情報を管理するテーブル。複数のナンバリング体系を持つ。

| 列名 | 型 | 説明 |
|---|---|---|
| `episode_id` | INT PK AUTO_INCREMENT | エピソード ID |
| `series_id` | INT FK | 所属シリーズ（→ `series`） |
| `series_ep_no` | INT | シリーズ内話数（1始まり） |
| `total_ep_no` | INT UNIQUE NULL | プリキュアシリーズ通算話数 |
| `total_oa_no` | INT UNIQUE NULL | プリキュアシリーズ通算放送回数（合体SP 等で話数と乖離する場合あり） |
| `nitiasa_oa_no` | INT UNIQUE NULL | ニチアサ枠通算放送回数 |
| `title_text` | VARCHAR(255) | サブタイトル（プレーンテキスト） |
| `title_rich_html` | TEXT NULL | サブタイトル（ルビ付き HTML） |
| `title_kana` | VARCHAR(255) NULL | サブタイトル読み（ひらがな） |
| `title_char_stats` | JSON NULL | サブタイトルの文字統計 JSON（TitleCharStatsBuilder で生成） |
| `on_air_at` | DATETIME | 放送日時（JST 想定） |
| `on_air_date` | DATE GENERATED | `on_air_at` から算出される放送日（STORED） |
| `toei_anim_summary_url` | VARCHAR(1024) NULL | 東映あらすじページ URL |
| `toei_anim_lineup_url` | VARCHAR(1024) NULL | 東映ラインナップ URL |
| `youtube_trailer_url` | VARCHAR(1024) NULL | YouTube 予告動画 URL |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**CHECK 制約:**
- `ck_nitiasa_matches`: `nitiasa_oa_no = total_oa_no + 978`（978 = 『明日のナージャ』までの放送回数）
- `ck_series_ep_no_pos` / `ck_total_ep_no_pos` / `ck_total_oa_no_pos` / `ck_nitiasa_oa_no_pos`: 各話数は 1 以上

**ナンバリング体系の補足:**

| 列 | 意味 | 例（たんプリ第1話） |
|---|---|---|
| `series_ep_no` | 作品内の話数 | 1 |
| `total_ep_no` | プリキュア通算話数（『ふたりはプリキュア』第1話=1） | 1068 |
| `total_oa_no` | プリキュア通算放送回数 | 1082 |
| `nitiasa_oa_no` | ニチアサ通算放送回数（= total_oa_no + 978） | 2060 |

#### `part_types` — パート種別マスタ

エピソードを構成するパートの種別を定義するマスタテーブル。

| 列名 | 型 | 説明 |
|---|---|---|
| `part_type` | VARCHAR(32) PK | パート種別コード（例: `AVANT`, `PART_A`, `PART_B`, `ED`, `PREVIEW`） |
| `name_ja` | VARCHAR(64) | 日本語名（例: 「アバンタイトル」「Aパート」） |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序（小さい値が先頭。UNIQUE 制約で重複不可） |

#### `episode_parts` — エピソードパート

エピソードの内部構成（アバン・OP・Aパート・Bパート・ED・次回予告等）とその尺を管理する。

| 列名 | 型 | 説明 |
|---|---|---|
| `episode_id` | INT PK FK | エピソード ID（→ `episodes`、CASCADE DELETE） |
| `episode_seq` | TINYINT UNSIGNED PK | パート順序（1始まり） |
| `part_type` | VARCHAR(32) FK | パート種別（→ `part_types`） |
| `oa_length` | SMALLINT UNSIGNED NULL | 本放送尺（秒） |
| `disc_length` | SMALLINT UNSIGNED NULL | 円盤（BD/DVD）尺（秒） |
| `vod_length` | SMALLINT UNSIGNED NULL | 配信尺（秒） |
| `notes` | VARCHAR(255) NULL | 備考 |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**複合 PK**: `(episode_id, episode_seq)`
**UNIQUE 制約**: `(episode_id, part_type)` — 同一エピソード内で同じパート種別は 1 つまで
**CHECK 制約**: 各尺は NULL または 0 以上、seq は 1 以上

### title_char_stats JSON スキーマ

`TitleCharStatsBuilder.BuildJson()` が生成する JSON の構造:

```json
{
  "norm": "NFKC+jpn-fix+ellipsis",
  "chars": {
    "!": 2,
    "0": 1,
    "1": 1,
    "D": 1,
    "E": 1,
    "V": 1,
    "a": 1,
    "i": 1,
    "v": 1,
    "て": 1,
    "れ": 1,
    "カ": 1,
    "ト": 1,
    "ピ": 1,
    "ロ": 1,
    "本": 1,
    "立": 1
  },
  "length": {
    "graphemes": 18,
    "codepoints": 19,
    "unique_graphemes": 17
  },
  "spaces": 1,
  "version": 1,
  "categories": {
    "Emoji": 0,
    "Kanji": 2,
    "Latin": 6,
    "Other": 0,
    "Punct": 2,
    "Digits": 2,
    "Symbols": 0,
    "Hiragana": 2,
    "Katakana": 4
  }
}
```

| フィールド | 説明 |
|---|---|
| `version` | スキーマバージョン（現在は常に 1） |
| `norm` | 適用した正規化: NFKC + 日本語固有修正（波ダッシュ統一）+ 三点リーダ復元 |
| `length.codepoints` | Unicode コードポイント数 |
| `length.graphemes` | 書記素クラスタ数（空白を除く） |
| `length.unique_graphemes` | ユニーク書記素数 |
| `spaces` | 空白文字数（カウント対象外） |
| `categories` | カテゴリ別の書記素数 |
| `chars` | 各文字の出現回数（空白を除く） |

---

## ライセンス

[MIT License](LICENSE) © 2025 Shota (SHOWTIME)
