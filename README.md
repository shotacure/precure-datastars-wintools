# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）**、および **クレジット情報（OP / ED の階層構造、人物・企業・キャラクター・プリキュアの各マスタ）** を MySQL データベースで管理するためのアプリケーション集です。Web 公開用の静的サイトジェネレータ `PrecureDataStars.SiteBuilder` により、ローカル MySQL の内容をそのまま静的 HTML として書き出せます。

> **現行バージョン: v1.4.0**。各機能の現状仕様は本文を、全バージョンの変更履歴は [`CHANGELOG.md`](CHANGELOG.md) を参照してください。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common … カタログ GUI 共通（Dialog/Service/CSV Import）
├── PrecureDataStars.TemplateRendering … 役職テンプレ DSL 展開エンジン（共通ライブラリ）
│
├── PrecureDataStars.Episodes … エピソード管理 GUI（WinForms）
├── PrecureDataStars.Catalog … カタログ管理 GUI（WinForms）
├── PrecureDataStars.TitleCharStatsJson … 文字統計一括再計算（コンソール）
├── PrecureDataStars.YouTubeCrawler … YouTube URL 自動抽出（コンソール）
│
├── PrecureDataStars.BDAnalyzer … Blu-ray/DVD チャプター解析（WinForms）＋DB 連携
├── PrecureDataStars.CDAnalyzer … CD-DA トラック解析（WinForms）＋DB 連携
│
├── PrecureDataStars.SiteBuilder … Web 公開用静的サイト生成（コンソール）
│
├── Directory.Build.props … 全プロジェクト共通の Version・LangVersion
└── db/
 ├── schema.sql … MySQL スキーマ定義（DDL、新規構築用）
 ├── migrations/ … バージョン別差分 SQL。ファイル名は `v<VERSION>_migration_<topic>.sql`（例 `v1.3.5_migration_precure_key_color.sql`）。バージョン昇順に適用（適用済み。詳細は CHANGELOG.md / Git）
 └── utilities/
 └── backfill_products_price_inc_tax.sql … 税込価格の発売日ベース自動算出
