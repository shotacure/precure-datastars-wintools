# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）**、および **クレジット情報（OP / ED の階層構造、人物・企業・キャラクター・プリキュアの各マスタ）** を MySQL データベースで管理するためのアプリケーション集です。**v1.3.0 で Web 公開用の静的サイトジェネレータ `PrecureDataStars.SiteBuilder` を新設**し、ローカル MySQL の内容をそのまま静的 HTML として書き出せるようになりました。

> **v1.3.1** — `PrecureDataStars.SiteBuilder` の UX 改善。全ページ共通の **SNS シェアボタン群**（X / Facebook / Bluesky / はてなブックマーク / LINE / URL コピー の 6 種）、**SVG ファビコン**、**OGP 拡張**（サイト共通既定 OG 画像の自動補完 + Twitter カード `summary_large_image` 化、`twitter:site` / `twitter:creator` の帰属メタタグ、`BreadcrumbList` 構造化データ、ホームの `WebSite` + `SearchAction` および `Organization` 構造化データ）、**ヒーローのグラデ背景**、**統計ランキング表のメダル装飾**（CSS `:has()` でテンプレ無変更）を追加。あわせて **プライバシーポリシー** (`/privacy/`)・**免責事項** (`/disclaimer/`)・**お問い合わせ** (`/contact/`) の 3 ページに加えて **404 ページ** (`/404.html`) を新設し、フッタの運営情報リンク群から導線を確保。SEO 補助ファイルも整備し、**`robots.txt` を多 User-agent ブロック構成**（AI 学習・SEO 解析系のリソース消費が大きい一部クローラへの個別 Disallow + 主要検索エンジンへの控えめな `Crawl-delay`）に拡張、AdSense クライアント ID 設定時は **`ads.txt`** を IAB 標準形式の 1 行で自動出力。`App.config` に既定 OG 画像 URL 設定 `DefaultOgImage` キーを追加。**エピソード・シリーズ・人物・楽曲・商品・キャラクター・企業・プリキュア の全詳細ページの `MetaDescription` を実データから動的構築**（放送日・主要スタッフ・主題歌・主役声優・主要役職・歌手・作詞作曲・発売元・収録曲数など）し、**`TVEpisode` / `TVSeries` / `Movie` / `Person` / `MusicComposition` / `MusicAlbum` / `Organization` などの JSON-LD に `description` をはじめ各種プロパティ**（`director` / `creator` / `actor` / `genre` / `jobTitle` / `numberOfTracks` 等）を追加して検索結果のリッチスニペット候補を増やした。さらに **アクセシビリティ強化**として「本文へスキップ」スキップリンクと **印刷用スタイル**（`@media print` でナビ・シェアボタン・フッタリンク等を非表示化、本文だけを印字）を実装。サイドナビ（ページ内セクションナビ）も再設計し、件数バッジが ○ マーカーを兼ねる **3 列 grid 構造**（年度＝右揃え / マーカー＝中央 / ラベル＝左揃え）と、縦進捗線がマーカー中央を貫通する整列を実現。あわせて **シリーズ間関係マスタ `series_relation_kinds` に逆向き表示名カラム** (`name_ja_reverse` / `name_en_reverse`) を追加（マイグレーション SQL `db/migrations/v1.3.1-series-relation-reverse.sql` 同梱）、これを用いて **シリーズ詳細の関連作品セクションを統合再設計**（旧「併映・子作品」+「関連作品」の 2 セクション分割を撤廃し、`#related-works` 1 セクションに統合 + `relation_kind` バッジ表示）。**シリーズ詳細メインスタッフ表示** は旧 `<h3>役職名</h3>` + `<ul>` 構造を廃止して役職ごと 1 行のバッジ形式（2 列 grid：左バッジ右揃え / 右人名左揃え、長さ違いバッジでも縦境界が揃う）に、**エピソード詳細スタッフ表示** も同様にバッジ形式へ統一（旧 `<table class="staff-table">` 廃止）。**シリーズ詳細エピソード一覧** は `/episodes/` ランディングと意匠を揃え、第N話 + 放送日の縦積み左ブロック + サブタイトル（ルビ付き、`<br>` を除去して 1 行表示）+ スタッフバッジ群の構造に再構築。**エピソード詳細のオープニング/エンディングクレジット内主題歌ブロック** で「作詞 / 作曲 / 編曲 / 歌」の役職ラベルと各クレジット名義を **両方ともリンク化済み HTML** で出力するように経路を改修（テンプレ展開エンジン `PrecureDataStars.TemplateRendering` の `ThemeSongsHandler` 改修 + `ILookupCache` を `PrecureDataStars.Data` プロジェクトへ移管、`SongCreditsRepository` / `SongRecordingSingersRepository` に HTML 版 `GetDisplayHtmlAsync` を新設、`LookupCharacterAliasHtmlAsync` を `ILookupCache` に追加）。**エピソード詳細の主題歌・挿入歌セクション** では「歌」役職ラベルもリンク化、メタ行（歌 / 作詞 / 作曲 / 編曲）を `display: inline-flex` で 1 行に並列化、種別ラベル（OP / ED / 挿入歌）は `song_music_classes` マスタの `name_ja` から「オープニング主題歌」「エンディング主題歌」「挿入歌」など正式名称で表示。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
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
    │   ├── v1.3.0_stage21_role_link_placeholder.sql … v1.3.0 続編 stage 21（テンプレ DSL に `{ROLE_LINK:code=...}` プレースホルダ追加に伴う `SERIALIZED_IN` テンプレ更新）
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

