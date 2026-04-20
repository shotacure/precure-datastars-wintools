# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）** を MySQL データベースで管理するためのアプリケーション集です。

> **v1.1.0** — 音楽・映像カタログ機能を追加しました（商品／ディスク／トラック／歌／劇伴の 2 階層管理、CDAnalyzer / BDAnalyzer からの DB 登録、Catalog GUI、旧 SQL Server 版からの移行ツール）。詳細は末尾の [変更履歴](#変更履歴) を参照。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data                    … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common          … カタログ GUI 共通（Dialog/Service、★ v1.1.0）
│
├── PrecureDataStars.Episodes                … エピソード管理 GUI（WinForms）
├── PrecureDataStars.Catalog                 … カタログ管理 GUI（WinForms、★ v1.1.0）
├── PrecureDataStars.TitleCharStatsJson      … 文字統計一括再計算（コンソール）
├── PrecureDataStars.YouTubeCrawler          … YouTube URL 自動抽出（コンソール）
├── PrecureDataStars.LegacyImport            … 旧 SQL Server 版 → MySQL 版 移行（コンソール、★ v1.1.0）
│
├── PrecureDataStars.BDAnalyzer              … Blu-ray/DVD チャプター解析（WinForms）＋DB 連携（★ v1.1.0）
├── PrecureDataStars.CDAnalyzer              … CD-DA トラック解析（WinForms）＋DB 連携（★ v1.1.0）
│
├── Directory.Build.props                    … 全プロジェクト共通の Version・LangVersion（★ v1.1.0）
└── db/
    ├── schema.sql                           … MySQL スキーマ定義（DDL、新規構築用）
    └── migrations/
        └── v1.1.0_add_music_catalog.sql     … 既存 v1.0.x DB への追加用マイグレーション（★ v1.1.0）
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series, Product, Disc, Track, Song, SongRecording, BgmCue, BgmRecording 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Catalog.Common** ★ | クラスライブラリ | CDAnalyzer / BDAnalyzer / Catalog GUI の 3 つで共有するダイアログ（`DiscMatchDialog`・`NewProductDialog`）と `DiscRegistrationService`（ディスク照合 → 登録ビジネスロジック）を提供する。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインのエピソード管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.Catalog** ★ | WinForms GUI | 音楽・映像カタログ管理 GUI。**閲覧専用の「ディスク・トラック閲覧」**（翻訳値で一覧表示、尺はミリ秒まで表示）と、5 つの編集フォーム（商品・ディスク／トラック・歌（曲＋録音）・劇伴（キュー＋録音）・マスタ類）をメニューから切り替えて使う。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.LegacyImport** ★ | コンソール | 旧 SQL Server 版の discs / tracks / songs / musics テーブルから、新 MySQL 版の products / discs / tracks / songs / song_recordings / bgm_cues / bgm_sessions へ移行するバッチ。`--dry-run` オプションで件数サマリーだけの試行運転が可能。 |
| **PrecureDataStars.BDAnalyzer** ★ | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。v1.1.0 からは DB 連携パネルで既存ディスクとの照合・新規商品登録が可能（チャプター→トラック自動投入はなし。トラック編集は Catalog GUI で行う運用）。 |
| **PrecureDataStars.CDAnalyzer** ★ | WinForms GUI | CD-DA ディスクの TOC・MCN・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。v1.1.0 からは DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行できる。 |

---

## 動作要件

- **OS**: Windows 10 以降（CDAnalyzer / BDAnalyzer はドライブ P/Invoke のため Windows 専用）
- **ランタイム**: .NET 9 SDK
- **データベース**: MySQL 8.0+
- **旧 SQL Server 版からの移行を行う場合のみ**: SQL Server（Express 以上）+ ネットワーク到達性
- **外部ライブラリ（NuGet）**:
  - Dapper / MySqlConnector（データアクセス）
  - MeCab.DotNet（形態素解析 — Episodes GUI のみ）
  - System.Configuration.ConfigurationManager
  - Microsoft.Data.SqlClient（LegacyImport のみ）

---

## セットアップ

### 1. データベース構築（新規の場合）

```bash
mysql -u root -p < db/schema.sql
```

`db/schema.sql` によりデータベース `precure_datastars` と全テーブル（エピソード系 6 本 + 音楽・映像カタログ系 14 本）が作成されます。

### 1'. 既存 v1.0.x からのアップグレード

既に v1.0.x を運用中の DB には、マイグレーション SQL を流して音楽カタログ系テーブルのみを追加します（既存テーブル・既存データは一切変更されません。冪等なので複数回流しても安全）。

```bash
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.0_add_music_catalog.sql
```

追加される内容:

- マスタ 7 本（`product_kinds`, `disc_kinds`, `track_content_kinds`, `song_music_classes`, `song_size_variants`, `song_part_variants`, `bgm_sessions`）と初期データ（INSERT IGNORE）
- 本体 7 本（`products`, `discs`, `tracks`, `video_chapters`, `songs`, `song_recordings`, `bgm_cues`）

### 2. 接続文字列の設定

DB 接続が必要なプロジェクト（Episodes / Catalog / CDAnalyzer / BDAnalyzer / TitleCharStatsJson / YouTubeCrawler）の `App.config.sample` を `App.config` にコピーし、接続文字列を設定してください。

```xml
<connectionStrings>
  <add name="DatastarsMySql"
       connectionString="Server=localhost;Port=3306;Database=precure_datastars;Uid=YOUR_USER;Pwd=YOUR_PASSWORD;CharSet=utf8mb4;"
       providerName="MySqlConnector" />
</connectionStrings>
```

**LegacyImport** のみは 2 つの接続文字列（`LegacyServer` と `TargetMySql`）が必要です（`App.config.sample` 参照）。

### 3. ビルド・実行

```bash
dotnet build precure-datastars-wintools.sln
dotnet run --project PrecureDataStars.Episodes
dotnet run --project PrecureDataStars.Catalog
```

### 4. リリースビルド（配布用 ZIP の作成）

`scripts/build-release.ps1` が配布対象の全 EXE プロジェクトを `publish` → ZIP 化し、`release/` フォルダに集約します。バージョン番号は `Directory.Build.props` から自動取得されます。

**VSCode から実行**

- `Ctrl+Shift+B` で既定タスク「Release Build」を起動
- もしくはコマンドパレット `Ctrl+Shift+P` → `Tasks: Run Task` → 以下から選択:
  - **Release Build**：フレームワーク依存（配布先に .NET 9 Desktop Runtime が必要）
  - **Release Build (Self-Contained)**：ランタイム同梱（配布先に .NET 不要・サイズ大）
  - **Release Build (Skip Clean)**：前回の publish を再利用して差分のみ更新（動作確認用）
  - **Release Clean**：`publish/` と `release/` を削除
  - **dotnet build**：開発用の通常ビルド

**コマンドラインから実行**

```powershell
# フレームワーク依存
.\scripts\build-release.ps1

# 自己完結（ランタイム同梱）
.\scripts\build-release.ps1 -SelfContained

# 差分ビルド（clean スキップ）
.\scripts\build-release.ps1 -SkipClean
```

**生成される配布物** (`release/` 配下)

- `PrecureDataStars.Catalog-v<VERSION>-win-x64.zip`
- `PrecureDataStars.CDAnalyzer-v<VERSION>-win-x64.zip`
- `PrecureDataStars.BDAnalyzer-v<VERSION>-win-x64.zip`
- `PrecureDataStars.LegacyImport-v<VERSION>-win-x64.zip`
- `PrecureDataStars.Episodes-v<VERSION>-win-x64.zip`
- `PrecureDataStars.TitleCharStatsJson-v<VERSION>-win-x64.zip`
- `PrecureDataStars.YouTubeCrawler-v<VERSION>-win-x64.zip`
- `precure-datastars-db-v<VERSION>.zip`（`schema.sql` + `migrations/*`）

スクリプト完走後に画面に表示される「Next steps」に従って、`git tag` → `git push --tags` → GitHub Releases へ `release/*.zip` をアップロード、の流れでリリースしてください。

---

## 主要ワークフロー

### エピソード管理

`PrecureDataStars.Episodes` で、シリーズとエピソードの CRUD、サブタイトルのかな・ルビ編集、パート構成（アバン・OP・A/B パート・ED・予告）の編集を行います。新規エピソード追加後は `PrecureDataStars.TitleCharStatsJson` で文字統計を再計算、`PrecureDataStars.YouTubeCrawler` で YouTube 予告動画 URL を自動補完するのが定型運用です。

### 音楽カタログ登録（v1.1.0 追加）

#### A. CD の登録

1. `PrecureDataStars.CDAnalyzer` を起動し、CD をドライブに挿入。
2. 「読み取り」で TOC・MCN・CD-Text を取得。
3. 「既存ディスクと照合 / 新規登録...」ボタンで `DiscRegistrationService` を通じた優先順（MCN → CDDB-ID → TOC 曖昧）の照合が走り、`DiscMatchDialog` が候補を表示。
4. 候補があれば選択して CD 情報を既存ディスクに反映。なければ「新規登録」を選んで `NewProductDialog` で商品を作成し、品番を入力してディスク＋トラックを一括登録。

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で .mpls/.IFO をロード。
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺による TOC 曖昧のみ）。
3. 反映時は discs テーブルの物理情報が同期され、加えて **`video_chapters` テーブルへチャプター情報が一括登録される**（再読み取り時は「全削除 → 置換」で上書き）。
   - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ。
   - `title` / `part_type` / `notes` は NULL のまま登録されるため、Catalog GUI 側で手動で補完する運用。

#### C. トラックの内容編集（歌・劇伴への紐付け）

1. `PrecureDataStars.Catalog` を起動し、メニューから「ディスク／トラック管理...」を選択。
2. ディスクを選んでトラック一覧を開き、各トラックの **内容種別** を選択する:
   - `SONG`: 親曲（`songs`）→ 録音（`song_recordings`）をドロップダウンで選択。
   - `BGM`: 劇伴 cue（`bgm_cues`）をドロップダウンで選択（1 段）。
   - `DRAMA` / `RADIO` / `LIVE` / `TIE_UP` / `OTHER`: タイトル文字列の上書きだけ行う（録音参照なし）。
3. 歌・劇伴マスタ側の新規作成は「歌マスタ管理...」「劇伴マスタ管理...」メニューから。

#### D. 旧 SQL Server からの移行

1. `PrecureDataStars.LegacyImport` の `App.config` に `LegacyServer` と `TargetMySql` を設定。
2. まず `--dry-run` で件数サマリーを確認:
   ```bash
   dotnet run --project PrecureDataStars.LegacyImport -- --dry-run
   ```
3. 問題なければ通常実行で移行。recording 未特定で OTHER に格下げされたトラックは Catalog GUI で後補正する前提。

---

## データベーススキーマ

DDL ファイル: [`db/schema.sql`](db/schema.sql)（新規構築用、全テーブル含む）
マイグレーション: [`db/migrations/v1.1.0_add_music_catalog.sql`](db/migrations/v1.1.0_add_music_catalog.sql)（v1.0.x → v1.1.0 差分用）

### ER 概要

```
series_kinds ──┐
               ├── series ──┬── episodes ──── episode_parts
series_relation_kinds ──┘   │                      │
                            └── (self-ref)    part_types ──┴── video_chapters
                                 │                              (BD/DVD チャプター)
                                 └─ products ── discs ──┬── tracks ──┬── song_recordings ── songs
                                    (NULL=ALL)          │            │
                                                        │            └── bgm_cues ── bgm_sessions
                                                        │               (M 番号)    (録音セッション)
                                                        │
                                                        └── video_chapters
                                                            (BD/DVD チャプター)

  付随マスタ: product_kinds / disc_kinds / track_content_kinds /
              song_music_classes /
              song_size_variants / song_part_variants / bgm_sessions
```

---

### エピソード系テーブル（v1.0 から変更なし）

> **マスタ系テーブル共通の監査列**
>
> 以下 10 本のマスタ（`series_kinds` / `series_relation_kinds` / `part_types` / `product_kinds` / `disc_kinds` / `track_content_kinds` / `song_music_classes` / `song_size_variants` / `song_part_variants` / `bgm_sessions`）は、すべて次の 4 列を共通して持つ。Catalog GUI の「マスタ管理」タブからレコードを追加・更新した際の履歴を残すことが目的。
>
> | 列名 | 型 | 説明 |
> |---|---|---|
> | `created_at` | TIMESTAMP DEFAULT CURRENT_TIMESTAMP | レコード作成日時（DB が自動付与） |
> | `updated_at` | TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP | レコード最終更新日時（DB が自動付与） |
> | `created_by` | VARCHAR(64) NULL | レコード作成者（Catalog GUI は `Environment.UserName` を設定） |
> | `updated_by` | VARCHAR(64) NULL | レコード最終更新者（同上） |
>
> 下記の各マスタ定義表では監査列の記載を省略する（共通列として本節を参照）。Model 側では `CreatedBy` / `UpdatedBy` だけを公開し、`CreatedAt` / `UpdatedAt` は DB 任せ。

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

---

### 音楽・映像カタログ系テーブル（v1.1.0 追加）

#### `product_kinds` — 商品種別マスタ

販売単位としての商品分類。旧 SQL Server 版の 3 文字コード (Drm/ImA/ImS/Liv/Nov/OES/OST/Rdo/TUp/VoA/VoB) に基本対応する 11 区分を、さらに 2 区分（後期主題歌シングル、映画 OST）に細分した 13 区分 +「その他」で構成する。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード（例: `DRAMA`, `CHARA_ALBUM`, `OST`, `THEME_SINGLE`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**（旧区分との対応）:

| kind_code | name_ja | 旧コード | 細分条件 |
|---|---|---|---|
| `DRAMA` | ドラマ | Drm | — |
| `CHARA_ALBUM` | キャラクターアルバム | ImA | — |
| `CHARA_SINGLE` | キャラクターシングル | ImS | — |
| `LIVE_ALBUM` | ライブアルバム | Liv | — |
| `LIVE_NOVELTY` | ライブ特典スペシャルCD | Nov | — |
| `THEME_SINGLE` | 主題歌シングル | OES | 下記以外 |
| `THEME_SINGLE_LATE` | 後期主題歌シングル | OES | 所属シリーズ `kind_code='TV'` かつ 発売日 ≥ 放送開始年の 6/1 |
| `OST` | オリジナル・サウンドトラック | OST | 下記以外 |
| `OST_MOVIE` | 映画オリジナル・サウンドトラック | OST | 所属シリーズ `kind_code ∈ {MOVIE, SPRING}` |
| `RADIO` | ラジオ | Rdo | — |
| `TIE_UP` | タイアップアーティスト | TUp | — |
| `VOCAL_ALBUM` | ボーカルアルバム | VoA | — |
| `VOCAL_BEST` | ボーカルベスト | VoB | — |
| `OTHER` | その他 | (上記以外) | — |

※ 細分条件の判定は LegacyImport による自動投入時にのみ適用される。Catalog GUI 上で手動編集する場合は任意のコードを選択可能。

#### `disc_kinds` — ディスク種別マスタ

物理形状ではなく、商品内でのディスクの用途区分（本編・特典・ボーナス等）。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード（例: `MAIN`, `BONUS`, `KARAOKE`, `INSTRUMENTAL`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: なし。旧 SQL Server 版に対応する概念が無く、LegacyImport でも値を決定できないため、運用開始時に Catalog GUI の「マスタ管理」→「ディスク種別」タブから必要なコードだけを登録する設計とする。`discs.disc_kind_code` は NULL 許容 FK のため、未登録のまま運用しても既存データは破綻しない。

#### `track_content_kinds` — トラック内容種別マスタ

トラックが何を収録しているかを区別する。`tracks.content_kind_code` の値として使用され、SONG/BGM 時は録音への参照が必須となる（CHECK 制約）。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `SONG`（歌）, `BGM`（劇伴）, `DRAMA`（ドラマ）, `RADIO`（ラジオ）, `LIVE`（ライブ音源／songs マスタ非登録）, `TIE_UP`（タイアップ音源／songs マスタ非登録）, `OTHER`（その他）。

> `LIVE` / `TIE_UP` は「出自としてライブ録音／タイアップだが、曲として個別に `songs` マスタで管理しない」トラック向けの区分で、`song_recording_id` は持たない（`DRAMA` / `RADIO` / `OTHER` と同じく録音参照なしのグループ）。旧 DB の `tracks.track_class = 'Live' / 'TieUp'` が LegacyImport によりこれらにマップされる。

#### `song_music_classes` — 曲の音楽種別マスタ

曲の作品内における役割区分。OP 主題歌・ED 主題歌・挿入歌・キャラクターソング・イメージソング等。

| 列名 | 型 | 説明 |
|---|---|---|
| `class_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `OP`, `ED`, `INSERT`, `CHARA`, `IMAGE`, `MOVIE`, `OTHER`。

#### `song_size_variants` — 曲のサイズ種別マスタ

歌トラック（tracks.content_kind_code='SONG'）のサイズ区分。フルサイズ・TV サイズ・映画サイズ等。1 トラックは `(song_recording_id, song_size_variant_code, song_part_variant_code)` の 3 軸で一意に特定される軸のひとつ。

| 列名 | 型 | 説明 |
|---|---|---|
| `variant_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**（旧 `tracks.song_size` との対応）:

| variant_code | name_ja | 旧値 |
|---|---|---|
| `FULL` | フルサイズ | `Full` |
| `TV` | TVサイズ | `TV` |
| `TV_V1` | TVサイズ歌詞1番 | `TV1番` |
| `TV_V2` | TVサイズ歌詞2番 | `TV2番` |
| `TV_TYPE_I` | TVサイズ Type.I | `TV Type.Ⅰ` |
| `TV_TYPE_II` | TVサイズ Type.II | `TV Type.Ⅱ` |
| `TV_TYPE_III` | TVサイズ Type.III | `TV Type.Ⅲ` |
| `TV_TYPE_IV` | TVサイズ Type.IV | `TV Type.Ⅳ` |
| `TV_TYPE_V` | TVサイズ Type.V | `TV Type.Ⅴ` |
| `SHORT` | ショート | `Short` |
| `MOVIE` | 映画サイズ | `Movie` |
| `LIVE_EDIT` | LIVE Edit Ver. | `LIVE Edit Ver.` |
| `MOV_1` | 第1楽章 | `Mov.1` |
| `MOV_3` | 第3楽章 | `Mov.3` |
| `OTHER` | その他 | — |

#### `song_part_variants` — 曲のパート種別マスタ

歌トラックのパート（ボーカル／カラオケ／コーラス入り／ガイドメロディ入り）区分。サイズ種別と直交する軸で、1 トラックは `(song_recording_id, song_size_variant_code, song_part_variant_code)` の 3 軸で一意に特定される。

| 列名 | 型 | 説明 |
|---|---|---|
| `variant_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**（旧 `tracks.song_type` との対応）:

| variant_code | name_ja | 旧値 |
|---|---|---|
| `VOCAL` | 歌入り | NULL |
| `INST` | オリジナル・カラオケ | `Inst` |
| `INST_STR` | ストリングス入りオリジナル・メロディ・カラオケ | `Inst+Str` |
| `INST_GUIDE` | オリジナル・メロディ・カラオケ | `Inst+Guide` |
| `INST_CHO` | コーラス入りオリジナル・カラオケ | `Inst+Cho` |
| `INST_CHO_GUIDE` | コーラス入りオリジナル・メロディ・カラオケ | `Inst+Cho+Guide` |
| `INST_PART_VO` | パート歌入りオリジナル・カラオケ | `Inst+PartVo` |
| `OTHER` | その他 | — |

#### `products` — 商品

販売単位としての商品。価格・発売日・販売元などの「商品メタ情報」を管理する。複数枚組の場合も 1 商品として扱い、ディスクは `discs` 側で品番単位に分割される。`series_id` が NULL のときはオールスターズ扱い。

| 列名 | 型 | 説明 |
|---|---|---|
| `product_catalog_no` | VARCHAR(32) PK | 代表品番（1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目の catalog_no） |
| `title` | VARCHAR(255) | 商品タイトル（日本語） |
| `title_short` | VARCHAR(128) NULL | 略称 |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = オールスターズ扱い、ON DELETE SET NULL） |
| `product_kind_code` | VARCHAR(32) FK | 商品種別（→ `product_kinds`） |
| `release_date` | DATE | 発売日 |
| `price_ex_tax` | INT NULL | 税抜価格（円） |
| `price_inc_tax` | INT NULL | 税込価格（円） |
| `disc_count` | TINYINT UNSIGNED DEFAULT 1 | ディスク枚数（複数枚組は 2 以上） |
| `manufacturer` | VARCHAR(64) NULL | 発売元 |
| `distributor` | VARCHAR(64) NULL | 販売元 |
| `label` | VARCHAR(64) NULL | レーベル |
| `amazon_asin` | VARCHAR(16) NULL | Amazon ASIN |
| `apple_album_id` | VARCHAR(32) NULL | Apple Music Album ID |
| `spotify_album_id` | VARCHAR(32) NULL | Spotify Album ID |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**CHECK 制約:**
- `ck_products_disc_count_pos`: `disc_count ≥ 1`
- `ck_products_price_ex_nonneg` / `ck_products_price_inc_nonneg`: 価格は NULL または 0 以上

#### `discs` — 物理ディスク

1 枚のディスクを表す。主キーは **品番** (`catalog_no`)。複数枚組の場合は各ディスクが別品番を持ち、同じ `product_catalog_no`（代表品番）に紐付く。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK | 品番（例: `COCX-12345`） |
| `product_catalog_no` | VARCHAR(32) FK | 所属商品の代表品番（→ `products`、CASCADE） |
| `title` | VARCHAR(255) NULL | ディスクタイトル（複数枚組の各ディスクで異なる場合に使用） |
| `title_short` | VARCHAR(128) NULL | 略称 |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
| `disc_no_in_set` | INT UNSIGNED NULL | 組中位置（単品は NULL、複数枚組は 1/2/3...） |
| `disc_kind_code` | VARCHAR(32) FK NULL | ディスク種別（→ `disc_kinds`） |
| `media_format` | ENUM DEFAULT 'CD' | `CD` / `CD_ROM` / `DVD` / `BD` / `DL` / `OTHER` |
| `mcn` | VARCHAR(13) NULL | Media Catalog Number（= JAN/EAN バーコード。CDAnalyzer で取得） |
| `total_tracks` | TINYINT UNSIGNED NULL | 総トラック数（CD-DA 用） |
| `total_length_frames` | INT UNSIGNED NULL | 総尺（1 フレーム = 1/75 秒、CD-DA 基準。BD/DVD も換算して格納） |
| `num_chapters` | SMALLINT UNSIGNED NULL | チャプター総数（BD/DVD 用、CD も便宜的に使用可） |
| `volume_label` | VARCHAR(64) NULL | ボリュームラベル（BD/DVD のディスクラベル） |
| `cd_text_album_title` / `_performer` / `_songwriter` / `_composer` / `_arranger` / `_message` | VARCHAR NULL | CD-Text のアルバム単位情報（CD のみ） |
| `cd_text_disc_id` | VARCHAR(32) NULL | CD-Text Disc ID |
| `cd_text_genre` | VARCHAR(64) NULL | CD-Text Genre |
| `cddb_disc_id` | CHAR(8) NULL | freedb 互換 Disc ID（CDAnalyzer が TOC から算出） |
| `musicbrainz_disc_id` | VARCHAR(32) NULL | MusicBrainz Disc ID |
| `last_read_at` | DATETIME NULL | 最終読み取り日時（CD/BD/DVD Analyzer で更新） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**UNIQUE 制約**: `(product_catalog_no, disc_no_in_set)` — 同一商品内で組中位置は重複不可
**CHECK 制約:**
- `ck_discs_disc_no_pos`: `disc_no_in_set` は NULL または 1 以上
- `ck_discs_total_tracks_nonneg` / `ck_discs_total_length_nonneg` / `ck_discs_num_chapters_nonneg`: 各数値は NULL または 0 以上

#### `tracks` — 物理トラック

ディスク上の 1 トラックまたは 1 チャプター。**主キーは `(catalog_no, track_no, sub_order)` の 3 列複合**。通常のトラックは `sub_order = 0` の 1 行のみで表現し、1 トラックに複数の曲・BGM が入っているケース（メドレー、ボーナストラックの複数曲構成、BGM の前後半分割等）では、同じ `track_no` の下に `sub_order = 1, 2, ...` を追加して複数行で表す。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK(1) FK | 所属ディスク（→ `discs`、CASCADE） |
| `track_no` | TINYINT UNSIGNED PK(2) | トラック番号（1 始まり） |
| `sub_order` | TINYINT UNSIGNED PK(3) DEFAULT 0 | トラック内順序。通常は 0、1 トラック内に複数曲があるときのみ 1, 2, ... |
| `content_kind_code` | VARCHAR(32) FK DEFAULT 'OTHER' | 内容種別（→ `track_content_kinds`）。同一 `track_no` 内の全 `sub_order` 行で一致していなければならない |
| `song_recording_id` | INT FK NULL | 歌の録音参照（→ `song_recordings`、`SONG` 時のみ NOT NULL、ON DELETE SET NULL） |
| `song_size_variant_code` | VARCHAR(32) FK NULL | 歌トラックのサイズ種別（→ `song_size_variants`、SONG 時のみ） |
| `song_part_variant_code` | VARCHAR(32) FK NULL | 歌トラックのパート種別（→ `song_part_variants`、SONG 時のみ） |
| `bgm_series_id` | INT FK(1) NULL | 劇伴参照の第1列（シリーズ ID、→ `bgm_cues.series_id`、`BGM` 時のみ NOT NULL） |
| `bgm_m_no_detail` | VARCHAR(255) FK(2) NULL | 劇伴参照の第2列（M番号詳細、→ `bgm_cues.m_no_detail`） |
| `track_title_override` | VARCHAR(255) NULL | トラック固有タイトル上書き。ドラマ／ラジオ等の実体マスタを持たないトラックのタイトルに加え、**同じ音源でもディスクごとに異なる表記で収録されることがあるため SONG/BGM でも使用可**。`sub_order>0` の子行でも独自に持てる。 |
| `start_lba` | INT UNSIGNED NULL | 開始 LBA（CD-DA の Logical Block Address）。物理情報は **親行 (sub_order=0) のみ** が保有する |
| `length_frames` | INT UNSIGNED NULL | 尺（フレーム、1 秒 = 75 フレーム）。親行のみ |
| `isrc` | CHAR(12) NULL | International Standard Recording Code。親行のみ |
| `is_data_track` | BOOL DEFAULT 0 | データトラックか（Control bit 2）。親行のみ（子行は 0 固定） |
| `has_pre_emphasis` | BOOL DEFAULT 0 | プリエンファシスあり（Control bit 0）。親行のみ |
| `is_copy_permitted` | BOOL DEFAULT 0 | デジタルコピー許可（Control bit 3）。親行のみ |
| `cd_text_title` | VARCHAR(255) NULL | CD-Text トラックタイトル。親行のみ |
| `cd_text_performer` | VARCHAR(255) NULL | CD-Text トラックアーティスト。親行のみ |
| `notes` | VARCHAR(1024) NULL | 備考（親行・子行それぞれに持てる） |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**劇伴参照は 2 列複合 FK**: `(bgm_series_id, bgm_m_no_detail) → bgm_cues(series_id, m_no_detail)`。2 列はすべて NOT NULL または すべて NULL のどちらか（トリガーで検証）。

**CHECK 制約 / トリガー（排他参照・sub_order ルールの整合性）:**
- `ck_tracks_track_no_pos`: `track_no ≥ 1`
- `ck_tracks_length_nonneg`: 尺は NULL または 0 以上
- **トリガー `trg_tracks_bi_fk_consistency` / `trg_tracks_bu_fk_consistency`**: 以下を違反する行は `SIGNAL SQLSTATE '45000'` で拒否する。
  - `content_kind_code` が `SONG` 以外なのに `song_recording_id` / `song_size_variant_code` / `song_part_variant_code` のいずれかが NOT NULL → 拒否
  - `content_kind_code` が `BGM` 以外なのに BGM 3 列のいずれかが NOT NULL → 拒否
  - `content_kind_code = 'SONG'` なのに `song_recording_id IS NULL`（INSERT 時のみ厳密チェック）
  - `content_kind_code = 'BGM'` なのに BGM 3 列のいずれかが NULL（INSERT 時のみ厳密チェック）
  - `sub_order > 0` の行なのに `start_lba` / `length_frames` / `isrc` / `is_data_track` / `has_pre_emphasis` / `is_copy_permitted` / `cd_text_title` / `cd_text_performer` のいずれかが非 NULL/0 → 拒否
  - 同一 `(catalog_no, track_no)` 内で複数の `sub_order` 行の `content_kind_code` が一致しない → 拒否（SONG と BGM の混在禁止）
- なお、本来は CHECK 制約で記述したかったが、MySQL 8.0 の仕様上 `ON DELETE SET NULL` を持つ FK 列に対して同じ列を参照する CHECK 制約を併置すると Error 3823 で拒否される。そのためトリガーで代替実装している。
- **ダングリング許容**: recording 側削除時は `ON DELETE SET NULL` で SONG/BGM 参照列が NULL に落ちるが、`content_kind_code` は `SONG` / `BGM` のままになる。このケースは BEFORE UPDATE トリガーでも拒否せず許容し、Catalog GUI 側で `content_kind_code` を `OTHER` に落とす運用でカバーする（カスケード削除を通すため）。

#### `songs` — 歌マスタ（メロディ + アレンジ単位）

**1 曲 = メロディ + アレンジ**の単位で管理する歌マスタ。同じメロディでもアレンジが違えば別レコード（例: 「DANZEN! ふたりはプリキュア」と「DANZEN! ふたりはプリキュア Ver. MaxHeart」は別 song）。歌唱者違いは `song_recordings` 側、サイズ／パート（フル/TV/カラオケ 等）は `tracks` 側の `song_size_variant_code` / `song_part_variant_code` で表現する。

| 列名 | 型 | 説明 |
|---|---|---|
| `song_id` | INT PK AUTO_INCREMENT | 曲 ID |
| `title` | VARCHAR(255) | 曲タイトル（派生アレンジ名込み、日本語、utf8mb4_ja_0900_as_cs_ks） |
| `title_kana` | VARCHAR(255) NULL | タイトル読み（ひらがな） |
| `music_class_code` | VARCHAR(32) FK NULL | 音楽種別（→ `song_music_classes`） |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = シリーズ横断、ON DELETE SET NULL） |
| `original_lyricist_name` | VARCHAR(255) NULL | 作詞者 |
| `original_lyricist_name_kana` | VARCHAR(255) NULL | 作詞者読み |
| `original_composer_name` | VARCHAR(255) NULL | 作曲者 |
| `original_composer_name_kana` | VARCHAR(255) NULL | 作曲者読み |
| `arranger_name` | VARCHAR(255) NULL | 編曲者（songs がアレンジ単位になったためここに持つ） |
| `arranger_name_kana` | VARCHAR(255) NULL | 編曲者読み |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

#### `song_recordings` — 歌の歌唱者バージョン

同じ `song_id`（＝メロディ + アレンジで 1 意な親曲）に対する歌唱者違い・バリエーション違いを 1 レコードとして持つ。編曲は songs 側、サイズ/パートは tracks 側に持つため、ここには持たない。

| 列名 | 型 | 説明 |
|---|---|---|
| `song_recording_id` | INT PK AUTO_INCREMENT | 録音 ID |
| `song_id` | INT FK | 親曲（→ `songs`、CASCADE） |
| `singer_name` | VARCHAR(1024) NULL | 歌唱者（複数時は自由な記法を許容するため 1024） |
| `singer_name_kana` | VARCHAR(1024) NULL | 歌唱者読み |
| `variant_label` | VARCHAR(128) NULL | 自由ラベル（歌唱者バリエーションの補助表記、例: 「メリダ Ver.」、派生アレンジの完全タイトル等）。**非空ならディスク／トラック閲覧画面ではこのラベルが単独で表示される**（親曲名との併記はしない）。NULL のときだけ `songs.title` にフォールバックする。 |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

#### `bgm_sessions` — 劇伴の録音セッションマスタ

劇伴の録音セッション（いつ・どういう体制で収録したか）を管理するマスタ。シリーズごとに `session_no` を `1, 2, 3, ...` と採番する。**シリーズ内にセッションが 1 つしか無い場合でも `session_no=1` を付ける**（v1.1.1 の採番 A 案。旧設計の `session_no=0`「未設定」既定セッションは廃止）。将来的に録音日・スタジオ名等の属性を追加するための器として現段階で用意している。

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK(1) FK | 所属シリーズ（→ `series`、ON DELETE RESTRICT） |
| `session_no` | TINYINT UNSIGNED PK(2) DEFAULT 1 | シリーズ内のセッション番号。1 始まりで連続採番 |
| `session_name` | VARCHAR(128) NOT NULL | セッション名（例: `(未設定)`, `1st Recording 2004/03`） |
| `notes` | TEXT NULL | 備考 |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**主キー**: `(series_id, session_no)` — シリーズ × セッション番号で 1 意

> LegacyImport はシリーズ内で旧 `musics.rec_session` 文字列を distinct で拾って `1, 2, ...` と採番する。空文字の行（旧 DB で rec_session 未設定）も 1 件のセッションとして採番され、`session_name` は `"(未設定)"` が入る。

#### `bgm_cues` — 劇伴の音源 1 件 = 1 行

劇伴（BGM）の音源を 1 行単位で管理する。シリーズ × M 番号詳細で 1 意となる **1 テーブル統合モデル**（v1.1.0 ターン C で旧 `bgm_cues` + `bgm_recordings` の二階層構造を廃止して統合）。実世界のヒエラルキーは「シリーズ → 録音セッション → M 番号（枝番含む= 音源）」だが、同一シリーズ内で `m_no_detail` は一意という前提のため、`session_no` は属性として保持する。

`m_no_detail` は旧データ準拠の詳細表記（例: `M01`, `M219a`, `M224 ShortVer A`）をそのまま保存する。枝番を畳んだグループキーは `m_no_class`（例: `M219`, `M224`）に別途格納し、GUI の一覧・ソート用に使える。**枝番のために独自カラムを持たせない**点が重要。

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK(1) FK | 所属シリーズ（→ `series`、ON DELETE RESTRICT） |
| `m_no_detail` | VARCHAR(255) PK(2) | M 番号の詳細表記（例: `M01`, `M219a`, `M224 ShortVer A`） |
| `session_no` | TINYINT UNSIGNED FK DEFAULT 1 | 録音セッション番号（→ `bgm_sessions`、同一シリーズ内で 1 意） |
| `m_no_class` | VARCHAR(64) NULL | グループ化用 M 番号（例: `M219`、インデックス `ix_bgm_cues_class` 付き） |
| `menu_title` | VARCHAR(255) NULL | キューのメニュー名（日本語、utf8mb4_ja_0900_as_cs_ks）。枝番ごとに独立 |
| `composer_name` | VARCHAR(255) NULL | 作曲者（枝番ごとに異なる可能性あり） |
| `composer_name_kana` | VARCHAR(255) NULL | 作曲者読み |
| `arranger_name` | VARCHAR(255) NULL | 編曲者（枝番ごとに異なる可能性あり） |
| `arranger_name_kana` | VARCHAR(255) NULL | 編曲者読み |
| `length_seconds` | SMALLINT UNSIGNED NULL | 尺（秒） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**主キー**: `(series_id, m_no_detail)` — シリーズ × M 番号詳細で 1 意

**インデックス**:
- `ix_bgm_cues_class (series_id, m_no_class)` — 幹番号でのグループ化・ソート
- `ix_bgm_cues_session (series_id, session_no)` — セッション絞り込み

**CHECK 制約:**
- `ck_bgm_cues_length_nonneg`: 尺は NULL または 0 以上

#### `video_chapters` — BD/DVD チャプター

Blu-ray / DVD の物理チャプター情報を格納する表（v1.1.1 追加）。`tracks` が CD-DA の物理トラックを扱うのと対に、`video_chapters` は光学映像ディスク（`discs.media_format IN ('BD','DVD')`）のチャプター専用。BDAnalyzer の MPLS/IFO パース結果がそのまま投入され、再読み取り時は同一 `catalog_no` 配下の行を全削除してから置換する（`VideoChaptersRepository.ReplaceAllForDiscAsync`）。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK(1) FK | 所属ディスクの品番（→ `discs`、ON DELETE CASCADE） |
| `chapter_no` | SMALLINT UNSIGNED PK(2) | シリアルなチャプター番号。1 始まり |
| `title` | VARCHAR(255) NULL | チャプタータイトル（手動入力。読み取り直後は NULL） |
| `part_type` | VARCHAR(32) NULL FK | パート種別（→ `part_types`、AVANT/OPENING/PART_A 等。手動入力で NULL 可） |
| `start_time_ms` | BIGINT UNSIGNED | プレイリスト先頭からの開始時刻（ミリ秒） |
| `duration_ms` | BIGINT UNSIGNED | チャプターの長さ（ミリ秒） |
| `playlist_file` | VARCHAR(128) NULL | パース元のプレイリストファイル名（BD は `00001.mpls` 等、DVD は `VTS_01_0.IFO` 等） |
| `source_kind` | ENUM('MPLS','IFO','MANUAL') NOT NULL | パース元の種別。MPLS = Blu-ray、IFO = DVD、MANUAL = 手動追加 |
| `notes` | VARCHAR(1024) NULL | 備考（手動入力） |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**主キー**: `(catalog_no, chapter_no)` — ディスク × チャプター番号で 1 意

**インデックス**:
- `ix_video_chapters_part_type (part_type)` — OP/ED 等の種別フィルタ用

**CHECK 制約:**
- `ck_video_chapters_chapter_no_pos`: `chapter_no >= 1`

> BDAnalyzer は `title` / `part_type` / `notes` を NULL のまま登録する。運用としては Catalog GUI 側でこれらを順次補完していく想定。再読み取りで置換すると手動補完値が失われる点には注意（物理境界の更新が主目的の場合に使う）。

---

### ディスク照合ロジック

CDAnalyzer / BDAnalyzer の DB 連携では、`DiscRegistrationService.FindCandidatesAsync()` が以下の優先順で既存ディスクを照合する:

1. **MCN 完全一致**（`discs.mcn`）
2. **CDDB Disc ID 完全一致**（`discs.cddb_disc_id`）
3. **TOC 曖昧一致**: 総トラック数完全一致 AND 総尺 ±75 フレーム（≒ ±1 秒）

最上位のキーでヒットした時点で以降の検索は行わない。BD/DVD では MCN / CDDB が取れないため 3 のみでの照合となり、精度が落ちる点に注意。

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

## 変更履歴

### v1.1.0 — 音楽・映像カタログ拡張

**破壊的変更なし**（既存テーブルは触らず、新規テーブルの追加のみ）。

追加:

- **DB**: 音楽・映像カタログ向けテーブル 7 本（`products`, `discs`, `tracks`, `video_chapters`, `songs`, `song_recordings`, `bgm_cues`）と、関連マスタ 7 本（`product_kinds`, `disc_kinds`, `track_content_kinds`, `song_music_classes`, `song_size_variants`, `song_part_variants`, `bgm_sessions`）およびその初期データ
- **マイグレーション**: `db/migrations/v1.1.0_add_music_catalog.sql`（v1.0.x 運用中の DB に冪等に流せる差分 SQL）
- **プロジェクト**: `PrecureDataStars.Catalog`（GUI）、`PrecureDataStars.Catalog.Common`（共通ライブラリ）、`PrecureDataStars.LegacyImport`（旧 SQL Server からの移行コンソール）
- **CDAnalyzer**: DB 連携パネル（MCN → CDDB-ID → TOC 曖昧の優先順で既存ディスク照合、新規商品＋ディスク＋トラック一括登録）
- **BDAnalyzer**: DB 連携パネル（TOC 曖昧のみでのディスクレベル照合・登録、チャプター→トラック自動投入はなし）
- **バージョン管理**: `Directory.Build.props` による `Version=1.1.0` の一元管理

### v1.0.x

エピソード管理機能の初期リリース（シリーズ・エピソード・パート構成、MeCab かな／ルビ、YouTube クローラー、文字統計、CDAnalyzer／BDAnalyzer の読み取り専用版）。

---

## ライセンス

[MIT License](LICENSE) © 2025 Shota (SHOWTIME)