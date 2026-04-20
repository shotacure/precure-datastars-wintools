# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）** を MySQL データベースで管理するためのアプリケーション集です。

> **v1.1.1** — `series_id` の所在を `products` から `discs` に移設しました（シリーズ所属は本来ディスクの属性であるため）。詳細は末尾の [変更履歴](#変更履歴) を参照。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data                    … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common          … カタログ GUI 共通（Dialog/Service）
│
├── PrecureDataStars.Episodes                … エピソード管理 GUI（WinForms）
├── PrecureDataStars.Catalog                 … カタログ管理 GUI（WinForms）
├── PrecureDataStars.TitleCharStatsJson      … 文字統計一括再計算（コンソール）
├── PrecureDataStars.YouTubeCrawler          … YouTube URL 自動抽出（コンソール）
├── PrecureDataStars.LegacyImport            … 旧 SQL Server 版 → MySQL 版 移行（コンソール）
│
├── PrecureDataStars.BDAnalyzer              … Blu-ray/DVD チャプター解析（WinForms）＋DB 連携
├── PrecureDataStars.CDAnalyzer              … CD-DA トラック解析（WinForms）＋DB 連携
│
├── Directory.Build.props                    … 全プロジェクト共通の Version・LangVersion
└── db/
    ├── schema.sql                           … MySQL スキーマ定義（DDL、新規構築用）
    └── migrations/
        ├── v1.1.0_add_music_catalog.sql     … v1.0.x → v1.1.0 差分用
        ├── v1.1.1_move_series_id_to_disc.sql … v1.1.0 → v1.1.1 差分用（★ v1.1.1 追加）
        └── cleanup_music_catalog.sql        … カタログ系のデータ全削除ユーティリティ
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series, Product, Disc, Track, Song, SongRecording, BgmCue, BgmSession, VideoChapter 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Catalog.Common** | クラスライブラリ | CDAnalyzer / BDAnalyzer / Catalog GUI の 3 つで共有するダイアログ（`DiscMatchDialog`・`NewProductDialog`）と `DiscRegistrationService`（ディスク照合 → 登録ビジネスロジック）を提供する。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインのエピソード管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理 GUI。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、尺はミリ秒まで表示）と、5 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類）をメニューから切り替えて使う。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.LegacyImport** | コンソール | 旧 SQL Server 版の discs / tracks / songs / musics テーブルから、新 MySQL 版の products / discs / tracks / songs / song_recordings / bgm_cues / bgm_sessions へ移行するバッチ。`--dry-run` オプションで件数サマリーだけの試行運転が可能。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。DVD は VIDEO_TS.IFO を指定するとフォルダ全走査で多話収録 DVD にも対応する（v1.1.1）。DB 連携パネルで既存ディスクとの照合・新規商品登録が可能。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行できる。 |

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

`db/schema.sql` によりデータベース `precure_datastars` と全テーブル（エピソード系 6 本 + 音楽・映像カタログ系 14 本）が作成されます。スキーマは v1.1.1 相当（`discs.series_id` を持ち、`products.series_id` は無い）。

### 1'. 既存環境からのアップグレード

バージョンごとに用意された差分 SQL を順番に流します（適用済みステップは冪等に無視されます）。

```bash
# v1.0.x → v1.1.0（音楽・映像カタログ系テーブルを追加）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.0_add_music_catalog.sql

# v1.1.0 → v1.1.1 (1/2)：series_id を products から discs へ移設
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_move_series_id_to_disc.sql

# v1.1.0 → v1.1.1 (2/2)：長さ単位の是正（BD/DVD 尺を ms 精度へ、CD の num_chapters を NULL 化）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_fix_length_units.sql
```

**v1.1.1 マイグレーションで実施される変更**:

*`v1.1.1_move_series_id_to_disc.sql`:*

1. `discs` に `series_id INT NULL` 列を追加（`title_en` の直後）
2. `discs` に インデックス `ix_discs_series` と外部キー `fk_discs_series`（`ON DELETE SET NULL ON UPDATE CASCADE`）を追加
3. `UPDATE discs d JOIN products p ON d.product_catalog_no = p.product_catalog_no SET d.series_id = p.series_id` で値をコピー
4. `products` から FK `fk_products_series`・インデックス `ix_products_series`・列 `series_id` を撤去

*`v1.1.1_fix_length_units.sql`:*

1. `discs` に `total_length_ms BIGINT UNSIGNED NULL` 列と CHECK 制約 `ck_discs_total_length_ms_nonneg` を追加
2. BD/DVD 既存行: `total_length_ms = total_length_frames * 1000 / 75`（整数除算）で変換し、`total_length_frames` と `total_tracks` を NULL 化
3. CD/CD_ROM 既存行: `num_chapters` を NULL 化（旧仕様では `total_tracks` と同値を冗長格納していた）

いずれのスクリプトも `INFORMATION_SCHEMA` で各オブジェクトの存在を確認してから ALTER するため、再実行しても安全です。

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

### 音楽カタログ登録

#### A. CD の登録

1. `PrecureDataStars.CDAnalyzer` を起動し、CD をドライブに挿入。
2. 「読み取り」で TOC・MCN・CD-Text を取得。
3. 「既存ディスクと照合 / 新規登録...」ボタンで `DiscRegistrationService` を通じた優先順（MCN → CDDB-ID → TOC 曖昧）の照合が走り、`DiscMatchDialog` が候補を表示。
4. 候補があれば選択して CD 情報を既存ディスクに反映。なければ「新規登録」を選んで `NewProductDialog` で商品を作成し、品番を入力してディスク＋トラックを一括登録。**v1.1.1 以降、`NewProductDialog` で選択したシリーズは作成される Product ではなく Disc 側の `series_id` に適用される。**

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で `.mpls` / `.IFO` をロード。
   - **Blu-ray**: `BDMV/PLAYLIST/*.mpls` を指定。ドライブ自動検知は `00000.mpls` → `00001.mpls` の順に探す。
   - **DVD（v1.1.1 推奨）**: **`VIDEO_TS/VIDEO_TS.IFO` を指定**。同フォルダ内の全 `VTS_xx_0.IFO` を自動走査し、各 VTS の最長 PGC をその VTS のタイトル本編として抽出する。ドライブ自動検知も `VIDEO_TS.IFO` を優先する。
   - **DVD（単一 VTS モード）**: `VTS_xx_0.IFO` を直接指定すると、その VTS の先頭 PGC のみを解析する（個別 VTS 確認用。v1.1.0 互換）。
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺 ms ±1 秒による TOC 曖昧のみ）。
3. 反映時は discs テーブルの物理情報が同期され、加えて `video_chapters` テーブルへチャプター情報が一括登録される（再読み取り時は「全削除 → 置換」で上書き）。
   - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ。
   - `title` / `part_type` / `notes` は NULL のまま登録されるため、Catalog GUI 側で手動で補完する運用。
   - DVD フォルダ全走査モードでは、チャプター番号 (`chapter_no`) はディスク全体で通し番号（1, 2, 3, …）となり、`playlist_file` に各タイトルの VTS 識別子（`VTS_02` 形式）が入る。これにより同一ディスク内でどのチャプターがどのタイトル由来かを区別できる。
   - チャプター開始時刻 (`start_time_ms`) は**タイトル単位の相対時刻**（各タイトルの先頭 = 0ms）として記録される。

##### DVD フォルダ全走査のフィルタ

多話収録 DVD では「メニュー用ダミー VTS」「空 Cell」「黒画面 1 フレームのナビゲーション用 Cell」などが混ざるため、v1.1.1 では以下の 3 段階フィルタで除外する:

| # | フィルタ対象 | しきい値 | 判断基準 |
|---|---|---|---|
| 1 | VTS 全体 | 最長 PGC < 5 秒 | メニュー/初期化用ダミー VTS を丸ごと除外 |
| 2 | ゼロ尺チャプター | duration < 1 ms | 空 Cell や PGC 終端プレースホルダを全て除外 |
| 3 | 境界の極短チャプター | duration < 500 ms かつ 先頭または末尾 | 黒画面 1 フレームやナビゲーション用ダミー Cell。**中央部の短チャプターは保持**（本編中のスポンサー表示やアイキャッチを誤削しないため） |

フィルタで除外されたチャプター数は ListView 末尾の「除外」行にサマリとして表示される。

##### ディスク総尺の集約ロジック（v1.1.1）

DVD は UDF ハードリンクによって複数 VTS が同一 VOB を参照している構造（`VTS_02_1.VOB` と `VTS_03_1.VOB` がバイト一致）があるため、全タイトルの尺を単純合計するとハードリンク分だけ水増しされる。逆に真に独立した多話 DVD では合計が本当の総尺。両方で破綻しないよう、`discs.total_length_ms` には **「最長タイトル尺」と「全タイトル尺合計」のうち小さい方** を採用する。

#### C. トラックの内容編集（歌・劇伴への紐付け）

1. `PrecureDataStars.Catalog` を起動し、メニューから「ディスク／トラック管理...」を選択。
2. ディスクを選んでトラック一覧を開き、各トラックの **内容種別** を選択する:
   - `SONG`: 親曲（`songs`）→ 録音（`song_recordings`）をドロップダウンで選択。
   - `BGM`: 劇伴 cue（`bgm_cues`）をドロップダウンで選択（1 段）。
   - `DRAMA` / `RADIO` / `LIVE` / `TIE_UP` / `OTHER`: タイトル文字列の上書きだけ行う（録音参照なし）。
3. **ディスクのシリーズ所属** は、ディスク詳細エリアの「シリーズ」コンボから変更できる（v1.1.1 追加）。先頭の「(オールスターズ)」を選ぶと `series_id = NULL` として保存される。
4. 歌・劇伴マスタ側の新規作成は「歌マスタ管理...」「劇伴マスタ管理...」メニューから。

#### D. 旧 SQL Server からの移行

1. `PrecureDataStars.LegacyImport` の `App.config` に `LegacyServer` と `TargetMySql` を設定。
2. まず `--dry-run` で件数サマリーを確認:
   ```bash
   dotnet run --project PrecureDataStars.LegacyImport -- --dry-run
   ```
3. 問題なければ通常実行で移行。recording 未特定で OTHER に格下げされたトラックは Catalog GUI で後補正する前提。
4. 旧 `discs.series_id` の値は、グループ内の新 `discs.series_id`（複数枚組なら全枚数分）へ同じ値としてコピーされる。新 `products` には `series_id` は載らない。

---

## データベーススキーマ

DDL ファイル: [`db/schema.sql`](db/schema.sql)（新規構築用、全テーブル含む）
マイグレーション:
- [`db/migrations/v1.1.0_add_music_catalog.sql`](db/migrations/v1.1.0_add_music_catalog.sql)（v1.0.x → v1.1.0 差分用）
- [`db/migrations/v1.1.1_move_series_id_to_disc.sql`](db/migrations/v1.1.1_move_series_id_to_disc.sql)（v1.1.0 → v1.1.1 差分用）
- [`db/migrations/cleanup_music_catalog.sql`](db/migrations/cleanup_music_catalog.sql)（カタログ系データ全削除ユーティリティ）

### ER 概要

```
series_kinds ──┐
               ├── series ──┬── episodes ──── episode_parts
series_relation_kinds ──┘   │                      │
                            ├── (self-ref)    part_types
                            │
                            └── discs ── tracks ──┬── song_recordings ── songs
                                ▲       │         │
                                │       │         └── bgm_cues ── bgm_sessions
                                │       │             (M 番号)    (録音セッション)
                                │       │
                                │       └── video_chapters (BD/DVD チャプター)
                                │
                                products
                                (販売単位メタ情報、series_id は持たない)

  付随マスタ: product_kinds / disc_kinds / track_content_kinds /
              song_music_classes / song_size_variants / song_part_variants
```

> **v1.1.1 の所在変更**: シリーズ所属は `products` ではなく `discs` 側の属性になった（上図の `discs ──▲── series` の FK）。1 商品内に複数シリーズのディスクが混在するケースや、1 シリーズに 1 ディスクだけが対応するケースの表現に対応できる構造になった。

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
| `vod_intro` | SMALLINT UNSIGNED NULL | 配信版の東映動画タイトル尺（秒） |
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
| `total_oa_no` | INT UNIQUE NULL | プリキュアシリーズ通算放送回数 |
| `nitiasa_oa_no` | INT UNIQUE NULL | ニチアサ枠通算放送回数 |
| `title_text` | VARCHAR(255) | サブタイトル（プレーンテキスト） |
| `title_rich_html` | TEXT NULL | サブタイトル（ルビ付き HTML） |
| `title_kana` | VARCHAR(255) NULL | サブタイトル読み（ひらがな） |
| `title_char_stats` | JSON NULL | サブタイトルの文字統計 JSON |
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
- `ck_nitiasa_matches`: `nitiasa_oa_no = total_oa_no + 978`
- `ck_series_ep_no_pos` / `ck_total_ep_no_pos` / `ck_total_oa_no_pos` / `ck_nitiasa_oa_no_pos`: 各話数は 1 以上

#### `part_types` — パート種別マスタ

エピソードを構成するパートの種別を定義するマスタテーブル。

| 列名 | 型 | 説明 |
|---|---|---|
| `part_type` | VARCHAR(32) PK | パート種別コード（例: `AVANT`, `PART_A`, `PART_B`, `ED`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

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

---

### 音楽・映像カタログ系テーブル

#### `product_kinds` — 商品種別マスタ

販売単位としての商品分類。

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

※ 細分条件の判定は LegacyImport による自動投入時にのみ適用される。Catalog GUI 上で手動編集する場合は任意のコードを選択可能。細分判定で参照される「所属シリーズ」は、v1.1.1 以降はグループ代表ディスクの `series_id` を用いる（旧 DB は 1 商品 = 1 シリーズの前提のため変わらず機能する）。

#### `disc_kinds` — ディスク種別マスタ

物理形状ではなく、商品内でのディスクの用途区分（本編・特典・ボーナス等）。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード（例: `MAIN`, `BONUS`, `KARAOKE`, `INSTRUMENTAL`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: なし。運用開始時に Catalog GUI の「マスタ管理」→「ディスク種別」タブから必要なコードだけを登録する設計とする。`discs.disc_kind_code` は NULL 許容 FK のため、未登録のまま運用しても既存データは破綻しない。

#### `track_content_kinds` — トラック内容種別マスタ

トラックが何を収録しているかを区別する。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `SONG`（歌）, `BGM`（劇伴）, `DRAMA`（ドラマ）, `RADIO`（ラジオ）, `LIVE`（ライブ音源／songs マスタ非登録）, `TIE_UP`（タイアップ音源／songs マスタ非登録）, `OTHER`（その他）。

#### `song_music_classes` — 曲の音楽種別マスタ

曲の作品内における役割区分。

| 列名 | 型 | 説明 |
|---|---|---|
| `class_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `OP`, `ED`, `INSERT`, `CHARA`, `IMAGE`, `MOVIE`, `OTHER`。

#### `song_size_variants` — 曲のサイズ種別マスタ

歌トラックのサイズ区分。

| 列名 | 型 | 説明 |
|---|---|---|
| `variant_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `FULL` / `TV` / `TV_V1` / `TV_V2` / `TV_TYPE_I` ～ `TV_TYPE_V` / `SHORT` / `MOVIE` / `LIVE_EDIT` / `MOV_1` / `MOV_3` / `OTHER`。

#### `song_part_variants` — 曲のパート種別マスタ

歌トラックのパート（ボーカル／カラオケ／コーラス入り／ガイドメロディ入り）区分。

| 列名 | 型 | 説明 |
|---|---|---|
| `variant_code` | VARCHAR(32) PK | 種別コード |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: `VOCAL` / `INST` / `INST_STR` / `INST_GUIDE` / `INST_CHO` / `INST_CHO_GUIDE` / `INST_PART_VO` / `OTHER`。

#### `products` — 商品

販売単位としての商品。価格・発売日・販売元などの「商品メタ情報」を管理する。複数枚組の場合も 1 商品として扱い、ディスクは `discs` 側で品番単位に分割される。

> **v1.1.1**: 本テーブルから `series_id` 列が撤去された。シリーズ所属は各 `discs` 行の `series_id` で判断する。これは「シリーズごとに 1 枚だけディスクがある」構造、および「シリーズ合同盤でディスクごとに異なるシリーズが紐付く」構造の双方に自然対応するため。

| 列名 | 型 | 説明 |
|---|---|---|
| `product_catalog_no` | VARCHAR(32) PK | 代表品番（1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目の catalog_no） |
| `title` | VARCHAR(255) | 商品タイトル（日本語） |
| `title_short` | VARCHAR(128) NULL | 略称 |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
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

> **v1.1.1**: `series_id` 列を本テーブルに追加した。シリーズ所属はディスクの属性である（同じ商品内でもディスクごとに異なるシリーズを持ち得る）。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK | 品番（例: `COCX-12345`） |
| `product_catalog_no` | VARCHAR(32) FK | 所属商品の代表品番（→ `products`、CASCADE） |
| `title` | VARCHAR(255) NULL | ディスクタイトル（複数枚組の各ディスクで異なる場合に使用） |
| `title_short` | VARCHAR(128) NULL | 略称 |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = オールスターズ扱い、ON DELETE SET NULL、ON UPDATE CASCADE）★ v1.1.1 追加 |
| `disc_no_in_set` | INT UNSIGNED NULL | 組中位置（単品は NULL、複数枚組は 1/2/3...） |
| `disc_kind_code` | VARCHAR(32) FK NULL | ディスク種別（→ `disc_kinds`） |
| `media_format` | ENUM DEFAULT 'CD' | `CD` / `CD_ROM` / `DVD` / `BD` / `DL` / `OTHER` |
| `mcn` | VARCHAR(13) NULL | Media Catalog Number（= JAN/EAN バーコード。CDAnalyzer で取得） |
| `total_tracks` | TINYINT UNSIGNED NULL | 総トラック数（**CD-DA 専用**。BD/DVD では NULL） |
| `total_length_frames` | INT UNSIGNED NULL | 総尺（**CD-DA 専用**、1 フレーム = 1/75 秒。BD/DVD では NULL） |
| `total_length_ms` | BIGINT UNSIGNED NULL | 総尺（**BD/DVD 専用**、ミリ秒精度）★ v1.1.1 追加 |
| `num_chapters` | SMALLINT UNSIGNED NULL | チャプター数（**BD/DVD 専用**。CD-DA には「チャプター」概念がないため NULL） |
| `volume_label` | VARCHAR(64) NULL | ボリュームラベル（BD/DVD のファイルシステム上のラベル） |
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
**外部キー**:
- `fk_discs_product`: `product_catalog_no → products.product_catalog_no`（CASCADE）
- `fk_discs_series`: `series_id → series.series_id`（ON DELETE SET NULL、ON UPDATE CASCADE）★ v1.1.1 追加
- `fk_discs_kind`: `disc_kind_code → disc_kinds.kind_code`

**インデックス**:
- `ix_discs_product (product_catalog_no)`
- `ix_discs_series (series_id)` ★ v1.1.1 追加
- `ix_discs_mcn (mcn)`
- `ix_discs_cddb (cddb_disc_id)`
- `ix_discs_musicbrainz (musicbrainz_disc_id)`

**CHECK 制約:**
- `ck_discs_disc_no_pos`: `disc_no_in_set` は NULL または 1 以上
- `ck_discs_total_tracks_nonneg` / `ck_discs_total_length_nonneg` / `ck_discs_total_length_ms_nonneg` / `ck_discs_num_chapters_nonneg`: 各数値は NULL または 0 以上

**物理同期ポリシー**: `DiscsRepository.UpsertPhysicalInfoAsync`（CDAnalyzer / BDAnalyzer が呼ぶ）は、物理情報（MCN, TOC, CD-Text, CDDB-ID, last_read_at）のみを UPDATE し、`series_id` を含む Catalog 運用情報（title, disc_kind_code, product_catalog_no, series_id, notes 等）は保全する。

#### `tracks` — 物理トラック

ディスク上の 1 トラックまたは 1 チャプター。**主キーは `(catalog_no, track_no, sub_order)` の 3 列複合**。通常のトラックは `sub_order = 0` の 1 行のみで表現し、1 トラックに複数の曲・BGM が入っているケース（メドレー、ボーナストラックの複数曲構成、BGM の前後半分割等）では、同じ `track_no` の下に `sub_order = 1, 2, ...` を追加して複数行で表す。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK(1) FK | 所属ディスク（→ `discs`、CASCADE） |
| `track_no` | TINYINT UNSIGNED PK(2) | トラック番号（1 始まり） |
| `sub_order` | TINYINT UNSIGNED PK(3) DEFAULT 0 | トラック内順序 |
| `content_kind_code` | VARCHAR(32) FK DEFAULT 'OTHER' | 内容種別（→ `track_content_kinds`） |
| `song_recording_id` | INT FK NULL | 歌の録音参照（→ `song_recordings`、`SONG` 時のみ NOT NULL、ON DELETE SET NULL） |
| `song_size_variant_code` | VARCHAR(32) FK NULL | 歌トラックのサイズ種別（→ `song_size_variants`、SONG 時のみ） |
| `song_part_variant_code` | VARCHAR(32) FK NULL | 歌トラックのパート種別（→ `song_part_variants`、SONG 時のみ） |
| `bgm_series_id` | INT FK(1) NULL | 劇伴参照の第1列（シリーズ ID、→ `bgm_cues.series_id`、`BGM` 時のみ NOT NULL） |
| `bgm_m_no_detail` | VARCHAR(255) FK(2) NULL | 劇伴参照の第2列（M番号詳細、→ `bgm_cues.m_no_detail`） |
| `track_title_override` | VARCHAR(255) NULL | トラック固有タイトル上書き |
| `start_lba` | INT UNSIGNED NULL | 開始 LBA（親行のみ） |
| `length_frames` | INT UNSIGNED NULL | 尺（フレーム、親行のみ） |
| `isrc` | CHAR(12) NULL | ISRC（親行のみ） |
| `is_data_track` / `has_pre_emphasis` / `is_copy_permitted` | BOOL DEFAULT 0 | CD フラグ（親行のみ） |
| `cd_text_title` / `cd_text_performer` | VARCHAR(255) NULL | CD-Text トラック情報（親行のみ） |
| `notes` | VARCHAR(1024) NULL | 備考 |

**劇伴参照は 2 列複合 FK**: `(bgm_series_id, bgm_m_no_detail) → bgm_cues(series_id, m_no_detail)`。

**CHECK 制約 / トリガー（排他参照・sub_order ルールの整合性）**: INSERT/UPDATE 時に `trg_tracks_bi_fk_consistency` / `trg_tracks_bu_fk_consistency` トリガーが content_kind 一貫性と sub_order ルールを検証する。

#### `songs` — 歌マスタ（メロディ + アレンジ単位）

| 列名 | 型 | 説明 |
|---|---|---|
| `song_id` | INT PK AUTO_INCREMENT | 曲 ID |
| `title` | VARCHAR(255) | 曲タイトル |
| `title_kana` | VARCHAR(255) NULL | タイトル読み |
| `music_class_code` | VARCHAR(32) FK NULL | 音楽種別（→ `song_music_classes`） |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = シリーズ横断、ON DELETE SET NULL） |
| `original_lyricist_name` / `_kana` | VARCHAR(255) NULL | 作詞者 |
| `original_composer_name` / `_kana` | VARCHAR(255) NULL | 作曲者 |
| `arranger_name` / `_kana` | VARCHAR(255) NULL | 編曲者 |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

#### `song_recordings` — 歌の歌唱者バージョン

| 列名 | 型 | 説明 |
|---|---|---|
| `song_recording_id` | INT PK AUTO_INCREMENT | 録音 ID |
| `song_id` | INT FK | 親曲（→ `songs`、CASCADE） |
| `singer_name` / `singer_name_kana` | VARCHAR(1024) NULL | 歌唱者 |
| `variant_label` | VARCHAR(128) NULL | 自由ラベル（歌唱者バリエーションの補助表記） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

#### `bgm_sessions` — 劇伴の録音セッションマスタ

シリーズごとに `session_no` を `1, 2, 3, ...` と採番する。

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK(1) FK | 所属シリーズ（→ `series`、ON DELETE RESTRICT） |
| `session_no` | TINYINT UNSIGNED PK(2) DEFAULT 1 | シリーズ内のセッション番号 |
| `session_name` | VARCHAR(128) NOT NULL | セッション名 |
| `notes` | TEXT NULL | 備考 |

#### `bgm_cues` — 劇伴の音源 1 件 = 1 行

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK(1) FK | 所属シリーズ（→ `series`、ON DELETE RESTRICT） |
| `m_no_detail` | VARCHAR(255) PK(2) | M 番号の詳細表記 |
| `session_no` | TINYINT UNSIGNED FK DEFAULT 1 | 録音セッション番号（→ `bgm_sessions`） |
| `m_no_class` | VARCHAR(64) NULL | グループ化用 M 番号 |
| `menu_title` | VARCHAR(255) NULL | キューのメニュー名 |
| `composer_name` / `_kana` | VARCHAR(255) NULL | 作曲者 |
| `arranger_name` / `_kana` | VARCHAR(255) NULL | 編曲者 |
| `length_seconds` | SMALLINT UNSIGNED NULL | 尺（秒） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

**インデックス**:
- `ix_bgm_cues_class (series_id, m_no_class)`
- `ix_bgm_cues_session (series_id, session_no)`

#### `video_chapters` — BD/DVD チャプター

Blu-ray / DVD の物理チャプター情報を格納する表。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK(1) FK | 所属ディスクの品番（→ `discs`、CASCADE） |
| `chapter_no` | SMALLINT UNSIGNED PK(2) | チャプター番号 |
| `title` | VARCHAR(255) NULL | チャプタータイトル |
| `part_type` | VARCHAR(32) NULL FK | パート種別（→ `part_types`） |
| `start_time_ms` | BIGINT UNSIGNED | プレイリスト先頭からの開始時刻（ミリ秒） |
| `duration_ms` | BIGINT UNSIGNED | チャプターの長さ（ミリ秒） |
| `playlist_file` | VARCHAR(128) NULL | パース元のプレイリストファイル名 |
| `source_kind` | ENUM('MPLS','IFO','MANUAL') NOT NULL | パース元の種別 |
| `notes` | VARCHAR(1024) NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

**インデックス**: `ix_video_chapters_part_type (part_type)`

---

### ディスク照合ロジック

CDAnalyzer / BDAnalyzer の DB 連携では、v1.1.1 よりメディアごとに専用の照合メソッドが用意されている:

**CD-DA（`DiscRegistrationService.FindCandidatesForCdAsync`）**: 以下の優先順で検索し、最上位のキーでヒットした時点で以降の検索は行わない。

1. **MCN 完全一致**（`discs.mcn`）
2. **CDDB Disc ID 完全一致**（`discs.cddb_disc_id`）
3. **TOC 曖昧一致**: `total_tracks` 完全一致 AND `total_length_frames` ±75 フレーム（≒ ±1 秒）

**BD/DVD（`DiscRegistrationService.FindCandidatesForVideoAsync`）**: MCN / CDDB は取れないため、TOC 曖昧のみ。

- `num_chapters` 完全一致 AND `total_length_ms` ±1000 ms（≒ ±1 秒）

v1.1.0 までは CD/BD/DVD を単一の `FindCandidatesAsync` で兼用し、BD/DVD のチャプター数を `totalTracks` に、総尺を CD-DA の 1/75 秒フレームに換算して詰め込んでいたが、意味論の混乱と尺精度の劣化（ms → 1/75 秒で約 13ms 単位に丸められていた）を解消するため v1.1.1 で分離した。

### シリーズ紐付けの運用

v1.1.1 以降、ディスクのシリーズ所属は以下の経路で設定できる:

1. **新規登録時**: CDAnalyzer / BDAnalyzer → `NewProductDialog` でシリーズを選択。ダイアログの `SelectedSeriesId` が新規作成される `disc.SeriesId` に適用される（`product` 側には設定されない）。
2. **後から編集**: Catalog GUI の「ディスク／トラック管理」画面 → ディスク詳細の「シリーズ」コンボで変更・保存。
3. **LegacyImport**: 旧 `discs.series_id` の値を新 `discs.series_id` へ 1 対 1 でコピー（複数枚組の場合はグループ内全ディスクに同じ値）。

### title_char_stats JSON スキーマ

`TitleCharStatsBuilder.BuildJson()` が生成する JSON の構造:

```json
{
  "norm": "NFKC+jpn-fix+ellipsis",
  "chars": { "カ": 1, "ト": 1, "ピ": 1, "ロ": 1, "本": 1, "立": 1 },
  "length": { "graphemes": 18, "codepoints": 19, "unique_graphemes": 17 },
  "spaces": 1,
  "version": 1,
  "categories": {
    "Emoji": 0, "Kanji": 2, "Latin": 6, "Other": 0, "Punct": 2,
    "Digits": 2, "Symbols": 0, "Hiragana": 2, "Katakana": 4
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

### v1.1.1 — series_id の所在移設 + 長さ単位の是正

**破壊的変更あり**（アプリ・DB 双方にスキーマ変更あり）。このリリースは独立した 2 つの整理を束ねている。

#### (1) series_id の所在を products から discs へ

変更:

- **DB スキーマ**:
  - `products` から `series_id` 列・FK `fk_products_series`・インデックス `ix_products_series` を撤去
  - `discs` に `series_id INT NULL` 列・FK `fk_discs_series`（`ON DELETE SET NULL ON UPDATE CASCADE`）・インデックス `ix_discs_series` を追加
- **マイグレーション**: `db/migrations/v1.1.1_move_series_id_to_disc.sql`
- **モデル**:
  - `Product.SeriesId` プロパティを削除
  - `Disc.SeriesId` プロパティを追加
- **リポジトリ**:
  - `ProductsRepository` の SELECT/INSERT/UPDATE 列から `series_id` を除去、`GetBySeriesAsync` を削除
  - `DiscsRepository` の SELECT/UPSERT 列に `series_id` を追加、新たに `GetBySeriesAsync(int?)` を追加、`GetBrowserListAsync` の JOIN キーを `p.series_id` から `d.series_id` に変更
  - `DiscsRepository.UpsertPhysicalInfoAsync`（物理情報同期専用）は `series_id` を保全対象に含める
- **UI**:
  - `NewProductDialog`: シリーズコンボは残しつつ、選択値を `SelectedSeriesId` プロパティに分離公開。`Result`（Product）には載せない
  - `CDAnalyzer` / `BDAnalyzer`: 新規登録パスで `disc.SeriesId = pdlg.SelectedSeriesId` を実施
  - `ProductsEditorForm`: シリーズ欄を撤去（SeriesRepository 依存も削除）
  - `DiscsEditorForm`: ディスク詳細エリアに「シリーズ」コンボを追加
- **LegacyImport**: 旧 `discs.series_id` を新 `products` ではなく新 `discs` の全枚数分に同値コピー

#### (2) 長さ・チャプター列の意味論整理

v1.1.0 までは `total_length_frames` に BD/DVD 尺も CD-DA の 1/75 秒に換算して詰め込み、`num_chapters` は CD でも冗長に `total_tracks` と同値を格納していた。v1.1.1 でメディア別の列使い分けに整理した。

変更:

- **DB スキーマ**:
  - `discs` に `total_length_ms BIGINT UNSIGNED NULL` 列と CHECK `ck_discs_total_length_ms_nonneg` を追加
  - 各長さ/チャプター列のコメントで「CD-DA 専用」「BD/DVD 専用」を明記
- **マイグレーション**: `db/migrations/v1.1.1_fix_length_units.sql`
  - BD/DVD 既存行: `total_length_ms = total_length_frames * 1000 / 75` で変換、`total_length_frames` と `total_tracks` を NULL 化
  - CD 既存行: `num_chapters` を NULL 化
- **モデル**: `Disc.TotalLengthMs` プロパティ追加、他の長さ/チャプター列の XML ドキュメントで用途メディアを明記
- **リポジトリ**:
  - `DiscsRepository` の SELECT/UPSERT 列に `total_length_ms` を追加
  - `FindByTocFuzzyAsync` を `FindByTocFuzzyForCdAsync` と `FindByTocFuzzyForVideoAsync` に分離
  - `DiscBrowserRow` に `TotalLengthMs` / `NumChapters` を追加、`GetBrowserListAsync` の SELECT も拡張
- **サービス**: `DiscRegistrationService.FindCandidatesAsync` を `FindCandidatesForCdAsync`（MCN/CDDB/TOC 三段）と `FindCandidatesForVideoAsync`（TOC 曖昧のみ、`numChapters + total_length_ms`）に分離
- **UI**:
  - `CDAnalyzer`: disc 作成時に `NumChapters` を埋めるのをやめ、DB 照合は `FindCandidatesForCdAsync` を呼ぶ
  - `BDAnalyzer`: disc 作成時に `TotalLengthFrames` ではなく `TotalLengthMs` を埋め、DB 照合は `FindCandidatesForVideoAsync(numChapters, totalLengthMs)` を呼ぶ。チャプター数を `totalTracks` に詰め替える迂回コードを撤去

#### (3) DVD の多話収録構造への対応

v1.1.0 までの DVD 解析は「指定された 1 個の VTS_xx_0.IFO の先頭 PGC だけを読む」設計だったため、多話収録 DVD（ダミー VTS_01 + 本編 VTS_02〜VTS_NN 分散型）では本編をまったく取り逃がしていた。v1.1.1 で VIDEO_TS フォルダ全走査に拡張し、実用レベルの DVD 対応を実現した。

変更:

- **`IfoParser`**: 新 API を追加
  - `ExtractAllPgcsFromVtsIfo(path)`: VTS 内の全 PGC を列挙（従来の `ExtractProgramsFromVtsIfo` は先頭 PGC のみで後方互換維持）
  - `ExtractTitlesFromVideoTs(folderPath, minVtsDurationSec, minChapterDurationMs, minBoundaryChapterMs)`: VIDEO_TS フォルダを全走査し、各 VTS の最長 PGC を代表タイトルとして抽出。3 段階フィルタ（VTS レベル < 5 秒 / ゼロ尺チャプター / 境界の極短 < 500ms）でゴミ排除
- **`BDAnalyzer/MainForm`**:
  - `LoadIfo` を振り分けルータに変更：`VIDEO_TS.IFO` なら `LoadIfoFolderScan`、`VTS_xx_0.IFO` なら `LoadIfoSingleVts`（従来互換）
  - `LoadIfoFolderScan`: タイトル/チャプターを階層表示、除外件数のサマリ行を表示、タイトル単位の相対時刻で `video_chapters` を組み立て
  - `BuildSnapshot` の `chapterTimings` 要素に `PlaylistTag` を追加（タイトルごとに `VTS_02` 等の識別子を格納）
  - ドライブ自動検知の優先順を `VIDEO_TS.IFO` → `VTS_01_0.IFO` に変更（従来は逆で、ダミー VTS_01 を掴んでしまう問題があった）
- **総尺集約ロジック（A3 案）**: `discs.total_length_ms` は「最長タイトル尺」と「全タイトル尺合計」の min を採用。UDF ハードリンクで VOB 共有している多話 DVD（合計が水増しされる）と独立多話 DVD（合計が本物）の双方に破綻しない

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
