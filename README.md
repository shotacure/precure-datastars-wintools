# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）**、および **クレジット情報（OP / ED の階層構造、人物・企業・キャラクター・プリキュアの各マスタ）** を MySQL データベースで管理するためのアプリケーション集です。**v1.3.0 で Web 公開用の静的サイトジェネレータ `PrecureDataStars.SiteBuilder` を新設**し、ローカル MySQL の内容をそのまま静的 HTML として書き出せるようになりました。

> **v1.3.0** — Web 公開用の静的サイトジェネレータ **`PrecureDataStars.SiteBuilder`** を新設しました。ローカル MySQL を読み出して、シリーズ・エピソード・人物・企業・プリキュア・キャラクター・楽曲・商品・劇伴の各軸ページと、サブタイトル統計・尺統計・役職別ランキング・声優ランキングなどの統計ページを `out/site/` 以下に静的 HTML として書き出すコンソールアプリです。サイト内検索（クライアント側 JS）、SEO 関連（sitemap.xml / robots.txt / OGP / JSON-LD）、Google Analytics 4 / Search Console / AdSense の任意連携も実装。AWS S3 への同期は本ツール範囲外（手動 `aws s3 sync` を別途想定）。クレジット階層は Catalog 側プレビューと同等の表現で出力（役職テンプレ DSL 展開、フォールバック表、絵コンテ・演出融合表示、「協力」末尾追記、leading_company 字下げ等）。あわせて `episodes.duration_minutes` 列追加・主題歌 5 役職の seed・`bgm_cues.seq_in_session` 追加・`episode_uses` テーブル新設など、Web 公開を支える DB マイグレーションを 6 本投入。役職テンプレ DSL 展開エンジン本体は Catalog / SiteBuilder で重複していた 5 ファイルを共通プロジェクト **`PrecureDataStars.TemplateRendering`** に集約し、`ILookupCache` インターフェース注入で各 `LookupCache` 実装を切り替える設計に統一。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.4** — プリキュア本体マスタ `precures` テーブルを新設、キャラクター続柄／家族関係を汎用構造化（`character_relation_kinds` / `character_family_relations`）、声優キャスティングテーブルを撤去（業務ルール「ノンクレ除いてクレジットされている＝キャスティング」に基づき `credit_block_entries` の `CHARACTER_VOICE` エントリに一元化）。`name_en` 列を 4 表に追加して英文クレジット出力の対称性確保。`CreditMastersEditorForm` を **15 タブ**に再編し、肌色ピッカー UserControl を新設。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.3** — 歌・劇伴のクレジット情報を構造化（連名・ユニット名義・キャラ(CV) を中間表で表現）。`person_aliases.display_text_override` 列追加でユニット名義の長い表示文字列に対応。既存フリーテキスト列は温存し段階的移行する設計。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.2** — クレジット一括入力フォーマットを完全可逆化（LOGO `[屋号#CIバージョン]` / 本放送限定 🎬 / A/B 併記 `&` / 行末備考 `// 備考` / `@notes=` / `@cols=N` を構文サポート）。Draft 階層を一括入力フォーマットに逆翻訳する `CreditBulkInputEncoder` を新設し、ツリー右クリック「📝 一括入力で編集...」を全レベル対応に。Card/Tier/Group/Role の備考編集 UI を `NodePropertiesEditorPanel` として新設。DB スキーマ変更なし。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.1** — クレジット一括入力ダイアログ追加（複数行テキスト + リアルタイムプレビューでクレジットを一括投入）、人物・企業・キャラの名寄せ機能、プレビュー改良。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.0** — クレジット管理基盤を新規追加。シリーズまたはエピソードの OP/ED クレジットを構造化して保持できる新スキーマ（人物・人物名義・共同名義中間表・企業・屋号・ロゴ・キャラクター・キャラクター名義・役職・クレジット本体 4 段階：credits / credit_cards / credit_card_roles / credit_role_blocks / credit_block_entries・エピソード主題歌）を追加。`PrecureDataStars.Catalog` メニューに「クレジット系マスタ管理」と「クレジット編集画面」を新設、編集を全面メモリ化（Draft セッション方式）+ 常時 HTML プレビュー、役職テンプレ DSL `role_templates` 統合テーブルなど。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.1.5** — CDAnalyzer / BDAnalyzer 同時起動時のドライブ占有競合を解消（CDAnalyzer のメディア種別自動判定）、Blu-ray プレイリスト全走査モード追加、新規商品登録ダイアログの税込価格自動算出。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.1.4** — 商品・ディスク管理画面の挙動改善とレイアウト刷新、マスタ管理画面の改善。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.1.3** — データ入力 UI を大幅刷新（商品とディスクを 1 画面に統合した「商品・ディスク管理」、トラック編集を独立させた「トラック管理」、税込価格自動算出、歌・劇伴の CSV 取り込み、劇伴の仮 M 番号フラグ）。詳細は末尾の [変更履歴](#変更履歴) を参照。

---

## ソリューション構成

```
precure-datastars-wintools.sln
│
├── PrecureDataStars.Data                    … データアクセス層（共通ライブラリ）
├── PrecureDataStars.Data.TitleCharStatsJson … 文字統計ビルダー（共通ライブラリ）
├── PrecureDataStars.Catalog.Common          … カタログ GUI 共通（Dialog/Service/CSV Import）
├── PrecureDataStars.TemplateRendering       … 役職テンプレ DSL 展開エンジン（共通ライブラリ、v1.3.0 新設）
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
├── PrecureDataStars.SiteBuilder             … Web 公開用静的サイト生成（コンソール、v1.3.0 新設）
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
    │   ├── v1.2.0_add_credits.sql           … v1.1.5 → v1.2.0 差分用（クレジット管理基盤の追加）
    │   ├── v1.2.0_h10_credit_kinds_and_role_templates.sql … v1.2.0 内部工程 H-10（credit_kinds マスタ化＋role_templates 統合）
    │   ├── v1.2.1_drop_character_aliases_valid_dates.sql … v1.2.0 → v1.2.1（character_aliases から valid_from/to を撤去）
    │   ├── v1.2.1_seed_storyboard_and_director_roles.sql … v1.2.1（roles マスタの初期投入）
    │   ├── v1.2.1_series_hide_storyboard_role.sql … v1.2.1（絵コンテ非表示フラグの series 列追加）
    │   ├── v1.2.3_add_music_credits.sql     … v1.2.2 → v1.2.3（音楽系クレジット構造化：4 表追加 + display_text_override 列追加）
    │   ├── v1.2.4_add_precures_and_family.sql … v1.2.3 → v1.2.4（プリキュア本体マスタ＋続柄マスタ＋家族関係を追加、character_voice_castings を撤去）
    │   ├── v1.2.4_add_name_en_columns.sql    … v1.2.4 内追加（person_aliases / company_aliases / characters / character_aliases の 4 表に name_en 列を追加、対称性確保）
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
| **PrecureDataStars.TemplateRendering** | クラスライブラリ | 役職テンプレ DSL（v1.2.0 工程 H で導入）の展開エンジン共通プロジェクト（v1.3.0 で抽出）。Catalog 側プレビュー（`CreditPreviewRenderer`）と SiteBuilder 側 HTML 生成（`CreditTreeRenderer`）の双方から参照される。`TemplateContext` / `TemplateNode` / `TemplateParser` / `RoleTemplateRenderer` / `Handlers/ThemeSongsHandler` と、`LookupCache` 抽象化のための `ILookupCache` インターフェースを保持。`net9.0`（Forms 非依存）で構成し、コンソール側の SiteBuilder からも安全に参照可能。Catalog 側 / SiteBuilder 側の同名ディレクトリで重複していた 5 ファイルを集約し、各プロジェクトが持つ独自の `LookupCache` 実装を `ILookupCache` インターフェース注入で切り替える設計とした。 |
| **PrecureDataStars.Episodes** | WinForms GUI | メインのエピソード管理ツール。シリーズ・エピソードの CRUD、MeCab によるかな/ルビ自動生成、パート構成の DnD 編集、URL 自動提案、文字統計表示、偏差値ランキング等。 |
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理 GUI。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、ディスク総尺・トラック尺ともに M:SS.fff で表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）と、6 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類・**クレジット系マスタ**）をメニューから切り替えて使う。クレジット系マスタは v1.2.0 で新設、v1.2.4 でタブ構成を再編して **15 タブ構成**となった `CreditMastersEditorForm`：プリキュア（v1.2.4 新設・先頭）／人物／人物名義／企業／企業屋号／ロゴ／キャラクター／キャラクター名義／キャラクター続柄（v1.2.4 新設）／家族関係（v1.2.4 新設）／役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別。声優キャスティングタブは v1.2.4 で撤去（業務ルール「ノンクレ除いてクレジットされている＝キャスティング」に基づき、`credit_block_entries` の `CHARACTER_VOICE` エントリに一元化）。v1.3.0 ブラッシュアップ stage 16 で **音楽クレジット名寄せ移行フォーム `MusicCreditsMigrationForm`** が本格運用化され、未マッチング名義一覧 → `UnmatchedAliasRegisterDialog` での人物・名義登録 → 全シリーズ全列での構造化テーブル INSERT までをワンストップで実行できるようになった（`SongCreditsRepository` / `SongRecordingSingersRepository` / `BgmCueCreditsRepository` を経由した完全一致 + SP 正規化マッチング、既移行レコード自動除外）。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.LegacyImport** | コンソール | 旧 SQL Server 版の discs / tracks / songs / musics テーブルから、新 MySQL 版の products / discs / tracks / songs / song_recordings / bgm_cues / bgm_sessions へ移行するバッチ。`--dry-run` オプションで件数サマリーだけの試行運転が可能。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。DVD は VIDEO_TS.IFO を指定するとフォルダ全走査で多話収録 DVD にも対応する（v1.1.1）。Blu-ray も v1.1.5 から `BDMV/PLAYLIST` 配下指定時はフォルダ全走査モードに切り替わり、ディスク内の有意なプレイリストを並列抽出する（既定 60 秒未満の短尺ダミーと重複プレイリストは自動除外）。DB 連携パネルで既存ディスクとの照合・新規商品登録が可能。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行できる。v1.1.5 以降、メディア挿入時に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系プロファイル以外（DVD / BD / HD DVD）であれば後続の SCSI コマンドを発行せず即座にデバイスハンドルをクローズする（BDAnalyzer との同時起動時にドライブ占有競合を起こさないため）。 |
| **PrecureDataStars.SiteBuilder** | コンソール | Web 公開用の静的サイト生成ツール（v1.3.0 新設）。ローカル MySQL の内容を読み出し、シリーズ・エピソードを中心とした静的 HTML 一式を `out/site/` 以下に書き出す。テンプレートエンジンは Scriban、共通レイアウト＋コンテンツの 2 段レンダリング。エピソード詳細ページにはフォーマット表（OA / 配信 / 円盤の累積タイムコード）、サブタイトル文字情報（初出・唯一・「N年Mか月ぶり」）、文字統計、パート尺偏差値、主題歌、クレジット階層（Card → Tier → Group → Role → Block → Entry）までを 1 ページに集約する。さらに `/persons/{personId}/` `/companies/{companyId}/` `/precures/{precureId}/` `/characters/{characterId}/` の人物・企業・プリキュア・キャラクター軸ページも提供し、起動時に 1 回だけ構築する `CreditInvolvementIndex` 経由で「人物・企業・キャラごとにどのシリーズのどのエピソードに、どの役職で関与したか／いつ誰の声で演じられたか」を逆引き表示する。AWS 連携は本ツール範囲外（手動 `aws s3 sync` を別途想定）。 |

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

`db/schema.sql` によりデータベース `precure_datastars` と全テーブル（エピソード系 6 本 + 音楽・映像カタログ系 14 本 + **クレジット管理系**：人物・企業・キャラクター・役職・クレジット本体／カード／ティア／グループ／役職／ブロック／エントリ・エピソード主題歌・credit_kinds・role_templates・person_alias_members・song_credits・song_recording_singers・bgm_cue_credits **+ v1.2.4 で追加した precures / character_relation_kinds / character_family_relations の 3 表**）が作成されます。スキーマは v1.2.4 時点の最新状態（`discs.series_id` を持ち、`products.series_id` は無い。`songs` の作詞／作曲列は `lyricist_name` / `composer_name` 等の素の命名。`bgm_cues` には仮 M 番号フラグ `is_temp_m_no` がある。`series_kinds` には `credit_attach_to`、`part_types` には `default_credit_kind` の宣言列が追加されており、`character_voice_castings` テーブルは含まれず（v1.2.4 で撤去）、その代わり `precures`（プリキュア本体マスタ、変身前後 4 名義 + 誕生日 + 声優 + 肌色 HSL/RGB + 学校情報）、`character_relation_kinds`（続柄マスタ）、`character_family_relations`（家族関係、汎用）が含まれる）。

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

# v1.1.5 → v1.2.0：クレジット管理基盤の追加（人物・企業・キャラクター・役職・クレジット本体・主題歌）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.0_add_credits.sql

# v1.2.0 → v1.2.1：character_aliases から valid_from / valid_to を撤去
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.1_drop_character_aliases_valid_dates.sql

# v1.2.1：series テーブルに「絵コンテを明示しないシリーズ」フラグ列を追加
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.1_series_hide_storyboard_role.sql

# v1.2.1：roles マスタに STORYBOARD（絵コンテ）と EPISODE_DIRECTOR（演出）をシード
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.1_seed_storyboard_and_director_roles.sql

# v1.2.2 → v1.2.3：音楽系クレジット構造化（person_aliases.display_text_override 列追加 +
#                  person_alias_members / song_credits / song_recording_singers / bgm_cue_credits 4 表追加）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.3_add_music_credits.sql

# v1.2.3 → v1.2.4：プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）を追加し、
#                 未使用の character_voice_castings テーブルを撤去
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.4_add_precures_and_family.sql

# v1.2.4 内追加：person_aliases / company_aliases / characters / character_aliases の 4 表に
#               name_en 列（VARCHAR(128) NULL）を追加（対称性確保、英文クレジット出力対応）
mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.4_add_name_en_columns.sql
```

> ⚠️ **v1.2.3 マイグレーション中の部分適用について**: 初回リリース時の本マイグレーション SQL では
> 2 つの問題で `CREATE TABLE` が止まる現象が報告された。修正版で解消済み:
>
> 1. `person_alias_members` の自己参照禁止を CHECK 制約 `ck_pam_no_self` で記述していたが、
>    MySQL 8.0.16+ の「FK 参照アクション列を CHECK で参照不可」(Error 3823) により失敗 →
>    ネスト禁止トリガーに統合済み。
> 2. `person_alias_members` / `song_recording_singers` の一部 FK が `ON UPDATE CASCADE` を
>    指定しており、CHECK 制約 (`ck_pam_kind_columns` / `ck_srs_kind_columns`) が同列を
>    参照する関係で同じ Error 3823 が発生 → これら 7 件の FK の `ON UPDATE` を
>    `NO ACTION` に下げて両立可能にした（参照先列はいずれも AUTO_INCREMENT 代理キーで
>    実運用で値が更新されないため挙動への影響なし。`ON DELETE RESTRICT` は維持）。
>
> 既に `display_text_override` 列追加までは適用されている DB に対しても、修正版の本マイグレーションは
> 冪等（`ALTER TABLE` の存在チェックと `CREATE TABLE IF NOT EXISTS` の組み合わせ）のため
> そのまま再実行で続きから流せる。

**v1.2.4 マイグレーションで実施される変更**:

*`v1.2.4_add_precures_and_family.sql`:*

1. `character_voice_castings` テーブルを撤去（無条件 DROP）。事前に件数を NOTE 表示するが、業務側で 0 件を確認済みのため挙動は単純に DROP。代わりに `credit_block_entries`（`CHARACTER_VOICE` エントリ）の登場行を「ノンクレ除いてクレジットされている＝キャスティング」として扱う設計に変更（実体テーブルとしての `character_voice_castings` は不要）。
2. `character_relation_kinds` テーブルを新設（キャラクター続柄マスタ）。初期データとして `FATHER` / `MOTHER` / `BROTHER_OLDER` / `BROTHER_YOUNGER` / `SISTER_OLDER` / `SISTER_YOUNGER` / `GRANDFATHER` / `GRANDMOTHER` / `UNCLE` / `AUNT` / `COUSIN` / `PET` / `OTHER_FAMILY` の 13 種を `INSERT IGNORE` で投入。`display_order` は 10 単位飛び番。
3. `character_family_relations` テーブルを新設。`characters` 同士の家族関係を表す中間表（汎用、プリキュア以外でも使える）。PK は (`character_id`, `related_character_id`, `relation_code`) の 3 列。自分自身（`character_id == related_character_id`）の禁止は当初 CHECK 制約 `ck_cfr_no_self` で表現する予定だったが、MySQL 8.0.16+ の制約で「FK の参照アクション（CASCADE 等）で使う列を CHECK 制約から参照できない」(Error 3823) ため定義不能。代わりに `BEFORE INSERT` / `BEFORE UPDATE` トリガ `tr_cfr_check_no_self_bi` / `_bu` で `SIGNAL SQLSTATE '45000'` 方式で禁止する（`precures` の `character_id` 整合性検証と同じパターン）。FK は `character_id` / `related_character_id` 双方とも `ON DELETE CASCADE`、`relation_code` は `ON DELETE RESTRICT`。1 行 = 「`character_id` から見た `related_character_id` の続柄」を表現する非対称構造で、双方向で完全表現するときは A→B（`FATHER`）と B→A（逆続柄）の 2 行を別途立てる運用。
4. `precures` テーブルを新設。プリキュア本体マスタ（1 行 = 1 プリキュア）。変身前 / 変身後 / 変身後 2 / 別形態 の 4 名義 FK（→ `character_aliases.alias_id`）、誕生日（`birth_month` `TINYINT UNSIGNED` 1-12 / `birth_day` `TINYINT UNSIGNED` 1-31、両方 NULL 可、CHECK 制約付き）、声優（FK → `persons.person_id`、NULL 可）、肌色 6 列（`skin_color_h` `SMALLINT UNSIGNED` 0-360、`skin_color_s` / `_l` `TINYINT UNSIGNED` 0-100、`skin_color_r` / `_g` / `_b` `TINYINT UNSIGNED` 0-255。CHECK は H/S/L のみ；R/G/B は型範囲 0-255 が完全一致のため CHECK 不要）、学校（`VARCHAR(128)`）／クラス（`VARCHAR(64)`）／家業（`VARCHAR(255)`）、備考、監査列、`is_deleted`。`transform_alias_id` には UNIQUE 制約（同じ「変身後の名義」を 2 つのプリキュアレコードに紐付けない）。`pre_transform` / `transform` の FK は `ON DELETE RESTRICT`、`transform2` / `alt_form` / `voice_actor_person_id` は `ON DELETE SET NULL`。
5. `precures` に `BEFORE INSERT` / `BEFORE UPDATE` トリガ `tr_precures_check_character_bi` / `_bu` を追加。4 本の alias FK が指す `character_id` がすべて同一であることを `SIGNAL SQLSTATE '45000'` で保証する（NULL の `transform2` / `alt_form` はスキップ）。MySQL 8.0 では CHECK 制約から別テーブルを参照できないため、`credit_block_entries` の整合性検証と同じパターン（トリガで SIGNAL）で実装している。

`INFORMATION_SCHEMA` で各オブジェクトの存在を確認してから DDL を発行するため、再実行しても安全な冪等スクリプト。

*`v1.2.4_add_name_en_columns.sql`:*

`person_aliases` / `company_aliases` / `characters` / `character_aliases` の 4 表に `name_en VARCHAR(128) DEFAULT NULL` 列を `name_kana` の直後に追加する。親テーブル `persons` / `companies` は v1.2.0 から `name_en` 列を持っていたが、名義テーブル群と `characters` 自体が持っていなかったため、英文クレジット出力で表記単位の英語名が引けないという対称性破れがあった。本マイグレでこれを解消する。各列の存在を `INFORMATION_SCHEMA.COLUMNS` で確認してから `ALTER TABLE` を発行する冪等スクリプトで、再実行しても安全。本体マイグレ `v1.2.4_add_precures_and_family.sql` とは関心事が違う（プリキュア追加 vs 名義群の英語表記追加）ため、独立ファイルに分離している。

**v1.2.0 マイグレーションで実施される変更**:

*`v1.2.0_add_credits.sql`:*

1. `series_kinds` に `credit_attach_to ENUM('SERIES','EPISODE') NOT NULL DEFAULT 'EPISODE'` 列を追加（`name_en` の直後）。既知 5 種別のうち TV / SPIN-OFF を `EPISODE`、MOVIE / MOVIE_SHORT / SPRING を `SERIES` にバックフィル。
2. `part_types` に `default_credit_kind ENUM('OP','ED') NULL` 列を追加（`display_order` の直後）。`OPENING` を `OP`、`ENDING` を `ED` にバックフィル（その他は NULL のまま＝クレジットを伴わないパート）。
3. 人物層 3 表の作成: `persons`（人物の同一性）／ `person_aliases`（時期別表記、前後リンク自参照 FK）／ `person_alias_persons`（多対多中間表、共同名義の稀ケース対応）。
4. 企業層 3 表の作成: `companies`（企業の同一性）／ `company_aliases`（屋号、前後リンク自参照 FK で改名・分社化に対応）／ `logos`（屋号配下の CI バージョン別ロゴ）。
5. キャラクター層 3 表の作成: `characters`（series 非依存で全プリキュア統一管理）／ `character_aliases`（話数別表記）／ `character_voice_castings`（REGULAR/SUBSTITUTE/TEMPORARY/MOB の 4 区分、期間管理付き）。
6. 役職層 2 表の作成: `roles`（NORMAL/SERIAL/THEME_SONG/VOICE_CAST/COMPANY_ONLY/LOGO_ONLY の 6 書式区分。**初期データ投入は行わない方針** — マイグレーションでは空テーブルだけを用意し、業務側で必要な役職を後から登録する）／ `series_role_format_overrides`（シリーズ × 役職 × 期間で書式テンプレを上書き、PK に `valid_from` を含む）。
7. クレジット本体の階層構造作成（v1.2.0 工程 G で 6 段階へ拡張）: `credits`（1 件 = 1 枚分のクレジット表示。シリーズ単位 or エピソード単位で OP/ED 各 1 件まで、`scope_kind` × `series_id`/`episode_id` の排他は **トリガーで担保**）／ `credit_cards`（クレジット内のカード 1 枚 = 1 行、`presentation` が `CARDS` か `ROLL`）／ `credit_card_tiers`（カード内の Tier 1 つ = 1 行、`tier_no` 1=上段 / 2=下段、v1.2.0 工程 G で実体テーブル化）／ `credit_card_groups`（Tier 内の Group 1 つ = 1 行、`group_no` 1 始まり、同 Tier 内で役職がサブグループを成すケースに対応、v1.2.0 工程 G で実体テーブル化）／ `credit_card_roles`（Group 配下の役職 1 つ = 1 行、`card_group_id` + `order_in_group` の 2 列構成、v1.2.0 工程 G で旧 4 列構成から刷新）／ `credit_role_blocks`（役職下のブロック、`col_count`（横カラム数、1=縦並び、2 以上で横カラム表示）と先頭企業名フィールド `leading_company_alias_id` を持つ。v1.2.0 工程 H 補修で `row_count` 列は撤去（行数はカラム数とエントリ数の従属で実行時に決まるため独立列として持つ意味がない、という判断）。旧コメント：v1.2.0 工程 F-fix3 で `rows` / `cols` から `row_count` / `col_count` にリネームしたが、その後 `row_count` は撤去された）／ `credit_block_entries`（ブロック内のエントリ、`entry_kind` PERSON/CHARACTER_VOICE/COMPANY/LOGO/TEXT で参照先列が切り替わる、整合性は **トリガーで担保**。v1.2.0 工程 H で SONG 種別を物理削除）。
8. `episode_theme_songs` の作成: エピソード × 主題歌（OP / ED 各 1 件、INSERT は複数可）の紐付け。クレジットの `THEME_SONG` ロールエントリはここから歌情報を引いてレンダリングする。
9. `series_id` / `episode_id` 等の FK の参照アクション（CASCADE / SET NULL）が CHECK 制約と併用できない MySQL 8.0 の制限（Error 3823）を回避するため、`credits.scope_kind` ⇄ `series_id`/`episode_id` の排他、`credit_block_entries.entry_kind` ⇄ 各参照列の整合性は、いずれも `BEFORE INSERT` / `BEFORE UPDATE` トリガーで実装している（`tracks` テーブルと同じ運用パターン）。

`INFORMATION_SCHEMA.COLUMNS` で列の存在を確認してから ALTER し、新規テーブルは `CREATE TABLE IF NOT EXISTS`、初期データは `INSERT IGNORE`、トリガーは `DROP TRIGGER IF EXISTS` してから再作成するため、再実行しても安全な冪等スクリプトです。

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

> **v1.1.5 メモ — 非 CD メディア投入時の挙動**: ドライブに DVD / Blu-ray / HD DVD が挿入された場合、CDAnalyzer は MMC `GET CONFIGURATION` で Current Profile を確認した時点で読み取りをスキップし、デバイスハンドルを即座にクローズします。挿入の自動検知（WM_DEVICECHANGE）経由ではメッセージボックスを出さず、画面下部のステータスラベルに「Drive X: DVD を検知したため読み取りをスキップ（BDAnalyzer 側で読み込んでください）」とだけ表示されるため、BDAnalyzer と同時起動して BD/DVD を扱う運用でも CDAnalyzer 側のドライブ占有が原因で BDAnalyzer が `VIDEO_TS.IFO` / `*.mpls` を読み損ねる事象が起きません。なお、ユーザが手動で「読み取り」ボタンを押した場合は、検知されたメディア種別とプロファイルコードを案内するダイアログを表示します（こちらは情報通知が必要な手動操作とみなすため）。GET CONFIGURATION 非対応の旧ドライブでは Current Profile を判定できないので、安全側に倒して従来通り TOC 読み取りにフォールバックします。

#### B. BD/DVD の登録

1. `PrecureDataStars.BDAnalyzer` を起動。自動または手動で `.mpls` / `.IFO` をロード。
   - **Blu-ray（v1.1.5 推奨）**: `BDMV/PLAYLIST/*.mpls` の任意 1 個を指定する。親フォルダが `PLAYLIST` であることが検出されると、フォルダ内の全 `*.mpls` を走査して有意なタイトル（プレイリスト）を並列抽出するフォルダ全走査モードに切り替わる。ロゴ・著作権警告等の短尺ダミーと、anti-rip 系の重複プレイリストは自動除外される（フィルタ仕様は後述）。ドライブ自動検知も `BDMV/PLAYLIST` 配下の `.mpls` が 1 個でもあればフォルダごと採用するため、`00000.mpls` / `00001.mpls` がない構成にも対応する。
   - **Blu-ray（単一プレイリストモード）**: `BDMV/PLAYLIST` 配下にない `.mpls` ファイル（コピーして別フォルダに置いたものや、個別プレイリストを明示確認したいケース）を直接指定すると、そのプレイリスト 1 個だけを従来通り解析する（v1.1.4 互換）。
   - **DVD（v1.1.1 推奨）**: **`VIDEO_TS/VIDEO_TS.IFO` を指定**。下記の二段階ルーティングでチャプター一覧を抽出する。ドライブ自動検知も `VIDEO_TS.IFO` を優先する。
   - **DVD（単一 VTS モード）**: `VTS_xx_0.IFO` を直接指定すると、その VTS の先頭 PGC のみを解析する（個別 VTS 確認用。v1.1.0 互換）。
2. 「既存ディスクと照合 / 新規登録...」で照合（チャプター数 + 総尺 ms ±1 秒による TOC 曖昧のみ）。CD と同様に v1.1.3 から `DiscMatchDialog` のアクションが 3 通りに増えており、**「既存商品に追加ディスクとして登録」** で BOX 商品の Disc 2 / Disc 3 を後から足す運用が可能（後述の「既存商品への追加ディスク登録フロー」を参照）。
3. 反映時は discs テーブルの物理情報が同期され、加えて `video_chapters` テーブルへチャプター情報が一括登録される（再読み取り時は「全削除 → 置換」で上書き）。
   - 自動投入されるのは `start_time_ms` / `duration_ms` / `playlist_file` / `source_kind` の物理情報のみ。
   - `title` / `part_type` / `notes` は NULL のまま登録されるため、Catalog GUI 側で手動で補完する運用。
   - DVD フォルダ全走査モードでは、チャプター番号 (`chapter_no`) はディスク全体で通し番号（1, 2, 3, …）となり、`playlist_file` にはタイトル識別子が入る（VMGI モードでは `Title_01` 等、Per-VTS モードでは `VTS_02` 等）。Blu-ray のフォルダ全走査モードでは `playlist_file` に MPLS ファイル名（例 `00000.mpls`）が入る。これにより同一ディスク内でどのチャプターがどのタイトル由来かを区別できる。
   - チャプター開始時刻 (`start_time_ms`) は**タイトル単位の相対時刻**（各タイトルの先頭 = 0ms）として記録される（DVD・Blu-ray 共通）。

##### Blu-ray PLAYLIST フォルダ全走査の仕様（v1.1.5）

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
- チャプター行: 2 段インデント `    1`、`    2`、…
- 末尾の除外サマリ行（除外があった場合のみ、グレー文字）: `短尺 X / 0ms Y / 境界極短 Z / 重複 W`
- 既定チェックは「総尺最大タイトル + そのチャプター」のみオン、他は未チェック（DVD 側と同じ流儀）

`lblInfo` には `00000.mpls - (Blu-ray PLAYLIST scan) Titles: N   Chapters: M   Aggregated: hh:mm:ss.ff` の形式で集約サマリが表示される。集約総尺はフィルタ D で重複が畳まれた後の単純合計（Blu-ray ではハードリンク等の概念がないため、DVD 側のような max/sum 切替ロジックは持たない）。

しきい値は `MplsParser.ExtractTitlesFromBdmv` の引数で変更可能だが、現状の MainForm からは既定値（60 / 1 / 500）固定で呼んでいる。30 秒スポット等を取り込みたいケースでパラメータ調整が必要になったら、別途オーバーロードを公開する。

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

#### C''. 商品・ディスク管理画面（v1.1.3 新設、v1.1.4 でレイアウト刷新）

`PrecureDataStars.Catalog` のメニュー「商品・ディスク管理...」は、商品 1 件と所属ディスク群を 1 画面で編集する統合エディタです。旧 `ProductsEditorForm`（商品管理）と、`DiscsEditorForm`（ディスク／トラック管理）のうちディスク詳細編集パートを 1 画面に統合したもの。トラック編集は「トラック管理...」（C 節参照）に分離されました。

**画面構成（v1.1.4 改）**

上下 2 段構成です。上段（商品エリア）と下段（ディスクエリア）を上下に並べ、それぞれ左 60% に一覧、右 40% に詳細エディタを配置します。下段の高さは 400 px に固定され、残りの縦領域はすべて上段に割り当てられるため、商品エディタの全フィールドが余裕で表示できます。

- **検索バー**（最上部）: 検索キーワード（品番／タイトル／略称／英語タイトルに部分一致）、検索・再読込ボタン
- **上段左ペイン（60%）**: 商品一覧。**発売日昇順、同一日内は代表品番昇順**で並ぶ（v1.1.3 で並び順を変更。過去から時系列に入力していく運用に合わせるため）。表示カラムは「発売日 / 品番 / タイトル / 種別 / 税込 / 枚数」と翻訳値のみで、内部コードは出さない。
- **上段右ペイン（40%）**: 商品詳細エディタ。代表品番・タイトル・略称・英語タイトル・商品種別・発売日・税抜価格・**税込価格＋自動計算ボタン**・ディスク枚数・発売元・販売元・レーベル・Amazon ASIN・Apple Album ID・Spotify Album ID・備考。新規／保存／削除ボタンは右端に固定。
- **下段左ペイン（60%）**: 所属ディスク一覧（組内番号・品番・ディスクタイトル・メディア）。下段の高さ 400 px は所属ディスク 10 行表示と、ディスク詳細エディタ全フィールド表示のうち大きい方を満たす値（プリキュアの BOX 商品は MAX 10 枚程度を想定）。
- **下段右ペイン（40%）**: ディスク詳細エディタ（品番・組内番号・ディスクタイトル・略称・英語タイトル・シリーズ・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考）。新規／保存／削除ボタンは右端に固定。

ウインドウをリサイズすると、上段と下段の左右 60:40 比率は `splitProduct.SizeChanged` / `splitDisc.SizeChanged` イベントで都度自動的に再計算されます。下段の高さ 400 px は `splitMain.FixedPanel = FixedPanel.Panel2` で固定され、縦方向の拡縮はすべて上段（商品エリア）に追加されます。詳細エディタの入力欄は `Anchor = Top|Left|Right` で右端追従、ボタン群は `Anchor = Top|Right` で右端固定です。

**ディスク詳細編集と物理情報の保全**（v1.1.4 改）

本画面で編集できるのはタイトル系・組内番号・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考といったメタ情報のみです。`total_length_frames` / `total_length_ms` / `num_chapters` などの物理情報、CD-Text 系 8 列、`cddb_disc_id` / `musicbrainz_disc_id` / `last_read_at` といった「CDAnalyzer / BDAnalyzer が読み取って記録するもの」は本フォームから編集できません。

これらの非編集列は、ディスク保存時に DB から既存値を引き直して自動的に引き継ぎます。v1.1.3 では UPSERT 経路で NULL 上書きが発生し、ディスクタイトルだけ変えるつもりで保存したら閲覧 UI のディスク総尺が空欄になるという不具合がありましたが、v1.1.4 で解消されています（既に被害を受けたレコードは CDAnalyzer / BDAnalyzer から「選択したディスクに反映」で再取り込みすると物理情報のみ復旧できます）。

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
2. `DiscMatchDialog` のグリッド（自動照合候補 or 手動検索結果）から、**追加先 BOX に既に登録されているディスクを 1 つ選択**（例: BOX の Disc 1）。**v1.1.4 改: 自動照合候補が 1 件のみ・手動検索結果が 1 件のみの場合は先頭行が自動選択された状態でグリッドが表示される**ため、ユーザーが行をクリックする手間なくそのままボタンを押下できる
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
2. **新ディスクの品番が DB 上に既存していないかを事前検証（v1.1.4 追加）**。`DiscsRepository.GetByCatalogNoAsync` で既存レコードがヒットしたら `InvalidOperationException("品番 [XXX] は既に登録されています。別の品番を指定してください。")` を送出して以降の処理を行わない。論理削除済み (`is_deleted = 1`) のレコードもヒット扱いとする（誤って論理削除済みディスクを `INSERT ... ON DUPLICATE KEY UPDATE` 経由で復活させてしまう事故を防ぐ）。CDAnalyzer / BDAnalyzer 側はこの例外を `ShowError` で MessageBox に出すため、ユーザーには「重複していたので登録されなかった」ことが伝わる
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

#### C'. ディスク・トラック閲覧画面（読み取り専用、v1.1.2 改、v1.1.4 改）

`PrecureDataStars.Catalog` のメニュー「ディスク・トラック閲覧」は、ディスク → トラックを翻訳済みの表示値で一覧する参照専用ビューです。編集は一切行いません。

**画面構成**

- **ツールバー**（最上部）: 検索キーワード（品番 / タイトル / シリーズ名に部分一致）、シリーズ絞り込みドロップダウン、再読込ボタン、件数表示
- **ディスク一覧**（上段、SplitContainer）
- **トラック一覧**（下段、SplitContainer。ディスク選択に応じて更新）

v1.1.2 より外周に 10 px の余白を設け、上下ペインの分割バーも若干太めに取って視覚的な窮屈さを解消しています。

**v1.1.4 改: 上下ペインを常に半々で自動追従**

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

#### C'''''. マスタ管理画面（v1.1.4 改）

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

**画面構成（v1.1.4 改）**

各タブは上半分にグリッド、下半分に編集フォームと操作ボタンが並びます。`bgm_sessions` を除く 6 つのマスタタブは共通レイアウト（`BuildTab` ヘルパで生成）で、以下のボタンを縦並びに 4 つ持ちます:

- **新規**: フォーム入力欄をすべて空にし、グリッド選択を解除する。これから入力する内容を新しいレコードとして登録する操作の起点。新規追加と既存行の編集を見た目で明確に区別するため、v1.1.4 で追加されたボタン。
- **保存 / 更新**: 入力欄のコードに基づいて UPSERT を実行（同コードがあれば更新、なければ INSERT）。
- **選択行を削除**: グリッドで選択中の行を削除する（FK で参照されている場合は失敗）。
- **並べ替えを反映** （v1.1.4 で追加）: 後述の行ドラッグ&ドロップで変更したグリッド上の並び順を、`display_order` カラムに `1, 2, 3, ...` として一斉反映する。確認ダイアログを経て実行。

`bgm_sessions` タブは PK が `(series_id, session_no)` の 2 列で表示順を `session_no` が兼ねるため、共通 `BuildTab` を使わず専用 `BuildBgmSessionsTab` で構築されます。シリーズ選択コンボでフィルタしたうえで、`session_no` を自動採番して新規追加する「新規追加」「保存 / 更新」「選択行を削除」の 3 ボタン構成（並べ替えは対象外）。

**行ドラッグ&ドロップによる並べ替え** （v1.1.4 で追加、`bgm_sessions` を除く 6 マスタ）

`display_order` を NumericUpDown で 1 件ずつ数値入力する操作は、間に挿入したい時に既存値の全書き換えが必要で煩雑でした。v1.1.4 では DataGridView の行を上下にマウスドラッグして並べ替えできるようにしています:

1. 行をクリックしてドラッグ → 希望位置にドロップ。複数回繰り返して目的の順序に並べ替える。
2. ドラッグだけでは DB は変わらず、グリッド表示上の List 内で要素が入れ替わるだけ。
3. **「並べ替えを反映」ボタン**を押すと「現在の並び順で表示順を 1〜N に振り直しますがよろしいですか？」の確認ダイアログ。Yes でグリッドの先頭から `display_order = 1, 2, 3, ...` を割り当てて全件 UPSERT。

ドラッグ実装は `EnableRowDrag` 共通ヘルパで、`SystemInformation.DragSize` を超える移動でドラッグ開始（クリック選択との誤動作を防止）、`DataSource` が `IList` の場合のみ要素を入れ替えて再バインドします。各マスタタブの `LoadAllAsync` / 「並べ替えを反映」後の再読み込みで `(await Repo.GetAllAsync()).ToList()` をバインドする実装になっており、ドラッグ操作の前提が常に整っています。

**監査列の自動非表示** （v1.1.4 で全タブ統一）

すべてのグリッドで `CreatedAt` / `UpdatedAt` / `CreatedBy` / `UpdatedBy` 列は `DataBindingComplete` 時に自動的に Visible = false に設定されます。マスタの実運用で必要な情報は「コード / 名称(日) / 名称(英) / 表示順」の 4 列のみで、監査列はノイズになるため。実装は `HideAuditColumns` 共通ヘルパで、コンストラクタで全グリッドに 1 度だけ結線します。

**`CreatedBy` の保全**

並べ替え反映時は同じ List 内のアイテムを再 UPSERT しますが、Repository の SQL が `INSERT ... ON DUPLICATE KEY UPDATE` の `UPDATE` 部分で `created_by` を含めない設計のため、既存行の `CreatedBy` は DB レベルで保全されます。`UpdatedBy` のみ `Environment.UserName` で更新されます。

**ウインドウサイズ**

`ClientSize = 1000×680`、`StartPosition = CenterScreen`。ボタン 1 列分の縦サイズ拡張と外周余白の確保に伴い、v1.1.3 までの 900×560 から拡大しました。

#### D. 旧 SQL Server からの移行

1. `PrecureDataStars.LegacyImport` の `App.config` に `LegacyServer` と `TargetMySql` を設定。
2. まず `--dry-run` で件数サマリーを確認:
   ```bash
   dotnet run --project PrecureDataStars.LegacyImport -- --dry-run
   ```
3. 問題なければ通常実行で移行。recording 未特定で OTHER に格下げされたトラックは Catalog GUI で後補正する前提。
4. 旧 `discs.series_id` の値は、グループ内の新 `discs.series_id`（複数枚組なら全枚数分）へ同じ値としてコピーされる。新 `products` には `series_id` は載らない。

### クレジット編集（v1.2.0 工程 H-8 で全面メモリ化）

`PrecureDataStars.Catalog` のメインメニュー「クレジット編集...」から `CreditEditorForm` を起動して、シリーズまたはエピソードに紐づく OP/ED クレジットの 6 階層（Card / Tier / Group / Role / Block / Entry）を編集する。3 ペイン構成：

- **左ペイン**: scope（SERIES / EPISODE）の絞込み、シリーズ・エピソードの選択コンボ、クレジット一覧 ListBox、新規クレジット作成・**話数コピー** ボタン、選択中クレジットのプロパティ編集（presentation / part_type / 備考）と「プロパティ保存」「クレジット削除」ボタン。
- **中央ペイン**: 階層ツリーと「+ カード」「+ Tier」「+ Group」「+ 役職」「+ ブロック」「+ エントリ」「↑」「↓」「✖ 削除」のツリー編集ボタン群、画面下に **「💾 保存」「✖ 取消」**。
- **右ペイン**: ツリーで選択したノードに応じて切り替わる。Block 選択時は `BlockEditorPanel`（col_count / block_seq / leading_company_alias_id / notes の編集と「適用」ボタン）、Entry 選択時は `EntryEditorPanel`（種別ごとの入力 UI と「保存」「削除」ボタン）。

#### 編集の流れ（Draft セッション方式）

1. クレジットを選択すると、`CreditDraftLoader` が DB から全階層を読み込んで Draft セッションをメモリ上に構築する。
2. ユーザーがツリーやパネルで操作（追加・編集・削除・並べ替え・DnD 移動）すると、すべて Draft オブジェクトに対して反映され、DB は触らない。
3. 未保存変更があると、ツリー背景色が **薄い黄色**、ステータスバー末尾に「★ 未保存の変更あり」が表示され、画面下部の「💾 保存」「✖ 取消」が Enabled になる。
4. 「💾 保存」を押すと `CreditSaveService` が 5 フェーズ（**1A** エントリ削除 → **2** 新規 → **3** 更新 → **1B** ブロック以上の親階層削除 → **4** seq 整合性）を **1 トランザクション** 内で実行して DB へ確定する。失敗すれば全体ロールバック。親階層 DELETE を更新フェーズの後ろに置くことで、DnD で別ブロックに移動したエントリ／別役職に移動したブロック等が、旧親 DELETE の CASCADE で巻き添え削除される事故を防ぐ（v1.3.0 で恒久対策）。
5. 「✖ 取消」を押すと現在の Draft を破棄して DB から再読み込みする。

#### 話数コピー（シリーズ跨ぎ対応）

新シリーズの第 1 話を作成する際、毎回ゼロから役職構造を組み立てるのは非効率なので、**前作の OP / ED を丸ごと複製してから差分編集** するワークフローに対応する：

1. コピー元クレジットを左ペインで選択 → **「📋 話数コピー...」ボタン** を押下。
2. `CreditCopyDialog` でコピー先のシリーズ・エピソード・presentation・part_type・備考を指定（クレジット種別はコピー元と同じで固定）。
3. コピー先に同種クレジットが既に存在する場合は「上書き／中止」を選ぶ（上書き時は既存を即時論理削除）。
4. `CreditDraftLoader.CloneForCopyAsync` がコピー元を読み込んで配下を全部 `State = Added` で deep clone し、コピー先 Draft を構築。
5. 画面がコピー先 Draft に切り替わる（黄色背景・未保存マーク）。内容を確認・編集してから「💾 保存」で 1 トランザクション INSERT。

#### HTML プレビュー（v1.2.0 工程 H-9）

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

#### クレジット一括入力（v1.2.1 追加、v1.2.2 で大幅拡張）

長尺クレジットをツリー編集で 1 件ずつ追加するのは現実的でないため、テキスト形式でまとめて流し込めるダイアログを用意した。左ペイン下部の **「📝 クレジット一括入力...」** ボタンから開く（v1.2.1 仕様）。v1.2.2 では **ツリー右クリック「📝 一括入力で編集...」** メニューも追加され、選択スコープ（クレジット全体／カード／ティア／グループ／役職）の中身を編集する用途にも使えるようになった。

**起動経路（v1.2.2 で 2 モード化）**:

| モード | 起動方法 | 動作 |
|---|---|---|
| `AppendToCredit`（v1.2.1 → v1.3.0 で全面改訂） | 左ペイン「📝 クレジット一括入力...」ボタン | **クレジット全体の構造差分検出モード**。起動時に現状全文を `CreditBulkInputEncoder.EncodeFullAsync` で逆翻訳した文字列が初期値として入る。適用時は旧テキストと新テキストの構造差分を検出して、変わった末端だけ Modified / Added / Deleted で反映（変わっていない Card / Tier / Group / Role / Block / Entry はすべて Unchanged 維持で `alias_id` や監査列も保持）。Block 内 Entry は LCS マッチングで行入れ替え（ヒューマンエラー）を `entry_seq` 更新の Modified として拾う |
| `ReplaceScope`（v1.2.2 新規） | ツリー右クリック「📝 一括入力で編集...」 | 選択スコープの中身を **置換**。起動時に既存内容を `CreditBulkInputEncoder` で逆翻訳した文字列が初期値として入る。スコープ配下を全 DELETE → 全 INSERT する全置換セマンティクス（差分検出はしない、範囲限定の用途向け） |

**書式の要点**（v1.2.1 仕様 + v1.2.2 拡張）:

- 行末コロン `XXX:` で役職開始、空行で同役職内のブロック区切り、`-` / `--` / `---` / `----` でブロック・グループ・ティア・カード区切り、タブ区切りで `col_count` 並び、`<キャラ>声優` で CHARACTER_VOICE
- **v1.2.2 追加** `[屋号#CIバージョン]` で LOGO エントリ（最右の `#` で屋号と CI バージョンに分解、屋号下のロゴから引き当て）
- **v1.2.2 追加** 行頭 `🎬`（U+1F3AC、絵文字）で本放送限定エントリ（`is_broadcast_only=1`）
- **v1.2.2 追加** 行頭 `& `（半角アンパサンド + 半角SP）で直前エントリと A/B 併記（保存時に `parallel_with_entry_id` 解決）
- **v1.2.2 追加** 行末 ` // 備考` で当該エントリの `notes` 設定
- **v1.2.2 追加** `@cols=N` で当該ブロックの `col_count` を明示指定（タブ数推測より優先）
- **v1.2.2 追加** `@notes=値` で直近スコープ（Card/Tier/Group/Role/Block のうち最後に開いたもの）の `notes` を設定
- 修飾子は重ねがけ可（例: `🎬 & 山田 太郎 // 旧名義あり`）。順序を問わない
- 250 ms デバウンスでパースしてプレビュー反映、Block 重大度の警告 1 件で「適用」ボタンが Disabled
- 適用時、未登録役職は `QuickAddRoleDialog` を 1 件ずつ起動して登録（日本語名は事前入力済）、Person / Character / Company は自動 QuickAdd、引き当てに失敗した名前は TEXT エントリ（`raw_text`）に降格
- LOGO エントリのみ屋号 + CI バージョン未ヒット時は **TEXT 降格 + InfoMessage**（マスタ管理画面の「ロゴ」タブで明示登録するよう促す。LOGO は CI デザイン情報を伴うため自動投入しない方針）
- Draft セッションへの追加は **構造差分検出**（AppendToCredit モード、v1.3.0 で改訂）または **スコープ置換**（ReplaceScope モード）。DB 確定は通常の「💾 保存」フロー

**v1.2.2 ラウンドトリップ性**:

`CreditBulkInputEncoder` は Draft 階層を一括入力フォーマットに逆翻訳するため、「右クリック → 一括入力で編集 → 編集 → 適用」のサイクルで既存クレジットの大幅な書き換えがテキストエディタ感覚で行える。Encoder の出力を Parser に通すと、マスタ ID 解決後の状態で同じ構造が再現される（例外: `IsForcedNewCharacter` のアスタは Draft 上で追跡しないため再エンコードでは消える）。

**v1.3.0 拡張: 「旧名義 =&gt; 新名義」記法**

人物・キャラ・企業屋号・LOGO の屋号部いずれの位置でも、`旧名義 =&gt; 新名義` という記法でリダイレクトを明示できる。**矢印の向きは「名義が変わった」遷移方向に揃えてあり、左 = 既存マスタの参照キー、右 = この行で実際に表示する別名義** を表す。

- 例: `青木 久美子 => 青木 久実子` — 左の「青木 久美子」を `person_aliases.name` 完全一致で引き当てて `person_id` を取得し、同 person 配下に右の「青木 久実子」を新 alias として登録
- 例: `[東映アニメ => 東映アニメーション]` — COMPANY エントリ。同じく既存 company に新屋号を追加
- 例: `<キュアブラック => 美墨なぎさ>本名 陽子` — VOICE_CAST 行。`<>` 内に `=>` でキャラの別名義を、外側の声優部にも独立して `=>` を書ける（両側併用可）
- 例: `[東映アニメ => 東映アニメーション#2024年版]` — LOGO エントリ。屋号部分（`#` の左側）のみ `=>` を解釈、CI バージョン違いは別 logos 行で表現するためリダイレクト対象外

旧名義が既存マスタに見つからない場合は警告 `InfoMessages` を出した上で、右側のみで通常の新規作成にフォールバック。タイポしたまま気づかずに人物が量産される事故を抑える。

**v1.3.0 拡張: 似て非なる名義の警告 + 新規登録候補の事前通知**

新規作成しようとしている名義（`=>` リダイレクトで決着しなかったもの）について、`person_aliases` / `character_aliases` / `company_aliases` を全件取得し、空白除去後の **LCS（最長共通部分列）／ max(len) ≥ 0.5** を満たすが完全一致でない既存名義があれば警告に積む。漢字違い（「五條 真由美」↔「五条真由美」）や空白違いの誤入力を検出し、ユーザーに「同一人物なら `=>` で書くか、マスタ管理画面で別名義として統合してください」と促す。LOGO の屋号引き当て失敗時は `company_aliases` に対して同じ判定が走る。

**新規登録候補の事前通知**（hotfix3 で追加）: 完全一致なし + 似て非なる候補もなし、つまり「ピュアに新規登録される予定」の名義については、警告ペインに **情報レベル（ℹ）の警告**として「ℹ N 行目: ○○名義「△△」は新規登録候補です（マスタに既存名義および類似名義なし）。Apply 時に新規 person + alias を作成します。」と表示する。同様に **`roles` マスタに無い役職表示名** も `name_ja` 完全一致で突合して、未登録なら情報レベルで「Apply 時に QuickAddRoleDialog で role_code / 英名 / role_format_kind を入力して新規登録します。」と表示する。これにより、ユーザーは Apply 前にマスタ追加が発生する箇所を全部チェックできる。

**判定タイミング**（hotfix2 で改訂）: 入力テキストの 250 ms デバウンス後にリアルタイム判定。パース完了直後に `CreditBulkApplyService.CheckSimilarNamesAsync` が呼ばれ、新規作成予定の名義（リダイレクト無し + `SearchAsync` 完全一致なし）について全件比較を実施。結果は **警告ペイン**（`lstWarnings`）に他のパース警告と同じ並びで表示される（行番号付き、`Severity = Warning` または `Info` なので Apply ボタンは無効化されない）。連続入力時は `CancellationTokenSource` で前の判定を取り消し、最後のテキストに対する結果だけがペインに残る。比較中は警告ペイン上部に **「似て非なる名義を比較中... (n/total)」** のステータスラベルが出て完了で消える。Apply 経路の `ResolveOrCreate` 内でも同じ判定が冗長に走り、こちらは適用後の MessageBox の `InfoMessages` に積まれる（入力後すぐ Apply して類似度判定が完了する前に呼ばれた場合の救済）。

**v1.3.0 拡張: ダイアログレイアウト**

右ペインのプレビュー（上）：警告（下）の比率を **3:1 → 4:1（8:2）** に変更（`splitterDistance` 500 → 515）。警告ペインがプレビューを圧迫していた問題を解消。

#### Card / Tier / Group / Role の備考編集（v1.2.2 追加）

クレジット編集画面のツリーで Card / Tier / Group / 役職ノードを選択すると、右ペインに **`NodePropertiesEditorPanel`** が表示され、対応する DB 列（`credit_cards.notes` / `credit_card_tiers.notes` / `credit_card_groups.notes` / `credit_card_roles.notes`）の備考を直接編集できる。複数行 TextBox + 「💾 保存」ボタンの単純な構成。保存ボタン押下で Draft.Entity.Notes 更新 + `MarkModified()` を実行し、ツリー再描画で `📝<備考>` ラベルが反映される。DB への書き込みは通常の「💾 保存」ボタンで一括コミット。

#### 名寄せ機能（v1.2.1 追加）

クレジット入力中にうっかり同名人物を別人として 2 件登録してしまったり、改名（旧屋号 → 新屋号、旧名義 → 新名義）が発生したとき用に、`CreditMastersEditorForm` の人物名義 / 企業屋号 / キャラ名義タブそれぞれにボタンを 2 つずつ追加した：

- **「別人物（企業／キャラ）に付け替え...」** (`AliasReassignDialog`)：選択中名義の紐付け先だけを別の既存親に変更する。親本体の表示名は変更しない。
- **「この名義で改名...」** (`AliasRenameDialog`)：新表記を入力して改名する。人物・企業の場合は **新 alias を作成して旧 alias と predecessor/successor で自動リンク**（履歴を残す）、キャラの場合は現 alias を上書き（character_aliases に履歴列が無いため）。

孤立した旧親（紐付く名義が 0 件になった `persons` / `companies` / `characters`）は付け替え時に自動で論理削除される。

---


### クレジット入力レシピ集（役職別の正しいブロック構成）

クレジットは「役職 → ブロック → エントリ」の 3 階層構造を取り、役職に紐づく `default_format_template`（DSL テンプレート）が **どのブロックから何のエントリを取り出してどう並べるか** を決める。役職ごとに想定するブロック構成が異なるため、本節では代表的な役職について「ツリー上どう積めば期待する展開結果になるか」を示す。

> **💡 v1.2.1 / v1.2.2 補足**: 1 件 1 件のエントリを TreeView 上で積み上げていくのは、長尺クレジット（とくに連名が多い「制作協力」「アニメーション制作」など）では手数が多すぎて現実的でない。v1.2.1 で追加された **「📝 クレジット一括入力...」** ボタンを使うと、テキスト形式でまとめて投入できる。v1.2.2 では既存クレジットを **ツリー右クリック「📝 一括入力で編集...」** から逆翻訳して開き、テキストエディタ感覚で書き換えてから戻すこともできる（書式は変更履歴 v1.2.1 / v1.2.2 のセクションを参照）。本節のレシピは「どういう構造を最終的に作りたいか」を理解するためのリファレンスとして読み、実際の入力は一括入力 → 微調整、の順で進めるとよい。

#### 連載（`SERIALIZED_IN`） + 漫画（`MANGA`） — v1.3.0 stage 19 で構造変更

**背景の変更**: v1.3.0 stage 19 までは、連載クレジット下の漫画家を `SERIALIZED_IN` 役職下に PERSON エントリとして同居させていたが、`CreditInvolvementIndex` の集計で漫画家が「連載」役職として誤集計される問題があった。stage 19 で **役職を 2 つに分割**：

- `SERIALIZED_IN`「連載」: 雑誌（COMPANY エントリ）のみ
- `MANGA`「漫画」: 漫画家（PERSON エントリ）のみ

表示上は `SERIALIZED_IN` テンプレの中で **兄弟役職参照構文 `{ROLE:MANGA.PERSONS}`** を使い、同 Group 内の MANGA 役職下の人物を取り込む。これにより画像 1 のレイアウト（「漫画・上北 ふたご」を「連載」見出しの直下に表示）を保ちつつ、集計は `MANGA` → 「漫画」、雑誌 → 「連載」と正しく分かれる。

**テンプレ（`SERIALIZED_IN`、stage 19 で更新）**:
```
{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
<strong>漫画</strong>・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

**テンプレ（`MANGA`、stage 19 で新設）**:
```
{PERSONS}
```

`MANGA` テンプレは普段 `{ROLE:MANGA.PERSONS}` 経由で間接的に評価される。`MANGA` 役職を独立に描画する状況（クレジット画面で漫画役職だけのカードを作るレアケース）に備えて単純な `{PERSONS}` を持たせる。

**期待する展開結果**:
```
連載  講談社「なかよし」
      漫画・上北 ふたご
       「たのしい幼稚園」
       「おともだち」ほか
```

**ブロック構成（stage 19 マイグレーション後）**:
```
Card / Tier / Group
├─ Role: SERIALIZED_IN  連載  (order_in_group=1)
│   ├─ Block #1 (1 cols, 1 entries)   ← {#BLOCKS:first}
│   │    leading_company_alias_id = 「講談社」屋号 ID
│   │    └─ [COMPANY]  #1  「なかよし」屋号
│   ├─ Block #2 (1 cols, 1 entries)   ← {#BLOCKS:rest} の最初
│   │    └─ [COMPANY]  #1  「たのしい幼稚園」屋号
│   └─ Block #3 (1 cols, 1 entries)   ← {#BLOCKS:rest} の続き
│        └─ [COMPANY]  #1  「おともだち」屋号
└─ Role: MANGA  漫画  (order_in_group=2)
    └─ Block #1 (1 cols, 1 entries)
         └─ [PERSON]   #1  「上北 ふたご」名義
```

**ポイント**:
- **`{ROLE:MANGA.PERSONS}`** が新構文（兄弟役職参照）。同 Group 内で `role_code='MANGA'` の役職を 1 つ探し、その役職配下の Block 群を一巡りして `{PERSONS}` を Block ごとに評価し、Block 間は内側プレースホルダの `sep` オプション（既定 `、`）で連結する。
- `{ROLE:CODE.PLACEHOLDER}` の **1 段ネスト不可**: `MANGA` テンプレ内で `{ROLE:SERIALIZED_IN.…}` と書いても無限ループせず、再帰経路の `{ROLE:…}` は空文字に展開される（無限ループ防止）。
- 旧 v1.3.0 stage 18 までのデータ構造（`SERIALIZED_IN` 配下に PERSON 同居）は **stage 19 のマイグレーション SQL**（`db/migrations/v1.3.0_stage19_manga_role_split.sql`）で自動的に新構造に変換される。冪等性あり、再実行可能。
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
Role: THEME_SONG_OP  オープニング主題歌  (order 1)
├─ 📀 Song(OP): ...               ← episode_theme_songs から動的取得（仮想ノード、編集不可）
└─ Block #1 (1 cols, 1 entries)    ← レーベル表記用のブロック
     └─ [COMPANY]  #1  「マーベラスエンターテイメント」屋号
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
「DANZEN! ふたりはプリキュア」    「ゲッチュウ! らぶらぶぅ?!」
作詞:青木久美子                    作詞:青木久美子
作曲:小杉保夫                      作曲:佐藤直紀
編曲:佐藤直紀                      編曲:佐藤直紀
うた:五條真由美                    うた:五條真由美

マーベラスエンターテイメント
```

**ブロック構成**:
```
Role: THEME_SONG_OP_COMBINED  主題歌  (order 1)  [横 2 カラム表示指定]
├─ 📀 Song(OP): 『DANZEN! ふたりはプリキュア』 ...
├─ 📀 Song(ED): 『ゲッチュウ! らぶらぶぅ?!』 ...
└─ Block #1 (1 cols, 1 entries)
     └─ [COMPANY]  #1  「マーベラスエンターテイメント」屋号
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
Role: INSERT_SONG  挿入歌  (order 1)
├─ 📀 Song(INSERT): ...           ← 1 曲または複数曲
└─ Block #1 (1 cols, 1 entries)
     └─ [COMPANY]  #1  レーベル屋号
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
Role: INSERT_SONGS_NONCREDITED  挿入歌（ノンクレジット）  (order 1)
└─ 📀 Song(INSERT): 🚫[ノンクレジット] ...   ← 視認用マーク付き
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
Role: PRODUCER  プロデューサー  (order 1)
└─ Block #1 (1 cols, 1 entries)
   ├─ [PERSON]  #1  「西澤萌黄」名義（所属:ABC）
   ├─ [PERSON]  #2  「高橋知子」名義（所属:ADK）
   └─ [PERSON]  #3  「鷲尾天」名義
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
※ `{CHARACTER_VOICES}` プレースホルダは v1.2.0 工程 H 時点では未実装。将来の拡張候補。

**ブロック構成**:
```
Role: VOICE_CAST  声の出演  (order 1)
└─ Block #1 (m×n)
   ├─ [CHARACTER_VOICE]  #1  キャラ「美墨なぎさ」 / 声優「本名陽子」
   ├─ [CHARACTER_VOICE]  #2  キャラ「雪城ほのか」 / 声優「ゆかな」
   └─ ...
```

**ポイント**:
- `[CHARACTER_VOICE]` エントリは「キャラクター名義（`character_aliases`）+ 声優名義（`person_aliases`）」のペアで 1 行を成す。`character_alias_id` の代わりに `raw_character_text`（モブ等のマスタ未登録）も使える。
- 役職テンプレで `{CHARACTER_VOICES}` の整形ロジックを将来実装する場合は、専用ハンドラ（`Forms/TemplateRendering/Handlers/` 配下）として追加する想定。

#### 制作協力 / 制作（ロゴ列挙系）

**ブロック構成**:
```
Role: PRODUCTION_COOPERATION  制作協力  (order 1)
└─ Block #1 (1 cols, 1 entries)
   └─ [LOGO]  #1  「東映」マーク+横書きゴシック

Role: PRODUCTION  制作  (order 2)
└─ Block #1 (1 cols, 1 entries)
   ├─ [LOGO]  #1  「ABC」ABC(1989年3代目ロゴ)
   ├─ [LOGO]  #2  「ADK」ADK(2002年ロゴ)
   └─ [LOGO]  #3  「東映アニメーション」東映アニメーション(通常ロゴ)
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

### Web 公開用静的サイト生成（v1.3.0 追加）

`PrecureDataStars.SiteBuilder` は、ローカル MySQL の内容を読み出して Web 公開用の静的 HTML 一式を生成するコンソールアプリケーションです。`precure.tv` での Web 公開を想定していますが、ツール自体は AWS 連携を含まず、純粋に「DB → 静的ファイル」変換のみを行います（成果物を S3 等に同期する処理は本ツール範囲外、手動 `aws s3 sync` 等を別途想定）。

#### A. 前提・実行方法

1. 既存の Catalog GUI / Episodes GUI と同じ MySQL データベース（`precure_datastars`）が稼働していること。
2. `PrecureDataStars.SiteBuilder/App.config.sample` を `App.config` にコピーし、`DatastarsMySql` 接続文字列を環境に合わせて書き換える。
3. 同じ `App.config` の `appSettings` で出力先・ベース URL・サイト名を指定できる:
   - `SiteOutputDir`: 生成 HTML 一式の出力先ディレクトリ（絶対パス推奨。空のときは実行ファイル直下 `out/site/` にフォールバック）
   - `SiteBaseUrl`: canonical / OGP / sitemap.xml の絶対 URL 組み立て用ベース URL（末尾スラッシュなし、例 `https://precure.tv`）。空のときは canonical 出力をスキップ
   - `SiteName`: ヘッダ・タイトルに表示するサイト名（既定 `precure-datastars`）
4. ビルド & 実行:
   ```bash
   dotnet run --project PrecureDataStars.SiteBuilder -c Release
   ```
5. 出力先（既定 `out/site/`）に静的 HTML 一式と `assets/site.css` が生成される。`out/` 配下は `.gitignore` で追跡対象外。

#### B. v1.3.0 タスク 1〜3 範囲（叩き台）で生成されるページ

| URL パターン | 内容 |
|---|---|
| `/` | サイトトップ。シリーズ一覧をグリッド表示し、本サイトの特徴を紹介 |
| `/about/` | サイト案内・運営者情報・権利表記 |
| `/series/` | 全シリーズ索引（年代順表）。種別・話数併記 |
| `/series/{slug}/` | シリーズ詳細。基本情報・外部 URL・所属エピソード一覧 |
| `/series/{slug}/{seriesEpNo}/` | **エピソード詳細（中核ページ）**。本節 C 参照 |

#### C. エピソード詳細ページの構成（中核）

`/series/{slug}/{seriesEpNo}/` には次の情報を 1 ページに集約する:

1. **サブタイトル表示**: `title_rich_html`（ルビ付き HTML）があればそのまま流す。なければ `title_text` をプレーン表示。下に `title_kana` を補助表示。フォントは Episodes エディタとは合わせず、ブラウザ既定の和文フォントで素朴に表示（指示通り）。
2. **基本情報テーブル**: 放送日時・シリーズ内話数・通算話数・通算放送回・ニチアサ通算放送回、外部 URL（東映あらすじ／ラインナップ）、YouTube 予告埋め込み（`youtube_trailer_url` から ID を抽出して `<iframe>` 化）。
3. **フォーマット表**: `episode_parts` から OA / 配信 / 円盤の各バージョンの「累積開始時刻」と「尺」を併記する 22 パート種別対応の表。各媒体ごとに独立して累積タイムコードを計算し、当該媒体に該当パートが無い場合は空セル（—）扱いにして加算しない。フッタに媒体別の総尺を表示。
4. **サブタイトル文字情報**: Episodes エディタの `BuildTitleInformationPerCharAsync` ロジックを `TitleCharInfoRenderer` として移植したもの。サブタイトル中の登場順ユニーク文字ごとに、`EpisodesRepository.GetFirstUseOfCharAsync` で初出話を、`GetEpisodeUsageCountOfCharAsync` で総使用話数を、`GetTitleCharRevivalStatsAsync` で 1 年以上ぶりの復活情報を取得し、「`「文字」… [初出] [唯一] N年Mか月(P話)ぶりQ回目 『シリーズ』第N話「サブタイトル」(YYYY.M.D)以来`」形式の HTML を生成する。badge は CSS で色分け（初出 = 黄、唯一 = ピンク）。
5. **サブタイトル文字統計**: `episodes.title_char_stats` JSON の `length`（書記素数・コードポイント数・ユニーク書記素数・空白数）と `categories`（漢字 / ひらがな / カタカナ / 英字 / 数字 / 記号 / 句読点 / 絵文字 / その他）をテーブル化。`System.Text.Json` でパースし、JSON が NULL / 異常値のときは黙ってフォールバック（後段タスクでロガー連携予定）。
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

> **注意**: 本叩き台では Catalog 側 `CreditPreviewRenderer` の高度な特殊処理（役職テンプレ展開、絵コンテ・演出融合、声の出演の協力行追記、VOICE_CAST 役職名のカード跨ぎ省略など）は取り込んでいない。あくまで「DB に格納された階層をそのまま見せる素朴版」。これらは v1.3 系の後続タスクで段階的に追加する。

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
  Pages generated  : 1234
    home         : 1
    about        : 1
    series       : 31
    episodes     : 1201
  Warnings         : 5
  Elapsed          : 12.3 sec
```

警告は `BuildLogger.Warn` 経由で集計される。代表的な警告:
- `title_char_stats が未生成` のエピソードが見つかった（`PrecureDataStars.TitleCharStatsJson` で再計算を促す）
- 役職マスタに無い `role_code` がクレジットで参照されている

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
| `hide_storyboard_role` | TINYINT(1) NOT NULL DEFAULT 0 | 「絵コンテ」役職を独立表示せず「演出」と融合表示するか（v1.2.1 追加。プレビュー描画専用フラグ。詳細は変更履歴 v1.2.1 参照） |
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

#### `product_companies` — 商品社名マスタ ★ v1.3.0 stage20 新設

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
| `label_product_company_id` | INT FK NULL | レーベル（→ `product_companies`、ON DELETE SET NULL）★ v1.3.0 stage20 |
| `distributor_product_company_id` | INT FK NULL | 販売元（→ `product_companies`、ON DELETE SET NULL）★ v1.3.0 stage20 |
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
| `catalog_no` | VARCHAR(32) PK | 品番（例: アルバム `MJSA-01000` / シングル `MJSS-09000`） |
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

### v1.3.0 続編 — サイトテンプレートの細部ブラッシュアップ（フッタ書き換え・通算ラベル整理・関連作品セクション分離・他）

v1.3.0 公開直前の最終調整として、SiteBuilder の出力する HTML のうち、シリーズ詳細・エピソード詳細・人物詳細・企業詳細・人物一覧・企業一覧・共通レイアウトの 7 種に対して、表示文言と構造の整理を入れた（バージョン番号は据え置きで v1.3.0 のまま）。SQL スキーマや収集ロジックには変更なし、純粋に表示レイヤーだけの調整。本変更で「公開して人に見せられる状態」を 1 段引き上げる位置づけ。

#### フッタを 4 段落構成に書き換え（`_layout.sbn` + `BuildConfig` + `LayoutModel` + `PageRenderer` + `site.css`）

旧フッタは「© Shota (SHOWTIME). All rights reserved for original works.」と「サークル「SHOWTIME」代表・祥太が個人で運営しています。ABCアニメーション様、東映アニメーション様、その他各社様とは一切関係ございません。」の 2 段だったが、v1.3.0 続編で以下の 4 段落構成に変更した。

```
© 2026 Shota (SHOWTIME).
本サイトは、サークル「SHOWTIME」代表・祥太が個人で運営する非公式ファンサイトです。本サイトは、各公式権利者様とは一切関係ございません。
本サイト内で引用、言及している「プリキュア」シリーズに関する著作権・商標権等の知的財産権は、ABCアニメーション様、東映アニメーション様、および関係各社様に帰属します。
なお、本サイトに掲載されている独自のコンテンツおよびサイトデザイン等に関する著作権のみ、当方に帰属いたします。
```

- 著作権年（1 段目の数字部分）は公開年〜現在年の自動算出。公開年と現在年が同じなら `2026` の単年表記、現在年が進んで以降なら `2026-2027` のような期間表記に切り替わる。
- 公開年は `BuildConfig` の `PublishedYear` プロパティで保持し、App.config の `SitePublishedYear` で上書き可能（既定値 `2026`）。
- `PageRenderer` のコンストラクタで `BuildConfig.PublishedYear` と `DateTime.Now.Year` から表記文字列（`CopyrightYears`）を 1 回算出してキャッシュし、全ページの `LayoutModel.CopyrightYears` に流し込む。`_layout.sbn` 側は `{{ CopyrightYears }}` を埋め込むだけ。
- CSS は `#pageFooter #copyright` に対して、1 段目（`.copyright-line`）は太字 + 等幅数字、2 段目以降（`.disclaimer-line`）は法的免責文として控えめな書体で整える。

#### エピソード詳細の通算情報ラベルを整理（`EpisodeGenerator` + `episode-detail.sbn`）

エピソード詳細「基本情報」セクションの通算情報テーブルで、左ラベルが略記寄りで意味が伝わりづらかったのを「ぱっと見でなにを数えているか分かる」長めの表現に整えた。

| 旧ラベル | 新ラベル |
|---|---|
| 通算（基本情報行見出し）| 通算話数 |
| シリーズ内 | シリーズ内話数 |
| 全シリーズ通算 | 全プリキュアTV通算話数 |
| 通算放送回 | 全プリキュアTV通算放送回数 |
| ニチアサ通算放送回 | 全ニチアサ通算放送回数 |

#### 「いま現在の参照点」キャプションを正式名称化（`EpisodeGenerator.BuildLatestAiredCaption`）

サブタイトル文字情報・パート尺統計情報の説明文末尾に出る「（YYYY年M月D日現在、『○○プリキュア』第N話時点）」のシリーズ名を、`Series.TitleShort ?? Series.Title` から `Series.Title` 単独に変更した。旧仕様では「『プリキュア』第N話時点」のような略記が出てしまい、どのシリーズを指しているのか曖昧になるケースがあったため。

#### シリーズ詳細「外部サイト」セクションを最下部に移動（`series-detail.sbn` / `episode-detail.sbn`）

旧仕様では基本情報の直下に「外部サイト」セクションがあり、ユーザーが自サイトのコンテンツを読み始める前に外部リンクが提示される構造だった。マーケティング観点で「自サイト内コンテンツ → 関連リンク」の順で誘導する方が回遊が伸びるため、外部サイトセクションをページ最下部に移動した。さらに以下を併せて適用：

- リンクはすべて `target="_blank" rel="nofollow noopener noreferrer"` で新規タブ展開。本サイトを閉じずに公式側を確認できる。
- セクション上端に控えめな点線罫線（`.external-links-section { border-top: 1px dotted; }`）を入れて、本文との視覚的な区切りを作る。
- リンク色は控えめの `--accent-blue` で、本文リンクと区別。

#### シリーズ詳細「併映・子作品」と「関連作品」を別セクションに分離（`SeriesGenerator` + `series-detail.sbn`）

旧仕様では `parent_series_id` が当該シリーズを指す全作品を「併映・子作品」というセクション 1 つにまとめていたため、TV シリーズの続編・スピンオフ（SPIN-OFF）・大人向け（OTONA）と、映画併映短編（MOVIE_SHORT）が混在する状態だった。v1.3.0 続編でこれを 2 つのセクションに分離した。

- **「併映・子作品」**：`IsChildOfMovie(s)` が `true` の作品のみ（実質 `kind_code='MOVIE_SHORT'`）。単独詳細ページを持たないので、タイトルだけ平文で表示する純粋な併映情報。
- **「関連作品」**：上記以外の親子関係（TV 続編・SPIN-OFF・OTONA など）。単独ページを持つ作品の集合で、シリーズの広がりを示すリンク集として独立セクションに置く。表示はテーブル形式（`.series-related-table`）にして、タイトルカラムは可変幅、公開日カラム（`col-period`, 14em 固定 + 右寄せ + 折り返し抑止 + 等幅数字）で、長いタイトルでもレイアウトが崩れないようにした。

`SeriesGenerator` の DTO 側では旧 `Related` プロパティを `RelatedChildren` / `RelatedSiblings` の 2 つに分離し、テンプレ側でそれぞれ独立した `<section>` として描画する。

#### エピソード一覧で絵コンテ・演出が同一人物のときは 1 行に統合（`SeriesGenerator.ExtractStaffSummaryAsync` + `series-detail.sbn`）

シリーズ詳細のエピソード一覧（`.ep-list dd.ep-meta` 内）で、絵コンテと演出が同一人物のエピソードは旧仕様では「絵コンテ 伊藤 尚往 / 演出 伊藤 尚往」のように同じ名前が 2 回出ていた。v1.3.0 続編では `ExtractStaffSummaryAsync` の戻り値 `EpisodeStaffSummary` に `StoryboardDirectorMerged` フラグを追加し、絵コンテ・演出の PERSON エントリ重複キー集合が一致（かつ両方非空）の場合だけ true を立てる。テンプレ側はこのフラグを見て：

- `true` → 「絵コンテ・演出 伊藤 尚往」の 1 表記に統合
- `false` → 従来通り「絵コンテ ○○ / 演出 ○○」と 2 行独立に表示

判定は HTML 文字列ではなく重複キー集合の `SetEquals` で行うため、表示揺れ（DisplayTextOverride の有無など）に左右されない。

#### エピソード詳細スタッフセクションの役職ラベルをリンク化（`EpisodeGenerator` + `episode-detail.sbn` + `site.css`）

エピソード詳細の「スタッフ」テーブル（`.staff-table`）で、左 `<th>` の役職ラベル（脚本／絵コンテ／演出／作画監督／美術）が単なるテキストだったのを、役職統計詳細ページ `/stats/roles/{rep_role_code}/` への `<a>` リンクに変えた。代表 role_code の解決は `RoleSuccessorResolver` 経由で行うため、`EpisodeGenerator` のコンストラクタに `RoleSuccessorResolver` を追加で受け取る。

絵コンテ・演出が同一人物で「絵コンテ・演出」の統合ラベルになる行については、「絵コンテ」「演出」をそれぞれ別リンクにする（`StaffRow.SubRoleLinks` に 2 件詰める）。テンプレ側は `SubRoleLinks.Count > 0` のときだけ「・」区切りで個別リンクを描画する分岐を入れた。

CSS は `.staff-table th a` に「本文色 + 点線下線」、`:hover` で「アクセントピンク + 同色下線」のさりげない遷移を入れた。

#### シリーズ一覧サブ行のメインスタッフサマリを正式表記に揃える（`SeriesGenerator.BuildKeyStaffSummaryBySeriesCacheAsync`）

シリーズ一覧の TV シリーズ行に並ぶ「メインスタッフ簡易サマリ」の役職ラベルが旧仕様では `[製作]` `[構成]` `[監督]` `[キャラ]` `[美術]` のような略記だったのを、シリーズ詳細セクションと同じ正式表記（プロデューサー／シリーズ構成／シリーズディレクター／キャラクターデザイン／美術デザイン）に格上げした。区切り文字も `｜` から `　／　` に変更し、フレックスで自然に折り返せるレイアウトに整える。

#### 人物・企業・団体ページの「関与クレジット」→「クレジット履歴」改名（`persons-detail.sbn` / `companies-detail.sbn`）

人物詳細・企業詳細の役職別関与一覧セクションの見出しを、`関与クレジット` から `クレジット履歴` に改名した。表示内容に変更なし、純粋にラベル変更のみ。

#### 人物・企業・団体一覧の「関与話数」→「クレジット話数」改名（`persons-index.sbn` / `companies-index.sbn`）

人物一覧・企業一覧の右端カラム見出しを `関与話数` から `クレジット話数` に改名。「関与」は社内会話寄りの用語で、サイト訪問者には「クレジット話数」の方が直感的に伝わる。

#### 持ち越し項目（次回以降）

以下は v1.3.0 続編のスコープでは未対応で、次回以降のブラッシュアップで対応予定：

- 人物詳細：所属屋号でクレジットされた履歴の併記（「○○（東映アニメーション）」のような所属付き表記）
- 企業詳細：当該企業を所属としてクレジットされた人物名義の「メンバー履歴」セクション追加
- 企業詳細：ブランド & ロゴテーブルのロゴ列下に、ロゴがクレジットされたシリーズ・話数範囲の小書き注記
- 人物・企業一覧：読みカラムの隣に「役職」カラムを追加し、クレジットされた役職を早かった方から列挙
- クレジット階層内（`{THEME_SONGS}` 等）の楽曲・人物・役職のリンク化
- 主題歌・挿入歌のクレジット展開で `SONG_KIND` がコード値（`OP`/`ED`/`INSERT`）のまま出ている問題の修正
- クレジット中の漫画役職・名義のリンク化
- 声の出演下の協力（キャスティング協力）の名義を 2 段目（声の出演の名義カラム位置）に揃える
- クレジット中の「絵コンテ・演出」ラベルから絵コンテと演出をそれぞれ別役職リンクに分割

---

### v1.3.0 — Web 公開用静的サイトジェネレータ `PrecureDataStars.SiteBuilder` の新設

`precure.tv` での Web 公開を見据えて、ローカル MySQL を読み出して静的 HTML 一式を書き出すコンソールアプリ `PrecureDataStars.SiteBuilder` を新設した。AWS S3 への同期や CloudFront 連携は本ツール範囲外で、純粋に「DB → 静的ファイル」変換のみを行う。

#### 新規プロジェクト

`PrecureDataStars.SiteBuilder`（.NET 9 コンソール、`PrecureDataStars.Data` を ProjectReference）。テンプレートエンジンに **Scriban**、`Templates/*.sbn` と `wwwroot/assets/site.css` で見た目を制御する 2 段レンダリング構成（共通レイアウト + コンテンツ）。`App.config` に出力先・ベース URL・サイト名・GA4 / Search Console / AdSense ID を設定し、`dotnet run` 1 発でフルビルドする。

**追加 NuGet 依存**: `Scriban 5.10.0`（テンプレートエンジン）、`System.Configuration.ConfigurationManager 9.0.9`（接続文字列読み取り、既存 Episodes / TitleCharStatsJson と統一）。

#### 出力されるページ

| パス | 内容 |
|---|---|
| `/` | ホームページ。リード文「プリキュアまるごとデータベース。」+ 最終ビルド表記 + データベース統計（11 ボックス、各ボックスは該当一覧ページへリンク。TV シリーズ・映画・スピンオフの 3 ボックスは `/series/` 内のフラグメント（`#tv-series` / `#movies` / `#spinoff`）に直接ジャンプ）+ 今日の記念日（JS 動的計算、`wwwroot/assets/anniversaries.js`）+ 今後の放送予定 + 最新エピソード + 音楽商品の発売予定 + 新着の音楽商品 |
| `/about/` | サイト案内 |
| `/series/` | 全シリーズ索引。TV シリーズ・映画・大人向けスピンオフ・ショートアニメ・イベント・スピンオフの最大 6 セクション構成（内容 0 件のセクションは非表示）。各セクションは `id` 属性付きでホームの統計ボックスから直接アンカー遷移可能（`#tv-series` / `#movies` / `#otona` / `#short-anime` / `#event` / `#spinoff`）。TV シリーズは放送開始順に純粋な TV だけを並べる。映画は親作品（`kind_code IN ('MOVIE','SPRING')` すべて、`parent_series_id` の有無不問）を公開順に親として並べ、各親映画の下に `kind_code='MOVIE_SHORT'` の併映短編を `seq_in_parent` 昇順で字下げ表示。親+子の `run_time_seconds` 合計を TV の「全N話」と同じ列位置に「m分ss秒」で表示（いずれかが NULL なら空欄）。親映画タイトル先頭には `[秋映画]`（紅葉系オートゥムカラー）/ `[春映画]`（桜ピンク）のシーズンバッジを付与。スピンオフ系は OTONA → SHORT → EVENT → SPIN-OFF の順で 4 セクションに細分化（v1.3.0 公開直前のデザイン整理第 3 弾）、各セクションは TV と同じ「タイトル / 期間 / 全N話」表構成。子作品（MOVIE_SHORT）の単独詳細ページは生成しない |
| `/series/{slug}/` | シリーズ詳細。エピソード一覧（dl 形式）、メインスタッフ 5 役職、劇伴一覧表（M 番号 / メニュータイトル / 作曲 / 編曲 / 尺 / セッション / 使用回数の 7 列、`is_temp_m_no=1` の仮 M 番号は Web 非表示）、主要スタッフ表（脚本・絵コンテ・演出・作画監督・美術監督ら役職別）、プリキュアセクション（v1.3.0 公開直前のデザイン整理第 4 弾で追加、`series_precures` 多対多関連テーブル経由で取得、変身前 / 変身後 / 声優の 3 列） |
| `/series/{slug}/{seriesEpNo}/` | エピソード詳細（サイトの中核ページ）。サブタイトル（ルビ付き）、フォーマット表（OA / 配信(Amazon Prime) / Blu-ray/DVD のタイムコード）、サブタイトル文字情報（初出・唯一・「N年Mか月ぶり」）、文字統計、パート尺の偏差値、主題歌、使用音声、クレジット階層、前後話ナビ（上下にカード型の「← 前話 / 次話 →」+ 数字のみのページネーション）、YouTube 予告埋め込み |
| `/episodes/` | エピソード一覧ランディング（v1.3.0 公開直前のデザイン整理で新設）。全 TV シリーズのエピソードをシリーズ別の `<details>` セクションで折り畳み一覧化。話数 + サブタイトル + 放送日のシンプルな縦リスト。ホームのデータベース統計セクション「エピソード」ボックスの遷移先 |
| `/persons/` `/persons/{personId}/` | 人物軸。50 音順索引と、人物詳細（旧姓・別名義・ユニット名義を `person_alias_persons` で合算、声優関与は演じたキャラクター名併記） |
| `/companies/` `/companies/{companyId}/` | 企業軸。同様に 50 音順索引と、企業ごとの全屋号・配下ロゴを合算した詳細 |
| `/precures/` `/precures/{precureId}/` | プリキュア軸。`precure_id` 昇順（≒登場年代順）索引と、4 名義（変身前 / 変身後 / 変身後 2 / 別形態）を集めた詳細（誕生日「M月D日」・声優・学校・学年/組・家業を併記）。**肌色情報は表示しない**（センシティブな研究情報のため、内部データとしては保持するが Web には載せない運用） |
| `/characters/` `/characters/{characterId}/` | キャラクター軸。`character_kinds.display_order` 順でセクション分け（プリキュア / 仲間たち / 敵 / とりまく人々）、各セクション内 50 音順。詳細では声の出演履歴 + 表記揺れ一覧 + 家族関係（`character_family_relations` を続柄ラベル付きで表示） |
| `/products/` `/products/{product_catalog_no}/` | 商品（CD / Blu-ray / DVD）。`product_kinds.display_order` 順でセクション分け、各セクション内は発売日昇順。詳細ではディスクごとのトラック表 |
| `/songs/` `/songs/{song_id}/` | 楽曲。シリーズ放送順 + 楽曲 song_id 順 + 録音バリエーションをサブ行表示の索引と、収録商品 + 主題歌使用エピソードを併記する詳細 |
| `/music/` | 音楽カテゴリランディング（歌・劇伴・商品の 3 カード） |
| `/bgms/` `/bgms/{slug}/` | 劇伴の索引・詳細 |
| `/stats/` | 統計ランディング。役職別 / 声優 / サブタイトル / 尺の 4 入口 |
| `/stats/roles/` `/stats/roles/{role_code}/` | 役職別ランキング索引と詳細（VOICE_CAST 系を除く）。`roles.successor_role_code` クラスタで集約、URL 代表 role_code |
| `/stats/roles/all-persons/` `/stats/roles/all-companies/` | 人物・企業の総合ランキング（TOP 100、複数役職兼任は 1 回扱い、上位 5 件の役職内訳併記） |
| `/stats/voice-cast/` | 声優ランキング。`characters.character_kind` で 3 セクション振り分け（PRECURE→メイン、ALLY/VILLAIN→サブ、SUPPORTING→ゲスト）、上位 5 件のキャラ名併記 |
| `/stats/subtitles/` 配下 | 使用文字 TOP 100（全文字 / 漢字限定の 2 タブ）、文字数ランキング、漢字率ランキング、シリーズ別文字種別比率 |
| `/stats/episodes/` 配下 | A/B パート尺ランキング、中 CM 入り時刻ランキング、シリーズ × パート別の平均/最短/最長尺 |
| `/sitemap.xml` `/robots.txt` | SEO 用。`SiteBaseUrl` が App.config 未設定なら sitemap.xml はスキップ |
| `/search-index.json` | サイト内検索の静的 JSON インデックス（全 8 種：シリーズ・エピソード・プリキュア・キャラ・人物・企業・楽曲・商品） |

順位はすべて Wimbledon 形式（同点同順、次は同点者数だけ飛ばす：1, 2, 2, 4, ...）。集計のシリーズ範囲は TV のみ。

#### クレジット階層の HTML 化

エピソード詳細・シリーズ詳細のクレジットセクションは、Catalog 側 `CreditPreviewRenderer` の表示を全面移植して下記をすべて再現する：

- **役職テンプレ DSL の展開**：`role_templates` から `(role_code, series_id) → (role_code, NULL)` フォールバックで取得し、`{ROLE_NAME}` `{#BLOCKS}` `{#THEME_SONGS:opts}` を含む DSL を `RoleTemplateRenderer` で評価
- **フォールバック表**：`fallback-table` / `fallback-vc-table` の 3 カラム形式で「役職名（左固定幅）+ ブロック内エントリを `col_count` で横並び」
- **絵コンテ・演出融合表示**：`series.hide_storyboard_role=1` のシリーズで条件成立時に「（絵コンテ・）演出 名前」または「演出 名前A（絵コンテ）/ 名前B（演出）」を 1 ブロックで描画
- **VOICE_CAST 役職名のカード跨ぎ抑止** / **`CASTING_COOPERATION` の「協力」末尾追記**（VC テーブル末尾の 3 セル構成） / **leading_company の字下げ** / **`is_broadcast_only=1` エントリの除外**

クレジット内のあらゆる表示要素は詳細ページにリンク化される：役職名 → `/stats/roles/{role_code}/`（VOICE_CAST 系は `/stats/voice-cast/`）、人物名義 → `/persons/{person_id}/`（共有名義は `StaffNameLinkResolver` 経由で添字付き複数リンク化）、企業屋号 → `/companies/{company_id}/`、ロゴ → 屋号名に置換した上で `/companies/{company_id}/` へ。

人物・企業・プリキュア・キャラの軸ページは、起動時に 1 回だけ構築する **`CreditInvolvementIndex`** が全クレジット階層を走査して `person_alias_id` / `company_alias_id` / `logo_id` / `character_alias_id` から逆引きできるインデックスをメモリ上に構築し、各 Generator で共有する設計（複数 Generator が個別に階層走査すると DB 負荷が大きいため）。`Involvement` クラスは `PersonAliasId` プロパティを持ち、キャラ軸からの逆引き結果から声優名（`person_aliases.display_text_override` または `name`）を解決可能。

#### エピソード×劇伴・歌の使用箇所紐付テーブル `episode_uses` の新設

エピソードのパート内で流れた音声（歌・劇伴・ドラマパート・ラジオ・ジングル・その他）を記録するためのテーブル。`tracks`（discs 配下）と同じ流儀で `content_kind_code` により参照列を切り替える：

- `SONG` → `song_recordings`
- `BGM` → `bgm_cues`
- テキスト系（`DRAMA` / `RADIO` / `JINGLE` / `OTHER`）→ `use_title_override` テキスト

複合 PK は `(episode_id, part_kind, use_order, sub_order)` で、メドレー的に複数曲が連続するケースも `sub_order` で表現可能。整合性は `tracks` と同じ流儀で `BEFORE INSERT` / `BEFORE UPDATE` トリガで担保。マイグレーションスクリプトは `db/migrations/v1.3.0_add_episode_uses.sql`。

`PrecureDataStars.Data` 側に `EpisodeUse` モデルと `EpisodeUsesRepository`（`GetAllAsync` / `GetByEpisodeAsync` / `GetByBgmCueAsync` / `GetBySongRecordingAsync` / `ReplaceAllForEpisodeAsync` の 5 API）を追加。SiteBuilder のエピソード詳細に「使用音声」セクション（パート別グルーピング、6 列テーブル）を追加し、シリーズ詳細の劇伴一覧表に「使用回数」列も追加（春映画・秋映画で本編 BGM が流用されるケースに対応するため、当該シリーズだけでなく全シリーズで使われた回数を含める）。

#### スキーマ変更（v1.3.0 マイグレーション）

| マイグレーション | 内容 |
|---|---|
| `v1.3.0_add_roles_successor.sql` | `roles.successor_role_code` 列追加。役職別ランキングを系譜統合する基盤（同一クラスタは URL 代表 role_code に集約） |
| `v1.3.0_add_bgm_cues_seq_in_session.sql` | `bgm_cues.seq_in_session` 追加（録音セッション内の収録順） |
| `v1.3.0_rename_episode_theme_songs_seq.sql` | `episode_theme_songs` の seq 系列名整理 |
| `v1.3.0_seed_theme_song_roles.sql` | 主題歌 5 役職（LYRICS / COMPOSITION / ARRANGEMENT / VOCALS / LABEL）の seed |
| `v1.3.0_add_episodes_duration_minutes.sql` | `episodes.duration_minutes TINYINT UNSIGNED NULL` を `on_air_at` の直後に追加。既存 TV 全エピソードに 30 をバックフィル。エピソード詳細の放送日時を「2004年2月1日 8:30〜9:00」フォーマットに（尺未登録時は終了時刻を省略） |
| `v1.3.0_add_episode_uses.sql` | `episode_uses` テーブル新設（前項参照） |

#### SEO・アナリティクス

`_layout.sbn` に下記を埋め込む：

- **Open Graph Protocol**：`og:url` / `og:type` / `og:site_name` / `og:locale=ja_JP` / `og:title` / `og:description` / `og:image` + `twitter:card`
- **JSON-LD**（Schema.org 構造化データ）：ホーム=`WebSite`、TV シリーズ=`TVSeries`、映画=`Movie`、エピソード=`TVEpisode`（親シリーズを `partOfSeries` で入れ子）、人物=`Person`（名義を `alternateName` 配列）、企業=`Organization`（屋号を `alternateName` 配列、`foundingDate` / `dissolutionDate` も埋め込み）、商品=`Product` または音楽系種別なら `MusicAlbum`（`recordLabel` 付き）、楽曲=`MusicComposition`（`lyricist` / `composer` を `Person` ノードで入れ子）の各種別で出し分け
- **Google Analytics 4**：`gtag.js` のローダ + `gtag('config', '<MeasurementId>')` の 2 段
- **Google Search Console**：所有権確認メタタグ
- **Google AdSense 自動広告**：App.config の `GoogleAdSenseClientId` 設定時のみ `adsbygoogle.js` を出力

GA4 メジャメント ID / Search Console トークン / AdSense クライアント ID は App.config の `Ga4MeasurementId` / `GoogleSiteVerification` / `GoogleAdSenseClientId` から読み込み、未設定（空文字）の場合はそれぞれの埋め込みを丸ごと省略する（公開直前まで未設定のまま運用しても害が無い設計）。`og:image` は当面個別画像の指定を持たず空文字運用とし、画像が無いページでは `twitter:card=summary`、画像があるページでは `twitter:card=summary_large_image` に切り替える条件分岐のみ実装済み。

#### サイト内検索（クライアント側 JS）

ビルド時に `/search-index.json` を出力（全 8 種：シリーズ・エピソード・プリキュア・キャラ・人物・企業・楽曲・商品。各アイテムは URL / タイトル / 種別 / サブテキスト / 正規化済み読みの 5 キー）。ヘッダ右端の検索ボックスから JS が初回入力時に fetch、メモリ上で部分一致 + AND 検索する。クエリは正規化（全角カナ→ひらがな、英大文字→小文字、空白除去）してマッチング。結果は最大 20 件、種別バッジ付きのドロップダウンで表示し、↓↑ で候補移動・Enter で選択・Esc で閉じる。フレームワーク非依存・依存ライブラリなしの素 JS で実装し、AWS S3 等での配信を想定した完全静的サイト構成を維持する。

#### スタイル・レイアウト

- サイト全体のフォントは Google Fonts の **Kiwi Maru**、数字部分は等幅フォント（`--font-num` = JetBrains Mono / Consolas / Menlo / Roboto Mono）でフォーマット表・パート尺統計・ページネーションが縦に揃う
- プリキュア風カラフルアクセント変数（pink / blue / yellow / green / purple）を CSS 変数で定義、見出し階層アクセントに利用
- リンク色を `var(--fg)`（本文色）に統一し、ホバー時のみ下線
- フッター（`<footer id="pageFooter">`）は 2 段構成：`<section id="contact">`（お問い合わせ：X: @shota_ / mailto:shota@precure.tv）と `<div id="copyright">`（© Shota (SHOWTIME) の著作権表記 + サークル『SHOWTIME』代表・祥太が個人で運営している旨と ABCアニメーション様・東映アニメーション様その他各社様とは一切関係ございません旨の免責事項）を縦に並べる
- 話数ページネーション（エピソード詳細ページ）は v1.3.0 公開直前のデザイン整理で再構成：上下それぞれに「前後話のおしゃれカード型リンク（← 前話 #N サブタイトル / 次話 #N サブタイトル →）」+「数字のみのシンプルなページネーション（« 1 … 10 11 [12] 13 14 … 50 »）」の 2 段ナビを配置
- パート尺統計表は 2 段ヘッダ（上段で「『○○プリキュア』 / 歴代プリキュア全体」を colspan=2 でグルーピング、下段で「順位 / 偏差値」を並べる）
- 映画シリーズ一覧では親映画タイトル先頭にシーズンバッジ（秋映画＝紅葉系オートゥムカラー、春映画＝桜ピンク）を表示し、シリーズ種別を一目で判別できるようにする

#### ユーティリティ追加

`PrecureDataStars.SiteBuilder/Utilities/` に以下を追加：

- `EpisodeRangeCompressor`：エピソード範囲表記（#1〜4, 8 圧縮、全話一致時は (全話) 付加）
- `CompanyKanaNormalizer`：企業名 50 音ソート用の読み正規化
- `MNoNaturalComparer`：M 番号の自然順比較（"M1" < "M2" < "M10"、枝番無し優先）
- `RoleSuccessorResolver`：`roles.successor_role_code` クラスタの代表 role_code 解決
- `PathUtil` の各種 URL 生成関数（`PrecureUrl(int)` / `CharacterUrl(int)` / `ProductUrl(string)`（`Uri.EscapeDataString` で URL エスケープ）/ `SongUrl(int)`）

#### TemplateRendering 共通プロジェクトの分離（重複コード解消）

役職テンプレ DSL（v1.2.0 工程 H で導入）の展開エンジンは、SiteBuilder 新設時に Catalog 側 `Forms/TemplateRendering/` 配下の 5 ファイルを SiteBuilder 側 `TemplateRendering/` にコピー利用する形で立ち上げた。実装本体は両者で完全に同一だったため、その後の改修で片方だけ修正して片方が陳腐化するリスクを抱えていた。v1.3.0 ブラッシュアップで本コードを共通プロジェクト **`PrecureDataStars.TemplateRendering`** に集約し、片方更新で済む構造に統一した。

**新規プロジェクト**：`PrecureDataStars.TemplateRendering`（`net9.0`、Forms 非依存）。`PrecureDataStars.Data` を ProjectReference し、`Dapper` を PackageReference する。Catalog（`net9.0-windows`、WinForms）と SiteBuilder（`net9.0`、コンソール）の双方が ProjectReference する形で参照。

**新規ファイル**：

- `ILookupCache.cs` — テンプレ展開エンジンが必要とする最小限の参照解決インターフェース（`LookupPersonAliasNameAsync` / `LookupCompanyAliasNameAsync` / `LookupLogoNameAsync` の 3 メソッドのみ）。Catalog 側 `LookupCache`（GUI のメモリキャッシュ機構付き）と SiteBuilder 側 `LookupCache`（ビルド 1 回限りのオンメモリキャッシュ）の両方が本インターフェースを実装することで、`RoleTemplateRenderer` 1 本のコードで両環境を扱える。

**移動ファイル**（旧 Catalog 側 / SiteBuilder 側の同名ファイル群を削除して、本プロジェクトに統合）：

- `TemplateContext.cs`（`internal sealed` → `public sealed` に昇格）
- `TemplateNode.cs`
- `TemplateParser.cs`
- `RoleTemplateRenderer.cs`（`internal static` → `public static`、`RenderAsync` 引数の `LookupCache lookup` → `ILookupCache lookup` に変更）
- `Handlers/ThemeSongsHandler.cs`（`internal static` → `public static`）

**呼び出し側の修正**：

- `PrecureDataStars.Catalog/Forms/LookupCache.cs`：クラス宣言に `: ILookupCache` を追加
- `PrecureDataStars.SiteBuilder/Rendering/LookupCache.cs`：同上
- `PrecureDataStars.Catalog/Forms/Preview/CreditPreviewRenderer.cs`：using を `PrecureDataStars.Catalog.Forms.TemplateRendering` から `PrecureDataStars.TemplateRendering` に変更
- `PrecureDataStars.SiteBuilder/Rendering/CreditTreeRenderer.cs`：using を `PrecureDataStars.SiteBuilder.TemplateRendering` から `PrecureDataStars.TemplateRendering` に変更
- 両プロジェクトの `.csproj` に `PrecureDataStars.TemplateRendering` への ProjectReference を追加
- ソリューションファイル `precure-datastars-wintools.sln` に新プロジェクトを登録

呼び出し時は `_lookup`（具象 `LookupCache`）を `RoleTemplateRenderer.RenderAsync(..., lookup: _lookup, ...)` 引数（`ILookupCache`）に渡すだけで暗黙アップキャストにより通る。両 `LookupCache` の `LookupPersonAliasNameAsync` / `LookupCompanyAliasNameAsync` / `LookupLogoNameAsync` のシグネチャはもともと完全一致していたため、既存メソッドがそのままインターフェース実装になり、メソッド本体の修正は発生しない。

#### 設計上の意思決定

- 楽曲のクレジット情報は **構造化テーブル（`song_credits` / `song_recording_singers` / `bgm_cue_credits`）** が存在するものだけ `CreditInvolvementIndex` に反映する。フリーテキスト列（`_name` 系列）は楽曲詳細で表示は継続するが、人物・企業の関与集計には載せない（マスタ駆動の堅実な紐付けに限定）。Catalog 側 `MusicCreditsMigrationForm` で構造化に移行された名義は v1.3.0 ブラッシュアップ stage 16 Phase 2 から人物軸ページに「作詞 / 作曲 / 編曲 / 歌唱 / 劇伴作曲 / 劇伴編曲」セクションとして表示される
- 子作品（`parent_series_id` 持ちで SPIN-OFF 以外）は単独詳細ページを生成しない
- `bgm_cues` の仮 M 番号（`is_temp_m_no=1`）は Web 非表示、ただし統計はカウント対象
- 統計のシリーズ集計対象は TV のみ
- 用語：表示は「企業 → 企業・団体」「屋号 → ブランド」、英語表記は表示しない（DB は持つ）
- 内部データ・URL は変更しない
- 全話担当者：名前のみ、部分担当者：名前 (#1〜4)
- 役職別ランキング系譜統合：`roles.successor_role_code` クラスタで集約、URL 代表 role_code

DB スキーマには破壊的変更なし。既存の Catalog / Episodes ツールはそのまま動作する。

---

### v1.3.0 公開直前のデザイン整理（Web 生成サイトのナビゲーション・シリーズ一覧・エピソード詳細の刷新）

リリース前 v1.3.0 の最終仕上げ。バージョン据え置き、DB スキーマ変更なし。`PrecureDataStars.SiteBuilder` の出力 HTML のみを対象としたデザイン・情報設計の整理で、Catalog / Episodes ツールは無関係。

#### シリーズ一覧 `/series/` の刷新

旧仕様の「TV シリーズを縦軸とし、TV を親とする秋映画／春映画／併映短編を字下げ表示」する単一テーブル構造を廃止し、**TV シリーズ・映画・スピンオフの 3 セクション構成**に再編した。

- **TV シリーズセクション**：純粋に TV シリーズだけを放送開始順に連番付きで並べる。子作品（併映短編・子映画）の字下げ表示はここから撤去し、映画セクションに移管した。
- **映画セクション**：親 TV を持たない単独映画系（`parent_series_id=NULL` の MOVIE / SPRING）を公開順に親作品として並べ、各親映画の下にぶら下がる子作品（併映短編・子映画）を字下げ表示する。子作品はリンクなしテキスト表示（単独詳細ページを生成しない既存方針を踏襲）、公開日は親と同じため省略。
- **スピンオフセクション**：`SPIN-OFF` 種別だけを放送順に連番付きで並べる。セクション見出しから自明なので `[スピンオフ]` のテキストラベルは付けない。

親映画タイトル先頭に**シーズンバッジ**を追加：

- **`[秋映画]`**：紅葉系オートゥムカラーのグラデーション（`#d97706 → #c2410c`、白文字、境界 `#b45309`）。
- **`[春映画]`**：桜ピンクのグラデーション（`#f472b6 → #ec4899`、白文字、境界 `#db2777`）。サイトのアクセントピンクと近い色味で統一感を出しつつ、秋映画と濃度を揃えて並び時の見映えを担保。

実装：`Generators/SeriesGenerator.cs` の `GenerateIndex()` を全面書き換え。旧 `TvSeriesRow.Children` プロパティは廃止し、映画セクション用に新 DTO `MovieSeriesRow`（`SeasonBadgeClass` / `SeasonBadgeLabel` / `Children` を保持）を追加。スピンオフは既存 `TvSeriesRow` を共用する（同じ連番付き表形式で描画するため）。テンプレ `Templates/series-index.sbn` も全面書き換え。スタイルは `.movie-badge` / `.movie-badge-fall` / `.movie-badge-spring` / `.series-movie-table` / `.series-spinoff-table` を `site.css` に追加。

#### エピソード詳細 `/series/{slug}/{seriesEpNo}/` の前後話ナビ再構成

旧仕様の「ページネーション端ボタンに前話・次話のサブタイトルを長く詰める（`« 前話サブタイトル ... 1 5 6 (7) 8 9 ... 50 次話サブタイトル »`）」表記を廃止し、以下の 2 段構成に再編した：

1. **前後話おしゃれカード型リンク**：上下それぞれの直上に、左に「← 前話 #N サブタイトル」右に「次話 #N サブタイトル →」の枠付きカード型リンクを配置。ホバーでピンクの差し色 + 軽い浮き上がりエフェクト、`max-width:50%` で両側拮抗、サブタイトルが長い場合は省略表示。スマホ幅（≤560px）では縦積みに自動切り替え。最初／最終話で対応がない側は `.disabled` 状態（薄字 + 灰色枠）で「なし」と表示する。
2. **数字のみのシンプルなページネーション**：上下それぞれの前後話カードの直下に、純粋な数字ベースの「`« 1 ... 5 6 (7) 8 9 ... 50 »`」を配置。端ボタンの `«` / `»` は前話／次話へのリンクのみで、サブタイトルは含めない（カードに分離したため）。

加えて、エピソード詳細冒頭の **`CoverageLabel`（サイト全体カバレッジ表記）の表示を削除**した。エピソード詳細はサブタイトル文字情報・パート尺統計の各セクション末尾に出る `BuildPointCaption`（参照点キャプション）だけで「いま現在の参照点」を案内する方針へ。基本情報・クレジット・主題歌・使用音声などの他要素は登録の有無で表示が切り替わるだけでカバレッジ概念が当てはまらないため、ページ全体宣言は不要。

実装：`Templates/episode-detail.sbn` を全面書き換え。`EpisodeContentModel` の `Pagination` / `PrevUrl` / `NextUrl` / `PrevPagerLabel` / `NextPagerLabel` プロパティはそのまま流用（既存 `BuildPagination` ロジックも変更不要）。スタイルは `.ep-prev-next-cards` / `.ep-prev-next-card` / `.ep-prev-next-direction` / `.ep-prev-next-label` 一式を `site.css` に追加。

#### ホーム `/` のレイアウト整理

リード文を簡潔化：

- 旧：「プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断検索できる非公式データベース。」
- 新：**「プリキュアまるごとデータベース。」**

データベース統計セクションの位置を**ページ末尾から hero / BuildLabel 直下に移動**し、サイトのファーストビューで全体規模が即座に把握できるようにした。あわせてセクション見出し（`<h2>データベース統計</h2>`）は廃止し、見出し無しで 11 ボックスをコンパクトに並べる構成へ。

11 ボックスを `<a>` でラップして該当一覧ページへの遷移リンクを付与：

| ボックス | 遷移先 |
|---|---|
| TVシリーズ / 映画 / スピンオフ | `/series/` |
| エピソード | `/episodes/`（新設） |
| プリキュア | `/precures/` |
| キャラクター | `/characters/` |
| 歌 | `/songs/` |
| 劇伴 | `/bgms/` |
| 音楽商品 | `/products/` |
| 人物 | `/persons/` |
| 企業・団体 | `/companies/` |

リンクラッパー `.home-db-stats-link` はホバー時に背景がピンクの淡色に切り替わり、統計値もピンクのアクセント色に変化する。

実装：`Templates/home.sbn` を書き換え（`HomeGenerator.cs` は無変更、既存 DbStats モデルをそのまま流用）。

#### エピソード一覧ランディング `/episodes/` の新設

ホームのデータベース統計セクションで「エピソード」ボックスをクリックしたときの遷移先として、**全 TV シリーズのエピソードをシリーズ別セクションで折り畳み一覧化する単一ページ**を新設。

- TV シリーズに限定して放送開始順に並べ、各シリーズを `<details>` で折り畳み可能な 1 セクションとして描画。初期状態では全シリーズを閉じる（総エピソード数が 1000 を超える運用想定のため、描画コストとスクロール量を抑制）。
- `<summary>` 行にはシリーズタイトル + 放送期間 + 話数ラベルを並べ、開かなくても概要が読めるようにする。展開時にはピンクの差し色で `<summary>` 背景と枠色がアクティブ感を出す。
- 各エピソード行は「話数（第N話）+ サブタイトル（ルビ付き優先）+ 放送日（曜日付き）」のシンプルな 3 段グリッド。スマホ幅（≤640px）では縦積みに自動切り替え。
- 詳細スタッフ情報はシリーズ詳細・エピソード詳細に委ねる。このランディングは話数の概観と各話詳細への動線提供に専念する。

実装：

- `Generators/EpisodesIndexGenerator.cs` を新設（同期生成、`BuildContext` と `PageRenderer` のみに依存）。
- `Templates/episodes-index.sbn` を新設。
- `Pipeline/SiteBuilderPipeline.cs` に `EpisodesIndexGenerator` を登録。`EpisodeGenerator` が全エピソード詳細を書き終えた後に走らせて、内部リンクの妥当性を担保する。
- スタイルは `.episodes-index-section` / `.episodes-index-summary` / `.episodes-index-list` 一式を `site.css` に追加。
- ナビゲーションには追加しない（ナビは既存通り：シリーズ / プリキュア / キャラクター / 音楽 / 人物 / 企業・団体 / 統計 / このサイトについて）。
- `search-index.json` への登録は見送り（エピソード個別ページが全件登録されているため、ランディングは重複情報になる）。
- `sitemap.xml` には登録される（クロール導線として、既存の `PageRenderer.WrittenPages` 経由で自動的に拾われる）。

#### フッタの構造刷新

旧 `<footer class="site-footer">` 構造（連絡先・著作権・免責の 3 段構成）を廃止し、**`<footer id="pageFooter">`** 構造（**お問い合わせ + Copyright + 免責の 2 部構成**）へ差し替え：

```html
<footer id="pageFooter">
  <section id="contact">
    <h2 data-i18n="contact_h2">お問い合わせ</h2>
    <p>
      <a href="https://x.com/shota_" target="_blank">X: @shota_</a>
      <a href="mailto:shota@precure.tv">shota@precure.tv</a>
    </p>
  </section>
  <div id="copyright">
    <p>© Shota (SHOWTIME). All rights reserved for original works.</p>
    <p data-i18n="disclaimer">サークル「SHOWTIME」代表・祥太が個人で運営しています。ABCアニメーション様、東映アニメーション様、その他各社様とは一切関係ございません。</p>
  </div>
</footer>
```

`data-i18n` 属性は将来の多言語化のためのプレースホルダ（現時点で参照する JS は無い）。実装は `Templates/_layout.sbn` のフッタ部分のみ書き換え。スタイルは別途 `#pageFooter` ブロックで定義する（site.css 側の旧 `.site-footer` セレクタは残置するが、HTML 出力からは出なくなるため実害なし）。

#### 影響ファイル一覧

| 区分 | パス | 変更内容 |
|---|---|---|
| Generator | `PrecureDataStars.SiteBuilder/Generators/SeriesGenerator.cs` | `TvSeriesRow.Children` 廃止、`MovieSeriesRow` 新設、`GetSeasonBadgeClass` / `GetSeasonBadgeLabel` 追加、`GenerateIndex()` の TV / 映画 / スピンオフ 3 セクション再編 |
| Generator | `PrecureDataStars.SiteBuilder/Generators/EpisodesIndexGenerator.cs` | 新規追加（エピソード一覧ランディング `/episodes/` の生成） |
| Pipeline | `PrecureDataStars.SiteBuilder/Pipeline/SiteBuilderPipeline.cs` | `EpisodesIndexGenerator` を `EpisodeGenerator` の後に登録 |
| Template | `PrecureDataStars.SiteBuilder/Templates/series-index.sbn` | 全面書き換え（3 セクション構成 + 映画バッジ） |
| Template | `PrecureDataStars.SiteBuilder/Templates/episode-detail.sbn` | `CoverageLabel` 削除、前後話おしゃれカード追加（上下）、ページネーションを数字のみのシンプル版に戻す |
| Template | `PrecureDataStars.SiteBuilder/Templates/home.sbn` | lead 文簡潔化、データベース統計を hero 直下に移動、見出し削除、11 ボックスをリンク化 |
| Template | `PrecureDataStars.SiteBuilder/Templates/episodes-index.sbn` | 新規追加 |
| Template | `PrecureDataStars.SiteBuilder/Templates/_layout.sbn` | フッタを `<footer id="pageFooter">` 構造（contact + copyright の 2 部構成）に差し替え |
| Asset | `PrecureDataStars.SiteBuilder/wwwroot/assets/site.css` | 末尾に追加スタイル（`.movie-badge` 系・`.ep-prev-next-cards` 系・`.home-db-stats-link`・`.episodes-index-section` 系） |

DB スキーマには変更なし。`HomeGenerator.cs` も無変更（既存 DbStats モデルをテンプレ側でリンク化するだけで済む設計に揃えた）。

---

### v1.3.0 公開直前のデザイン整理（第 2 弾：エピソード一覧の Scriban ループ上限対応・映画親子分類の精密化・劇伴詳細の仮 M 番号と収録盤情報）

バージョン据え置き、DB スキーマ変更なし。第 1 弾投入後のフィードバックを受けて以下を整理する。

#### Scriban `LoopLimit` の引き上げ（`/episodes/` ランディング 404 問題の解消）

第 1 弾で新設した `/episodes/` エピソード一覧ランディングは、エピソード総数が 1000 を超えるシリーズ運用下で **Scriban 既定の `TemplateContext.LoopLimit` = 1000** に抵触し、`"Exceeding number of iteration limit 1000 for loop statement"` でレンダリングが失敗してファイルが生成されなかった（結果として該当 URL が 404 になっていた）。

`Rendering/ScribanRenderer.cs` の `TemplateContext` 構築箇所で `LoopLimit = 1_000_000` を明示する：

```csharp
var context = new TemplateContext
{
    TemplateLoader = _loader,
    MemberRenamer = m => m.Name,
    MemberFilter = m => true,
    LoopLimit = 1_000_000  // 既定 1000 → 100 万に引き上げ
};
```

この値は「テンプレ 1 回のレンダリング中の累積ループ反復回数」の上限。`/episodes/` ランディングは「TVシリーズ数 + 全 TV エピソード数」分のループを 1 ページで回すため、現状の運用規模（エピソード約 1200 + シリーズ 20 強）に対して十分な余裕がある。本変更で `/episodes/` のレンダリングが通り、出力ファイル `episodes/index.html` が正しく書き出されるようになる。

#### シリーズ一覧 / 映画セクションの親子分類仕様の精密化

「映画」セクションの親子配列ルールを以下のとおり明確化した：

- 映画系シリーズの定義：`kind_code IN ('MOVIE','SPRING','MOVIE_SHORT')` の 3 種
- **親として表示** = `kind_code IN ('MOVIE','SPRING')` のみ。`parent_series_id` の有無（TV シリーズを親に持つかどうか）にかかわらず、これらは独立した親作品として映画セクションに公開順で並ぶ。旧仕様の「TV を親とする MOVIE は TV の下に字下げ表示」は廃止済み（第 1 弾で TV 一覧から子作品の字下げは撤廃され、第 2 弾で映画一覧の側でも全 MOVIE / SPRING が独立配置となる）
- **子として親の下に字下げ表示** = `kind_code = 'MOVIE_SHORT'` のみ。`seq_in_parent` 昇順で並べ、リンクなしテキスト表示（単独詳細ページは生成しない）
- 親+子の `run_time_seconds` 合計を TV の「全N話」と同じ列位置に「m分ss秒」形式で表示。親または子のいずれかに NULL（未登録）が 1 件でも混じる場合は列を空欄にする

第 1 弾で導入した `MovieSeriesRow` DTO に `RuntimeLabel` プロパティを追加して尺合計ラベルを保持。`SeriesGenerator.GenerateIndex` の親子集約ロジックは以下のとおり再構成：

```csharp
// 子は MOVIE_SHORT のみ、seq_in_parent 昇順
var movieShortByParent = _ctx.Series
    .Where(s => s.KindCode == "MOVIE_SHORT" && s.ParentSeriesId.HasValue)
    .GroupBy(s => s.ParentSeriesId!.Value)
    .ToDictionary(g => g.Key, g => g
        .OrderBy(c => c.SeqInParent ?? byte.MaxValue)
        .ThenBy(c => c.SeriesId)
        .ToList());

// 親は MOVIE / SPRING すべて（parent の有無不問）、公開順
var movieRows = _ctx.Series
    .Where(s => s.KindCode == "MOVIE" || s.KindCode == "SPRING")
    .OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId)
    .Select(...)
    .ToList();
```

子作品判定の共通ヘルパ `IsChildOfMovie(Series)` も `kind_code == 'MOVIE_SHORT'` 一発判定に簡素化（旧実装は「parent あり + SPIN-OFF 以外」だった）。`SeriesGenerator` と `MusicGenerator` の双方が同じロジックを使う。

#### ホーム統計ボックスのアンカー遷移対応

第 1 弾で TVシリーズ / 映画 / スピンオフのボックスはすべて `/series/` に飛ぶようリンク化したが、フラグメント無しのため遷移先で常に TV シリーズセクションに着地していた。各セクションに `id` 属性を付与し、ホームのリンクもフラグメント付きに修正した：

| ボックス | リンク先 |
|---|---|
| TVシリーズ | `/series/#tv-series` |
| 映画 | `/series/#movies` |
| スピンオフ | `/series/#spinoff` |

`series-index.sbn` のセクション要素に `id="tv-series"` / `id="movies"` / `id="spinoff"` を付与し、`site.css` で `scroll-margin-top: 80px` を指定してサイトヘッダ（高さ約 56px）に見出しが隠れない位置で停止するように調整。

#### 劇伴詳細：仮 M 番号 cue の表示方針変更と収録盤情報の併記

旧仕様（v1.3.0 ブラッシュアップ続編で導入）の `MusicGenerator` は `is_temp_m_no=1` の cue を閲覧 UI から **完全除外** していた（`visibleCues = cues.Where(c => !c.IsTempMNo)`）。

これを「**仮タイトルとして表示しない**」と「**cue そのものを存在しなかったことにする**」は別概念である、という方針整理に従って改訂：

- 仮 M 番号 cue も **行自体は表示する**（除外フィルタを撤廃）
- M 番号セル：空欄（仮 M 番号 cue のとき）
- メニューセル：DB の `menu_title` は表示せず、代わりに**「最初に収録された盤のトラックタイトル」** を代替表示する（イタリック＋muted 色で「代替表示である」ニュアンスを伝える）
- 行全体に淡い背景色（`rgba(212, 160, 23, 0.04)`）と左ボーダーアクセント（`var(--accent-yellow)`）を入れて、通常 cue との区別を視覚的に補助
- 仮 M 番号 cue も件数集計に含める（ホーム統計・`/music/` ランディング・`/bgms/` 一覧のすべてで仮含む全件カウントに統一）

あわせて、**全 cue（仮・通常を問わず）について** メニューセル下段に「収録盤タイトル｜Tr.N｜トラックタイトル」のリストを発売日昇順で小さい字で列挙する：

- `tracks × discs × products` の JOIN クエリを 1 度だけ実行（`MusicGenerator.LoadBgmCueRecordingsAsync`）、`(bgm_series_id, bgm_m_no_detail)` で in-memory グルーピング
- 並び順は `release_date ASC, product_catalog_no ASC, track_no ASC, sub_order ASC` の安定タイブレーク。仮 M 番号 cue のメニュー代替表示も「リスト先頭 = 最初に収録された盤のトラックタイトル」となるので一貫する
- 表示は `.bgm-cue-recordings` クラスで `font-size: 0.78em` の小さな字。商品タイトルは `/products/{catalog_no}/` への内部リンク化
- 当該 cue を収録する盤が 0 件なら、リスト自体を出さない（メニューだけが見える状態）

作曲・編曲列は「シリーズ全 cue を通じて同じ人物が並ぶことが多い」運用実態に合わせて、字サイズを `0.85em`・列幅を `8em` に縮小（`site.css` 側で対応）。メニュー列が広めに使えるようバランスを取る。

#### 劇伴セクション見出しから「セッション{No}：」プレフィックス削除

`bgms-detail.sbn` のセクション見出しを：

```html
<h2>セッション{{ sec.SessionNo }}：{{ sec.SessionName | html.escape }}</h2>
```

から：

```html
<h2>{{ sec.SessionName | html.escape }}</h2>
```

に変更。セッション番号は HTML 上に出さない（`session_name` でユニーク識別される運用前提）。

##### `bgm_sessions.session_name` の用途と運用方針

`session_name` は以下の用途で使われている：

1. **サイトの劇伴詳細ページ**：`bgms-detail.sbn` のセクション見出し（本変更で番号プレフィックス無しの素のタイトル表示に変更）
2. **Catalog の bgm セッション選択 UI**：`BgmCuesEditorForm` のセッションコンボボックスで「`{SessionNo}: {SessionName}`」形式のラベル表示
3. **bgm CSV インポート時のキー**：CSV の `session_name` 列で同シリーズ内の `bgm_sessions` を完全一致検索。無ければ「既存最大 `session_no` + 1」で新規採番してインサート

3 の CSV インポートロジックがそもそも「シリーズ内 session_name 完全一致」をキーにしているため、実質的に**シリーズ内ユニーク前提**で運用されている。「第 1 回録音」「サウンドトラック制作」のような意味のある名称への運用変更は DB を手で書き換えるだけで OK で、コード変更は不要。SessionName が `default` のようなプレースホルダ名のままだと、本節の見出し変更後にそのまま `<h2>default</h2>` が出てしまうので、運用側で意味のある名称への置き換えが望ましい。

#### カバレッジラベルの方針確定（コード変更なし）

「プリキュア・キャラ・人物・企業のカバレッジラベルをピンポイント表示にしたい」の要望について、用途を確認した結果「サイト全体のクレジット最終話を全ページ共通で表示する現行仕様のままで良い」（ゲスト出演者がその後シリーズに出ていないことも情報として読み取れるため）と方針確定。コードは現状維持。

#### 影響ファイル一覧（第 2 弾）

| 区分 | パス | 変更内容 |
|---|---|---|
| Rendering | `PrecureDataStars.SiteBuilder/Rendering/ScribanRenderer.cs` | `TemplateContext.LoopLimit = 1_000_000` 設定追加（`/episodes/` 生成エラー対策） |
| Generator | `PrecureDataStars.SiteBuilder/Generators/SeriesGenerator.cs` | 映画親子分類ロジックを `MOVIE_SHORT` 限定に再構成、親+子の尺合計ラベル（m分ss秒）を `MovieSeriesRow.RuntimeLabel` で供給 |
| Generator | `PrecureDataStars.SiteBuilder/Generators/MusicGenerator.cs` | 仮 M 番号 cue を表示対象に含めるよう改訂、`tracks × discs × products` JOIN で全 cue の収録盤情報を取得、`BgmCueRow.Recordings` / `MenuFallbackTitle` / `IsTempMNo` フィールドを供給 |
| Template | `PrecureDataStars.SiteBuilder/Templates/series-index.sbn` | 3 セクションに `id="tv-series"` / `id="movies"` / `id="spinoff"` 付与、親映画行に尺合計列追加（`col-episodes` 流用） |
| Template | `PrecureDataStars.SiteBuilder/Templates/home.sbn` | TVシリーズ / 映画 / スピンオフのリンクをフラグメント付き（`#tv-series` / `#movies` / `#spinoff`）に変更 |
| Template | `PrecureDataStars.SiteBuilder/Templates/bgms-detail.sbn` | セッション見出しから「セッション{No}：」プレフィックス削除、メニューセル下段に収録盤情報リスト追加、仮 M 番号 cue 行の代替表示分岐追加 |
| Asset | `PrecureDataStars.SiteBuilder/wwwroot/assets/site.css` | `scroll-margin-top` 設定、`.bgm-cue-recordings` 系・`.bgm-cue-row-temp` 系・作曲/編曲列幅縮小・`.series-movie-table` の `.col-episodes` 等幅フォント指定を追加 |

---

### v1.3.0 公開直前のデザイン整理（第 3 弾：スピンオフ系シリーズ種別の細分化）

バージョン据え置き、DB スキーマ追加なし。`series_kinds` マスタへのデータ追加（`OTONA` / `SHORT` / `EVENT` の 3 種）に伴うサイト生成側の対応。

#### `series_kinds` マスタの追加 3 種別

旧仕様の `SPIN-OFF` 1 種別では「大人向けスピンオフ」「ショートアニメ」「3D シアターのイベント上映枠」が一緒くたになっており、シリーズ一覧での見通しが悪かった。マスタ側で 3 種別を追加して分類を細かくする：

| `kind_code` | `name_ja` | `name_en` | `credit_attach_to` |
|---|---|---|---|
| `OTONA` | 大人向けスピンオフ | Grown-up spin-off | `EPISODE` |
| `SHORT` | ショートアニメ | Short Anime | `EPISODE` |
| `EVENT` | イベント | 3D Theater | `SERIES` |
| `SPIN-OFF` | スピンオフ（狭義の本流スピンオフのみ） | Spin-off | `EPISODE` |

`credit_attach_to` は OTONA / SHORT / SPIN-OFF がエピソード単位、EVENT のみシリーズ単位でクレジットを保持する（3D シアター上映はエピソードという概念が無いため）。

#### シリーズ一覧のセクション構成

`/series/` の構成を以下のとおり再編。映画セクションの下に新たに **OTONA → SHORT → EVENT → SPIN-OFF の順** で並べ、各セクションは内容が 0 件のときは表示自体を出さない（不要な見出しが残らないように）：

| 順 | セクション見出し | `kind_code` | アンカー id |
|---|---|---|---|
| 1 | TV シリーズ | `TV` | `#tv-series` |
| 2 | 映画 | `MOVIE` / `SPRING`（子 `MOVIE_SHORT`） | `#movies` |
| 3 | 大人向けスピンオフ | `OTONA` | `#otona` |
| 4 | ショートアニメ | `SHORT` | `#short-anime` |
| 5 | イベント | `EVENT` | `#event` |
| 6 | スピンオフ | `SPIN-OFF` | `#spinoff` |

3 〜 6 の表構成は TV と同じ「タイトル / 期間 / 全N話」の素直な 3 列（共通の `TvSeriesRow` DTO を流用）。OTONA・SHORT・EVENT・SPIN-OFF それぞれは `SeriesIndexModel.OtonaSeries` / `ShortSeries` / `EventSeries` / `SpinOffSeries` プロパティで個別に渡され、テンプレ側で 4 つの `<section>` を if 分岐で並べる。

`SeriesGenerator` 側には共通ヘルパ `BuildSimpleRowsByKind(string kindCode)` を新設して、TV を含む 5 種別すべての行リストを同じ組み立てロジックで作る（放送開始日昇順 → series_id 昇順タイブレーク）。

#### ホーム統計ボックス「スピンオフ」の取り扱い

統計ボックスは「スピンオフ」**1 ボックスのまま据え置き**、値とリンクを更新：

- `DbStats.SpinOffSeriesCount` の集計範囲を `kind_code IN ('OTONA','SHORT','EVENT','SPIN-OFF')` の合計件数に拡張（旧仕様：`SPIN-OFF` のみ）
- リンク先を `/series/#spinoff` から `/series/#otona`（4 セクションの最初）に変更。ユーザーは OTONA セクションに着地し、必要に応じて下へスクロールして SHORT / EVENT / SPIN-OFF を見られる

併せて、第 2 弾で SeriesGenerator 側の親映画判定を `kind_code IN ('MOVIE','SPRING')`（parent の有無不問）に整理した結果、`HomeGenerator.MovieSeriesCount` の集計式が旧仕様（`MOVIE / MOVIE_SHORT / SPRING の parent NULL 件数`）のままで食い違っていたため、こちらも新仕様に揃える：

```csharp
// 映画件数：MOVIE / SPRING 全件（MOVIE_SHORT は子作品なので除外、parent の有無不問）
int movieSeriesCount = _ctx.Series.Count(s =>
    string.Equals(s.KindCode, "MOVIE",  StringComparison.Ordinal)
 || string.Equals(s.KindCode, "SPRING", StringComparison.Ordinal));
```

同時に `HomeGenerator.IsChildOfMovie()` も `kind_code == "MOVIE_SHORT"` の 1 行判定に簡素化して `SeriesGenerator` / `MusicGenerator` と完全に揃えた（旧実装は「parent あり + SPIN-OFF 以外」だった）。

#### 影響ファイル一覧（第 3 弾）

| 区分 | パス | 変更内容 |
|---|---|---|
| Generator | `PrecureDataStars.SiteBuilder/Generators/SeriesGenerator.cs` | `SeriesIndexModel` に `OtonaSeries` / `ShortSeries` / `EventSeries` プロパティ追加、`SpinOffSeries` を `SPIN-OFF` のみに範囲縮小、共通ヘルパ `BuildSimpleRowsByKind(string)` 追加で TV / 4 スピンオフ系の行構築を統一 |
| Generator | `PrecureDataStars.SiteBuilder/Generators/HomeGenerator.cs` | `movieSeriesCount` を `kind_code IN ('MOVIE','SPRING')` 全件に揃え（第 2 弾の親判定と整合）、`spinOffSeriesCount` を 4 種別合計に拡張、`IsChildOfMovie()` を `MOVIE_SHORT` 1 行判定に簡素化 |
| Template | `PrecureDataStars.SiteBuilder/Templates/series-index.sbn` | 旧スピンオフ 1 セクションを OTONA / SHORT / EVENT / SPIN-OFF の 4 セクションに展開、空セクションは表示しない if 分岐 |
| Template | `PrecureDataStars.SiteBuilder/Templates/home.sbn` | スピンオフボックスのリンクを `/series/#spinoff` → `/series/#otona` に変更、統計セクションのコメントを 4 種別合計仕様に更新 |
| Asset | `PrecureDataStars.SiteBuilder/wwwroot/assets/site.css` | `scroll-margin-top` セレクタに `#otona`, `#short-anime`, `#event` を追加 |

DB スキーマ変更なし（`series_kinds` テーブルへのデータ追加のみ、これはユーザー側で SQL を流して対応済み）。

---

### v1.3.0 公開直前のデザイン整理（第 4 弾：シリーズ × プリキュア紐付けテーブル新設・複合行アプローチ・商品タイトル短縮）

バージョン据え置き。**DB スキーマ追加あり**：`series_precures` テーブルを新設。Catalog UI（紐付け編集フォーム）は本リリース範囲外で、当面は手動 SQL 運用。

#### 背景

シリーズ詳細にプリキュア一覧セクションを出したい / シリーズ一覧の各行にサブ情報を併記したい、という要望に対し、既存スキーマ（`precures` テーブル）には「シリーズへの紐付け」を保持する列が無かった。1 プリキュアは複数シリーズに渡って登場し得る（クロスオーバー作品でレギュラー扱い、続編シリーズで引き続きレギュラー、変身前の姿で出てきて変身しない出演 等）ため、`precures` 側に `series_id` を 1 列足す案は採用せず、純粋な多対多関連テーブル `series_precures` を新設して対応する。

加えて、劇伴詳細・シリーズ一覧で「情報量と可読性の両立」を実現する共通アプローチとして「**複合行（メイン行 + サブ行）**」構造を導入する。

#### `series_precures` テーブル

```sql
CREATE TABLE `series_precures` (
  `series_id`     int                NOT NULL,
  `precure_id`    int                NOT NULL,
  `display_order` tinyint unsigned   NOT NULL DEFAULT 0,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)        DEFAULT NULL,
  `updated_by`    varchar(64)        DEFAULT NULL,
  PRIMARY KEY (`series_id`, `precure_id`),
  KEY `ix_series_precures_precure` (`precure_id`),
  CONSTRAINT `fk_series_precures_series`
    FOREIGN KEY (`series_id`)  REFERENCES `series`   (`series_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_precures_precure`
    FOREIGN KEY (`precure_id`) REFERENCES `precures` (`precure_id`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
```

設計のポイント：

- PK は `(series_id, precure_id)` の複合 → 同シリーズに同一プリキュアの重複登録不可
- `display_order` で同シリーズ内のプリキュア並び順を制御。0 始まり、昇順、デフォルト 0、同値時 `precure_id` 昇順でタイブレーク
- FK は両側 `ON DELETE CASCADE`：シリーズ削除またはプリキュア削除に伴い関連も自動削除
- `precure_id` 単独の逆引きインデックス（プリキュア → 登場シリーズ群の引き）
- 論理削除フラグは持たない（紐付けは「入れる／消す」の 2 値運用）

マイグレーション SQL（`db/migrations/v1.3.0_series_precures_create.sql`）は冪等な `CREATE TABLE IF NOT EXISTS`。既存環境で再実行しても問題なくスキップされる。

#### Data 層

- `PrecureDataStars.Data/Models/SeriesPrecure.cs`：エンティティモデル（`SeriesId` / `PrecureId` / `DisplayOrder` + 標準メタ）
- `PrecureDataStars.Data/Repositories/SeriesPrecuresRepository.cs`：CRUD リポジトリ
  - `GetAllAsync(ct)`：全件取得、`series_id ASC, display_order ASC, precure_id ASC` でソート（SiteBuilder の一括ロード用）
  - `GetBySeriesAsync(seriesId, ct)`：指定シリーズの紐付けを `display_order ASC` で取得
  - `GetByPrecureAsync(precureId, ct)`：指定プリキュアの登場シリーズを `series_id ASC` で取得
  - `AddAsync(entity, ct)` / `UpdateDisplayOrderAsync(...)` / `RemoveAsync(seriesId, precureId, ct)`

#### SiteBuilder：シリーズ × プリキュア反映

`SeriesGenerator` に以下を追加：

- `BuildPrecureRowsBySeriesCacheAsync(ct)`：起動時に `series_precures` を全件ロードし、`precures` / `character_aliases` / `persons` の 3 マスタを引いて `series_id → SeriesPrecureDisplay` のマップに解決してキャッシュ
- `GetPrecureRows(seriesId)`：上記キャッシュからの読み出しヘルパ
- シリーズ詳細 (`series-detail.sbn`)：メインスタッフセクション直下に **プリキュアセクション** を追加。`(変身前 / 変身後 / 声優)` の 3 列テーブル。変身後名は `/precures/{precure_id}/` リンク、声優は `/persons/{person_id}/` リンク。紐付け 0 件のシリーズではセクション自体を出さない
- シリーズ一覧 (`series-index.sbn`)：TV シリーズの各行を **メイン行 + サブ行** の複合構造に変更。サブ行は colspan で横ぶち抜き、淡背景 + 左ボーダーアクセント

#### シリーズ一覧の複合行サブ情報

サブ行には 2 種類の情報を縦に積む（どちらも空なら行自体を出さない）：

1. **メインスタッフサマリ**：5 役職（製作 / 構成 / 監督 / キャラ / 美術）について、各役職「担当エピソード数が最も多い 1 名（同値時はソートキー昇順）」を抜粋して「`[製作] ○○ ｜ [構成] ○○ ｜ [監督] ○○ ｜ [キャラ] ○○ ｜ [美術] ○○`」形式の単一行に整形。TV シリーズのみ集計（他種別は空文字）
2. **プリキュア一覧**：シリーズに紐付くプリキュアを `display_order` 昇順で「`キュアブラック (CV: 本名陽子) / キュアホワイト (CV: ゆかな)`」形式に連結

これらの集計は `BuildKeyStaffSummaryBySeriesCacheAsync(ct)` で起動時に 1 回計算してキャッシュ化、`BuildSimpleRowsByKind("TV")` でキャッシュから引いて `TvSeriesRow.KeyStaffSummary` / `PrecureSummary` に詰める。

#### 劇伴詳細の複合行アプローチ（第 2 弾の同セル縦並びを再構成）

劇伴詳細 (`bgms-detail.sbn`) を **メイン行（M番号 / メニュー / 作曲 / 編曲 / 尺 / 使用回数）+ サブ行（収録盤情報を colspan）** の 2 行ペア構造に再編：

- メイン行は常に 1 行高で揃い、縦スクロールで M 番号と主要属性をスキャンしやすい
- サブ行は淡背景 + 左ボーダーアクセントで「メイン行に付属する補足」と視覚的に伝える
- 収録盤情報が 0 件の cue ではサブ行自体を出さない
- 仮 M 番号 cue 行のサブ行は黄系の左ボーダー + 淡黄背景に切り替え（メイン行の `col-mno` アクセントとの一体感）

これにより、第 2 弾で「同セル内縦並び」だった構造が解消され、表全体の縦リズムが揃う。

#### 商品タイトルの表示用短縮

長尺の商品タイトル（例「トロピカル～ジュ!プリキュア オリジナル・サウンドトラック1 プリキュア・トロピカル・サウンド!!」）を、収録盤情報リスト上で読みやすく表示するため、`MusicGenerator.ShortenProductTitle(string)` で表示用に短縮する：

| 元 | 短縮後 |
|---|---|
| 「○○プリキュア オリジナル・サウンドトラックN サブタイトル」 | 「サントラN『サブタイトル』」 |
| 「○○プリキュア オリジナル・サウンドトラック」 | 「サントラ」 |
| 「○○プリキュア オリジナル・サウンドトラック2」 | 「サントラ2」 |

正規表現 `^.*?プリキュア\s+オリジナル・サウンドトラック(?<num>\d*)(?:\s+(?<sub>.+))?$` でマッチした場合のみ短縮、マッチしない商品（シングル / マキシ / イメージアルバム等）は元の文字列をそのまま返す（壊さない保守的な変換）。フル表記は `BgmCueRecording.ProductTitleFull` に保持し、テンプレ側で `<a title="...">` 属性に注入してホバー時に確認可能。

#### 影響ファイル一覧（第 4 弾）

| 区分 | パス | 変更内容 |
|---|---|---|
| DDL | `db/schema.sql` | `precures` トリガ定義の直後に `series_precures` テーブル定義を追加 |
| Migration | `db/migrations/v1.3.0_series_precures_create.sql` | 新規追加（冪等な `CREATE TABLE IF NOT EXISTS`、既存環境向け） |
| Data | `PrecureDataStars.Data/Models/SeriesPrecure.cs` | 新規追加 |
| Data | `PrecureDataStars.Data/Repositories/SeriesPrecuresRepository.cs` | 新規追加（CRUD + シリーズ別/プリキュア別取得） |
| Generator | `PrecureDataStars.SiteBuilder/Generators/SeriesGenerator.cs` | `SeriesPrecuresRepository` / `PrecuresRepository` / `CharacterAliasesRepository` 注入、`BuildPrecureRowsBySeriesCacheAsync` / `BuildKeyStaffSummaryBySeriesCacheAsync` で起動時キャッシュ構築、`BuildSimpleRowsByKind` で TV 行にサブ情報注入、シリーズ詳細に `Precures` プロパティ追加、`SeriesPrecureDisplay` / `SeriesPrecureRow` / `TvSeriesRow.KeyStaffSummary` / `TvSeriesRow.PrecureSummary` DTO 拡張 |
| Generator | `PrecureDataStars.SiteBuilder/Generators/MusicGenerator.cs` | `ShortenProductTitle(string)` メソッド追加、`BgmCueRecording` DTO に `ProductTitleFull` を追加（フル表記をホバー用に保持） |
| Template | `PrecureDataStars.SiteBuilder/Templates/series-detail.sbn` | メインスタッフ直下にプリキュアセクション（3 列テーブル：変身前 / 変身後 / 声優）を追加 |
| Template | `PrecureDataStars.SiteBuilder/Templates/series-index.sbn` | TV シリーズ表を複合行（メイン行 + サブ行）構造に変更、サブ行に `KeyStaffSummary` / `PrecureSummary` を縦に積む |
| Template | `PrecureDataStars.SiteBuilder/Templates/bgms-detail.sbn` | 全面書き換え：「同セル内縦並び」を「メイン行 + サブ行（colspan）」構造に再編、商品タイトルは短縮表記を表示、`<a title="...">` でフル表記を保持 |
| Asset | `PrecureDataStars.SiteBuilder/wwwroot/assets/site.css` | `.series-tv-table .main-row` / `.sub-row` 系・`.series-precures-table` 系・`.bgm-cues-table .bgm-cue-main` / `.bgm-cue-sub` 系のスタイル追加 |

**Catalog UI は本リリース範囲外**：紐付けの編集は当面 SQL 手動 INSERT で運用する。SeriesEditor へのタブ追加 もしくは 専用 `SeriesPrecuresEditorForm` 新設の判断は次の段階で扱う。

---

### v1.3.0 ブラッシュアップ stage 20 — 商品の社名構造化（`product_companies` 新設・フリーテキスト全廃・既定フラグ）

リリース前 v1.3.0 のブラッシュアップ第 4 弾。バージョン据え置き、**DB スキーマ追加・列削除あり**（破壊的：旧 `products.manufacturer` / `label` / `distributor` フリーテキスト 3 列をデータ自動移行のうえ撤去）。

#### 背景

これまで商品（`products`）の発売元（`manufacturer`）・販売元（`distributor`）・レーベル（`label`）は全てフリーテキストで持っていたため、

1. 表記揺れ（「マーベラス」「マーベラス株式会社」「Marvelous」）の同一視ができない
2. JSON-LD で `recordLabel` を Organization オブジェクトとして書き出せない
3. 屋号変更（旧マーベラス AQL → マーベラス）を運用者が個別商品で手書き直す必要がある

という問題が積み上がっていた。また、運用者の判断で「`manufacturer` と `label` は実質同じ概念」と整理されたため、両者をフィールド単位で分けて持つ意味も無くなった。

#### 設計判断：クレジット非依存の専用マスタ + 既定フラグ

クレジット系の `companies` / `company_aliases` に商品流通用の社名を相乗りさせる案も検討したが却下した。理由：商品メタは「ジャケット・帯にどう書かれていたか」のスナップショットで足り、クレジット側の屋号系譜と混ぜると意味的にズレた合算が起きやすい。商品とクレジットの関心分離を保つほうが将来の保守で楽。

→ クレジット非依存の `product_companies` テーブルを新設し、`products` から直接 FK で紐付ける構造に決定。

旧来 `NewProductDialog` で `txtLabel.Text = "MARV"` / `txtDistributor.Text = "SMS"` とハードコードしていた既定値については、マスタ側に `is_default_label` / `is_default_distributor` フラグを設けて指定する形に変更。フラグの排他性（最大 1 行）はアプリ側（リポジトリのトランザクション）で担保し、GUI でチェック ON にするだけで他社のフラグが自動的に落ちる。

#### データ構造変更

- 新テーブル `product_companies`（PK = `product_company_id`、和名 + かな + 英名 + 既定フラグ 2 つ + 備考）
- `products` に `label_product_company_id` / `distributor_product_company_id` の 2 つの FK 列を追加（ともに `ON DELETE SET NULL`、`ON UPDATE CASCADE`）
- `products` から **`manufacturer`** / **`label`** / **`distributor`** の 3 列を撤去（マイグレで `manufacturer` → `label` マージ後、distinct 値を `product_companies` に自動 INSERT、ID 埋め込み、列 DROP までを一気通貫）
- 列順を「`disc_count → label_product_company_id → distributor_product_company_id → amazon_asin`」に整理

マイグレーション SQL: `db/migrations/v1.3.0_stage20_product_companies.sql`

冪等性：全 PROCEDURE で `INFORMATION_SCHEMA.COLUMNS` による列存在確認のうえ実行。途中まで実行した環境（過去版で `product_companies` だけ作成済み等）でも再実行で完走する。

自動移行ロジック：
1. `products.label` / `distributor` の distinct 値を `release_date ASC, product_catalog_no ASC` の初出順で `product_companies` に INSERT（`created_by = 'migration_v1.3.0_stage20'` の印付き）
2. `products` の FK 列を `name_ja = TRIM(label)` の JOIN で埋める
3. フリーテキスト列を DROP
4. 使用頻度 TOP1 の社に `is_default_label` / `is_default_distributor` を自動セット（既存にフラグが立っていれば運用者意思を尊重して触らない）

safe update mode（MySQL Workbench 既定）対応：全 UPDATE 系 PROCEDURE で冒頭に `SQL_SAFE_UPDATES = 0` を設定し、終了時と EXIT HANDLER で元の値に復元する。

#### 表示・JSON-LD 出力

商品詳細ページ（`/products/{catalog_no}/`）のレーベル名・販売元名は、構造化 ID から `product_companies.name_ja` を引いた文字列のみを表示する（フォールバック分岐は廃止）。マスタ未紐付け or 論理削除済みなら空文字。

JSON-LD の `recordLabel` も同様で、構造化 ID が立っていれば `{"@type":"Organization","name":...}` の Organization オブジェクトを埋め込み、立っていなければ `recordLabel` 自体を出力しない。

#### Catalog UI

新メニュー「商品社名マスタ管理...」を「商品・ディスク管理...」の直後に挿入。`ProductCompaniesEditorForm`（左ペイン一覧 + 右ペイン詳細 5 フィールド + 既定フラグ 2 つ + 新規/保存/削除）で CRUD。

「商品・ディスク管理」フォームの商品詳細パネルからは旧フリーテキスト 3 行を撤去し、「レーベル」「販売元」の社名マスタ紐付け行 2 つに集約。各行は **[ReadOnly テキスト] + [選択...] + [解除]** の 3 コントロールで、「選択...」で `ProductCompanyPickerDialog` を起動する。`Tag` プロパティに `int?` の ID を持ち回す方式で、UI 上の表示名と実 ID を切り離す。

`NewProductDialog`（CDAnalyzer / BDAnalyzer の新規登録経路）からも旧 `txtLabel` / `txtDistributor` を撤去。代わりに `ProductCompaniesRepository.GetDefaultLabelAsync` / `GetDefaultDistributorAsync` で既定フラグ社を取得し、ReadOnly TextBox に表示する。実 ID はフィールドで保持し、OK 時に `Result.LabelProductCompanyId` / `DistributorProductCompanyId` にセットする。既定が未設定なら「(未設定)」を表示して内部 ID は null（FK は NULL のまま）。

#### 変更ファイル

| 区分 | パス | 変更 |
|---|---|---|
| DB | `db/migrations/v1.3.0_stage20_product_companies.sql` | 新規（product_companies CREATE + フラグ列 + manufacturer→label マージ + フリーテキスト自動移行 + DROP + FK 列追加 + 既定フラグセット + 列順整理。8 PROCEDURE 構成、冪等） |
| DB | `db/schema.sql` | `product_companies` テーブル定義追加、`products` から旧 3 列削除 + FK 列 2 追加 + 列順整理 |
| Data | `PrecureDataStars.Data/Models/ProductCompany.cs` | 新規 POCO（`IsDefaultLabel` / `IsDefaultDistributor` 含む） |
| Data | `PrecureDataStars.Data/Models/Product.cs` | `Manufacturer` / `Label` / `Distributor` プロパティ削除、`LabelProductCompanyId` / `DistributorProductCompanyId` のみ |
| Data | `PrecureDataStars.Data/Repositories/ProductCompaniesRepository.cs` | 新規 CRUD + フラグ列対応 + `GetDefaultLabelAsync` / `GetDefaultDistributorAsync` + フラグ排他処理（トランザクション） |
| Data | `PrecureDataStars.Data/Repositories/ProductsRepository.cs` | SELECT/INSERT/UPDATE から旧フリーテキスト 3 列を撤去、FK 列 2 つに完全置換 |
| Catalog UI | `PrecureDataStars.Catalog/Forms/Pickers/ProductCompanyPickerDialog.{cs,Designer.cs}` | 新規 picker |
| Catalog UI | `PrecureDataStars.Catalog/Forms/ProductCompaniesEditorForm.{cs,Designer.cs}` | 新規 CRUD エディタ + 既定フラグチェックボックス 2 つ + グリッドに ★ 列 |
| Catalog UI | `PrecureDataStars.Catalog/Forms/ProductDiscsEditorForm.{cs,Designer.cs}` | フリーテキスト 3 行撤去、社名屋号 2 行のみ |
| Catalog UI | `PrecureDataStars.Catalog/MainForm.{cs,Designer.cs}` | `_productCompaniesRepo` 注入、`mnuProductCompanies` メニュー追加、`mnuProductDiscs_Click` 引数追加 |
| Catalog UI | `PrecureDataStars.Catalog/Program.cs` | `ProductCompaniesRepository` インスタンス化 + MainForm に渡す |
| Catalog UI | `PrecureDataStars.Catalog.Common/Dialogs/NewProductDialog.{cs,Designer.cs}` | `txtLabel` / `txtDistributor` 撤去、`txtDefaultLabel` / `txtDefaultDistributor`（ReadOnly）に置換、既定社取得 + ID セット、コンストラクタに `ProductCompaniesRepository` 追加 |
| CDAnalyzer | `PrecureDataStars.CDAnalyzer/MainForm.cs` / `Program.cs` | `_productCompaniesRepo` フィールド + コンストラクタ引数、NewProductDialog 呼び出し引数追加 |
| BDAnalyzer | `PrecureDataStars.BDAnalyzer/MainForm.cs` / `Program.cs` | 同上 |
| SiteBuilder | `PrecureDataStars.SiteBuilder/Generators/ProductsGenerator.cs` | `ResolveCompanyName` をフォールバック撤去でシンプル化、`ProductView.LabelText` / `DistributorText` は構造化解決結果のみ、JSON-LD `recordLabel` を Organization 限定 |

---

### v1.3.0 ブラッシュアップ stage 19 — 連載クレジットの整理（漫画役職 `MANGA` 分離 + テンプレ DSL に兄弟役職参照構文を追加）

リリース前 v1.3.0 のブラッシュアップ第 3 弾。バージョン据え置き、**DB スキーマ追加なし**（roles マスタへのデータ追加と既存データの組み換えのみ）。

#### 背景

従来 `SERIALIZED_IN`「連載」役職下に PERSON（漫画家）と COMPANY（雑誌名）が同居していて、`CreditInvolvementIndex` の役職集計時に漫画家が「連載」役職として誤集計されていた（本来は「漫画」と分けたい）。

#### データ構造変更

`roles` マスタに新規役職 `MANGA`「漫画」（name_en=`Manga`、role_format_kind=`NORMAL`）を追加し、既存 `SERIALIZED_IN` 配下の PERSON エントリをすべて新 `MANGA` 役職下に移送する。`MANGA` 役職は同 `card_group_id` 配下に `order_in_group = SERIALIZED_IN.order_in_group + 1`（連載の直後）で配置。

マイグレーション SQL: `db/migrations/v1.3.0_stage19_manga_role_split.sql`

冪等性あり（再実行しても何も変わらない）、トランザクション内完結（途中失敗で全巻き戻り）。

#### テンプレ DSL 構文拡張: `{ROLE:CODE.PLACEHOLDER}`（兄弟役職参照）

`SERIALIZED_IN` テンプレから同 Group 内の `MANGA` 役職の人物を取り込むため、テンプレ DSL に **兄弟役職参照構文** `{ROLE:CODE.PLACEHOLDER}` を新設。例：

```
{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
<strong>漫画</strong>・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

`{ROLE:MANGA.PERSONS}` の評価: 現在の Group 内で `role_code='MANGA'` の役職を 1 つ探し、その役職配下の Block 群を順に巡って各 Block を一時的なカレントブロックとして内側プレースホルダ `{PERSONS}` を評価し、Block 間は内側プレースホルダの `sep` オプション（既定 `、`）で連結する。

**1 段ネスト不可（再帰禁止）**: `{ROLE:X.…}` 経由で展開した X のテンプレ内でさらに `{ROLE:Y.…}` を書いても、`TemplateContext.VisitedRoleCodes` セットでネスト中の参照は空文字に展開される（無限ループ防止）。

**未投入のケース**: 同 Group 内に該当 `role_code` の役職が存在しなければ空文字に展開される（警告は出さない、連載クレジット未投入のシリーズで支障なく動く）。

#### 影響ファイル

- `PrecureDataStars.TemplateRendering/TemplateNode.cs` に `RoleReferenceNode` を追加
- `PrecureDataStars.TemplateRendering/TemplateParser.cs` で `{ROLE:CODE.PLACEHOLDER[:opts]}` を新 AST にパース
- `PrecureDataStars.TemplateRendering/TemplateContext.cs` に `SiblingRoleResolver`（`Func<string, IReadOnlyList<BlockSnapshot>?>`）プロパティと `VisitedRoleCodes` セット、`WithSiblingRoleScope` メソッドを追加
- `PrecureDataStars.TemplateRendering/RoleTemplateRenderer.cs` で `RoleReferenceNode` の評価ロジック（`ResolveRoleReferenceAsync`）を追加
- `PrecureDataStars.SiteBuilder/Rendering/CreditTreeRenderer.cs` の Group ループで sibling 辞書を構築して `TemplateContext` に渡す（既存 Block ロードもこの辞書から流用して重複アクセスを回避）
- `PrecureDataStars.Catalog/Forms/Preview/CreditPreviewRenderer.cs` の DB 描画側 Group ループと Draft 描画側 Group ループで、同じパターンの sibling 辞書を構築して `TemplateContext` に渡す
- `db/migrations/v1.3.0_stage19_manga_role_split.sql` 新設

#### 動作確認の観点

- DB に MANGA 役職とテンプレが追加されている（`SELECT * FROM roles WHERE role_code='MANGA';` 等）
- 既存 `SERIALIZED_IN` 配下に PERSON エントリが 0 件、`MANGA` 配下に PERSON エントリが移送されている
- クレジット編集画面のライブプレビューで連載クレジットが画像 1 の通り「連載：講談社「なかよし」/ 漫画・上北 ふたご / 「たのしい幼稚園」/ 「おともだち」ほか」と表示される
- 上北ふたご個人ページや人物ランキングで「漫画」役職として集計される（「連載」では出ない）
- 講談社・なかよし・たのしい幼稚園・おともだちは「連載」役職で集計される
- Draft プレビュー（クレジット編集中の右ペイン）でも同じ表示になる
- 連載クレジット以外の役職（脚本、絵コンテ、声優キャストなど）の表示は何も変わらない（新構文は使わないため）



リリース前 v1.3.0 のブラッシュアップ。バージョン据え置き、DB スキーマ変更なし。

#### `CreditEditorForm.ClearTreeAndPreview` の充実

旧来は「ツリー」と「エントリエディタ」のみリセットしていたため、シリーズ／エピソードを切り替えてクレジット選択が外れた状態でも：

- 中央右の **HTML ライブプレビュー** に旧クレジットの内容が残ったまま
- 左下の **クレジットプロパティパネル**（CARDS/ROLL ラジオ、PartType コンボ、備考 TextBox）に旧クレジットの値が残ったまま
- ノードプロパティエディタ（`nodePropsEditor`）が開いた状態で残る

という UI 不整合を起こしていた。`ClearTreeAndPreview` で以下を全て同期リセットするよう修正：

- ツリー本体（`treeStructure.Nodes.Clear()`）
- 全エディタパネル（`entryEditor` / `blockEditor` / `nodePropsEditor` を `ClearAndDisable` + `Visible` を「ノード未選択」状態に揃える）
- クレジットプロパティパネル（ラジオ・コンボ・TextBox の値クリア）
- HTML ライブプレビュー（`RefreshPreviewAsync` を fire-and-forget 呼び出し、`_currentCredit` / `_draftSession` が null のとき「（クレジット未選択）」HTML が出る既存ロジックを利用）
- ステータスバー文言とボタン状態（`UpdateButtonStates`）

#### シリーズ／エピソード切替キャンセル時のコンボ戻し

未保存変更がある状態でシリーズ／エピソードコンボを切り替えようとして確認ダイアログ「キャンセル」を選んだとき、旧来は **コンボ表示は新しい値のまま、エピソードリスト／クレジットリストの内容は古いシリーズ／エピソード基準** という UI 不整合を起こしていた（コードコメントにも「暫定実装」と明記）。`_lastSeriesIdAccepted` / `_lastEpisodeIdAccepted` フィールドを追加し、ユーザーが切替を確定した瞬間に値を保存。キャンセル時は `_suppressComboCascade` で再帰発火を抑止しつつ `cboSeries.SelectedValue` / `cboEpisode.SelectedValue` を直前確定値に戻す。

#### 二重確認ダイアログの抑止

シリーズ切替で確認ダイアログを通過した後、`cboEpisode.DataSource` が再構成されることで `OnEpisodeChangedAsync` が連鎖発火し、再度「未保存変更があります」ダイアログが表示される問題を抑止。`OnEpisodeChangedAsync` の冒頭で `_isReloadingSeries` フラグを見て「シリーズ切替経由の連鎖発火」と判定したら早期 return する。

#### `ReloadCreditsAsync` の `_draftSession` 同期リセット

クレジットリストが空件数になった経路で `_draftSession` を null にしていなかったため、`ClearTreeAndPreview` 経由の `RefreshPreviewAsync` が「`_draftSession` 非 null」を検知して旧 Draft の内容をプレビューに描画してしまうバグがあった。`_currentCredit = null;` の直後に `_draftSession = null;` も置いて完全リセット。



リリース前 v1.3.0 の最終ブラッシュアップ第 2 弾。バージョン番号据え置き、DB スキーマ変更なし。

#### AppendToCredit モードの全面改訂

「📝 クレジット一括入力...」ボタン経由の `AppendToCredit` モードを **「現状ツリー逆変換 + 構造差分検出」モード** に置き換え。旧 v1.2.1 セマンティクス（末尾追加）は廃止。

ダイアログ起動時、現在の `CreditDraftSession.Root` 全体を `CreditBulkInputEncoder.EncodeFullAsync` で逆翻訳した文字列が初期テキストとしてエディタに入る。ユーザーが編集した後 Apply すると、`CreditBulkApplyService.ApplyDiffToCreditAsync` が **旧テキスト（=この initialText）と新テキストの両方を再パースして構造比較** し、変わった末端だけ Modified / Added / Deleted で Draft に反映する。

「最低限の単位で変更を検知」というユーザー要件に応えるため、変わっていない Card / Tier / Group / Role / Block / Entry はすべて `Unchanged` 維持で、`alias_id` や監査列も保持される。これにより：

- 同名 alias が複数存在する人物（共同名義）でも、触っていないエントリは元の `person_alias_id` を維持
- 触っていない Card / Block の `created_at` / `created_by` も保持
- 一括入力で間違えて全行を置き換えてしまった、というユーザー事故時にも、文字列レベルで変わっていない部分は DB 上 unchanged

#### LCS 適用範囲（A 案: Block 単位）

階層ごとの差分検出戦略：

| 階層 | 対応戦略 |
|---|---|
| Card / Tier / Group / Block | i 番目同士の単純対応（順序変更は希少な前提） |
| Role | role_code 辞書マッチング（同 Group 内では role_code は UNIQUE 前提） |
| Entry | **同 Block 内 LCS マッチング**（is_broadcast_only=false / true で 2 グループに分けて各々 LCS） |

Entry レベルで LCS を効かせることで、Block 内で行を入れ替えるヒューマンエラー（テキスト編集中の上下移動・コピペミス等）を **`entry_seq` 更新のみの Modified** として拾う。これも `alias_id` は保持される。

LCS のキーは `ParsedEntry` のシリアライズ文字列（`entry_kind` タグ + 各 RawText + 修飾子の連結、`LineNumber` は除外）。Block を跨いだ Entry 移動は対象外で、旧 Block で Deleted、新 Block で Added となる。

#### ReplaceScope モードは無変更

ツリー右クリック「📝 一括入力で編集...」経由の `ReplaceScope` モード（v1.2.2 で追加）は今までどおりの全置換セマンティクスで残す。スコープ範囲を限定する用途であり、差分検出のメリットが薄く、変更は退行リスクが高いため。

#### 影響ファイル

- `CreditBulkApplyService` に `ApplyDiffToCreditAsync` 公開メソッド + 階層 Diff ヘルパ群（`ApplyDiffCardAsync` / `ApplyDiffTierAsync` / `ApplyDiffGroupAsync` / `ApplyDiffRoleAsync` / `ApplyDiffBlockAsync` / `ApplyDiffEntriesInBlockAsync` / `ApplyEntryGroupDiffAsync`）を追加。`SerializeXxxForCompare` ヘルパ 6 個と `LcsMatchEntries` ヘルパも追加。
- `ApplyGroupAsync` 内のインライン Role / Block 追加ロジックを `ApplyParsedRoleNewAsync` / `ApplyParsedBlockNewAsync` に切り出し（Diff 経路で再利用するため）。挙動は同じ。
- `CreditBulkInputDialog` の `AppendToCredit` コンストラクタに `string initialText` 引数を追加。フィールド `_initialText` 保持。`OnApplyAsync` の AppendToCredit 分岐を `ApplyDiffToCreditAsync` 呼び出しに変更。
- `CreditBulkInputDialog.Designer.cs` のスコープラベル初期文言を「対象: クレジット全体（差分検出）」に変更（実際はコンストラクタで `ApplyScopeLabel` により上書きされるが、Designer プレビュー対策で初期値も新仕様に揃える）。
- `CreditEditorForm.OnBulkInputAsync` で `CreditBulkInputEncoder.EncodeFullAsync(_draftSession.Root, _lookupCache)` を呼んで initialText を生成、新コンストラクタに渡すよう変更。



リリース前 v1.3.0 の最終ブラッシュアップ。バージョン番号は据え置き、DB スキーマ変更なし。

#### 保存フェーズ順序の組み換え（CASCADE 巻き添え対策）

`CreditSaveService.SaveAsync` のフェーズ順を **1A → 2 → 2.7 → 2.5 → 2.6 → 3 → 1B → 4** に組み替え。Phase 1 を 2 つに分割し、`credit_block_entries` の DELETE のみ Phase 1A として先頭に残し、`credit_role_blocks` 以上の親階層 5 テーブル DELETE は Phase 1B として更新フェーズ（Phase 3）の後ろに移動した。

これにより、**DnD で別ブロックに移動したエントリ + 旧ブロック削除を 1 回でまとめて保存** したときに、エントリが旧ブロックの `ON DELETE CASCADE` で巻き添え削除される事故を恒久回避する。Phase 3 の UPDATE で先に `block_id` 列が新親に書き換わるため、Phase 1B で旧ブロックを削除しても CASCADE の対象に該当しない。同様に Role / Group / Tier / Card 階層の DnD 移動 + 旧親削除パターンも同時にカバーされる。「移動だけ保存 → 旧親削除して保存」の 2 段階ワークフローでは元から発生しないが、**1 トランザクションでまとめて保存** のケースで初めて顕在化していたバグ。

旧親の配下にそのまま残っていた「DnD で動かしていない真のオーファン」は親 FK が旧親のままなので、Phase 1B で期待通り CASCADE 連鎖削除される。

#### 一括入力ダイアログ：レイアウト調整

右ペインのプレビュー（上）：警告（下）の比率を **3:1 → 4:1（8:2）** に変更。`splitterDistance` 500 → 515、`Panel2MinSize` 120 → 100。警告ペインがプレビューを圧迫していた問題を解消。

#### 一括入力フォーマット：「旧名義 =&gt; 新名義」記法

人物 / キャラ / 企業屋号 / LOGO の屋号部いずれの位置でも **`旧名義 =&gt; 新名義`** という記法を許可。矢印の向きは「名義が変わった」遷移方向に揃え、左 = 既存マスタの参照キー（`person_aliases.name` / `character_aliases.name` / `company_aliases.name` 完全一致）、右 = この行で実際に表示する別名義。

適用フェーズ（`CreditBulkApplyService`）が左側で既存 alias を引き当てて主人物 / 主キャラ / 主企業の `parent_id` を取得し、同 parent 配下に右側を新 alias として登録する：

- PERSON: `person_aliases` INSERT + 中間表 `person_alias_persons` UPSERT（`person_seq=1` 固定、共同名義は稀のため）
- CHARACTER: `character_aliases` INSERT（`character_id` 列直結）
- COMPANY: `company_aliases` INSERT（`company_id` 列直結）
- LOGO: 屋号部分のみ `=>` を解釈。CI バージョン違いは別 logos 行で表現するため対象外。新屋号で登録された logos を旧屋号表記からも引けるよう、屋号引き当ての軸として旧側を優先するフォールバック付き

旧側が引き当たらないときは警告メッセージを `InfoMessages` に積んだ上で、右側のみで通常の新規作成にフォールバック（タイポしたまま気付かずに人物が量産される事故を抑える）。

実装は `CreditBulkInputParser`（`SplitOldNewRedirect` ヘルパ + 全エントリ種別経路で適用）と `CreditBulkApplyService`（3 つの `ResolveOrCreateXxxAlias` メソッド + `ResolveLogoAsync` の引数拡張）にまたがる。`ParsedEntry` には `PersonOldName` / `CharacterOldName` / `CompanyOldName` プロパティを追加。CreditEditorForm のコンストラクタ引数に `PersonAliasPersonsRepository` を追加（既存 person への新 alias 追加で中間表の Upsert に使用）。

#### 一括入力フォーマット：似て非なる名義の警告

新規作成しようとしている名義（`=>` リダイレクトで決着しなかったもの）について、`person_aliases` / `character_aliases` / `company_aliases` を **全件取得**（`GetAllAsync(includeDeleted=false)`）し、空白除去後に **LCS（最長共通部分列）/ max(len) ≥ 0.5** を満たすが正規化後完全一致でない既存名義を検出して警告メッセージに積む。

- 「五條 真由美」と既存「五条真由美」のような漢字違い・空白違いを検出
- LOGO の屋号引き当て失敗時にも `company_aliases` に対して同じ判定が走る（屋号部の誤記検出）
- 警告メッセージには既存 alias の `alias_id` と所属 `person_id` / `character_id` / `company_id` を併記、ユーザーが Catalog のマスタ管理画面で確認しやすくする
- 1 ダイアログ起動につき各テーブル GetAllAsync は 1 回だけ（`_allXxxAliasesCache` フィールドで lazy cache、`ResolveAsync` 開始時にクリア）
- 比較中は警告ペイン上部のステータスラベルに「**似て非なる名義を比較中... (n/total)**」を約 50 件単位で更新表示。`CreditBulkApplyService.CompareProgress` イベントを `CreditBulkInputDialog` が購読する仕掛け。完了時にラベルは非表示に戻る

警告は **InfoMessages** ペイン（`b-1` 配置）に他のパース警告と共に積まれ、適用後にダイアログ上のリストとして閲覧できる。Apply 自体は止めない設計（誤検出が起きても作業が中断しない）。



v1.3.0 SiteBuilder 投入後の運用で見えてきた課題（クレジット系の enum 列がコードと DB で食い違いやすい・サイト側の人物軸ページに音楽クレジットが反映されない・名寄せ移行ツールが「テキスト → 構造化」の入口を運用者に丸投げしていた、等）を一括で解消するブラッシュアップ群。Phase 1 〜 Phase 4 の 4 段構成で進めた。バージョン番号は v1.3.0 のまま据え置き（DB マイグレーションは追加するが、Web 公開仕様に破壊的変更なし）。

#### Phase 1：マスタ系テーブルの正規化と enum → 役職コード列の置換

DB スキーマレベルで「文字列定数を **roles マスタ + varchar(32) FK** で正規化する」方針を音楽系に適用：

- **`character_kinds` マスタ新設**（PRECURE / ALLY / VILLAIN / SUPPORTING、`display_order` 10/20/30/40）。`characters.character_kind` enum を `character_kind_code varchar(32)` + FK へ置換。SiteBuilder 側のセクション分け表示は `character_kinds.display_order` 順に切り替え。
- **`song_music_classes` マスタ新設**（OP / ED / INSERT / MOVIE / IMAGE / CHARA / OTHER）。`songs.music_class` enum を `music_class_code varchar(32)` + FK へ置換。
- **`song_credits.credit_role`** を enum から varchar(32) + roles FK へ。値も `LYRICIST` / `COMPOSER` / `ARRANGER` から **`LYRICS` / `COMPOSITION` / `ARRANGEMENT`** にリネーム（roles マスタ側の役職コードと完全一致させ、表記揺れを根絶）。
- **`bgm_cue_credits.credit_role`** も同様に varchar(32) + roles FK 化。値は `COMPOSITION` / `ARRANGEMENT`。
- **`song_recording_singers.role_code varchar(32)`** 列を追加（DEFAULT 'VOCALS'）。PK を `(recording_id, role_code, singer_seq)` に拡張、CHORUS など複数役職の連名表示に対応。
- **`episode_theme_songs.usage_actuality`** enum を追加（NORMAL / BROADCAST_NOT_CREDITED / CREDITED_NOT_BROADCAST）。「本放送ではかかったがクレジットには載らなかった」「クレジットには載ったが本放送では使われなかった」というプリキュア系列に頻出する非対称ケースを 1 列で表現できるように。

既存データはマイグレーション内で新値にバックフィル。後続の Catalog / SiteBuilder 側コードは Phase 2 で追従。

#### Phase 2：データ層・テンプレート層の追従

- `SongCredit` / `BgmCueCredit` モデル：`credit_role` を enum から `string` へ変更。`SongCreditsRepository` / `BgmCueCreditsRepository` の API も `string role` を受け取る形にリファクタ。`ReplaceAllByRoleAsync` 等の既存ヘルパも追従。
- `SongRecordingSingersRepository.GetDisplayStringAsync` の第 2 引数を `string? roleCode = null` に変更（既定 null で全役職、指定時はその役職のみ）。
- `ThemeSongsHandler`（HTML 生成側、`PrecureDataStars.TemplateRendering`）と `CreditPreviewRenderer`（Catalog プレビュー側）の両方で、新値（`LYRICS` / `COMPOSITION` / `ARRANGEMENT` / `VOCALS`）の `SongCreditRoles` / `SongRecordingSingerRoles` 静的定数を参照するよう書き換え。
- `CreditInvolvementIndex`（SiteBuilder 起動時に 1 回だけ構築する全関与逆引き）に **音楽系クレジットの逆引きを追加**：人物軸ページ（`/persons/{personId}/`）に「作詞 / 作曲 / 編曲 / 歌唱 / 劇伴作曲 / 劇伴編曲」の関与履歴をシリーズ × 楽曲単位で表示。声優軸とは独立したセクションで、楽曲タイトル → `/songs/{song_id}/` リンクと、シリーズタイトル → `/series/{slug}/` リンクを併記。
- エピソード詳細の主題歌セクションで `usage_actuality` を視覚的に区別表示：BROADCAST_NOT_CREDITED は「本放送でかかったがクレジットには載らなかった」旨の注釈、CREDITED_NOT_BROADCAST は「クレジットには載ったが本放送では使われなかった」旨の注釈を、それぞれの行末に薄字で表示。
- `PrecureDataStars.TemplateRendering` を独立プロジェクト（.NET 9、Forms 非依存）に切り出し、Catalog 側 / SiteBuilder 側の同名ディレクトリで重複していた 5 ファイル（`TemplateContext` / `TemplateNode` / `TemplateParser` / `RoleTemplateRenderer` / `Handlers/ThemeSongsHandler`）を集約。各プロジェクトの `LookupCache` 実装は `ILookupCache` インターフェース注入で切り替える設計。

#### Phase 3：音楽クレジット名寄せ移行ツール（`MusicCreditsMigrationForm`）の本格運用化

Catalog 側の音楽クレジット名寄せ移行フォームを大幅拡張し、「テキストはあるが構造化クレジット行が無い」ケースを **未マッチング名義** として一覧化、人物・名義の登録から構造化テーブルへの移行までを 1 ボタンで完結させる：

- **未マッチング名義 ComboBox**（`cboUnmatched`）：6 列（`songs.lyricist_name` / `composer_name` / `arranger_name`、`song_recordings.singer_name`、`bgm_cues.composer_name` / `arranger_name`）に値があるが、対応する構造化クレジット行（`song_credits` / `song_recording_singers` / `bgm_cue_credits`）が**まだ作られていない**テキストを抽出。判定基準は person_aliases の有無ではなく **構造化クレジット行の有無**（既登録 alias でも構造化が無ければ未マッチング扱い）。並びは「最初に紐付く `song_recording_id` 昇順」（運用者がシリーズ古い順に処理しやすい）、bgm 由来は末尾（通常クレジット側で「音楽」役職として表示されるため移行優先度が低い）。
- **新規ダイアログ `UnmatchedAliasRegisterDialog`**：選んだ未マッチングテキストから人物・名義を登録するダイアログ。2 モード対応：
  - **既存人物の新名義として登録**：既存 `Person` を Picker で選び、対象テキストを `alias.name` として新名義を作成（同一人物の別表記名義として追加）。`alias.name_kana` / `name_en` / `display_text_override` は運用者がダイアログで指定。
  - **新規人物としてまとめて登録**：persons の `full_name` / `full_name_kana` / `name_en` を入力 → **同じ値が alias の name / name_kana / name_en にもそのまま採用**される（人物マスタの正式表記をクレジット表記としても使う運用）。フル氏名は半角/全角スペースで分割して `family_name` / `given_name` に自動振り分け。SP が無い場合は lblStatus に警告表示（MessageBox は出さない）。
  - 1 トランザクション内で `persons` → `person_aliases` → `person_alias_persons` の 3 INSERT を実行（途中失敗時は完全ロールバック、孤児データを残さない）。`PersonsRepository.AddPersonWithAliasAsync` / `AddAliasToExistingPersonAsync` をこの目的のために新設。
- **ワンストップ自動移行**：alias 登録完了 → 自動的に「全シリーズ + 全 6 列」で完全一致検索 → 全選択 → 構造化テーブルへ INSERT までを連続実行（`RunOneStopMigrationAsync`）。確認ダイアログは抑止し、完了通知は lblStatus に「`alias_id=N` を登録し、`M` 件を自動移行しました」と表示。元の UI 状態（シリーズフィルタ・列チェック）は finally で復元。
- **既登録 alias の自動利用**：選んだ未マッチングテキストが既に `person_alias` として登録済みなら、ダイアログを開かずに既存 alias を採用してそのままワンストップ移行を実行（`PersonAliasesRepository.FindByExactNameAsync` で同名を検出）。重複登録を防ぎ、既登録だが構造化が未済の名義もボタン 1 つで処理できる。
- **検索の一致判定（SP 正規化）**：`alias.name` と元フリーテキストの両方を半角/全角スペースで正規化してから比較。`alias.name=「青木 久美子」`（SP 有り）と元テキスト=`「青木久美子」`（SP 無し）のような表記揺れがあっても紐付けが成立する。`display_text_override`（ユニット長文表記）はスペースが意味を持つため正規化対象外で、これまで通り厳密一致のまま。
- **検索結果から既移行レコードを除外**：`SearchSongColumnAsync` / `SearchSingerAsync` / `SearchBgmColumnAsync` の SQL に `NOT EXISTS` を追加し、「既に対応する構造化クレジット行が存在する」レコードはグリッドに出さない。未移行のみが表示されるため、運用者は安心して「全選択 → 反映」が押せる（コード側 `ExistsSongCreditAsync` 等の二段ガードは念のため残置）。
- **再入禁止 + 待機カーソル**：OK ボタン Click ハンドラで `btnOk.Enabled = false` を立て、`async` 中の二度押しによる二重 INSERT を構造的に防止。

`CreditEditorForm` のツリー表示でも、楽曲ノードの作詞・作曲・編曲・歌唱は **新構造（`song_credits` / `song_recording_singers`）優先、無ければフリーテキスト** のフォールバック表示に変更（`SongCreditsRepository.GetDisplayStringAsync` / `SongRecordingSingersRepository.GetDisplayStringAsync` を行ごとに呼ぶ方式）。HTML プレビュー側（`ThemeSongsHandler`）と完全に同じロジックを適用し、Catalog ツリーと生成サイト・プレビューで表示の整合性を保つ。

#### v1.3.0 ブラッシュアップで追加されたマイグレーション

| マイグレーション | 内容 |
|---|---|
| `v1.3.0_brushup_add_character_kinds.sql` | `character_kinds` マスタ新設、`characters.character_kind_code` 列追加（旧 enum をバックフィル後撤去） |
| `v1.3.0_brushup_add_song_music_classes.sql` | `song_music_classes` マスタ新設、`songs.music_class_code` 列追加（旧 enum をバックフィル後撤去） |
| `v1.3.0_brushup_song_credits_role_code.sql` | `song_credits.credit_role` を enum → varchar(32) + roles FK へ（値も LYRICS / COMPOSITION / ARRANGEMENT にリネーム） |
| `v1.3.0_brushup_bgm_cue_credits_role_code.sql` | `bgm_cue_credits.credit_role` を enum → varchar(32) + roles FK へ |
| `v1.3.0_brushup_song_recording_singers_role.sql` | `song_recording_singers.role_code` 列追加、PK 拡張（CHORUS 等の複数役職対応） |
| `v1.3.0_brushup_episode_theme_songs_usage_actuality.sql` | `episode_theme_songs.usage_actuality` enum 追加（NORMAL / BROADCAST_NOT_CREDITED / CREDITED_NOT_BROADCAST） |
| `v1.3.0_brushup_add_hide_role_name_in_credit.sql` | `roles.hide_role_name_in_credit` 列追加（Phase 4。HTML クレジット階層で役職名カラムを抑止する表示制御フラグ。LABEL 役職にバックフィル適用） |
| `v1.3.0_stage20_product_companies.sql` | 商品社名マスタ `product_companies` 新設、`products.manufacturer` / `label` / `distributor` の 3 フリーテキスト列を `product_companies` に自動移行のうえ撤去、`label_product_company_id` / `distributor_product_company_id` の FK 列を追加、最頻出社に既定フラグセット、列順整理（stage 20） |

各マイグレーションはバックフィル付きで、適用後に Phase 2 のコード変更を含むビルドへ差し替えること（コード側が新値を期待するため、マイグレ前後で不整合が出る可能性がある）。

#### Phase 4：roles.hide_role_name_in_credit による役職名カラム非表示制御

クレジット階層の HTML 表示で、特定役職について「左カラムの役職名表示」だけを抑止する仕組みを追加。`roles` マスタに **`hide_role_name_in_credit TINYINT NOT NULL DEFAULT 0`** 列を追加し、`Catalog/Forms/CreditMastersEditorForm` の役職タブにチェックボックス UI を新設して運用者が直接切り替えられるようにした。

代表的な用途は **LABEL（レーベル）役職** で、データ上は「マーベラスエンターテイメント」を `LABEL` 役職としての企業クレジット関与として正規に保持しつつ、HTML 表示上はその直前の主題歌ブロックの末尾に屋号だけを並べて、「レーベル」という役職名カラムを出さない、という運用要望に応える。マイグレでは LABEL 役職にだけ `hide_role_name_in_credit = 1` をバックフィルする。

実装上の作用範囲：

- **`PrecureDataStars.Data/Models/Role.cs`** に `HideRoleNameInCredit` プロパティ（byte）追加、**`PrecureDataStars.Data/Repositories/RolesRepository.cs`** の SELECT / UPSERT にも列を追加。
- **`PrecureDataStars.Catalog/Forms/Preview/CreditPreviewRenderer.cs`** と **`PrecureDataStars.SiteBuilder/Rendering/CreditTreeRenderer.cs`** の両方で、役職名解決時 `r.HideRoleNameInCredit == 1` なら `roleName` 変数を空文字に上書き。これにより以降の `<td class="role-name">` セル描画は全て空セルになる（fallback-table / fallback-vc-table / DSL 展開のいずれの経路でも対応）。
- **`CreditMastersEditorForm` 役職タブ**：「クレジット HTML 描画で役職名カラムを非表示にする」チェックボックスを追加。`OnRoleRowSelected` でロード、`SaveRoleAsync` で永続化。

**集計には影響しない**：`CreditInvolvementIndex`（人物・企業・キャラの関与逆引き）、`/stats/roles/{role_code}/` の役職別ランキング、企業詳細の関与一覧などはすべて従来通り `role_code` ベースで動くため、`hide_role_name_in_credit=1` の役職もちゃんと検索でヒットし、関与履歴にも載る。本フラグは純粋に HTML テンプレ側の表示挙動だけを切り替える設計とした。

---

### v1.2.4 — プリキュア本体マスタ追加・キャラクター続柄／家族関係の汎用構造化・声優キャスティング撤去

v1.2.3 まで、音楽・キャラ・人物・企業・クレジット本体と一連のマスタを整備してきたものの、**肝心の「プリキュアそのもの」を 1 行で表現するテーブルが無い** という欠落があった。v1.2.4 ではこの欠落を埋めるため `precures` 本体マスタを新設し、あわせてキャラ間の家族関係を汎用的に表現する `character_relation_kinds` / `character_family_relations` を導入した。同時に、「ノンクレ除いてその役柄でクレジットされている＝キャスティング」という業務ルールが固まったことを受けて、未使用となっていた `character_voice_castings` テーブルを撤去している。

#### スキーマ変更

**`precures` テーブル新設**（プリキュア本体マスタ、PK `precure_id`）：

- 4 つの名義 FK（→ `character_aliases.alias_id`）：`pre_transform_alias_id`（変身前、必須）／`transform_alias_id`（変身後、必須、UNIQUE）／`transform2_alias_id`（変身後 2、強化形態など、任意）／`alt_form_alias_id`（別形態、任意）
- 誕生日：`birth_month TINYINT UNSIGNED`（1-12、任意、CHECK 制約）と `birth_day TINYINT UNSIGNED`（1-31、任意、CHECK 制約）の 2 列に正規化。和文「m月d日」と英文「Month d」の表示はアプリ側で生成
- 声優：`voice_actor_person_id INT NULL`（FK → `persons.person_id`、`ON DELETE SET NULL`）。`character_voice_castings` を廃止する代わりに「プリキュアごとの主担当声優を 1 発で引きたい」用の便宜参照カラム
- 肌色：HSL（`skin_color_h SMALLINT UNSIGNED` 0-360、`skin_color_s` / `_l TINYINT UNSIGNED` 0-100）と RGB（`skin_color_r` / `_g` / `_b TINYINT UNSIGNED` 0-255）を併記
- 属性テキスト：`school VARCHAR(128)` / `school_class VARCHAR(64)` / `family_business VARCHAR(255)` / `notes TEXT` をいずれも NULL 可で配置
- 監査列：`created_at` / `updated_at` / `created_by` / `updated_by` / `is_deleted`（論理削除）
- 整合性トリガ `tr_precures_check_character_bi` / `tr_precures_check_character_bu`：4 本の alias FK が指す `character_id` がすべて同一であることを `BEFORE INSERT` / `BEFORE UPDATE` で検証し、不整合なら `SIGNAL SQLSTATE '45000'` で拒否（業務ルール「変身前後で別キャラになるレギュラープリキュアは存在しない」を DB レイヤーで強制）。MySQL 8.0 では CHECK 制約から別テーブル参照ができないため、`credit_block_entries` の整合性検証と同じトリガパターンで実装。NULL の `transform2` / `alt_form` はチェックスキップ

**`character_relation_kinds` テーブル新設**（キャラクター続柄マスタ、PK `relation_code`）：FATHER / MOTHER / BROTHER_OLDER / BROTHER_YOUNGER / SISTER_OLDER / SISTER_YOUNGER / GRANDFATHER / GRANDMOTHER / UNCLE / AUNT / COUSIN / PET / OTHER_FAMILY の 13 種を初期投入。`name_ja` で和文ラベルを保持。

**`character_family_relations` テーブル新設**（キャラクター家族関係、PK `(character_id, related_character_id, relation_code)`）：`characters` 同士の中間表、汎用なのでプリキュア以外の敵キャラ・脇役にも使える。

**`character_voice_castings` テーブル撤去**：v1.2.0 で導入したが、業務ルール「ノンクレ除いて、その役柄でクレジットされている＝キャスティング」に基づき `credit_block_entries` の `CHARACTER_VOICE` エントリに一元化されたため不要となった。

**`name_en` 列を 4 表に追加**：`person_aliases` / `company_aliases` / `characters` / `character_aliases` の 4 表に `name_en VARCHAR(128) NULL` を追加。`persons` / `companies` は v1.2.0 から既に保有していたが、名義テーブル群と `characters` 自体が持っていなかったため、英文クレジット出力で表記単位の英語名が引けなかった対称性破れを解消。

#### GUI 変更

`CreditMastersEditorForm` のタブ構成を **15 タブ**に再編：先頭に「プリキュア」タブを追加、「キャラクター続柄」「家族関係」タブも追加、「声優キャスティング」タブを撤去、ウインドウサイズを 1100×720 → 1500×850 に拡張。

プリキュアタブには：

- **肌色ピッカー UserControl `SkinColorPickerControl`**：HSL/RGB 両方の入力欄＋ 2 つの色プレビューパネル＋ ΔE バッジ（CIE76 で評価、「✓ 許容範囲 (ΔE<2.3) / △ 要確認 (ΔE<5.0) / × 不一致」）
- **家族グリッド**：編集中プリキュアの変身前 alias から `character_id` を引いて `character_family_relations` を表示・追加・削除
- レイアウトは一覧グリッド 400px + 詳細エディタ 2 カラム化（変身前/変身後・変身後 2/別形態 を左右並列、誕生日と声優を左右並列、学校とクラスを左右並列）。CRUD ボタンは右上に絶対配置、家族グリッドは横全幅化

マスタ管理 4 タブ（人物名義・企業屋号・キャラクター・キャラクター名義）の編集パネルにも「英語表記」テキストボックスを `name_kana` の直下に追加。

---

### v1.2.3 — 音楽系クレジットの構造化（連名・ユニット・キャラ(CV) を中間表で表現）

歌（`songs` / `song_recordings`）と劇伴（`bgm_cues`）のクレジット情報をフリーテキストから **構造化テーブル** に展開した。連名（複数名義の並び）、ユニット名義（連名の中身を持つ親 alias）、キャラ(CV:声優) の語彙、スラッシュ並列表記（「キュアブラック / 美墨なぎさ」）を機械的に再現できる単一モデルにまとめている。既存のフリーテキスト列は **温存** し、構造化行が無い対象では従来通りフリーテキストで表示する **段階的移行** 方式とした。

#### スキーマ変更

**`person_aliases.display_text_override` 列追加**（VARCHAR(1024) NULL）：ユニット名義などで定形外の長い表示文字列が必要なケース用。非 NULL のときアプリ側の表示ロジックは `name` より優先してこの値を使う。

- 通常のユニット（例: `Berryz工房`、`いきものがかり`）→ `name` だけで十分なので NULL のまま
- 定形外（例: `プリキュアシンガーズ+1(五條真由美、池田 彩、うちやえゆか、二場裕美)`、`バッドエンド王国三幹部[ウルフルン(CV:志村知幸) & アカオーニ(CV:岩崎ひろし) & マジョリーナ(CV:富永みーな)]`）→ override に丸ごと格納

**`person_alias_persons` 中間表新設**：ユニット名義の構成メンバーを順序付きで保持。1 alias - N persons の連名関係を表現。

**`song_recording_singers` テーブル新設**（複合 PK `song_recording_id + singer_seq`）：1 録音に対する歌唱者連名を順序付きで保持。`billing_kind` が 2 値：

- `PERSON` — 個人歌唱（例: 五條真由美）。`person_alias_id` 必須
- `CHARACTER_WITH_CV` — キャラ(CV:声優)（例: 美墨なぎさ(CV:本名陽子)）。`character_alias_id` と `voice_person_alias_id` 必須

既存の `SongRecording.SingerName` フリーテキスト列は温存しており、本テーブルに行が無い録音では従来通りフリーテキストが表示に使われる。

**`song_credits` テーブル新設**：歌の作詞・作曲・編曲のクレジット行を保持（連名対応）。`role_code`（LYRICS / COMPOSITION / ARRANGEMENT）と `credit_seq`（同役職内の連名順）でユニーク。

**`bgm_cue_credits` テーブル新設**：劇伴の作曲・編曲のクレジット行を保持（同上）。

#### 移行ルール

既存のフリーテキスト列（`songs.original_lyrics_name` 等）は撤去せず保持。アプリ表示ロジックは「構造化行があればそれを優先、無ければフリーテキスト」のフォールバック型で書く。これにより、構造化を進めながら部分的にしか移行できていない状態でも全曲の表示が壊れない。

---

### v1.2.2 — クレジット一括入力フォーマットの完全可逆化 + 上位レベル備考 UI

v1.2.1 で導入した一括入力ダイアログを「**Draft の任意スコープを文字列化 → 編集 → 戻す**」というラウンドトリップ可能な構造に拡張した。テキストエディタの感覚で既存クレジットを大幅に書き換えたり、特定の役職だけを抜き出して整形し直したりできる。あわせて、Card/Tier/Group/Role の備考列（v1.2.0 から DB に存在していたが GUI 露出が無かった）を編集できる新パネルも導入した。DB スキーマ変更は無し。

#### 一括入力フォーマットの拡張構文

`CreditBulkInputParser` / `BulkParseResult` に v1.2.2 で追加された構文。既存（v1.2.1）の構文は完全互換。

| 入力パターン | 解釈 |
|---|---|
| `[屋号#CIバージョン]`（行全体またはセル） | LOGO エントリ。最右の `#` で「左側＝屋号テキスト」「右側＝CI バージョンラベル」に分解。屋号 alias 名と一致する屋号配下のロゴから `ci_version_label` 完全一致で `logo_id` を引き当てる。未ヒットなら TEXT 降格 + InfoMessage |
| 行頭 `🎬`（U+1F3AC、後続スペースは省略可） | そのエントリを `is_broadcast_only=1` として登録 |
| 行末 ` // 備考` | そのエントリの `notes` に保存 |
| 行頭 `& ` | 直前エントリと A/B 併記（保存時に `parallel_with_entry_id` を引き当て） |
| 役職／グループ／ティア／カード行直後の `@notes=備考` | そのスコープの `notes` に保存 |
| 役職行直後の `@cols=N` | そのロールの `col_count` を N に明示指定 |

#### 逆翻訳エンコーダ `CreditBulkInputEncoder`

Draft 階層を一括入力フォーマットに逆翻訳する新規ファイル。ツリー右クリックメニュー **「📝 一括入力で編集...」** を全レベル（クレジット全体／カード／ティア／グループ／役職）に対応。`CreditBulkInputDialog` に新たに **ReplaceScope モード** を追加し、選択スコープの中身を Encoder で逆翻訳した文字列を初期値としてダイアログを開き、編集後のパース結果でスコープ配下を置換できる。

#### 上位レベル備考編集パネル `NodePropertiesEditorPanel`

Card / Tier / Group / Role を選択時に右ペインで備考を直接編集可能に。

#### A/B 併記の保存フェーズ解決

`CreditSaveService` に **新フェーズ 2.7** を追加。保存時に直前エントリの実 ID を引き当てて `parallel_with_entry_id` 自参照リンクを構築する設計とした。

#### 既存バグ修正

- `card_seq` 等 tinyint 列に大きな退避値が UPDATE される問題（Phase 2.6 / Phase 4 の seq 退避ロジックを呼び出し側で適切な範囲のベース値を渡せるよう改修）
- 話数コピー後の左ペインクレジットリストが古いまま残る問題

---

### v1.2.1 — クレジット一括入力 + 名寄せ機能 + プレビュー改良

クレジット編集の入力負担を大幅に減らす **テキスト一括投入機能** と、マスタ運用を支える **名義の名寄せ機能** を追加した。あわせて、v1.2.0 工程 F でマスタ化された `character_kind` がエディタで ENUM ハードコードのままだったバグを修正し、運用上ほぼ使われていなかった `character_aliases.valid_from` / `valid_to` を撤廃した。

#### クレジット一括入力ダイアログ

クレジット編集画面（`CreditEditorForm`）の左ペイン「📝 クレジット一括入力...」ボタンから新ダイアログ `CreditBulkInputDialog` を開く。複数行テキストとリアルタイムプレビューでクレジット内容をまとめて投入できる。

入力文法（`CreditBulkInputParser`）：

| 入力パターン | 解釈 |
|---|---|
| `XXX:` または `XXX：`（行末コロン） | 役職開始 |
| `-`（半角ハイフン1個・前後トリム後の単独行） | ブロック区切り（ロールは閉じない） |
| `--` | グループ区切り |
| `---` | ティア区切り（最大 tier_no=2） |
| `----` | カード区切り |
| 空行 | 同一ブロック内の改行 |
| `屋号 / 名義` | スラッシュ並列（A/B 併記の前段） |
| `→ 名義` | 同名役職の自動継承 |

姓名分割不能名義は Warning 出力。「協力」役職は文脈依存で「キャスティング協力」にリネーム（VOICE_CAST 役職群の直後にある場合）。

#### 名寄せ機能（人物・企業・キャラの 3 対象に対称展開）

人物名義 / 企業屋号 / キャラ名義の 3 タブそれぞれに「名寄せ」ボタンを追加。重複する alias を 1 つに統合し、参照元を全部書き換える操作を 1 トランザクションで実行する。

#### プレビューレンダラの VOICE_CAST 3 カラムフォールバック

プレビュー HTML で VOICE_CAST 系役職を「キャラ名 / 名義 / 声優」の 3 カラムテーブルとしてレンダリング。VOICE_CAST 役職名のカード跨ぎ省略にも対応（同一役職が複数カードに渡るとき、2 枚目以降は役職名カラムを空にする）。

#### 既存バグ修正

- `character_kind` がマスタバインドされていなかった（v1.2.0 工程 F でマスタ化したが GUI 側 ENUM ハードコードのままだった）
- `NewCreditDialog` のラジオボタン排他バグ
- DnD で Role / Entry を別親に移動して保存すると消える致命バグ

#### 撤廃

- `character_aliases.valid_from` / `valid_to` 列：運用上ほぼ使われていなかった

---

### v1.2.0 — クレジット管理基盤の追加

クレジット管理基盤を新規追加した大型バージョン。シリーズまたはエピソードの OP/ED クレジットを構造化して保持できる新スキーマと、そのマスタ管理 GUI、クレジット本体の編集画面、HTML プレビューまでを一通り整備している。

#### 追加されたテーブル群（16 表 + 既存 2 表への列追加）

**マスタ系**：

- `persons`（人物）/ `person_aliases`（人物名義）/ `person_alias_persons`（共同名義中間表）
- `companies`（企業）/ `company_aliases`（企業屋号）/ `logos`（ロゴ）
- `characters`（キャラクター）/ `character_aliases`（キャラクター名義）/ `character_kinds`（キャラ区分マスタ：PRECURE / ALLY / VILLAIN / SUPPORTING の 4 類型を初期投入）
- `roles`（役職マスタ：脚本・絵コンテ・演出・作画監督などの全役職）
- `role_templates`（役職テンプレ統合テーブル：`series_id IS NULL` で既定、非 NULL でシリーズ別、序数キーで「既定 vs オーバーライド」の構造的非対称を排除）
- `credit_kinds`（OP/ED の表示名マスタ：旧 `ENUM('OP','ED')` を VARCHAR + FK 化）

**クレジット本体（5 段階階層）**：

- `credits`（クレジット本体、シリーズまたはエピソードに紐付く）
- `credit_cards`（カード：OP / ED / ED2 等）
- `credit_card_roles`（ロール：1 役職 = 1 ロール）
- `credit_role_blocks`（ブロック：同一役職内の塊。`leading_company_alias_id` でブロック先頭屋号を保持できる）
- `credit_block_entries`（エントリ：人物名義 / キャラ×声優ペア / 企業屋号 / ロゴ / 歌録音 / フリーテキストのいずれかに型付き）

**主題歌**：

- `episode_theme_songs`（エピソードの主題歌・挿入歌・本放送限定行）

**既存 2 表への列追加**：

- `series_kinds.credit_attach_to`（SERIES / EPISODE）：TV シリーズ系はエピソード単位、映画系はシリーズ単位でクレジットを持つ運用を表現
- `part_types.default_credit_kind`（OP / ED）

整合性は CHECK 制約とトリガーの併用で担保（CASCADE FK 列を含む整合性は MySQL 8.0 の Error 3823 制約のためトリガーで実装）。

#### クレジット系マスタ管理 GUI

`PrecureDataStars.Catalog` メニューに「クレジット系マスタ管理」を新設し、13 タブ（人物 / 人物名義 / 企業 / 企業屋号 / ロゴ / キャラクター / キャラクター名義 / 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 / シリーズ種別 / パート種別）を備えた `CreditMastersEditorForm` を追加（v1.2.4 で 15 タブに再編）。

#### クレジット編集画面 `CreditEditorForm`

Card / Tier / Group / Role / Block / Entry の階層構造を 4 ペイン GUI（左：絞込みとクレジット選択／中央：階層ツリーと編集ボタン／右：エントリ編集パネル／さらに右：HTML プレビュー）で編集可能。

**Draft セッション方式（全面メモリ化）**：ユーザーの操作は一旦メモリ上の Draft オブジェクトに反映され（カード追加・並べ替え・エントリ編集・DnD 移動など）、画面下部の「💾 保存」ボタン押下時に `CreditSaveService` が 5 フェーズ（1A エントリ削除 → 2 新規作成 → 3 更新 → 1B 親階層削除 → 4 seq 整合性）を 1 トランザクション内で実行して DB に確定する。1B（ブロック以上の親階層 DELETE）を 3（更新）の後ろに置く構成は v1.3.0 で導入。これにより、DnD で別ブロックに移動したエントリ等が、旧親 DELETE の CASCADE で巻き添え削除される事故を防いでいる（更新フェーズで既に DB 上の親 FK が新親に切り替わっているため、CASCADE の対象に該当しない）。未保存中はツリー背景色が薄い黄色になりステータスバーに「★ 未保存の変更あり」が表示される。クレジット切替・シリーズ／エピソード切替・フォーム閉じ時には未保存変更がある場合の確認ダイアログ（保存して続行／破棄して続行／キャンセル）を出してデータロストを防ぐ。

**クレジット話数コピー**：左ペインの「📋 話数コピー...」で、現在選択中のクレジットを別シリーズ・別エピソードへ丸ごと複製。シリーズ跨ぎコピーで「前作の OP 構造を新シリーズの第 1 話に流用してから差分編集する」運用に対応。

**HTML プレビュー（常時表示）**：編集画面の中央右に 4 ペイン目として埋め込む 3 段ネスト SplitContainer 構成（左 320 / 中央 / プレビュー 920 / 右 380、`ClientSize` 2240×880）。Draft セッションの内容を 250ms デバウンスでリアルタイム描画するため、保存していない編集状態がそのまま見える。`RenderDraftAsync` が Draft の Card/Tier/Group/Role/Block/Entry を直接走査して HTML 化（DB を経由しない）。プレビュー HTML の CSS には階層余白（card 18px / tier 12px / group 8px / role 4px）を入れて構造の切れ目を視覚化。OP/ED の並び順は `KindOrder` 関数で OP=1, ED=2 の固定順。

**役職テンプレート展開**：`role_templates` 統合テーブルから `(role_code, series_id) → (role_code, NULL)` の優先順で解決し、`RoleTemplateRenderer` で DSL を展開して HTML 化する。テンプレ未定義の役職は「役職名（左固定幅）+ ブロック内エントリを `col_count` で横並び」のフォールバック表で表示する（実物のスタッフロール風）。EPISODE スコープのクレジットでも `episodes` テーブルを逆引きしてシリーズ別テンプレが正しく適用される。テンプレに `{ROLE_NAME}` プレースホルダが含まれない場合、レンダラが自動的にフォールバック表と同じ「役職名カラム + 内容カラム」の HTML テーブルでラップし、テンプレ役職とフォールバック役職が同じ位置で整列する自動 2 カラムラップ機能を持つ。

**テンプレ DSL**：`{ROLE_NAME}` / `{#BLOCKS}` などの基本プレースホルダに加え、主題歌役職用の `{#THEME_SONGS:opts}...{/THEME_SONGS}` ループ構文をサポート。楽曲スコーププレースホルダ `{SONG_TITLE}` / `{SONG_KIND}` / `{LYRICIST}` / `{COMPOSER}` / `{ARRANGER}` / `{SINGER}` / `{VARIANT_LABEL}` を内側で参照可能。これにより主題歌役職の表記（カギ括弧の種類・「作詞:」ラベル・項目順・改行位置）をテンプレ作者が完全制御できる。

#### 設計判断のポイント

- 旧設計の `roles.default_format_template`（既定）と `series_role_format_overrides`（シリーズ別上書き）の二箇所運用を廃止し、`role_templates` 単一テーブルに統合
- プレビュー HTML は「オープニングクレジット／エンディングクレジット」と日本語表示し、CSS は枠線・カード見出しを撤廃してプレーンテキスト風に。テンプレ展開結果は HTML 素通し（`<b>` 等のタグが効く）、ロゴ表示は屋号名のみ（CI バージョンラベル非表示）
- 改行コードの取扱いを全面正規化：MySQL TEXT 列由来の `\r\n / \r / \n` 混在を 3 段階（マスタエディタ表示時 / プレビューレンダラ / 主題歌ハンドラ）で統一処理し、CSS から `white-space: pre-wrap` を撤去して `<br>` のみで改行制御する方針

---

### v1.1.5 — CDAnalyzer のドライブ占有解消 + Blu-ray プレイリスト全走査の導入

#### (1) CDAnalyzer のメディア種別自動判定によるドライブ占有解消

CDAnalyzer と BDAnalyzer を同時起動した状態で DVD / Blu-ray を投入すると、CDAnalyzer 側がドライブを SCSI コマンドで一時的に占有し、BDAnalyzer のファイル I/O（`VIDEO_TS.IFO` / `*.mpls` の読み込み）に悪影響が出る問題を修正。

CDAnalyzer がメディア挿入を自動検知した直後に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系（CD-ROM / CD-R / CD-RW）以外のメディアであれば後続の SCSI コマンド（READ TOC / READ SUB-CHANNEL / CD-Text 取得）を一切発行せず即座にデバイスハンドルをクローズするように変更。自動トリガ時はメッセージボックスを抑止して BDAnalyzer の作業を妨げない一方、ユーザの「読み取り」ボタン操作時は検知メディア種別を案内するダイアログを表示する（`silent` パラメータで分岐）。

#### (2) Blu-ray のプレイリスト全走査でディスク内の全タイトルを抽出

BDAnalyzer の Blu-ray 解析が `BDMV/PLAYLIST/00000.mpls` か `00001.mpls` だけを 1 個拾って読む実装になっていたため、ディスク内の他の有意なプレイリスト（複数話収録の各話プレイリストや特典プレイリスト等）が拾えていなかった問題を修正。

`BDMV/PLAYLIST/*.mpls` を全走査して有意なタイトルを抽出する「フォルダ全走査モード」を Blu-ray 側にも導入し（DVD 側で v1.1.1 から動作している `VIDEO_TS` 全走査の Blu-ray 版）、短尺ダミー（FBI 警告・配給ロゴ・レーベルロゴ等、デフォルト 60 秒未満）と重複プレイリスト（anti-rip スキームの 99 個重複等）を自動除外して、複数のメイン的プレイリストを並列して取り込めるようにした。

#### (3) 新規商品登録ダイアログの操作性改善 + 品番例・既定値の見直し

新規商品登録ダイアログ（`NewProductDialog`）の操作性を改善：

- 価格欄を NumericUpDown から TextBox に変更してスピンボタンを廃止
- 税抜価格を入れた時点で発売日に対応する日本の標準消費税率を適用した税込価格を自動計算して読み取り専用フィールドに表示（税率は発売日が 2019-10-01 以降なら 10%、2014-04-01〜2019-09-30 は 8%、それ以前の境界も同様に切り替わる）
- 発売元 `MARV` / 販売元 `SMS` を初期値として埋めるよう変更

#### (4) 商品・ディスク管理画面で 1 枚物商品のディスク詳細フォームが空になる不具合を修正

`ProductDiscsEditorForm` で、ディスクが 1 枚しか無い商品を選択するとディスク詳細フォームが空のままになる不具合を修正。`DataGridView.SelectionChanged` の発火タイミング（新旧 DataSource の現在行 index がいずれも 0 のままだと発火しない仕様）への依存を解消し、`RebindDiscGrid()` ヘルパで先頭行の明示選択 + 詳細フォーム反映を直接実行する経路に統一。複数枚商品でも商品選択直後に先頭ディスクの詳細が即座に見えるようになる副次効果あり。

---

### v1.1.4 — 商品・ディスク管理と既存商品への追加ディスク登録の挙動改善

- **(1) 商品・ディスク管理画面でディスクを保存しても物理情報を消さない**：CDAnalyzer / BDAnalyzer が読み取ったディスク総尺などの物理情報が、Catalog 側の保存で意図せず NULL クリアされる不具合を修正
- **(2) 既存商品への追加ディスク登録で、既存品番を入れても上書きされないように**：明示的にエラーで停止する挙動に変更
- **(3) `DiscMatchDialog` の候補が 1 件のとき自動選択する**
- **(4) ディスク・トラック閲覧画面の上下ペインを半々で自動追従**：ウインドウリサイズ時に常に半々で維持
- **(5) 商品・ディスク管理画面のレイアウト刷新**：上下 2 段（上＝商品エリア / 下＝ディスクエリア）に再構成、それぞれ左 60% に一覧、右 40% に詳細エディタを配置してエディタ領域の窮屈さを解消
- **(6) マスタ管理画面の改善**：外周余白を確保しつつ「新規」ボタンを追加して新規追加と既存編集の操作を明確化、`display_order` をマウスドラッグで並べ替え→「並べ替えを反映」ボタンで一斉 UPSERT する操作フローを新設、監査列（CreatedBy / UpdatedBy / CreatedAt / UpdatedAt）を全タブで自動非表示に

---

### v1.1.3 — データ入力 UI の大幅刷新

データ入力 UI を大幅に刷新したバージョン。

- **(1) Catalog GUI の編集フォーム再編**：商品とディスクを 1 画面に統合した「商品・ディスク管理」、トラック編集を独立させた「トラック管理」を新設
- **(2) 税込価格の自動算出**：発売日ベースで日本の標準消費税率を適用した税込価格を自動算出して `products.price_inc_tax` 列に格納（バックフィル SQL `db/utilities/backfill_products_price_inc_tax.sql` も同梱）
- **(3) トラック管理の SONG/BGM オートコンプリート選択**：曲名や M 番号の途中入力で候補リストが出る
- **(4) 劇伴の仮 M 番号フラグ**：`bgm_cues.is_temp_m_no` 列追加。確定 M 番号が決まる前に音源を仮登録できる
- **(5) 歌・劇伴マスタの CSV 一括取り込みと入力補完**：`SongCsvImportService` / `BgmCueCsvImportService` を `PrecureDataStars.Catalog.Common` に追加
- **(6) 既存商品への追加ディスク登録フロー**：既に登録済みの BOX 商品（Disc 1 だけ登録済み）に Disc 2 として新しい BD を追加するための `DiscRegistrationService.AppendDiscToExistingProductAsync` を追加。組内番号 `disc_no_in_set` は呼び出し側で指定させず、本メソッドが自動採番する（既存ディスクと新ディスクをまとめて品番昇順ソートして 1 始まりの連番に置き換える、歯抜けがあってもきれいに整列）

---

### v1.1.2 — ディスク・トラック閲覧 UI の整理 ＋ songs カラム名整理

- ディスク・トラック閲覧画面の表示ロジックを整理（翻訳値で一覧表示、ディスク総尺・トラック尺ともに M:SS.fff で表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）
- `songs` テーブルの `original_` 接頭辞を撤去（`original_lyrics_name` → `lyrics_name` 等）。マイグレーション SQL `v1.1.2_rename_song_columns.sql`

---

### v1.1.1 — series_id の所在移設 + 長さ単位の是正

- `series_id` の所在を `products` から `discs` に移設（1 商品が複数シリーズに跨る BOX 商品に対応するため）。`v1.1.1_move_series_id_to_disc.sql`
- 長さ単位の是正：CDAnalyzer / BDAnalyzer が読み取った尺情報の単位を統一。`v1.1.1_fix_length_units.sql`
- DVD の `VIDEO_TS` 全走査モード導入（複数話収録 DVD に対応）

---

### v1.1.0 — 音楽・映像カタログ拡張

音楽・映像カタログ系テーブル群を新規追加。商品・ディスク・トラック・歌・劇伴の 5 階層を MySQL に保持する基盤を導入。CDAnalyzer / BDAnalyzer から DB 連携で新規商品＋ディスク登録ができるようにした。マイグレーション SQL `v1.1.0_add_music_catalog.sql`。

---

### v1.0.x

エピソード管理機能の初期リリース（シリーズ・エピソード・パート構成、MeCab かな／ルビ、YouTube クローラー、文字統計、CDAnalyzer／BDAnalyzer の読み取り専用版）。

---

## ライセンス

[MIT License](LICENSE) © 2025 Shota (SHOWTIME)
