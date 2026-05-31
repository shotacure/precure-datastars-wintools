# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）、クレジット情報（OP / ED の階層構造、人物・企業・キャラクター・プリキュアの各マスタ）を MySQL データベースで管理するためのアプリケーション集です。Web 公開用の静的サイトジェネレータ `PrecureDataStars.SiteBuilder` により、ローカル MySQL の内容を静的 HTML として書き出せます。

> **現行バージョン: v1.4.1**。全バージョンの変更履歴は [`CHANGELOG.md`](CHANGELOG.md) を参照。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common … カタログ GUI 共通（Dialog/Service/CSV Import）
├── PrecureDataStars.TemplateRendering … 役職テンプレ DSL 展開エンジン（共通ライブラリ）
├── PrecureDataStars.AmazonPaApi … Amazon Creators API クライアントライブラリ
│
├── PrecureDataStars.Episodes … エピソード管理 GUI（WinForms）
├── PrecureDataStars.Catalog … カタログ管理 GUI（WinForms）
├── PrecureDataStars.AmazonSync … Creators API ジャケット画像一括取得（コンソール）
│
├── PrecureDataStars.BDAnalyzer … Blu-ray/DVD チャプター解析（WinForms）＋DB 連携
├── PrecureDataStars.CDAnalyzer … CD-DA トラック解析（WinForms）＋DB 連携
│
├── PrecureDataStars.SiteBuilder … Web 公開用静的サイト生成（コンソール）
│
├── Directory.Build.props … 全プロジェクト共通の Version・LangVersion
└── db/
    ├── schema.sql … MySQL スキーマ定義（DDL、新規構築用）
    ├── migrations/ … バージョン別差分 SQL（ファイル名 `v<VERSION>_migration_<topic>.sql`、バージョン昇順に適用）
    └── utilities/
        └── backfill_products_price_inc_tax.sql … 税込価格の発売日ベース自動算出
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series, Product, Disc, Track, Song, SongRecording, BgmCue, BgmSession, VideoChapter 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Catalog.Common** | クラスライブラリ | CDAnalyzer / BDAnalyzer / Catalog GUI で共有するダイアログ（`DiscMatchDialog`・`NewProductDialog`・`ConfirmAttachDialog`）、`DiscRegistrationService`（ディスク照合 → 登録ビジネスロジック）、歌・劇伴の CSV 取り込みサービス（`SongCsvImportService` / `BgmCueCsvImportService`）、最小 CSV リーダー（`SimpleCsvReader`、UTF-8/カンマ区切り、外部依存なし）を提供。 |
| **PrecureDataStars.TemplateRendering** | クラスライブラリ | 役職テンプレ DSL の展開エンジン。Catalog 側プレビュー（`CreditPreviewRenderer`）と SiteBuilder 側 HTML 生成（`CreditTreeRenderer`）の双方から参照される。`TemplateContext` / `TemplateNode` / `TemplateParser` / `RoleTemplateRenderer` / `Handlers/ThemeSongsHandler` と、`LookupCache` 抽象化のための `ILookupCache` インターフェースを保持。`net9.0`（Forms 非依存）構成。 |
| **PrecureDataStars.AmazonPaApi** | クラスライブラリ | Amazon Creators API のクライアントライブラリ。OAuth 2.0 トークン管理（v2.x Cognito / v3.x Login with Amazon の自動切替・キャッシュ）・GetItems・SearchItems・App.config からの Credential ID / Secret / Version 読み出しヘルパを提供。Catalog（商品検索ダイアログと一括画像取得）と AmazonSync コンソールから ProjectReference 経由で参照される。 |
| **PrecureDataStars.Episodes** | WinForms GUI | シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング。 |
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、ディスク総尺・トラック尺は M:SS.fff 表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）と、6 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類・クレジット系マスタ）をメニューから切り替える。クレジット系マスタは 15 タブ構成の `CreditMastersEditorForm`（プリキュア／人物／人物名義／企業／企業屋号／ロゴ／キャラクター／キャラクター名義／キャラクター続柄／家族関係／役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別）。声優キャスティングは `credit_block_entries` の `CHARACTER_VOICE` エントリに一元化。`MusicCreditsMigrationForm` は未マッチング名義一覧 → 人物・名義登録 → 全シリーズ全列での構造化テーブル INSERT までをワンストップで実行（`SongCreditsRepository` / `SongRecordingSingersRepository` / `BgmCueCreditsRepository` を経由）。人物・キャラクターの編集タブには誕生日入力欄（生年 NumericUpDown ＋「不明」チェック／公開可否コンボ／月・日コンボ）。かな・英語表記は `KanaRomanizer`（パスポート式、長音符無音・撥音 n・促音は子音重ね）で自動補完候補を提示。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。DVD は `VIDEO_TS.IFO` 指定でフォルダ全走査モード（多話収録 DVD 対応）。Blu-ray も `BDMV/PLAYLIST` 配下指定時はフォルダ全走査モード。DB 連携パネルで既存ディスクとの照合・新規商品登録が可能。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・ISRC・CD-Text を SCSI MMC コマンドで直接読み取り。DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行。メディア挿入時に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系プロファイル以外（DVD / BD / HD DVD）はハンドルを即クローズ。 |
| **PrecureDataStars.SiteBuilder** | コンソール | Web 公開用の静的サイト生成ツール。ローカル MySQL の内容を読み出し、シリーズ・エピソードを中心とした静的 HTML 一式を `out/site/` に書き出す。テンプレートエンジンは Scriban、共通レイアウト＋コンテンツの 2 段レンダリング。エピソード詳細・人物／企業／プリキュア／キャラクター詳細・クリエーター・楽曲・劇伴・商品・統計の各ページ群を生成する。`CreditInvolvementIndex` 経由で「人物・企業・キャラごとにどのシリーズのどのエピソードに、どの役職で関与したか」を逆引きする。 |
| **PrecureDataStars.AmazonSync** | コンソール | `products` テーブルから ASIN を持つ商品を抽出し、Creators API GetItems で `cover_image_url` を一括更新するバッチ。鮮度切れ判定（90 日経過 or 未取得）で対象を絞り込み、Creators API レート制限（1 TPS）順守のため各リクエスト間に 1.1 秒スリープを挟む。CLI オプションは `--all`（全件強制再取得）／`--asin B0XXXXXXXX`（単一テスト）／`--dry-run`（DB 更新せず表示のみ）。優先順位は CD ASIN → デジタル ASIN で、最初に画像 URL が取れた方を採用して `cover_image_source = amazon_cd` または `amazon_digital` で記録。 |

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

### 1. データベース構築（新規）

```bash
mysql -u root -p < db/schema.sql
```

`db/schema.sql` でデータベース `precure_datastars` と全テーブルが作成される。

### 1'. 既存環境からのアップグレード

`db/migrations/` 配下の差分 SQL をファイル名のバージョン昇順に適用する。各スクリプトは `INFORMATION_SCHEMA` で対象オブジェクトの存在を確認してから DDL を実行する冪等設計のため、適用済みのバージョンを再実行しても安全に素通りする。差分 SQL のファイル名は `v<VERSION>_migration_<topic>.sql` 形式（`VERSION` は `Directory.Build.props` のリリースバージョン、`topic` は英小文字スネークケース）。データ補正を伴う UPDATE も未設定行のみを対象にするなど非破壊。新規構築では `db/schema.sql` が常に最新スキーマを表す。

### 2. 接続文字列の設定