#### 連載（`SERIALIZED_IN`） + 漫画（`MANGA`） — v1.3.0 stage 19 で構造変更、v1.3.0 続編 stage 21 で役職ラベルをリンク化

**背景の変更**: v1.3.0 stage 19 までは、連載クレジット下の漫画家を `SERIALIZED_IN` 役職下に PERSON エントリとして同居させていたが、`CreditInvolvementIndex` の集計で漫画家が「連載」役職として誤集計される問題があった。stage 19 で **役職を 2 つに分割**：

- `SERIALIZED_IN`「連載」: 雑誌（COMPANY エントリ）のみ
- `MANGA`「漫画」: 漫画家（PERSON エントリ）のみ

表示上は `SERIALIZED_IN` テンプレの中で **兄弟役職参照構文 `{ROLE:MANGA.PERSONS}`** を使い、同 Group 内の MANGA 役職下の人物を取り込む。これにより画像 1 のレイアウト（「漫画・上北 ふたご」を「連載」見出しの直下に表示）を保ちつつ、集計は `MANGA` → 「漫画」、雑誌 → 「連載」と正しく分かれる。

v1.3.0 続編 stage 21 で、テンプレ内に直書きされていた `<strong>漫画</strong>` 部分を新プレースホルダ **`{ROLE_LINK:code=MANGA}`** に置換し、役職統計ページ `/stats/roles/MANGA/` への太字リンクに昇格させた。サイト上のクレジット内要素（人物名義／屋号／ロゴ）はすべて詳細ページへリンクしていたのに対し、ここだけがプレーンテキストでリンク化されていなかった問題の解消。`<strong>` ラップはレンダラ側で一律付与されるため、テンプレ作者は `<strong>` を書かない。「役職リンクなら必ず太字、違えば太字ではない」という見た目ルールを DSL の責務として保証する設計。

**テンプレ（`SERIALIZED_IN`、v1.3.0 続編 stage 21 で更新）**:
```
{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
{ROLE_LINK:code=MANGA}・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

**テンプレ（`MANGA`、stage 19 で新設）**:
```
{PERSONS}
```

`MANGA` テンプレは普段 `{ROLE:MANGA.PERSONS}` 経由で間接的に評価される。`MANGA` 役職を独立に描画する状況（クレジット画面で漫画役職だけのカードを作るレアケース）に備えて単純な `{PERSONS}` を持たせる。

**期待する展開結果（プレーンテキスト相当のレイアウト）**:
```
連載  講談社「なかよし」
      漫画・上北 ふたご
       「たのしい幼稚園」
       「おともだち」ほか
```

**期待する HTML 出力（SiteBuilder 側、stage 21 適用後）**:
```html
<a href="/companies/6/">講談社</a>「<a href="/companies/6/">なかよし</a>」
<strong><a href="/stats/roles/MANGA/">漫画</a></strong>・<a href="/persons/5/">上北 ふたご</a>
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

