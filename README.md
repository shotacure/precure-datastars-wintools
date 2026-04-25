# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）** を MySQL データベースで管理するためのアプリケーション集です。

> **v1.1.3** — データ入力 UI を大幅に刷新しました。商品とディスクを 1 画面に統合した「商品・ディスク管理」、トラック編集を独立させた「トラック管理」（SONG/BGM のオートコンプリート候補選択付き）、税込価格の自動算出、歌・劇伴の CSV 取り込み、劇伴の仮 M 番号フラグなどを追加しています。詳細は末尾の [変更履歴](#変更履歴) を参照。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data                    … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common          … カタログ GUI 共通（Dialog/Service/CSV Import）
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
    ├── migrations/
    │   ├── v1.1.0_add_music_catalog.sql     … v1.0.x → v1.1.0 差分用
    │   ├── v1.1.1_move_series_id_to_disc.sql … v1.1.0 → v1.1.1 差分用
    │   ├── v1.1.1_fix_length_units.sql      … v1.1.0 → v1.1.1 差分用（長さ単位の是正）
    │   ├── v1.1.2_rename_song_columns.sql   … v1.1.1 → v1.1.2 差分用（songs の original_ 接頭辞撤去）
    │   ├── v1.1.3_add_bgm_temp_flag.sql     … v1.1.2 → v1.1.3 差分用（劇伴の仮 M 番号フラグ追加）
    │   └── cleanup_music_catalog.sql        … カタログ系のデータ全削除ユーティリティ
    └── utilities/
        └── backfill_products_price_inc_tax.sql … 税込価格の発売日ベース自動算出（v1.1.3 追加）
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series, Product, Disc, Track, Song, SongRecording, BgmCue, BgmSession, VideoChapter 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Catalog.Common** | クラスライブラリ | CDAnalyzer / BDAnalyzer / Catalog GUI の 3 つで共有するダイアログ（`DiscMatchDialog`・`NewProductDialog`）と `DiscRegistrationService`（ディスク照合 → 登録ビジネスロジック）に加え、v1.1.3 より歌・劇伴の CSV 取り込みサービス（`SongCsvImportService` / `BgmCueCsvImportService`）と最小 CSV リーダー（`SimpleCsvReader`、UTF-8/カンマ区切り、外部依存なし）を提供する。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインのエピソード管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理 GUI。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、ディスク総尺・トラック尺ともに M:SS.fff で表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）と、5 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類）をメニューから切り替えて使う。 |
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

`db/schema.sql` によりデータベース `precure_datastars` と全テーブル（エピソード系 6 本 + 音楽・映像カタログ系 14 本）が作成されます。スキーマは v1.1.3 時点の最新状態（`discs.series_id` を持ち、`products.series_id` は無い。`songs` の作詞／作曲列は `lyricist_name` / `composer_name` 等の素の命名。`bgm_cues` には仮 M 番号フラグ `is_temp_m_no` がある）。

### 1'. 既存環境からのアップグレード

バージョンごとに用意された差分 SQL を順番に流します（適用済みステップは冪等に無視されます）。

```bash
# v1.0.x → v1.1.0（音楽・映像カタログ系テーブルを追加）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.0_add_music_catalog.sql

# v1.1.0 → v1.1.1 (1/2)：series_id を products から discs へ移設
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_move_series_id_to_disc.sql

# v1.1.0 → v1.1.1 (2/2)：長さ単位の是正（BD/DVD 尺を ms 精度へ、CD の num_chapters を NULL 化）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.1_fix_length_units.sql

# v1.1.1 → v1.1.2：songs テーブルの original_ 接頭辞を撤去（4 カラムの RENAME）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.2_rename_song_columns.sql

# v1.1.2 → v1.1.3：劇伴に仮 M 番号フラグを追加
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.3_add_bgm_temp_flag.sql
```

**v1.1.3 マイグレーションで実施される変更**:

*`v1.1.3_add_bgm_temp_flag.sql`:*

1. `bgm_cues` に `is_temp_m_no TINYINT NOT NULL DEFAULT 0` 列を追加（`notes` の直後）
2. 既存行のうち `m_no_detail` が `_temp_` プレフィックスで始まるものを `is_temp_m_no = 1` にバックフィル
3. 適用後の総件数と仮番号フラグの件数をサマリ出力

`INFORMATION_SCHEMA.COLUMNS` で列の存在を確認してから ALTER する冪等形式。STEP 2 の UPDATE は `m_no_detail LIKE '_temp_%'` のような前方一致を使用するため、MySQL Workbench の Safe Update Mode 下では Error 1175 になります。本マイグレーションは UPDATE の前後でセッション変数 `SQL_SAFE_UPDATES` を退避→無効化→復元する処理を内蔵しているため、Workbench の Preferences を変更することなくそのまま流せます。

**任意: 既存商品の税込価格を一括補完したい場合**

v1.1.3 で `products.price_inc_tax` を発売日と税抜価格から自動算出できるようになりました（音楽・映像ソフト業界の慣例に合わせて切り捨て）。既に税込価格が NULL のレコードを一括で埋めるユーティリティが用意されています。

```bash
mysql -u YOUR_USER -p precure_datastars < db/utilities/backfill_products_price_inc_tax.sql
```

実行すると、税率区分（0% / 3% / 5% / 8% / 10%）別の対象件数が DRY-RUN として最初に表示され、その後 `price_ex_tax IS NOT NULL AND price_inc_tax IS NULL` の行に対して UPDATE が走ります。`price_ex_tax` も NULL の行は対象外（残件数が事後表示で確認できる）。

**v1.1.2 マイグレーションで実施される変更**:

*`v1.1.2_rename_song_columns.sql`:*

`songs` テーブルの作詞／作曲カラム 4 本をリネームし、`arranger_name` と命名を揃える。

| 旧カラム名 | 新カラム名 |
|---|---|
| `original_lyricist_name` | `lyricist_name` |
| `original_lyricist_name_kana` | `lyricist_name_kana` |
| `original_composer_name` | `composer_name` |
| `original_composer_name_kana` | `composer_name_kana` |

MySQL 8.0 の `ALTER TABLE ... RENAME COLUMN` を使用。列型は変わらず、インデックス・外部キーも自動追随するため張り直し不要。各 STEP は `INFORMATION_SCHEMA.COLUMNS` で旧列の存在を確認してから実行するため、再実行しても安全です。

**⚠️ v1.1.2 バイナリは新カラム名を前提に SQL を発行するため、必ず上記マイグレーションを先に流してから v1.1.2 のアプリを起動してください。**

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
- `PrecureDataStars.Episodes-v<VERSION>-win-x64.zip`
- `PrecureDataStars.TitleCharStatsJson-v<VERSION>-win-x64.zip`
- `precure-datastars-db-v<VERSION>.zip`（`schema.sql` + `migrations/*`）

v1.1.2 より `PrecureDataStars.LegacyImport`（旧 SQL Server → MySQL 初期移行専用）と `PrecureDataStars.YouTubeCrawler`（エピソード予告 URL 自動抽出）はリリース ZIP 対象から外しています。コードはリポジトリ内に残しているので、必要になったら `scripts/build-release.ps1` の `$targets` 配列にコメントアウトで残してある行を復活させれば再度配布できます。

スクリプト完走後に画面に表示される「Next steps」に従って、`git tag` → `git push --tags` → GitHub Releases へ `release/*.zip` をアップロード、の流れでリリースしてください。

---

## 主要ワークフロー

### エピソード管理

`PrecureDataStars.Episodes` で、シリーズとエピソードの CRUD、サブタイトルのかな・ルビ編集、パート構成（アバン・OP・A/B パート・ED・予告）の編集を行います。新規エピソード追加後は `PrecureDataStars.TitleCharStatsJson` で文字統計を再計算、`PrecureDataStars.YouTubeCrawler` で YouTube 予告動画 URL を自動補完するのが定型運用です。

### 音楽カタログ登録

#### A. CD の登録

1. `PrecureDataStars.CDAnalyzer` を起動し、CD をドライブに挿入。
2. 「読み取り」で TOC・MCN・CD-Text を取得。
3. 「既存ディスクと照合 / 新規登録...」ボタンで `DiscRegistrationService` を通じた優先順（MCN → CDDB-ID → TOC 曖昧）の照合が走り、`DiscMatchDialog` が候補を表示。`DiscMatchDialog` のアクションは v1.1.3 から 3 通りに増えた:
   - **「選択したディスクに反映」**: TOC 一致した既存ディスクの物理情報のみ更新（タイトル等の Catalog 情報は保全）
   - **「選択したディスクの商品に追加」（v1.1.3 新設）**: 既存の複数枚組商品に新しいディスクを追加するケース。商品本体は新規作成せず、`DiscMatchDialog` のグリッドで対象 BOX のいずれかのディスク（例: Disc 1）を選択した状態で押下する。所属商品が一意に決まるため `ConfirmAttachDialog` で確認・シリーズ継承選択 → 品番候補入りの入力プロンプトで品番確定 → 新ディスクを INSERT。組内番号 (`disc_no_in_set`) は商品配下の全ディスクを品番順に自動再採番、`disc_count` も所属ディスク数 + 1 に自動更新される
   - **「新規商品＋ディスクとして登録」**: 商品もディスクも新規作成。品番入力 → `NewProductDialog` で商品種別・タイトル・シリーズ・発売日等を設定 → ディスク＋トラックを一括登録。**v1.1.1 以降、`NewProductDialog` で選択したシリーズは作成される Product ではなく Disc 側の `series_id` に適用される。**

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で `.mpls` / `.IFO` をロード。
   - **Blu-ray**: `BDMV/PLAYLIST/*.mpls` を指定。ドライブ自動検知は `00000.mpls` → `00001.mpls` の順に探す。
   - **DVD（v1.1.1 推奨）**: **`VIDEO_TS/VIDEO_TS.IFO` を指定**。下記の二段階ルーティングでチャプター一覧を抽出する。ドライブ自動検知も `VIDEO_TS.IFO` を優先する。
   - **DVD（単一 VTS モード）**: `VTS_xx_0.IFO` を直接指定すると、その VTS の先頭 PGC のみを解析する（個別 VTS 確認用。v1.1.0 互換）。
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺 ms ±1 秒による TOC 曖昧のみ）。CD と同様に v1.1.3 から `DiscMatchDialog` のアクションが 3 通りに増えており、**「既存商品に追加ディスクとして登録」** で BOX 商品の Disc 2 / Disc 3 を後から足す運用が可能（後述の「既存商品への追加ディスク登録フロー」を参照）。
3. 反映時は discs テーブルの物理情報が同期され、加えて `video_chapters` テーブルへチャプター情報が一括登録される（再読み取り時は「全削除 → 置換」で上書き）。
   - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ。
   - `title` / `part_type` / `notes` は NULL のまま登録されるため、Catalog GUI 側で手動で補完する運用。
   - DVD フォルダ全走査モードでは、チャプター番号 (`chapter_no`) はディスク全体で通し番号（1, 2, 3, …）となり、`playlist_file` にはタイトル識別子が入る（VMGI モードでは `Title_01` 等、Per-VTS モードでは `VTS_02` 等）。これにより同一ディスク内でどのチャプターがどのタイトル由来かを区別できる。
   - チャプター開始時刻 (`start_time_ms`) は**タイトル単位の相対時刻**（各タイトルの先頭 = 0ms）として記録される。

##### DVD 解析の二段階ルーティング（v1.1.1）

`VIDEO_TS.IFO` を指定すると以下の優先順で処理される:

1. **VMGI 経路（正攻法、優先）**: `VIDEO_TS.IFO` 先頭の `DVDVIDEO-VMG` シグネチャを確認後、**TT_SRPT** (Title Search Pointer Table、offset `0xC4`) を読んで論理タイトル一覧を取得する。各タイトルについて対応 `VTS_NN_0.IFO` の **VTS_PTT_SRPT** (Part-of-Title Search Pointer Table、offset `0xC8`) から `(PgcNo, PgmNo)` ペアをチャプターごとに解決し、該当 PGC の Program 尺リストから各チャプターの再生時間を組み立てる。DVD プレイヤーがユーザーに見せる「タイトル/チャプター」構造と完全一致する。
2. **Per-VTS 経路（フォールバック）**: VMGI が読めない／TT_SRPT が壊れているディスク向け。物理 `VTS_NN_0.IFO` を全走査し、各 VTS の最長 PGC を「その VTS のタイトル本編」とみなして拾う（v1.1.0 の挙動相当）。通常は VMGI 経路が成功するため発火しないが、オーサリング破損ディスクのサルベージ用として維持。

ListView のヘッダに `(DVD VMGI, hardlinked)` のように現在のスキャンモードと UDF ハードリンクの有無が表示される。

##### ゴミチャプター・ダミー VTS のフィルタ

いずれのモードでも、以下の 3 段階フィルタでノイズを除外する:

| # | フィルタ対象 | しきい値 | 適用モード | 判断基準 |
|---|---|---|---|---|
| 1 | VTS 全体 | 最長 PGC < 5 秒 | Per-VTS のみ | メニュー/初期化用ダミー VTS を丸ごと除外（VMGI モードでは論理タイトルをそのまま信じる） |
| 2 | ゼロ尺チャプター | duration < 1 ms | 両モード | 空 Cell や PGC 終端プレースホルダを全て除外 |
| 3 | 境界の極短チャプター | duration < 500 ms かつ 先頭または末尾 | 両モード | 黒画面 1 フレームやナビゲーション用ダミー Cell。**中央部の短チャプターは保持**（本編中のスポンサー表示やアイキャッチを誤削しないため） |
| 4 | 重複タイトル | 同一 `(VtsNo, PTT列)` シグネチャが 2 回目以降 | VMGI のみ | ARccOS 系の anti-rip 保護や、99 個のフィラータイトルが全部同じ PGC を指している構造の除去。Title_18 以外の 98 個が全部同じ PGC→同じ 1 Program を指すような異常構造で、実コンテンツを浮上させる |

フィルタで除外されたチャプター数は ListView 末尾の「除外」行にサマリとして表示される。

##### タイトル/チャプターのチェック選択（v1.1.1）

フィルタ 1〜4 で除去しきれない「ユーザー視点で明らかに不要なタイトル」（オーディオコメンタリ、未使用ダミー、先頭のアバン部分だけ削りたい等）を手動で除外できるよう、ListView の各行にチェックボックスがついている。

- **デフォルトで全行チェック済み**（何もしなければ従来通りの全件登録）
- **タイトル行のチェックを外す** → 配下チャプター全てが連動して外れる
- **チャプター行を個別に外す** → 親タイトル行は配下のチェック状態の OR（1 つでも残っていればチェック維持）
- **除外行（集計表示）** のチェックは機能しない（触っても自動で false に戻る）
- 「既存ディスクと照合 / 新規登録...」押下時に、チェックが残っているチャプターだけが `video_chapters` に投入される。`chapter_no` は投入対象のみで 1 から再採番される
- `discs.num_chapters` と `discs.total_length_ms` も絞り込み後の値で計算し直して登録する。`total_length_ms` の集計ルール（合計 vs 最大）はロード直後と同じ判定を使う

この機能は DVD の VMGI / Per-VTS フォルダ走査に加え、単一 VTS モード・BD の MPLS モードでも統一的に動作する（BD/単一 VTS の場合はチェックを触らずにそのまま登録すれば従来挙動）。

##### ディスク総尺の集約ロジック（v1.1.1）

`discs.total_length_ms` に格納する「ディスク全体の尺」は、タイトル数と UDF ハードリンクの検出結果で切り替える:

| 条件 | 集約方法 | 根拠 |
|---|---|---|
| タイトル 1 個 | 単純にそのタイトルの尺 | 場合分け不要 |
| タイトル複数 + VOB ハードリンク検出 | **最長タイトルの尺** | 同じ実データを別角度で複数ナビゲーションから見せている構造。合計すると水増しされる |
| タイトル複数 + VOB 独立 | **全タイトル尺の合計** | 真に独立した多話収録。合計が本当のディスク全体尺 |

UDF ハードリンクの検出は、`VTS_*_1.VOB` のバイト数が全て同一かどうかで判定する（同一なら実体 1 本を複数 VTS で共有している）。

#### C. トラックの内容編集（歌・劇伴への紐付け）

1. `PrecureDataStars.Catalog` を起動し、メニューから「トラック管理...」を選択（v1.1.3 で「ディスク／トラック管理」から名称変更）。
2. ディスクを選んでトラック一覧を開き、各トラックの **内容種別** を選択する:
   - `SONG`: 「曲名・作詞作曲で検索」テキストボックスに 2 文字以上を入力すると、曲名／かな／作詞者／作曲者／編曲者を横断した部分一致で候補リストが更新される（v1.1.3、250 ms デバウンス）。候補から親曲を選ぶと、その曲に紐づく `song_recordings` が「歌唱者バージョン」コンボに自動ロードされる。サイズ種別・パート種別は別コンボで指定。
   - `BGM`: シリーズコンボで絞り込み（未指定なら全シリーズ横断）、「M番号・メニュー名で検索」テキストボックスで `m_no_detail` / `m_no_class` / `menu_title` / 作曲者 / 編曲者 を横断検索。候補は既定で実番号のみ。「仮番号を候補に含める」チェックで `_temp_...` の仮 M 番号行も候補入りする。
   - `DRAMA` / `RADIO` / `LIVE` / `TIE_UP` / `OTHER`: タイトル文字列の上書きだけ行う（録音参照なし）。
3. **ディスクのシリーズ所属** は、メニュー「商品・ディスク管理...」（v1.1.3 で「商品管理」と「ディスク／トラック管理」のディスク編集機能を統合した新フォーム）のディスク詳細エリアにある「シリーズ」コンボから変更できる（v1.1.1 追加）。先頭の「(オールスターズ)」を選ぶと `series_id = NULL` として保存される。
4. 歌・劇伴マスタ側の新規作成は「歌マスタ管理...」「劇伴マスタ管理...」メニューから。v1.1.3 より両画面とも CSV 一括取り込み機能を搭載（後述）。

#### C''. 商品・ディスク管理画面（v1.1.3 新設）

`PrecureDataStars.Catalog` のメニュー「商品・ディスク管理...」は、商品 1 件と所属ディスク群を 1 画面で編集する統合エディタです。旧 `ProductsEditorForm`（商品管理）と、`DiscsEditorForm`（ディスク／トラック管理）のうちディスク詳細編集パートを 1 画面に統合したもの。トラック編集は「トラック管理...」（C 節参照）に分離されました。

**画面構成**

- **検索バー**（最上部）: 検索キーワード（品番／タイトル／略称／英語タイトルに部分一致）、検索・再読込ボタン
- **左ペイン**: 商品一覧。**発売日昇順、同一日内は代表品番昇順**で並ぶ（v1.1.3 で並び順を変更。過去から時系列に入力していく運用に合わせるため）。表示カラムは「発売日 / 品番 / タイトル / 種別 / 税込 / 枚数」と翻訳値のみで、内部コードは出さない。
- **右上ペイン**: 商品詳細。代表品番・タイトル・略称・英語タイトル・商品種別・発売日・税抜価格・**税込価格＋自動計算ボタン**・ディスク枚数・発売元・販売元・レーベル・Amazon ASIN・Apple Album ID・Spotify Album ID・備考。
- **右下ペイン**: 所属ディスク。
  - 上半分: ディスク一覧（組内番号・品番・ディスクタイトル・メディア）
  - 下半分: ディスク詳細編集（品番・組内番号・ディスクタイトル・略称・英語タイトル・シリーズ・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考）

**税込価格の自動計算**（v1.1.3 新設）

商品詳細の税込価格欄の隣にある「自動計算」ボタンを押すと、**税抜価格と発売日から日本の標準消費税率を切り捨てで適用** して税込価格を埋めます。書籍・音楽・映像ソフト業界における実務慣例（端数切り捨て）に合わせています。

| 発売日 | 適用税率 |
|---|---|
| 〜 1989-03-31 | 0%（消費税制度導入前。税抜＝税込） |
| 1989-04-01 〜 1997-03-31 | 3% |
| 1997-04-01 〜 2014-03-31 | 5% |
| 2014-04-01 〜 2019-09-30 | 8% |
| 2019-10-01 〜 | 10% |

商品保存時にも、税込価格が空で税抜価格が入っている場合は同じロジックで自動補完します（明示的に 0 を保存したい場合はそのまま 0 で `price_inc_tax = NULL` として登録される挙動）。既存レコードの一括補完は `db/utilities/backfill_products_price_inc_tax.sql`（前述）で行えます。

#### B'. 既存商品への追加ディスク登録フロー（v1.1.3 新設）

CDAnalyzer / BDAnalyzer から、既に登録済みの商品に対して **新しいディスクだけを追加登録** するフロー。BOX 商品で先に Disc 1 だけ登録しておき、後から Disc 2 / Disc 3 を流し込んでいく運用や、特典 CD・特典 DVD を本編商品にぶら下げて登録するケースを想定しています。

**起動経路（v1.1.3 で 1 画面完結に簡素化）**

1. CDAnalyzer / BDAnalyzer で対象ディスクを読み取り、「既存ディスクと照合 / 新規登録...」を押す
2. `DiscMatchDialog` のグリッド（自動照合候補 or 手動検索結果）から、**追加先 BOX に既に登録されているディスクを 1 つ選択**（例: BOX の Disc 1）
3. **「選択したディスクの商品に追加」** ボタンを押下（ディスク未選択時は Disabled）
4. **`ConfirmAttachDialog`** が開き、商品情報・所属ディスク・シリーズコンボ・**新ディスクの品番入力**（次の品番候補が初期値・全選択状態で入る）を 1 画面で確認 → 「追加して登録」で完了

商品検索も品番別ダイアログも無くなり、ディスク選択 → 確認・品番修正 → 完了の 3 ステップで登録できる。

**`ConfirmAttachDialog` の操作**

- **商品情報（読み取り専用）**: 代表品番 / タイトル / 発売日 / 現在の枚数 を表示
- **所属ディスクのプレビュー**: その商品に既に登録されているディスクが下段グリッドにプレビュー表示される（組内番号 / 品番 / タイトル / メディア / `series_id`）
- **組内番号は自動再採番**: 組内番号 (`disc_no_in_set`) はユーザーに選ばせず、登録時に商品配下の全ディスクを品番昇順（`StringComparison.Ordinal`）でソートし、1, 2, 3, … と振り直す。既存ディスクの組内番号が 1 始まりでなかったり歯抜けだったりしても、本操作を契機にきれいに整列される
- **シリーズの継承**: シリーズコンボの先頭は **「(既存ディスクから継承)」** が既定で選択されており、所属ディスクの先頭の `series_id` を自動採用する。ラベル右側に「（継承元: 〇〇）」と継承元シリーズ名が表示される。「(オールスターズ)」「(任意のシリーズ)」を選んで上書きも可能
- **品番入力**: 「新ディスク品番」テキストボックスに、商品配下で品番昇順末尾のディスク品番末尾を +1 した値が初期値・全選択状態で入る。例: `KICA-1234` → `KICA-1235`、`KICA-9999` → `KICA-10000`。元の桁数を維持してゼロパディング（`007` → `008`）。末尾が数字でない品番（`BIBA-12345A` など）は元の値をそのまま提示。ユーザーは桁修正だけで Enter 確定できる（AcceptButton = btnAttach）。空欄での確定はブロック
- **タイトル候補の自動計算**: 同様に起動時、所属ディスクの先頭の `Title`（非空）を `InheritedDiscTitle` プロパティに格納する。呼び出し側 (CDAnalyzer / BDAnalyzer) が新ディスクの `Title` 初期値として上書きする（CD-Text や VolumeLabel 由来の暫定タイトルを正規タイトルで置き換える狙い）。継承元が空のときは読み取り側既定値を維持

`AttachToProductDialog`（v1.1.3 中盤までの暫定実装、商品検索 UI 付き）は撤去された。商品検索を使いたいユーザー向けの代替手段は、Catalog GUI の「商品・ディスク管理」画面でディスクを直接編集する経路。

**確定後の登録処理**

「追加して登録」ボタン押下で `DiscRegistrationService.AttachDiscToExistingProductAsync` が呼ばれ、次の順序で DB 更新:

1. 指定の `productCatalogNo` で `Product` を取得（無ければ例外）
2. 新ディスクの `product_catalog_no` を既存商品に固定
3. **既存ディスク + 新ディスクを品番昇順にソートし、1 始まり連番で再採番**。既存ディスクのうち採番値が変わるものは `DiscsRepository.UpdateDiscNoInSetAsync` で `disc_no_in_set` のみ更新（タイトル等の他カラムは保全）。新ディスクには連番上の自分のスロット番号を設定
4. 既存所属ディスク数 + 1 を `Product.disc_count` に反映して `Products.UpdateAsync`
5. 新ディスク本体を `DiscsRepository.UpsertAsync`
6. CD ならトラック群、BD/DVD ならチャプター群を一括登録

MySQL のオートコミット動作のため、各ステップは個別に確定します。`CreateProductAndCommitAsync` と同じ実装方針で、トランザクション境界は呼び出し側の責任とせず、運用上は順次コミット前提です（途中で失敗した場合は手動修復）。

**設計上の注意点**

- 商品の `disc_count` は「現在の所属ディスク数 + 1」で算出するため、再採番後の連番の終端と一致する
- 品番ソートのキー比較は `StringComparison.Ordinal`（プリキュア BD/DVD/CD は「アルファベット 4 文字 + ハイフン + 数字 4-5 桁」フォーマットが大半で、単純な ASCII 順序が自然順と一致する）
- `UpdateDiscNoInSetAsync` は採番値が変わる行に対してのみ発行されるため、既に正しい連番になっている既存ディスクへの無駄な UPDATE は走らない
- 編集系コンボはすべて短縮名 (`title_short`) 優先表示の設計に統一されているため、`ConfirmAttachDialog` のシリーズコンボも同じく `title_short` 優先（無ければ `title`）

#### C'. ディスク・トラック閲覧画面（読み取り専用、v1.1.2 改）

`PrecureDataStars.Catalog` のメニュー「ディスク・トラック閲覧」は、ディスク → トラックを翻訳済みの表示値で一覧する参照専用ビューです。編集は一切行いません。

**画面構成**

- **ツールバー**（最上部）: 検索キーワード（品番 / タイトル / シリーズ名に部分一致）、シリーズ絞り込みドロップダウン、再読込ボタン、件数表示
- **ディスク一覧**（上段、SplitContainer）
- **トラック一覧**（下段、SplitContainer。ディスク選択に応じて更新）

v1.1.2 より外周に 10 px の余白を設け、上下ペインの分割バーも若干太めに取って視覚的な窮屈さを解消しています。

**ディスク一覧のカラム**（左から順）

| カラム | 幅 | 内容 |
|---|---|---|
| 品番 | 110 | `discs.catalog_no` |
| タイトル | Fill | `discs.title` を優先、無ければ所属商品タイトル |
| シリーズ | 140 | シリーズの略称（無ければ正式名） |
| 商品種別 | 100 | `product_kinds.name_ja`（翻訳値） |
| メディア | 70 | `discs.media_format`（CD/BD/DVD 等） |
| 発売日 | 100 | 所属商品の `release_date`。`yyyy-MM-dd` 表記 |
| 枚数 | 70 | 2 枚組以上のときのみ **`n / m`** 形式で表示。単品は空欄（v1.1.2 改。従来は「組中」「枚数」を 2 カラムで並べていた） |
| トラック数 | 75 | `discs.total_tracks`（v1.1.2 で「曲数」から改称） |
| 総尺 | 95 | **M:SS.fff 形式**（v1.1.2 新設）。CD は `total_length_frames` (1/75 秒) から、BD/DVD は `total_length_ms` から算出。どちらも NULL なら `—` |

v1.1.2 以前は末尾に MCN（バーコード）カラムがありましたが、閲覧時のノイズでしかないため撤去しました（MCN は `DiscsEditorForm` のディスク詳細エリアで閲覧・編集できます）。

**トラック一覧のカラム**（左から順）

| カラム | 幅 | 内容 |
|---|---|---|
| # | 52 | トラック番号。sub_order=0 は `"24"` のように番号のみ、sub_order&gt;=1 の行（主に歌の重ね録り別バージョン等）は `"24-2"` / `"24-3"` のように枝番を付加（右寄せ。v1.1.2 改） |
| 種別 | 70 | `track_content_kinds.name_ja` |
| タイトル | 220 | 下記「タイトル解決・BGM 集約ルール」参照（v1.1.2 で幅縮小） |
| アーティスト | 180 | SONG は歌唱者→CD-Text、BGM は **空欄**（v1.1.2 改。作曲/編曲は別カラムに分離したため）、その他は CD-Text |
| 作詞 | 110 | SONG は `songs.lyricist_name`、BGM/その他は空欄（v1.1.2 新設） |
| 作曲 | 110 | SONG は `songs.composer_name`、BGM は `bgm_cues.composer_name`、その他は空欄（v1.1.2 新設） |
| 編曲 | 110 | SONG は `songs.arranger_name`、BGM は `bgm_cues.arranger_name`、その他は空欄（v1.1.2 新設） |
| 尺 | 90 | M:SS.fff（右寄せ）。length_frames があれば 1/75 秒精度で算出、無ければ BGM cue の秒数にフォールバック |
| 備考 | Fill | `tracks.notes` |

v1.1.2 以前にあった `ISRC` カラムは参照頻度が低いため撤去しました（ISRC は `DiscsEditorForm` のトラック詳細で閲覧・編集できます）。

**タイトル解決・BGM 集約ルール**

閲覧画面のタイトル列は、内容種別と sub_order 行の有無で以下のように組み立てられます。BGM 以外の集約は行いません。

- **SONG**: `track_title_override` → (`variant_label` または親曲名) + ` [サイズ]` + ` [パート]` → `cd_text_title` の順
- **BGM（単独 sub_order 行）**: 主タイトル（`track_title_override` → `cd_text_title` → `menu_title` → `m_no_detail` の優先順）に、必ず `(m_no_detail [menu_title])` の注釈を後置する
  - 例: `track_title_override = "決戦のテーマ"` / `m_no_detail = "M220b Rhythm Cut"` / `menu_title = "戦闘・危機一髪"` のとき表示は `決戦のテーマ (M220b Rhythm Cut [戦闘・危機一髪])`
  - `menu_title` が NULL のときは `{主タイトル} (m_no_detail)` のみ
  - `bgm_cues` の JOIN が外れた（FK 切れ等）場合は注釈部を付けず主タイトルのみ
  - **`bgm_cues.is_temp_m_no = 1` の行（仮 M 番号、v1.1.3 追加）は閲覧 UI で `m_no_detail` を非表示**: 主タイトルのフォールバック候補からも、注釈の `(m_no_detail [menu_title])` 部分からも除外される（`menu_title` 単独になる、または注釈ごと省略される）。マスタメンテ画面（劇伴マスタ管理）では引き続き `m_no_detail` を素のまま表示・編集できる
- **BGM（同一 track_no で sub_order が複数ある、いわゆるメドレー構成の場合）**（v1.1.2 追加）: sub_order 全行を **1 行に集約**し、主タイトルは sub_order=0 行のものを採用。注釈部には全 sub_order 行の `m_no_detail [menu_title]` を ` + ` 区切りで連結する
  - 例: sub_order=0 が `M84(スローテンポ) [危機]`、sub_order=1 が `M84(アップテンポ) [危機]` のとき、1 行にまとめて `手ごわい相手 (M84(スローテンポ) [危機] + M84(アップテンポ) [危機])` と表示
  - 集約時の作詞/作曲/編曲・尺・備考・アーティストは sub_order=0 行のものを採用（通常は同一セッション内で作曲者も同じだが、異なる場合でも子行は隠れる）
- **DRAMA / RADIO / LIVE / TIE_UP / OTHER**: `track_title_override` → `cd_text_title`。sub_order 複数行がある場合は集約せず別行で表示し、`#` に枝番（`24-2` 等）を付ける

**尺整形ルール**（トラック・ディスク総尺で共通）

- `length_frames`（CD-DA、1/75 秒）があれば: 秒 + ミリ秒（1 フレーム = 1000/75 ≒ 13.333 ms、丸めで 1000 ms 到達時は秒を 1 繰り上げ）
- `length_frames` が無く `length_seconds` / `total_length_ms` があれば: そのミリ秒値または秒値（ミリ秒値は `.000` 固定）
- どれも無ければ: `—`

#### C'''. 歌マスタ管理画面（v1.1.3 改）

`PrecureDataStars.Catalog` のメニュー「歌マスタ管理...」で、`songs`（メロディ + アレンジ単位の曲マスタ）と `song_recordings`（歌唱者バージョン）の 2 階層を編集します。

**画面構成**

- **検索バー**（最上部）: タイトル／かなの部分一致テキスト、シリーズ絞り込み、音楽種別絞り込み、検索ボタン、**CSV取り込みボタン**（v1.1.3 追加）
- **上段**: 左に曲一覧、右に曲詳細（タイトル・かな・音楽種別コンボ・シリーズコンボ・作詞名・作詞名かな・作曲名・作曲名かな・編曲名・編曲名かな・備考）
- **下段左**: 選択中曲の歌唱者バージョン一覧 / バージョン詳細（歌手名・歌手名かな・バリエーションラベル・備考）
- **下段右**: 選択中バージョンの収録ディスク・トラック一覧（読み取り専用）

**入力補完**（v1.1.3 追加）

作詞・作曲・編曲・歌手のテキストボックス（およびそれぞれのかな欄）に、`AutoCompleteSource.CustomSource` で既存マスタのユニーク氏名一覧を注入しています。`AutoCompleteMode.SuggestAppend` により、1 文字目から候補ドロップダウンが表示され、Tab / Enter で確定できます。候補のロードはフォーム起動時と CSV 取り込み完了直後に行われ、新しく登録した氏名もすぐに候補に乗ります。

**CSV 一括取り込み**（v1.1.3 追加）

「CSV取り込み...」ボタンでファイル選択ダイアログが開き、選択後は次の 2 段階で進みます:

1. **ドライラン**: 実書き込みは行わず、行数集計（新規／更新／スキップ）と警告メッセージ（最初の 10 件）を確認ダイアログで表示
2. **本実行**: 「はい」で確定すると同じパースで UPSERT。既存判定は `(title, series_id, arranger_name)` の三要素キー（同名の曲でも編曲が違えば別行）

CSV ヘッダ仕様（UTF-8、カンマ区切り、ヘッダ行必須、ダブルクォート囲み可）:

```csv
title,title_kana,music_class_code,series_title_short,lyricist_name,lyricist_name_kana,composer_name,composer_name_kana,arranger_name,arranger_name_kana,notes
```

| 列 | 必須 | 解釈 |
|---|---|---|
| `title` | ◯ | 空ならスキップ＋警告 |
| `title_kana` |  | そのまま格納 |
| `music_class_code` |  | `song_music_classes.class_code` に存在しなければ NULL に退避＋警告 |
| `series_title_short` |  | `series.title_short` 完全一致 → `series.title` 部分一致の順で解決。未解決時は `series_id = NULL`（オールスターズ扱い）＋警告 |
| `lyricist_name` 〜 `arranger_name_kana` |  | そのまま格納 |
| `notes` |  | そのまま格納 |

サンプルは `docs/csv-templates/songs_import_sample.csv` を同梱しています。

#### C''''. 劇伴マスタ管理画面（v1.1.3 改）

「劇伴マスタ管理...」メニューで、`bgm_cues`（劇伴の音源 1 件 = 1 行、複合 PK `(series_id, m_no_detail)`）と関連 `bgm_sessions` を編集します。

**画面構成**

- **検索バー**（最上部）: シリーズフィルタ、セッションフィルタ、検索キーワード、検索ボタン、**CSV取り込みボタン**（v1.1.3 追加）
- **中段**: 左に劇伴一覧、右に詳細（シリーズ・セッション・M番号詳細・M番号分類・メニュー名・作曲者・作曲者かな・編曲者・編曲者かな・尺(秒)・**仮 M 番号フラグ**・**仮番号を採番ボタン**・備考）
- **下段**: 選択中キューの収録ディスク・トラック一覧（読み取り専用）

**仮 M 番号フラグ（`is_temp_m_no`、v1.1.3 新設）**

M 番号が判明していない劇伴音源は、内部的に `_temp_034108` のような暫定 PK を `m_no_detail` に入れて管理する運用があります。`is_temp_m_no` カラムでこの「仮番号運用中」を明示することで、画面ごとに表示挙動を切り替えています。

| 画面 | 仮番号行の扱い |
|---|---|
| 劇伴マスタ管理（本画面） | チェックボックスとして可視化、`m_no_detail` は素のまま表示・編集可。判明したら実番号にリネーム＋フラグを 0 に戻す運用 |
| ディスク・トラック閲覧 | `m_no_detail` を非表示にし、フォールバック候補からも注釈からも除外 |
| トラック管理の BGM 候補リスト | 既定で除外。「仮番号を候補に含める」チェックで明示的に含められる |

**仮番号採番ボタン**: 「仮番号を採番」を押すと、編集中シリーズ配下の既存 `_temp_NNNNNN` 連番から次の値（6 桁ゼロ埋め）を自動生成して `m_no_detail` フィールドに投入し、フラグもオンになります。既存連番に欠番があっても詰めず、最大値 + 1 を返します（採番アルゴリズムは `BgmCuesRepository.GenerateNextTempMNoAsync`）。

**CSV 一括取り込み**（v1.1.3 追加）

歌マスタ同様、ドライラン → 本実行の 2 段階。`session_name` がシリーズ内で未登録なら自動採番（既存最大 `session_no` + 1）して `bgm_sessions` を新規作成します。`m_no_detail` が空欄でも `is_temp_m_no` フラグが立っていれば `_temp_NNNNNN` を自動採番してインサートします（フラグが偽で空欄の行はスキップ＋警告）。

CSV ヘッダ仕様:

```csv
series_title_short,m_no_detail,session_name,m_no_class,menu_title,composer_name,composer_name_kana,arranger_name,arranger_name_kana,length_seconds,is_temp_m_no,notes
```

| 列 | 必須 | 解釈 |
|---|---|---|
| `series_title_short` | ◯ | 未解決時は行スキップ＋警告 |
| `m_no_detail` | △ | 空欄かつ `is_temp_m_no=1` なら自動採番、それ以外で空欄ならスキップ＋警告 |
| `session_name` |  | 未登録なら同シリーズ内で自動採番して新規作成 |
| `length_seconds` |  | 数値化できなければ NULL＋警告 |
| `is_temp_m_no` |  | `1` / `true` / `yes` / `y` / `t`（大小無視）を真、それ以外を偽。既定は偽 |
| その他 |  | そのまま格納 |

サンプルは `docs/csv-templates/bgm_cues_import_sample.csv` を同梱しています。

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
- [`db/migrations/v1.1.1_move_series_id_to_disc.sql`](db/migrations/v1.1.1_move_series_id_to_disc.sql)（v1.1.0 → v1.1.1 差分用：series_id の所在移設）
- [`db/migrations/v1.1.1_fix_length_units.sql`](db/migrations/v1.1.1_fix_length_units.sql)（v1.1.0 → v1.1.1 差分用：長さ単位の是正）
- [`db/migrations/v1.1.2_rename_song_columns.sql`](db/migrations/v1.1.2_rename_song_columns.sql)（v1.1.1 → v1.1.2 差分用：songs の original_ 接頭辞撤去）
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

> **v1.1.3 補足**:
> - 「商品・ディスク管理」画面の商品一覧の既定並び順は `release_date ASC, product_catalog_no ASC`（発売日昇順、同日内は代表品番昇順）。`ProductsRepository.GetAllAsync` の挙動が変更されたため、もし旧仕様の発売日降順が必要な照合系コード（`DiscMatchDialog` など）から呼び出す場合は新設された `GetAllDescAsync` を使う。
> - `price_inc_tax` は同画面の「自動計算」ボタン、もしくは商品保存時の自動補完で発売日と税抜価格から切り捨てで算出される。既存レコードの一括補完は `db/utilities/backfill_products_price_inc_tax.sql` を実行する。

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
| `lyricist_name` / `_kana` | VARCHAR(255) NULL | 作詞者（v1.1.2 で `original_` 接頭辞を撤去） |
| `composer_name` / `_kana` | VARCHAR(255) NULL | 作曲者（v1.1.2 で `original_` 接頭辞を撤去） |
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
| `is_temp_m_no` | TINYINT NOT NULL DEFAULT 0 | **仮 M 番号フラグ**（v1.1.3 追加）。`m_no_detail` が `_temp_034108` のような内部管理用のダミー番号であることを示す。1 のとき閲覧 UI では `m_no_detail` を非表示にし、マスタメンテ画面ではチェックボックスとして可視化する |
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

### v1.1.3 — データ入力 UI の大幅刷新

**DB に小規模なスキーマ変更あり**（`bgm_cues` への列追加 1 本のみ。既存列は無変更で後方互換）。

主な変更点は次の 5 系統:

#### (1) Catalog GUI の編集フォーム再編

- 旧「商品管理」と旧「ディスク／トラック管理」の 2 メニューを撤去し、以下に再編:
  - **「商品・ディスク管理...」** （`ProductDiscsEditorForm`）: 商品 1 件と所属ディスク群を 1 画面で編集する統合エディタ。左ペインに商品一覧、右上に商品詳細、右下に所属ディスク一覧と詳細を配置。
  - **「トラック管理...」** （`TracksEditorForm`）: トラック編集専用画面。SONG/BGM の紐付けは「検索テキスト → 候補リスト → 選択」のオートコンプリート形式に統一。
- 一覧グリッドはどれも翻訳値（マスタの `name_ja`）のみを表示し、内部コード列は隠蔽。
- 商品一覧の既定並び順を `release_date ASC, product_catalog_no ASC`（発売日昇順、同日内は代表品番昇順）に変更。時系列で過去から埋めていく入力スタイルに合わせる。旧仕様の降順並びは `ProductsRepository.GetAllDescAsync` として残置し、`DiscMatchDialog` 等の照合系から呼び出す。
- 旧 `ProductsEditorForm` / `DiscsEditorForm` のソースは v1.1.3 で撤去。

#### (2) 税込価格の自動算出

- 「商品・ディスク管理」画面の税込価格欄に「自動計算」ボタンを新設。発売日と税抜価格から日本の標準消費税率（0% / 3% / 5% / 8% / 10%）を切り捨てで適用して税込価格を埋める。
- 商品保存時にも同じロジックで自動補完（税込が空かつ税抜が入っている場合のみ）。
- 既存レコードの一括補完用に `db/utilities/backfill_products_price_inc_tax.sql` を新設。実行前に税率区分別の対象件数が DRY-RUN として表示される。

#### (3) トラック管理の SONG/BGM オートコンプリート選択

- SONG: 「曲名・作詞作曲で検索」テキストに 2 文字以上を入力 → `SongsRepository.SearchAsync`（曲名・かな・作詞・作曲・編曲を横断する LIKE 検索）で候補リスト更新 → 選択で親曲確定 → 歌唱者バージョンコンボがその曲の `song_recordings` で再構築される。
- BGM: シリーズ絞り込みコンボ（未指定時は全シリーズ横断）＋「M番号・メニュー名で検索」テキストで `BgmCuesRepository.SearchInSeriesAsync` / `SearchAllSeriesAsync`（`m_no_detail` / `m_no_class` / `menu_title` / `composer_name` / `arranger_name` を横断）→ 候補リスト → 選択で確定。
- `System.Windows.Forms.Timer` で 250 ms のデバウンスを挟んでから DB 問合せを発火。`CancellationTokenSource` で進行中の検索を最新入力でキャンセルし、ちらつき・無駄なクエリを抑制。

#### (4) 劇伴の仮 M 番号フラグ

- `bgm_cues` に列 `is_temp_m_no TINYINT NOT NULL DEFAULT 0` を追加（v1.1.3 マイグレーション）。
- 既存行のうち `m_no_detail` が `_temp_` プレフィックスで始まるものを `is_temp_m_no = 1` にバックフィル。
- 劇伴マスタ管理画面にチェックボックスと「仮番号を採番」ボタン（`BgmCuesRepository.GenerateNextTempMNoAsync` を呼び出して `_temp_NNNNNN` を 6 桁で自動生成）を新設。
- ディスク・トラック閲覧 UI と `TracksRepository.GetBrowserListByCatalogNoAsync` の SQL で、`is_temp_m_no = 1` の行の `m_no_detail` を NULL 化して表示・注釈から除外。
- トラック管理の BGM 候補リストは既定で仮番号行を除外。「仮番号を候補に含める」チェックで明示的に含められる。

#### (5) 歌・劇伴マスタの CSV 一括取り込みと入力補完

- 歌マスタ管理 / 劇伴マスタ管理それぞれに「CSV取り込み...」ボタンを新設。ドライラン → 確認 → 本実行の 2 段階。
- 歌 CSV は `(title, series_id, arranger_name)` を既存判定キーに UPSERT。劇伴 CSV はセッションが未登録なら自動採番で新規作成。仮番号フラグが立っていて `m_no_detail` が空欄の場合は自動採番でインサート。
- `PrecureDataStars.Catalog.Common/CsvImport/` に `SimpleCsvReader`（最小 RFC 4180 準拠リーダー、外部依存なし、UTF-8 BOM 対応、フィールド埋め込み改行対応）と `SongCsvImportService` / `BgmCueCsvImportService` を新設。
- 歌マスタ管理画面の作詞・作曲・編曲・歌手の各テキストボックス（および各かな欄）に既存マスタからユニーク抽出した氏名を `AutoCompleteSource.CustomSource` で注入。`AutoCompleteMode.SuggestAppend` で 1 文字目から候補ドロップダウンが表示される。

#### (6) 既存商品への追加ディスク登録フロー

- `DiscMatchDialog` のアクションを 2 通りから 3 通りに増強。「選択したディスクに反映」「**選択したディスクの商品に追加**」「新規商品＋ディスクとして登録」から選べる。
- 「商品に追加」ボタンはディスク未選択時は Disabled。グリッドのいずれかで行が選ばれると Enabled に切り替わり、押下時には選択ディスクをリポジトリから取り直して `AttachReferenceDisc` プロパティに格納する。
- `ConfirmAttachDialog` を新設（商品検索 UI を持たない確認専用ダイアログ）。商品情報のヘッダ表示・所属ディスクのプレビュー・シリーズの「(既存ディスクから継承)」既定＋上書きコンボのみを提供。
- `DiscRegistrationService.AttachDiscToExistingProductAsync` を新設。既存商品の `disc_count` を「現在の所属ディスク数 + 1」に更新しつつ、新ディスク本体とトラック・チャプターを登録する。
- **組内番号 (`disc_no_in_set`) は自動再採番**。商品配下の既存ディスク + 新ディスクを品番昇順（`StringComparison.Ordinal`）でソートし、1 始まりの連番に振り直す。既存ディスクの組内番号が 1 始まりでなかったり歯抜けだったりしても本操作を契機にきれいに整列される。`DiscsRepository.UpdateDiscNoInSetAsync` を新設し、採番値が変わる行のみピンポイントに UPDATE を発行する（タイトル等の他カラムは保全）。
- **フロー順序を「ディスク選択 → 確認＋品番入力」の 1 画面完結に変更**: `DiscMatchDialog` でユーザーが選んだディスクの `ProductCatalogNo` から所属商品が一意に決まるため、商品検索の追加ステップを廃止。さらに品番入力も `ConfirmAttachDialog` 内に取り込み、商品確認・シリーズ継承選択・品番入力を 1 画面で完結させた。次の品番候補（既存最後尾ディスクの品番末尾を +1 した値）が初期値・全選択状態でテキストボックスに入っており、桁修正だけで Enter 確定できる。
- **ディスクタイトルの初期値継承**: 既存ディスクの先頭 Title が新ディスクの初期値として自動コピーされる（BDAnalyzer / CDAnalyzer 側で `disc.Title` を上書き）。VolumeLabel や CD-Text 由来の暫定タイトルではなく、商品の正規タイトルから書き始められる。
- 用途: BOX 商品で先に Disc 1 だけ登録 → 後から Disc 2 / Disc 3 を追加する運用、特典 CD・特典 DVD を本編商品にぶら下げる運用、複数枚組商品の段階的登録など。
- 閲覧 UI のシリーズ列表示を「正式名（`series.title`）優先 → 短縮名フォールバック」に変更（編集系コンボは従来どおり短縮名優先のまま）。
- `db/utilities/backfill_products_price_inc_tax.sql` も MySQL Workbench の Safe Update Mode 下で UPDATE が拒否されないよう、`SQL_SAFE_UPDATES` の退避→無効化→復元処理を追加。
- 撤去ファイル: `PrecureDataStars.Catalog.Common/Dialogs/AttachToProductDialog.cs` および `.Designer.cs`（v1.1.3 中盤までの暫定実装。`ConfirmAttachDialog` への置き換えに伴い削除）。手動削除が必要。

#### コードレベルの変更（v1.1.3）

- `PrecureDataStars.Data/Models/BgmCue.cs`: `IsTempMNo` プロパティを追加
- `PrecureDataStars.Data/Repositories/BgmCuesRepository.cs`: `SELECT` / `UPSERT` に `is_temp_m_no` 反映、`SearchInSeriesAsync` / `SearchAllSeriesAsync` / `GenerateNextTempMNoAsync` を新設
- `PrecureDataStars.Data/Repositories/ProductsRepository.cs`: `GetAllAsync` の並びを発売日昇順に変更、旧降順を `GetAllDescAsync` として残置。`SearchByTitleAsync` の検索対象列に `product_catalog_no` の LIKE を追加（`09013` → `MJSS-09013` のような品番末尾検索に対応）
- `PrecureDataStars.Data/Repositories/DiscsRepository.cs`: 商品発売日昇順でディスクを返す `GetByProductReleaseOrderAsync` を新設（トラック管理画面のディスク一覧用）。閲覧用 SQL の `SeriesName` を `COALESCE(s.title_short, s.title)` から `COALESCE(s.title, s.title_short)` に反転（正式名優先）。組内番号のみを更新する `UpdateDiscNoInSetAsync` を新設（追加ディスク登録時の品番順自動再採番用）
- `PrecureDataStars.Data/Repositories/TracksRepository.cs`: 閲覧用 SQL で `bc.is_temp_m_no = 1` の `m_no_detail` を NULL 化（フォールバック・注釈ともに連動）
- `PrecureDataStars.Data/Repositories/SongsRepository.cs`: `SearchAsync`（曲名・作詞・作曲・編曲横断）/ `GetCreatorNameCandidatesAsync` を新設
- `PrecureDataStars.Data/Repositories/SongRecordingsRepository.cs`: `GetSingerNameCandidatesAsync` を新設
- `PrecureDataStars.Catalog/Forms/ProductDiscsEditorForm.cs` / `.Designer.cs`: 新規ファイル
- `PrecureDataStars.Catalog/Forms/TracksEditorForm.cs` / `.Designer.cs`: 新規ファイル
- `PrecureDataStars.Catalog/Forms/SongsEditorForm.cs` / `.Designer.cs`: CSV 取り込みボタン、`SetupAutoCompleteAsync()` / `ImportCsvAsync()` 追加
- `PrecureDataStars.Catalog/Forms/BgmCuesEditorForm.cs` / `.Designer.cs`: 仮 M 番号チェックボックス、仮番号採番ボタン、CSV 取り込みボタン、`AssignTempMNoAsync()` / `ImportCsvAsync()` 追加
- `PrecureDataStars.Catalog/MainForm.cs` / `.Designer.cs`: メニュー項目を `mnuProductDiscs` / `mnuTracks` に再配線
- `PrecureDataStars.Catalog.Common/Dialogs/ConfirmAttachDialog.cs` / `.Designer.cs`: 新規ファイル（既存商品への追加ディスク登録の確認専用ダイアログ）。`SuggestedCatalogNo`（次の品番候補）/ `InheritedDiscTitle`（継承タイトル候補）/ `CatalogNo`（ユーザー入力の確定品番、空欄ブロック付き）プロパティを公開。商品確認・シリーズ継承選択・品番入力を 1 画面で完結
- `PrecureDataStars.Catalog.Common/Dialogs/DiscMatchDialog.cs` / `.Designer.cs`: 「選択したディスクの商品に追加」ボタンを追加し、ディスク未選択時は Disabled、選択時に Enabled に切り替え。`WantsAttachToExistingProduct` プロパティに加えて `AttachReferenceDisc`（選択ディスクをリポジトリ最新で取り直したもの）プロパティを公開。選択ディスクの取得処理を `GetActiveSelectedCatalogNo` に共通化
- `PrecureDataStars.Catalog.Common/Services/DiscRegistrationService.cs`: `AttachDiscToExistingProductAsync(productCatalogNo, disc, tracks)` を新設。商品配下の全ディスク（既存 + 新規）を品番昇順で並べて 1 始まり連番に再採番してから登録する
- `PrecureDataStars.Catalog/Forms/DiscBrowserForm.cs`: シリーズフィルタコンボの表示も正式名優先のフォールバックに変更
- `PrecureDataStars.BDAnalyzer/MainForm.cs`: `WantsAttachToExistingProduct` 分岐を追加（既存商品への追加ディスク登録フロー、`ConfirmAttachDialog` を呼び出して品番入力まで 1 画面完結）。DVD 解析時の既定チェックを「総尺最大タイトル + そのチャプターのみ」に変更（特典タイトルが多い DVD で本編以外を 1 つずつ外す手間を解消）
- `PrecureDataStars.CDAnalyzer/MainForm.cs`: 同上（DVD 関連を除く。`WantsAttachToExistingProduct` 分岐のみ追加）
- `db/migrations/v1.1.3_add_bgm_temp_flag.sql`: 列追加 + バックフィル（Workbench Safe Update Mode 下でも安全に流れるよう、UPDATE の前後で `SQL_SAFE_UPDATES` を退避→無効化→復元）
- `db/utilities/backfill_products_price_inc_tax.sql`: 既存商品の税込価格を発売日基準で一括補完（同じく Safe Update Mode 対応済み）

### v1.1.2 — ディスク・トラック閲覧 UI の整理 ＋ songs カラム名整理

**DB スキーマに破壊的変更あり**（`songs` テーブル 4 カラムのリネーム）。アプリ本体は v1.1.1 と API 互換だが、SQL 層で新カラム名を前提にするため、**v1.1.2 のアプリを起動する前に必ず v1.1.2 マイグレーションを適用すること**。主な改善は 3 系統:

1. `Catalog` GUI の閲覧画面（ディスク・トラック閲覧）の表示情報整理と可読性向上
2. `songs` テーブルの作詞／作曲カラムから意味をなさなくなった接頭辞 `original_` を撤去
3. 配布 ZIP 対象から `LegacyImport` / `YouTubeCrawler` を除外（コードはリポジトリ内に残置、必要時は build スクリプトで復帰可能）

#### songs テーブルのカラム名整理

旧スキーマで `original_lyricist_name` / `original_composer_name` と付けていたのは、かつて「カバー版では別の作詞作曲者フィールドを持つ」案を検討した際の名残。現行の「同一メロディでもアレンジ違いなら別 songs 行として持つ」設計では意味をなしておらず、すでに他の列（`arranger_name` 等）と命名が噛み合っていなかった。v1.1.2 でこれを解消し、`songs` は `lyricist_name` / `composer_name` / `arranger_name` の素直な命名に統一。

変更:

- **DB スキーマ**（`songs` テーブル 4 カラムのリネーム、列型・インデックス・FK は不変）:
  - `original_lyricist_name` → `lyricist_name`
  - `original_lyricist_name_kana` → `lyricist_name_kana`
  - `original_composer_name` → `composer_name`
  - `original_composer_name_kana` → `composer_name_kana`
- **マイグレーション**: `db/migrations/v1.1.2_rename_song_columns.sql`（各 STEP は `INFORMATION_SCHEMA.COLUMNS` で旧列の存在を確認してから `ALTER TABLE ... RENAME COLUMN` を実行する冪等形式）
- **モデル** (`Song.cs`): `OriginalLyricistName` / `OriginalLyricistNameKana` / `OriginalComposerName` / `OriginalComposerNameKana` の 4 プロパティを `LyricistName` / `LyricistNameKana` / `ComposerName` / `ComposerNameKana` にリネーム
- **リポジトリ**:
  - `SongsRepository`: SELECT / INSERT / UPDATE の SQL とパラメータ名を新名に更新
  - `TracksRepository.GetBrowserListByCatalogNoAsync`: v1.1.2 で新設した閲覧用 SQL の `sg.original_lyricist_name` / `sg.original_composer_name` 参照も新カラム名に同期
- **UI** (`SongsEditorForm`): 画面値の読み取りブロックと保存ブロックのプロパティ参照を更新
- **LegacyImport**: 旧 SQL Server の作詞／作曲カラムから新 `Song` モデルに流し込む箇所のプロパティ名を更新

#### ディスク・トラック閲覧フォームの一覧 UI 刷新

- **ディスク一覧カラム**:
  - `MCN` カラムを撤去（閲覧時のノイズ。`DiscsEditorForm` で引き続き閲覧・編集可）
  - `組中`／`枚数` の 2 カラムを 1 カラムに統合し、2 枚組以上のときのみ `n / m` 形式で表示。単品は空欄
  - `曲数` カラムを `トラック数` にリネーム（CD 以外でも指す語に合わせる）
  - `総尺` カラムを新設（M:SS.fff 形式）。CD は `discs.total_length_frames`（1/75 秒）から、BD/DVD は `discs.total_length_ms` から算出
- **トラック一覧カラム**:
  - `タイトル` 列の幅を 320 → 220 に縮小（Fill 解除、代わりに `備考` を Fill）
  - `作詞`／`作曲`／`編曲` の独立カラムを新設
    - SONG: `songs.lyricist_name` / `songs.composer_name` / `songs.arranger_name`
    - BGM:  作詞は空欄、作曲は `bgm_cues.composer_name`、編曲は `bgm_cues.arranger_name`
    - その他: いずれも空欄
  - `ISRC` カラムを撤去（参照頻度が低い。`DiscsEditorForm` のトラック詳細で閲覧・編集可）
  - `アーティスト` 列は BGM 行で空欄になる（作曲/編曲を別カラムに分離したため）
- **劇伴トラックのタイトル表示形式を刷新**:
  - 従来: `menu_title`（または `m_no_detail`）単独表示
  - v1.1.2 単独 sub_order 行: `{主タイトル} (m_no_detail [menu_title])` 形式で常に M 番号注釈を後置
    - 主タイトル = `track_title_override` → `cd_text_title` → `menu_title` → `m_no_detail` の優先順
    - `menu_title` が NULL の場合は `{主タイトル} (m_no_detail)` のみ
    - `bgm_cues` の JOIN が外れた場合は注釈なしで主タイトルのみ
  - v1.1.2 複数 sub_order 行（メドレー）: **1 行に集約**し、注釈部に全 sub_order の `m_no_detail [menu_title]` を ` + ` 区切りで連結
    - 例: `手ごわい相手 (M84(スローテンポ) [危機] + M84(アップテンポ) [危機])`
    - 集約時の属性（作詞/作曲/編曲/尺/備考/アーティスト）は sub_order=0 行のものを採用
- **SONG 等の sub_order 枝番表示**（v1.1.2 追加）: BGM 以外で同一 track_no に sub_order &gt;= 1 の行がある場合は集約せず個別行として残し、`#` 列に `"{TrackNo}-{SubOrder+1}"` 形式の枝番を付加。例：sub_order=0 は `"24"`、sub_order=1 は `"24-2"`、sub_order=2 は `"24-3"`
- **レイアウト改善**:
  - グリッド群を包む外周 `Panel` を新設し、10 px の Padding でウインドウ端との余白を確保
  - `SplitContainer` の分割バー幅を 6 px に拡大
  - トラック見出しラベルの高さ / Padding を調整し、ラベルと下のグリッドの間に視覚的な間を確保
  - フォームの既定サイズを 1100×680 → 1180×700 に拡大（追加カラム分）

#### コードレベルの変更（閲覧 UI 系）

- **`DiscBrowserRow`** (DiscsRepository.cs): 計算プロパティ `DiscCountDisplay` / `TotalLengthDisplay` を追加（DB 列は変更なし、Dapper のマッピング対象外の get-only プロパティ）
- **`TrackBrowserRow`** (TracksRepository.cs): `Lyricist` / `Composer` / `Arranger` プロパティを追加。加えて BGM 集約用 raw 値 `BgmMNoDetail` / `BgmMenuTitle` と、表示用トラック番号 `TrackNoDisplay` を追加
- **`TracksRepository.GetBrowserListByCatalogNoAsync`**: SQL を書き換え
  - タイトル解決を `COALESCE(track_title_override, CASE..., cd_text_title)` の外側 COALESCE 構造から、`CASE` 内で完結する構造に変更
  - `Artist` の BGM 分岐を `NULL` 固定に
  - 作詞／作曲／編曲の 3 列を SELECT に追加（`songs` / `bgm_cues` の既存 LEFT JOIN を再利用）
  - BGM タイトルは「M 番号注釈を含まないベース部分」のみ返すよう簡素化。注釈の付与および sub_order 集約は `DiscBrowserForm` 側（C#）で行う。併せて `bc.m_no_detail` / `bc.menu_title` を raw 値として追加 SELECT
- **`DiscBrowserForm.BuildDisplayRows`** (新設): 生の DB 行を track_no でグルーピングし、BGM 複数 sub_order 行の集約、BGM 単独行の注釈付与、非 BGM の sub_order 枝番付与を行う整形レイヤー
- **`DiscBrowserForm.SetupGridColumns`**: 新カラム配置に合わせて全面書き換え。`#` 列は `TrackNo` 直バインドではなく `TrackNoDisplay` 文字列バインドに変更
- **`DiscBrowserForm.Designer.cs`**: 外周パネル `pnlBody` を新設、Padding・SplitterWidth・ClientSize を調整

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

#### (3) DVD の多話収録構造・複雑ナビゲーション構造への対応

v1.1.0 までの DVD 解析は「指定された 1 個の VTS_xx_0.IFO の先頭 PGC だけを読む」設計だったため、多話収録 DVD（ダミー VTS_01 + 本編 VTS_02〜VTS_NN 分散型）では本編をまったく取り逃がしていた。また、1 本の実データに対して複数ナビゲーションを提供する UDF ハードリンク構造（論理タイトル数 ≠ 物理 VTS 数）では、VTS 単位で拾うと論理構造と一致しない。v1.1.1 で VMGI (VIDEO_TS.IFO の TT_SRPT) を直接パースする正攻法ルートに拡張し、DVD プレイヤーが UI に見せる「タイトル/チャプター」構造と完全一致する解析を実現した。

変更:

- **`IfoParser`**: 新 API を追加
  - `ExtractAllPgcsFromVtsIfo(path)`: VTS 内の全 PGC を列挙（従来の `ExtractProgramsFromVtsIfo` は先頭 PGC のみで後方互換維持）
  - `TryReadVmgi(path)`: `VIDEO_TS.IFO` の `DVDVIDEO-VMG` シグネチャ検証と TT_SRPT (offset `0xC4`) パース。論理タイトル一覧 `List<VmgiTitleEntry>` を返す（失敗時 null）
  - `TryReadTitlePttEntries(vtsIfoPath, ttnInVts)`: 指定 VTS の VTS_PTT_SRPT (offset `0xC8`) から、指定 TTN のチャプターを構成する `(PgcNo, PgmNo)` ペアのリストを返す
  - `AreVobsHardlinked(folderPath)`: 全 `VTS_*_1.VOB` のバイト数が一致しているか判定（UDF ハードリンク検出）
  - `ExtractTitlesFromVideoTs(...)`: 二段階ルータに変更。まず VMGI 経路を試し、失敗時に Per-VTS 経路へフォールバック
- **`TitleScanResult`** に `ScanMode`（"VMGI" / "PerVts"）と `VobsHardlinked` を追加
- **`BDAnalyzer/MainForm`**:
  - `LoadIfo` を振り分けルータに変更：`VIDEO_TS.IFO` なら `LoadIfoFolderScan`、`VTS_xx_0.IFO` なら `LoadIfoSingleVts`（従来互換）
  - `LoadIfoFolderScan`: タイトル/チャプターを階層表示、除外件数のサマリ行を表示、タイトル単位の相対時刻で `video_chapters` を組み立て。ヘッダにスキャンモードと UDF ハードリンク状態を表示
  - `BuildSnapshot` の `chapterTimings` 要素に `PlaylistTag` を追加（VMGI モードなら `Title_01` 等、Per-VTS モードなら `VTS_02` 等をタイトルごとに格納）
  - ドライブ自動検知の優先順を `VIDEO_TS.IFO` → `VTS_01_0.IFO` に変更（従来は逆で、ダミー VTS_01 を掴んでしまう問題があった）
- **総尺集約ロジック**: `discs.total_length_ms` は「タイトル数」と「VOB ハードリンク有無」で切り替え
  - タイトル 1 つ: そのタイトルの尺
  - タイトル複数 + ハードリンク: **最長タイトルの尺**（重複ナビゲーションの水増し回避）
  - タイトル複数 + 独立 VOB: **全タイトル尺の合計**（真に独立した多話の合算）
- **ListView でのタイトル/チャプター手動選択**: フィルタで除去しきれないユーザー視点のノイズ（オーディオコメンタリ、先頭のダミータイトル等）を手動で外せるよう、各行にチェックボックスを追加。タイトル行と配下チャプター行が連動。登録時にチェック済み行だけが `video_chapters` に投入され、`chapter_no` 再採番・ディスク総尺の再計算も自動
- **フィルタ 4（VMGI モード限定、タイトル重複除外）**: `(VtsNumber, 各チャプターの (PgcNo, PgmNo) 列)` でシグネチャを取り、同一シグネチャを持つ 2 つ目以降のタイトルを自動除外。ARccOS 系 anti-rip 保護で「99 タイトルが全部同じ PGC を指している」構造に対処

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
