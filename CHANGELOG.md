# 変更履歴 — precure-datastars-wintools

本ファイルは `README.md` から移設した全バージョンの変更履歴です。概略のみを記載しています。工程単位の試行錯誤や変更ファイル一覧などの詳細は、Git のコミット履歴および GitHub のリリースノートを参照してください。

### v1.3.7 — コード／コメント／README／CSS の縮約（機能・出力・DB 不変）

挙動・生成 HTML・DB・スキーマ・テンプレートを一切変えず、ソースのコメントと README/CSS の冗長記述のみを縮約し、Repomix 集約時のコンテキスト容量を削減したリビジョン。

- **コメントの機械的縮約**：全手書き `.cs` に対し、装飾区切りコメント（`// ────` 等）・中身が空の XML doc 行（`/// <para>` 等）・連続空行を除去。コード行（コメント・空行以外）はバイト単位で不変であることを全ファイル検証済み。
- **純動作説明コメントのみ要点化**：3 行以上連続する `//` ブロックと多行 `/// <summary>` のうち、設計判断・意図・整合性・落とし穴・順序依存・トレードオフなど「なぜそうしたか」を一切含まない純粋な動作説明だけを要点化。設計根拠を含む塊は温存に倒すガードで丸ごと残す（`<param>` / `<returns>` 等は不変）。
- **site.css の整理**：多行 `/* */` 設計理由バナーを要点 1 行へ圧縮し連続空行を詰めた。セレクタ・宣言・値は不変（コメント除去後のルール本体が縮約前と完全一致することを検証済み）。
- **README の純化**：冒頭の版数ブロックを「最新版 1 行＋ CHANGELOG 参照」へ再圧縮（v1.3.2 で確立した方針の再適用）。CD 登録節の実装内部ブロック引用（MCN/ISRC の CDB バイト構成、ISRC リトライ内部、ジャケット画像フェーズ方針など）を仕様要約へ凝縮し、メカニズム詳細はコードと本ファイルを一次情報とする旨に整理。散在していた履歴的ナレーション（「〜を統合した」「撤去」「v1.0 から変更なし」等）を現状仕様文へ簡約。全機能・全スキーマの網羅性は維持。
- 縮約規模：手書き `.cs` ＋ `site.css` で約 7,500 行減（コード挙動は不変）。設計意図コメントを保全する方針のため、純ノイズと冗長な動作説明の除去に範囲を限定。
- **既存記述ミスの修正**：`CreditBulkApplyService.ResolveLogoAsync` の XML doc が縮約前から二重 `<summary>`（開き 2・閉じ 1）になっていたのを、ステイの開始タグ 1 行のみ削除して単一の整形済み doc に修正（本文の文言・情報は無損失。コード不変）。
- **統計エピソード単位ページの脱テーブル化**：パート尺（A/B・アバン 各長短）・アバンスキップ回・中 CM 入り時刻（早遅）・サブタイトル文字数/漢字率/記号率（多少）の計 15 ページを `<table>` から専用 `stats-ep-list` レイアウトへ全面置換。1 行＝左（順位＋指標値〔数値のみ〕を横並び・本文の約 1.77 倍・`<li>` 内上下中央）／右（上段「シリーズ名〔シリーズ詳細リンク〕 (年度)」・下段「第N話のみ〔シリーズ詳細のエピソード一覧と同装い・放送日は出さない〕＋ルビ付き改行除去サブタイトル〔エピソード詳細リンク〕」）。同率順位は 2 件目以降の順位表示を空にし（指標値は常に表示・順位カラムは固定幅で整列）、1〜3 位装飾は廃止。アバンスキップ回は順位・指標値なし（左ブロック省略）。ルビ補完は追加 SQL なしで `BuildContext.LookupEpisodeBySeriesEpNo` により解決し、行組み立ては共有 `StatsEpisodeRows` に集約（`EpisodePartStatsGenerator` / `SubtitleStatsGenerator` から再利用）。集計ロジック・順位・対象範囲は不変。左の順位＋指標値は淡いアクセント地＋左アクセントバーの角丸パネルで装飾（1〜3 位の特別装飾は持たない）、行間も拡張。

