# 変更履歴 — precure-datastars-wintools

本ファイルは `README.md` から移設した全バージョンの変更履歴です。概略のみを記載しています。工程単位の試行錯誤や変更ファイル一覧などの詳細は、Git のコミット履歴および GitHub のリリースノートを参照してください。

### v1.4.0 — 劇伴セッション補足説明の導入 ほか

劇伴詳細ページのセッション見出しに、録音日・スタジオ名などを小さく添えるための補足説明列を導入したリビジョン。複数ステージで構成。

- **`bgm_sessions.caption` の新設と劇伴詳細ページへの反映**：`bgm_sessions` に公開向け補足説明用の `caption VARCHAR(255) NULL` を `session_name` 直後に追加（`v1.4.0_migration_bgm_sessions_caption.sql`、`INFORMATION_SCHEMA` で存在確認してから `PREPARE/EXECUTE` する冪等パターン）。`BgmSession` モデルに `Caption` プロパティを追加し、`BgmSessionsRepository` の SELECT 列・`InsertNextAsync` の引数・`UpdateAsync` の SET 句に caption を流す。Catalog のマスタ管理「劇伴・セッション」タブの編集フォームに「補足」テキストボックスをセッション名と備考の間に追加（既存の備考＝`notes` は内部メモ用途のまま据置）。CSV 一括取り込み（`BgmCueCsvImportService`）は caption を扱わず、自動採番で新規作成されたセッションでは常に NULL を入れる（caption の編集はマスタ管理画面で個別に行う運用）。サイト側の `MusicGenerator` は `BgmSessionSection.Caption` プロパティを供給し、`bgms-detail.sbn` ではセッション見出し（h2）の右隣に `<span class="bgm-session-caption">` を 60% サイズ・muted 色で描画する（空文字なら span 自体を出さない）。`<section>` 要素には `data-section-nav-label="{SessionName}"` 属性を明示し、左サイドのセクションナビ（`section-nav.js`、ラベル解決順は `data-section-nav-label` > h2 textContent > id）に SessionName だけが入って caption がナビ表示に漏れ込まないようにする。
- **劇伴詳細ページの音源カウントを「曲数 + バージョン数」表記に変更**：リード行を「全 N 音源」から「全 N 曲 M バージョン」へ。曲数は `bgm_cues.m_no_class` でグループ化した数（同一 class を共有する複数 cue は 1 曲・複数バージョンと数える。M220 / M220b / M220 ShortVer 等の枝番違いが 1 曲に集約される）、バージョン数は cue 総数（仮 M 番号 cue を含む既存集計と同値）。class が NULL ないし空の cue は `m_no_detail` を独立キーにして 1 曲としてカウントする。`MusicGenerator` の `BgmDetailModel` に `SongCount` プロパティを追加して算出値を供給する。
- **左サイドのセクションナビが本文に被るのを防止**：長いセクション名（商品詳細ページの「Disc N : ＜長い商品名＞」等）でナビコンテナが本文側に侵食して読みづらくなる問題を解消。`.page-section-nav` に `max-width: calc((100vw - 960px) / 2 - 28px)` を追加し、本文（max-width 960px 中央寄せ）の左端と重ならない実効幅で打ち切る。あわせて `.page-section-nav-item > a` に `min-width: 0; max-width: 100%;` を追加し、grid item 既定の `min-width: auto` が `1fr` のラベル列を縮められなくして `text-overflow: ellipsis` を無効化していた問題を解消。実効幅が狭いときはラベルが末尾省略表示される（フル文字列は `<a title="…">` 属性で保持されホバー時に確認可能）。

### v1.3.8 — 歌の音楽種別を録音単位へ移設・ページ大タイトル「歴代プリキュア〜」統一・ランディング／統計索引／楽曲索引のカード化＋ホーム今月カレンダー刷新

歌の種別属性を録音単位へ正しく位置付けし直し、サイト各セクションの大ページタイトルを「歴代プリキュア〜」の体系に統一、ランディングカードを「カード全域＝1 リンク」方式へ揃えたリビジョン。複数ステージで構成。