**ブロック構成（stage 19 マイグレーション後、stage 21 でも変更なし）**:
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
- **`{ROLE_LINK:code=MANGA}`** が stage 21 の新プレースホルダ。役職コードから役職統計ページへのリンク化済み HTML を太字付きで埋め込む。SiteBuilder 側は `<strong><a href="/stats/roles/MANGA/">漫画</a></strong>`、Catalog 側プレビューは `<strong>漫画</strong>`（リンクなし）を出力。`<strong>` ラップはレンダラ側で一律付与されるため、テンプレに `<strong>` を書かない運用に統一した（「役職リンクなら必ず太字」を DSL レンダラの責務として保証）。
- **`{ROLE:MANGA.PERSONS}`** は stage 19 の新構文（兄弟役職参照）。同 Group 内で `role_code='MANGA'` の役職を 1 つ探し、その役職配下の Block 群を一巡りして `{PERSONS}` を Block ごとに評価し、Block 間は内側プレースホルダの `sep` オプション（既定 `、`）で連結する。
- `{ROLE:CODE.PLACEHOLDER}` の **1 段ネスト不可**: `MANGA` テンプレ内で `{ROLE:SERIALIZED_IN.…}` と書いても無限ループせず、再帰経路の `{ROLE:…}` は空文字に展開される（無限ループ防止）。
- 旧 v1.3.0 stage 18 までのデータ構造（`SERIALIZED_IN` 配下に PERSON 同居）は **stage 19 のマイグレーション SQL**（`db/migrations/v1.3.0_stage19_manga_role_split.sql`）で自動的に新構造に変換される。冪等性あり、再実行可能。v1.3.0 続編 stage 21 のマイグレーション SQL（`db/migrations/v1.3.0_stage21_role_link_placeholder.sql`）も冪等で、`format_template` に既に `{ROLE_LINK:code=MANGA}` が含まれていれば UPDATE 対象から除外する。
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
| `title_short` | VARCHAR(128) NULL | 略称（例: 「キミプリ」）。v1.3.0 stage22 後段以降、**SiteBuilder の生成出力（Web 表示）では本列を一切参照しない方針**。DB マスタ上は引き続き保持し、Catalog 側の編集 UI（コンボの省スペース表示など）でのみ使用する |
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

各バージョンの変更履歴は概略のみを記載しています。工程単位の試行錯誤や変更ファイル一覧などの詳細は、Git のコミット履歴および GitHub のリリースノートを参照してください。

### v1.3.1 — `PrecureDataStars.SiteBuilder` の UX 改善

Web 公開サイトの利用体験・流入・運営面を一通り整備したリビジョン。

- **SNS シェア導線**：全ページ共通のシェアボタン群（X / Facebook / Bluesky / LINE / URL コピー）を追加。
- **OGP / 構造化データの拡張**：エピソード詳細（`TVEpisode`）・シリーズ詳細（`TVSeries` / `Movie`）・人物詳細（`Person`）をはじめ全詳細ページで `MetaDescription` を動的構築し、JSON-LD を拡充。ホームに `Organization` 構造化データを追加。
- **ブランディング**：SVG ファビコン + ブランドカラー、ヒーローセクションのグラデ背景、統計ランキング表のメダル装飾。
- **運営情報ページ**：運営者情報・プライバシーポリシー・お問い合わせの 3 ページを新設。
- **SEO 補助ファイル**：`robots.txt` を多 User-agent 構成に、`ads.txt` を自動出力。`/404.html` を新設。
- **アクセシビリティ**：スキップリンク + 印刷用スタイルを追加。
- **ナビゲーション**：ページ内セクションナビを再設計。
- **データ**：シリーズ間関係マスタに逆向き表示名カラムを追加。`ILookupCache` を `PrecureDataStars.Data` へ移管し `LookupCharacterAliasHtmlAsync` を追加。主題歌・挿入歌セクションを HTML 経路化（役職・名義をリンク化）。シリーズ詳細のメインスタッフ表示を 2 列 grid バッジ形式へ、エピソード詳細スタッフ表示を統一バッジ形式へ再設計。

### v1.3.0 — Web 公開用静的サイトジェネレータ `PrecureDataStars.SiteBuilder` の新設