### v1.3.6 — CDAnalyzer の MCN/ISRC 修正・トラック整合トリガー修正・商品詳細の物販強化

CDAnalyzer の物理情報読み取りの根本不具合修正を起点に、DB 整合トリガーの修正と、公開サイト商品詳細の情報量・収益化動線の強化までを含むリビジョン。複数ステージで構成。

- **CDAnalyzer の MCN/ISRC 読み取り修正**：READ SUB-CHANNEL (0x42) の CDB バイト構成が MMC 仕様とずれており（SubQ ビット・データフォーマット・トラック番号の配置誤り）、MCN・ISRC が一切取得できていなかった不具合を修正。応答解析を MCVal/TCVal 有効ビット検証＋固定オフセット方式に堅牢化し、有効ビット非対応ドライブ向けのレニエントなフォールバック解析を温存。
- **ISRC の SEEK 付きリトライ取得**：多くのドライブが ISRC をヘッド近傍トラックでしか返さない特性に対応し、SEEK(10) (0x2B) で対象トラック先頭へ移動してから READ SUB-CHANNEL を行うリトライを追加。読み取りは 2 パス構成（第 1 パスで全トラック 1 回ずつ→ディスク内に 1 件でも取得できれば「ISRC 収録盤」と判定し未取得トラックのみ最大 5 回まで SEEK 併用で再試行、1 件も取れなければ未収録盤として再試行しないディスク単位ゲート）。
- **tracks content_kind 一貫性トリガーの UPSERT 誤検知修正**：`trg_tracks_bi_fk_consistency`（BEFORE INSERT）の content_kind 一貫性チェックに、同一 PK `(catalog_no, track_no, sub_order)` が既存する場合（＝実質 UPDATE）はスキップするガードを追加。`INSERT ... ON DUPLICATE KEY UPDATE` で BEFORE INSERT が先に発火する際、INSERT VALUES 側の暫定 `content_kind_code`（物理情報 UPSERT では `'OTHER'`）とメドレー分割子行（`sub_order>0`、例 `'BGM'`）を誤って不一致判定し、既存ディスクへの物理情報同期を弾いていた不具合を解消。整合性の最終判定は後続の BEFORE UPDATE トリガーが確定値で行うため制約は緩まない（`v1.3.6_migration_tracks_content_kind_trigger_upsert_fix.sql`、冪等）。
- **商品詳細ページの情報強化**：取得済み MCN を商品基本情報テーブルに「JAN」行として 1 回表示（一般呼称を主表記。CD を含む商品のみ、複数ディスクで共通の前提で先頭 CD ディスクの MCN を採用）。各トラックの ISRC をトラック表「No.」セルの `title` ツールチップとして添え、点線アンダーライン＋help カーソルで存在を明示。トラック尺を `/stats/episodes/series-summary/` の平均尺表記に揃え、整数部「m:ss」＋小数 2 桁を `micro-fraction` 縮小表示（端数繰り上げの誤表記防止つき）。商品 JAN が 13 桁数字のとき商品 JSON-LD へ schema.org `gtin13` を出力（複数枚組 BOX でも商品単位で一意）。
- **ジャケット画像と購入・試聴リンク（フェーズ 1）**：商品詳細にジャケット画像（iTunes Lookup API＝認証不要・無料、で取得し `products` の新キャッシュ列 `cover_image_url` / `cover_image_source` / `cover_image_fetched_at` に保存。画像実体は保存せず提供元 CDN 直参照のホットリンク運用）と Amazon / Apple Music / Spotify への外部リンクを追加。Amazon リンクは `App.config` の新キー `AmazonAssociateTag` を `?tag=` で付与（PA-API 不使用・ASIN への正規 URL 組み立てのみのため審査前でも合法に掲示可）、外部リンクは `rel="nofollow sponsored noopener"` ＋ `target=_blank`。画像取得は SiteBuilder ビルドから分離し、Catalog の商品・ディスク管理フォームの「画像取得」ボタンで手動・差分実行（Apple ID あり・画像未取得のみ対象）。手動で URL を直接埋めた商品は自動取得の対象外となり保護される。キャッシュ列は PA-API 開通後の取得元 `amazon` 差し替えまで見越した汎用設計（`v1.3.6_migration_products_cover_image_cache.sql`、`INFORMATION_SCHEMA` 方式で冪等）。
- **バグ修正（CDAnalyzer ビルド）**：ISRC リトライ実装ステージで `ScsiMmci.cs` の引数なし `ReadIsrcForTrack` の `return` 文を欠落させ CS1002/CS1026 を生じた回帰を修正。
- **マイグレーション修正**：`v1.3.6_migration_products_cover_image_cache.sql` を当初 `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`（MariaDB 拡張で MySQL では 1064）で作成していたのを、リポジトリ規約どおり `INFORMATION_SCHEMA.COLUMNS` で存在確認し未存在時のみ動的 DDL を実行する冪等方式へ修正。