- **歌の音楽種別を `song_recordings` へ移設**：同一曲のカバー版・アレンジ違いが「主題歌→キャラソン」「OP→挿入歌」のように文脈で種別変化するケースを表現できるよう、`music_class_code` を `songs` から `song_recordings` へ移設。`Song` モデルから当該プロパティを撤去、`SongRecording` 側に追加し、Repository SQL（SELECT/INSERT/UPDATE）の列セットを並行で整理。`SongsRepository.GetCreatorNameCandidatesAsync` 等の挙動は不変（音楽種別が SELECT 句に含まれていた構造のみ整理）。マイグレ `v1.3.8_move_music_class_to_recording.sql`：4 ステップ（`song_recordings.music_class_code` 列追加 → `songs.music_class_code` 値の全 recording への伝播 → `songs` 側 FK/INDEX/列の撤去 → `song_recordings.music_class_code` の FK/INDEX 張替）を、全 DDL を `INFORMATION_SCHEMA` で存在確認してから `PREPARE/EXECUTE` する冪等パターンで実装。`SQL_SAFE_UPDATES` をスクリプト冒頭で OFF にし終端で復元（PK 非参照の UPDATE を含むため、MySQL Workbench 既定環境でも素通る）。途中失敗時も再実行で残処理だけ進む。マスタ再シード（旧ステップ 4）は本マイグレからは撤去し、別管理に分離（次項参照）。
- **Catalog GUI 歌マスタ管理画面の整理**：曲詳細パネルから音楽種別コンボを撤去し、録音詳細パネルに移設（録音単位での種別選択）。曲一覧の検索バーから音楽種別フィルタを撤去（種別が録音単位になったため曲一覧のフィルタ軸ではなくなった。録音単位での絞り込みは将来課題）。`SongCsvImportService` から `music_class_code` の取り込みを撤去（旧 CSV にこの列が残っていても無視＋警告のみで取り込み継続。`SongMusicClassesRepository` への依存も撤去）。`docs/csv-templates/songs_import_sample.csv` から `music_class_code` 列を撤去。
- **歌の出典シリーズを `song_recordings` へ移設**：同一曲のカバー版や別作品挿入歌への流用で出典が文脈変化するケースを表現できるよう、`series_id` を `songs` から `song_recordings` へ移設（v1.3.8 後段ステージ）。`Song` モデルから `SeriesId` プロパティを撤去、`SongRecording` 側に追加。`SongsRepository` の SELECT/INSERT/UPDATE 列セットから `series_id` を撤去、未使用の `GetBySeriesAsync` も削除。`SongRecordingsRepository` の SELECT/INSERT/UPDATE 列セットに `series_id` を追加。マイグレ `v1.3.8_move_series_to_recording.sql`：4 ステップ（`song_recordings.series_id` 列追加 → `songs.series_id` 値の全 recording への伝播 [既存 NULL 値のみに、Catalog GUI で個別調整した値は上書きしない] → `songs` 側 FK/INDEX/列の撤去 → `song_recordings.series_id` の FK/INDEX 張替）を、`v1.3.8_move_music_class_to_recording.sql` と同型の冪等パターンで実装。FK の挙動は旧 `songs.series_id` と同じ `ON DELETE SET NULL ON UPDATE CASCADE`。Catalog GUI 歌マスタ管理画面：曲詳細パネルから「シリーズ」コンボを撤去し、録音詳細パネルに「出典シリーズ」コンボとして移設（音楽種別の直下）。曲一覧の検索バーからシリーズフィルタを撤去（曲一覧のフィルタ軸ではなくなった）。`SongCsvImportService` は曲生成と同時に「同 `song_id` × CSV 由来 `series_id` を持つ録音を 1 件自動確保」する動作に変更（コンストラクタに `SongRecordingsRepository` を追加）。既存曲判定キーから `series_id` を撤去（`(title, arranger_name)` 簡易キーへ）。CSV テンプレート `docs/csv-templates/songs_import_sample.csv` のヘッダは無変更（`series_title_short` 列は引き続き受け付け、解決した `series_id` は録音側に伝播）。
- **楽曲索引／詳細の出典シリーズ取り扱い改修**：`SongsGenerator` で disc 経由の「初出盤シリーズ」解決ヘルパー `BuildInitialReleaseSeriesMap` を撤去（出典シリーズが録音単位の正本になったため逆算不要）。楽曲索引 `/songs/` のセクション分類軸を `song_recordings.series_id` 直参照に変更。楽曲詳細 `/songs/{song_id}/` では `SongView` から `SeriesTitle` / `SeriesLink` を撤去、各 `RecordingView` に同名フィールドを追加し、recording セクション内の `.song-credits` に出典シリーズ行（中立グレー系の `.role-badge[data-role-code="SERIES"]` バッジ + シリーズ名リンク + 開始年「(2023)」の薄色補助 = `RecordingView.SeriesStartYearLabel`）を歌唱者バッジと並列で出す。MetaDescription の出典シリーズ採用は「先頭 recording のシリーズ」を代表値として使用。
- **楽曲詳細ページの細部調整**：(1) 各録音セクションの `<section>` に `data-section-nav-label="{曲名（バリアント優先）}"` を付与し、左サイドナビ（`section-nav.js`）が `<h2>` 内のバッジ文字列（例：「DANZEN! ふたりはプリキュア オープニング主題歌」のように音楽種別バッジが連結される）を拾わず、曲名のみをラベルにするよう明示。(2) パンくず下の一覧へのリンクテキストを「楽曲一覧」→「歴代プリキュアソング」に統一。(3) 収録商品テーブルの列を整理：「Disc/Track」列名を「収録」に変更（CSS クラス名は `col-disctrack` のまま）、「サイズ」「パート」の 2 列を「種別」1 列に統合してバッジ並びで表示する。サイズ（曲尺）は淡い緑、パート（歌入り／カラオケ等）は淡い青で全件共通色、ただしパート `VOCAL`（歌入り）は録音物の既定状態として Generator 側でバッジ自体を非表示（カラオケ・パート歌入り等の特殊版だけが目印として残る）。`RecordingTrackRow` から `SizeLabel` / `PartLabel` を撤去し `KindBadgesHtml` に統合（Generator 側で完成 HTML を組み立て、テンプレ側は無加工で出す）。CSS は `.col-size` / `.col-part` を撤去、`.col-kind` と `.recording-tracks-kind-badge.recording-tracks-kind-size` / `-kind-part` を新設。
- **スムーススクロール戦略の刷新**：CSS の `html { scroll-behavior: smooth }` グローバル指定を撤去。グローバル指定では URL 直リンク（`#anchor` 付き URL の初回表示）や戻る／進むナビゲーション、ハッシュリンクのプログラム移動など全アンカー遷移にアニメーションがかかり、目的位置への即時ジャンプを期待する場面でストレスになる問題があった。代わりに `section-nav.js` のセクションナビ内 `<a>` クリックだけプログラマブルに `window.scrollTo({ behavior: 'smooth' })` する方式へ移行。左サイドナビ・モバイルオーバーレイミラーの双方にデリゲート方式でハンドラを取り付け、修飾キー押下時（Ctrl/Cmd/Shift/Alt + クリック、新規タブ等）は素通し、`prefers-reduced-motion: reduce` 環境では smooth を無効化、URL ハッシュは `history.replaceState` で更新（履歴を汚さない）。クリック以外（直リンク・戻る／進む・自動スクロール）は全てブラウザ既定の instant ジャンプに戻る。
- **商品索引 `/products/` のシリーズ別タブ末尾を商品種別サブセクション化**：従来「複数シリーズ」「その他」の 2 バケットで一緒くたに扱っていた「単一のシリーズに紐付かない商品」（=ディスクで複数シリーズに分かれる／全ディスクが `series_id=NULL` ／ディスクが未登録）を、シリーズセクション末尾に商品種別（`product_kinds.display_order` 昇順）のサブセクションとして展開する形に整理。見出しは `product_kinds.name_ja`、各サブセクション内は引き続き発売日昇順・代表品番昇順。バリエーション商品や横断的商品が「種別不明扱いのその他」に押し込まれて見つけにくかった問題を解消し、種別軸での回遊性を向上。`ProductsGenerator` の `BuildSeriesSections` は `productKindMap` も受け取るシグネチャに変更、旧 `MultiSeriesBucketLabel` / `OtherSeriesBucketLabel` 定数は撤去、`bucketNonSingleByKind` で種別コード単位の再分配を行う。テンプレ `products-index.sbn` のセクション描画ループはそのまま流用可能（`ProductIndexSection.Label` が新規サブセクションでは種別名を持つだけで、構造変更なし）。コメントブロックを v1.3.8 仕様に更新。
- **商品索引 `/products/` のカード化（テーブル廃止）**：従来の `.products-index-table`（品番／タイトル／発売日／枚数の 4 列テーブル）を撤去し、楽曲索引と同型の横長カード（`.products-card-list` 直下に `<a class="products-card">` でカード全域 1 リンク化）に置換。シリーズ別タブとジャンル別タブの両方で同じカード意匠を使う。カードは内側 `.products-card-grid` で「左：`.products-card-main`（タイトル + meta 行）／右：`.products-card-badge-cell`（商品種別バッジ）」の 2 カラム grid。meta 行は発売日（`2004.2.1` 形式の短縮表記）／品番／税込価格（`税込 ¥3,300`、「税込」プレフィックスで税抜価格との混同を防ぐ）／枚数（複数枚商品のみ「{N}枚組」）をパイプ区切りで並べる。品番は 1 枚商品なら `ProductCatalogNo` をそのまま、複数枚商品なら所属ディスクの `disc_no_in_set` 昇順で筆頭・最終を取り、両者の共通 prefix + 差分サフィックスで `MJCD-20019〜21` 形式に整形（`BuildCatalogRangeLabel` ヘルパー、`ProductsGenerator` の private static として実装）。`ProductIndexRow` DTO に `ReleaseDateShort` / `CatalogNoRange` / `PriceIncTaxLabel` / `DiscCountLabel` / `ProductKindLabel` / `BadgeClassSuffix` を追加、行生成を `BuildProductIndexRow` 共通ヘルパーに切り出してシリーズ別／ジャンル別の両セクションから呼ぶ統一構造に。`BuildKindSections` は `discsByProduct` も受け取るシグネチャに変更（品番レンジ計算に必要）。CSS は楽曲索引の `.songs-card` 系と同じ意匠（左ボーダー 4px・ホバー時ピンク化・focus-visible 対応・子孫テキストへの underline 浸透抑止）を商品索引専用クラス `.products-card-*` として新設。商品種別バッジは `product_kind_code` ごとの固定色マッピング（`.products-card-kind-{code}` で 14 種別に色相環ベースのトーンを割り当て）。
- **商品詳細 `/products/{catalog_no}/` のトラックリスト全面刷新**：従来「No / 種別 / タイトル / 補足 / 尺」5 列の表で SONG・BGM・その他を区別なく扱っていたものを、種別ごとに構造化したレイアウトに刷新（4 列「No / 種別 / タイトル / 尺」、補足列は廃止）。
  - **歌（SONG）**：タイトル列の上段に「曲名（`recording.variant_label` 優先、無ければ `song.title`、楽曲詳細リンク付）」+ 右にサイズ・パートバッジ（楽曲詳細と同意匠、淡い緑＝サイズ・淡い青＝パート、`VOCAL`（歌入り）は既定状態としてバッジ非表示）。下段に作詞・作曲・編曲・歌の役職バッジ + 名義リンクを 4 セグメント連結。
  - **劇伴（BGM）**：タイトル列の上段に「収録タイトル（`track_title_override`）」、中段に「Mナンバー [bgm_cues.menu_title]」（劇伴の慣習表記）、下段に `bgm_cue_credits` 由来の作曲・編曲バッジ + 名義リンク。
  - **その他**：タイトル平文のみ、メタ行なし。
  - 共通基盤として新クラス **`PrecureDataStars.SiteBuilder.Utilities.TrackCreditHtmlBuilder`** を新設し、構造化クレジット（`song_credits` / `song_recording_singers` / `bgm_cue_credits`）から「役職バッジ + 名義リンク」HTML を組み立てる責務を集約。商品詳細・楽曲詳細など複数のジェネレータから再利用可能。`PersonAlias` → `Person` ID 解決は `person_alias_persons` 中間テーブルを起動時に一括ロードして `personIdByAliasId` lookup マップ化、共同名義（1 alias に複数 person）の場合は `person_seq` 最小値を採用。`SongRecordingSinger.BillingKind` を尊重し `CharacterWithCv` ではキャラ名リンク +「（CV: 人物名リンク）」展開、`Person` では人物名リンクのみ。劇伴クレジットでは隣り合う役職グループの連名 `person_alias_id` 列が順序通り完全一致する場合、バッジを横並びにして名義を 1 回だけ出すマージ処理を入れる（同一スタッフが作曲・編曲を兼ねる典型例で「`[作曲] 佐藤 直紀 [編曲] 佐藤 直紀`」が「`[作曲][編曲] 佐藤 直紀`」と整理される。連名同順「`[作曲][編曲] 佐藤 直紀、菅野 祐悟`」のケースも同則で吸収される）。歌クレジットでは構造化 `song_credits` 行が無い場合のフォールバックとして `Song.LyricistName` / `ComposerName` / `ArrangerName` のフリーテキストをリンクなしの平文で表示する（旧 import 由来でフリーテキストだけ入っている楽曲でも作詞・作曲・編曲が読める）。歌唱者も同様に `song_recording_singers` 行が無ければ `SongRecording.SingerName` をリンクなし平文でフォールバック。さらに歌の作詞・作曲・編曲・歌セグメント全体について、隣接する役職同士で名義 HTML が完全一致する場合バッジを並べて名義を 1 回だけ出す汎用マージ（`BuildMergedRoleSegmentsHtml`）を共通化（フリーテキストの「作曲=EFFY、編曲=EFFY」が「`[作曲][編曲] EFFY`」と整理される。構造化由来同士の同一 alias も結局 HTML 文字列が一致するので同じ仕組みでマージされる。構造化 + フリーテキスト混在ケースはリンクの有無で HTML が一致せず、意図的にマージ対象外）。
  - `ProductsGenerator`：構造化クレジット系リポジトリ（`SongCreditsRepository` / `SongRecordingSingersRepository` / `BgmCueCreditsRepository` / `RolesRepository` / `PersonAliasesRepository` / `CharacterAliasesRepository` / `PersonAliasPersonsRepository`）を依存に追加、`BuildTrackRow` を非同期化（`BuildTrackRowAsync`）して `_creditHtml` 経由でクレジット HTML を組み立てる構造に変更。`TrackRow` DTO から旧 `SubTitle` を撤去、`ContentKindCode` / `TitleHtml` / `KindBadgesHtml` / `MetaLineHtml` を新設。
  - **基本情報表から `Amazon ASIN` / `Apple Music` ID / `Spotify` ID の生 ID 行を撤去**（閲覧者にとっての情報価値が薄く、購入・試聴リンクから直接遷移できるため）。
  - **Amazon リンクテキストを「Amazon で見る」→「Amazon で聴く」**に変更（リンク先がデジタル音楽商品ページのため、Apple Music / Spotify との 3 リンク表記を統一した「聴く」動詞に揃える）。
  - **ジャケット画像の表示サイズ拡大**：CSS `.product-cover img` の `width: 200px / max-width: 40vw` → `width: 280px / max-width: 50vw`。
  - **ジャケット画像直下に Apple iTunes Search API ガイドライン充足のための attribution 追加**：「ジャケット画像は Apple iTunes Search API より、本商品の Apple Music ページへのご案内を目的として表示しています（画像実体は当サイトには保存していません）。Provided courtesy of iTunes.」を `.product-cover-attribution` クラスで極小フォント・muted トーンで表示。Apple のガイドライン 3 条件（(i) その商品を促進するページ／(ii) Apple Music 等への購入リンクと近接配置／(iii) "provided courtesy of" iTunes と attribution）を全て満たし、画像実体は保存せずホットリンク表示する運用の合法性を担保。
  - CSS：`.col-sub` 系を撤去し、`.track-title-line` / `.track-title-text` / `.track-title-badges` / `.track-meta-line` / `.track-credit-segment` / `.track-credit-names` / `.track-credit-list` / `.track-bgm-mno` / `.track-bgm-menu` / `.product-cover-attribution` を新設。
  - **ディスク内に複数シリーズ起源の劇伴が同居する場合のシリーズ略記プレフィックス**：商品詳細のトラック表で、1 枚のディスクに `bgm_cues.series_id` が 2 種類以上含まれる場合（映画オールスターズ系サウンドトラック等で典型）、各 BGM トラックのメタ行先頭にシリーズ略記スタンプ（`.track-bgm-series`、淡灰ボックス）を出して、M ナンバーがどのシリーズ起源か即座に識別できるようにする。略記には `series.title_short` を使用（プロジェクト方針として `title_short` は通常 UI では使わないが、本用途は「M ナンバーの起源識別補助」という限定文脈のディスク内表記として例外的に許容）。`title_short` が空のシリーズは `title` 全文をフォールバック採用。単一シリーズしか持たないディスクでは略記は出さない（冗長表記回避）。`ProductsGenerator.GenerateDetailAsync` のディスクループ冒頭で `bgmSeriesIdsInDisc` 集合と `bgmSeriesPrefixMap` を組み立て、`BuildTrackRowAsync` に渡す形で実装。CSS は `.track-bgm-series` を新設。
  - **商品詳細の劇伴トラックから劇伴詳細ページの該当 cue 行へのアンカーリンク**：商品詳細の各 BGM トラックで、上段の「メニュータイトル」と下段の「Mナンバー [メニュー]」の両方を `/bgms/{slug}/#cue-{m_no_detail}` 形式のアンカーリンクで包む（クリックで劇伴詳細ページの当該 cue 行へ直接ジャンプ）。リンク先 fragment は `PathUtil.SlugifyMNoDetail` で URL-safe 化（ASCII 英数字はそのまま、その他は `%XX` エンコード）し、劇伴詳細ページ側の `<tr id="{AnchorId}">`（同じく `cue-{Slugify(m_no_detail)}`）と整合させる。劇伴詳細テンプレ `bgms-detail.sbn` の `bgm-cue-main` 行に id 属性を追加、`MusicGenerator.BgmCueRow` DTO に `AnchorId` フィールドを新設、`PathUtil` に `SlugifyMNoDetail` / `BgmCueAnchorUrl` ヘルパーを追加。シリーズが解決できない異常データではリンクなしの平文に自動フォールバック。CSS は `.track-bgm-cuelink`（リンク内のテキストカラー継承・ホバー時のみアクセント色＋下線）と `.bgm-cues-table .bgm-cue-main[id] { scroll-margin-top: 80px; }`（固定ヘッダ下に潜らないジャンプ着地位置）を新設。
  - **パンくず下の一覧へのリンクテキスト統一**：商品詳細 `/products/{catalog_no}/` の冒頭リンクテキストを「商品一覧」→「歴代プリキュア音楽商品」に統一（楽曲詳細の「歴代プリキュアソング」と同方針）。