```

### プロジェクト詳細

| プロジェクト | 種別 | 概要 |
|---|---|---|
| **PrecureDataStars.Data** | クラスライブラリ | Model（Episode, Series, Product, Disc, Track, Song, SongRecording, BgmCue, BgmSession, VideoChapter 等）・Dapper ベースの Repository・DB 接続ファクトリを提供。全アプリケーションから参照される共通データ層。 |
| **PrecureDataStars.Data.TitleCharStatsJson** | クラスライブラリ | サブタイトル文字列を NFKC 正規化し、書記素単位でカテゴリ分類した統計 JSON を生成する `TitleCharStatsBuilder`。 |
| **PrecureDataStars.Catalog.Common** | クラスライブラリ | CDAnalyzer / BDAnalyzer / Catalog GUI の 3 つで共有するダイアログ（`DiscMatchDialog`・`NewProductDialog`）と `DiscRegistrationService`（ディスク照合 → 登録ビジネスロジック）に加え、歌・劇伴の CSV 取り込みサービス（`SongCsvImportService` / `BgmCueCsvImportService`）と最小 CSV リーダー（`SimpleCsvReader`、UTF-8/カンマ区切り、外部依存なし）を提供する。 |
| **PrecureDataStars.TemplateRendering** | クラスライブラリ | 役職テンプレ DSLの展開エンジン共通プロジェクト。Catalog 側プレビュー（`CreditPreviewRenderer`）と SiteBuilder 側 HTML 生成（`CreditTreeRenderer`）の双方から参照される。`TemplateContext` / `TemplateNode` / `TemplateParser` / `RoleTemplateRenderer` / `Handlers/ThemeSongsHandler` と、`LookupCache` 抽象化のための `ILookupCache` インターフェースを保持。`net9.0`（Forms 非依存）で構成し、コンソール側の SiteBuilder からも安全に参照可能。各プロジェクトの `LookupCache` 実装を `ILookupCache` インターフェース注入で切り替える設計。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインのエピソード管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理 GUI。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、ディスク総尺・トラック尺ともに M:SS.fff で表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）と、6 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類・**クレジット系マスタ**）をメニューから切り替えて使う。クレジット系マスタは **15 タブ構成**の `CreditMastersEditorForm`：プリキュア／人物／人物名義／企業／企業屋号／ロゴ／キャラクター／キャラクター名義／キャラクター続柄／家族関係／役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別。声優キャスティングは業務ルール「ノンクレ除いてクレジットされている＝キャスティング」に基づき `credit_block_entries` の `CHARACTER_VOICE` エントリに一元化されている。**音楽クレジット名寄せ移行フォーム `MusicCreditsMigrationForm`** は、未マッチング名義一覧 → `UnmatchedAliasRegisterDialog` での人物・名義登録 → 全シリーズ全列での構造化テーブル INSERT までをワンストップで実行する（`SongCreditsRepository` / `SongRecordingSingersRepository` / `BgmCueCreditsRepository` を経由した完全一致 + SP 正規化マッチング、既移行レコード自動除外）。人物・キャラクターの編集タブには誕生日入力欄（生年 NumericUpDown ＋「不明」チェック／公開可否コンボ〔公開＝`PUBLIC` 生成に出す・非公開＝`PRIVATE` 生成に出さない〕／月・日コンボ）があり、`persons` / `characters` の `birth_year` / `birth_year_visibility` / `birth_month` / `birth_day` を編集できる（生年「不明」時は NULL、月・日は先頭「(未)」が NULL。プリキュアの誕生日は本キャラクタータブで管理し、プリキュアタブには誕生日欄を置かない）。**かな・英語表記の自動補完**：共有ロジック `PrecureDataStars.Data.Text.KanaRomanizer` はかな（ひらがな・カタカナ＋長音符・中黒・半角スペース）をパスポート式ローマ字へ変換する純粋クラスで、長音は表記せず（「さとう」→ Sato、長音符「ー」は無音、母音長音は直前ローマ字末尾が o/u のときの後続「う」を抑止）、撥音「ん」は常に n、促音「っ」は次音の子音重ね、語（半角スペース・中黒区切り）ごとに先頭 1 文字のみ大文字化、姓名は反転しない。漢字・英数字等が 1 文字でも混入する入力は変換不能として扱う。`CreditMastersEditorForm` の人物・企業・キャラクター保存時はかな有り・英語空なら `KanaRomanizer` で英語を補完、人物名義・企業屋号・キャラクター名義の保存時は紐づく補完元（人物 `person_alias_persons` 一意紐付け／企業 `company_id`／親キャラ）の name_kana・name_en をコピーし、英語が補完元も空なら名義名のローマ字へフォールバックする（かなは読み推定不能のため補完元が空ならスキップ）。いずれも補完候補を `MessageBox` で提示し、ユーザーが承認した場合のみ入力欄へ反映してから通常保存する。コードベースには登録・変更時の自動補完フックと共有ロジック `KanaRomanizer` が常駐する。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。DVD は VIDEO_TS.IFO を指定するとフォルダ全走査で多話収録 DVD にも対応する。Blu-ray も `BDMV/PLAYLIST` 配下指定時はフォルダ全走査モードに切り替わり、ディスク内の有意なプレイリストを並列抽出する（既定 60 秒未満の短尺ダミーと重複プレイリストは自動除外）。DB 連携パネルで既存ディスクとの照合・新規商品登録が可能。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・ISRC・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行できる。メディア挿入時に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系プロファイル以外（DVD / BD / HD DVD）であれば後続の SCSI コマンドを発行せず即座にデバイスハンドルをクローズする（BDAnalyzer との同時起動時にドライブ占有競合を起こさないため）。 |
| **PrecureDataStars.SiteBuilder** | コンソール | Web 公開用の静的サイト生成ツール。ローカル MySQL の内容を読み出し、シリーズ・エピソードを中心とした静的 HTML 一式を `out/site/` 以下に書き出す。テンプレートエンジンは Scriban、共通レイアウト＋コンテンツの 2 段レンダリング。エピソード詳細ページにはフォーマット表（OA / 配信 / 円盤の累積タイムコード）、サブタイトル文字情報（初出・唯一・「N年Mか月ぶり」）、文字統計、パート尺偏差値、主題歌、クレジット階層（Card → Tier → Group → Role → Block → Entry）までを 1 ページに集約する。さらに `/persons/{personId}/` `/companies/{companyId}/` `/precures/{precureId}/` `/characters/{characterId}/` の人物・企業・プリキュア・キャラクター軸ページも提供し、起動時に 1 回だけ構築する `CreditInvolvementIndex` 経由で「人物・企業・キャラごとにどのシリーズのどのエピソードに、どの役職で関与したか／いつ誰の声で演じられたか」を逆引き表示する。人物・企業・団体・声優・役職は「クリエーター」セクション（`/creators/` 配下）に集約して串刺し検索・横断一覧でき、個別の `/persons/{personId}/` `/companies/{companyId}/` 詳細ページは直リンク用に引き続き生成する。AWS 連携は本ツール範囲外（手動 `aws s3 sync` を別途想定）。 |
| **PrecureDataStars.AmazonPaApi** | クラスライブラリ | Amazon Product Advertising API 5.0 (PA-API) のクライアントライブラリ（v1.4.0 追加）。AWS Signature V4 署名・GetItems・SearchItems・App.config からの認証情報読み出しヘルパを提供する。Catalog（商品検索ダイアログと一括画像取得）と AmazonSync コンソールの両方から ProjectReference 経由で参照され、それぞれの publish 出力に同梱される。本ライブラリ単体ではコマンドラインに出ず、配布 ZIP も独立に作らない。 |
| **PrecureDataStars.AmazonSync** | コンソール | products テーブルから ASIN を持つ商品を抽出し、PA-API GetItems で `cover_image_url` を一括更新するバッチ（v1.4.0 追加）。鮮度切れ判定（90 日経過 or 未取得）で対象を絞り込み、PA-API レート制限（1 TPS）を順守するため各リクエストの間に 1.1 秒スリープを挟む。`--all`（全件強制再取得）／`--asin B0XXXXXXXX`（単一テスト）／`--dry-run`（DB 更新せず表示のみ）のオプションを持つ。優先順位は CD ASIN → デジタル ASIN で、最初に画像 URL が取れた方を採用して `cover_image_source = amazon_cd` または `amazon_digital` で記録する。 |

---

## 動作要件

- **OS**: Windows 10 以降（CDAnalyzer / BDAnalyzer はドライブ P/Invoke のため Windows 専用）
- **ランタイム**: .NET 9 SDK
- **データベース**: MySQL 8.0+
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

`db/schema.sql` によりデータベース `precure_datastars` と全テーブルが作成されます。内訳はエピソード系 6 本、音楽・映像カタログ系（`movie_bgm_cues` を含む）、**クレジット管理系**（人物・企業・キャラクター〔`persons` / `characters` は誕生日列 `birth_year` / `birth_year_visibility` / `birth_month` / `birth_day` を保持〕・役職・クレジット本体／カード／ティア／グループ／役職／ブロック／エントリ・エピソード主題歌・credit_kinds・role_templates・person_alias_members・song_credits・song_recording_singers・bgm_cue_credits）、プリキュア本体マスタ `precures`（変身前後 4 名義 + 声優 + 肌色 HSL/RGB + バッジ地色 `key_color` + 学校情報。誕生日は `persons` / `characters` 側が保持し `precures` は誕生日カラムを持たない）、続柄マスタ `character_relation_kinds`、家族関係 `character_family_relations`（汎用）です。`discs.series_id` を持ち `products.series_id` は無く、`songs` の作詞／作曲列は `lyricist_name` / `composer_name` 等の素の命名、`bgm_cues` には仮 M 番号フラグ `is_temp_m_no`、`series_kinds` には `credit_attach_to`、`part_types` には `default_credit_kind` を持ちます。

映画作品の BGM リスト用テーブル `movie_bgm_cues` は、代理 PK `movie_bgm_cue_id`、`series_id` で映画系シリーズへ直結、`seq`/`sub_seq` の順序、映画固有 `m_no` 文字列、区分は `track_content_kinds` を共用、`is_unused`／`is_missing` の排他 2 フラグを持ちます。`bgm_cues`（TV シリーズのセッション制・劇伴専用）とは別概念で、映画にはセッション・パートの概念がありません。`series_id` は映画系 kind（`MOVIE` / `MOVIE_SHORT` / `SPRING` / `EVENT`）のみ許容し、MySQL の CHECK は他テーブルを参照できないため BEFORE INSERT / UPDATE トリガー `trg_movie_bgm_cues_bi_series_kind` / `_bu_series_kind` で担保します。未使用と欠番の排他は CHECK `ck_movie_bgm_cues_unused_missing_exclusive` で担保します。

### 1'. 既存環境からのアップグレード

既存データベースを最新スキーマへ更新する場合は、`db/migrations/` 配下のバージョン別差分 SQL を
順番に適用します。各スクリプトは `INFORMATION_SCHEMA` で対象オブジェクトの存在を確認してから
DDL を実行する冪等設計のため、適用済みのステップを再実行しても安全です。どのバージョンで何が
変わったかは [`CHANGELOG.md`](CHANGELOG.md)、適用単位の詳細は Git のコミット履歴および GitHub
のリリースノートを参照してください。新規構築の場合はマイグレーション不要で、`db/schema.sql` が
常に最新スキーマを表します。

差分 SQL のファイル名は **`v<VERSION>_migration_<topic>.sql`** 形式で固定する（`VERSION` は `Directory.Build.props` のリリースバージョン、`topic` は変更内容を表す英小文字スネークケース。例: `v1.3.5_migration_precure_key_color.sql`）。適用はファイル名のバージョン昇順で行い、各スクリプトは `INFORMATION_SCHEMA` で対象オブジェクトの存在を確認してから DDL を流す冪等設計とするため、適用済みのバージョンを再実行しても安全に素通りする。データ補正を伴う UPDATE も、未設定行のみを対象にするなど再実行・後続編集に対して非破壊とする。

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
- `PrecureDataStars.SiteBuilder-v<VERSION>-win-x64.zip`
- `PrecureDataStars.AmazonSync-v<VERSION>-win-x64.zip`（v1.4.0 で追加。PA-API でジャケット画像 URL を一括取得するコンソールバッチ）
- `precure-datastars-db-v<VERSION>.zip`（`schema.sql` + `migrations/*`）

`PrecureDataStars.AmazonPaApi` はクラスライブラリのため独立 ZIP は出力されず、`Catalog` と `AmazonSync` の publish 出力にそれぞれ DLL として同梱されます。
`PrecureDataStars.LegacyImport`（初期データ移行専用）と `PrecureDataStars.YouTubeCrawler`（エピソード予告 URL 自動抽出）はリリース ZIP の配布対象外です。コードはリポジトリ内に残しているため、必要になったら `scripts/build-release.ps1` の `$targets` 配列にコメントアウトで残してある行を復活させれば再度配布できます。

スクリプト完走後に画面に表示される「Next steps」に従って、`git tag` → `git push --tags` → GitHub Releases へ `release/*.zip` をアップロード、の流れでリリースしてください。

---

## 主要ワークフロー

### エピソード管理

`PrecureDataStars.Episodes` で、シリーズとエピソードの CRUD、サブタイトルのかな・ルビ編集、パート構成（アバン・OP・A/B パート・ED・予告）の編集を行います。新規エピソード追加後は `PrecureDataStars.TitleCharStatsJson` で文字統計を再計算、`PrecureDataStars.YouTubeCrawler` で YouTube 予告動画 URL を自動補完するのが定型運用です。

### 音楽カタログ登録

#### A. CD の登録

1. `PrecureDataStars.CDAnalyzer` を起動し、CD をドライブに挿入。
2. 「読み取り」で TOC・MCN・ISRC・CD-Text を取得。
3. 「既存ディスクと照合 / 新規登録...」ボタンで `DiscRegistrationService` を通じた優先順（MCN → CDDB-ID → TOC 曖昧）の照合が走り、`DiscMatchDialog` が候補を表示。`DiscMatchDialog` のアクションは 3 通り:
 - **「選択したディスクに反映」**: TOC 一致した既存ディスクの物理情報のみ更新（タイトル等の Catalog 情報は保全）
 - **「選択したディスクの商品に追加」**: 既存の複数枚組商品に新しいディスクを追加するケース。商品本体は新規作成せず、`DiscMatchDialog` のグリッドで対象 BOX のいずれかのディスク（例: Disc 1）を選択した状態で押下する。所属商品が一意に決まるため `ConfirmAttachDialog` で確認・シリーズ継承選択 → 品番候補入りの入力プロンプトで品番確定 → 新ディスクを INSERT。組内番号 (`disc_no_in_set`) は商品配下の全ディスクを品番順に自動再採番、`disc_count` も所属ディスク数 + 1 に自動更新される
 - **「新規商品＋ディスクとして登録」**: 商品もディスクも新規作成。品番入力 → `NewProductDialog` で商品種別・タイトル・シリーズ・発売日等を設定 → ディスク＋トラックを一括登録。`NewProductDialog` で選択したシリーズは、作成される Product ではなく Disc 側の `series_id` に適用される。

> **非 CD メディア投入時の挙動**: DVD / Blu-ray / HD DVD 投入時は MMC `GET CONFIGURATION` の Current Profile 判定で読み取りをスキップしハンドルを即クローズする（BDAnalyzer との同時起動時のドライブ占有競合回避）。自動検知経由はステータスラベル通知のみ、手動「読み取り」時はメディア種別を案内するダイアログを表示。GET CONFIGURATION 非対応の旧ドライブは安全側で従来の TOC 読み取りにフォールバック。
>
> **MCN / ISRC の取得仕様**: MCN（JAN/EAN バーコード）と ISRC は SCSI MMC の READ SUB-CHANNEL (0x42) で取得する（CDB バイト構成・有効ビット検証・固定オフセット解析の詳細はコード `ScsiMmci` および CHANGELOG 参照）。ISRC は対象トラック先頭への SEEK(10) を挟む 2 パス取得で、ディスク内に 1 件でも取得できれば「収録盤」と判定し未取得トラックのみ最大 5 回再試行、1 件も無ければ未収録盤として再試行しない（ディスク単位ゲート）。取得値は `discs.mcn`（照合最優先キー）・`tracks.isrc` に格納。
>
> **公開サイトでの掲示（商品詳細ページ）**: MCN は商品基本情報テーブルに「JAN」行として 1 回表示（CD 含む商品のみ、先頭 CD ディスクの MCN を採用）。各トラックの ISRC はトラック表「No.」セルの `title` ツールチップ（`.track-list td.col-no.has-isrc`）。トラック尺は整数部「m:ss」＋小数 2 桁を `.micro-fraction` 縮小表示（端数繰り上げの誤表記防止つき）。商品 JAN が 13 桁数字のとき商品 JSON-LD に schema.org `gtin13` を出力。
>
> **ジャケット画像と購入・試聴リンク（v1.4.0 PA-API 対応版）**: 商品見出し直下にジャケット画像＋外部リンクを並べる。Amazon リンクは物理パッケージ向けの `amazon_asin_cd`（「Amazon で買う (CD)」）とデジタル音源向けの `amazon_asin_digital`（「Amazon で聴く (デジタル)」）の 2 系統を並列表示し、片方しか登録されていない商品は片方だけ出る。Apple Music / Spotify は従来通り。画像は `products.cover_image_url` にキャッシュした URL を提供元 CDN へホットリンク（`loading=lazy decoding=async`）し、実体は当サイトに保存しない（PA-API 規約遵守）。取得元コード `cover_image_source` は `amazon_cd` / `amazon_digital` / `apple` の 3 値で、採用優先順位は CD ASIN → デジタル ASIN → Apple Music ID（iTunes Lookup フォールバック）。取得は SiteBuilder ビルドから分離し、Catalog の「画像取得」ボタン（手動・差分）または `PrecureDataStars.AmazonSync` コンソール（バッチ・鮮度切れ自動判定）で行う。外部リンクはすべて `rel="nofollow sponsored noopener"` ＋ `target=_blank`、`AmazonAssociateTag` が設定されていれば `?tag=` 付与でアフィリエイト計測対象。JSON-LD では `offers` 配列に物理／デジタルそれぞれの Offer を出力し、リッチリザルトの購入リンク候補に乗せる。

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で `.mpls` / `.IFO` をロード。
 - **Blu-ray**: `BDMV/PLAYLIST/*.mpls` の任意 1 個を指定する。親フォルダが `PLAYLIST` であることが検出されると、フォルダ内の全 `*.mpls` を走査して有意なタイトル（プレイリスト）を並列抽出するフォルダ全走査モードに切り替わる。ロゴ・著作権警告等の短尺ダミーと、anti-rip 系の重複プレイリストは自動除外される（フィルタ仕様は後述）。ドライブ自動検知も `BDMV/PLAYLIST` 配下の `.mpls` が 1 個でもあればフォルダごと採用するため、`00000.mpls` / `00001.mpls` がない構成にも対応する。
 - **Blu-ray（単一プレイリストモード）**: `BDMV/PLAYLIST` 配下にない `.mpls` ファイル（コピーして別フォルダに置いたものや、個別プレイリストを明示確認したいケース）を直接指定すると、そのプレイリスト 1 個だけを従来通り解析する。
 - **DVD**: **`VIDEO_TS/VIDEO_TS.IFO` を指定**。下記の二段階ルーティングでチャプター一覧を抽出する。ドライブ自動検知も `VIDEO_TS.IFO` を優先する。
 - **DVD（単一 VTS モード）**: `VTS_xx_0.IFO` を直接指定すると、その VTS の先頭 PGC のみを解析する（個別 VTS 確認用）。
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺 ms ±1 秒による TOC 曖昧のみ）。CD と同様に `DiscMatchDialog` のアクションは 3 通りあり、**「既存商品に追加ディスクとして登録」** で BOX 商品の Disc 2 / Disc 3 を後から足す運用が可能（後述の「既存商品への追加ディスク登録フロー」を参照）。
3. 反映時は discs テーブルの物理情報が同期され、加えて `video_chapters` テーブルへチャプター情報が一括登録される（再読み取り時は「全削除 → 置換」で上書き）。
 - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ。
 - `title` / `part_type` / `notes` は NULL のまま登録されるため、Catalog GUI 側で手動で補完する運用。
 - DVD フォルダ全走査モードでは、チャプター番号 (`chapter_no`) はディスク全体で通し番号（1, 2, 3, …）となり、`playlist_file` にはタイトル識別子が入る（VMGI モードでは `Title_01` 等、Per-VTS モードでは `VTS_02` 等）。Blu-ray のフォルダ全走査モードでは `playlist_file` に MPLS ファイル名（例 `00000.mpls`）が入る。これにより同一ディスク内でどのチャプターがどのタイトル由来かを区別できる。
 - チャプター開始時刻 (`start_time_ms`) は**タイトル単位の相対時刻**（各タイトルの先頭 = 0ms）として記録される（DVD・Blu-ray 共通）。

##### Blu-ray PLAYLIST フォルダ全走査の仕様

`BDMV/PLAYLIST` 配下の任意 `.mpls` を指定すると、`MplsParser.ExtractTitlesFromBdmv` が以下の処理を行う:

1. フォルダ内の `*.mpls` をファイル名昇順に列挙する。
2. 各 MPLS を `MplsParser.Parse(path, allowFallback: false)` でパースし、隣接 MPLS への自動フォールバック（章 1 個以下のとき次番号 MPLS を試す既存ロジック）を抑止して、各プレイリストの個別解析結果を取得する。
3. 4 段のフィルタ（A〜D）を順に適用する:
 - **フィルタ A — 短尺ダミー除外**: プレイリスト総尺がしきい値（既定 60 秒）未満のものを除外する。FBI / Interpol 警告画面、配給会社ロゴアニメーション（5〜15 秒）、レーベルロゴ（数十秒）、メニューBGMの短尺バージョン等を弾く。
 - **フィルタ B — ゼロ尺チャプター除外**: 章尺 < 1ms のチャプターを除去。
 - **フィルタ C — 境界極短チャプター除外**: プレイリスト先頭・末尾の 500ms 未満チャプターを剥がす（黒みフレーム等の自動補正）。
 - **フィルタ D — 重複プレイリスト畳み込み**: `(総尺 ticks, マーク数)` を重複キーとし、同一キーの 2 個目以降のプレイリストを除外する。anti-rip スキーム（同一内容のプレイリストを 99 個用意して本編特定を妨害するパターン）や、視聴順違いの繰り返しに対処する。
4. 残ったプレイリストを `MplsTitleInfo` として返却する。チャプターの `Start` はタイトル先頭からの相対時刻に再計算される（DVD 側 `LoadIfoFolderScan` と同じ運用）。

ListView 表示は DVD のフォルダ全走査と同一の階層形式:
- タイトルヘッダ行: `[00000.mpls]`（薄いグレー背景）
- チャプター行: 2 段インデント ` 1`、` 2`、…
- 末尾の除外サマリ行（除外があった場合のみ、グレー文字）: `短尺 X / 0ms Y / 境界極短 Z / 重複 W`
- 既定チェックは「総尺最大タイトル + そのチャプター」のみオン、他は未チェック（DVD 側と同じ流儀）

`lblInfo` には `00000.mpls - (Blu-ray PLAYLIST scan) Titles: N Chapters: M Aggregated: hh:mm:ss.ff` の形式で集約サマリが表示される。集約総尺はフィルタ D で重複が畳まれた後の単純合計（Blu-ray ではハードリンク等の概念がないため、DVD 側のような max/sum 切替ロジックは持たない）。

しきい値は `MplsParser.ExtractTitlesFromBdmv` の引数で変更可能だが、現状の MainForm からは既定値（60 / 1 / 500）固定で呼んでいる。30 秒スポット等を取り込みたいケースでパラメータ調整が必要になったら、別途オーバーロードを公開する。

##### DVD 解析の二段階ルーティング

`VIDEO_TS.IFO` を指定すると以下の優先順で処理される:

1. **VMGI 経路（正攻法、優先）**: `VIDEO_TS.IFO` 先頭の `DVDVIDEO-VMG` シグネチャを確認後、**TT_SRPT** (Title Search Pointer Table、offset `0xC4`) を読んで論理タイトル一覧を取得する。各タイトルについて対応 `VTS_NN_0.IFO` の **VTS_PTT_SRPT** (Part-of-Title Search Pointer Table、offset `0xC8`) から `(PgcNo, PgmNo)` ペアをチャプターごとに解決し、該当 PGC の Program 尺リストから各チャプターの再生時間を組み立てる。DVD プレイヤーがユーザーに見せる「タイトル/チャプター」構造と完全一致する。
2. **Per-VTS 経路（フォールバック）**: VMGI が読めない／TT_SRPT が壊れているディスク向け。物理 `VTS_NN_0.IFO` を全走査し、各 VTS の最長 PGC を「その VTS のタイトル本編」とみなして拾う。通常は VMGI 経路が成功するため発火しないが、オーサリング破損ディスクのサルベージ用として維持。

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

##### タイトル/チャプターのチェック選択

フィルタ 1〜4 で除去しきれない「ユーザー視点で明らかに不要なタイトル」（オーディオコメンタリ、未使用ダミー、先頭のアバン部分だけ削りたい等）を手動で除外できるよう、ListView の各行にチェックボックスがついている。

- **デフォルトで全行チェック済み**（何もしなければ従来通りの全件登録）
- **タイトル行のチェックを外す** → 配下チャプター全てが連動して外れる
- **チャプター行を個別に外す** → 親タイトル行は配下のチェック状態の OR（1 つでも残っていればチェック維持）
- **除外行（集計表示）** のチェックは機能しない（触っても自動で false に戻る）
- 「既存ディスクと照合 / 新規登録...」押下時に、チェックが残っているチャプターだけが `video_chapters` に投入される。`chapter_no` は投入対象のみで 1 から再採番される
- `discs.num_chapters` と `discs.total_length_ms` も絞り込み後の値で計算し直して登録する。`total_length_ms` の集計ルール（合計 vs 最大）はロード直後と同じ判定を使う

この機能は DVD の VMGI / Per-VTS フォルダ走査に加え、単一 VTS モード・BD の MPLS モードでも統一的に動作する（BD/単一 VTS の場合はチェックを触らずにそのまま登録すれば従来挙動）。

##### ディスク総尺の集約ロジック

`discs.total_length_ms` に格納する「ディスク全体の尺」は、タイトル数と UDF ハードリンクの検出結果で切り替える:

| 条件 | 集約方法 | 根拠 |
|---|---|---|
| タイトル 1 個 | 単純にそのタイトルの尺 | 場合分け不要 |
| タイトル複数 + VOB ハードリンク検出 | **最長タイトルの尺** | 同じ実データを別角度で複数ナビゲーションから見せている構造。合計すると水増しされる |
| タイトル複数 + VOB 独立 | **全タイトル尺の合計** | 真に独立した多話収録。合計が本当のディスク全体尺 |

UDF ハードリンクの検出は、`VTS_*_1.VOB` のバイト数が全て同一かどうかで判定する（同一なら実体 1 本を複数 VTS で共有している）。

#### C. トラックの内容編集（歌・劇伴への紐付け）

1. `PrecureDataStars.Catalog` を起動し、メニューから「トラック管理...」を選択。
2. ディスクを選んでトラック一覧を開き、各トラックの **内容種別** を選択する:
 - `SONG`: 「曲名・作詞作曲で検索」テキストボックスに 2 文字以上を入力すると、曲名／かな／作詞者／作曲者／編曲者を横断した部分一致で候補リストが更新される（250 ms デバウンス）。候補から親曲を選ぶと、その曲に紐づく `song_recordings` が「歌唱者バージョン」コンボに自動ロードされる。サイズ種別・パート種別は別コンボで指定。
 - `BGM`: シリーズコンボで絞り込み（未指定なら全シリーズ横断）、「M番号・メニュー名で検索」テキストボックスで `m_no_detail` / `m_no_class` / `menu_title` / 作曲者 / 編曲者 を横断検索。候補は既定で実番号のみ。「仮番号を候補に含める」チェックで `_temp_...` の仮 M 番号行も候補入りする。
 - `DRAMA` / `RADIO` / `LIVE` / `TIE_UP` / `OTHER`: タイトル文字列の上書きだけ行う（録音参照なし）。
3. **ディスクのシリーズ所属** は、メニュー「商品・ディスク管理...」のディスク詳細エリアにある「シリーズ」コンボから変更できる。先頭の「(オールスターズ)」を選ぶと `series_id = NULL` として保存される。
4. 歌・劇伴マスタ側の新規作成は「歌マスタ管理...」「劇伴マスタ管理...」メニューから。両画面とも CSV 一括取り込み機能を搭載（後述）。

#### C''. 商品・ディスク管理画面

`PrecureDataStars.Catalog` のメニュー「商品・ディスク管理...」は、商品 1 件と所属ディスク群を 1 画面で編集する統合エディタです。トラック編集は「トラック管理...」（C 節参照）が担い、本画面はトラックを扱わない。

**画面構成**

上下 2 段構成です。上段（商品エリア）と下段（ディスクエリア）を上下に並べ、それぞれ左 60% に一覧、右 40% に詳細エディタを配置します。下段の高さは 400 px に固定され、残りの縦領域はすべて上段に割り当てられるため、商品エディタの全フィールドが余裕で表示できます。

- **検索バー**（最上部）: 検索キーワード（品番／タイトル／略称／英語タイトルに部分一致）、検索・再読込ボタン
- **上段左ペイン（60%）**: 商品一覧。**発売日昇順、同一日内は代表品番昇順**で並ぶ（過去から時系列に入力していく運用に合わせた並び）。表示カラムは「発売日 / 品番 / タイトル / 種別 / 税込 / 枚数」と翻訳値のみで、内部コードは出さない。
- **上段右ペイン（40%）**: 商品詳細エディタ。代表品番・タイトル・略称・英語タイトル・商品種別・発売日・税抜価格・**税込価格＋自動計算ボタン**・ディスク枚数・発売元・販売元・レーベル・Amazon ASIN・Apple Album ID・Spotify Album ID・備考。新規／保存／削除ボタンは右端に固定。
- **下段左ペイン（60%）**: 所属ディスク一覧（組内番号・品番・ディスクタイトル・メディア）。下段の高さ 400 px は所属ディスク 10 行表示と、ディスク詳細エディタ全フィールド表示のうち大きい方を満たす値（プリキュアの BOX 商品は MAX 10 枚程度を想定）。
- **下段右ペイン（40%）**: ディスク詳細エディタ（品番・組内番号・ディスクタイトル・略称・英語タイトル・シリーズ・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考）。新規／保存／削除ボタンは右端に固定。

ウインドウをリサイズすると、上段と下段の左右 60:40 比率は `splitProduct.SizeChanged` / `splitDisc.SizeChanged` イベントで都度自動的に再計算されます。下段の高さ 400 px は `splitMain.FixedPanel = FixedPanel.Panel2` で固定され、縦方向の拡縮はすべて上段（商品エリア）に追加されます。詳細エディタの入力欄は `Anchor = Top|Left|Right` で右端追従、ボタン群は `Anchor = Top|Right` で右端固定です。

**ディスク詳細編集と物理情報の保全**

本画面で編集できるのはタイトル系・組内番号・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考といったメタ情報のみです。`total_length_frames` / `total_length_ms` / `num_chapters` などの物理情報、CD-Text 系 8 列、`cddb_disc_id` / `musicbrainz_disc_id` / `last_read_at` といった「CDAnalyzer / BDAnalyzer が読み取って記録するもの」は本フォームから編集できません。

これらの非編集列は、ディスク保存時に DB から既存値を引き直して自動的に引き継ぎます（タイトルだけ変更しても物理情報は保持される）。

**税込価格の自動計算**

商品詳細の税込価格欄の隣にある「自動計算」ボタンを押すと、**税抜価格と発売日から日本の標準消費税率を切り捨てで適用** して税込価格を埋めます。書籍・音楽・映像ソフト業界における実務慣例（端数切り捨て）に合わせています。

| 発売日 | 適用税率 |
|---|---|
| 〜 1989-03-31 | 0%（消費税制度導入前。税抜＝税込） |
| 1989-04-01 〜 1997-03-31 | 3% |
| 1997-04-01 〜 2014-03-31 | 5% |
| 2014-04-01 〜 2019-09-30 | 8% |
| 2019-10-01 〜 | 10% |

商品保存時にも、税込価格が空で税抜価格が入っている場合は同じロジックで自動補完します（明示的に 0 を保存したい場合はそのまま 0 で `price_inc_tax = NULL` として登録される挙動）。既存レコードの一括補完は `db/utilities/backfill_products_price_inc_tax.sql`（前述）で行えます。

#### B'. 既存商品への追加ディスク登録フロー

CDAnalyzer / BDAnalyzer から、既に登録済みの商品に対して **新しいディスクだけを追加登録** するフロー。BOX 商品で先に Disc 1 だけ登録しておき、後から Disc 2 / Disc 3 を流し込んでいく運用や、特典 CD・特典 DVD を本編商品にぶら下げて登録するケースを想定しています。

**起動経路**

1. CDAnalyzer / BDAnalyzer で対象ディスクを読み取り、「既存ディスクと照合 / 新規登録...」を押す
2. `DiscMatchDialog` のグリッド（自動照合候補 or 手動検索結果）から、**追加先 BOX に既に登録されているディスクを 1 つ選択**（例: BOX の Disc 1）。自動照合候補が 1 件のみ・手動検索結果が 1 件のみの場合は先頭行が自動選択された状態でグリッドが表示されるため、ユーザーが行をクリックする手間なくそのままボタンを押下できる
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

商品検索を使いたい場合は、Catalog GUI の「商品・ディスク管理」画面でディスクを直接編集する経路を用いる。

**確定後の登録処理**

「追加して登録」ボタン押下で `DiscRegistrationService.AttachDiscToExistingProductAsync` が呼ばれ、次の順序で DB 更新:

1. 指定の `productCatalogNo` で `Product` を取得（無ければ例外）
2. **新ディスクの品番が DB 上に既存していないかを事前検証**。`DiscsRepository.GetByCatalogNoAsync` で既存レコードがヒットしたら `InvalidOperationException("品番 [XXX] は既に登録されています。別の品番を指定してください。")` を送出して以降の処理を行わない。論理削除済み (`is_deleted = 1`) のレコードもヒット扱いとする（誤って論理削除済みディスクを `INSERT ... ON DUPLICATE KEY UPDATE` 経由で復活させてしまう事故を防ぐ）。CDAnalyzer / BDAnalyzer 側はこの例外を `ShowError` で MessageBox に出すため、ユーザーには「重複していたので登録されなかった」ことが伝わる
3. 新ディスクの `product_catalog_no` を既存商品に固定
4. **既存ディスク + 新ディスクを品番昇順にソートし、1 始まり連番で再採番**。既存ディスクのうち採番値が変わるものは `DiscsRepository.UpdateDiscNoInSetAsync` で `disc_no_in_set` のみ更新（タイトル等の他カラムは保全）。新ディスクには連番上の自分のスロット番号を設定
5. 既存所属ディスク数 + 1 を `Product.disc_count` に反映して `Products.UpdateAsync`
6. 新ディスク本体を `DiscsRepository.UpsertAsync`
7. CD ならトラック群、BD/DVD ならチャプター群を一括登録

MySQL のオートコミット動作のため、各ステップは個別に確定します。`CreateProductAndCommitAsync` と同じ実装方針で、トランザクション境界は呼び出し側の責任とせず、運用上は順次コミット前提です（途中で失敗した場合は手動修復）。

**設計上の注意点**

- 商品の `disc_count` は「現在の所属ディスク数 + 1」で算出するため、再採番後の連番の終端と一致する
- 品番ソートのキー比較は `StringComparison.Ordinal`（プリキュア BD/DVD/CD は「アルファベット 4 文字 + ハイフン + 数字 4-5 桁」フォーマットが大半で、単純な ASCII 順序が自然順と一致する）
- `UpdateDiscNoInSetAsync` は採番値が変わる行に対してのみ発行されるため、既に正しい連番になっている既存ディスクへの無駄な UPDATE は走らない
- 編集系コンボはすべて短縮名 (`title_short`) 優先表示の設計に統一されているため、`ConfirmAttachDialog` のシリーズコンボも同じく `title_short` 優先（無ければ `title`）

#### C'. ディスク・トラック閲覧画面（読み取り専用）

`PrecureDataStars.Catalog` のメニュー「ディスク・トラック閲覧」は、ディスク → トラックを翻訳済みの表示値で一覧する参照専用ビューです。編集は一切行いません。

**画面構成**

- **ツールバー**（最上部）: 検索キーワード（品番 / タイトル / シリーズ名に部分一致）、シリーズ絞り込みドロップダウン、再読込ボタン、件数表示
- **ディスク一覧**（上段、SplitContainer）
- **トラック一覧**（下段、SplitContainer。ディスク選択に応じて更新）

外周に 10 px の余白を設け、上下ペインの分割バーも若干太めに取って視覚的な窮屈さを抑えています。

**上下ペインを常に半々で自動追従**

上下ペイン（ディスク一覧 / トラック一覧）はウインドウのリサイズに合わせて常に縦方向半々で表示されます。`splitMain.SizeChanged` イベントで都度 `(splitMain.Height - splitMain.SplitterWidth) / 2` を SplitterDistance に書き戻すことで実現しています。ユーザがバーを手動でドラッグすることは引き続き可能ですが、次のリサイズで自動的に半々に戻ります。

**ディスク一覧のカラム**（左から順）

| カラム | 幅 | 内容 |
|---|---|---|
| 品番 | 110 | `discs.catalog_no` |
| タイトル | Fill | `discs.title` を優先、無ければ所属商品タイトル |
| シリーズ | 140 | シリーズの略称（無ければ正式名） |
| 商品種別 | 100 | `product_kinds.name_ja`（翻訳値） |
| メディア | 70 | `discs.media_format`（CD/BD/DVD 等） |
| 発売日 | 100 | 所属商品の `release_date`。`yyyy-MM-dd` 表記 |
| 枚数 | 70 | 2 枚組以上のときのみ **`n / m`** 形式で表示。単品は空欄 |
| トラック数 | 75 | `discs.total_tracks` |
| 総尺 | 95 | **M:SS.fff 形式**。CD は `total_length_frames` (1/75 秒) から、BD/DVD は `total_length_ms` から算出。どちらも NULL なら `—` |

本一覧は MCN（バーコード）カラムを持ちません（MCN は `DiscsEditorForm` のディスク詳細エリアで閲覧・編集できます）。

**トラック一覧のカラム**（左から順）

| カラム | 幅 | 内容 |
|---|---|---|
| # | 52 | トラック番号。sub_order=0 は `"24"` のように番号のみ、sub_order&gt;=1 の行（主に歌の重ね録り別バージョン等）は `"24-2"` / `"24-3"` のように枝番を付加（右寄せ） |
| 種別 | 70 | `track_content_kinds.name_ja` |
| タイトル | 220 | 下記「タイトル解決・BGM 集約ルール」参照 |
| アーティスト | 180 | SONG は歌唱者→CD-Text、BGM は **空欄**（作曲／編曲は別カラム）、その他は CD-Text |
| 作詞 | 110 | SONG は `songs.lyricist_name`、BGM/その他は空欄 |
| 作曲 | 110 | SONG は `songs.composer_name`、BGM は `bgm_cues.composer_name`、その他は空欄 |
| 編曲 | 110 | SONG は `songs.arranger_name`、BGM は `bgm_cues.arranger_name`、その他は空欄 |
| 尺 | 90 | M:SS.fff（右寄せ）。length_frames があれば 1/75 秒精度で算出、無ければ BGM cue の秒数にフォールバック |
| 備考 | Fill | `tracks.notes` |

本一覧は `ISRC` カラムを持ちません（ISRC は `DiscsEditorForm` のトラック詳細で閲覧・編集できます）。

**タイトル解決・BGM 集約ルール**

閲覧画面のタイトル列は、内容種別と sub_order 行の有無で以下のように組み立てられます。BGM 以外の集約は行いません。

- **SONG**: `track_title_override` → (`variant_label` または親曲名) + ` [サイズ]` + ` [パート]` → `cd_text_title` の順
- **BGM（単独 sub_order 行）**: 主タイトル（`track_title_override` → `cd_text_title` → `menu_title` → `m_no_detail` の優先順）に、必ず `(m_no_detail [menu_title])` の注釈を後置する
 - 例: `track_title_override = "決戦のテーマ"` / `m_no_detail = "M220b Rhythm Cut"` / `menu_title = "戦闘・危機一髪"` のとき表示は `決戦のテーマ (M220b Rhythm Cut [戦闘・危機一髪])`
 - `menu_title` が NULL のときは `{主タイトル} (m_no_detail)` のみ
 - `bgm_cues` の JOIN が外れた（FK 切れ等）場合は注釈部を付けず主タイトルのみ
 - **`bgm_cues.is_temp_m_no = 1` の行（仮 M 番号）は閲覧 UI で `m_no_detail` を非表示**: 主タイトルのフォールバック候補からも、注釈の `(m_no_detail [menu_title])` 部分からも除外される（`menu_title` 単独になる、または注釈ごと省略される）。マスタメンテ画面（劇伴マスタ管理）では引き続き `m_no_detail` を素のまま表示・編集できる
- **BGM（同一 track_no で sub_order が複数ある、いわゆるメドレー構成の場合）**: sub_order 全行を **1 行に集約**し、主タイトルは sub_order=0 行のものを採用。注釈部には全 sub_order 行の `m_no_detail [menu_title]` を ` + ` 区切りで連結する
 - 例: sub_order=0 が `M84(スローテンポ) [危機]`、sub_order=1 が `M84(アップテンポ) [危機]` のとき、1 行にまとめて `手ごわい相手 (M84(スローテンポ) [危機] + M84(アップテンポ) [危機])` と表示
 - 集約時の作詞/作曲/編曲・尺・備考・アーティストは sub_order=0 行のものを採用（通常は同一セッション内で作曲者も同じだが、異なる場合でも子行は隠れる）
- **DRAMA / RADIO / LIVE / TIE_UP / OTHER**: `track_title_override` → `cd_text_title`。sub_order 複数行がある場合は集約せず別行で表示し、`#` に枝番（`24-2` 等）を付ける

**尺整形ルール**（トラック・ディスク総尺で共通）

- `length_frames`（CD-DA、1/75 秒）があれば: 秒 + ミリ秒（1 フレーム = 1000/75 ≒ 13.333 ms、丸めで 1000 ms 到達時は秒を 1 繰り上げ）
- `length_frames` が無く `length_seconds` / `total_length_ms` があれば: そのミリ秒値または秒値（ミリ秒値は `.000` 固定）
- どれも無ければ: `—`

#### C'''. 歌マスタ管理画面

`PrecureDataStars.Catalog` のメニュー「歌マスタ管理...」で、`songs`（メロディ + アレンジ単位の曲マスタ）と `song_recordings`（歌唱者バージョン）の 2 階層を編集します。

**画面構成**

- **検索バー**（最上部）: タイトル／かなの部分一致テキスト、シリーズ絞り込み、音楽種別絞り込み、検索ボタン、**CSV取り込みボタン**
- **上段**: 左に曲一覧、右に曲詳細（タイトル・かな・音楽種別コンボ・シリーズコンボ・作詞名・作詞名かな・作曲名・作曲名かな・編曲名・編曲名かな・備考）
- **下段左**: 選択中曲の歌唱者バージョン一覧 / バージョン詳細（歌手名・歌手名かな・バリエーションラベル・備考）
- **下段右**: 選択中バージョンの収録ディスク・トラック一覧（読み取り専用）

**入力補完**

作詞・作曲・編曲・歌手のテキストボックス（およびそれぞれのかな欄）に、`AutoCompleteSource.CustomSource` で既存マスタのユニーク氏名一覧を注入しています。`AutoCompleteMode.SuggestAppend` により、1 文字目から候補ドロップダウンが表示され、Tab / Enter で確定できます。候補のロードはフォーム起動時と CSV 取り込み完了直後に行われ、新しく登録した氏名もすぐに候補に乗ります。

**CSV 一括取り込み**

「CSV取り込み...」ボタンでファイル選択ダイアログが開き、選択後は次の 2 段階で進みます:

1. **ドライラン**: 実書き込みは行わず、行数集計（新規／更新／スキップ）と警告メッセージ（最初の 10 件）を確認ダイアログで表示
2. **本実行**: 「はい」で確定すると同じパースで UPSERT。既存判定は `(title, series_id, arranger_name)` の三要素キー（同名の曲でも編曲が違えば別行）

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

> 音楽種別 `music_class_code` は `song_recordings` 側で管理する仕様のため、歌マスタ CSV 取り込みでは扱いません。CSV に `music_class_code` 列が含まれていても無視して取り込みは継続されます（後方互換で列の存在自体は許容、値は使われず警告のみ出力）。録音バージョンごとの種別は Catalog GUI の「歌マスタ管理」画面で個別に設定してください。

サンプルは `docs/csv-templates/songs_import_sample.csv` を同梱しています。

#### C''''. 劇伴マスタ管理画面

「劇伴マスタ管理...」メニューで、`bgm_cues`（劇伴の音源 1 件 = 1 行、複合 PK `(series_id, m_no_detail)`）と関連 `bgm_sessions` を編集します。

「映画 BGM リスト管理...」メニューで、映画作品専用の `movie_bgm_cues` を編集します（`MovieBgmCuesEditorForm`）。`bgm_cues` とは別概念で、映画にはセッション・パートの概念が無く、その映画固有の M ナンバー文字列・順序（`seq`）・サブ順序（`sub_seq`）・区分（`track_content_kinds` を共用＝SONG/BGM/.../OTHER）と、未使用（音源はあるが本編未使用）・欠番（そもそも未制作）の排他 2 フラグを持ちます。シリーズ選択コンボには映画系 kind（`MOVIE` / `MOVIE_SHORT` / `SPRING` / `EVENT`）のシリーズのみを表示し（DB 側トリガーでも担保）、グリッドで順序・M番号・区分・曲名・未使用/欠番チェックを編集して一括保存します（保存時に未使用と欠番の同時設定を検出して弾く）。映画系シリーズの詳細ページ（SiteBuilder 生成）には、1 件以上あるとき「BGM リスト」セクションを描画し、欠番行は M 番号・曲名を出さず「（欠番）」表示、未使用行は淡色＋「（未使用）」注記で視覚的に区別します。

**画面構成**

- **検索バー**（最上部）: シリーズフィルタ、セッションフィルタ、検索キーワード、検索ボタン、**CSV取り込みボタン**
- **中段**: 左に劇伴一覧、右に詳細（シリーズ・セッション・M番号詳細・M番号分類・メニュー名・作曲者・作曲者かな・編曲者・編曲者かな・尺(秒)・**仮 M 番号フラグ**・**仮番号を採番ボタン**・備考）
- **下段**: 選択中キューの収録ディスク・トラック一覧（読み取り専用）

**仮 M 番号フラグ（`is_temp_m_no`）**

M 番号が判明していない劇伴音源は、内部的に `_temp_034108` のような暫定 PK を `m_no_detail` に入れて管理する運用があります。`is_temp_m_no` カラムでこの「仮番号運用中」を明示することで、画面ごとに表示挙動を切り替えています。

| 画面 | 仮番号行の扱い |
|---|---|
| 劇伴マスタ管理（本画面） | チェックボックスとして可視化、`m_no_detail` は素のまま表示・編集可。判明したら実番号にリネーム＋フラグを 0 に戻す運用 |
| ディスク・トラック閲覧 | `m_no_detail` を非表示にし、フォールバック候補からも注釈からも除外 |
| トラック管理の BGM 候補リスト | 既定で除外。「仮番号を候補に含める」チェックで明示的に含められる |

**仮番号採番ボタン**: 「仮番号を採番」を押すと、編集中シリーズ配下の既存 `_temp_NNNNNN` 連番から次の値（6 桁ゼロ埋め）を自動生成して `m_no_detail` フィールドに投入し、フラグもオンになります。既存連番に欠番があっても詰めず、最大値 + 1 を返します（採番アルゴリズムは `BgmCuesRepository.GenerateNextTempMNoAsync`）。

**CSV 一括取り込み**

歌マスタ同様、ドライラン → 本実行の 2 段階。`session_name` がシリーズ内で未登録なら自動採番（既存最大 `session_no` + 1）して `bgm_sessions` を新規作成します。`m_no_detail` が空欄でも `is_temp_m_no` フラグが立っていれば `_temp_NNNNNN` を自動採番してインサートします（フラグが偽で空欄の行はスキップ＋警告）。

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

サンプルは `docs/csv-templates/bgm_cues_import_sample.csv` を同梱しています。

#### C'''''. マスタ管理画面

`PrecureDataStars.Catalog` のメニュー「マスタ管理」で、小マスタ群を 1 画面の TabControl で編集します。タブ構成は以下の 7 つ:

| タブ名 | 対象テーブル | 主キー |
|---|---|---|
| 商品種別 | `product_kinds` | `kind_code` |
| ディスク種別 | `disc_kinds` | `kind_code` |
| トラック内容 | `track_content_kinds` | `kind_code` |
| 曲・音楽種別 | `song_music_classes` | `class_code` |
| 曲・サイズ種別 | `song_size_variants` | `variant_code` |
| 曲・パート種別 | `song_part_variants` | `variant_code` |
| 劇伴・セッション | `bgm_sessions` | `(series_id, session_no)` |

**画面構成**

各タブは上半分にグリッド、下半分に編集フォームと操作ボタンが並びます。`bgm_sessions` を除く 6 つのマスタタブは共通レイアウト（`BuildTab` ヘルパで生成）で、以下のボタンを縦並びに 4 つ持ちます:

- **新規**: フォーム入力欄をすべて空にし、グリッド選択を解除する。これから入力する内容を新しいレコードとして登録する操作の起点。新規追加と既存行の編集を見た目で明確に区別する。
- **保存 / 更新**: 入力欄のコードに基づいて UPSERT を実行（同コードがあれば更新、なければ INSERT）。
- **選択行を削除**: グリッドで選択中の行を削除する（FK で参照されている場合は失敗）。
- **並べ替えを反映** : 後述の行ドラッグ&ドロップで変更したグリッド上の並び順を、`display_order` カラムに `1, 2, 3, ...` として一斉反映する。確認ダイアログを経て実行。

`bgm_sessions` タブは PK が `(series_id, session_no)` の 2 列で表示順を `session_no` が兼ねるため、共通 `BuildTab` を使わず専用 `BuildBgmSessionsTab` で構築されます。シリーズ選択コンボでフィルタしたうえで、`session_no` を自動採番して新規追加する「新規追加」「保存 / 更新」「選択行を削除」の 3 ボタン構成（並べ替えは対象外）。

**行ドラッグ&ドロップによる並べ替え** （`bgm_sessions` を除く 6 マスタ）

`display_order` の手入力に加え、DataGridView の行を上下にマウスドラッグして並べ替えできます:

1. 行をクリックしてドラッグ → 希望位置にドロップ。複数回繰り返して目的の順序に並べ替える。
2. ドラッグだけでは DB は変わらず、グリッド表示上の List 内で要素が入れ替わるだけ。
3. **「並べ替えを反映」ボタン**を押すと「現在の並び順で表示順を 1〜N に振り直しますがよろしいですか？」の確認ダイアログ。Yes でグリッドの先頭から `display_order = 1, 2, 3, ...` を割り当てて全件 UPSERT。

ドラッグ実装は `EnableRowDrag` 共通ヘルパで、`SystemInformation.DragSize` を超える移動でドラッグ開始（クリック選択との誤動作を防止）、`DataSource` が `IList` の場合のみ要素を入れ替えて再バインドします。各マスタタブの `LoadAllAsync` / 「並べ替えを反映」後の再読み込みで `(await Repo.GetAllAsync()).ToList()` をバインドする実装になっており、ドラッグ操作の前提が常に整っています。

**監査列の自動非表示** 

すべてのグリッドで `CreatedAt` / `UpdatedAt` / `CreatedBy` / `UpdatedBy` 列は `DataBindingComplete` 時に自動的に Visible = false に設定されます。マスタの実運用で必要な情報は「コード / 名称(日) / 名称(英) / 表示順」の 4 列のみで、監査列はノイズになるため。実装は `HideAuditColumns` 共通ヘルパで、コンストラクタで全グリッドに 1 度だけ結線します。

**`CreatedBy` の保全**

並べ替え反映時は同じ List 内のアイテムを再 UPSERT しますが、Repository の SQL が `INSERT ... ON DUPLICATE KEY UPDATE` の `UPDATE` 部分で `created_by` を含めない設計のため、既存行の `CreatedBy` は DB レベルで保全されます。`UpdatedBy` のみ `Environment.UserName` で更新されます。

**ウインドウサイズ**

`ClientSize = 1000×680`、`StartPosition = CenterScreen`。

### クレジット編集

`PrecureDataStars.Catalog` のメインメニュー「クレジット編集...」から `CreditEditorForm` を起動して、シリーズまたはエピソードに紐づく OP/ED クレジットの 6 階層（Card / Tier / Group / Role / Block / Entry）を編集する。3 ペイン構成：

- **左ペイン**: scope（SERIES / EPISODE）の絞込み、シリーズ・エピソードの選択コンボ、クレジット一覧 ListBox、新規クレジット作成・**話数コピー** ボタン、選択中クレジットのプロパティ編集（presentation / part_type / 備考）と「プロパティ保存」「クレジット削除」ボタン。
- **中央ペイン**: 階層ツリーと「+ カード」「+ Tier」「+ Group」「+ 役職」「+ ブロック」「+ エントリ」「↑」「↓」「✖ 削除」のツリー編集ボタン群、画面下に **「💾 保存」「✖ 取消」**。
- **右ペイン**: ツリーで選択したノードに応じて切り替わる。Block 選択時は `BlockEditorPanel`（col_count / block_seq / leading_company_alias_id / notes の編集と「適用」ボタン）、Entry 選択時は `EntryEditorPanel`（種別ごとの入力 UI と「保存」「削除」ボタン）。

#### 編集の流れ（Draft セッション方式）

1. クレジットを選択すると、`CreditDraftLoader` が DB から全階層を読み込んで Draft セッションをメモリ上に構築する。
2. ユーザーがツリーやパネルで操作（追加・編集・削除・並べ替え・DnD 移動）すると、すべて Draft オブジェクトに対して反映され、DB は触らない。
3. 未保存変更があると、ツリー背景色が **薄い黄色**、ステータスバー末尾に「★ 未保存の変更あり」が表示され、画面下部の「💾 保存」「✖ 取消」が Enabled になる。
4. 「💾 保存」を押すと `CreditSaveService` が 5 フェーズ（**1A** エントリ削除 → **2** 新規 → **3** 更新 → **1B** ブロック以上の親階層削除 → **4** seq 整合性）を **1 トランザクション** 内で実行して DB へ確定する。失敗すれば全体ロールバック。親階層 DELETE を更新フェーズの後ろに置くことで、DnD で別ブロックに移動したエントリ／別役職に移動したブロック等が、旧親 DELETE の CASCADE で巻き添え削除される事故を防ぐ。
5. 「✖ 取消」を押すと現在の Draft を破棄して DB から再読み込みする。

#### 話数コピー（シリーズ跨ぎ対応）

新シリーズの第 1 話を作成する際、毎回ゼロから役職構造を組み立てるのは非効率なので、**前作の OP / ED を丸ごと複製してから差分編集** するワークフローに対応する：

1. コピー元クレジットを左ペインで選択 → **「📋 話数コピー...」ボタン** を押下。
2. `CreditCopyDialog` でコピー先のシリーズ・エピソード・presentation・part_type・備考を指定（クレジット種別はコピー元と同じで固定）。
3. コピー先に同種クレジットが既に存在する場合は「上書き／中止」を選ぶ（上書き時は既存を即時論理削除）。
4. `CreditDraftLoader.CloneForCopyAsync` がコピー元を読み込んで配下を全部 `State = Added` で deep clone し、コピー先 Draft を構築。
5. 画面がコピー先 Draft に切り替わる（黄色背景・未保存マーク）。内容を確認・編集してから「💾 保存」で 1 トランザクション INSERT。

#### HTML プレビュー

クレジット編集中、テンプレ展開後の完成形を確認したい場合は、左ペインの「🌐 HTMLプレビュー」ボタンを押す。

- 非モーダルの新ウィンドウが画面右側に開き、選択中のクレジット（エピソードスコープなら同エピソードの OP / ED 等を縦に並べて）を `WebBrowser` コントロールで HTML 表示する
- シリーズ書式上書き（`series_role_format_overrides`）があればそれを優先、無ければ `roles.default_format_template` の DSL を `RoleTemplateRenderer` で展開
- テンプレが未定義の役職は「役職名 + 配下エントリの右並び表」のフォールバック表示（実物のスタッフロール風レイアウト）
- Card / Tier / Group / Block の階層は CSS の枠線とインデントで視覚的に区切る
- プレビューを開いたままクレジット切替・保存・取消をすると、自動的に追従して再描画される
- 未保存 Draft がある場合は確認ダイアログで「DB の現状を見るか／キャンセルか」を選ぶ（プレビューは DB ベース描画のため、編集途中状態は反映されない）

#### 未保存ライフサイクル管理

未保存変更がある状態で別操作（クレジット切替・シリーズ／エピソード切替・フォーム閉じ）を行おうとすると、3 択の確認ダイアログが出る：

- **保存して続行**: 現在の Draft を保存してから次の操作へ進む
- **破棄して続行**: 現在の Draft を破棄して次の操作へ進む
- **キャンセル**: 操作を取りやめて元の状態に戻る（lstCredits の選択を元のクレジットへ復帰）

これにより「うっかり別クレジットに切り替えて未保存変更を失う」事故を防ぐ。

#### クレジット一括入力

長尺クレジットをツリー編集で 1 件ずつ追加するのは現実的でないため、テキスト形式でまとめて流し込めるダイアログを用意した。左ペイン下部の **「📝 クレジット一括入力...」** ボタンから開く。**ツリー右クリック「📝 一括入力で編集...」** メニューでも、選択スコープ（クレジット全体／カード／ティア／グループ／役職）の中身を編集できる。

**起動経路**:

| モード | 起動方法 | 動作 |
|---|---|---|
| `AppendToCredit` | 左ペイン「📝 クレジット一括入力...」ボタン | **クレジット全体の構造差分検出モード**。起動時に現状全文を `CreditBulkInputEncoder.EncodeFullAsync` で逆翻訳した文字列が初期値として入る。適用時は旧テキストと新テキストの構造差分を検出して、変わった末端だけ Modified / Added / Deleted で反映（変わっていない Card / Tier / Group / Role / Block / Entry はすべて Unchanged 維持で `alias_id` や監査列も保持）。Block 内 Entry は LCS マッチングで行入れ替え（ヒューマンエラー）を `entry_seq` 更新の Modified として拾う |
| `ReplaceScope` | ツリー右クリック「📝 一括入力で編集...」 | 選択スコープの中身を **置換**。起動時に既存内容を `CreditBulkInputEncoder` で逆翻訳した文字列が初期値として入る。スコープ配下を全 DELETE → 全 INSERT する全置換セマンティクス（差分検出はしない、範囲限定の用途向け） |

**書式の要点**:

- 行末コロン `XXX:` で役職開始、空行で同役職内のブロック区切り、`-` / `--` / `---` / `----` でブロック・グループ・ティア・カード区切り、タブ区切りで `col_count` 並び、`<キャラ>声優` で CHARACTER_VOICE
- `[屋号#CIバージョン]` で LOGO エントリ（最右の `#` で屋号と CI バージョンに分解、屋号下のロゴから引き当て）
- 行頭 `🎬`（U+1F3AC、絵文字）で本放送限定エントリ（`is_broadcast_only=1`）
- 行頭 `& `（半角アンパサンド + 半角SP）で直前エントリと A/B 併記（保存時に `parallel_with_entry_id` 解決）
- 行末 ` // 備考` で当該エントリの `notes` 設定
- `@cols=N` で当該ブロックの `col_count` を明示指定（タブ数推測より優先）
- `@notes=値` で直近スコープ（Card/Tier/Group/Role/Block のうち最後に開いたもの）の `notes` を設定
- 修飾子は重ねがけ可（例: `🎬 & 山田 太郎 // 旧名義あり`）。順序を問わない
- 250 ms デバウンスでパースしてプレビュー反映、Block 重大度の警告 1 件で「適用」ボタンが Disabled
- 適用時、未登録役職は `QuickAddRoleDialog` を 1 件ずつ起動して登録（日本語名は事前入力済）、Person / Character / Company は自動 QuickAdd、引き当てに失敗した名前は TEXT エントリ（`raw_text`）に降格
- LOGO エントリのみ屋号 + CI バージョン未ヒット時は **TEXT 降格 + InfoMessage**（マスタ管理画面の「ロゴ」タブで明示登録するよう促す。LOGO は CI デザイン情報を伴うため自動投入しない方針）
- Draft セッションへの追加は **構造差分検出**（AppendToCredit モード）または **スコープ置換**（ReplaceScope モード）。DB 確定は通常の「💾 保存」フロー

**ラウンドトリップ性**:

`CreditBulkInputEncoder` は Draft 階層を一括入力フォーマットに逆翻訳するため、「右クリック → 一括入力で編集 → 編集 → 適用」のサイクルで既存クレジットの大幅な書き換えがテキストエディタ感覚で行える。Encoder の出力を Parser に通すと、マスタ ID 解決後の状態で同じ構造が再現される（例外: `IsForcedNewCharacter` のアスタは Draft 上で追跡しないため再エンコードでは消える）。

**「旧名義 =&gt; 新名義」記法**

人物・キャラ・企業屋号・LOGO の屋号部いずれの位置でも、`旧名義 =&gt; 新名義` という記法でリダイレクトを明示できる。**矢印の向きは「名義が変わった」遷移方向に揃えてあり、左 = 既存マスタの参照キー、右 = この行で実際に表示する別名義** を表す。

- 例: `青木 久美子 => 青木 久実子` — 左の「青木 久美子」を `person_aliases.name` 完全一致で引き当てて `person_id` を取得し、同 person 配下に右の「青木 久実子」を新 alias として登録
- 例: `[東映アニメ => 東映アニメーション]` — COMPANY エントリ。同じく既存 company に新屋号を追加
- 例: `<キュアブラック => 美墨なぎさ>本名 陽子` — VOICE_CAST 行。`<>` 内に `=>` でキャラの別名義を、外側の声優部にも独立して `=>` を書ける（両側併用可）
- 例: `[東映アニメ => 東映アニメーション#2024年版]` — LOGO エントリ。屋号部分（`#` の左側）のみ `=>` を解釈、CI バージョン違いは別 logos 行で表現するためリダイレクト対象外

旧名義が既存マスタに見つからない場合は警告 `InfoMessages` を出した上で、右側のみで通常の新規作成にフォールバック。タイポしたまま気づかずに人物が量産される事故を抑える。

**似て非なる名義の警告 + 新規登録候補の事前通知**

新規作成しようとしている名義（`=>` リダイレクトで決着しなかったもの）について、`person_aliases` / `character_aliases` / `company_aliases` を全件取得し、空白除去後の **LCS（最長共通部分列）／ max(len) ≥ 0.5** を満たすが完全一致でない既存名義があれば警告に積む。漢字違い（「五條 真由美」↔「五条真由美」）や空白違いの誤入力を検出し、ユーザーに「同一人物なら `=>` で書くか、マスタ管理画面で別名義として統合してください」と促す。LOGO の屋号引き当て失敗時は `company_aliases` に対して同じ判定が走る。

**新規登録候補の事前通知**: 完全一致なし + 似て非なる候補もなし、つまり「ピュアに新規登録される予定」の名義については、警告ペインに **情報レベル（ℹ）の警告**として「ℹ N 行目: ○○名義「△△」は新規登録候補です（マスタに既存名義および類似名義なし）。Apply 時に新規 person + alias を作成します。」と表示する。同様に **`roles` マスタに無い役職表示名** も `name_ja` 完全一致で突合して、未登録なら情報レベルで「Apply 時に QuickAddRoleDialog で role_code / 英名 / role_format_kind を入力して新規登録します。」と表示する。これにより、ユーザーは Apply 前にマスタ追加が発生する箇所を全部チェックできる。

**判定タイミング**: 入力テキストの 250 ms デバウンス後にリアルタイム判定。パース完了直後に `CreditBulkApplyService.CheckSimilarNamesAsync` が呼ばれ、新規作成予定の名義（リダイレクト無し + `SearchAsync` 完全一致なし）について全件比較を実施。結果は **警告ペイン**（`lstWarnings`）に他のパース警告と同じ並びで表示される（行番号付き、`Severity = Warning` または `Info` なので Apply ボタンは無効化されない）。連続入力時は `CancellationTokenSource` で前の判定を取り消し、最後のテキストに対する結果だけがペインに残る。比較中は警告ペイン上部に **「似て非なる名義を比較中... (n/total)」** のステータスラベルが出て完了で消える。Apply 経路の `ResolveOrCreate` 内でも同じ判定が冗長に走り、こちらは適用後の MessageBox の `InfoMessages` に積まれる（入力後すぐ Apply して類似度判定が完了する前に呼ばれた場合の救済）。

**ダイアログレイアウト**

右ペインのプレビュー（上）：警告（下）の比率は **4:1（8:2）**（`splitterDistance` 515）。警告ペインがプレビューを過度に圧迫しない配分とする。

#### Card / Tier / Group / Role の備考編集

クレジット編集画面のツリーで Card / Tier / Group / 役職ノードを選択すると、右ペインに **`NodePropertiesEditorPanel`** が表示され、対応する DB 列（`credit_cards.notes` / `credit_card_tiers.notes` / `credit_card_groups.notes` / `credit_card_roles.notes`）の備考を直接編集できる。複数行 TextBox + 「💾 保存」ボタンの単純な構成。保存ボタン押下で Draft.Entity.Notes 更新 + `MarkModified()` を実行し、ツリー再描画で `📝<備考>` ラベルが反映される。DB への書き込みは通常の「💾 保存」ボタンで一括コミット。

#### 名寄せ機能

クレジット入力中にうっかり同名人物を別人として 2 件登録してしまったり、改名（旧屋号 → 新屋号、旧名義 → 新名義）が発生したとき用に、`CreditMastersEditorForm` の人物名義 / 企業屋号 / キャラ名義タブそれぞれにボタンを 2 つずつ追加した：

- **「別人物（企業／キャラ）に付け替え...」** (`AliasReassignDialog`)：選択中名義の紐付け先だけを別の既存親に変更する。親本体の表示名は変更しない。
- **「この名義で改名...」** (`AliasRenameDialog`)：新表記を入力して改名する。人物・企業の場合は **新 alias を作成して旧 alias と predecessor/successor で自動リンク**（履歴を残す）、キャラの場合は現 alias を上書き（character_aliases に履歴列が無いため）。

孤立した旧親（紐付く名義が 0 件になった `persons` / `companies` / `characters`）は付け替え時に自動で論理削除される。

---


### クレジット入力レシピ集（役職別の正しいブロック構成）

クレジットは「役職 → ブロック → エントリ」の 3 階層構造を取り、役職に紐づく `default_format_template`（DSL テンプレート）が **どのブロックから何のエントリを取り出してどう並べるか** を決める。役職ごとに想定するブロック構成が異なるため、本節では代表的な役職について「ツリー上どう積めば期待する展開結果になるか」を示す。

> **💡 補足**: 長尺クレジット（とくに連名が多い「制作協力」「アニメーション制作」など）を TreeView で 1 件ずつ積み上げるのは手数が多い。**「📝 クレジット一括入力...」** ボタンでテキスト形式でまとめて投入でき、既存クレジットも **ツリー右クリック「📝 一括入力で編集...」** から逆翻訳して書き換え・反映できる。本節のレシピは最終的に作りたい構造を理解するためのリファレンスとして読み、実際の入力は一括入力 → 微調整の順で進めるとよい。

#### 連載（`SERIALIZED_IN`） + 漫画（`MANGA`）

**役職構成**: 連載クレジットでは、雑誌等の連載媒体を表す `SERIALIZED_IN` と、漫画家を表す `MANGA` の **2 役職に分割**している（同居させると `CreditInvolvementIndex` の集計で漫画家が「連載」役職として誤集計されるため）：

- `SERIALIZED_IN`「連載」: 雑誌（COMPANY エントリ）のみ
- `MANGA`「漫画」: 漫画家（PERSON エントリ）のみ

表示上は `SERIALIZED_IN` テンプレの中で **兄弟役職参照構文 `{ROLE:MANGA.PERSONS}`** を使い、同 Group 内の MANGA 役職下の人物を取り込む。これにより画像 1 のレイアウト（「漫画・上北 ふたご」を「連載」見出しの直下に表示）を保ちつつ、集計は `MANGA` → 「漫画」、雑誌 → 「連載」と正しく分かれる。

テンプレ内の漫画役職ラベルはプレースホルダ **`{ROLE_LINK:code=MANGA}`** で表現し、役職詳細ページ `/creators/roles/manga/`（URL パス上の役職コードは小文字）への太字リンクとして出力する。`<strong>` ラップはレンダラ側で一律付与されるため、テンプレ作者は `<strong>` を書かない。「役職リンクなら必ず太字、違えば太字ではない」という見た目ルールを DSL の責務として保証する設計。

**テンプレ（`SERIALIZED_IN`）**:
```
{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
{ROLE_LINK:code=MANGA}・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

**テンプレ（`MANGA`）**:
```
{PERSONS}
```

`MANGA` テンプレは普段 `{ROLE:MANGA.PERSONS}` 経由で間接的に評価される。`MANGA` 役職を独立に描画する状況（クレジット画面で漫画役職だけのカードを作るレアケース）に備えて単純な `{PERSONS}` を持たせる。

**期待する展開結果（プレーンテキスト相当のレイアウト）**:
```
連載 講談社「なかよし」
 漫画・上北 ふたご
 「たのしい幼稚園」
 「おともだち」ほか
```

**期待する HTML 出力（SiteBuilder 側）**:
```html
<a href="/companies/6/">講談社</a>「<a href="/companies/6/">なかよし</a>」
<strong><a href="/creators/roles/manga/">漫画</a></strong>・<a href="/persons/5/">上北 ふたご</a>
　「<a href="/companies/6/">たのしい幼稚園</a>」
　「<a href="/companies/6/">おともだち</a>」ほか
```

**Catalog 側プレビュー（リンクなし版）**:
```html
講談社「なかよし」
<strong>漫画</strong>・上北 ふたご
　「たのしい幼稚園」
　「おともだち」ほか
```
プレビュー画面では `LookupRoleHtmlAsync` がエスケープ済み表示名のみを返し、レンダラが `<strong>` ラップを付与するので、見た目は従来通り「漫画だけ太字」のままになる（差分はリンクの有無のみ）。

Catalog プレビュー（`CreditPreviewRenderer`）の DB 版・Draft 版いずれのループにも、SiteBuilder 側 `CreditTreeRenderer` と同一規則の **`consumedRoleCodes` 事前スキャン** を導入している。同 Group 内の各役職テンプレを `{ROLE:<CODE>.` で軽量スキャンし、他役職に消費される `role_code`（典型例：`SERIALIZED_IN` が `{ROLE:MANGA.PERSONS}` で取り込む `MANGA`）をメインループの描画対象から除外する。これにより、プレビュー上で「漫画」役職が `SERIALIZED_IN` に取り込まれた上にさらに単独役職として二重表示される不具合が解消され、SiteBuilder と表示が一致する。あわせて声の出演テーブル末尾の「協力」（`CASTING_COOPERATION`）行を、SiteBuilder の協力行と同じ **3 セル構成**（1 段目＝役職名カラム空、2 段目＝キャラ名カラムに「協力」、3 段目＝声優名カラムに屋号一覧）に統一している。プレビューは UI のため「協力」も屋号もリンク化せず（屋号はエスケープ済みプレーン文字列を全角スペース連結）、右寄せ・太字は埋め込み CSS の `.cooperation-row td.character-cell` で SiteBuilder の同名装飾と同じ見た目に揃える。

**ブロック構成**:
```
Card / Tier / Group
├─ Role: SERIALIZED_IN 連載 (order_in_group=1)
│ ├─ Block #1 (1 cols, 1 entries) ← {#BLOCKS:first}
│ │ leading_company_alias_id = 「講談社」屋号 ID
│ │ └─ [COMPANY] #1 「なかよし」屋号
│ ├─ Block #2 (1 cols, 1 entries) ← {#BLOCKS:rest} の最初
│ │ └─ [COMPANY] #1 「たのしい幼稚園」屋号
│ └─ Block #3 (1 cols, 1 entries) ← {#BLOCKS:rest} の続き
│ └─ [COMPANY] #1 「おともだち」屋号
└─ Role: MANGA 漫画 (order_in_group=2)
 └─ Block #1 (1 cols, 1 entries)
 └─ [PERSON] #1 「上北 ふたご」名義
```

**ポイント**:
- **`{ROLE_LINK:code=MANGA}`** は役職コードから役職詳細ページへのリンク化済み HTML を太字付きで埋め込むプレースホルダ。SiteBuilder 側は `<strong><a href="/creators/roles/manga/">漫画</a></strong>`（URL パス上の役職コードは小文字。リンク URL は `PathUtil.RoleStatsUrl` に集約）、Catalog 側プレビューは `<strong>漫画</strong>`（リンクなし）を出力。`<strong>` ラップはレンダラ側で一律付与されるため、テンプレに `<strong>` を書かない運用に統一した（「役職リンクなら必ず太字」を DSL レンダラの責務として保証）。
- **`{ROLE:MANGA.PERSONS}`** は兄弟役職参照の構文。同 Group 内で `role_code='MANGA'` の役職を 1 つ探し、その役職配下の Block 群を一巡りして `{PERSONS}` を Block ごとに評価し、Block 間は内側プレースホルダの `sep` オプション（既定 `、`）で連結する。
- `{ROLE:CODE.PLACEHOLDER}` の **1 段ネスト不可**: `MANGA` テンプレ内で `{ROLE:SERIALIZED_IN.…}` と書いても無限ループせず、再帰経路の `{ROLE:…}` は空文字に展開される（無限ループ防止）。
- 旧構造（`SERIALIZED_IN` 配下に PERSON 同居）から本構造への変換、および `format_template` への `{ROLE_LINK:code=MANGA}` 反映は、いずれも `db/migrations/` 配下の冪等な SQL で行える（再実行可能）。
- 雑誌名（「なかよし」など）は屋号マスタ `company_aliases` に **別エントリ** として登録する運用（出版社「株式会社講談社」とは別屋号）。
- **`{COMPANIES:wrap=""}`** の wrap オプションは `「」` の括弧文字を表す（先頭が開き、末尾が閉じ）。
- 漫画家共著の場合は `MANGA` 役職下の Block 内に PERSON エントリを 2 件並べる（`{PERSONS}` の `sep` 既定値 `、` で結合）。Block を分けると `{ROLE:MANGA.PERSONS}` の Block 間連結は同じく `sep` 規定値が使われる。

#### オープニング主題歌（`THEME_SONG_OP`） / エンディング主題歌（`THEME_SONG_ED`）

**テンプレ（OP）**:
```
{ROLE_NAME}
{THEME_SONGS:kind=OP}
```

**テンプレ（ED）**:
```
{ROLE_NAME}
{THEME_SONGS:kind=ED}
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
- 楽曲は `episode_theme_songs` テーブルが真実の源泉なので、ツリー上は **読み取り専用の楽曲仮想ノード** として自動表示される。クレジットエディタで楽曲を直接追加・編集することはできない（「クレジット系マスタ管理 → エピソード主題歌」タブで管理する）。
- レーベル名（販売元）はクレジット表記される文字列なので、ブロックの `[COMPANY]` エントリで明示的に持つ。中期以降のシリーズではレーベル変更も多いため、屋号マスタからの参照で持つことで一括管理できる。
- レーベルが複数枠出るような特殊ケースが将来発生したら、`{#BLOCKS}{COMPANIES}{/BLOCKS}` のようなテンプレに拡張する余地を残してある（現状はテンプレの末尾に Block 由来の COMPANY 連結を入れていない最小構成）。

#### 主題歌（黎明期 OP+ED 統合）（`THEME_SONG_OP_COMBINED`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=OP+ED,columns=2}
```

**期待する展開結果**:
```
主題歌
「DANZEN! ふたりはプリキュア」 「ゲッチュウ! らぶらぶぅ?!」
作詞:青木久美子 作詞:青木久美子
作曲:小杉保夫 作曲:佐藤直紀
編曲:佐藤直紀 編曲:佐藤直紀
うた:五條真由美 うた:五條真由美

マーベラスエンターテイメント
```

**ブロック構成**:
```
Role: THEME_SONG_OP_COMBINED 主題歌 (order 1) [横 2 カラム表示指定]
├─ 📀 Song(OP): 『DANZEN! ふたりはプリキュア』 ...
├─ 📀 Song(ED): 『ゲッチュウ! らぶらぶぅ?!』 ...
└─ Block #1 (1 cols, 1 entries)
 └─ [COMPANY] #1 「マーベラスエンターテイメント」屋号
```

**ポイント**:
- 黎明期（最初の 10 年程度）の OP カードに置く役職。OP 曲と ED 曲が「主題歌」という 1 枠の中に 2 カラム横並びで並ぶ表現を再現する。
- ED カードには別途主題歌役職を置かない（黎明期は OP 1 枠だけが主題歌枠）。ED カードは挿入歌があれば `INSERT_SONGS_NONCREDITED` 役職で情報保持できる（ノンクレジット楽曲の事実保持用）。

#### 挿入歌（`INSERT_SONG`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=INSERT}
```

**期待する展開結果**（挿入歌 1 曲の場合）:
```
挿入歌
「○○の歌」
作詞:○○
作曲:○○
うた:○○

マーベラスエンターテイメント
```

**ブロック構成**:
```
Role: INSERT_SONG 挿入歌 (order 1)
├─ 📀 Song(INSERT): ... ← 1 曲または複数曲
└─ Block #1 (1 cols, 1 entries)
 └─ [COMPANY] #1 レーベル屋号
```

**ポイント**:
- 12 年目以降に挿入歌が独立してクレジットされるようになった以降の挿入歌枠。複数曲ある場合は `episode_theme_songs.insert_seq` の昇順で縦並びで全部出る。
- 同 episode に挿入歌が 1 曲しかなければ 1 曲だけ出る。

#### 挿入歌（ノンクレジット）（`INSERT_SONGS_NONCREDITED`）

**テンプレ**:
```
{ROLE_NAME}
{THEME_SONGS:kind=INSERT}
```

**ブロック構成**:
```
Role: INSERT_SONGS_NONCREDITED 挿入歌（ノンクレジット） (order 1)
└─ 📀 Song(INSERT): 🚫[ノンクレジット] ... ← 視認用マーク付き
```

**ポイント**:
- 実放送ではクレジットされなかったが楽曲事実としてデータベースに保持しておきたい挿入歌枠。役職コード上は `INSERT_SONG` と同じ `kind=INSERT` を引くが、運用上は **どちらか一方だけ置く** 前提。
- 楽曲ノードラベルに `🚫[ノンクレジット]` マークが付与され、編集画面で「これらは実放送には出ない」と一目でわかる。
- 黎明期は通常クレジットでは挿入歌が出ないため、`INSERT_SONGS_NONCREDITED` 役職をクレジットエディタの末尾に置いて情報保持に使う。
- ブロック配下にレーベル `[COMPANY]` を入れることもできるが、ノンクレジットなので運用上は不要。

#### 通常役職（`PRODUCER` / `ORIGINAL` / `MUSIC` 等の人物 1 〜複数列挙）

**テンプレ（既定）**:
```
{ROLE_NAME}
{#BLOCKS}{PERSONS}{/BLOCKS}
```

**期待する展開結果**（複数人の場合）:
```
プロデューサー
西澤萌黄、高橋知子、鷲尾天
```

**ブロック構成**:
```
Role: PRODUCER プロデューサー (order 1)
└─ Block #1 (1 cols, 1 entries)
 ├─ [PERSON] #1 「西澤萌黄」名義（所属:ABC）
 ├─ [PERSON] #2 「高橋知子」名義（所属:ADK）
 └─ [PERSON] #3 「鷲尾天」名義
```

**ポイント**:
- 1 ブロックに人物名義をすべて並べるシンプルな形式。`{PERSONS}` プレースホルダの既定 `sep="、"` で読点区切り。
- 所属屋号（ABC や ADK）は `affiliation_company_alias_id` または `affiliation_text` で人物名義の小カッコ所属として表現できるが、現行テンプレでは出力していない（必要なら `{PERSONS_WITH_AFFILIATION}` 等の拡張プレースホルダを将来追加する余地あり）。

#### キャラクター × 声優（`VOICE_CAST`）

**テンプレ（想定）**:
```
{ROLE_NAME}
{#BLOCKS}{CHARACTER_VOICES}{/BLOCKS}
```
※ `{CHARACTER_VOICES}` プレースホルダは未実装。将来の拡張候補。

**ブロック構成**:
```
Role: VOICE_CAST 声の出演 (order 1)
└─ Block #1 (m×n)
 ├─ [CHARACTER_VOICE] #1 キャラ「美墨なぎさ」 / 声優「本名陽子」
 ├─ [CHARACTER_VOICE] #2 キャラ「雪城ほのか」 / 声優「ゆかな」
 └─ ...
```

**ポイント**:
- `[CHARACTER_VOICE]` エントリは「キャラクター名義（`character_aliases`）+ 声優名義（`person_aliases`）」のペアで 1 行を成す。`character_alias_id` の代わりに `raw_character_text`（モブ等のマスタ未登録）も使える。
- 役職テンプレで `{CHARACTER_VOICES}` の整形ロジックを将来実装する場合は、専用ハンドラ（`Forms/TemplateRendering/Handlers/` 配下）として追加する想定。

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

**ポイント**:
- `[LOGO]` エントリはロゴ画像と CI バージョン（時期）を持つ。同じ会社でもロゴが時期によって違うため、バージョン別管理が可能。
- テンプレで `{LOGOS}` プレースホルダを使う場合、画像ファイル名や CI ラベルを表示することになる（現状は名前文字列のみ整形。将来クレジット GUI レンダリング時に画像合成する想定）。

#### 共通の運用ルール

- **`leading_company_alias_id`** はブロック先頭に企業屋号を出すケースの特殊フィールド。連載や特殊な役職でのみ使う。通常の役職では NULL のまま。
- **`is_broadcast_only`** はブロック・エントリ単位のフラグ。本放送と円盤・配信でロゴ画像が違う等の差し替えを `is_broadcast_only=0`（既定行）と `=1`（本放送限定行）の 2 行並立で表現する。クレジットエディタでは右ペインのチェックボックスで設定。
- **`role_format_kind = 'THEME_SONG'`** の役職にはツリー上で楽曲仮想ノード（📀 Song）が自動表示される。`THEME_SONG_OP` / `THEME_SONG_ED` / `THEME_SONG_OP_COMBINED` / `INSERT_SONG` / `INSERT_SONGS_NONCREDITED` の 5 役職が該当。
- **テンプレ DSL の `{#BLOCKS:first|rest|last}`** はブロックの位置指定ループ。連載のように「1 つ目とそれ以降で表示が違う」ケースで使う。`{#BLOCKS}`（filter なし）は全ブロック。

---

### Web 公開用静的サイト生成

`PrecureDataStars.SiteBuilder` は、ローカル MySQL の内容を読み出して Web 公開用の静的 HTML 一式を生成するコンソールアプリケーションです。`precure.tv` での Web 公開を想定していますが、ツール自体は AWS 連携を含まず、純粋に「DB → 静的ファイル」変換のみを行います（成果物を S3 等に同期する処理は本ツール範囲外、手動 `aws s3 sync` 等を別途想定）。

#### A. 前提・実行方法

1. 既存の Catalog GUI / Episodes GUI と同じ MySQL データベース（`precure_datastars`）が稼働していること。
2. `PrecureDataStars.SiteBuilder/App.config.sample` を `App.config` にコピーし、`DatastarsMySql` 接続文字列を環境に合わせて書き換える。
3. 同じ `App.config` の `appSettings` で出力先・ベース URL・サイト名を指定できる:
 - `SiteOutputDir`: 生成 HTML 一式の出力先ディレクトリ（絶対パス推奨。空のときは実行ファイル直下 `out/site/` にフォールバック）
 - `SiteBaseUrl`: canonical / OGP / sitemap.xml の絶対 URL 組み立て用ベース URL（末尾スラッシュなし、例 `https://precure.tv`）。空のときは canonical 出力をスキップ
 - `SiteName`: ヘッダ・タイトルに表示するサイト名（既定 `precure-datastars`）
 - `AmazonAssociateTag`: Amazon アソシエイトのトラッキング ID（例 `yourtag-22`）。商品詳細の Amazon リンク（CD 物理パッケージ / デジタル音源の両方）に `?tag=` として付与しアフィリエイト計測に使う。空のときはリンク自体は出すが tag を付けない。PA-API による商品検索や画像取得を行う場合は、本キーとは別に Catalog / AmazonSync 側の App.config で PA-API キー（PaApi.AccessKey / PaApi.SecretKey / PaApi.PartnerTag）を設定する必要がある。
4. ビルド & 実行:
 ```bash
 dotnet run --project PrecureDataStars.SiteBuilder -c Release
 ```
5. 出力先（既定 `out/site/`）に静的 HTML 一式と `assets/site.css` が生成される。`out/` 配下は `.gitignore` で追跡対象外。

#### B. 生成されるページ

| URL パターン | 内容 |
|---|---|
| `/` | サイトトップ。シリーズ一覧をグリッド表示し、本サイトの特徴を紹介 |
| `/about/` | サイト案内・運営者情報・権利表記 |
| `/series/` | 全シリーズ索引（年代順表）。種別・話数併記 |
| `/series/{slug}/` | シリーズ詳細。基本情報・外部 URL・所属エピソード一覧。セクション順は 基本情報 → 関連作品 → **プリキュア → メインスタッフ** → BGM リスト → エピソード一覧 → 劇伴 → 外部サイト |
| `/series/{slug}/{seriesEpNo}/` | **エピソード詳細（中核ページ）**。本節 C 参照 |
| `/creators/` | クリエーターのランディング。スタッフ / 声の出演の 2 カードを案内（`/music/` と同型） |
| `/creators/staff/` | スタッフ一覧。役職順（既定）/ 五十音順 / 初参加順（シリーズ別セクション）/ 参加話数が多い順 の 4 タブ。役職順以外は人物と企業・団体を 1 リストに混在（個人/団体バッジ＋絞り込みトグル。トグルは役職順タブ非アクティブ時のみ表示）。表にシリーズ列は持たない。旧 `/persons/`・`/companies/` 索引、旧役職統計索引・総合集計の役割を統合。一度もクレジットの無い役職は「役職順」索引にも役職詳細ページにも一切出さない（関与エンティティ 0 件はスキップ） |
| `/creators/roles/{role_code}/` | 役職詳細。当該役職に関わった人物・企業/団体を 1 リストに混在し、五十音順 / 初参加順 / 担当話数が多い順 のタブで切替。順位列は持たない（脱ランキング） |
| `/creators/voice-cast/` | 声の出演一覧。**1 行＝(声優 × シリーズ × キャラ)** の粒度。キャラクター順（既定・シリーズ別セクション）/ 五十音順 / 初出演順（シリーズ別セクション）/ 出演話数が多い順 の 4 タブ。表にシリーズ列は持たず、シリーズはセクション見出しで示す |
| `/persons/{personId}/` `/companies/{companyId}/` | 人物・企業/団体の個別詳細（直リンク用。索引はクリエーターへ統合済み） |

なおトップページの DB 統計ボックスでは、人物数と企業・団体数を個別に出さず合算した「クリエーター」1 項目（`DbStats.CreatorsCount` = 人物数＋企業・団体数）として表示し、リンク先は `/creators/` ランディングとする。
| `/stats/` | 統計ランディング。サブタイトル統計・エピソード尺統計の 2 系統（クレジット関連はクリエーターへ移設） |

##### クリエーターセクションと役職詳細ページの URL

クリエーターセクションは `/creators/`（ランディング）・`/creators/staff/`（スタッフ一覧）・`/creators/roles/{role_code}/`（役職詳細）・`/creators/voice-cast/`（声の出演一覧）の 4 種を `CreatorsGenerator` が生成する。人物・企業/団体は「順位」を持たずタブ（五十音順／初参加順／担当話数が多い順 ほか）での並べ替えのみを提供する。スタッフ一覧は人物と企業・団体を 1 リストに混在させ、行ごとに個人/団体バッジで区別し、上部トグルで個人のみ・団体のみへ絞り込める（トグルはルートの `data-entity-filter` を書き換え、行の出し分けは CSS セレクタが解決）。役職詳細ページは `/creators/roles/{role_code}/` 形式。`roles` テーブルは業務コード `role_code`（`varchar(32)`・`utf8mb4_bin`）を PK とし数値サロゲートを持たないため、URL パス上の役職コードは `string.ToLowerInvariant()` で小文字化する（例: `/creators/roles/screenplay/`）。役職コードは「英大文字 + アンダースコア」のみで構成されるため小文字化しても元コードと 1 対 1 に対応し衝突しない。出力先パスと参照リンク（シリーズ／エピソード／楽曲のスタッフバッジ、クレジット階層内アンカー等）は双方とも `PathUtil.RoleStatsUrl()` に集約して整合させる。内部のデータ処理（集計キー・系譜解決など）は実コード（大文字）のまま行い、URL 文字列だけを小文字化する。バッジ色分け CSS の `data-role-code` 属性はセレクタの都合で実コード（大文字）を保持する（画面に文字としては現れない）。閲覧者向けに不要な内部コードは画面に出さない方針で、役職詳細冒頭の「役職コード／区分」表記や「歴代の名前」欄の役職コード併記は表示せず、日本語の役職名のみを出す。声の出演一覧（`/creators/voice-cast/`）は声優出演がシリーズ・キャラ単位でクレジットされる構造に合わせ、(声優 × シリーズ × キャラ) ごとに 1 行へフラット展開する（人物詳細の役職→シリーズ単位行＋キャラ名併記と同じ粒度。同じ声優が別シリーズ・別キャラで出演していればその都度別行として全キャラ名・全シリーズ名が出る）。1 行の出演話数は当該 (声優 × シリーズ × キャラ) の重複排除済みエピソード数で、シリーズ全体スコープのみのクレジットは話数「—」表示の 1 行として残す。順位はサブタイトル統計・エピソード尺統計（作品系の統計）にのみ Wimbledon 形式で用い、人物・企業/団体・声優に対しては用いない。

並べ替えの一貫方針として、五十音順以外のすべてのタブ（役職順・初参加順・担当話数が多い順・キャラクター順・初出演順・出演話数が多い順）では、「同じ話数内では初めてクレジットされた位置の順」を暗黙の副ソートキーとして効かせる。`CreditInvolvementIndex` が、クレジット階層を表示順（同一エピソード内の credit レコードを **明示順序カラム `credits.credit_seq` 昇順** → credit_id 昇順で並べ、各 credit 内を card_seq → tier_no → group_no → order_in_group → block_seq → entry_seq）でエピソード単位に走査する過程で、各関与に 0 始まりの出現連番 `Involvement.CreditSeq` を採番する。`credits` テーブルはクレジット階層の最上位で、下位階層（card_seq 等）と同じく **明示順序カラム `credit_seq`（smallint unsigned, 同一スコープ内 1 始まり, `UNIQUE(series_id,credit_seq)` / `UNIQUE(episode_id,credit_seq)`）** を持ち、OP より ED を先に流す回や OP/ED 以外のクレジットが同一スコープに増えた場合でも表示順を一意に表現できる。WinTools のクレジット編集画面にはクレジット一覧の ↑↓ 並べ替えボタンがあり、`CreditsRepository.BulkUpdateSeqAsync`（退避値経由のトランザクション再採番、下位階層の並べ替えと同方式）で即時 DB 反映する。新規クレジットの `InsertAsync` は同一スコープ内 `MAX(credit_seq)+1` を自動採番して末尾に追加する。集計側は (シリーズ放送開始日, シリーズ内話数) が同点になった行・役職を、この最小 `CreditSeq` の昇順で並べる。`roles.display_order` はマスタ管理画面（WinTools）のグリッド表示順を決めるだけの値であり、公開サイトの並べ替えには一切用いない。スタッフ一覧「役職順」タブの役職の並びも、`display_order` ではなく「その役職が最も早くクレジットされた (放送開始, 話数, クレジット出現位置)」で決定する。完全同点（同一話・同一クレジット位置で初出）の場合にのみ内部 `role_code` で安定化する（画面には現れない）。なお五十音順タブは読み仮名で並びが完全に一意に定まるため、この副キーは挟まない。スタッフ一覧の個人/団体絞り込みトグルは、エンティティ行を持たない「役職順」タブ（役職名＋人数の索引）がアクティブな間は無意味になるため非表示にし、五十音順・初参加順・参加話数が多い順タブでのみ表示する。 キャラクター一覧（`/characters/`）も同方針で、character_kind セクション内のキャラ配列を読み仮名順ではなく、各キャラが最も早くクレジットされた (放送開始, 話数, クレジット出現位置) の昇順に統一する（クレジットが無いキャラは末尾、完全同点時のみ読み仮名→名前で安定化）。

主題歌・劇伴スタッフ（`song_credits` / `song_recording_singers` / `bgm_cue_credits` 由来の作詞・作曲・編曲・歌唱）は曲・録音単位のマスタから `episode_theme_songs` 経由でエピソードに紐づくため、それ自体はクレジット階層上の物理位置を持たない。しかし当該エピソードのクレジット階層には `role_format_kind='THEME_SONG'` の役職ブロックが必ず置かれており（ブロックの entry は人物ではなく曲を指す）、主題歌スタッフはその主題歌ロールブロックの位置に現れるべきである。そこで `CreditInvolvementIndex` は階層走査時に THEME_SONG 形式の役職ブロックへ到達した時点の `CreditSeq` を「(エピソード, 親 credit の kind=OP/ED)」をキーに控え、`song_credits` / `song_recording_singers` 由来の関与へ `theme_kind`（OP/ED/INSERT）に応じてその位置を継承させる（OP 主題歌→OP クレジット内の主題歌ブロック位置、ED→ED、INSERT 等の親 kind 非対応は同エピソード最初の主題歌ブロック位置にフォールバック、主題歌ブロックが階層に無ければクレジット末尾相当）。劇伴（`bgm_cue_credits`、主題歌種別を持たない）は同エピソードの主題歌ブロック位置→末尾相当の順でフォールバックする。これにより主題歌・劇伴スタッフが初参加順・役職順・キャラクター順などのクレジット順ソートで「クレジットに出てくるとおりの位置」に正しく並ぶ。

初参加順・キャラクター順・初出演順の各タブは、シリーズごとのセクション（`<section>` ＋シリーズ名見出し、放送開始日昇順）に束ねる。シリーズ名・年は見出しに集約するため、これらのタブを含め**すべての表からシリーズ列を撤去**し、初参加シリーズの情報は初参加順タブのセクション見出しとしてのみ示す（初参加順以外の表に初参加情報は出さない）。スタッフ一覧の列構成は、五十音順＝区分／名前／読み／役職／参加話数、初参加順＝シリーズ別セクション＋区分／名前／読み／役職／参加話数、参加話数が多い順＝区分／名前／読み／役職／参加話数。役職詳細は五十音順／初参加順（シリーズ別セクション）／担当話数が多い順の 3 タブで、各行は区分／名前／読み／担当話数。声の出演は **キャラクター順を筆頭・既定タブ**とし（スタッフ一覧が役職順を筆頭にしているのに合わせる）、タブ順はキャラクター順（シリーズ別セクション）／五十音順／初出演順（シリーズ別セクション）／出演話数が多い順。各行は声優／読み／キャラクター／出演話数（キャラクター順セクション内はキャラクター列を先頭に出す）。キャラクター順はキャラクターを主たる単位とし、各シリーズセクション内で「キャラが最初にクレジットされた位置（そのキャラの全声優行のうち最小の (最早話数, クレジット階層位置)）」順にキャラを並べ、同一キャラを複数声優が演じる場合（交代・代役）はその声優行を当該キャラの位置に連続してまとめて表示する（キャラ読み五十音ではなくクレジット出現順）。

##### エピソード行レイアウト（一覧系の共通構造）

エピソードを一覧表示する画面は `episodes-index-section` 構造に統一されている。すなわち `<section class="episodes-index-section">` → `<h2 class="episodes-index-heading">` → `<ol class="episodes-index-list">` → `<li class="ep-row">` → `.ep-row-main`（`.ep-row-no-date`＝第N話＋放送日の縦積み、`.ep-row-title`、`.staff-badges-row`）の入れ子。対象は `/episodes/`・シリーズ詳細「エピソード一覧」・ホーム「今後の放送予定」「最新エピソード」。サブタイトルは全画面共通の `.ep-row-title`（CSS で `font-weight: 600`、太字）で、`title_rich_html`（ルビ付き HTML）があればそれを優先し、無ければ `title_text` のエスケープ平文を表示する。放送日は密表示用の「2024.2.4」形式（`JpDateFormat.DotDate`）。

シリーズ詳細「エピソード一覧」は単一シリーズなので `episodes-index-section` を 1 個出す。外側の `<section id="episode-list">` 枠・`<h2>エピソード一覧</h2>` 見出し・エピソード未登録時表示は保持し、内側のみ `episodes-index-section` で構成する。ただしシリーズ詳細ではすぐ上の基本情報に年度・放送期間・全話数が出ていて重複するため、シリーズ単位の見出し行 `<h2 class="episodes-index-heading">` はシリーズ詳細テンプレからは出さない。さらに枠線ボックスの体裁も `site.css` の `#episode-list .episodes-index-section`（および `#episode-list .episodes-index-list` の左右パディング 0）でシリーズ詳細スコープ限定に解除し、素のリストとして見せる（エピソード行間の点線 `.episodes-index-list li` の `border-bottom` は維持）。`#episode-list` はシリーズ詳細専用 id のため、`/episodes/`・人物詳細・企業詳細・ホームの `episodes-index-section`（見出し・枠あり）には影響しない。ホームの「今後の放送予定」「最新エピソード」は外側の `<section id="upcoming-episodes">`／`<section id="latest-episodes">` 枠と各 `<h2>` 見出しを保持し、その下にシリーズ単位の `episodes-index-section` を入れ子で並べる（表示範囲にシリーズ跨ぎがあれば複数並ぶ）。並び順は「今後の放送予定」＝放送日昇順、「最新エピソード」＝放送日降順で、セクション（シリーズ）の並びも各シリーズ内の最小（昇順時）／最大（降順時）放送日で同方向。ホーム内側の `episodes-index-section` には `id` を付けない（左サイドのセクションナビは `section[id]` を収集して項目化するため、各シリーズが重複表示されるのを防ぐ。アンカージャンプ用途もホームには無い）。ホームのエピソード staff サマリは `SeriesGenerator.GetEpisodeStaffSummaries()` の memoize 結果を参照するため、ビルドパイプラインは `SeriesGenerator` → `HomeGenerator` の順で実行する。

ホーム「今日の記念日」は閲覧日基準の JS 動的描画（`anniversaries.js`）で、ep-row 構造に準じた表示にする。1 話ずつ放送年代が異なるため `episodes-index-heading` は出さず、各 ep-row の上に「n年前　シリーズ (放送年度)」のシリーズ表記行を添える。記念日行はスタッフバッジ段を出さないため、記念日 JSON（`home-anniversary-data`）にはスタッフ HTML を載せず、シリーズ放送年度（`sy`）のみを持たせる。日付は他の一覧系と同じ「2024.2.4」形式、サブタイトルはルビなしの平文。

統計のエピソード単位ページ（パート尺の長短・アバンタイトル尺の長短・アバンタイトルスキップ回・中 CM 入り時刻の早遅・サブタイトル文字数/漢字率/記号率の多少）は、テーブルをやめた専用の `stats-ep-list` レイアウトで全ページ統一する。1 行は左ブロック（順位＋指標値を横並び・本文の約 1.77 倍サイズで `<li>` 内上下中央。指標値は数値のみ＝文字数 / `m:ss` / `h:mm:ss` / 百分率）と右ブロック（上段＝「シリーズ名（シリーズ詳細へのリンク） (年度)」、下段＝第N話のみ〔シリーズ詳細のエピソード一覧と同じ `.ep-row-no-date` の装い・放送日は出さない〕＋ルビ付きで改行を除去したサブタイトル〔エピソード詳細へのリンク〕）で構成する。同率順位が連続する場合は 2 件目以降の順位表示を空文字にする（指標値は常に表示。順位カラムは固定幅で全行整列）。1〜3 位の金銀銅装飾は持たない。アバンタイトルスキップ回は指標値を持たないが、パネルのデザイン・グリッドは他のエピソード単位ページと完全同一とし、左パネルに順位ではなく放映順の回次（1 始まりの連番）を入れ、指標値セルだけを出さない（パネル寸法・回次の表示位置は他ページの順位と同一）。ルビ付きサブタイトルの補完（集計クエリ結果はシリーズ slug と話数のみ保持）は、SQL を増やさず `BuildContext` の `LookupEpisodeBySeriesEpNo` でメモリ上のエピソードを引いて行い、行組み立て・同率ブランク化・改行除去は共有ビルダー `StatsEpisodeRows` に集約して `EpisodePartStatsGenerator` / `SubtitleStatsGenerator` 双方から再利用する。 使用文字 TOP100（全文字／漢字限定の 2 ページ）も同じ脱テーブル方針で、左は共通の `stats-ep-rank` アクセントパネル（順位＋出現回数）を再利用し、右に対象文字を正方形セル内で上下左右中央寄せした `<li>` 高さに迫る超特大（`stats-char-glyph`）で表示する（記号・かな・漢字の字幅差で左寄りに見えるのを防ぐ）。同率順位のブランク化は使用文字ページにも適用し、共有ビルダー `StatsCharRows` に集約する（SQL/Repository 不変。記号初出現順ページは構造が異なるため従来テーブルのまま）。 また「シリーズ別 TOP5 文字」と対になる「シリーズ別 TOP5 漢字」（漢字＋繰り返し記号「々」限定、各シリーズ最頻漢字 TOP5、DENSE_RANK の同点同順）を新設し、索引「シリーズ別」グループの TOP5 文字直下に配置する。集計は既存 `GetCharRankingKanjiAsync` と同一の漢字フィルタ（`REGEXP '\p{Han}|[々]'`）を `GetTopCharsBySeriesAsync` と同型 SQL に足した `GetTopKanjiBySeriesAsync` で行い、表示テンプレ・整形ロジックは TOP5 文字版と完全同一。 「記号出現回数・初使用エピソード」ページ（旧「記号出現回数」）も脱テーブル化：使用文字 TOP100 と同じ大きな記号グリフ＋出現回数のみのパネル（順位を持たないため `stats-ep-rank value-only` で順位カラムを詰める）に、右側へ初使用エピソード（シリーズと年度／下段は「放送日（左寄せ・括弧なし YYYY.M.D・エピソード一覧と同じ 0.88em muted）｜第N話（右寄せ）｜ルビ付きサブタイトル（左寄せ）」を固定幅 3 カラムで全行ぴったり整列）を並べる。並びは従来どおり初使用が早い順を保持（ランキングではないため順位は持たない）。ルビ生成は `StatsEpisodeRows.BuildTitleHtml` を公開化して再利用し、行組み立ては共有ビルダー `StatsSymbolRows` に集約（SQL/Repository・初使用順は不変）。

ホーム「今日の記念日」は**誕生日対応**である。埋め込み JSON `home-anniversary-data` は エピソード放送日に加えて、映画公開日・キャラクター誕生日・人物誕生日を 1 配列に種別タグ `k`（`ep` / `mv` / `cb` / `pb`）付きで内包する（プロパティ名は容量削減のため短縮形。`HomeGenerator.BuildCalendarDataJsonAsync`）。`anniversaries.js` は `k==='ep'` のみエピソードロジック（今日／今週）に流し、`cb` / `pb` のうち閲覧日と月日が一致するものを抽出して「今日の記念日」内でエピソード行より**上**に積む。キャラクター誕生日は対象を `character_kind` が `PRECURE` / `ALLY` のキャラに限定し、`characters.name` をシリーズ表記行とともに表示（プリキュアはキーカラーバッジを添える。バッジ文字色は地色の相対輝度から暗/明グレーを自動算出）。代表シリーズは「`series_precures` のうち放送開始が最も早いシリーズ」、代表 precure は「最小 `precure_id`」で決定的に解決する。人物誕生日は氏名リンクで、生年は `birth_year_visibility = 'PUBLIC'` かつ判明時のみ年齢を併記する。映画（`mv`）は今日の記念日には出さずカレンダー専用。

ホーム「今月のカレンダー」は、初期表示として閲覧した瞬間の当月 1 か月分を表示する JS 動的カレンダー（`calendar.js`、`home-anniversary-data` を `anniversaries.js` と共有）である。キャプションは「‹ 前月ボタン｜大きな月名（旧表示の約 2 倍・中央揃え）｜翌月ボタン ›」の一行構成で、前月／翌月ボタンで表示月を送り、`calendar.js` がその都度タイトルとグリッドを再描画する。1 月→前年 12 月・12 月→翌年 1 月の年跨ぎも処理する。表示データは月日ベース（記念日）なので、月送りは共有 JSON を月でフィルタし直すだけで完結し、追加データ取得・JSON・Generator の変更は伴わない。各日セルにその月日の項目をチップで縦に積み、優先順は**キャラクター誕生日 > 映画公開日 > 人物誕生日 > TV 放送**。プリキュア誕生日チップは変身前名義をキーカラーバッジで、`ALLY` は名称の素バッジで表示。色区分に加えてチップ先頭に種別絵文字を付す：TV 放送は 📺、映画は �、キャラクター／人物誕生日は 🎂。映画はシリーズ略称、TV 放送は「シリーズ略称＋#話数」で表示する（以前の「映」ピルは廃止し、上記絵文字で置き換え。カレンダー UI に限り `title_short` を用いるポリシー例外。前述）。本日セルのハイライトは実際の当月を表示しているときだけ付与し（他月を送った状態では付けない）、日曜・土曜は配色で区別。データ空・JS 無効環境では section ごと非表示。凡例を併記する。

##### シリーズ一覧 TV サブ行（プリキュア／スタッフ バッジ）

`/series/` の TV シリーズセクション（`series-tv-table`）は、各 TV シリーズのメイン行（番号／タイトル／放送期間／話数）の直下に複合サブ行（`tr.sub-row` の `td.sub-row-cell`、colspan）を出す。サブ行は見出しラベルを持たず、上から順に **プリキュアバッジ行**（`.precure-badges-row`）→ **スタッフバッジ行**（`.staff-badges-row`）の 2 段で構成する。いずれの段も該当データが 0 件ならその段を出さず、両方 0 件ならサブ行自体を出さない。サブ行の集計対象は TV シリーズのみで、映画・スピンオフ等のセクションはサブ行を持たない。

プリキュアバッジは当該シリーズに紐付くプリキュア（`series_precures`、`display_order` 昇順・同値時 `precure_id` 昇順）を 1 体 1 バッジで並べる。各バッジは `/precures/{precure_id}/` へのリンクで、表記は **プリキュア観点の名義連結**：`transform_alias_id`（変身後）→ `transform2_alias_id`（変身後 2）→ `pre_transform_alias_id`（変身前）の各 `character_aliases.name` を、この順で「 / 」連結する（NULL・未解決の名義は除外。すべて空のときのみ変身後名義へフォールバック）。`characters.name` は表記には用いない（参照用に保持のみ）。標準担当声優（`precures.voice_actor_person_id`）が登録されていれば連結名の後ろに「 (CV: ○○)」を付す（未登録なら名前のみ）。この名義連結ロジックは `/precures/{id}/` 詳細ページの h1 と共有する（共有ヘルパ `PrecureNaming.JoinAliasNames`）。

バッジの地色はプリキュアマスタの `key_color`（`char(7)`・`#RRGGBB`・NULL 可、フォーマットはCHECK 制約 `ck_precures_key_color` で担保）。文字色は地色を WCAG 2.x 定義の相対輝度（linearized sRGB の加重和 0.2126R + 0.7152G + 0.0722B）に変換し、しきい値 0.179 を境に暗グレー `#1a1a1a` ／明グレー `#f5f5f5` を自動で出し分けるため、地色が濃色でも淡色でも本文が読める。ボーダーは文字色側に寄せた半透明色（暗文字側 `rgba(0,0,0,.22)`／明文字側 `rgba(255,255,255,.30)`）で、地色がページ背景に近いときでも輪郭が保たれる。地色・文字色・ボーダーはビルド時に算出され、バッジ要素のインライン `style` として出力する。`key_color` 未設定または不正値のプリキュアはインライン色を持たず、`.precure-badge` の CSS 既定（中立の淡色バッジ）で描画される。地色・文字色の解決は `SeriesGenerator.ResolveBadgeColors` に集約し、バッジ整形は `BuildPrecureBadges` が `PrecureBadge` DTO のリストを `TvSeriesRow.PrecureBadges`に詰める。

##### TV シリーズの放送見込み（未完）表記

TV シリーズ（`kind_code='TV'`）で、登録済みエピソードレコード数が総話数マスタ値（`series.episodes`）に満たないシリーズは「放送見込み（未完）」とみなし、放送期間の終了日と総話数の直後に「（見込）」を添える。総話数マスタ値が未設定（`NULL`）のシリーズは比較不能なので見込み扱いにしない。放送中で終了日（`end_date`）が未設定の TV シリーズは放送期間を「2025年2月2日 〜」と「〜」止めで放送継続中を示す（`JpDateFormat.TvSeriesPeriod`）。映画・スピンオフ系は終了日 `NULL` でも開始日単独表記のまま（本表記は TV 文脈のみ）。終了日が未設定のときは「終了日」が無いので「（見込）」は付けず、総話数側にのみ付ける。シリーズ一覧テーブル（`series-tv-table` の `col-period` 22em／`col-episodes` 7em 固定列幅・`nowrap`・右揃え）で列がガタつかないよう、各セルの本体（「全 50 話」「〜 2026年1月25日」）を `.series-col-val`（`position: relative`）でくくり、「（見込）」は `.estimate-note` として `.series-col-val` の右辺へ絶対配置（`left: 100%`、本体幅に不算入）で添える。これにより本体右端は「（見込）」の有無に関わらず全行で一直線に揃う。シリーズ詳細の基本情報テーブルとエピソード一覧見出し・`/episodes/` 見出しは右揃え整列対象でないため、「（見込）」は通常フローの inline 注記として付与する。

##### シリーズ一覧の映画セクション

`/series/` の映画セクション（`series-movie-table`）は親映画（`kind_code ∈ {MOVIE, SPRING}`）を公開日昇順で並べ、TV シリーズ・スピンオフと同じ意匠の通し番号を振る。採番は CSS カウンタ `movie-no`（`tv-no` とは独立）で親映画行（`movie-row`）のみに `01.` 形式（ゼロ詰め 2 桁＋ピリオド、等幅・muted・太字）を打ち、子作品行には振らない。映画テーブルは TV／スピンオフと異なり 5 カラム構成（番号 `col-no`／シーズンバッジ `col-movie-badge`／タイトル `col-title`／公開日 `col-period`／尺 `col-episodes`）で、秋映画（`MOVIE`）／春映画（`SPRING`）のシーズンバッジ（`.movie-badge-fall` / `.movie-badge-spring`）をタイトルと別カラム（`col-movie-badge`、幅 4.5em・`nowrap`）に分離する。バッジを別カラム化することで、親映画のタイトル開始位置（`col-title` セル左端）が子作品の箇条書き（同じ `col-title` セル）と物理的に一致し、バッジ幅の差に依存しない整列になる。バッジとタイトルの間隔は `col-movie-badge` セルの `padding-right` で取る（バッジ要素自体は `margin-right` を持たない）。無印作品（バッジ無し）でも `col-movie-badge` セルは空で出し、タイトル列の左端は不変。親映画行は公開日（`col-period`）と、親＋全子（`MOVIE_SHORT`）の合計上映時間（`col-episodes`、いずれかが `run_time_seconds` NULL なら空）を出す。

親映画にぶら下がる子作品（`MOVIE_SHORT`、`seq_in_parent` 昇順）は行（`tr.movie-child-row`）を維持して尺カラムを親と揃えつつ、タイトルセル内を `<ul class="movie-child-list"><li>…</li></ul>` の箇条書きでマークアップする（中黒は `<li>` のリストマーカーで表現し、テキストとして「・」を持たない）。子の `col-title` セルは親映画の `col-title` セルと同じ列なので、セル左 padding を 0 にしてリストマーカーぶんの最小インデント（`movie-child-list` の `padding-left`）のみを持たせ、子タイトルの開始位置を親タイトルと揃える。子作品の種別ラベル（旧 `[秋映画(併映)]` 表記）は出さない。公開日は親と同一運用のため出さず（`col-period` 空）、子単体の上映時間（`RelatedSeriesRow.RuntimeLabel`、`run_time_seconds` NULL なら空）を親と同じ `col-episodes` 位置に表示してカラムを揃える。子作品はリンクを張らない。

#### C. エピソード詳細ページの構成（中核）

`/series/{slug}/{seriesEpNo}/` には次の情報を 1 ページに集約する:

1. **サブタイトル表示**: `title_rich_html`（ルビ付き HTML）があればそのまま流す。なければ `title_text` をプレーン表示。下に `title_kana` を補助表示。フォントは Episodes エディタとは合わせず、ブラウザ既定の和文フォントで素朴に表示（指示通り）。
2. **基本情報テーブル**: 放送日時・シリーズ内話数・通算話数・通算放送回・ニチアサ通算放送回、外部 URL（東映あらすじ／ラインナップ）、YouTube 予告埋め込み（`youtube_trailer_url` から ID を抽出して `<iframe>` 化）。
3. **フォーマット表**: `episode_parts` から OA / 配信 / 円盤の各バージョンの「累積開始時刻」と「尺」を併記する 22 パート種別対応の表。各媒体ごとに独立して累積タイムコードを計算し、当該媒体に該当パートが無い場合は空セル（—）扱いにして加算しない。フッタに媒体別の総尺を表示。
4. **サブタイトル文字情報**: Episodes エディタの `BuildTitleInformationPerCharAsync` ロジックを `TitleCharInfoRenderer` として移植したもの。サブタイトル中の登場順ユニーク文字ごとに、`EpisodesRepository.GetFirstUseOfCharAsync` で初出話を、`GetEpisodeUsageCountOfCharAsync` で総使用話数を、`GetTitleCharRevivalStatsAsync` で 1 年以上ぶりの復活情報を取得し、「`「文字」… [初出] [唯一] N年Mか月(P話)ぶりQ回目 『シリーズ』第N話「サブタイトル」(YYYY.M.D)以来`」形式の HTML を生成する。badge は CSS で色分け（初出 = 黄、唯一 = ピンク）。
5. **サブタイトル文字統計**: `episodes.title_char_stats` JSON の `length`（書記素数・コードポイント数・ユニーク書記素数・空白数）と `categories`（漢字 / ひらがな / カタカナ / 英字 / 数字 / 記号 / 句読点 / 絵文字 / その他）をテーブル化。`System.Text.Json` でパースし、JSON が NULL / 異常値のときは黙ってフォールバックする。
6. **パート尺偏差値**: `EpisodePartsRepository.GetPartLengthStatsAsync` を直接呼び出し、AVANT / PART_A / PART_B のシリーズ内および全シリーズ横断（歴代）の順位・偏差値を表示。Episodes エディタと同じ計算ロジック（MySQL のウィンドウ関数 `RANK / AVG / STDDEV_POP`）を使うため値は完全一致する。
7. **主題歌**: `episode_theme_songs` から OP / ED / 挿入歌（最大 OP 1 + ED 1 + INSERT 複数）を取り出し、`song_recordings` → `songs` を JOIN で引いて表示。`is_broadcast_only=1` 行は「（本放送のみ）」マーカー付きで併記する。
8. **クレジット階層**: `credits.scope_kind = 'EPISODE'` のクレジット（OP / ED）について、`Card → Tier → Group → Role → Block → Entry` の階層を構造保持で HTML 化する。Entry は 5 種別（PERSON / CHARACTER_VOICE / COMPANY / LOGO / TEXT）に対応:
 - `PERSON`: `person_aliases.display_text_override` 優先、なければ `name` を表示
 - `CHARACTER_VOICE`: 「キャラ名義（CV: 声優名義）」形式
 - `COMPANY`: 屋号
 - `LOGO`: `[屋号#CIバージョン]` のテキスト表現（ロゴ画像化は将来課題）
 - `TEXT`: フリーテキスト退避口
 - `affiliation_company_alias_id` / `affiliation_text` は小カッコ所属表記
 - `is_broadcast_only=1` のエントリは「（本放送のみ）」マーカー付き
 - `credit_role_blocks.col_count` は CSS Grid `grid-template-columns: repeat(N, ...)` でカラム数として反映
9. **前後話ナビ**: 同シリーズ内の前後話（series_ep_no ベース）へのリンク。

#### D. テンプレートとスタイル

- テンプレートエンジン: **Scriban**（`Templates/*.sbn`）
- 共通レイアウト: `_layout.sbn`（ヘッダ・フッタ・パンくず・canonical タグ・OGP メタ等を吸収）
- レンダリングは 2 段階: 各 Generator が「コンテンツテンプレ」を model でレンダリング → 結果 HTML を `LayoutModel.Content` に詰めて `_layout.sbn` を再レンダリング、というシンプルな流れ。Razor の `@RenderBody()` 相当の機構が Scriban に無いための便宜。
- スタイル: `wwwroot/assets/site.css` 1 ファイル。CSS フレームワーク不採用、最低限の素朴スタイル。CSS 変数で色を管理し、サブタイトルだけはアクセントボーダー付きのカードで強調表示。
- 静的アセットは `wwwroot/` 配下を出力ルートに丸ごとコピーする方式（`SiteBuilderPipeline.CopyStaticAssets`）。

#### E. ログ・サマリ

ビルドの最後にコンソールへサマリが出力される。

```
== Build Summary ==
 Pages generated : 1234
 home : 1
 about : 1
 series : 31
 episodes : 1201
 Warnings : 5
 Elapsed : 12.3 sec
```

警告は `BuildLogger.Warn` 経由で集計される。代表的な警告:
- `title_char_stats が未生成` のエピソードが見つかった（`PrecureDataStars.TitleCharStatsJson` で再計算を促す）
- 役職マスタに無い `role_code` がクレジットで参照されている

---

## データベーススキーマ

##### 誕生日データモデル（persons / characters）

人物（`persons`）とキャラクター（`characters`）は誕生日を 4 カラムで保持する。生年は「不明」「判明・公開」「判明・非公開（本人スタンス尊重）」の 3 状態を表す必要があるため、`DATE` 1 本ではなく月日正規化＋生年＋公開可否で持つ（部分日付モデル。`precures` の月日正規化流儀と一貫し、年が欠けても破綻しない）。

- `birth_year`（`smallint unsigned` NULL）… 判明していれば西暦。不明は `NULL`。`birth_year_visibility` が `PRIVATE` でも値の保持自体は可。
- `birth_year_visibility`（`varchar(16)` NOT NULL 既定 `PUBLIC`）… `PUBLIC`＝サイト生成に生年・年齢を出す／`PRIVATE`＝本人スタンス尊重で生年・年齢を生成に出さない。アプリ意味論の閉じた 2 値のためマスタ化せず CHECK 制約で固定。
- `birth_month`（`tinyint unsigned` NULL、1–12）／ `birth_day`（`tinyint unsigned` NULL、1–31）… 誕生月日。`birth_year_visibility` に関わらず常にカレンダー／記念日の判定単位（月日一致）。`birth_day` があるなら `birth_month` 必須（CHECK 制約 `ck_*_birth_day_needs_month`）。

記念日・カレンダーは「閲覧日の月日」と各エンティティの `birth_month` / `birth_day` の一致だけを見る。生年・年齢の表示は `birth_year_visibility = 'PUBLIC'` かつ `birth_year` 非 NULL のときのみ。プリキュアの誕生日はプリキュア本体（`precures`）ではなく、対応キャラクター（`characters`、`transform_alias_id → character_aliases.character_id` 経由で解決）側で保持する（`precures` は誕生日カラムを持たない）。ホームの「今月のカレンダー」「誕生日対応の今日の記念日」はこの誕生日データモデルを参照する（次章参照）。Catalog の人物（人物タブ）／キャラクター（キャラクタータブ）編集には誕生日入力欄（生年 NumericUpDown ＋「不明」チェック・公開可否コンボ・月／日コンボ）があり、生年「不明」チェック時は NULL、月／日は先頭「(未)」が NULL となる。コンパクトなカレンダー表示に限り `series.title_short` を用いる方針例外を設けている（本書「シリーズ系テーブル」の `title_short` 行に明記）。

DDL ファイル: [`db/schema.sql`](db/schema.sql)（新規構築用、全テーブル含む）
マイグレーション:
- [`db/migrations/`](db/migrations/) … バージョン別の差分 SQL 群。新規構築では不要（`db/schema.sql` が常に最新スキーマ）。
 既存環境の更新時に順次適用する。各スクリプトは冪等。どのバージョンで何が変わったかは [`CHANGELOG.md`](CHANGELOG.md) を参照。
 ファイル名は **`v<VERSION>_migration_<topic>.sql`** 形式（`VERSION` は `Directory.Build.props` のリリースバージョン、`topic` は英小文字スネークケース）。適用はバージョン昇順。各バージョンで追加・撤去された差分 SQL の内訳は [`CHANGELOG.md`](CHANGELOG.md) を参照。同一バージョン内はファイル名昇順で順次適用する（誕生日カラムの追加→撤去のように順序に意味がある場合は CHANGELOG 側に適用順を明記している）。

### ER 概要

```
series_kinds ──┐
 ├── series ──┬── episodes ──── episode_parts
series_relation_kinds ──┘ │ │
 ├── (self-ref) part_types
 │
 └── discs ── tracks ──┬── song_recordings ── songs
 ▲ │ │
 │ │ └── bgm_cues ── bgm_sessions
 │ │ (M 番号) (録音セッション)
 │ │
 │ └── video_chapters (BD/DVD チャプター)
 │
 products
 (販売単位メタ情報、series_id は持たない)

 付随マスタ: product_kinds / disc_kinds / track_content_kinds /
 song_music_classes / song_size_variants / song_part_variants
```

> **シリーズの所在**: シリーズ所属は `products` ではなく `discs` 側の属性として持つ（上図の `discs ──▲── series` の FK）。1 商品内に複数シリーズのディスクが混在するケースや、1 シリーズに 1 ディスクだけが対応するケースの双方を自然に表現できる構造とするため。

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
| `title_short` | VARCHAR(128) NULL | 略称（例: 「キミプリ」）。**SiteBuilder の生成出力（Web 表示）では原則として本列を一切参照しない**。DB マスタ上は引き続き保持し、Catalog 側の編集 UI（コンボの省スペース表示など）で使用する。**唯一の例外**：ホーム「今月のカレンダー」の日セル内チップ（エピソード＝シリーズ略称＋話数、映画＝シリーズ略称）に限り、1 マスの限られた幅に収めるため `title_short` を用いる（未設定時は `title` にフォールバック）。これはコンパクトなカレンダー UI のための明示的なポリシー例外であり、他の生成出力（一覧・詳細・記念日行・構造化データ等）では従来どおり `title` のみを用いる |
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

※ 細分条件の判定は LegacyImport による自動投入時にのみ適用される。Catalog GUI 上で手動編集する場合は任意のコードを選択可能。細分判定で参照される「所属シリーズ」は、グループ代表ディスクの `series_id` を用いる。

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

**初期データ**: `OP`（オープニング主題歌）, `ED`（エンディング主題歌）, `INSERT`（挿入歌）, `CHARA`（キャラクターソング）, `IMAGE`（イメージソング）, `MOVIE_OP`（映画オープニング主題歌）, `MOVIE_ED`（映画エンディング主題歌）, `MOVIE_INSERT`（映画挿入歌）, `OTHER`（その他）。

> 映画主題歌は `MOVIE_OP` / `MOVIE_ED` / `MOVIE_INSERT` の 3 種に分けて持つ（OP/ED/挿入歌の区別を表現するため）。`display_order` の付番は `OP=1 / ED=2 / INSERT=3 / CHARA=4 / IMAGE=5 / MOVIE_OP=6 / MOVIE_ED=7 / MOVIE_INSERT=8 / OTHER=99`（`UNIQUE` 制約を満たす連番）。

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

クレジット側と分離した理由：商品の流通元（レーベル名や販売元名）は「そのレコードのジャケット・帯にどう書かれていたか」のスナップショット名義であり、クレジット集計に混ぜると「同じ社名なのに屋号変更や CI 改訂で別エンティティとして数える」といった意味的にズレた合算が起きやすい。商品メタ専用の小さなマスタとして独立させることで、表示・JSON-LD 出力は構造化 ID 経由で安定させつつ、クレジット側の集計は純粋に「作品制作・配給に関与した企業」だけで保てる。

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

**既定フラグの排他性**: 同フラグが立つ行はマスタ全体で最大 1 行という制約は DB レベルではなく**アプリ側（`ProductCompaniesRepository.InsertAsync` / `UpdateAsync` 内のトランザクション）で担保**する。フラグを ON で保存しようとすると、他の全行の同フラグを 0 に落としてから対象行を 1 にセットする処理がトランザクション内で実行される。GUI でチェック ON にすれば自動的に他社のフラグが落ちるので、運用者が意識する必要はない。

**運用 UI**: メインメニュー「商品社名マスタ管理...」から CRUD。商品エディタ（「商品・ディスク管理」）の流通系行から「選択...」ボタンで picker（`ProductCompanyPickerDialog`）が呼ばれて紐付け先 ID をセットする。`NewProductDialog`（CDAnalyzer / BDAnalyzer の新規商品作成ダイアログ）は起動時に既定フラグ社を `GetDefaultLabelAsync` / `GetDefaultDistributorAsync` で取得し、ReadOnly TextBox に表示する（個別商品ごとの差し替えは作成後に商品エディタで行う）。

#### `products` — 商品

販売単位としての商品。価格・発売日・販売元などの「商品メタ情報」を管理する。複数枚組の場合も 1 商品として扱い、ディスクは `discs` 側で品番単位に分割される。

> **シリーズ列を持たない**: 本テーブルは `series_id` 列を持たない。シリーズ所属は各 `discs` 行の `series_id` で判断する。これは「シリーズごとに 1 枚だけディスクがある」構造、および「シリーズ合同盤でディスクごとに異なるシリーズが紐付く」構造の双方に自然対応するため。

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
| `apple_album_id` | VARCHAR(32) NULL | Apple Music Album ID |
| `spotify_album_id` | VARCHAR(32) NULL | Spotify Album ID |
| `cover_image_url` | VARCHAR(512) NULL | ジャケット画像 URL（提供元 CDN を直接参照するホットリンク。実体は保存しない） |
| `cover_image_source` | VARCHAR(16) NULL | 画像の取得元コード。`amazon_cd`（PA-API・CD ASIN 由来）／`amazon_digital`（PA-API・デジタル ASIN 由来）／`apple`（iTunes Lookup 由来）の 3 値 |
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

1 枚のディスクを表す。主キーは **品番** (`catalog_no`)。複数枚組の場合は各ディスクが別品番を持ち、同じ `product_catalog_no`（代表品番）に紐付く。

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
| `total_tracks` | TINYINT UNSIGNED NULL | 総トラック数（**CD-DA 専用**。BD/DVD では NULL） |
| `total_length_frames` | INT UNSIGNED NULL | 総尺（**CD-DA 専用**、1 フレーム = 1/75 秒。BD/DVD では NULL） |
| `total_length_ms` | BIGINT UNSIGNED NULL | 総尺（**BD/DVD 専用**、ミリ秒精度） |
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
- `fk_discs_series`: `series_id → series.series_id`（ON DELETE SET NULL、ON UPDATE CASCADE）
- `fk_discs_kind`: `disc_kind_code → disc_kinds.kind_code`

**インデックス**:
- `ix_discs_product (product_catalog_no)`
- `ix_discs_series (series_id)`
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

**CHECK 制約 / トリガー（排他参照・sub_order ルールの整合性）**: INSERT/UPDATE 時に `trg_tracks_bi_fk_consistency` / `trg_tracks_bu_fk_consistency` トリガーが content_kind 一貫性と sub_order ルールを検証する。 `trg_tracks_bi_fk_consistency`（BEFORE INSERT）の content_kind 一貫性チェックには、同一 PK `(catalog_no, track_no, sub_order)` の行が既に存在する場合は SIGNAL をスキップするガードがある。これは `INSERT ... ON DUPLICATE KEY UPDATE` で BEFORE INSERT が先に発火する際、INSERT VALUES 側の暫定 `content_kind_code`（CDAnalyzer / BDAnalyzer の物理情報 UPSERT では `'OTHER'`）が、同一 `(catalog_no, track_no)` のメドレー分割子行（`sub_order>0`、例: `'BGM'`）と誤って不一致判定され、既存ディスクへの物理情報同期が不当に弾かれるのを防ぐためである。実質 UPDATE となるこのケースでは `content_kind_code` は UPDATE 句で書き換えられず、整合性の最終判定は後続の `trg_tracks_bu_fk_consistency`（BEFORE UPDATE）が保全後の確定値で行うため制約は緩まない。真に新規 PK を別 `content_kind_code` で挿入するケースは従来通り検出される。

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

> 音楽種別 `music_class_code` は `song_recordings` 側で保持する設計です。同一曲のカバーやアレンジが「主題歌→キャラソン」のように文脈で種別を変えるケース（カバー版収録時に種別が変わる等）を表現するため、種別を録音単位で管理しています。

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
| `is_temp_m_no` | TINYINT NOT NULL DEFAULT 0 | **仮 M 番号フラグ**。`m_no_detail` が `_temp_034108` のような内部管理用のダミー番号であることを示す。1 のとき閲覧 UI では `m_no_detail` を非表示にし、マスタメンテ画面ではチェックボックスとして可視化する |
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

CDAnalyzer / BDAnalyzer の DB 連携では、メディアごとに専用の照合メソッドが用意されている:

**CD-DA（`DiscRegistrationService.FindCandidatesForCdAsync`）**: 以下の優先順で検索し、最上位のキーでヒットした時点で以降の検索は行わない。複数枚組（BOX）は全ディスクが同一 MCN（商品バーコード）を共有し Disc 2 を Disc 1 に誤マッチさせる危険があるため、CD-DA 照合では **MCN を照合キーに用いない**（引数は互換のため受け取るが不使用）。

1. **CDDB Disc ID 完全一致**（`discs.cddb_disc_id`）
2. **TOC 曖昧一致**: `total_tracks` 完全一致 AND `total_length_frames` ±75 フレーム（≒ ±1 秒）

**BD/DVD（`DiscRegistrationService.FindCandidatesForVideoAsync`）**: MCN / CDDB は取れないため、TOC 曖昧のみ。

- `num_chapters` 完全一致 AND `total_length_ms` ±1000 ms（≒ ±1 秒）

CD と BD/DVD で照合メソッドを分離しているのは、単一メソッドで兼用すると BD/DVD の尺を CD-DA の 1/75 秒フレームに換算する必要があり、意味論の混乱と尺精度の劣化（約 13ms 単位への丸め）を招くため。

### シリーズ紐付けの運用

ディスクのシリーズ所属は以下の経路で設定できる:

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

各バージョンの詳細な変更履歴は [`CHANGELOG.md`](CHANGELOG.md)。工程単位の詳細は Git コミット履歴／GitHub リリースノートを参照。

## ライセンス

[MIT License](LICENSE) © 2025 Shota (SHOWTIME)