### v1.3.5 — 誕生日カラムの正規化・ホームカレンダー新設・プリキュアバッジ化

誕生日情報を `precures` から汎用マスタ（`persons` / `characters`）へ移設し、ホームに記念日・カレンダー系の閲覧体験を追加したリビジョン。複数ステージで構成。

- **（Stage 1）誕生日カラムの追加とバックフィル**：`persons` / `characters` に誕生日 4 列（生年 `birth_year`／公開可否 `birth_year_visibility`〔PUBLIC / PRIVATE〕／月 `birth_month`／日 `birth_day`）と関連 CHECK 制約を追加。既存 `precures` の誕生月日を `transform_alias_id → character_aliases.character_id` 経由で対応キャラへ非破壊バックフィル（生年は元来持たないため `birth_year` は NULL・`birth_year_visibility` は既定 PUBLIC、情報欠落なし）。マイグレは冪等（`v1.3.5_migration_persons_characters_birthday.sql`）。
- **（Stage 2）`precures` の誕生日カラム撤去**：バックフィル完了後に `precures.birth_month` / `birth_day` と CHECK 制約を原子的に削除（`v1.3.5_migration_drop_precure_birthday.sql`。**必ず Stage 1 マイグレ適用後に適用**）。
- **（Stage 3）ホーム「今月のカレンダー」新設＋「今日の記念日」の誕生日対応**：閲覧月 1 か月分を表示する JS 動的カレンダー（`calendar.js`）を新設（前月／翌月ナビなし・当月のみ）。埋め込み JSON `home-anniversary-data` をエピソード放送日／映画公開日／キャラクター誕生日／人物誕生日の 4 種別タグ付き 1 配列へ拡張（`HomeGenerator.BuildCalendarDataJsonAsync`）。「今日の記念日」（`anniversaries.js`）はキャラクター・人物誕生日をエピソード行より上に積むよう拡張。カレンダー UI に限り `series.title_short` を用いるポリシー例外を新設。
- **（Stage 4）Catalog に誕生日入力欄を追加**：`CreditMastersEditorForm` の人物・キャラクター編集タブに誕生日入力欄（生年 NumericUpDown ＋「不明」チェック／公開可否コンボ／月・日コンボ）を追加。プリキュアの誕生日はキャラクタータブで管理し、プリキュアタブには誕生日欄を置かない設計とした。
- **（先行分）プリキュアのバッジ化と地色**：`precures` にバッジ地色 `key_color`（#RRGGBB、CHECK 制約付き）を追加し、暫定地色を未設定行のみ初期投入（`v1.3.5_migration_precure_key_color.sql`）。地色の相対輝度から文字色（暗/明グレー）を自動算出し任意地色で本文可読に。シリーズ一覧 TV サブ行のプリキュア表示をバッジ化し、見出しラベル（「スタッフ」「プリキュア」）を撤去、プリキュアバッジ行を上・スタッフバッジ行を下へ並べ替え。バッジ表記および `/precures/{id}/` 詳細 h1 は、プリキュア観点で「変身後／変身後 2／変身前」の名義名（`character_aliases.name`）を「 / 」連結（NULL 名義は除外）し、声優を「(CV: ○○)」で後置（バッジのみ。共有ヘルパ `PrecureNaming.JoinAliasNames`）。
- **バグ修正（エピソード詳細の生成対象）**：`PrecureDataStars.SiteBuilder` の `EpisodeGenerator.IsChildOfMovie` が `parent_series_id` を持つシリーズを一律「映画子作品」とみなしていたため、親シリーズを持つ `kind_code='TV'` シリーズのエピソード詳細ページ（`/series/{slug}/{seriesEpNo}/`）が生成されない不具合を修正。TV シリーズは親の有無に関わらず必ず単独エピソード詳細を生成するようガードを追加（`MOVIE_SHORT` 子作品スキップと SPIN-OFF 除外の挙動は不変。`SeriesClassifier.IsMovieShortChild` とは別判定として併存する設計は維持）。
- **ドキュメント整理**：肥大化していた `README.md` の履歴的ナレーション（版数タグ、「〜改」「〜以降」「叩き台」「タスク N」「Stage N」「hotfix」「撤去された」「メモ」等）を本ファイルへ無損失移設し、README を現状仕様の網羅解説へ純化（恒久仕様文に統一）。欠落していた v1.3.4 / v1.3.5 のエントリを本ファイルへ補完し、README 冒頭の版数ブロックを「最新版 1 行＋本ファイルへの参照」に再圧縮（v1.3.2 で確立した方針の再適用）。陳腐化注記・撤去済みクラスやカラムの経緯は現状記述へ簡約。挙動・生成物・DB・スキーマの変更はなし。