- **`song_music_classes` マスタの再構成**：旧 `MOVIE`（映画主題歌の総称）を `MOVIE_OP`（映画オープニング主題歌）/ `MOVIE_ED`（映画エンディング主題歌）/ `MOVIE_INSERT`（映画挿入歌）の 3 種へ分割（OP/ED/挿入歌の判別を可能に）。マスタ内容の整理（旧 `MOVIE` の分割、`MEDLEY` 等の追加、`display_order` 再採番）はマイグレでは自動実行しない方針に統一：マイグレで `DELETE + INSERT` 方式の再シードを行うと現場で手動追加した行（例：`MEDLEY`）が消える上、`song_recordings.music_class_code` から FK 参照されていて `DELETE` が通らないリスクがあるため、マスタ更新は Catalog GUI またはアドホック SQL での運用に分離する。マイグレ `v1.3.8_move_music_class_to_recording.sql` は本リファクタを反映し、`song_recordings.music_class_code` 列追加・`songs.music_class_code` からの値伝播・`songs` 側 FK/INDEX/列の撤去・`song_recordings.music_class_code` への FK/INDEX 張替の 4 ステップのみを担う（旧ステップ 4「マスタ再シード」は撤去）。
- **歌索引 `/songs/` の 2 タブ化＋初出盤シリーズ分類＋バリアント主タイトル化**：商品索引と同型の 2 タブ UI（シリーズ別／ジャンル別）を導入。既定タブは「シリーズ別」で、各 recording を「初出盤シリーズ」（当該 recording に紐付くトラック所属 disc のうち、所属 product の `release_date` が最古の disc の `series_id`。同点なら `product_catalog_no` 昇順 → `disc_no_in_set` 昇順で 1 件を確定）で分類。紐付け不能・`series_id` 不在は「その他」バケット。「ジャンル別」タブは録音単位の `music_class_code` で `song_music_classes.display_order` 順にセクション化、種別未設定は「種別未設定」バケット。行タイトルは `variant_label` 優先（無ければ親曲名）にし、親曲名のサブ表示は廃止（バリアントを親曲同様の独立した楽曲として扱う方針）。リンク先は引き続き `/songs/{song_id}/`。楽曲詳細ページの「種別」表示位置を基本情報から録音セクションへ移動し、各録音セクションの h2 を `variant_label` 優先（無ければ親曲名）に統一して種別ラベルを隣に控えめに添える。タブ切替は商品索引と同型のインラインスクリプト。
- **ページ大タイトルの「歴代プリキュア〜」統一**：10 ページの h1 / `PageTitle` / Breadcrumb 末尾（自身のラベル）を改称：`/music/`「音楽」→「歴代プリキュア音楽」、`/songs/`「楽曲一覧」→「歴代プリキュアソング」、`/bgms/`「劇伴一覧」→「歴代プリキュア劇伴」、`/products/`「商品一覧」→「歴代プリキュア音楽商品」、`/series/`「シリーズ一覧」→「歴代プリキュアシリーズ」、`/precures/`「プリキュア一覧」→「歴代プリキュアオールスターズ」、`/characters/`「キャラクター一覧」→「歴代キャラクター」、`/creators/`「クリエーター」→「歴代クリエーター」、`/creators/staff/`「スタッフ」→「歴代プリキュアスタッフ」、`/creators/voice-cast/`「声の出演」→「歴代プリキュア声優」。ナビ（ヘッダ／フッタの主導線リンクテキスト）およびランディングカード内のリンクテキストは不変。
- **ランディングカードを「カード全域＝1 リンク」方式に統一**：`/music/`（歌・劇伴・音楽商品の 3 カード）と `/creators/`（スタッフ・声の出演の 2 カード）のランディング HTML を `<article>` から `<a class="music-category-card" href="…">` に変更し、内側のテキストリンク（旧 `<a class="music-category-link">○○一覧へ →</a>`）を撤去。CSS は `.music-category-card` に `display:block`／`text-decoration:none`／`color:inherit` を与え、ホバー・フォーカス時に `border-color` をアクセントピンクへ、背景色を淡いピンクへ、`box-shadow` と `transform: translateY(-1px)` で押下可能性を視覚的に示す（`transition: 150ms`）。
- **`/stats/` ランディングのカード化**：従来の `.stats-landing-list` 列挙レイアウトを廃止し、`/music/` ・`/creators/` と同型の `.music-categories` / `.music-category-card` 構造（カード全域＝1 リンク）に置換。2 カード（サブタイトル統計／エピソード尺統計）を並べ、各カバレッジラベルをカード内に併記。`StatsLandingGenerator` のテンプレモデルから未使用 `CreditCoverageLabel` を撤去（参照していたテンプレ側も既に廃止済み）。CSS の `.stats-landing-list` ルールも撤去。
- **ホーム「今月のカレンダー」キャプション刷新**（先行ステージ）：月名を約 2 倍に拡大して中央に配置、左右に前月／翌月送りボタンを追加（クライアント完結。表示データは月日ベースのため JSON・Generator・DB・スキーマは不変）。
- **統計索引 `/stats/subtitles/` ・ `/stats/episodes/` の横長カード化**：従来の `<ul><li><a>タイトル</a><span class="muted">説明</span></li></ul>` を、各項目を「タイトル + 説明」の縦積み 1 つの `<a class="stats-landing-link">` にまとめた横長カードリストに置換。1 列スタックの縦並びで、左ボーダー（常時 4px 確保）をホバー時にアクセントピンクへ変色、背景うっすらピンクで押下可能性を表現。グローバル `a:hover` 由来の下線浸透は `.stats-landing-link *` まで `text-decoration: none` で抑止。
- **楽曲索引 `/songs/` のジャンル別タブ廃止＋シリーズ別フラット 1 ページに統一**：従来の 2 タブ UI（シリーズ別 / ジャンル別）を廃止し、`episodes-index.sbn` と同型の「シリーズ別フラット 1 ページ運用」に統一。各 recording は引き続き「初出盤シリーズ」（当該 recording に紐付くトラック所属 disc のうち、所属 product の `release_date` が最古の disc の `series_id`）で `<section id="songs-series-{n}">` にセクション化、シリーズ並び順は `series.start_date` 昇順 → `SeriesId` 昇順（「その他」は末尾固定）。左サイドナビは既存 `section-nav.js` が `<section id>` を自動検出して「[年4桁] [件数] ○ ラベル」の縦タイムラインを構築（`data-section-nav-label` / `data-section-nav-year` / `data-section-nav-count` 属性を付与）。ジャンル別タブを捨てる分でページサイズが約半分に縮減。各録音カード（横長カード、`/songs/{song_id}/` への単一リンク、`music_class_code` の固定 8 色バッジ、役職バッジ 4 色＋名義テキストの meta 行）の意匠は維持。`SongsGenerator` は `GenerateIndex` 1 本に整理（旧 `BuildSeriesSections` / `BuildClassSections` / 年度別ページ化案で導入した `GenerateLanding` / `GenerateYearPages` / `BuildYearNavHtml` 等は全削除、`BadgeSeriesShort` / `BadgeSeriesColorIndex` 等の関連フィールドも撤去）。
- **楽曲詳細 `/songs/{song_id}/` のクレジット表示リファクタ**：基本情報セクションから「曲名」「よみ」行を撤去（h1 とその下の muted 行で既出）、「出典」行も撤去（出典シリーズは録音バリエーション単位で変わる情報であり、本来は recording 側のカラムであるべきため。今回は表示のみ落とす）。残る作詞・作曲・編曲はシリーズ詳細 `#key-staff` と同型の `.song-credits` grid レイアウトで `.key-staff-line` 行として並べる（バッジ列：max-content / 名義列：1fr）。役職ラベルは `BuildRoleLabelLinkHtml` 改修により `<a class="role-badge role-badge-sm" data-role-code="LYRICS|COMPOSITION|ARRANGEMENT">作詞</a>` 等のバッジ風 `<a>` を返す（マスタ不在時は同型の `<span>` フォールバック）。各録音セクションでは h2 隣の `[音楽種別]` 平文を楽曲索引と同じ `.songs-card-badge.songs-badge-{code}` の固定 8 色バッジに統一、「歌：」段落は `.song-credits` 内に `<span class="role-badge" data-role-code="VOCALS">歌</span>` バッジ + 名義 HTML の `.key-staff-line` 構造へ書き換え。役職バッジ 4 色 CSS のスコープを `.songs-card` から外してグローバル化（`data-role-code` 値で着色が効くため既存 PRODUCER 等とは衝突しない）。`SongsGenerator` の `RecordingView` に `BadgeClassSuffix`（`music_class_code` を `tolowerinvariant + _→-` 変換した CSS クラス末尾、楽曲索引と共通）を追加し、テンプレ側のフィルタチェーン依存を排除。
- **楽曲詳細の収録商品テーブル刷新**：列構成を「商品 / Disc/Track / サイズ / パート」の 4 列に整理（旧「発売日」列を撤去）。商品セルを 2 行構成にして、上段：`<a class="recording-tracks-product-name">商品名</a>`、下段：`<span class="recording-tracks-product-meta muted">{2024.2.4 形式の発売日} ／ {DiscCatalogNo}</span>`。Disc/Track 列は従来の「Disc N / Track NN」表記を「Tr01」「3-Tr23」式の簡略表記に短縮（単一 disc 商品は `DiscNoInSet` を出さず `Tr{NN}` のみ、複数枚組は `{N}-Tr{NN}`、Track 番は常時 2 桁ゼロパディング）、`text-align:right` + `tabular-nums` で右揃え。`RecordingTrackRow` に `DiscTrackLabel` と `ProductReleaseDateShort` を追加し Generator 側で組み立て済み（テンプレでの分岐は不要）。サイズ・パート列は現状維持。
- **コメント・注釈の CHANGELOG への集約**：ソースコード（C# / JavaScript / CSS / SVG / `.csproj` / `App.config.sample` / Scriban テンプレ）に残っていた「v1.x.x で導入／移設／撤去」「v1.x.x 続編」「v1.x.x 公開直前のデザイン整理 第 N 弾」等の歴史的注釈を一掃。各箇所のコメントは「現状仕様を素直に説明する」内容へ整理し直し、撤去された機能・属性に対する旧仕様の残響も削る（例：`section-nav.js` の「data-section-nav-label は現状サイトには無い」という陳腐化注釈はテンプレ側の実利用に即した説明へ書き換え、`SongCsvImportService` の「旧バージョンでは series_id も判定キーだった」等の歴史記述は撤去）。バージョン番号付きの「いつ何が変わったか」は本ファイル（CHANGELOG.md）に一元化する方針を徹底。あわせて `README.md` 冒頭の「最新 v1.3.8 — 〇〇」変更点列挙も CHANGELOG への参照のみへ簡潔化。