ローカル MySQL を読み出して、シリーズ・エピソード・人物・企業・キャラクター・プリキュア・楽曲・劇伴・商品・字幕統計などの静的 HTML サイトを生成する新プロジェクト `PrecureDataStars.SiteBuilder`（コンソール）を追加した。役職テンプレ DSL の展開エンジンは共通ライブラリ `PrecureDataStars.TemplateRendering` として分離している。本バージョンは公開準備の過程で多数の調整・ブラッシュアップを重ねており、ここではその到達状態の要点のみを記載する。

**サイト生成の骨格**

- 出力ページ：ホーム、シリーズ一覧／詳細、エピソード一覧（`/episodes/` ランディング）／詳細、人物一覧／詳細、企業・団体一覧／詳細、キャラクター一覧／詳細、プリキュア一覧／詳細、楽曲一覧／詳細（`/songs/`）、劇伴一覧（`/bgms/{series}/`）、商品索引／詳細、各種字幕・統計ページ、404 ページ。
- クレジット階層（カード／Tier／グループ／ブロック／エントリ）を HTML 化し、役職・人物・企業・ロゴ・キャラ名義をリンク化して描画。
- テンプレートエンジンは Scriban。共通レイアウト・サイト内検索（クライアント側 JS）・セクション内ナビ（左サイド縦タイムライン型）・モバイル時のハンバーガーメニューを備える。
- SEO・アナリティクス（メタ情報・サイトマップ・構造化データの基盤）を整備。

**スキーマ変更（v1.3.0 マイグレーション）**

- `episode_uses` テーブルを新設：エピソードのパート内で流れた音声（歌・劇伴・ドラマ・ラジオ・ジングル・その他）を記録し、楽曲・劇伴の使用箇所逆引きを可能にする。
- `product_companies` マスタを新設：商品の発売元（label）／販売元（distributor）をクレジット非依存の社名マスタ ID で表現する設計に統一。フリーテキストのレーベル列は廃し、既定フラグ（`is_default_label` / `is_default_distributor`）で新規登録時の既定社を指定する。
- 連載クレジット整理：漫画役職 `MANGA` を分離。テンプレ DSL に兄弟役職参照構文 `{ROLE:CODE.PLACEHOLDER}` と役職リンク化プレースホルダ `{ROLE_LINK:code=ROLE_CODE}` を追加。
- `series_precures` テーブルを新設：シリーズ × プリキュアの多対多関連を表現。
- `series_kinds` にスピンオフ系の細分化種別を追加。シリーズ間関係マスタに逆向き表示名カラムを追加。

**サイト仕様の確定事項**

- `series.title_short` の生成・UI 使用を全面廃止し、出力には必ずシリーズ正式名を用いる。複数シリーズ名が並列に現れる箇所には開始年（西暦 4 桁）を併記する。
- 商品索引はジャンル別・シリーズ別の 2 タブ構成。商品詳細の発売元・販売元は `product_companies` の社名で表示。
- 楽曲索引は録音バリエーション（`song_recording_id`）単位で表示。作詞・作曲・編曲は構造化クレジットから出力し、フリーテキストはフォールバック。主題歌使用エピソードは範囲集約して表示。
- シリーズ一覧では映画系を独立セクションに分け、子作品扱いは `MOVIE_SHORT` に限定。関連作品は単一セクションに統合。
- 声優出演統計は character_kind による分割をせず 1 リストで集計。スタッフ表示はサイト全体で役職色付きバッジに統一。
- 劇伴一覧では未収録 cue にも「（未収録）」のサブ行を明示。仮 M 番号 cue も閲覧 UI に表示。
### v1.2.4 — プリキュア本体マスタ追加・キャラクター続柄／家族関係の汎用構造化・声優キャスティング撤去

プリキュア本体を 1 行で表現する `precures` マスタを新設し、キャラ間の家族関係を汎用的に表現する `character_relation_kinds` / `character_family_relations` を導入。あわせて「ノンクレ除いてその役柄でクレジットされている＝キャスティング」という業務ルールに基づき `character_voice_castings` テーブルを撤去した。

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

クレジット編集の入力負担を減らす **テキスト一括投入機能** と、マスタ運用を支える **名義の名寄せ機能** を追加。あわせて `character_kind` の ENUM ハードコード不具合を修正し、`character_aliases.valid_from` / `valid_to` を撤廃した。

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