### v1.3.4 — 「クリエーター」セクションへの統合（脱ランキング）

人物・企業・団体・声優・役職を「作り手」として 1 ハブへ集約したリビジョン。

- **クリエーターセクション新設**（`/creators/`）：旧 `/persons/`・`/companies/` 索引、旧役職統計（`/stats/roles/` 索引・総合集計）、旧声優統計（`/stats/voice-cast/`）を 1 つのハブへ統合。`CreatorsGenerator` が `/creators/`（ランディング）・`/creators/staff/`（スタッフ一覧）・`/creators/roles/{role_code}/`（役職詳細）・`/creators/voice-cast/`（声の出演一覧）の 4 種を生成。
- **脱ランキング型 UI**：人物・企業/団体・声優は順位列を持たず、タブ（役職順／五十音順／初参加順／参加話数が多い順 ほか）での並べ替えのみ。スタッフ一覧は人物と企業・団体を 1 リストに混在させ、行ごと個人/団体バッジ＋上部トグルで絞り込み。順位は作品系統計（サブタイトル統計・エピソード尺統計）にのみ Wimbledon 形式で残す。
- **役職詳細の移設**：`/creators/roles/{role_code}/` 形式へ移設し順位列を廃止。役職コードは URL 上で小文字化（`PathUtil.RoleStatsUrl()` に集約）。
- **グローバルナビ統合**：「人物」「企業・団体」を「クリエーター」1 項目へ統合（8 → 7 本）。トップの DB 統計ボックスは人物数＋企業・団体数を合算した「クリエーター」1 項目（`DbStats.CreatorsCount`）に変更。
- **`/stats/` の縮小**：サブタイトル統計とエピソード尺統計の 2 系統に縮小（クレジット関連はクリエーターへ移設）。
- 個別の `/persons/{personId}/` `/companies/{companyId}/` 詳細ページは直リンク用に引き続き生成。

### v1.3.3 — 3D シアター枠の導入・クレジットプレビュー整合・かな英語自動補完・映画 BGM リスト

DB スキーマ拡張と Catalog／SiteBuilder 双方の機能追加を含むリビジョン。