### v1.3.7 — コード／README/CSS の縮約＋統計ページの脱テーブル刷新・新規ランキング追加・CD 照合の安全化

ソースコメントと README/CSS の冗長記述を縮約してコンテキスト容量を削減するとともに、サブタイトル／エピソード尺などの統計ページを `<table>` から専用 `stats-ep-list` レイアウトへ全面刷新し、「シリーズ別 TOP5 漢字」「記号出現回数・初使用エピソード」等の新規・改題ページを追加。あわせて `/creators/staff/` のクリーンアップ、シリーズ詳細のセクション並び替え、CD アナライザ照合の安全化（複数枚組での MCN 誤マッチ回避）を行ったリビジョン。集計ロジック・順位規則・DB・スキーマは不変。

- **コメントの機械的縮約**：全手書き `.cs` に対し、装飾区切りコメント（`// ────` 等）・中身が空の XML doc 行（`/// <para>` 等）・連続空行を除去。コード行（コメント・空行以外）はバイト単位で不変であることを全ファイル検証済み。
- **純動作説明コメントのみ要点化**：3 行以上連続する `//` ブロックと多行 `/// <summary>` のうち、設計判断・意図・整合性・落とし穴・順序依存・トレードオフなど「なぜそうしたか」を一切含まない純粋な動作説明だけを要点化。設計根拠を含む塊は温存に倒すガードで丸ごと残す（`<param>` / `<returns>` 等は不変）。
- **site.css の整理**：多行 `/* */` 設計理由バナーを要点 1 行へ圧縮し連続空行を詰めた。セレクタ・宣言・値は不変（コメント除去後のルール本体が縮約前と完全一致することを検証済み）。
- **README の純化**：冒頭の版数ブロックを「最新版 1 行＋ CHANGELOG 参照」へ再圧縮（v1.3.2 で確立した方針の再適用）。CD 登録節の実装内部ブロック引用（MCN/ISRC の CDB バイト構成、ISRC リトライ内部、ジャケット画像フェーズ方針など）を仕様要約へ凝縮し、メカニズム詳細はコードと本ファイルを一次情報とする旨に整理。散在していた履歴的ナレーション（「〜を統合した」「撤去」「v1.0 から変更なし」等）を現状仕様文へ簡約。全機能・全スキーマの網羅性は維持。
- 縮約規模：手書き `.cs` ＋ `site.css` で約 7,500 行減（コード挙動は不変）。設計意図コメントを保全する方針のため、純ノイズと冗長な動作説明の除去に範囲を限定。
- **既存記述ミスの修正**：`CreditBulkApplyService.ResolveLogoAsync` の XML doc が縮約前から二重 `<summary>`（開き 2・閉じ 1）になっていたのを、ステイの開始タグ 1 行のみ削除して単一の整形済み doc に修正（本文の文言・情報は無損失。コード不変）。
- **統計エピソード単位ページの脱テーブル化**：パート尺（A/B・アバン 各長短）・アバンスキップ回・中 CM 入り時刻（早遅）・サブタイトル文字数/漢字率/記号率（多少）の計 15 ページを `<table>` から専用 `stats-ep-list` レイアウトへ全面置換。1 行＝左（順位＋指標値〔数値のみ〕を横並び・本文の約 1.77 倍・`<li>` 内上下中央）／右（上段「シリーズ名〔シリーズ詳細リンク〕 (年度)」・下段「第N話のみ〔シリーズ詳細のエピソード一覧と同装い・放送日は出さない〕＋ルビ付き改行除去サブタイトル〔エピソード詳細リンク〕」）。同率順位は 2 件目以降の順位表示を空にし（指標値は常に表示・順位カラムは固定幅で整列）、1〜3 位装飾は廃止。アバンスキップ回は他のエピソード単位ページとパネル・グリッドを完全同一にしたまま、左パネルに順位ではなく放映順の回次（1 始まりの連番）を入れ、指標値セルだけを出さない。ルビ補完は追加 SQL なしで `BuildContext.LookupEpisodeBySeriesEpNo` により解決し、行組み立ては共有 `StatsEpisodeRows` に集約（`EpisodePartStatsGenerator` / `SubtitleStatsGenerator` から再利用）。集計ロジック・順位・対象範囲は不変。左の順位＋指標値は淡いアクセント地＋左アクセントバーの角丸パネルで装飾（1〜3 位の特別装飾は持たない）、行間も拡張。 使用文字 TOP100（全文字／漢字限定）も脱テーブル化：左は共通アクセントパネル（順位＋出現回数）、右に対象文字を正方形セル内で中央寄せした超特大表示（字幅差による左寄りを解消）。 さらに「シリーズ別 TOP5 漢字」ページを新設（「シリーズ別 TOP5 文字」と対、漢字＋「々」限定）。リポジトリに `GetTopKanjiBySeriesAsync`（`GetTopCharsBySeriesAsync` と同型＋既存と同一の漢字 REGEXP フィルタ）、生成に `GenerateTopKanjiBySeriesAsync`、テンプレ `stats-subtitles-top-kanji-by-series.sbn` を追加し、索引の TOP5 文字直下にリンク。既存 TOP5 文字・DENSE_RANK 同点同順・集計範囲は不変（subtitles 16→17 ページ）。 「記号出現回数」を「記号出現回数・初使用エピソード」に改題し脱テーブル化：使用文字 TOP100 と同じ大グリフ＋出現回数のみのパネル（`value-only` で順位カラム圧縮）＋初使用エピソード（シリーズ/年度・下段は「放送日(左寄せ,括弧なし)｜第N話(右寄せ)｜サブタイトル(左寄せ)」を固定幅 3 カラムで全行整列、等間隔・放送日はエピソード一覧と同じ 0.88em muted）。初使用が早い順は維持。`StatsEpisodeRows.BuildTitleHtml` を公開化し共有ビルダー `StatsSymbolRows` に集約。SQL/初使用順は不変。
- **`/creators/staff/` のクリーンアップ**：一度もクレジットの無い役職を「役職順」索引にも役職詳細ページにも出さないよう、`CreatorsGenerator` の役職ループで関与エンティティ 0 件（`rows.Count == 0`）をスキップ。集計・並び順・他タブは不変。
- **シリーズ詳細のセクション並び替え**：`series-detail.sbn` で「プリキュア」セクションを「メインスタッフ」より前に移動（基本情報 → 関連作品 → プリキュア → メインスタッフ → BGM → …）。内容・id・セクションナビは不変、表示順のみ変更。
- **CDアナライザの照合を CDDB 主体に**：`DiscRegistrationService.FindCandidatesForCdAsync` から MCN 完全一致ステージを削除。複数枚組（BOX）は全ディスクが同一 MCN を共有し Disc 2→Disc 1 誤マッチの危険があるため。CDDB Disc ID 完全一致を最優先＋ TOC 曖昧一致を安全網に。`mcn` 引数・`MatchResult`・呼び出し側シグネチャは不変（`FindByMcnAsync` 自体は他用途のため残置）。同率順位ブランク化を適用し共有ビルダー `StatsCharRows` に集約（SQL 不変。記号初出現順ページは従来テーブル維持）。

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