DB 接続が必要なプロジェクト（Episodes / Catalog / CDAnalyzer / BDAnalyzer / SiteBuilder / AmazonSync）の `App.config.sample` を `App.config` にコピーし、接続文字列を設定する。

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
dotnet run --project PrecureDataStars.Catalog
```

### 4. リリースビルド（配布用 ZIP の作成）

`scripts/build-release.ps1` が配布対象の全 EXE プロジェクトを `publish` → ZIP 化し、`release/` フォルダに集約する。バージョン番号は `Directory.Build.props` から自動取得される。

**VSCode から**: `Ctrl+Shift+B` で既定タスク「Release Build」を起動。タスク一覧:

- **Release Build**：フレームワーク依存（配布先に .NET 9 Desktop Runtime が必要）
- **Release Build (Self-Contained)**：ランタイム同梱（配布先に .NET 不要・サイズ大）
- **Release Build (Skip Clean)**：前回の publish を再利用して差分のみ更新（動作確認用）
- **Release Clean**：`publish/` と `release/` を削除
- **dotnet build**：開発用の通常ビルド

**コマンドラインから**:

```powershell
.\scripts\build-release.ps1                # フレームワーク依存
.\scripts\build-release.ps1 -SelfContained # 自己完結
.\scripts\build-release.ps1 -SkipClean     # 差分ビルド
```

**生成される配布物** (`release/` 配下):

- `PrecureDataStars.Catalog-v<VERSION>-win-x64.zip`
- `PrecureDataStars.CDAnalyzer-v<VERSION>-win-x64.zip`
- `PrecureDataStars.BDAnalyzer-v<VERSION>-win-x64.zip`
- `PrecureDataStars.Episodes-v<VERSION>-win-x64.zip`
- `PrecureDataStars.SiteBuilder-v<VERSION>-win-x64.zip`
- `PrecureDataStars.AmazonSync-v<VERSION>-win-x64.zip`
- `precure-datastars-db-v<VERSION>.zip`（`schema.sql` + `migrations/*`）

`PrecureDataStars.AmazonPaApi` はクラスライブラリのため独立 ZIP は出力されず、`Catalog` と `AmazonSync` の publish 出力にそれぞれ DLL として同梱される。

完走後のコンソールに表示される「Next steps」に従って `git tag` → `git push --tags` → GitHub Releases へ `release/*.zip` をアップロードする。

---

## 主要ワークフロー

### エピソード管理

`PrecureDataStars.Episodes` でシリーズとエピソードの CRUD、サブタイトルのかな・ルビ編集、パート構成（アバン・OP・A/B パート・ED・予告）の編集を行う。新規エピソード追加後はサブタイトル文字統計（`title_char_stats`）と YouTube 予告動画 URL を必要に応じて補完する。

### 音楽カタログ登録

#### A. CD の登録

1. `PrecureDataStars.CDAnalyzer` を起動し、CD をドライブに挿入
2. 「読み取り」で TOC・MCN・ISRC・CD-Text を取得
3. 「既存ディスクと照合 / 新規登録...」ボタンで `DiscRegistrationService` を通じた優先順（MCN → CDDB-ID → TOC 曖昧）の照合が走り、`DiscMatchDialog` が候補を表示。アクションは 3 通り:
   - **「選択したディスクに反映」**: TOC 一致した既存ディスクの物理情報のみ更新（タイトル等の Catalog 情報は保全）
   - **「選択したディスクの商品に追加」**: 既存の複数枚組商品に新しいディスクを追加するケース。`DiscMatchDialog` のグリッドで対象 BOX のいずれかのディスクを選択した状態で押下する。`ConfirmAttachDialog` で確認・シリーズ継承選択 → 品番候補入りの入力プロンプトで品番確定 → 新ディスクを INSERT。組内番号 (`disc_no_in_set`) は商品配下の全ディスクを品番順に自動再採番、`disc_count` も所属ディスク数 + 1 に自動更新される
   - **「新規商品＋ディスクとして登録」**: 商品もディスクも新規作成。品番入力 → `NewProductDialog` で商品種別・タイトル・シリーズ・発売日等を設定 → ディスク＋トラックを一括登録。`NewProductDialog` で選択したシリーズは Disc 側の `series_id` に適用される

> **非 CD メディア投入時の挙動**: DVD / Blu-ray / HD DVD 投入時は MMC `GET CONFIGURATION` の Current Profile 判定で読み取りをスキップしハンドルを即クローズする。自動検知経由はステータスラベル通知のみ、手動「読み取り」時はメディア種別を案内するダイアログを表示。GET CONFIGURATION 非対応の旧ドライブは安全側で従来の TOC 読み取りにフォールバック。
>
> **MCN / ISRC の取得仕様**: MCN（JAN/EAN バーコード）と ISRC は SCSI MMC の READ SUB-CHANNEL (0x42) で取得。ISRC は対象トラック先頭への SEEK(10) を挟む 2 パス取得で、ディスク内に 1 件でも取得できれば「収録盤」と判定し未取得トラックのみ最大 5 回再試行、1 件も無ければ未収録盤として再試行しない（ディスク単位ゲート）。取得値は `discs.mcn`（照合最優先キー）・`tracks.isrc` に格納。
>
> **公開サイトでの掲示**: MCN は商品基本情報テーブルに「JAN」行として 1 回表示（CD 含む商品のみ、先頭 CD ディスクの MCN を採用）。各トラックの ISRC はトラック表「No.」セルの `title` ツールチップ。トラック尺は整数部「m:ss」＋小数 2 桁を `.micro-fraction` 縮小表示。商品 JAN が 13 桁数字のとき商品 JSON-LD に schema.org `gtin13` を出力。
>
> **ジャケット画像と購入リンク**: 商品見出し直下にジャケット画像＋外部リンクを並べる。Amazon リンクは物理パッケージ向けの `amazon_asin_cd`（「Amazon で買う (CD)」）とデジタル音源向けの `amazon_asin_digital`（「Amazon で聴く (デジタル)」）の 2 系統を並列表示（v1.4.2 で Apple Music / Spotify 経路は撤去、Amazon Creators API 一本運用に整理）。画像は `products.cover_image_url` にキャッシュした URL を Amazon CDN へホットリンク（`loading=lazy decoding=async`）。`cover_image_source` は `amazon_cd` / `amazon_digital` の 2 値で、採用優先順位は CD ASIN → デジタル ASIN。取得は SiteBuilder ビルドから分離し、Catalog の「画像取得」ボタン（手動・差分）または `PrecureDataStars.AmazonSync` コンソール（バッチ・鮮度切れ自動判定）で行う。外部リンクはすべて `rel="nofollow sponsored noopener"` ＋ `target=_blank`、`AmazonAssociateTag` が設定されていれば `?tag=` 付与でアフィリエイト計測対象。JSON-LD では `offers` 配列に物理／デジタルそれぞれの Offer を出力。

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で `.mpls` / `.IFO` をロード
   - **Blu-ray**: `BDMV/PLAYLIST/*.mpls` の任意 1 個を指定。親フォルダが `PLAYLIST` であることが検出されると、フォルダ内の全 `*.mpls` を走査して有意なタイトルを並列抽出するフォルダ全走査モードに切り替わる。ロゴ・著作権警告等の短尺ダミーと、anti-rip 系の重複プレイリストは自動除外（フィルタ仕様は後述）
   - **Blu-ray（単一プレイリストモード）**: `BDMV/PLAYLIST` 配下にない `.mpls` ファイルを直接指定すると、そのプレイリスト 1 個だけを解析
   - **DVD**: `VIDEO_TS/VIDEO_TS.IFO` を指定。下記の二段階ルーティングでチャプター一覧を抽出
   - **DVD（単一 VTS モード）**: `VTS_xx_0.IFO` を直接指定すると、その VTS の先頭 PGC のみを解析
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺 ms ±1 秒による TOC 曖昧のみ）
3. 反映時は `discs` テーブルの物理情報が同期され、`video_chapters` テーブルへチャプター情報が一括登録される（再読み取り時は「全削除 → 置換」で上書き）
   - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ
   - `title` / `part_type` / `notes` は NULL のまま登録され、Catalog GUI 側で手動補完
   - フォルダ全走査モードでは `chapter_no` はディスク全体で通し番号、`playlist_file` にはタイトル識別子（DVD VMGI モードでは `Title_01` 等、Per-VTS モードでは `VTS_02` 等、Blu-ray は MPLS ファイル名）
   - `start_time_ms` はタイトル単位の相対時刻（各タイトルの先頭 = 0ms）

##### Blu-ray PLAYLIST フォルダ全走査の仕様

`BDMV/PLAYLIST` 配下の `.mpls` を指定すると `MplsParser.ExtractTitlesFromBdmv` が以下を実行:

1. フォルダ内の `*.mpls` をファイル名昇順に列挙
2. 各 MPLS を `MplsParser.Parse(path, allowFallback: false)` でパースし、隣接 MPLS への自動フォールバックを抑止して個別解析結果を取得
3. 4 段のフィルタを順に適用:
   - **フィルタ A — 短尺ダミー除外**: プレイリスト総尺が既定 60 秒未満を除外（FBI 警告画面、配給会社ロゴ等）
   - **フィルタ B — ゼロ尺チャプター除外**: 章尺 < 1ms を除去
   - **フィルタ C — 境界極短チャプター除外**: 先頭・末尾の 500ms 未満チャプターを剥がす（黒みフレーム等）
   - **フィルタ D — 重複プレイリスト畳み込み**: `(総尺 ticks, マーク数)` を重複キーとし、同一キーの 2 個目以降を除外（anti-rip スキーム対策）
4. 残ったプレイリストを `MplsTitleInfo` として返却。チャプターの `Start` はタイトル先頭からの相対時刻に再計算

ListView 表示は DVD のフォルダ全走査と同一の階層形式:
- タイトルヘッダ行: `[00000.mpls]`（薄いグレー背景）
- チャプター行: 2 段インデント
- 末尾の除外サマリ行（除外があった場合のみ、グレー文字）: `短尺 X / 0ms Y / 境界極短 Z / 重複 W`
- 既定チェックは「総尺最大タイトル + そのチャプター」のみオン

`lblInfo` には `00000.mpls - (Blu-ray PLAYLIST scan) Titles: N Chapters: M Aggregated: hh:mm:ss.ff` の形式で集約サマリが表示される。集約総尺はフィルタ D で重複が畳まれた後の単純合計。しきい値は `MplsParser.ExtractTitlesFromBdmv` の引数で変更可能だが、現状の MainForm からは既定値（60 / 1 / 500）固定で呼ぶ。

##### DVD 解析の二段階ルーティング

`VIDEO_TS.IFO` を指定すると以下の優先順で処理:

1. **VMGI 経路（正攻法、優先）**: `VIDEO_TS.IFO` 先頭の `DVDVIDEO-VMG` シグネチャを確認後、TT_SRPT (Title Search Pointer Table、offset `0xC4`) を読んで論理タイトル一覧を取得。各タイトルについて対応 `VTS_NN_0.IFO` の VTS_PTT_SRPT (offset `0xC8`) から `(PgcNo, PgmNo)` ペアをチャプターごとに解決し、該当 PGC の Program 尺リストから各チャプターの再生時間を組み立てる。DVD プレイヤーがユーザーに見せる「タイトル/チャプター」構造と完全一致
2. **Per-VTS 経路（フォールバック）**: VMGI が読めない／TT_SRPT が壊れているディスク向け。物理 `VTS_NN_0.IFO` を全走査し、各 VTS の最長 PGC を「その VTS のタイトル本編」とみなして拾う

ListView のヘッダに `(DVD VMGI, hardlinked)` のように現在のスキャンモードと UDF ハードリンクの有無が表示される。

##### ゴミチャプター・ダミー VTS のフィルタ

| # | フィルタ対象 | しきい値 | 適用モード | 判断基準 |
|---|---|---|---|---|
| 1 | VTS 全体 | 最長 PGC < 5 秒 | Per-VTS のみ | メニュー/初期化用ダミー VTS を丸ごと除外 |
| 2 | ゼロ尺チャプター | duration < 1 ms | 両モード | 空 Cell や PGC 終端プレースホルダ |
| 3 | 境界の極短チャプター | duration < 500 ms かつ 先頭または末尾 | 両モード | 黒画面 1 フレームやナビゲーション用ダミー Cell。中央部の短チャプターは保持 |
| 4 | 重複タイトル | 同一 `(VtsNo, PTT列)` シグネチャが 2 回目以降 | VMGI のみ | ARccOS 系の anti-rip 保護、99 個のフィラータイトル対策 |

フィルタで除外されたチャプター数は ListView 末尾の「除外」行に表示される。

##### タイトル/チャプターのチェック選択

フィルタで除去しきれない「ユーザー視点で明らかに不要なタイトル」を手動で除外できるよう、ListView の各行にチェックボックスがついている:

- デフォルトで全行チェック済み
- タイトル行のチェックを外す → 配下チャプター全てが連動して外れる
- チャプター行を個別に外す → 親タイトル行は配下のチェック状態の OR
- 除外行（集計表示）のチェックは機能しない
- 「既存ディスクと照合 / 新規登録...」押下時に、チェックが残っているチャプターだけが `video_chapters` に投入される。`chapter_no` は投入対象のみで 1 から再採番
- `discs.num_chapters` と `discs.total_length_ms` も絞り込み後の値で計算し直して登録

##### ディスク総尺の集約ロジック

| 条件 | 集約方法 | 根拠 |
|---|---|---|
| タイトル 1 個 | 単純にそのタイトルの尺 | 場合分け不要 |
| タイトル複数 + VOB ハードリンク検出 | 最長タイトルの尺 | 同じ実データを別角度で複数ナビから見せている構造 |
| タイトル複数 + VOB 独立 | 全タイトル尺の合計 | 真に独立した多話収録 |

UDF ハードリンクの検出は、`VTS_*_1.VOB` のバイト数が全て同一かどうかで判定する。

#### C. トラックの内容編集（歌・劇伴への紐付け）

1. `PrecureDataStars.Catalog` を起動し、メニューから「トラック管理...」を選択
2. ディスクを選んでトラック一覧を開き、各トラックの**内容種別**を選択する
3. **ディスクのシリーズ所属**は、メニュー「商品・ディスク管理...」のディスク詳細エリアにある「シリーズ」コンボから変更できる。先頭の「(オールスターズ)」を選ぶと `series_id = NULL` として保存
4. 歌・劇伴マスタ側の新規作成は「歌マスタ管理...」「劇伴マスタ管理...」メニューから。両画面とも CSV 一括取り込み機能を搭載

#### C''. 商品・ディスク管理画面

「商品・ディスク管理...」は商品 1 件と所属ディスク群を 1 画面で編集する統合エディタ。トラック編集は扱わない（「トラック管理...」が担う）。

**画面構成**: 上下 2 段構成、上段（商品エリア）と下段（ディスクエリア）を上下に並べ、それぞれ左 60% に一覧、右 40% に詳細エディタを配置。下段の高さは 400 px に固定。

- **検索バー**（最上部）: 検索キーワード（品番／タイトル／略称／英語タイトルに部分一致）、検索・再読込ボタン
- **上段左ペイン（60%）**: 商品一覧。発売日昇順、同一日内は代表品番昇順。表示カラムは「発売日 / 品番 / タイトル / 種別 / 税込 / 枚数」
- **上段右ペイン（40%）**: 商品詳細エディタ。代表品番・タイトル・略称・英語タイトル・商品種別・発売日・税抜価格・税込価格＋自動計算ボタン・ディスク枚数・発売元・販売元・レーベル・Amazon ASIN (CD / デジタル)・備考。新規／保存／削除ボタンは右端に固定
- **下段左ペイン（60%）**: 所属ディスク一覧（組内番号・品番・ディスクタイトル・メディア）
- **下段右ペイン（40%）**: ディスク詳細エディタ（品番・組内番号・ディスクタイトル・略称・英語タイトル・シリーズ・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考）

リサイズ時の左右 60:40 比率は `splitProduct.SizeChanged` / `splitDisc.SizeChanged` で都度自動再計算。下段の高さ 400 px は `splitMain.FixedPanel = FixedPanel.Panel2` で固定。

**ディスク詳細編集と物理情報の保全**: 本画面で編集できるのはタイトル系・組内番号・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考のみ。`total_length_frames` / `total_length_ms` / `num_chapters` などの物理情報、CD-Text 系 8 列、`cddb_disc_id` / `musicbrainz_disc_id` / `last_read_at` は本フォームから編集できず、ディスク保存時に DB から既存値を引き直して自動引き継ぐ。

**税込価格の自動計算**: 「自動計算」ボタンで税抜価格と発売日から日本の標準消費税率を切り捨てで適用して税込価格を埋める（書籍・音楽・映像ソフト業界の実務慣例に合わせて端数切り捨て）。

| 発売日 | 適用税率 |
|---|---|
| 〜 1989-03-31 | 0% |
| 1989-04-01 〜 1997-03-31 | 3% |
| 1997-04-01 〜 2014-03-31 | 5% |
| 2014-04-01 〜 2019-09-30 | 8% |
| 2019-10-01 〜 | 10% |

商品保存時にも、税込価格が空で税抜価格が入っている場合は同じロジックで自動補完。既存レコードの一括補完は `db/utilities/backfill_products_price_inc_tax.sql`。

#### B'. 既存商品への追加ディスク登録フロー

CDAnalyzer / BDAnalyzer から、既に登録済みの商品に対して**新しいディスクだけを追加登録**するフロー。BOX 商品の Disc 2 / Disc 3 を後から流し込む運用や、特典 CD・特典 DVD を本編商品にぶら下げて登録するケース向け。

**起動経路**:

1. CDAnalyzer / BDAnalyzer で対象ディスクを読み取り、「既存ディスクと照合 / 新規登録...」を押す
2. `DiscMatchDialog` のグリッドから、追加先 BOX に既に登録されているディスクを 1 つ選択（自動照合候補が 1 件のみ・手動検索結果が 1 件のみの場合は先頭行が自動選択された状態で表示）
3. **「選択したディスクの商品に追加」**ボタンを押下（ディスク未選択時は Disabled）
4. `ConfirmAttachDialog` が開き、商品情報・所属ディスク・シリーズコンボ・新ディスクの品番入力（次の品番候補が初期値・全選択状態）を 1 画面で確認 → 「追加して登録」で完了

**`ConfirmAttachDialog` の操作**:

- **商品情報（読み取り専用）**: 代表品番 / タイトル / 発売日 / 現在の枚数
- **所属ディスクのプレビュー**: その商品に既に登録されているディスクが下段グリッドにプレビュー表示（組内番号 / 品番 / タイトル / メディア / `series_id`）
- **組内番号は自動再採番**: 登録時に商品配下の全ディスクを品番昇順（`StringComparison.Ordinal`）でソートし、1, 2, 3, … と振り直す
- **シリーズの継承**: シリーズコンボの先頭は「(既存ディスクから継承)」が既定で選択され、所属ディスクの先頭の `series_id` を自動採用。ラベル右側に「（継承元: 〇〇）」と継承元シリーズ名が表示される
- **品番入力**: 「新ディスク品番」テキストボックスに、商品配下で品番昇順末尾のディスク品番末尾を +1 した値が初期値・全選択状態で入る（例: `KICA-1234` → `KICA-1235`、`KICA-9999` → `KICA-10000`、元の桁数を維持してゼロパディング）。末尾が数字でない品番は元の値をそのまま提示。AcceptButton = btnAttach。空欄での確定はブロック
- **タイトル候補の自動計算**: 起動時、所属ディスクの先頭の `Title`（非空）を `InheritedDiscTitle` に格納し、呼び出し側が新ディスクの `Title` 初期値として上書きする（CD-Text や VolumeLabel 由来の暫定タイトルを正規タイトルで置き換える狙い）

**確定後の登録処理**: 「追加して登録」ボタン押下で `DiscRegistrationService.AttachDiscToExistingProductAsync` が呼ばれ、次の順序で DB 更新:

1. 指定の `productCatalogNo` で `Product` を取得（無ければ例外）
2. **新ディスクの品番が DB 上に既存していないかを事前検証**。`DiscsRepository.GetByCatalogNoAsync` で既存レコードがヒットしたら `InvalidOperationException("品番 [XXX] は既に登録されています。別の品番を指定してください。")` を送出（論理削除済みもヒット扱い）
3. 新ディスクの `product_catalog_no` を既存商品に固定
4. 既存ディスク + 新ディスクを品番昇順にソートし、1 始まり連番で再採番。採番値が変わる既存ディスクは `DiscsRepository.UpdateDiscNoInSetAsync` で `disc_no_in_set` のみ更新
5. 既存所属ディスク数 + 1 を `Product.disc_count` に反映
6. 新ディスク本体を `DiscsRepository.UpsertAsync`
7. CD ならトラック群、BD/DVD ならチャプター群を一括登録

#### C'. ディスク・トラック閲覧画面（読み取り専用）

「ディスク・トラック閲覧」はディスク → トラックを翻訳済みの表示値で一覧する参照専用ビュー。

**画面構成**:

- **ツールバー**（最上部）: 検索キーワード（品番 / タイトル / シリーズ名に部分一致）、シリーズ絞り込みドロップダウン、再読込ボタン、件数表示
- **ディスク一覧**（上段、SplitContainer）
- **トラック一覧**（下段、SplitContainer。ディスク選択に応じて更新）

外周 10 px の余白、上下ペインの分割バーは若干太め。上下ペインはウインドウのリサイズに合わせて常に縦方向半々（`splitMain.SizeChanged` で都度書き戻し）。

**ディスク一覧のカラム**:

| カラム | 幅 | 内容 |
|---|---|---|
| 品番 | 110 | `discs.catalog_no` |
| タイトル | Fill | `discs.title` 優先、無ければ所属商品タイトル |
| シリーズ | 140 | シリーズの略称（無ければ正式名） |
| 商品種別 | 100 | `product_kinds.name_ja`（翻訳値） |
| メディア | 70 | `discs.media_format`（CD/BD/DVD 等） |
| 発売日 | 100 | 所属商品の `release_date`。`yyyy-MM-dd` 表記 |
| 枚数 | 70 | 2 枚組以上のときのみ `n / m` 形式で表示。単品は空欄 |
| トラック数 | 75 | `discs.total_tracks` |
| 総尺 | 95 | M:SS.fff 形式。CD は `total_length_frames`、BD/DVD は `total_length_ms` から算出。NULL なら `—` |

MCN カラムは持たない（MCN は `DiscsEditorForm` のディスク詳細エリアで閲覧・編集）。

**トラック一覧のカラム**:

| カラム | 幅 | 内容 |
|---|---|---|
| # | 52 | トラック番号。`sub_order=0` は番号のみ、`sub_order>=1` は `"24-2"` の枝番（右寄せ） |
| 種別 | 70 | `track_content_kinds.name_ja` |
| タイトル | 220 | 下記「タイトル解決・BGM 集約ルール」 |
| アーティスト | 180 | SONG は歌唱者→CD-Text、BGM は空欄、その他は CD-Text |
| 作詞 | 110 | SONG は `songs.lyricist_name`、BGM/その他は空欄 |
| 作曲 | 110 | SONG は `songs.composer_name`、BGM は `bgm_cues.composer_name`、その他は空欄 |
| 編曲 | 110 | SONG は `songs.arranger_name`、BGM は `bgm_cues.arranger_name`、その他は空欄 |
| 尺 | 90 | M:SS.fff（右寄せ）。`length_frames` があれば 1/75 秒精度で算出、無ければ BGM cue の秒数にフォールバック |
| 備考 | Fill | `tracks.notes` |

ISRC カラムは持たない（ISRC は `DiscsEditorForm` のトラック詳細で閲覧・編集）。

**タイトル解決・BGM 集約ルール**: BGM 以外の集約は行わない。

- **SONG**: `track_title_override` → (`variant_label` または親曲名) + ` [サイズ]` + ` [パート]` → `cd_text_title`
- **BGM（単独 sub_order 行）**: 主タイトル（`track_title_override` → `cd_text_title` → `menu_title` → `m_no_detail` の優先順）に、`(m_no_detail [menu_title])` の注釈を後置
  - 例: `track_title_override = "決戦のテーマ"` / `m_no_detail = "M220b Rhythm Cut"` / `menu_title = "戦闘・危機一髪"` のとき → `決戦のテーマ (M220b Rhythm Cut [戦闘・危機一髪])`
  - `menu_title` が NULL のときは `{主タイトル} (m_no_detail)` のみ
  - `bgm_cues` の JOIN が外れた場合は注釈部を付けず主タイトルのみ
  - **`is_temp_m_no = 1` の行（仮 M 番号）**は閲覧 UI で `m_no_detail` を非表示
- **BGM（同一 track_no で sub_order が複数：メドレー構成）**: sub_order 全行を 1 行に集約。主タイトルは `sub_order=0` 行のものを採用、注釈部には全 sub_order 行の `m_no_detail [menu_title]` を ` + ` 区切りで連結
- **DRAMA / RADIO / LIVE / TIE_UP / OTHER**: `track_title_override` → `cd_text_title`。sub_order 複数行がある場合は別行で表示し、`#` に枝番

**尺整形ルール**:

- `length_frames`（CD-DA、1/75 秒）があれば: 秒 + ミリ秒（1 フレーム = 1000/75 ≒ 13.333 ms、丸めで 1000 ms 到達時は秒を 1 繰り上げ）
- `length_frames` が無く `length_seconds` / `total_length_ms` があれば: そのミリ秒値または秒値（ミリ秒値は `.000` 固定）
- どれも無ければ: `—`

#### C'''. 歌マスタ管理画面

「歌マスタ管理...」で `songs`（メロディ + アレンジ単位の曲マスタ）と `song_recordings`（歌唱者バージョン）の 2 階層を編集。

**画面構成**:

- **検索バー**（最上部）: タイトル／かなの部分一致テキスト、シリーズ絞り込み、音楽種別絞り込み、検索ボタン、CSV取り込みボタン
- **上段**: 左に曲一覧、右に曲詳細（タイトル・かな・音楽種別コンボ・シリーズコンボ・作詞名・作詞名かな・作曲名・作曲名かな・編曲名・編曲名かな・備考）
- **下段左**: 選択中曲の歌唱者バージョン一覧 / バージョン詳細（歌手名・歌手名かな・バリエーションラベル・備考）
- **下段右**: 選択中バージョンの収録ディスク・トラック一覧（読み取り専用）

**入力補完**: 作詞・作曲・編曲・歌手のテキストボックスに `AutoCompleteSource.CustomSource` で既存マスタのユニーク氏名一覧を注入。`AutoCompleteMode.SuggestAppend` により 1 文字目から候補ドロップダウンが表示される。

**CSV 一括取り込み**: 「CSV取り込み...」でファイル選択 → 2 段階で進む:

1. **ドライラン**: 実書き込みは行わず、行数集計（新規／更新／スキップ）と警告メッセージ（最初の 10 件）を確認ダイアログで表示
2. **本実行**: 「はい」で確定すると同じパースで UPSERT。既存判定は `(title, series_id, arranger_name)` の三要素キー

CSV ヘッダ仕様（UTF-8、カンマ区切り、ヘッダ行必須、ダブルクォート囲み可）:

```csv
title,title_kana,series_title_short,lyricist_name,lyricist_name_kana,composer_name,composer_name_kana,arranger_name,arranger_name_kana,notes
```

| 列 | 必須 | 解釈 |
|---|---|---|
| `title` | ◯ | 空ならスキップ＋警告 |
| `title_kana` | | そのまま格納 |
| `series_title_short` | | `series.title_short` 完全一致 → `series.title` 部分一致の順で解決。未解決時は `series_id = NULL`（オールスターズ扱い）＋警告 |
| `lyricist_name` 〜 `arranger_name_kana` | | そのまま格納 |
| `notes` | | そのまま格納 |

> `music_class_code` は `song_recordings` 側で管理する仕様のため、歌マスタ CSV 取り込みでは扱わない。CSV に含まれていても無視して取り込みは継続される（後方互換、列の存在自体は許容、値は使われず警告のみ出力）。録音バージョンごとの種別は Catalog GUI で個別設定。

サンプルは `docs/csv-templates/songs_import_sample.csv`。

#### C''''. 劇伴マスタ管理画面

「劇伴マスタ管理...」で `bgm_cues`（劇伴の音源 1 件 = 1 行、複合 PK `(series_id, m_no_detail)`）と関連 `bgm_sessions` を編集。

「映画 BGM リスト管理...」で映画作品専用の `movie_bgm_cues` を編集（`MovieBgmCuesEditorForm`）。映画にはセッション・パートの概念が無く、その映画固有の M ナンバー文字列・順序（`seq`）・サブ順序（`sub_seq`）・区分（`track_content_kinds` 共用）と、未使用（音源はあるが本編未使用）・欠番（そもそも未制作）の排他 2 フラグを持つ。シリーズ選択コンボには映画系 kind（`MOVIE` / `MOVIE_SHORT` / `SPRING` / `EVENT`）のシリーズのみを表示。保存時に未使用と欠番の同時設定を検出して弾く。映画系シリーズの詳細ページ（SiteBuilder 生成）には、1 件以上あるとき「BGM リスト」セクションを描画し、欠番行は M 番号・曲名を出さず「（欠番）」表示、未使用行は淡色＋「（未使用）」注記。

**画面構成**:

- **検索バー**（最上部）: シリーズフィルタ、セッションフィルタ、検索キーワード、検索ボタン、CSV取り込みボタン
- **中段**: 左に劇伴一覧、右に詳細（シリーズ・セッション・M番号詳細・M番号分類・メニュー名・作曲者・作曲者かな・編曲者・編曲者かな・尺(秒)・仮 M 番号フラグ・仮番号を採番ボタン・備考）
- **下段**: 選択中キューの収録ディスク・トラック一覧（読み取り専用）

**仮 M 番号フラグ（`is_temp_m_no`）**: M 番号が判明していない劇伴音源は、内部的に `_temp_034108` のような暫定 PK を `m_no_detail` に入れて管理する運用がある。`is_temp_m_no` カラムでこの「仮番号運用中」を明示することで画面ごとに表示挙動を切り替える。

| 画面 | 仮番号行の扱い |
|---|---|
| 劇伴マスタ管理 | チェックボックスとして可視化、`m_no_detail` は素のまま表示・編集可 |
| ディスク・トラック閲覧 | `m_no_detail` を非表示にし、フォールバック候補からも注釈からも除外 |
| トラック管理の BGM 候補リスト | 既定で除外。「仮番号を候補に含める」チェックで明示的に含められる |

**仮番号採番ボタン**: 編集中シリーズ配下の既存 `_temp_NNNNNN` 連番から次の値（6 桁ゼロ埋め）を自動生成して `m_no_detail` フィールドに投入し、フラグもオンになる。既存連番に欠番があっても詰めず、最大値 + 1 を返す（`BgmCuesRepository.GenerateNextTempMNoAsync`）。

**CSV 一括取り込み**: 歌マスタ同様、ドライラン → 本実行の 2 段階。`session_name` がシリーズ内で未登録なら自動採番（既存最大 `session_no` + 1）して `bgm_sessions` を新規作成。`m_no_detail` が空欄でも `is_temp_m_no` フラグが立っていれば `_temp_NNNNNN` を自動採番してインサート（フラグが偽で空欄の行はスキップ＋警告）。CSV では `bgm_sessions.caption` は扱わず、自動採番で新規作成されたセッションでは `caption` は NULL。`caption` の編集は「マスタ管理 → 劇伴・セッション」タブで個別に行う。

CSV ヘッダ仕様:

```csv
series_title_short,m_no_detail,session_name,m_no_class,menu_title,composer_name,composer_name_kana,arranger_name,arranger_name_kana,length_seconds,is_temp_m_no,notes
```

| 列 | 必須 | 解釈 |
|---|---|---|
| `series_title_short` | ◯ | 未解決時は行スキップ＋警告 |
| `m_no_detail` | △ | 空欄かつ `is_temp_m_no=1` なら自動採番、それ以外で空欄ならスキップ＋警告 |
| `session_name` | | 未登録なら同シリーズ内で自動採番して新規作成 |
| `length_seconds` | | 数値化できなければ NULL＋警告 |
| `is_temp_m_no` | | `1` / `true` / `yes` / `y` / `t`（大小無視）を真、それ以外を偽。既定は偽 |
| その他 | | そのまま格納 |

サンプルは `docs/csv-templates/bgm_cues_import_sample.csv`。

#### C'''''. マスタ管理画面

「マスタ管理」で小マスタ群を 1 画面の TabControl で編集。タブ構成:

| タブ名 | 対象テーブル | 主キー |
|---|---|---|
| 商品種別 | `product_kinds` | `kind_code` |
| ディスク種別 | `disc_kinds` | `kind_code` |
| トラック内容 | `track_content_kinds` | `kind_code` |
| 曲・音楽種別 | `song_music_classes` | `class_code` |
| 曲・サイズ種別 | `song_size_variants` | `variant_code` |
| 曲・パート種別 | `song_part_variants` | `variant_code` |
| 劇伴・セッション | `bgm_sessions` | `(series_id, session_no)` |

各タブは上半分にグリッド、下半分に編集フォームと操作ボタン。`bgm_sessions` を除く 6 つのマスタタブは共通レイアウト（`BuildTab` ヘルパで生成）で、以下のボタンを縦並びに 4 つ持つ:

- **新規**: フォーム入力欄を空にし、グリッド選択を解除
- **保存 / 更新**: 入力欄のコードに基づいて UPSERT（同コードがあれば更新、なければ INSERT）
- **選択行を削除**: グリッドで選択中の行を削除（FK で参照されている場合は失敗）
- **並べ替えを反映**: 行ドラッグ&ドロップで変更したグリッド上の並び順を、`display_order` カラムに `1, 2, 3, ...` として一斉反映

#### クレジット編集（3 ペイン構成）

- **左ペイン**: scope（SERIES / EPISODE）の絞込み、シリーズ・エピソードの選択コンボ、クレジット一覧 ListBox、新規クレジット作成・話数コピーボタン、選択中クレジットのプロパティ編集（presentation / part_type / 備考）と「プロパティ保存」「クレジット削除」ボタン
- **中央ペイン**: 階層ツリーと「+ カード」「+ Tier」「+ Group」「+ 役職」「+ ブロック」「+ エントリ」「↑」「↓」「✖ 削除」のツリー編集ボタン群、画面下に「💾 保存」「✖ 取消」
- **右ペイン**: ツリーで選択したノードに応じて切り替わる。Block 選択時は `BlockEditorPanel`（col_count / block_seq / leading_company_alias_id / notes の編集と「適用」ボタン）、Entry 選択時は `EntryEditorPanel`（種別ごとの入力 UI と「保存」「削除」ボタン）

#### 編集の流れ（Draft セッション方式）

1. クレジットを選択すると、`CreditDraftLoader` が DB から全階層を読み込んで Draft セッションをメモリ上に構築
2. ユーザーがツリーやパネルで操作（追加・編集・削除・並べ替え・DnD 移動）すると、すべて Draft オブジェクトに反映され、DB は触らない
3. 未保存変更があると、ツリー背景色が薄い黄色、ステータスバー末尾に「★ 未保存の変更あり」が表示され、画面下部の「💾 保存」「✖ 取消」が Enabled
4. 「💾 保存」を押すと `CreditSaveService` が 5 フェーズ（**1A** エントリ削除 → **2** 新規 → **3** 更新 → **1B** ブロック以上の親階層削除 → **4** seq 整合性）を 1 トランザクション内で実行。失敗すれば全体ロールバック
5. 「✖ 取消」を押すと現在の Draft を破棄して DB から再読み込み

#### 話数コピー（シリーズ跨ぎ対応）

新シリーズの第 1 話を作成する際、前作の OP / ED を丸ごと複製してから差分編集するワークフロー:

1. コピー元クレジットを左ペインで選択 → 「📋 話数コピー...」ボタン
2. `CreditCopyDialog` でコピー先のシリーズ・エピソード・presentation・part_type・備考を指定（クレジット種別はコピー元と同じで固定）
3. コピー先に同種クレジットが既に存在する場合は「上書き／中止」を選ぶ（上書き時は既存を即時論理削除）
4. `CreditDraftLoader.CloneForCopyAsync` がコピー元を読み込んで配下を全部 `State = Added` で deep clone し、コピー先 Draft を構築
5. 画面がコピー先 Draft に切り替わる。内容を確認・編集してから「💾 保存」で 1 トランザクション INSERT

#### HTML プレビュー

クレジット編集中、テンプレ展開後の完成形を確認するため、左ペインの「🌐 HTMLプレビュー」ボタンを押す:

- 非モーダルの新ウィンドウが画面右側に開き、選択中のクレジット（エピソードスコープなら同エピソードの OP / ED 等を縦に並べて）を `WebBrowser` コントロールで HTML 表示
- シリーズ書式上書き（`series_role_format_overrides`）があればそれを優先、無ければ `roles.default_format_template` の DSL を `RoleTemplateRenderer` で展開
- テンプレが未定義の役職は「役職名 + 配下エントリの右並び表」のフォールバック表示
- Card / Tier / Group / Block の階層は CSS の枠線とインデントで視覚的に区切る
- プレビューを開いたままクレジット切替・保存・取消をすると、自動的に追従して再描画
- 未保存 Draft がある場合は確認ダイアログ（プレビューは DB ベース描画のため編集途中状態は反映されない）

#### 未保存ライフサイクル管理

未保存変更がある状態で別操作（クレジット切替・シリーズ／エピソード切替・フォーム閉じ）を行おうとすると、3 択の確認ダイアログ:

- **保存して続行**: 現在の Draft を保存してから次の操作へ進む
- **破棄して続行**: 現在の Draft を破棄して次の操作へ進む
- **キャンセル**: 操作を取りやめて元の状態に戻る（lstCredits の選択を元のクレジットへ復帰）

#### クレジット一括入力

左ペイン下部の「📝 クレジット一括入力...」ボタンで開く。ツリー右クリック「📝 一括入力で編集...」メニューでも選択スコープ（クレジット全体／カード／ティア／グループ／役職）の中身を編集できる。

| モード | 起動方法 | 動作 |
|---|---|---|
| `AppendToCredit` | 左ペイン「📝 クレジット一括入力...」ボタン | クレジット全体の構造差分検出モード。起動時に現状全文を `CreditBulkInputEncoder.EncodeFullAsync` で逆翻訳した文字列が初期値として入る。適用時は旧テキストと新テキストの構造差分を検出して、変わった末端だけ Modified / Added / Deleted で反映。Block 内 Entry は LCS マッチングで行入れ替えを `entry_seq` 更新の Modified として拾う |
| `ReplaceScope` | ツリー右クリック「📝 一括入力で編集...」 | 選択スコープの中身を置換。起動時に既存内容を `CreditBulkInputEncoder` で逆翻訳した文字列が初期値として入る。スコープ配下を全 DELETE → 全 INSERT する全置換セマンティクス |

**書式の要点**:

- 行末コロン `XXX:` で役職開始、空行で同役職内のブロック区切り、`-` / `--` / `---` / `----` でブロック・グループ・ティア・カード区切り、タブ区切りで `col_count` 並び、`<キャラ>声優` で CHARACTER_VOICE
- `[屋号#CIバージョン]` で LOGO エントリ（最右の `#` で屋号と CI バージョンに分解、屋号下のロゴから引き当て）
- 行頭 `🎬`（U+1F3AC、絵文字）で本放送限定エントリ（`is_broadcast_only=1`）
- 行頭 `& `（半角アンパサンド + 半角SP）で直前エントリと A/B 併記（保存時に `parallel_with_entry_id` 解決）
- 行末 ` // 備考` で当該エントリの `notes` 設定
- `@cols=N` で当該ブロックの `col_count` を明示指定
- `@notes=値` で直近スコープ（Card/Tier/Group/Role/Block のうち最後に開いたもの）の `notes` を設定
- 修飾子は重ねがけ可（例: `🎬 & 山田 太郎 // 旧名義あり`）
- 250 ms デバウンスでパースしてプレビュー反映、Block 重大度の警告 1 件で「適用」ボタンが Disabled
- 適用時、未登録役職は `QuickAddRoleDialog` を 1 件ずつ起動して登録、Person / Character / Company は自動 QuickAdd、引き当てに失敗した名前は TEXT エントリ（`raw_text`）に降格
- LOGO エントリのみ屋号 + CI バージョン未ヒット時は TEXT 降格 + InfoMessage（マスタ管理画面の「ロゴ」タブで明示登録するよう促す）

**ラウンドトリップ性**: `CreditBulkInputEncoder` は Draft 階層を一括入力フォーマットに逆翻訳するため、「右クリック → 一括入力で編集 → 編集 → 適用」のサイクルで既存クレジットの大幅な書き換えがテキストエディタ感覚で行える。

**「旧名義 =&gt; 新名義」記法**: 人物・キャラ・企業屋号・LOGO の屋号部いずれの位置でも、`旧名義 =&gt; 新名義` の記法でリダイレクトを明示できる。矢印の向きは「名義が変わった」遷移方向に揃え、左 = 既存マスタの参照キー、右 = この行で実際に表示する別名義。

- 例: `青木 久美子 => 青木 久実子` — 左の「青木 久美子」を `person_aliases.name` 完全一致で引き当てて `person_id` を取得し、同 person 配下に右の「青木 久実子」を新 alias として登録
- 例: `[東映アニメ => 東映アニメーション]` — COMPANY エントリ
- 例: `<キュアブラック => 美墨なぎさ>本名 陽子` — VOICE_CAST 行。`<>` 内に `=>` でキャラの別名義を、外側の声優部にも独立して `=>` を書ける（両側併用可）
- 例: `[東映アニメ => 東映アニメーション#2024年版]` — LOGO エントリ。屋号部分（`#` の左側）のみ `=>` を解釈

旧名義が既存マスタに見つからない場合は警告 `InfoMessages` を出した上で、右側のみで通常の新規作成にフォールバック。

**似て非なる名義の警告 + 新規登録候補の事前通知**: 新規作成しようとしている名義について、`person_aliases` / `character_aliases` / `company_aliases` を全件取得し、空白除去後の LCS（最長共通部分列）／ max(len) ≥ 0.5 を満たすが完全一致でない既存名義があれば警告に積む。漢字違い（「五條 真由美」↔「五条真由美」）や空白違いの誤入力を検出。完全一致なし + 似て非なる候補もなしの「ピュアに新規登録される予定」の名義は、情報レベル（ℹ）の警告として「Apply 時に新規 person + alias を作成します」と表示する。`roles` マスタに無い役職表示名も同様に通知する。

**判定タイミング**: 入力テキストの 250 ms デバウンス後にリアルタイム判定。`CreditBulkApplyService.CheckSimilarNamesAsync` が呼ばれ、新規作成予定の名義について全件比較。結果は警告ペイン（`lstWarnings`）に他のパース警告と同じ並びで表示（行番号付き、`Severity = Warning` または `Info` なので Apply ボタンは無効化されない）。連続入力時は `CancellationTokenSource` で前の判定を取り消す。

**ダイアログレイアウト**: 右ペインのプレビュー（上）：警告（下）の比率は 4:1（8:2）（`splitterDistance` 515）。

#### Card / Tier / Group / Role の備考編集

ツリーで Card / Tier / Group / 役職ノードを選択すると、右ペインに `NodePropertiesEditorPanel` が表示され、対応する DB 列（`credit_cards.notes` / `credit_card_tiers.notes` / `credit_card_groups.notes` / `credit_card_roles.notes`）の備考を直接編集できる。複数行 TextBox + 「💾 保存」ボタンの単純な構成。

#### 名寄せ機能

`CreditMastersEditorForm` の人物名義 / 企業屋号 / キャラ名義タブそれぞれにボタンを 2 つずつ:

- **「別人物（企業／キャラ）に付け替え...」** (`AliasReassignDialog`): 選択中名義の紐付け先だけを別の既存親に変更する。親本体の表示名は変更しない
- **「この名義で改名...」** (`AliasRenameDialog`): 新表記を入力して改名する。人物・企業の場合は新 alias を作成して旧 alias と predecessor/successor で自動リンク、キャラの場合は現 alias を上書き（character_aliases に履歴列が無いため）

孤立した旧親（紐付く名義が 0 件になった `persons` / `companies` / `characters`）は付け替え時に自動で論理削除される。

---

### クレジット入力レシピ集（役職別のブロック構成）

クレジットは「役職 → ブロック → エントリ」の 3 階層構造を取り、役職に紐づく `default_format_template`（DSL テンプレート）が「どのブロックから何のエントリを取り出してどう並べるか」を決める。

> **💡 補足**: 長尺クレジットは「📝 クレジット一括入力...」でテキスト形式投入、既存も「📝 一括入力で編集...」から逆翻訳して書き換え可能。本節のレシピは最終的に作りたい構造を理解するためのリファレンス。

#### 連載（`SERIALIZED_IN`） + 漫画（`MANGA`）

**役職構成**: 連載クレジットでは、雑誌等の連載媒体を表す `SERIALIZED_IN` と、漫画家を表す `MANGA` の 2 役職に分割する（同居させると `CreditInvolvementIndex` の集計で漫画家が「連載」役職として誤集計されるため）:

- `SERIALIZED_IN`「連載」: 雑誌（COMPANY エントリ）のみ
- `MANGA`「漫画」: 漫画家（PERSON エントリ）のみ

表示上は `SERIALIZED_IN` テンプレの中で兄弟役職参照構文 `{ROLE:MANGA.PERSONS}` を使い、同 Group 内の MANGA 役職下の人物を取り込む。テンプレ内の漫画役職ラベルはプレースホルダ `{ROLE_LINK:code=MANGA}` で、役職詳細ページ `/creators/roles/manga/` への太字リンク。

**テンプレ（`SERIALIZED_IN`）**:
```
{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
{ROLE_LINK:code=MANGA}・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

**テンプレ（`MANGA`）**: `{PERSONS}` のみ（普段は `{ROLE:MANGA.PERSONS}` 経由で間接評価）

**期待する展開結果**:
```
連載 講談社「なかよし」
     漫画・上北 ふたご
     「たのしい幼稚園」
     「おともだち」ほか
```

**期待する HTML 出力**:
```html
<a href="/companies/6/">講談社</a>「<a href="/companies/6/">なかよし</a>」
<strong><a href="/creators/roles/manga/">漫画</a></strong>・<a href="/persons/5/">上北 ふたご</a>
　「<a href="/companies/6/">たのしい幼稚園</a>」
　「<a href="/companies/6/">おともだち</a>」ほか
```

**ブロック構成**:
```
Card / Tier / Group
├─ Role: SERIALIZED_IN 連載 (order_in_group=1)
│   ├─ Block #1 (1 cols, 1 entries) ← {#BLOCKS:first}
│   │   leading_company_alias_id = 「講談社」屋号 ID
│   │   └─ [COMPANY] #1 「なかよし」屋号
│   ├─ Block #2 (1 cols, 1 entries) ← {#BLOCKS:rest} の最初
│   │   └─ [COMPANY] #1 「たのしい幼稚園」屋号
│   └─ Block #3 (1 cols, 1 entries) ← {#BLOCKS:rest} の続き
│       └─ [COMPANY] #1 「おともだち」屋号
└─ Role: MANGA 漫画 (order_in_group=2)
    └─ Block #1 (1 cols, 1 entries)
        └─ [PERSON] #1 「上北 ふたご」名義
```

**ポイント**:
- `{ROLE_LINK:code=MANGA}` は役職コードから役職詳細ページへのリンク化済み HTML を太字付きで埋め込むプレースホルダ。SiteBuilder 側は `<strong><a href="/creators/roles/manga/">漫画</a></strong>`、Catalog 側プレビューは `<strong>漫画</strong>`（リンクなし）を出力。`<strong>` ラップはレンダラ側で一律付与
- `{ROLE:MANGA.PERSONS}` は兄弟役職参照の構文。同 Group 内で `role_code='MANGA'` の役職を 1 つ探し、その役職配下の Block 群を一巡りして `{PERSONS}` を Block ごとに評価
- `{ROLE:CODE.PLACEHOLDER}` の 1 段ネスト不可（無限ループ防止）
- 雑誌名（「なかよし」など）は屋号マスタ `company_aliases` に別エントリとして登録する運用
- `{COMPANIES:wrap=""}` の wrap オプションは `「」` の括弧文字
- 漫画家共著の場合は `MANGA` 役職下の Block 内に PERSON エントリを 2 件並べる

#### オープニング主題歌（`THEME_SONG_OP`）/ エンディング主題歌（`THEME_SONG_ED`）

**テンプレ（OP）**:
```
{ROLE_NAME}
{THEME_SONGS:kind=OP}
```

**期待する展開結果（OP の例）**:
```
オープニング主題歌
「DANZEN! ふたりはプリキュア」
作詞:青木久美子
作曲:小杉保夫
編曲:佐藤直紀
うた:五條真由美

マーベラスエンターテイメント
```

**ブロック構成**:
```
Role: THEME_SONG_OP オープニング主題歌 (order 1)
├─ 📀 Song(OP): ... ← episode_theme_songs から動的取得（仮想ノード、編集不可）
└─ Block #1 (1 cols, 1 entries) ← レーベル表記用のブロック
    └─ [COMPANY] #1 「マーベラスエンターテイメント」屋号
```

**ポイント**:
- 楽曲は `episode_theme_songs` テーブルが真実の源泉。ツリー上は読み取り専用の楽曲仮想ノードとして自動表示
- レーベル名（販売元）はクレジット表記される文字列なので、ブロックの `[COMPANY]` エントリで明示的に持つ

#### 主題歌（黎明期 OP+ED 統合）（`THEME_SONG_OP_COMBINED`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=OP+ED,columns=2}
```

**ブロック構成**:
```
Role: THEME_SONG_OP_COMBINED 主題歌 (order 1) [横 2 カラム表示指定]
├─ 📀 Song(OP): 『DANZEN! ふたりはプリキュア』 ...
├─ 📀 Song(ED): 『ゲッチュウ! らぶらぶぅ?!』 ...
└─ Block #1 (1 cols, 1 entries)
    └─ [COMPANY] #1 「マーベラスエンターテイメント」屋号
```

OP 曲と ED 曲が「主題歌」という 1 枠の中に 2 カラム横並びで並ぶ表現。ED カードには別途主題歌役職を置かない（黎明期は OP 1 枠だけが主題歌枠）。

#### 挿入歌（`INSERT_SONG`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=INSERT}
```

**ブロック構成**:
```
Role: INSERT_SONG 挿入歌 (order 1)
├─ 📀 Song(INSERT): ... ← 1 曲または複数曲
└─ Block #1 (1 cols, 1 entries)
    └─ [COMPANY] #1 レーベル屋号
```

複数曲ある場合は `episode_theme_songs.insert_seq` の昇順で縦並びで全部出る。

#### 挿入歌（ノンクレジット）（`INSERT_SONGS_NONCREDITED`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=INSERT}
```

実放送ではクレジットされなかったが楽曲事実としてデータベースに保持しておきたい挿入歌枠。役職コード上は `INSERT_SONG` と同じ `kind=INSERT` を引くが、運用上はどちらか一方だけ置く前提。楽曲ノードラベルに `🚫[ノンクレジット]` マークが付与される。

#### 通常役職（`PRODUCER` / `ORIGINAL` / `MUSIC` 等の人物 1 〜複数列挙）

**テンプレ（既定）**:
```
{ROLE_NAME}
{#BLOCKS}{PERSONS}{/BLOCKS}
```

**ブロック構成**:
```
Role: PRODUCER プロデューサー (order 1)
└─ Block #1 (1 cols, 1 entries)
    ├─ [PERSON] #1 「西澤萌黄」名義（所属:ABC）
    ├─ [PERSON] #2 「高橋知子」名義（所属:ADK）
    └─ [PERSON] #3 「鷲尾天」名義
```

1 ブロックに人物名義をすべて並べるシンプルな形式。`{PERSONS}` プレースホルダの既定 `sep="、"` で読点区切り。

#### キャラクター × 声優（`VOICE_CAST`）

**ブロック構成**:
```
Role: VOICE_CAST 声の出演 (order 1)
└─ Block #1 (m×n)
    ├─ [CHARACTER_VOICE] #1 キャラ「美墨なぎさ」 / 声優「本名陽子」
    ├─ [CHARACTER_VOICE] #2 キャラ「雪城ほのか」 / 声優「ゆかな」
    └─ ...
```

`[CHARACTER_VOICE]` エントリは「キャラクター名義（`character_aliases`）+ 声優名義（`person_aliases`）」のペアで 1 行を成す。`character_alias_id` の代わりに `raw_character_text`（モブ等のマスタ未登録）も使える。

#### 制作協力 / 制作（ロゴ列挙系）

**ブロック構成**:
```
Role: PRODUCTION_COOPERATION 制作協力 (order 1)
└─ Block #1 (1 cols, 1 entries)
    └─ [LOGO] #1 「東映」マーク+横書きゴシック

Role: PRODUCTION 制作 (order 2)
└─ Block #1 (1 cols, 1 entries)
    ├─ [LOGO] #1 「ABC」ABC(1989年3代目ロゴ)
    ├─ [LOGO] #2 「ADK」ADK(2002年ロゴ)
    └─ [LOGO] #3 「東映アニメーション」東映アニメーション(通常ロゴ)
```

`[LOGO]` エントリはロゴ画像と CI バージョン（時期）を持つ。同じ会社でもロゴが時期によって違うため、バージョン別管理。

#### 共通の運用ルール

- **`leading_company_alias_id`** はブロック先頭に企業屋号を出すケースの特殊フィールド。連載や特殊な役職でのみ使う
- **`is_broadcast_only`** はブロック・エントリ単位のフラグ。本放送と円盤・配信でロゴ画像が違う等の差し替えを `is_broadcast_only=0`（既定行）と `=1`（本放送限定行）の 2 行並立で表現
- **`role_format_kind = 'THEME_SONG'`** の役職にはツリー上で楽曲仮想ノード（📀 Song）が自動表示される。`THEME_SONG_OP` / `THEME_SONG_ED` / `THEME_SONG_OP_COMBINED` / `INSERT_SONG` / `INSERT_SONGS_NONCREDITED` の 5 役職が該当
- **テンプレ DSL の `{#BLOCKS:first|rest|last}`** はブロックの位置指定ループ。`{#BLOCKS}`（filter なし）は全ブロック

---

### Web 公開用静的サイト生成

`PrecureDataStars.SiteBuilder` はローカル MySQL の内容を読み出して Web 公開用の静的 HTML 一式を生成するコンソールアプリ。`precure.tv` での Web 公開を想定するが、ツール自体は AWS 連携を含まず DB → 静的ファイル変換のみ（成果物の S3 等への同期は手動 `aws s3 sync` 等を別途想定）。

#### A. 前提・実行方法

1. Catalog GUI / Episodes GUI と同じ MySQL データベース（`precure_datastars`）が稼働
2. `PrecureDataStars.SiteBuilder/App.config.sample` を `App.config` にコピーし、`DatastarsMySql` 接続文字列を書き換え
3. 同じ `App.config` の `appSettings` で出力先・ベース URL・サイト名を指定:
   - `SiteOutputDir`: 生成 HTML 一式の出力先ディレクトリ（絶対パス推奨。空のときは実行ファイル直下 `out/site/`）
   - `SiteBaseUrl`: canonical / OGP / sitemap.xml の絶対 URL 組み立て用ベース URL（末尾スラッシュなし、例 `https://precure.tv`）。空のときは canonical 出力をスキップ
   - `SiteName`: ヘッダ・タイトルに表示するサイト名（既定 `precure-datastars`）
   - `AmazonAssociateTag`: Amazon アソシエイトのトラッキング ID（例 `yourtag-22`）。商品詳細の Amazon リンクに `?tag=` として付与しアフィリエイト計測に使う
4. ビルド & 実行:
   ```bash
   dotnet run --project PrecureDataStars.SiteBuilder -c Release
   ```
5. 出力先（既定 `out/site/`）に静的 HTML 一式と `assets/site.css` が生成される

#### B. 生成されるページ

| URL パターン | 内容 |
|---|---|
| `/` | サイトトップ。シリーズ一覧をグリッド表示し、本サイトの特徴を紹介 |
| `/about/` | サイト案内・運営者情報・権利表記 |
| `/series/` | 全シリーズ索引。種別・話数併記 |
| `/series/{slug}/` | シリーズ詳細。基本情報 → 関連作品 → プリキュア → メインスタッフ → BGM リスト → エピソード一覧 → 劇伴 → 外部サイト |
| `/series/{slug}/{seriesEpNo}/` | エピソード詳細（中核ページ） |
| `/creators/` | クリエーターのランディング。スタッフ / 声の出演の 2 カードを案内 |
| `/creators/staff/` | スタッフ一覧。役職順（既定）/ 五十音順 / 初参加順（シリーズ別セクション）/ 参加話数が多い順 の 4 タブ。役職順以外は人物と企業・団体を 1 リストに混在（個人/団体バッジ＋絞り込みトグル）。一度もクレジットの無い役職は索引にも役職詳細ページにも出さない |
| `/creators/roles/{role_code}/` | 役職詳細。当該役職に関わった人物・企業/団体を 1 リストに混在し、五十音順 / 初参加順 / 担当話数が多い順 のタブで切替 |
| `/creators/voice-cast/` | 声の出演一覧。1 行＝(声優 × シリーズ × キャラ) の粒度。キャラクター順（既定・シリーズ別セクション）/ 五十音順 / 初出演順（シリーズ別セクション）/ 出演話数が多い順 の 4 タブ |
| `/persons/{personId}/` `/companies/{companyId}/` | 人物・企業/団体の個別詳細（直リンク用） |
| `/stats/` | 統計ランディング。サブタイトル統計・エピソード尺統計の 2 系統 |

トップページの DB 統計ボックスでは人物数と企業・団体数を合算した「クリエーター」1 項目（`DbStats.CreatorsCount` = 人物数＋企業・団体数）として表示し、リンク先は `/creators/` ランディング。

##### クリエーターセクションと役職詳細ページの URL

`CreatorsGenerator` が `/creators/`（ランディング）・`/creators/staff/`（スタッフ一覧）・`/creators/roles/{role_code}/`（役職詳細）・`/creators/voice-cast/`（声の出演一覧）の 4 種を生成する。人物・企業/団体は「順位」を持たずタブ（五十音順／初参加順／担当話数が多い順 ほか）での並べ替えのみを提供。スタッフ一覧は人物と企業・団体を 1 リストに混在させ、行ごとに個人/団体バッジで区別、上部トグルで個人のみ・団体のみへ絞り込める。役職詳細ページは `/creators/roles/{role_code}/` 形式。`roles` テーブルは業務コード `role_code`（`varchar(32)`・`utf8mb4_bin`）を PK とし数値サロゲートを持たないため、URL パス上の役職コードは `string.ToLowerInvariant()` で小文字化する（例: `/creators/roles/screenplay/`）。出力先パスと参照リンクは `PathUtil.RoleStatsUrl()` に集約。閲覧者向けに不要な内部コード（役職コード／区分）は表示せず、日本語の役職名のみを出す。

声の出演一覧は (声優 × シリーズ × キャラ) ごとに 1 行へフラット展開する。1 行の出演話数は当該 (声優 × シリーズ × キャラ) の重複排除済みエピソード数で、シリーズ全体スコープのみのクレジットは話数「—」表示の 1 行として残す。順位はサブタイトル統計・エピソード尺統計のみに Wimbledon 形式で用い、人物・企業/団体・声優には用いない。

並べ替えの一貫方針として、五十音順以外のすべてのタブでは「同じ話数内では初めてクレジットされた位置の順」を暗黙の副ソートキーとする。`CreditInvolvementIndex` が、クレジット階層を表示順（同一エピソード内の credit レコードを明示順序カラム `credits.credit_seq` 昇順 → credit_id 昇順で並べ、各 credit 内を card_seq → tier_no → group_no → order_in_group → block_seq → entry_seq）でエピソード単位に走査する過程で、各関与に 0 始まりの出現連番 `Involvement.CreditSeq` を採番する。`credits` テーブルはクレジット階層の最上位で、明示順序カラム `credit_seq`（smallint unsigned, 同一スコープ内 1 始まり, `UNIQUE(series_id,credit_seq)` / `UNIQUE(episode_id,credit_seq)`）を持つ。WinTools のクレジット編集画面にはクレジット一覧の ↑↓ 並べ替えボタンがあり、`CreditsRepository.BulkUpdateSeqAsync` で即時 DB 反映する。新規クレジットの `InsertAsync` は同一スコープ内 `MAX(credit_seq)+1` を自動採番。集計側は (シリーズ放送開始日, シリーズ内話数) が同点になった行・役職を、この最小 `CreditSeq` の昇順で並べる。`roles.display_order` はマスタ管理画面のグリッド表示順を決めるだけの値で、公開サイトの並べ替えには用いない。完全同点（同一話・同一クレジット位置で初出）の場合にのみ内部 `role_code` で安定化する。五十音順タブは読み仮名で並びが完全に一意に定まるためこの副キーは挟まない。キャラクター一覧（`/characters/`）も同方針で、character_kind セクション内のキャラ配列を読み仮名順ではなく「最も早くクレジットされた位置」順に統一する。

主題歌・劇伴スタッフ（`song_credits` / `song_recording_singers` / `bgm_cue_credits` 由来）は曲・録音単位のマスタから `episode_theme_songs` 経由でエピソードに紐づくため、それ自体はクレジット階層上の物理位置を持たない。`CreditInvolvementIndex` は階層走査時に THEME_SONG 形式の役職ブロックへ到達した時点の `CreditSeq` を「(エピソード, 親 credit の kind=OP/ED)」をキーに控え、主題歌スタッフへ `theme_kind`（OP/ED/INSERT）に応じてその位置を継承させる（OP 主題歌→OP クレジット内の主題歌ブロック位置、ED→ED、INSERT 等の親 kind 非対応は同エピソード最初の主題歌ブロック位置にフォールバック、主題歌ブロックが階層に無ければクレジット末尾相当）。劇伴は同エピソードの主題歌ブロック位置→末尾相当の順でフォールバック。

初参加順・キャラクター順・初出演順の各タブは、シリーズごとのセクション（`<section>` ＋シリーズ名見出し、放送開始日昇順）に束ねる。シリーズ名・年は見出しに集約するため、これらのタブを含めすべての表からシリーズ列を撤去する。スタッフ一覧の列構成は、五十音順＝区分／名前／読み／役職／参加話数、初参加順＝シリーズ別セクション＋同上、参加話数が多い順＝同上。役職詳細は五十音順／初参加順（シリーズ別セクション）／担当話数が多い順の 3 タブで、各行は区分／名前／読み／担当話数。声の出演はキャラクター順を筆頭・既定タブ。キャラクター順はキャラクターを主たる単位とし、各シリーズセクション内で「キャラが最初にクレジットされた位置」順にキャラを並べ、同一キャラを複数声優が演じる場合（交代・代役）はその声優行を当該キャラの位置に連続してまとめて表示する。

##### エピソード行レイアウト（一覧系の共通構造）

エピソードを一覧表示する画面は `episodes-index-section` 構造に統一されている。`<section class="episodes-index-section">` → `<h2 class="episodes-index-heading">` → `<ol class="episodes-index-list">` → `<li class="ep-row">` → `.ep-row-main`（`.ep-row-no-date`＝第N話＋放送日の縦積み、`.ep-row-title`、`.staff-badges-row`）の入れ子。対象は `/episodes/`・シリーズ詳細「エピソード一覧」・ホーム「今後の放送予定」「最新エピソード」。サブタイトルは全画面共通の `.ep-row-title`（CSS で `font-weight: 600`）で、`title_rich_html`（ルビ付き HTML）があればそれを優先し、無ければ `title_text` のエスケープ平文を表示。放送日は密表示用の「2024.2.4」形式（`JpDateFormat.DotDate`）。

シリーズ詳細「エピソード一覧」は単一シリーズなので `episodes-index-section` を 1 個出す。外側の `<section id="episode-list">` 枠・`<h2>エピソード一覧</h2>` 見出し・エピソード未登録時表示は保持し、内側のみ `episodes-index-section` で構成する。シリーズ詳細ではすぐ上の基本情報に年度・放送期間・全話数が出ていて重複するため、シリーズ単位の見出し行 `<h2 class="episodes-index-heading">` はシリーズ詳細テンプレからは出さない。枠線ボックスの体裁も `site.css` の `#episode-list .episodes-index-section` でシリーズ詳細スコープ限定に解除し、素のリストとして見せる。ホームの「今後の放送予定」「最新エピソード」は外側の `<section id="upcoming-episodes">`／`<section id="latest-episodes">` 枠と各 `<h2>` 見出しを保持し、その下にシリーズ単位の `episodes-index-section` を入れ子で並べる。並び順は「今後の放送予定」＝放送日昇順、「最新エピソード」＝放送日降順で、セクション（シリーズ）の並びも各シリーズ内の最小（昇順時）／最大（降順時）放送日で同方向。ホーム内側の `episodes-index-section` には `id` を付けない（左サイドのセクションナビの重複表示防止）。ホームのエピソード staff サマリは `SeriesGenerator.GetEpisodeStaffSummaries()` の memoize 結果を参照するため、ビルドパイプラインは `SeriesGenerator` → `HomeGenerator` の順で実行する。

ホーム「今日の記念日」は閲覧日基準の JS 動的描画（`anniversaries.js`）で、ep-row 構造に準じた表示。1 話ずつ放送年代が異なるため `episodes-index-heading` は出さず、各 ep-row の上に「n年前　シリーズ (放送年度)」のシリーズ表記行を添える。記念日行はスタッフバッジ段を出さないため、記念日 JSON（`home-anniversary-data`）にはスタッフ HTML を載せず、シリーズ放送年度（`sy`）のみを持たせる。

統計のエピソード単位ページ（パート尺の長短・アバンタイトル尺の長短・アバンタイトルスキップ回・中 CM 入り時刻の早遅・サブタイトル文字数/漢字率/記号率の多少）は専用の `stats-ep-list` レイアウトで全ページ統一。1 行は左ブロック（順位＋指標値を横並び・本文の約 1.77 倍サイズで `<li>` 内上下中央。指標値は数値のみ＝文字数 / `m:ss` / `h:mm:ss` / 百分率）と右ブロック（上段＝「シリーズ名（シリーズ詳細へのリンク） (年度)」、下段＝第N話のみ＋ルビ付きで改行を除去したサブタイトル〔エピソード詳細へのリンク〕）で構成。同率順位が連続する場合は 2 件目以降の順位表示を空文字にする。アバンタイトルスキップ回は指標値を持たないが、パネルのデザイン・グリッドは他のエピソード単位ページと完全同一とし、左パネルに順位ではなく放映順の回次（1 始まりの連番）を入れる。ルビ付きサブタイトルの補完は `BuildContext` の `LookupEpisodeBySeriesEpNo` でメモリ上のエピソードを引き、行組み立て・同率ブランク化・改行除去は共有ビルダー `StatsEpisodeRows` に集約。使用文字 TOP100（全文字／漢字限定の 2 ページ）も同じ脱テーブル方針で、左は共通の `stats-ep-rank` アクセントパネル（順位＋出現回数）を再利用し、右に対象文字を正方形セル内で上下左右中央寄せした超特大（`stats-char-glyph`）で表示。記号初出現順ページは構造が異なるため従来テーブルのまま。「シリーズ別 TOP5 漢字」（漢字＋繰り返し記号「々」限定、各シリーズ最頻漢字 TOP5、DENSE_RANK の同点同順）は索引「シリーズ別」グループの TOP5 文字直下に配置する。集計は `GetTopKanjiBySeriesAsync`。「記号出現回数・初使用エピソード」ページも脱テーブル化：使用文字 TOP100 と同じ大きな記号グリフ＋出現回数のみのパネル（順位を持たないため `stats-ep-rank value-only` で順位カラムを詰める）に、右側へ初使用エピソード（シリーズと年度／下段は「放送日（左寄せ・括弧なし YYYY.M.D・muted）｜第N話（右寄せ）｜ルビ付きサブタイトル（左寄せ）」を固定幅 3 カラムで全行整列）を並べる。

ホーム「今日の記念日」は誕生日対応。埋め込み JSON `home-anniversary-data` はエピソード放送日に加えて、映画公開日・キャラクター誕生日・人物誕生日を 1 配列に種別タグ `k`（`ep` / `mv` / `cb` / `pb`）付きで内包する（プロパティ名は容量削減のため短縮形。`HomeGenerator.BuildCalendarDataJsonAsync`）。`anniversaries.js` は `k==='ep'` のみエピソードロジック（今日／今週）に流し、`cb` / `pb` のうち閲覧日と月日が一致するものを「今日の記念日」内でエピソード行より上に積む。キャラクター誕生日は対象を `character_kind` が `PRECURE` / `ALLY` のキャラに限定し、`characters.name` をシリーズ表記行とともに表示（プリキュアはキーカラーバッジを添える。バッジ文字色は地色の相対輝度から暗/明グレーを自動算出）。代表シリーズは「`series_precures` のうち放送開始が最も早いシリーズ」、代表 precure は「最小 `precure_id`」で決定的に解決。人物誕生日は氏名リンクで、生年は `birth_year_visibility = 'PUBLIC'` かつ判明時のみ年齢を併記。映画（`mv`）は今日の記念日には出さずカレンダー専用。

ホーム「今月のカレンダー」は、初期表示として閲覧した瞬間の当月 1 か月分を表示する JS 動的カレンダー（`calendar.js`、`home-anniversary-data` を `anniversaries.js` と共有）。キャプションは「‹ 前月ボタン｜大きな月名（中央揃え）｜翌月ボタン ›」の一行構成。1 月→前年 12 月・12 月→翌年 1 月の年跨ぎも処理。各日セルにその月日の項目をチップで縦に積み、優先順は**キャラクター誕生日 > 映画公開日 > 人物誕生日 > TV 放送**。プリキュア誕生日チップは変身前名義をキーカラーバッジで、`ALLY` は名称の素バッジで表示。チップ先頭に種別絵文字を付す：TV 放送は 📺、映画は 🎬、キャラクター／人物誕生日は 🎂。映画はシリーズ略称、TV 放送は「シリーズ略称＋#話数」で表示（カレンダー UI に限り `title_short` を用いるポリシー例外）。本日セルのハイライトは実際の当月を表示しているときだけ付与し、日曜・土曜は配色で区別。データ空・JS 無効環境では section ごと非表示。凡例を併記。

##### シリーズ一覧 TV サブ行（プリキュア／スタッフ バッジ）

`/series/` の TV シリーズセクション（カード型リスト `series-card-list`）は、各 TV シリーズカードの本体（番号／タイトル／放送期間／話数）の下にサブ行（プリキュアバッジ → スタッフバッジ）を出す。いずれの段も該当データが 0 件ならその段を出さず、両方 0 件ならサブ行自体を出さない。集計対象は TV シリーズのみで、映画・スピンオフ等のセクションはサブ行を持たない。

プリキュアバッジは当該シリーズに紐付くプリキュア（`series_precures`、`display_order` 昇順・同値時 `precure_id` 昇順）を 1 体 1 バッジで並べる。各バッジは `/precures/{precure_id}/` へのリンクで、表記はプリキュア観点の名義連結：`transform_alias_id`（変身後）→ `transform2_alias_id`（変身後 2）→ `pre_transform_alias_id`（変身前）の各 `character_aliases.name` を、この順で「 / 」連結する（NULL・未解決の名義は除外。すべて空のときのみ変身後名義へフォールバック）。`characters.name` は表記には用いない。標準担当声優（`precures.voice_actor_person_id`）が登録されていれば連結名の後ろに「 (CV: ○○)」を付す。この名義連結ロジックは `/precures/{id}/` 詳細ページの h1 と共有する（共有ヘルパ `PrecureNaming.JoinAliasNames`）。

バッジの地色はプリキュアマスタの `key_color`（`char(7)`・`#RRGGBB`・NULL 可、フォーマットは CHECK 制約 `ck_precures_key_color` で担保）。文字色は地色を WCAG 2.x 定義の相対輝度（linearized sRGB の加重和 0.2126R + 0.7152G + 0.0722B）に変換し、しきい値 0.179 を境に暗グレー `#1a1a1a` ／明グレー `#f5f5f5` を自動で出し分ける。ボーダーは文字色側に寄せた半透明色（暗文字側 `rgba(0,0,0,.22)`／明文字側 `rgba(255,255,255,.30)`）。地色・文字色・ボーダーはビルド時に算出され、バッジ要素のインライン `style` として出力する。`key_color` 未設定または不正値のプリキュアはインライン色を持たず、`.precure-badge` の CSS 既定で描画される。色解決は `SeriesGenerator.ResolveBadgeColors` に集約、バッジ整形は `BuildPrecureBadges`。

##### 継続中シリーズと放送見込み（未完）表記

`series_kinds.credit_attach_to='EPISODE'` のシリーズ（TV / SPIN-OFF / OTONA / SHORT）は、終了日（`end_date`）が未設定なら期間を「2025年2月2日 〜」と「〜」止めで継続中を示す（`JpDateFormat.PeriodOrOngoing`）。`credit_attach_to='SERIES'` のシリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）は単一時点扱いで `end_date` が `NULL` でも開始日単独表記のまま。シリーズ詳細ページの基本情報テーブル左セルも同じ方針に揃え、EPISODE 系は「期間」、SERIES 系は「公開」を `<th>` に出す。

ビルド時点で終了していない（`end_date` が `NULL` もしくはビルド時刻より未来）かつ総話数マスタ値（`series.episodes`）があるシリーズは「放送見込み（未完）」とみなし、期間の終了日と総話数の直後に「（見込）」を添える。終了済みシリーズは登録済みエピソードレコード数と総話数マスタ値にギャップがあっても確定扱いとする（データ入力残と見込みは別問題なので一緒にしない）。総話数マスタ値が未設定（`NULL`）のシリーズは比較不能なので見込み扱いにしない。総話数マスタ値が未設定かつ継続中（EPISODE 系で `end_date=NULL`）の場合、`/episodes/` 索引やシリーズ詳細のエピソード一覧見出しに出していた「N 話」フォールバックも空文字に切り替えて出さない（確定値が無いため）。

##### シリーズ一覧の映画セクション

`/series/` の映画セクションはカード型リスト（`series-card-list`）として親映画（`kind_code ∈ {MOVIE, SPRING}`）を公開日昇順で並べる。秋映画（`MOVIE`）／春映画（`SPRING`）のシーズンバッジ（`.movie-badge-fall` / `.movie-badge-spring`）はメタ行に並ぶ。親映画には公開日と、親＋全子（`MOVIE_SHORT`）の合計上映時間を出す（いずれかが `run_time_seconds` NULL なら空）。親映画にぶら下がる子作品（`MOVIE_SHORT`、`seq_in_parent` 昇順）は親カードの中の小リスト（`<ul class="movie-child-list"><li>…</li></ul>`）として箇条書きで並べる。子作品はリンクを張らない。子単体の上映時間（`RelatedSeriesRow.RuntimeLabel`、`run_time_seconds` NULL なら空）を併記。

#### C. エピソード詳細ページの構成（中核）

`/series/{slug}/{seriesEpNo}/` には次の情報を 1 ページに集約する:

1. **サブタイトル表示**: `title_rich_html`（ルビ付き HTML）があればそのまま流す。なければ `title_text` をプレーン表示。下に `title_kana` を補助表示
2. **基本情報テーブル**: 放送日時・シリーズ内話数・通算話数・通算放送回・ニチアサ通算放送回、外部 URL（東映あらすじ／ラインナップ）、YouTube 予告埋め込み（`youtube_trailer_url` から ID を抽出して `<iframe>` 化）
3. **フォーマット表**: `episode_parts` から OA / 配信 / 円盤の各バージョンの「累積開始時刻」と「尺」を併記する 22 パート種別対応の表。各媒体ごとに独立して累積タイムコードを計算し、当該媒体に該当パートが無い場合は空セル（—）扱いにして加算しない。フッタに媒体別の総尺を表示
4. **サブタイトル文字情報**: `TitleCharInfoRenderer` で、サブタイトル中の登場順ユニーク文字ごとに `EpisodesRepository.GetFirstUseOfCharAsync` で初出話を、`GetEpisodeUsageCountOfCharAsync` で総使用話数を、`GetTitleCharRevivalStatsAsync` で 1 年以上ぶりの復活情報を取得し、「`「文字」… [初出] [唯一] N年Mか月(P話)ぶりQ回目 『シリーズ』第N話「サブタイトル」(YYYY.M.D)以来`」形式の HTML を生成。badge は CSS で色分け（初出 = 黄、唯一 = ピンク）
5. **サブタイトル文字統計**: `episodes.title_char_stats` JSON の `length`（書記素数・コードポイント数・ユニーク書記素数・空白数）と `categories`（漢字 / ひらがな / カタカナ / 英字 / 数字 / 記号 / 句読点 / 絵文字 / その他）をテーブル化。JSON が NULL / 異常値のときは黙ってフォールバック
6. **パート尺偏差値**: `EpisodePartsRepository.GetPartLengthStatsAsync` を直接呼び出し、AVANT / PART_A / PART_B のシリーズ内および全シリーズ横断（歴代）の順位・偏差値を表示。Episodes エディタと同じ計算ロジック（MySQL のウィンドウ関数 `RANK / AVG / STDDEV_POP`）
7. **主題歌**: `episode_theme_songs` から OP / ED / 挿入歌（最大 OP 1 + ED 1 + INSERT 複数）を取り出し、`song_recordings` → `songs` を JOIN で引いて表示。`is_broadcast_only=1` 行は「（本放送のみ）」マーカー付きで併記
8. **クレジット階層**: `credits.scope_kind = 'EPISODE'` のクレジット（OP / ED）について、`Card → Tier → Group → Role → Block → Entry` の階層を構造保持で HTML 化。Entry は 5 種別（PERSON / CHARACTER_VOICE / COMPANY / LOGO / TEXT）に対応:
   - `PERSON`: `person_aliases.display_text_override` 優先、なければ `name`
   - `CHARACTER_VOICE`: 「キャラ名義（CV: 声優名義）」形式
   - `COMPANY`: 屋号
   - `LOGO`: `[屋号#CIバージョン]` のテキスト表現
   - `TEXT`: フリーテキスト退避口
   - `affiliation_company_alias_id` / `affiliation_text` は小カッコ所属表記
   - `is_broadcast_only=1` のエントリは「（本放送のみ）」マーカー付き
   - `credit_role_blocks.col_count` は CSS Grid `grid-template-columns: repeat(N, ...)` でカラム数として反映
9. **前後話ナビ**: 同シリーズ内の前後話（series_ep_no ベース）へのリンク

#### D. テンプレートとスタイル

- テンプレートエンジン: Scriban（`Templates/*.sbn`）
- 共通レイアウト: `_layout.sbn`（ヘッダ・フッタ・パンくず・canonical タグ・OGP メタ等を吸収）
- レンダリングは 2 段階: 各 Generator が「コンテンツテンプレ」を model でレンダリング → 結果 HTML を `LayoutModel.Content` に詰めて `_layout.sbn` を再レンダリング
- スタイル: `wwwroot/assets/site.css` 1 ファイル。CSS フレームワーク不採用、最低限の素朴スタイル。CSS 変数で色を管理
- 静的アセットは `wwwroot/` 配下を出力ルートに丸ごとコピー（`SiteBuilderPipeline.CopyStaticAssets`）

#### E. ログ・サマリ

ビルドの最後にコンソールへサマリが出力される:

```
== Build Summary ==
  Pages generated   : 1234
    home            : 1
    about           : 1
    series          : 31
    episodes        : 1201
  Warnings          : 5
  Elapsed           : 12.3 sec
```

警告は `BuildLogger.Warn` 経由で集計される。代表的な警告:
- `title_char_stats が未生成` のエピソード（Catalog 側でサブタイトル編集を再保存すれば再計算される）
- 役職マスタに無い `role_code` がクレジットで参照されている

---

## データベーススキーマ

##### 誕生日データモデル（persons / characters）

人物（`persons`）とキャラクター（`characters`）は誕生日を 4 カラムで保持する。生年は「不明」「判明・公開」「判明・非公開（本人スタンス尊重）」の 3 状態を表す必要があるため、`DATE` 1 本ではなく月日正規化＋生年＋公開可否で持つ（部分日付モデル）。

- `birth_year`（`smallint unsigned` NULL）… 判明していれば西暦。不明は `NULL`。`birth_year_visibility` が `PRIVATE` でも値の保持自体は可
- `birth_year_visibility`（`varchar(16)` NOT NULL 既定 `PUBLIC`）… `PUBLIC`＝サイト生成に生年・年齢を出す／`PRIVATE`＝本人スタンス尊重で生年・年齢を生成に出さない。CHECK 制約で固定
- `birth_month`（`tinyint unsigned` NULL、1–12）／ `birth_day`（`tinyint unsigned` NULL、1–31）… 誕生月日。`birth_year_visibility` に関わらず常にカレンダー／記念日の判定単位（月日一致）。`birth_day` があるなら `birth_month` 必須（CHECK 制約 `ck_*_birth_day_needs_month`）

記念日・カレンダーは「閲覧日の月日」と各エンティティの `birth_month` / `birth_day` の一致だけを見る。生年・年齢の表示は `birth_year_visibility = 'PUBLIC'` かつ `birth_year` 非 NULL のときのみ。プリキュアの誕生日はプリキュア本体（`precures`）ではなく、対応キャラクター（`characters`、`transform_alias_id → character_aliases.character_id` 経由で解決）側で保持する（`precures` は誕生日カラムを持たない）。

DDL ファイル: [`db/schema.sql`](db/schema.sql)（新規構築用、全テーブル含む）
マイグレーション: [`db/migrations/`](db/migrations/) … バージョン別の差分 SQL 群。新規構築では不要。既存環境の更新時に順次適用。各スクリプトは冪等。ファイル名は `v<VERSION>_migration_<topic>.sql` 形式。

### ER 概要

```
series_kinds ──┐
               ├── series ──┬── episodes ──── episode_parts
series_relation_kinds ──┘    │            │
                              ├── (self-ref)  part_types
                              │
                              └── discs ── tracks ──┬── song_recordings ── songs
                                  ▲          │      │       │
                                  │          │      │       └── song_recording_bgm_assignments
                                  │          │      │                  │ (両性紐付け、パート別)
                                  │          │      └── bgm_cues ──────┘
                                  │          │         │ (M 番号)
                                  │          │         └── bgm_sessions
                                  │          │            (録音セッション)
                                  │          │
                                  │          └── video_chapters (BD/DVD チャプター)
                                  │
                              products
                              (販売単位メタ情報、series_id は持たない)

                              付随マスタ: product_kinds / disc_kinds / track_content_kinds /
                                           song_music_classes / song_size_variants / song_part_variants
```

> **シリーズの所在**: シリーズ所属は `products` ではなく `discs` 側の属性として持つ。1 商品内に複数シリーズのディスクが混在するケースや、1 シリーズに 1 ディスクだけが対応するケースの双方を自然に表現できる構造とするため。

---

### エピソード系テーブル

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
| `credit_attach_to` | ENUM('SERIES','EPISODE') | クレジット付与単位の宣言（既定 `EPISODE`） |

初期投入は 6 種：`TV`（TVシリーズ）/ `MOVIE`（秋映画）/ `MOVIE_SHORT`（秋映画 併映）/ `SPRING`（春映画）/ `SPIN-OFF`（スピンオフ）/ `EVENT`（イベント＝3D シアター上映等の特設枠）。`credit_attach_to` は `TV` / `SPIN-OFF` が `EPISODE`、映画系 3 種と `EVENT` が `SERIES`（`EVENT` はエピソード概念を持たずシリーズ単位でクレジットを保持）。

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
| `title_short` | VARCHAR(128) NULL | 略称（例: 「キミプリ」）。SiteBuilder の生成出力では原則として本列を一切参照しない（Web 表示は `title` のみ）。DB マスタ上は引き続き保持し、Catalog 側の編集 UI（コンボの省スペース表示など）で使用する。**唯一の例外**：ホーム「今月のカレンダー」の日セル内チップ（エピソード＝シリーズ略称＋話数、映画＝シリーズ略称）に限り、1 マスの限られた幅に収めるため `title_short` を用いる（未設定時は `title` にフォールバック） |
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
| `hide_storyboard_role` | TINYINT(1) NOT NULL DEFAULT 0 | 「絵コンテ」役職を独立表示せず「演出」と融合表示するか（プレビュー描画専用フラグ） |
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

**初期データ**:

| kind_code | name_ja | 細分条件 |
|---|---|---|
| `DRAMA` | ドラマ | — |
| `CHARA_ALBUM` | キャラクターアルバム | — |
| `CHARA_SINGLE` | キャラクターシングル | — |
| `LIVE_ALBUM` | ライブアルバム | — |
| `LIVE_NOVELTY` | ライブ特典スペシャルCD | — |
| `THEME_SINGLE` | 主題歌シングル | 下記以外 |
| `THEME_SINGLE_LATE` | 後期主題歌シングル | 所属シリーズ `kind_code='TV'` かつ 発売日 ≥ 放送開始年の 6/1 |
| `OST` | オリジナル・サウンドトラック | 下記以外 |
| `OST_MOVIE` | 映画オリジナル・サウンドトラック | 所属シリーズ `kind_code ∈ {MOVIE, SPRING}` |
| `RADIO` | ラジオ | — |
| `TIE_UP` | タイアップアーティスト | — |
| `VOCAL_ALBUM` | ボーカルアルバム | — |
| `VOCAL_BEST` | ボーカルベスト | — |
| `OTHER` | その他 | — |

※ 細分条件の判定は過去のデータ一括投入時にのみ適用された。Catalog GUI 上で手動編集する場合は任意のコードを選択可能。細分判定で参照される「所属シリーズ」は、グループ代表ディスクの `series_id` を用いる。

#### `disc_kinds` — ディスク種別マスタ

物理形状ではなく、商品内でのディスクの用途区分（本編・特典・ボーナス等）。

| 列名 | 型 | 説明 |
|---|---|---|
| `kind_code` | VARCHAR(32) PK | 種別コード（例: `MAIN`, `BONUS`, `KARAOKE`, `INSTRUMENTAL`） |
| `name_ja` | VARCHAR(64) | 日本語名 |
| `name_en` | VARCHAR(64) NULL | 英語名 |
| `display_order` | TINYINT UNSIGNED UNIQUE NULL | 表示順序 |

**初期データ**: なし。運用開始時に Catalog GUI の「マスタ管理」→「ディスク種別」タブから必要なコードだけを登録する設計。`discs.disc_kind_code` は NULL 許容 FK のため、未登録のまま運用しても既存データは破綻しない。

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

**初期データ**: `OP`（オープニング主題歌）, `ED`（エンディング主題歌）, `INSERT`（挿入歌）, `CHARA`（キャラクターソング）, `IMAGE`（イメージソング）, `MOVIE_OP`（映画オープニング主題歌）, `MOVIE_ED`（映画エンディング主題歌）, `MOVIE_INSERT`（映画挿入歌）, `OTHER`（その他）。

> 映画主題歌は `MOVIE_OP` / `MOVIE_ED` / `MOVIE_INSERT` の 3 種に分けて持つ。`display_order` の付番は `OP=1 / ED=2 / INSERT=3 / CHARA=4 / IMAGE=5 / MOVIE_OP=6 / MOVIE_ED=7 / MOVIE_INSERT=8 / OTHER=99`。

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

#### `product_companies` — 商品社名マスタ

商品（`products`）の発売元（label）／販売元（distributor）として紐付ける**クレジット非依存の社名マスタ**。クレジット系の `companies` / `company_aliases` とは完全に独立した別系統で、屋号系譜（前任/後任）の概念は持たない。1 社 = 1 行、和名・かな・英名のみのシンプル構造。

クレジット側と分離した理由：商品の流通元（レーベル名や販売元名）は「そのレコードのジャケット・帯にどう書かれていたか」のスナップショット名義であり、クレジット集計に混ぜると意味的にズレた合算が起きやすい。商品メタ専用の小さなマスタとして独立させることで、表示・JSON-LD 出力は構造化 ID 経由で安定させつつ、クレジット側の集計は純粋に「作品制作・配給に関与した企業」だけで保てる。

| 列名 | 型 | 説明 |
|---|---|---|
| `product_company_id` | INT PK AUTO_INCREMENT | 主キー |
| `name_ja` | VARCHAR(128) NOT NULL | 社名（日本語） |
| `name_kana` | VARCHAR(128) NULL | かな |
| `name_en` | VARCHAR(128) NULL | 英名 |
| `is_default_label` | TINYINT DEFAULT 0 | 新規商品作成時のレーベル既定（マスタ全体で最大 1 行のみ true） |
| `is_default_distributor` | TINYINT DEFAULT 0 | 新規商品作成時の販売元既定（同上） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**インデックス**: `name_ja`、`name_kana`（picker の前方一致 / 部分一致検索向け）。

**FK 制約**: `products.label_product_company_id` / `products.distributor_product_company_id` から参照される。論理削除しても `ON DELETE SET NULL` で商品側の紐付けが外れるだけで、商品自体は残る。

**既定フラグの排他性**: 同フラグが立つ行はマスタ全体で最大 1 行という制約はアプリ側（`ProductCompaniesRepository.InsertAsync` / `UpdateAsync` 内のトランザクション）で担保。フラグを ON で保存しようとすると、他の全行の同フラグを 0 に落としてから対象行を 1 にセットする処理がトランザクション内で実行される。

**運用 UI**: メインメニュー「商品社名マスタ管理...」から CRUD。商品エディタの流通系行から「選択...」ボタンで picker（`ProductCompanyPickerDialog`）が呼ばれて紐付け先 ID をセットする。`NewProductDialog` は起動時に既定フラグ社を `GetDefaultLabelAsync` / `GetDefaultDistributorAsync` で取得し、ReadOnly TextBox に表示する。

#### `products` — 商品

販売単位としての商品。価格・発売日・販売元などの「商品メタ情報」を管理する。複数枚組の場合も 1 商品として扱い、ディスクは `discs` 側で品番単位に分割される。

> **シリーズ列を持たない**: 本テーブルは `series_id` 列を持たない。シリーズ所属は各 `discs` 行の `series_id` で判断する。

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
| `label_product_company_id` | INT FK NULL | レーベル（→ `product_companies`、ON DELETE SET NULL） |
| `distributor_product_company_id` | INT FK NULL | 販売元（→ `product_companies`、ON DELETE SET NULL） |
| `amazon_asin_cd` | VARCHAR(16) NULL | Amazon ASIN（物理パッケージ向け：CD/BD/DVD） |
| `amazon_asin_digital` | VARCHAR(16) NULL | Amazon ASIN（デジタル音源向け：Amazon Music の MP3 アルバム） |
| `cover_image_url` | VARCHAR(512) NULL | ジャケット画像 URL（Amazon CDN を直接参照するホットリンク。実体は保存しない） |
| `cover_image_source` | VARCHAR(16) NULL | 画像の取得元コード。`amazon_cd`（Creators API・CD ASIN 由来）／`amazon_digital`（Creators API・デジタル ASIN 由来）の 2 値 |
| `cover_image_fetched_at` | DATETIME NULL | 画像 URL の取得日時（鮮度判定用。AmazonSync は 90 日経過で再取得） |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

**CHECK 制約:**
- `ck_products_disc_count_pos`: `disc_count ≥ 1`
- `ck_products_price_ex_nonneg` / `ck_products_price_inc_nonneg`: 価格は NULL または 0 以上

> **補足**:
> - 「商品・ディスク管理」画面の商品一覧の既定並び順は `release_date ASC, product_catalog_no ASC`（発売日昇順、同日内は代表品番昇順）。`ProductsRepository.GetAllAsync` がこの順を返す。発売日降順が必要な照合系コード（`DiscMatchDialog` など）からは `GetAllDescAsync` を使う。
> - `price_inc_tax` は同画面の「自動計算」ボタン、もしくは商品保存時の自動補完で発売日と税抜価格から切り捨てで算出される。既存レコードの一括補完は `db/utilities/backfill_products_price_inc_tax.sql` を実行する。

#### `discs` — 物理ディスク

1 枚のディスクを表す。主キーは品番 (`catalog_no`)。複数枚組の場合は各ディスクが別品番を持ち、同じ `product_catalog_no`（代表品番）に紐付く。

> **シリーズ所属**: `series_id` 列はディスクの属性である（同じ商品内でもディスクごとに異なるシリーズを持ち得る）。

| 列名 | 型 | 説明 |
|---|---|---|
| `catalog_no` | VARCHAR(32) PK | 品番（例: アルバム `MJSA-01000` / シングル `MJSS-09000`） |
| `product_catalog_no` | VARCHAR(32) FK | 所属商品の代表品番（→ `products`、CASCADE） |
| `title` | VARCHAR(255) NULL | ディスクタイトル（複数枚組の各ディスクで異なる場合に使用） |
| `title_short` | VARCHAR(128) NULL | 略称 |
| `title_en` | VARCHAR(255) NULL | 英語タイトル |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = オールスターズ扱い、ON DELETE SET NULL、ON UPDATE CASCADE） |
| `disc_no_in_set` | INT UNSIGNED NULL | 組中位置（単品は NULL、複数枚組は 1/2/3...） |
| `disc_kind_code` | VARCHAR(32) FK NULL | ディスク種別（→ `disc_kinds`） |
| `media_format` | ENUM DEFAULT 'CD' | `CD` / `CD_ROM` / `DVD` / `BD` / `DL` / `OTHER` |
| `mcn` | VARCHAR(13) NULL | Media Catalog Number（= JAN/EAN バーコード。CDAnalyzer で取得） |
| `total_tracks` | TINYINT UNSIGNED NULL | 総トラック数（CD-DA 専用。BD/DVD では NULL） |
| `total_length_frames` | INT UNSIGNED NULL | 総尺（CD-DA 専用、1 フレーム = 1/75 秒。BD/DVD では NULL） |
| `total_length_ms` | BIGINT UNSIGNED NULL | 総尺（BD/DVD 専用、ミリ秒精度） |
| `num_chapters` | SMALLINT UNSIGNED NULL | チャプター数（BD/DVD 専用。CD-DA には「チャプター」概念がないため NULL） |
| `volume_label` | VARCHAR(64) NULL | ボリュームラベル（BD/DVD のファイルシステム上のラベル） |
| `cd_text_album_title` / `_performer` / `_songwriter` / `_composer` / `_arranger` / `_message` | VARCHAR NULL | CD-Text のアルバム単位情報（CD のみ） |
| `cd_text_disc_id` | VARCHAR(32) NULL | CD-Text Disc ID |
| `cd_text_genre` | VARCHAR(64) NULL | CD-Text Genre |
| `cddb_disc_id` | VARCHAR(16) NULL | CDDB Disc ID |
| `musicbrainz_disc_id` | VARCHAR(32) NULL | MusicBrainz Disc ID |
| `last_read_at` | DATETIME NULL | CDAnalyzer / BDAnalyzer が最後にこのディスクを読み取った日時 |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |
| `created_at` / `updated_at` | TIMESTAMP | 作成・更新日時 |
| `created_by` / `updated_by` | VARCHAR(64) | 作成・更新ユーザー |

#### `tracks` — ディスク内のトラック

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

**CHECK 制約 / トリガー**: INSERT/UPDATE 時に `trg_tracks_bi_fk_consistency` / `trg_tracks_bu_fk_consistency` トリガーが content_kind 一貫性と sub_order ルールを検証する。BEFORE INSERT のチェックには同一 PK の行が既に存在する場合は SIGNAL をスキップするガードがあり、`INSERT ... ON DUPLICATE KEY UPDATE` で BEFORE INSERT が先に発火する際の不当弾きを防ぐ。整合性の最終判定は後続の `trg_tracks_bu_fk_consistency`（BEFORE UPDATE）が保全後の確定値で行う。

#### `songs` — 歌マスタ（メロディ + アレンジ単位）

| 列名 | 型 | 説明 |
|---|---|---|
| `song_id` | INT PK AUTO_INCREMENT | 曲 ID |
| `title` | VARCHAR(255) | 曲タイトル |
| `title_kana` | VARCHAR(255) NULL | タイトル読み |
| `series_id` | INT FK NULL | 所属シリーズ（→ `series`、NULL = シリーズ横断、ON DELETE SET NULL） |
| `lyricist_name` / `_kana` | VARCHAR(255) NULL | 作詞者 |
| `composer_name` / `_kana` | VARCHAR(255) NULL | 作曲者 |
| `arranger_name` / `_kana` | VARCHAR(255) NULL | 編曲者 |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

> 音楽種別 `music_class_code` は `song_recordings` 側で保持する設計。同一曲のカバーやアレンジが「主題歌→キャラソン」のように文脈で種別を変えるケースを表現するため、種別を録音単位で管理する。

#### `song_recordings` — 歌の歌唱者バージョン

| 列名 | 型 | 説明 |
|---|---|---|
| `song_recording_id` | INT PK AUTO_INCREMENT | 録音 ID |
| `song_id` | INT FK | 親曲（→ `songs`、CASCADE） |
| `singer_name` / `singer_name_kana` | VARCHAR(1024) NULL | 歌唱者 |
| `variant_label` | VARCHAR(128) NULL | 自由ラベル（歌唱者バリエーションの補助表記） |
| `music_class_code` | VARCHAR(32) FK NULL | 音楽種別（→ `song_music_classes`）。録音単位で保持する |
| `notes` | TEXT NULL | 備考 |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

#### `bgm_sessions` — 劇伴の録音セッションマスタ

シリーズごとに `session_no` を `1, 2, 3, ...` と採番する。

| 列名 | 型 | 説明 |
|---|---|---|
| `series_id` | INT PK(1) FK | 所属シリーズ（→ `series`、ON DELETE RESTRICT） |
| `session_no` | TINYINT UNSIGNED PK(2) DEFAULT 1 | シリーズ内のセッション番号 |
| `session_name` | VARCHAR(128) NOT NULL | セッション名（劇伴詳細ページの h2 見出しに採用） |
| `caption` | VARCHAR(255) NULL | 劇伴詳細ページのセッション見出し横に小さく添える補足説明（録音日・スタジオ名等の自由テキスト）。NULL なら見出しに span 自体を出さない。閲覧 UI 専用の表示テキストで、検索や絞り込みの対象とはしない |
| `notes` | TEXT NULL | 備考（内部メモ用途。公開 UI には出ない） |

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
| `is_temp_m_no` | TINYINT NOT NULL DEFAULT 0 | 仮 M 番号フラグ。`m_no_detail` が `_temp_034108` のような内部管理用のダミー番号であることを示す |
| `is_deleted` | TINYINT DEFAULT 0 | 論理削除フラグ |

**インデックス**:
- `ix_bgm_cues_class (series_id, m_no_class)`
- `ix_bgm_cues_session (series_id, session_no)`

#### `song_recording_bgm_assignments` — SONG ↔ BGM 両性紐付け中間テーブル

1 つの `song_recordings`（特定のアレンジ・テイク・歌唱者構成での録音）が「歌として収録された」のと同時に「劇伴としても扱う」二重性質を持つ場合に、当該録音と劇伴 cue（`bgm_cues`）の N:M 関係を表現する。`tracks.content_kind_code` は `'SONG'` / `'BGM'` を排他選択する仕様（`trg_tracks_bi/bu_fk_consistency` で強制）のまま据置で、SONG なのに BGM 性も併せ持つ追加の関係をこの中間テーブルで表現する。

| 列名 | 型 | 説明 |
|---|---|---|
| `song_recording_id` | INT PK(1) FK | 参照先録音 ID（→ `song_recordings`、ON DELETE CASCADE / ON UPDATE CASCADE） |
| `song_part_variant_code` | VARCHAR(32) PK(2) FK NOT NULL | 適用パートコード（→ `song_part_variants.variant_code`、ON DELETE RESTRICT / ON UPDATE CASCADE）。実パート（`VOCAL` / `INST` / 等）または sentinel `_ANY`（パート区別なく適用） |
| `bgm_series_id` | INT PK(3) FK(1) | 参照先 cue のシリーズ ID（→ `bgm_cues.series_id`、ON DELETE RESTRICT / ON UPDATE CASCADE） |
| `bgm_m_no_detail` | VARCHAR(255) PK(4) FK(2) | 参照先 cue の M 番号詳細表記（→ `bgm_cues.m_no_detail`） |
| `created_at` / `updated_at` / `created_by` / `updated_by` | 監査列 | レコード作成・更新の日時と識別子 |

**インデックス**:
- `ix_srba_cue (bgm_series_id, bgm_m_no_detail)` — cue 側からの逆引き
- `ix_srba_part (song_part_variant_code)` — パートコード絞り込み用

**運用ルール**:
- 録音単位で「劇伴としても扱う」紐付けを 1 行ずつ追加する。1 録音は複数の M ナンバーに紐付き得る（メドレートラック等）。
- パート違いで紐付く M ナンバーが変わるケース（VOCAL 版と INST 版で別 M ナンバー）に対応するため、`song_part_variant_code` を PK に含めて副キー化する。
- パート区別なく適用したい紐付けは sentinel `_ANY` を入れる。`song_part_variants` マスタにあらかじめ `variant_code='_ANY'` / `name_ja='(指定なし)'` の sentinel 行を投入しておく必要がある（マイグレ SQL が `INSERT ... ON DUPLICATE KEY UPDATE` で冪等に挿入する）。
- `tracks.song_part_variant_code` が NULL のトラック（パート未登録）は中間テーブルとマッチしない（NULL 既定マッチ方式は採らない）。
- 編集 UI は WinTools 側にまだ用意していないため、現状は手動 SQL での運用。

**トリガー** `trg_tracks_bu_block_kind_change_when_srba`:
`tracks.content_kind_code` を `SONG` から別の値（`BGM` / `DRAMA` / `RADIO` / `JINGLE` / `CHAPTER` / `OTHER`）に変えようとする UPDATE で、当該 `song_recording_id` が中間テーブルに紐付いていれば `SIGNAL SQLSTATE '45000'` で拒否する。先に中間テーブルの対応行を手動で削除してから種別変更する運用とし、「中間テーブルに紐付いた録音が SONG ではないトラックを指している孤児状態」を構造的に防ぐ。

**表示側の利用**:
- 劇伴詳細ページ `/bgms/{slug}/` の cue カード収録盤リストに、この中間テーブル経由で紐付くトラック（SONG）も含まれる（`MusicGenerator.LoadBgmCueRecordingsAsync` の SQL が `UNION ALL` で 2 系統に分かれ、第 2 系統が中間テーブル経由で SONG トラックを拾う）。
- 商品詳細ページ `/products/{catalog}/` の SONG トラックカードで、当該録音が中間テーブルに紐付いていれば、歌の役職クレジット行の下に追加メタ行として「シリーズ略記 + Mナンバー [メニュー]（→劇伴詳細リンク）」が出る。
- 同じく商品詳細トラックカードの番号バッジが、SONG（赤）+ BGM（緑）の斜め分割塗りに変わって両性であることを示す。

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

CDAnalyzer / BDAnalyzer の DB 連携では、メディアごとに専用の照合メソッドが用意されている。

**CD-DA（`DiscRegistrationService.FindCandidatesForCdAsync`）**: 以下の優先順で検索し、最上位のキーでヒットした時点で以降の検索は行わない。複数枚組（BOX）は全ディスクが同一 MCN（商品バーコード）を共有し Disc 2 を Disc 1 に誤マッチさせる危険があるため、CD-DA 照合では MCN を照合キーに用いない（引数は互換のため受け取るが不使用）:

1. **CDDB Disc ID 完全一致**（`discs.cddb_disc_id`）
2. **TOC 曖昧一致**: `total_tracks` 完全一致 AND `total_length_frames` ±75 フレーム（≒ ±1 秒）

**BD/DVD（`DiscRegistrationService.FindCandidatesForVideoAsync`）**: MCN / CDDB は取れないため、TOC 曖昧のみ:

- `num_chapters` 完全一致 AND `total_length_ms` ±1000 ms（≒ ±1 秒）

CD と BD/DVD で照合メソッドを分離しているのは、単一メソッドで兼用すると BD/DVD の尺を CD-DA の 1/75 秒フレームに換算する必要があり、意味論の混乱と尺精度の劣化（約 13ms 単位への丸め）を招くため。

### シリーズ紐付けの運用

ディスクのシリーズ所属は以下の経路で設定:

1. **新規登録時**: CDAnalyzer / BDAnalyzer → `NewProductDialog` でシリーズを選択。ダイアログの `SelectedSeriesId` が新規作成される `disc.SeriesId` に適用される
2. **後から編集**: Catalog GUI の「商品・ディスク管理」画面 → ディスク詳細の「シリーズ」コンボで変更・保存

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

各バージョンの変更履歴は [`CHANGELOG.md`](CHANGELOG.md) を参照。

## ライセンス

[MIT License](LICENSE) © 2025 Shota (SHOWTIME)