- **3D シアター上映枠**：`series_kinds` に新種別 `EVENT`（イベント / 3D Theater、クレジットはシリーズ単位）を追加し、専用シリーズ `slug=3dtheater`（開始日 2011-07-31）を `series_id=20` に新設。あわせて外部未公開のうちに既存 `series_id` 20〜68 を 21〜69 へ全テーブル一括で繰り上げて ID 体系を整理（単一トランザクション・二段オフセット法・冪等の移行 SQL）。
- **クレジットプレビューの SiteBuilder 整合**：Catalog の `CreditPreviewRenderer` を SiteBuilder の `CreditTreeRenderer` と挙動一致させた。連載で `{ROLE:CODE.PLACEHOLDER}` に消費される `MANGA` 等が単独役職として二重描画される不具合を `consumedRoleCodes` 事前スキャンで解消し、声の出演末尾「協力」行を 3 セル構成（リンクなし）に統一。
- **シリーズ詳細エピソード一覧の整理**：すぐ上の基本情報と重複するシリーズ見出し（`.episodes-index-heading`）と枠線ボックスを撤去（エピソード行間の点線は維持）。
- **かな・英語表記の自動補完**：パスポート式ローマ字変換の共有ロジック `KanaRomanizer`（長音切り捨て・撥音 n 固定・促音処理・語頭大文字、かな以外混入は変換不可）を `PrecureDataStars.Data` に追加。`CreditMastersEditorForm` の人物・企業・キャラクターおよび各名義の保存時に、空欄を補完元コピー＋ローマ字フォールバックで埋める確認付きフックを導入。既存データの遡及補完は一度きりの使い捨て一括フォームで実施し、実行後に撤去（恒久資産は `KanaRomanizer` と保存時フックのみ）。
- **映画 BGM リスト**：映画作品専用の新テーブル `movie_bgm_cues`（代理 PK、`series_id` で映画系シリーズへ直結、`seq`/`sub_seq` の順序、映画固有 `m_no` 文字列、区分は `track_content_kinds` 共用、音源はあるが本編未使用＝`is_unused`／そもそも未制作＝`is_missing` の排他 2 フラグ）を新設。`bgm_cues`（TV シリーズのセッション制・劇伴専用）とは別概念。`series_id` は映画系 kind（`MOVIE` / `MOVIE_SHORT` / `SPRING` / `EVENT`）のみ許容し、他テーブル参照のため BEFORE INSERT/UPDATE トリガーで担保、未使用と欠番の排他は CHECK で担保。編集用に `MovieBgmCuesEditorForm` を追加し、映画系シリーズ詳細ページに BGM リストを描画（欠番は「（欠番）」表示、未使用は淡色＋注記で区別）。

### v1.3.2 — `PrecureDataStars.SiteBuilder` の内部リファクタリング（機能・出力不変）

挙動・生成 HTML・DB・テンプレートを一切変えず、重複実装と実態に合わないコメントのみ整理したリビジョン。

- 各 Generator が個別に持っていた和文日付整形を `Utilities/JpDateFormat` に集約（書式別に `Date` / `NullableDate` / `Period` / `DotDate` / `DateWithWeekday` を用意）。
- 完全同一だった `MOVIE_SHORT` 子作品判定（Series / Music / Home）を `Utilities/SeriesClassifier.IsMovieShortChild` に集約。判定基準の異なる Episode / SearchIndex の判定は別物として温存。
- `PageRenderer` の 2 経路で重複していたレイアウトメタ補完を private ヘルパー `ApplyCommonLayoutDefaults` に抽出（特例ページのシェア空運用は維持）。
- 人物・企業・キャラクター・プリキュアの 4 Generator にバイト単位で完全一致して重複していたシリーズ／エピソードのルックアップ（`SeriesStartDate` / `EpisodeSeriesEpNo` / `LookupEpisode`、計 12 定義）を `Pipeline/BuildContextLookupExtensions`（`BuildContext` の拡張メソッド）に一本化。呼び出しは `_ctx.SeriesStartDate(...)` 等へ機械的に置換し、同一 `_ctx` 参照のため挙動は完全不変。
- 上記に関連して実態と乖離していたコメントを訂正。
- ドキュメント整理：肥大化していた変更履歴を `README.md` から本ファイル `CHANGELOG.md` へ無損失移設し、README はポインタ化（本体の機能・仕様詳細解説は削らず温存）。トップの版数注記も最新版＋本ファイルへの参照に圧縮。あわせて `.repomixignore` に `CHANGELOG.md` を追加し、Repomix 集約時のコンテキスト容量を削減（履歴は Git コミット／GitHub Releases からも辿れる）。

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
