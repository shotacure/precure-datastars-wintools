# precure-datastars-wintools

プリキュアデータベース「precure-datastars」向け Windows 用 ETL・データ管理ツール群。

プリキュアシリーズのエピソード情報（サブタイトル・放送日時・ナンバリング・パート構成・尺情報・YouTube 予告 URL 等）と、**音楽・映像カタログ情報（CD / BD / DVD・商品・ディスク・トラック・歌・劇伴）** を MySQL データベースで管理するためのアプリケーション集です。

> **v1.2.2** — クレジット一括入力フォーマットを完全可逆化。LOGO エントリ（`[屋号#CIバージョン]`）／本放送限定マーカー（行頭 🎬）／A/B 併記マーカー（行頭 `& `）／エントリ単位の備考（行末 ` // 備考`）／Card/Tier/Group/Role/Block の備考ディレクティブ（`@notes=`）／ColCount の明示指定（`@cols=N`）を構文サポートし、パーサと適用サービスに型・解決ロジックを追加。新規ファイル **`CreditBulkInputEncoder`** が Draft 階層を一括入力フォーマットに逆翻訳できるようになり、ツリー右クリックメニュー **「📝 一括入力で編集...」** を全レベル（クレジット全体／カード／ティア／グループ／役職）に対応。`CreditBulkInputDialog` に新たに **ReplaceScope モード** を追加し、選択スコープの中身を Encoder で逆翻訳した文字列を初期値としてダイアログを開き、編集後のパース結果でスコープ配下を置換できる。あわせて Card/Tier/Group/Role の **備考編集 UI** を **`NodePropertiesEditorPanel`** として新設し、上位ノード選択時に右ペインで備考を直接編集可能に。A/B 併記の `parallel_with_entry_id` 解決は `CreditSaveService` の **新フェーズ 2.7** で行い、保存時に直前エントリの実 ID を引き当てて自参照リンクを構築する設計とした。DB スキーマ変更なし。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.2.0** — クレジット管理基盤を新規追加（Phase A: DB + データ層 / Phase B: クレジット系マスタ管理 GUI / Phase H: クレジット編集画面）。シリーズまたはエピソードの OP/ED クレジットを構造化して保持できる新スキーマ（人物 / 人物名義 / 共同名義中間表 / 企業 / 屋号 / ロゴ / キャラクター / キャラクター名義 / 声優キャスティング / 役職 / シリーズ別書式上書き / クレジット本体 4 段階：credits / credit_cards / credit_card_roles / credit_role_blocks / credit_block_entries / エピソード主題歌）を追加し、クレジット内の各エントリを「人物名義」「キャラクター × 声優ペア」「企業屋号」「ロゴ」「歌録音」「フリーテキスト」のいずれかに型付きで保持できるようにした。整合性は CHECK 制約とトリガーの併用で担保（CASCADE FK 列を含む整合性は MySQL 8.0 の Error 3823 制約のためトリガーで実装）。`series_kinds` には `credit_attach_to`（SERIES/EPISODE）、`part_types` には `default_credit_kind`（OP/ED）の宣言列を追加し、TV シリーズ系はエピソード単位、映画系はシリーズ単位でクレジットを持つ運用を表現可能にした。`PrecureDataStars.Catalog` メニューに「クレジット系マスタ管理」を新設し、13 タブ（人物 / 人物名義 / 企業 / 企業屋号 / ロゴ / キャラクター / キャラクター名義 / 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 / シリーズ種別 / パート種別）を備えた `CreditMastersEditorForm` を追加。さらに **クレジット本体の編集画面 `CreditEditorForm`** を新設し、Card / Tier / Group / Role / Block / Entry の階層構造を 3 ペイン GUI（左：絞込みとクレジット選択／中央：階層ツリーと編集ボタン／右：エントリ編集パネル）で編集可能にした。**工程 H-8 では編集を全面メモリ化（Draft セッション方式）**：ユーザーの操作は一旦メモリ上の Draft オブジェクトに反映され（カード追加・並べ替え・エントリ編集・DnD 移動など）、画面下部の「💾 保存」ボタン押下時に `CreditSaveService` が 4 フェーズ（削除→新規作成→更新→seq 整合性）を 1 トランザクション内で実行して DB に確定する。未保存中はツリー背景色が薄い黄色になりステータスバーに「★ 未保存の変更あり」が表示される。クレジット切替・シリーズ／エピソード切替・フォーム閉じ時には未保存変更がある場合の確認ダイアログ（保存して続行／破棄して続行／キャンセル）を出してデータロストを防ぐ。**クレジット話数コピー** 機能も追加（左ペインの「📋 話数コピー...」）：現在選択中のクレジットを別シリーズ・別エピソードへ丸ごと複製でき、シリーズ跨ぎコピーで「前作の OP 構造を新シリーズの第 1 話に流用してから差分編集する」運用に対応。コピー先に同種クレジットが既存の場合は上書き／中止を選択できる。さらに **クレジット HTML プレビュー機能**（工程 H-9）を追加：左ペインの「🌐 HTMLプレビュー」ボタンで非モーダルウィンドウを開き、`WebBrowser` コントロール上でクレジットの完成形を確認できる。役職テンプレートは新設した **`role_templates` 統合テーブル**（工程 H-10）から `(role_code, series_id) → (role_code, NULL)` の優先順で解決し、`RoleTemplateRenderer` で DSL を展開して HTML 化する。テンプレ未定義の役職は「役職名（左固定幅）+ ブロック内エントリを `col_count` で横並び」のフォールバック表で表示する（実物のスタッフロール風）。クレジット切替・保存・取消のたびにプレビューは自動再描画される。**工程 H-10 では旧設計の`roles.default_format_template`（既定）と `series_role_format_overrides`（シリーズ別上書き）の二箇所運用を廃止し、`role_templates` 単一テーブルに統合**：序数キーで「既定 vs オーバーライド」という構造的非対称を排除し、`series_id IS NULL` で既定、非 NULL でシリーズ別を一元管理する設計に転換した。同工程で **`credit_kinds` テーブル**（OP/ED の表示名マスタ）も新設し、旧 `ENUM('OP','ED')` を VARCHAR + FK 化。プレビュー HTML は「オープニングクレジット／エンディングクレジット」と日本語表示し、CSS は枠線・カード見出しを撤廃してプレーンテキスト風に。テンプレ展開結果は HTML 素通し（`<b>` 等のタグが効く）、ロゴ表示は屋号名のみ（CI バージョンラベル非表示）に変更した。 **工程 H-11 でクレジットプレビューを常時表示化**：別ウィンドウだった HTML プレビューを廃止し、編集画面の中央右に 4 ペイン目として埋め込む 3 段ネスト SplitContainer 構成（左 320 / 中央 / プレビュー / 右 380）に変更。Draft セッションの内容を 250ms デバウンスでリアルタイム描画するため、保存していない編集状態がそのまま見える。`RenderDraftAsync` が Draft の Card/Tier/Group/Role/Block/Entry を直接走査して HTML 化（DB を経由しない）。プレビュー HTML の CSS には階層余白（card 18px / tier 12px / group 8px / role 4px）を入れ、構造の切れ目を視覚化。OP/ED の並び順は `KindOrder` 関数で OP=1, ED=2 の固定順に。**工程 H-12 でプレビュー幅を 920px（旧 460px の倍）に拡大**、`ClientSize` を 2240×880 に。主題歌の横並び表示を半角スペース 4 個区切りから HTML テーブルに変更し、ブラウザでの空白折り畳み問題を解消。EPISODE スコープのクレジットでも `episodes` テーブルを逆引きしてシリーズ別テンプレが正しく適用されるようになった。**工程 H-13 で役職テンプレートタブの UI を全面再設計**：上部の役職コンボ 1 個（フィルタ兼編集対象）+ 一覧グリッド + 操作ボタン 3 個（[+ 新規追加] [💾 保存 / 更新] [🗑 選択行を削除]）+ 詳細編集パネルというシンプル構成に。**工程 H-14 で改行コードの取扱いを全面正規化**：MySQL TEXT 列由来の `\r\n / \r / \n` 混在を 3 段階（マスタエディタ表示時 / プレビューレンダラ / 主題歌ハンドラ）で統一処理し、CSS から `white-space: pre-wrap` を撤去して `<br>` のみで改行制御する方針に。**工程 H-15 でテンプレ展開結果の自動 2 カラムラップ機能を導入**：テンプレに `{ROLE_NAME}` プレースホルダが含まれない場合、レンダラが自動的にフォールバック表と同じ「役職名カラム + 内容カラム」の HTML テーブルでラップし、テンプレ役職とフォールバック役職が同じ位置で整列。**工程 H-16 でテンプレ DSL に `{#THEME_SONGS:opts}...{/THEME_SONGS}` ループ構文を新設**：主題歌役職の表記（カギ括弧の種類・「作詞:」ラベル・項目順・改行位置）をテンプレ作者が完全制御できるようになり、楽曲スコーププレースホルダ `{SONG_TITLE}` / `{SONG_KIND}` / `{LYRICIST}` / `{COMPOSER}` / `{ARRANGER}` / `{SINGER}` / `{VARIANT_LABEL}` を内側で参照可能。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.1.5** — (1) CDAnalyzer と BDAnalyzer を同時起動した状態で DVD / Blu-ray を投入すると、CDAnalyzer 側がドライブを SCSI コマンドで一時的に占有し、BDAnalyzer のファイル I/O（`VIDEO_TS.IFO` / `*.mpls` の読み込み）に悪影響が出る問題を修正。CDAnalyzer がメディア挿入を自動検知した直後に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系（CD-ROM / CD-R / CD-RW）以外のメディアであれば後続の SCSI コマンド（READ TOC / READ SUB-CHANNEL / CD-Text 取得）を一切発行せず即座にデバイスハンドルをクローズするように変更。自動トリガ時はメッセージボックスを抑止して BDAnalyzer の作業を妨げない一方、ユーザの「読み取り」ボタン操作時は検知メディア種別を案内するダイアログを表示する。 (2) BDAnalyzer の Blu-ray 解析が `BDMV/PLAYLIST/00000.mpls` か `00001.mpls` だけを 1 個拾って読む実装になっていたため、ディスク内の他の有意なプレイリスト（複数話収録の各話プレイリストや特典プレイリスト等）が拾えていなかった問題を修正。`BDMV/PLAYLIST/*.mpls` を全走査して有意なタイトルを抽出する「フォルダ全走査モード」を Blu-ray 側にも導入し（DVD 側で v1.1.1 から動作している `VIDEO_TS` 全走査の Blu-ray 版）、短尺ダミー（FBI 警告・配給ロゴ・レーベルロゴ等、デフォルト 60 秒未満）と重複プレイリスト（anti-rip スキームの 99 個重複等）を自動除外して、複数のメイン的プレイリストを並列して取り込めるようにした。 (3) 新規商品登録ダイアログ（`NewProductDialog`）の操作性を改善。価格欄を NumericUpDown から TextBox に変更してスピンボタンを廃止、税抜価格を入れた時点で発売日に対応する日本の標準消費税率を適用した税込価格を自動計算して読み取り専用フィールドに表示する仕組みに刷新（税率は発売日が 2019-10-01 以降なら 10%、2014-04-01〜2019-09-30 は 8%、それ以前の境界も同様に切り替わる）。発売元 `MARV` / 販売元 `SMS` を初期値として埋めるよう変更。 (4) 商品・ディスク管理画面（`ProductDiscsEditorForm`）で、ディスクが 1 枚しか無い商品を選択するとディスク詳細フォームが空のままになる不具合を修正。`DataGridView.SelectionChanged` の発火タイミング（新旧 DataSource の現在行 index がいずれも 0 のままだと発火しない仕様）への依存を解消し、`RebindDiscGrid()` ヘルパで先頭行の明示選択 + 詳細フォーム反映を直接実行する経路に統一。複数枚商品でも商品選択直後に先頭ディスクの詳細が即座に見えるようになる副次効果あり。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
> **v1.1.4** — 商品・ディスク管理画面でディスク情報を保存したときに、CDAnalyzer / BDAnalyzer が読み取ったディスク総尺などの物理情報が消える不具合を修正。CDAnalyzer / BDAnalyzer 側でも、既存商品への追加ディスク登録時に既存品番を入力してしまうと既存ディスクが上書きされる挙動を改め、明示的にエラーで停止するように変更。`DiscMatchDialog` で品番検索結果が 1 件のとき自動選択するよう改善。あわせて、ディスク・トラック閲覧画面の上下ペインをウインドウリサイズ時に常に半々で維持、商品・ディスク管理画面のレイアウトを上下 2 段（上＝商品エリア / 下＝ディスクエリア）に刷新し、それぞれ左 60% に一覧、右 40% に詳細エディタを配置してエディタ領域の窮屈さを解消。マスタ管理画面についても、外周余白を確保しつつ「新規」ボタンを追加して新規追加と既存編集の操作を明確化、`display_order` をマウスドラッグで並べ替え→「並べ替えを反映」ボタンで一斉 UPSERT する操作フローを新設、監査列（CreatedBy / UpdatedBy / CreatedAt / UpdatedAt）を全タブで自動非表示に。詳細は末尾の [変更履歴](#変更履歴) を参照。
>
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
    │   ├── v1.2.0_add_credits.sql           … v1.1.5 → v1.2.0 差分用（クレジット管理基盤の追加）
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
| **PrecureDataStars.Catalog** | WinForms GUI | 音楽・映像カタログ管理 GUI。閲覧専用の「ディスク・トラック閲覧」（翻訳値で一覧表示、ディスク総尺・トラック尺ともに M:SS.fff で表示、トラック単位で作詞／作曲／編曲を独立表示、劇伴は M 番号・メニュー表記の注釈付き）と、6 つの編集フォーム（商品・ディスク／トラック・歌・劇伴・マスタ類・**クレジット系マスタ**）をメニューから切り替えて使う。クレジット系マスタは v1.2.0 で新設された 13 タブ構成のフォーム（人物 / 人物名義 / 企業 / 企業屋号 / ロゴ / キャラクター / キャラクター名義 / 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 / シリーズ種別 / パート種別）。 |
| **PrecureDataStars.TitleCharStatsJson** | コンソール | 全エピソードの `title_char_stats` を一括再計算して DB を更新するバッチツール。 |
| **PrecureDataStars.YouTubeCrawler** | コンソール | 東映アニメーション公式あらすじページから YouTube 予告動画 URL を自動抽出・登録するクローラー。1 秒/件のスロットリング付き。 |
| **PrecureDataStars.LegacyImport** | コンソール | 旧 SQL Server 版の discs / tracks / songs / musics テーブルから、新 MySQL 版の products / discs / tracks / songs / song_recordings / bgm_cues / bgm_sessions へ移行するバッチ。`--dry-run` オプションで件数サマリーだけの試行運転が可能。 |
| **PrecureDataStars.BDAnalyzer** | WinForms GUI | Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺・累積時間を表示。ディスク挿入の自動検知対応。DVD は VIDEO_TS.IFO を指定するとフォルダ全走査で多話収録 DVD にも対応する（v1.1.1）。Blu-ray も v1.1.5 から `BDMV/PLAYLIST` 配下指定時はフォルダ全走査モードに切り替わり、ディスク内の有意なプレイリストを並列抽出する（既定 60 秒未満の短尺ダミーと重複プレイリストは自動除外）。DB 連携パネルで既存ディスクとの照合・新規商品登録が可能。 |
| **PrecureDataStars.CDAnalyzer** | WinForms GUI | CD-DA ディスクの TOC・MCN・CD-Text を SCSI MMC コマンドで直接読み取り、トラック情報を表示。DB 連携パネルで MCN → CDDB-ID → TOC 曖昧の優先順でディスク照合し、既存反映 or 新規商品＋ディスク登録までを 1 画面で実行できる。v1.1.5 以降、メディア挿入時に MMC `GET CONFIGURATION` で Current Profile を確認し、CD 系プロファイル以外（DVD / BD / HD DVD）であれば後続の SCSI コマンドを発行せず即座にデバイスハンドルをクローズする（BDAnalyzer との同時起動時にドライブ占有競合を起こさないため）。 |

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

`db/schema.sql` によりデータベース `precure_datastars` と全テーブル（エピソード系 6 本 + 音楽・映像カタログ系 14 本 + **クレジット管理系 16 本**）が作成されます。スキーマは v1.2.0 時点の最新状態（`discs.series_id` を持ち、`products.series_id` は無い。`songs` の作詞／作曲列は `lyricist_name` / `composer_name` 等の素の命名。`bgm_cues` には仮 M 番号フラグ `is_temp_m_no` がある。`series_kinds` には `credit_attach_to`、`part_types` には `default_credit_kind` の宣言列が追加されており、人物・企業・キャラクター・役職・クレジット本体・エピソード主題歌の各テーブル群が含まれる）。

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
```

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
4. 「💾 保存」を押すと `CreditSaveService` が 4 フェーズ（削除 → 新規 → 更新 → seq 整合性）を **1 トランザクション** 内で実行して DB へ確定する。失敗すれば全体ロールバック。
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
| `AppendToCredit`（v1.2.1 既存） | 左ペイン「📝 クレジット一括入力...」ボタン | 既存クレジットの **末尾に追加** |
| `ReplaceScope`（v1.2.2 新規） | ツリー右クリック「📝 一括入力で編集...」 | 選択スコープの中身を **置換**。起動時に既存内容を `CreditBulkInputEncoder` で逆翻訳した文字列が初期値として入る |

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
- Draft セッションへの追加は **末尾追加**（AppendToCredit モード）または **スコープ置換**（ReplaceScope モード）。DB 確定は通常の「💾 保存」フロー

**v1.2.2 ラウンドトリップ性**:

`CreditBulkInputEncoder` は Draft 階層を一括入力フォーマットに逆翻訳するため、「右クリック → 一括入力で編集 → 編集 → 適用」のサイクルで既存クレジットの大幅な書き換えがテキストエディタ感覚で行える。Encoder の出力を Parser に通すと、マスタ ID 解決後の状態で同じ構造が再現される（例外: `IsForcedNewCharacter` のアスタは Draft 上で追跡しないため再エンコードでは消える）。

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

#### 連載（`SERIALIZED_IN`）

**テンプレ**:
```
{#BLOCKS:first}{ROLE_NAME}／{LEADING_COMPANY}「{COMPANIES:wrap=""}」
漫画・{PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
```

**期待する展開結果**:
```
連載／講談社「なかよし」
漫画・上北ふたご
　「たのしい幼稚園」
　「おともだち」ほか
```

**ブロック構成**:
```
Role: SERIALIZED_IN  連載  (order 1)
├─ Block #1 (1 cols, 2 entries)   ← {#BLOCKS:first} で展開される最初のブロック
│    leading_company_alias_id = 「講談社」屋号 ID
│    ├─ [COMPANY]  #1  「なかよし」屋号（雑誌名を屋号として登録）
│    └─ [PERSON]   #2  「上北ふたご」名義
├─ Block #2 (1 cols, 1 entries)   ← {#BLOCKS:rest} の最初のブロック
│    └─ [COMPANY]  #1  「たのしい幼稚園」屋号
└─ Block #3 (1 cols, 1 entries)   ← {#BLOCKS:rest} の続き
     └─ [COMPANY]  #1  「おともだち」屋号
```

**ポイント**:
- **`leading_company_alias_id`** はブロック側のフィールドで、テンプレの `{LEADING_COMPANY}` プレースホルダで参照される。出版社（講談社など）の屋号 ID を入れる。
- 雑誌名（「なかよし」など）はクレジット表記される文字列なので、`company_aliases`（屋号マスタ）に **別エントリ** として登録する。実体マスタ `companies` の階層では「株式会社講談社」だが、屋号マスタでは「講談社」「なかよし」「たのしい幼稚園」「おともだち」がそれぞれ独立した屋号として並ぶ運用。
- **`{COMPANIES:wrap=""}`** の wrap オプションは `「」` の括弧文字を表す（先頭が開き、末尾が閉じ）。テンプレで指定した括弧でブロック内 COMPANY エントリを囲む。
- 漫画家名は `[PERSON]` エントリで `person_aliases` から選ぶ。共著の場合は同ブロック内に PERSON エントリを 2 件並べる（`{PERSONS}` プレースホルダの `sep` オプション、既定値 `、` で結合される）。

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

### v1.2.2 — クレジット一括入力フォーマットの完全可逆化（LOGO / 備考 / 本放送限定 / 並列継承 / @cols / @notes）+ 右クリック一括編集 + 上位レベル備考 UI

クレジット編集の操作性をさらに引き上げる目的で、v1.2.1 で導入した **クレジット一括入力ダイアログ** を「**Draft の任意スコープを文字列化 → 編集 → 戻す**」というラウンドトリップ可能な構造に拡張した。これにより、テキストエディタの感覚で既存クレジットを大幅に書き換えたり、特定の役職だけを抜き出して整形し直したりできるようになる。あわせて、Card/Tier/Group/Role の備考列（v1.2.0 から DB に存在していたが GUI 露出が無かった）を編集できる **新パネル** を導入した。DB スキーマ変更は無し。

#### 追加: 一括入力フォーマットの拡張構文

`CreditBulkInputParser` / `BulkParseResult` に v1.2.2 で追加された構文。既存（v1.2.1）の構文は完全互換。

| 入力パターン | 解釈 |
|---|---|
| `[屋号#CIバージョン]`（行全体またはセル） | LOGO エントリ。最右の `#` で「左側＝屋号テキスト」「右側＝CI バージョンラベル」に分解。屋号 alias 名と一致する屋号配下のロゴから `ci_version_label` 完全一致で `logo_id` を引き当てる。未ヒットなら TEXT 降格 + InfoMessage（マスタ管理画面の「ロゴ」タブで明示登録するよう促す） |
| 行頭 `🎬`（U+1F3AC、後続スペースは省略可） | そのエントリを `is_broadcast_only=1` として登録（本放送限定エントリ） |
| 行末 ` // 備考`（半角SP + スラッシュ2 + 半角SP + 任意文字列） | そのエントリの `notes` に保存。`//` 自身を備考に含めたい場合は未対応 |
| 行頭 `& `（半角アンパサンド + 半角SP） | 直前エントリと A/B 併記関係。Draft 上は `RequestParallelWithPrevious=true`、保存時に `CreditSaveService` の新フェーズ 2.7 が直前エントリの実 ID を引き当てて `parallel_with_entry_id` に書き込む |
| `@cols=N`（ブロック先頭の単独行） | そのブロックの `col_count` を明示指定。省略時は従来どおりタブ数+1 で推測 |
| `@notes=備考`（各レベル区切り行直後の単独行） | 直近で開かれた Card / Tier / Group / Role / Block の `notes` に保存。スコープ自動遷移：Role 直後の 2 回目の `@notes=` はその役職の最初のブロックを対象とする。値が空文字なら明示クリア |

修飾子は重ねがけ可（例: `🎬 & 山田 太郎 // 旧名義あり`）。順序は問わない（`& 🎬 山田 // ...` でも同じ）。

#### 追加: 逆翻訳エンコーダ `CreditBulkInputEncoder`

`PrecureDataStars.Catalog/Forms/Drafting/CreditBulkInputEncoder.cs`。Draft レイヤ（`DraftCredit` / `DraftCard` / `DraftTier` / `DraftGroup` / `DraftRole`）を一括入力フォーマット文字列に変換する `static` クラス。

公開 API（5 メソッド）:
- `EncodeFullAsync(DraftCredit, LookupCache, CancellationToken)` — クレジット全体
- `EncodeCardAsync(DraftCard, LookupCache, CancellationToken)` — 1 カード分
- `EncodeTierAsync(DraftTier, LookupCache, CancellationToken)` — 1 Tier 分
- `EncodeGroupAsync(DraftGroup, LookupCache, CancellationToken)` — 1 Group 分
- `EncodeRoleAsync(DraftRole, LookupCache, CancellationToken)` — 1 役職分

サポートする出力構文:
- 階層区切り `----` / `---` / `--` / 空行（ブロック区切り）
- 役職開始行 `役職名:`（`LookupCache.LookupRoleNameJaAsync` で名前解決、未登録は role_code フォールバック）
- `@notes=値`（Card/Tier/Group/Role/Block 全レベル）
- `@cols=N`（ColCount > 1 のときのみ明示出力。ColCount=1 は省略時のデフォルトと同じなので省略）
- `[[屋号]]` グループトップ屋号
- `[屋号]` COMPANY エントリ
- `[屋号#CIバージョン]` LOGO エントリ（`LookupCache.LookupLogoComponentsAsync` で屋号と CI バージョンを分解取得）
- `<キャラ>声優` CHARACTER_VOICE エントリ
- 行頭 `🎬 ` IsBroadcastOnly
- 行頭 `& ` 併記継続（`RequestParallelWithPrevious` または `ParallelWithEntryId` が直前エントリ ID と一致で判定）
- 行末 ` // 備考` エントリ Notes
- TEXT エントリは `raw_text` をそのまま出力
- 削除マーク済み Draft ノードは出力対象外

ラウンドトリップ性: Encoder の出力を `CreditBulkInputParser` に通すと、マスタ ID 解決後の状態で同じ構造が再現できる。ただし以下の許容済み制限がある:
- `IsForcedNewCharacter`（アスタ付き）は Draft レイヤに保持されないため、再エンコード時にアスタは付かない（再パース時に同名キャラが存在すれば既存採用）
- 所属表記内に括弧が含まれる場合（例: `"東映 (本社)"`）は再パース時に右端括弧で分解されるため厳密ラウンドトリップしない

#### 追加: ダイアログの ReplaceScope モード

`CreditBulkInputDialog` を 2 モード化:

| モード | 起動経路 | 動作 |
|---|---|---|
| `BulkInputMode.AppendToCredit`（v1.2.1 既存仕様） | 左ペイン「📝 クレジット一括入力...」ボタン | `CreditBulkApplyService.ApplyToDraftAsync` を呼び、パース結果を Draft 末尾に追加 |
| `BulkInputMode.ReplaceScope`（v1.2.2 新規） | ツリー右クリック「📝 一括入力で編集...」 | `CreditBulkApplyService.ApplyToDraftReplaceAsync` を呼び、選択スコープ配下を新パース結果で置換 |

ReplaceScope モードでは、起動時に対象スコープを `CreditBulkInputEncoder` で逆翻訳した文字列を初期値としてテキストエディタにロードする。ダイアログ上部に専用ラベル（薄青背景）でスコープ表示を行い、ユーザーが「何が編集されるか」を即座に把握できるようにした。

`CreditBulkApplyService.ApplyToDraftReplaceAsync(parsed, session, scope, updatedBy, ct)` の動作:
1. 対象スコープ配下の既存子ノードを `ClearChildren` でクリア（Added は親リストから直接除去、Unchanged/Modified は `MarkDeleted()` で物理除去）
2. パース結果からスコープに対応する範囲を抜き出して新規 Draft 生成
3. スコープ自身（カード／ティア／グループ／役職）の Notes はパース結果のトップレベルから転写
4. パース結果が想定スコープより外側を持っている場合（例: Role スコープなのに `----` でカードを増やした）は最上位のみ採用、残りは `InfoMessages` に警告

#### 追加: ツリー右クリックメニュー「📝 一括入力で編集...」

`CreditEditorForm.treeStructure` に `ContextMenuStrip` を追加。右クリック時に `MouseDown` ハンドラで該当ノードを選択状態にした上で `Opening` イベントを発火させ、`mnuBulkEditScope` の有効/無効と表示テキストを選択ノード種別に合わせて更新する。

| 選択ノード種別 | 編集対象 | メニュー表示 |
|---|---|---|
| クレジットルート（Tag なし） | クレジット全体 | 「📝 一括入力で編集... (対象: クレジット全体)」 |
| Card | そのカード 1 枚 | 「📝 一括入力で編集... (対象: カード)」 |
| Tier | その Tier 内 | 「📝 一括入力で編集... (対象: ティア)」 |
| Group | その Group 内 | 「📝 一括入力で編集... (対象: グループ)」 |
| CardRole | その役職内 | 「📝 一括入力で編集... (対象: 役職)」 |
| Block / Entry / ThemeSongVirtual | （対象外） | 「📝 一括入力で編集... (この種別は対象外)」 |

メニュー押下時の処理は `OnBulkEditScopeAsync`：
1. `ResolveBulkEditScope` でツリー選択から `DraftScopeRef` を構築
2. スコープ別の `CreditBulkInputEncoder.EncodeXxxAsync` で初期テキストを生成
3. `CreditBulkApplyService` を `LogosRepository` 含めて構築（v1.2.2 拡張のため）
4. `CreditBulkInputDialog` の ReplaceScope モードコンストラクタで起動
5. 適用成功なら `RebuildTreeFromDraftAsync` + `RefreshPreviewAsync` + `UpdateButtonStates`

#### 追加: 上位レベル備考編集パネル `NodePropertiesEditorPanel`

`PrecureDataStars.Catalog/Forms/NodePropertiesEditorPanel.cs` および `.Designer.cs`。Card / Tier / Group / Role の備考列（`credit_cards.notes` / `credit_card_tiers.notes` / `credit_card_groups.notes` / `credit_card_roles.notes`）を直接編集できる UserControl。

レイアウト（縦積み）:
- ヘッダラベル（太字）：「選択中: カード #2」など対象ノード情報
- 補足ラベル（灰色）：「カード備考は credit_cards.notes に保存されます」など保存先テーブル説明
- 「備考 (notes):」見出し
- Notes 編集 TextBox（複数行・縦スクロール・Consolas 等幅・WordWrap=true・AcceptsReturn=true）
- 「💾 保存」ボタン（Dock=Bottom）

クレジット編集画面の右ペインに `EntryEditorPanel` / `BlockEditorPanel` と並ぶ形でスタックされ、`OnTreeNodeSelected` でノード種別に応じて Visible が切り替わる:
- Block 選択 → `BlockEditorPanel` のみ Visible
- Entry 選択 → `EntryEditorPanel` のみ Visible
- Card / Tier / Group / CardRole 選択（v1.2.2 追加）→ `NodePropertiesEditorPanel` のみ Visible
- ThemeSongVirtual / 何も選択していない → 全エディタ非アクティブ

公開 API:
- `Initialize(LookupCache)` — 役職名解決用に `LookupCache` を注入
- `ClearAndDisable()` — 編集対象を未設定にしてパネル全体を無効化
- `LoadCard(DraftCard)` / `LoadTier(DraftTier)` / `LoadGroup(DraftGroup)` / `LoadRoleAsync(DraftRole)` — 各種別でロード
- `NodeSaved` — `Func<Task>?` 型のイベント。Notes 反映後にツリー再構築をトリガするため、`BlockEditorPanel.BlockSaved` と同じ async-await 連動方式を採用

差分検出: ロード時の初期テキストと現在の TextBox 値を `string.Equals(StringComparison.Ordinal)` で比較し、差分があれば「💾 保存」ボタンを有効化。空文字は null として保存（DB スキーマ上、空文字と NULL を区別しない運用に整合）。

#### 追加: A/B 併記の保存フェーズ解決（CreditSaveService 新フェーズ 2.7）

`CreditSaveService.SaveAsync` の Phase 2（INSERT）と Phase 2.5（FK 再同期）の間に **新フェーズ 2.7** を追加し、`DraftEntry.RequestParallelWithPrevious=true` のエントリに対して `Entity.ParallelWithEntryId` を直前エントリの実 ID で解決する。

実装パターン:
- **Phase 2（既存）**：`Added` 状態のエントリを INSERT する直前に、`RequestParallelWithPrevious=true` なら `FindPreviousLiveEntryRealId(blk, en)` で同一ブロック内の直前 Deleted でないエントリの `RealId` を取得し、`en.Entity.ParallelWithEntryId` にセット。INSERT SQL は `parallel_with_entry_id` 列を含むため、そのまま DB に書き込まれる。直前エントリも Added の場合、本ループ順で先に INSERT 済みなので `RealId` は確定済み。
- **Phase 2.7（v1.2.2 追加）**：`Modified` または `Unchanged` 状態のエントリで `RequestParallelWithPrevious=true` のものを救済処理。直前エントリの `RealId` を引き当てて `Entity.ParallelWithEntryId` をセットし、`Unchanged` だった場合は `MarkModified()` で Phase 3 の UPDATE 対象に格上げ。
- **`ResetSessionState`**：保存成功後に `RequestParallelWithPrevious` フラグをクリアし、次回保存時に再解決を試みないようにする。

`FindPreviousLiveEntryRealId` ヘルパ：`block.Entries` 内で `target` の位置から先頭方向に走査して最初の `Deleted` でないエントリの `RealId` を返す。`target` がブロック先頭、または直前が全て `Deleted` の場合は null を返す（呼び出し側で `ParallelWithEntryId` も null となり、`CreditBulkApplyService` 側で「ブロック先頭の `& ` は無効」を InfoMessage に積む経路と整合）。

#### 追加: `LookupCache` 拡張（Encoder 用）

- `LookupLogoComponentsAsync(int logoId) -> (string CompanyAliasName, string CiVersionLabel)?` — logo_id を屋号名と CI バージョンラベルに分解。Encoder が `[屋号#CIバージョン]` 構文を組み立てるために使用。未登録 logo_id では null。
- `LookupRoleNameJaAsync(string? roleCode) -> string?` — 役職コードから `name_ja` のみを取り出す。Encoder が `役職名:` 行を組み立てるために使用。null コード / 未登録なら null。

#### 追加: `BulkParseResult` の新フィールド

`ParsedCard.Notes` / `ParsedTier.Notes` / `ParsedGroup.Notes` / `ParsedRole.Notes` / `ParsedBlock.Notes`、`ParsedBlock.ColCountExplicit`、`ParsedEntry.IsBroadcastOnly` / `IsParallelContinuation` / `Notes` / `LogoCiVersionLabel` を追加。`ParsedEntryKind.Logo` のドキュメントコメントに「v1.2.2 で構文サポート」を追記。

#### 追加: `DraftEntry.RequestParallelWithPrevious`

DB 非永続な一時フィールド。一括入力で行頭 `&` が付いていたエントリに対して `CreditBulkApplyService` が true をセットし、`CreditSaveService` の Phase 2 / 2.7 が消費する。永続化後（`ResetSessionState`）は false に戻る。

#### 追加: `DraftScopeRef`

`PrecureDataStars.Catalog/Forms/Drafting/DraftScopeRef.cs`。一括入力ダイアログの ReplaceScope モードで「どのスコープを置換対象にするか」を指す参照型。`ScopeKind` enum（`Credit` / `Card` / `Tier` / `Group` / `Role`）と、対応する Draft オブジェクトへの参照（`Credit` / `Card` / `Tier` / `Group` / `Role` のうち 1 つだけ非 null）を持つ不変オブジェクト。`ForCredit` / `ForCard` / `ForTier` / `ForGroup` / `ForRole` の静的ファクトリで生成。`GetDisplayLabel` でダイアログ上部のスコープ表示用ラベル文字列を返す。

#### CreditBulkApplyService の拡張

- コンストラクタに `LogosRepository` 引数を追加（LOGO エントリ解決用）
- 新メソッド `ResolveLogoAsync(companyName, ciVersionLabel, ct) -> int?`：屋号 alias 名で `company_aliases` を完全一致検索 → 屋号 alias_id 配下のロゴから `ci_version_label` 一致を採用。未ヒットは null（呼び出し側で TEXT 降格）。屋号自動投入は LOGO 文脈では行わない方針（LOGO はマスタ管理画面で明示登録すべきリソースのため）
- 新メソッド `ApplyToDraftReplaceAsync(parsed, session, scope, updatedBy, ct)`：ReplaceScope モード適用
- `AppendParsedEntryAsync` に LOGO 種別分岐を追加し、`ParsedEntryKind.Logo` を `AppendLogoEntry` で `entry_kind=LOGO` + `logo_id` として登録
- 全種別の AppendXxxEntry 直後に `ApplyEntryModifiersToLast` を呼ぶ統一フックで `IsBroadcastOnly` / `Notes` / `RequestParallelWithPrevious` をエントリに転写
- Card / Tier / Group の Notes は `ApplyNotesIfChanged` ヘルパで「値変更あり時のみ MarkModified」する経路で転写（既存ノード再利用時の余計な UPDATE を抑止）。Role / Block の Notes は新規ノード前提なので直接代入
- ParsedBlock.ColCountExplicit が true なら明示指定値を `block.Entity.ColCount` に優先反映

#### CreditBulkInputParser の拡張

- 状態機械に `pendingNotesTarget`（`NotesTarget` enum: None/Card/Tier/Group/Role/Block）と `pendingColsForBlock` を追加
- `[X#Y]` 専用の `BracketLogoRegex` を追加し、`[XXX]` 判定より先に評価する順序に
- `🎬` / `& ` プレフィクスと ` // 備考` サフィックスをセル単位で剥がす前処理（順序問わずループ剥離）
- `@notes=` / `@cols=` ディレクティブ行の検出と `ApplyNotesDirective` ヘルパによるスコープ別割り当て
- 「協力 → キャスティング協力」リネーム後処理に Notes 引き継ぎを追加

#### 修正: 既存バグ — `card_seq` 等 tinyint 列に大きな退避値が UPDATE される問題（Phase 2.6 / Phase 4）

`CreditSaveService.SaveAsync` 内で seq 列を一旦退避値に飛ばす処理が 2 箇所あり、両方とも tinyint unsigned (0-255) の seq 列に対して範囲外の値を UPDATE していたため、「Out of range value for column 'card_seq' at row 1」エラーが出てトランザクション全体が失敗していた（v1.2.1 から存在する隠れバグ。「話数コピー → 編集 → 保存」のような Modified 状態の Card / Role / Block を含む保存経路で発火）。

**Phase 2.6（退避フェーズ）**:
- 旧実装: 全テーブル横断で `30000+i` を退避値に使用
- `card_seq` / `order_in_group` / `block_seq` は **tinyint unsigned (0-255)** のため 30000 が範囲外
- 修正: tinyint 系には退避値 `200+i`（既存 `BulkUpdateSeqAsync` と同レンジ、200..255 で最大 56 件）、smallint 系の `entry_seq` には従来どおり `30000+i`

**Phase 4（再採番フェーズ `Resequence2PhaseAsync`）**:
- 旧実装: 共通ヘルパで `50000+i` を退避値にハードコード
- Phase 4 は **すべての保存で走る**（Added 行のみのケースでも実行される）ため、Phase 2.6 を直しただけでは話数コピー直後の保存でも同じエラーが再発
- 修正: ヘルパに `int escapeBase` 引数を追加し、呼び出し側で型に応じた値を渡す。tinyint 列は `escapeBase=100`（100..255 で最大 156 件）、smallint 列は `escapeBase=50000`（既存値、smallint 範囲内で安全）
- Phase 2.6 の退避レンジ（200+i）と Phase 4 の退避レンジ（100+i）は重ならないため、UNIQUE 制約衝突なし

**ガード**:
- Phase 2.6: `EnsureTinyEscapeRoom` ヘルパで tinyint カウンタが 255 を超えた場合は明示的に `InvalidOperationException`
- Phase 4: `Resequence2PhaseAsync` 冒頭で `seqColumn` 名から tinyint 列か判定し、範囲超過なら `InvalidOperationException`

実用上、1 トランザクションで 56 件以上の Modified 行や 156 件以上の同一親 FK 配下の seq 行を扱うケースは想定外ですが、防御策として残しています。

#### 修正: 既存バグ — 話数コピー後の左ペインクレジットリストが古いまま残る問題

`CreditEditorForm.OnCopyCreditAsync` が `_suppressComboCascade=true` のスコープ内で `cboEpisode.SelectedValue` をコピー先に切り替えた後、`SelectedIndexChanged → ReloadCreditsAsync → lstCredits 再構成` の連鎖が抑止される設計のため、左ペインの「クレジットリスト」がコピー元エピソードのまま残るバグがあった（v1.2.1 の「簡易実装」コメントとして明示されていた挙動）。

修正: `_suppressComboCascade=true` のスコープ内で、コピー先エピソード ID を使って `_creditsRepo.GetByEpisodeAsync(destEpisodeId2)` を呼び、`KindOrder`（OP=1 / ED=2 / その他=999）でソートしてから `lstCredits.DataSource` に流し込む処理をインライン実行するように変更。コピー先 Draft はまだ DB 未保存（CreditId=0）のためリストには現れないが、同エピソードの他クレジット（既存の ED 等）が表示されるようになり、ユーザーが「コピー先エピソードに切り替わった」事実を視覚的に確認できる。

#### 改善: クレジットリストの表示ラベルを 1 始まり順序番号に変更

クレジット選択リスト（`lstCredits`）のラベルが従来 `#{credit_id}  {kind}  ({presentation})` と DB 主キーを直接表示する形式だったが、ユーザー視点では DB 主キーは無関係でかえって混乱の元（例: 同一エピソード内のクレジットが「#7 OP, #14 ED」のように飛び番表示されてしまう）だったため、表示母集合内での **1 始まり順序番号**に変更した。例: `#1  OP  (CARDS)` / `#2  ED  (CARDS)`。順序の起点はソート済み順（OP → ED → その他）。

`BuildCreditListLabel` のシグネチャに順序番号引数を追加し、呼び出し側 2 箇所（`ReloadCreditsAsync` と `OnCopyCreditAsync`）で `Select((c, i) => ...)` パターンに変更して 1 始まりの index を渡すようにした。

#### 削除なし

DB スキーマ変更なし。マイグレーションファイルなし。v1.2.1 既存の AppendToCredit モード挙動は完全互換（コンストラクタ・呼び出しともに既存呼び出し側にとっては無変更）。

#### 主な変更ファイル

新規追加:
- `PrecureDataStars.Catalog/Forms/Drafting/CreditBulkInputEncoder.cs`
- `PrecureDataStars.Catalog/Forms/Drafting/DraftScopeRef.cs`
- `PrecureDataStars.Catalog/Forms/NodePropertiesEditorPanel.cs`
- `PrecureDataStars.Catalog/Forms/NodePropertiesEditorPanel.Designer.cs`

修正:
- `PrecureDataStars.Catalog/Forms/Dialogs/BulkParseResult.cs`（新フィールド）
- `PrecureDataStars.Catalog/Forms/Dialogs/CreditBulkInputParser.cs`（新構文）
- `PrecureDataStars.Catalog/Forms/Dialogs/CreditBulkInputDialog.cs`（モード化、スコープラベル、ReplaceScope 適用）
- `PrecureDataStars.Catalog/Forms/Dialogs/CreditBulkInputDialog.Designer.cs`（lblScope 追加）
- `PrecureDataStars.Catalog/Forms/Dialogs/CreditBulkApplyService.cs`（LogosRepository 注入、ResolveLogoAsync、ApplyToDraftReplaceAsync、各層 Notes 転写、ColCountExplicit 反映、ApplyEntryModifiersToLast）
- `PrecureDataStars.Catalog/Forms/Drafting/DraftEntry.cs`（RequestParallelWithPrevious 一時フィールド）
- `PrecureDataStars.Catalog/Forms/Drafting/CreditSaveService.cs`（Phase 2 INSERT 直前の解決、Phase 2.7 救済、FindPreviousLiveEntryRealId、ResetSessionState のフラグクリア）
- `PrecureDataStars.Catalog/Forms/LookupCache.cs`（LookupLogoComponentsAsync、LookupRoleNameJaAsync）
- `PrecureDataStars.Catalog/Forms/CreditEditorForm.cs`（OnTreeNodeSelected 拡張、右クリックメニュー結線、OnBulkEditScopeAsync、ResolveBulkEditScope、CreditBulkApplyService の LogosRepository 注入呼び出し）
- `PrecureDataStars.Catalog/Forms/CreditEditorForm.Designer.cs`（nodePropsEditor 配置、treeContextMenu / mnuBulkEditScope 配置）
- `Directory.Build.props`（Version 1.2.1 → 1.2.2）

### v1.2.1 — クレジット一括入力 + 名寄せ機能 + プレビュー改良 + 既存バグ修正

クレジット編集の入力負担を大幅に減らす **テキスト一括投入機能** と、マスタ運用を支える **名義の名寄せ機能** を追加した。あわせて、v1.2.0 工程 F でマスタ化された `character_kind` がエディタで ENUM ハードコードのままだったバグを修正し、運用上ほぼ使われていなかった `character_aliases.valid_from` / `valid_to` を撤廃した。

#### 追加: クレジット一括入力ダイアログ

クレジット編集画面（`CreditEditorForm`）の左ペイン、「選択中クレジットのプロパティ」グループ内に **「📝 クレジット一括入力...」** ボタンを新設。複数行テキストとリアルタイムプレビューでクレジット内容をまとめて投入できる新ダイアログ `CreditBulkInputDialog` を開く。

**入力文法（`CreditBulkInputParser`）**:

| 入力パターン | 解釈 |
|---|---|
| `XXX:` または `XXX：`（行末コロン） | 役職開始 |
| `-`（半角ハイフン1個・前後トリム後の単独行） | ブロック区切り（v1.2.1 仕様変更：ロールは閉じない） |
| `--` | グループ区切り（v1.2.1 仕様変更） |
| `---` | ティア区切り（最大 tier_no=2、v1.2.1 仕様変更） |
| `----` | カード区切り（v1.2.1 仕様変更） |
| 空行 | 同一役職内のブロック区切り |
| `[XXX]`（行全体） | `entry_kind=COMPANY`（位置に関係なく常に COMPANY エントリ扱い） |
| `[[XXX]]`（行全体） | ブロックのグループトップ屋号（`leading_company_alias_id`）。ブロックの**最初の有意行**でのみ許可、それ以外の位置や重複指定は Block 警告 |
| タブ区切り行 | エントリ群（タブ最大数+1 = `col_count`） |
| `<キャラ名義>声優名義`（VOICE_CAST 役職内） | `entry_kind=CHARACTER_VOICE` で既存マスタ参照 |
| `<*キャラ名義>声優名義`（VOICE_CAST 役職内） | 強制新規キャラ作成（同名既存があっても別個に作る、モブ用途） |
| `<*X>` 直後の声優名のみ行 | 各行を別個の新規 X として処理（モブの「女生徒」「男子生徒」等） |
| 通常テキスト | `entry_kind=PERSON` |

**人物名の格納規則**：
- 半角SP / 全角SP 区切り `山田 一郎` → `family=山田 / given=一郎 / full=山田 一郎`
- 「・」区切り `ロイ・フォッコ` → `given=ロイ / family=フォッコ / full=ロイ・フォッコ`（外国名想定）
- 区切りなし `キャラメル` → `family=NULL / given=NULL / full=キャラメル`

**動作フロー**：

1. 左ペインのテキストエディタに入力 → **250ms デバウンス** でパースし、右ペインのツリープレビューにリアルタイム反映。
2. 警告（マスタ複数ヒット・先頭が役職指定でない・ハイフン5個以上・ティア3個目超・`<...>`/`<*...>` 構文崩れ・`<X>`（アスタなし）後にキャラ指定なし行・キャラ指定なし行が文脈なしに登場・`[[XXX]]` がブロック先頭以外で登場 等）はリスト表示。**Block 重大度の警告** が 1 件でもあれば「適用」ボタンが無効化される。
3. 「💾 適用」ボタン押下 → 未登録役職を順次新規登録（`QuickAddRoleDialog` を 1 件ずつ起動し、コードと英語名をユーザーに入力させる。日本語名はテキスト中の表記が事前入力される）。Person / Company / Character は自動登録（マスタに無ければ姓名分解と既定 character_kind="MOB" で投入）。マスタ引き当てに失敗した名前は `entry_kind=TEXT` に降格して `raw_text` として退避。
4. Draft セッションに新規 Card / Tier / Group / Role / Block / Entry として **末尾追加**。「ロール 0 件のカード 1 枚」状態のクレジットなら、その空カードを上書きしてから始める。
5. 中央ペインの「💾 保存」ボタン押下で既存フローと同じく **1 トランザクション** で DB へ確定。

新規ファイル: `Forms/Dialogs/CreditBulkInputDialog.{cs,Designer.cs}` / `Forms/Dialogs/BulkParseResult.cs` / `Forms/Dialogs/CreditBulkInputParser.cs` / `Forms/Dialogs/CreditBulkApplyService.cs`。`Forms/Dialogs/QuickAddRoleDialog.cs` には事前入力用の `PrefilledNameJa` / `PrefilledFormatKind` プロパティを追加した。

#### 追加: プレビューレンダラの VOICE_CAST 3 カラムフォールバック

`role_format_kind == "VOICE_CAST"` 役職に対してテンプレが未定義のとき、従来の「役職名 | エントリ群」2 カラム表示ではなく、**役職名 | キャラ名義 | 声優名義 の 3 カラム表** にフォールバックする。

- 同役職内（複数ブロック跨ぎ可）で「直前行と同じキャラ名表示」のときは、キャラ名セルを薄く（`class=dim`、空表示）してエスペックタイトル風に詰めて表示する。
- `col_count` は無視して常に 1 行 1 エントリ（VOICE_CAST にカラム分けの慣習が無いため）。
- CHARACTER_VOICE 以外の種別（PERSON / COMPANY / TEXT 等）が混じっていた場合の保険描画もあり、書式違いのエントリでも壊れず描画する。

`Forms/Preview/CreditPreviewRenderer.cs` に `RenderVoiceCastFallbackAsync` / `ResolveCharacterLabelAsync` / `ResolvePersonWithAffiliationAsync` を追加。CSS `table.fallback-vc-table` 系を `<style>` ブロックに追加。

#### 追加: 名寄せ機能（人物・企業・キャラの 3 対象に対称展開）

`CreditMastersEditorForm` の「人物名義」「企業屋号」「キャラクター名義」3 タブそれぞれに、選択中名義の付け替え／改名のための 2 ボタンを追加（合計 6 ボタン）：

- **「別人物（企業／キャラ）に付け替え...」**：`AliasReassignDialog` を開き、選択中名義の紐付け先を別の既存親に変更する。親の表示名は変更しない。
- **「この名義で改名...」**：`AliasRenameDialog` を開き、新しい name / name_kana を入力させて改名する。

**改名の挙動は対象種別で異なる**：

| 対象 | 動作 | predecessor / successor 自参照 |
|---|---|---|
| 人物名義（person_aliases） | **新 alias を INSERT**、中間表 person_alias_persons の紐付けを新 alias にコピー | ✅ 旧↔新を自動リンク |
| 企業屋号（company_aliases） | **新 alias を INSERT**、同一 company_id を維持 | ✅ 旧↔新を自動リンク |
| キャラ名義（character_aliases） | **現 alias を上書き**（character_aliases に predecessor/successor 列が無いため） | ❌ 履歴は残らない |

「親同期」チェックボックスを ON にすると、親本体（`persons.full_name` / `companies.name` / `characters.name`）の表示名も新表記で上書きされる。人物の場合、共同名義（中間表 2 行以上）のときは親同期がスキップされる（曖昧さ回避）。

**孤立した旧親**（紐付く名義が 0 件になった `persons` / `companies` / `characters`）は付け替え時に自動で **論理削除**（`is_deleted=1`）される。

新規ファイル: `Forms/Dialogs/AliasReassignDialog.{cs,Designer.cs}` / `Forms/Dialogs/AliasRenameDialog.{cs,Designer.cs}`。リポジトリ側は `PersonAliasesRepository` / `CompanyAliasesRepository` / `CharacterAliasesRepository` に `ReassignTo*Async` / `RenameAsync` を実装。

#### 修正: 既存バグ — `character_kind` がマスタバインドされていなかった

v1.2.0 工程 F で `character_kind` が ENUM からマスタテーブル（`character_kinds`）へ移行されたが、`CreditMastersEditorForm` のキャラクタータブの区分コンボは `cboChKind.Items.AddRange(new object[] { "MAIN", "SUPPORT", "GUEST", "MOB", "OTHER" })` のままハードコードされており、マスタ側で類型を増やしても UI に反映されない、初期投入されている "PRECURE / ALLY / VILLAIN / SUPPORTING" の 4 類型を選べないというバグになっていた。

v1.2.1 ではコンボの DataSource を `CharacterKindsRepository.GetAllAsync()` の結果にバインドする方式に変更し、表示文字列は `"<コード> — <日本語名>"` 形式（例: `"PRECURE — プリキュア"`）で出る。`CreditMastersEditorForm.cs` に補助メソッド `BindCharacterKindComboAsync` / `GetSelectedCharacterKindCode` / `SetCharacterKindComboValue` を追加し、`OnCharacterRowSelected` / `ClearCharacterForm` / `SaveCharacterAsync` をそれを使う形に書き換えた。

#### 撤廃: `character_aliases.valid_from` / `valid_to`

実運用上ほぼ使われていなかった `character_aliases` の有効期間 2 列を物理削除した。表記揺れは別 alias 行として並存させ、声優交代等の期間管理は `character_voice_castings` 側で `casting_kind` (REGULAR / SUBSTITUTE / TEMPORARY / MOB) と `valid_from` / `valid_to` の併用で扱う運用に統一した。これに伴い UI からも入力欄を撤去した。

新規ファイル: `db/migrations/v1.2.1_drop_character_aliases_valid_dates.sql`（INFORMATION_SCHEMA で列存在を確認してから ALTER する冪等形式）。`db/schema.sql` / `Models/CharacterAlias.cs` / `Repositories/CharacterAliasesRepository.cs` / `Forms/CreditMastersEditorForm.{cs,Designer.cs}` から該当箇所を削除。

#### 追加: 一括入力パーサ — 同名役職の自動継承

長尺クレジット（特に「声の出演」のように 1 つの役職が長く続いて、途中で「カード切替」「ティア切替」「グループ切替」が必要になる用途）で、ユーザーが各カード冒頭に同じ `役職名:` を繰り返し書く手間を省くため、`CreditBulkInputParser` に **同名役職の自動継承** を実装した。

**仕様**：
- `---`（カード区切り）／`--`（ティア区切り）／`-`（グループ区切り）のいずれかで区切られた直後、明示的な役職指定（`XXX:` 行）が来ないままエントリ行が来た場合、**直前の役職と同じ表示名でロールを暗黙的に再作成**する
- 暗黙再作成されたロールは新カード／新ティア／新グループ配下に自然に紐付く
- 上下段（ティア跨ぎ）やサブグループも跨いで継承される
- 区切り後に明示的な `XXX:` 行が来た場合は、自動継承は行わずその役職に切り替わる（追跡名も更新される）

**例**：

```
声の出演:
<美墨なぎさ>本名 陽子
<雪城ほのか>ゆかな
---
<美墨理恵>荘 真由美    ← 自動継承で「声の出演」ロールが新カード配下に作られる
---
<ピーサード>高橋 広樹  ← さらに自動継承
-
<雪城さなえ>野沢雅子    ← グループ区切り後も同様に自動継承
-
協力:                  ← 明示的な役職指定で「協力」に切り替わる
[東映アカデミー]
```

データ層では各カードに紐付く別々のロールエンティティとして保存されるため、編集ツリー上は通常どおり 1 カードに 1 ロールずつ並ぶ。表示上の「役職名カード跨ぎ省略」は別仕様（後述の VOICE_CAST 役職名カード跨ぎ抑止を参照）。

#### 追加: 一括入力パーサ — 姓名分割不能名義の Warning

PERSON / CHARACTER_VOICE エントリの人物名（声優名）が **半角SP / 全角SP / 「・」のいずれも含まない** 場合、姓・名に機械的に分解できないため `family_name` / `given_name` が両方 NULL で投入されることになる。データ整合性は壊れないが、後続の人物検索や姓・名ベースのソートで使えないことになるため、Warning レベルで警告を出して気付かせる。

警告例：「7 行目: 声優名「ゆかな」は姓・名に分割できません（半角SP / 全角SP / 「・」のいずれも含まないため、family_name / given_name は NULL で投入されます）。」

Warning レベルなので適用ボタンは無効化されない（芸名 1 単語の名義は普通にあり得るため、Block にすると現実的でない）。

#### 改良: 一括入力ダイアログのレイアウト調整 — プレビュー : 警告 = 3:1
右ペイン上下分割の比率を従来の概ね 1:1 から **プレビュー : 警告 = 3:1** に変更し、プレビュー領域を広く取った。`splitterDistance=500, panel1Min=200, panel2Min=120` で `ApplySplitterLayout` ヘルパに渡す形で適用される。

#### 追加: プレビュー — VOICE_CAST 役職名のカード跨ぎ省略

VOICE_CAST 役職（`role_format_kind == "VOICE_CAST"`）が **カード／ティア／グループを跨いで連続する** とき、2 回目以降は役職名カラム（左カラム）を空表示にする。直前ロールとの判定は role_code の同一性で行う。`原画` のような NORMAL 役職を間に挟むと chain は切れるため、再度 VOICE_CAST が出たときは役職名が再表示される。

これは「声の出演」のように 1 つの役職が長く続くクレジットを視覚的に集約するための演出で、上記の「同名役職の自動継承」とセットで使う想定。データ層では各カードに別々のロールエンティティとして保存されることに変わりはなく、表示時のみカード跨ぎ集約が行われる。

実装は `CreditPreviewRenderer` の `RenderOneCreditFromDbAsync` / `RenderDraftAsync` 両方に `prevVoiceCastRoleCode` 状態を持たせ、`RenderCardRoleCommonAsync` → `RenderRoleFallbackDispatchAsync` → `RenderVoiceCastFallbackAsync` に `bool suppressVoiceCastRoleName` を伝搬させる方式。テンプレ展開ルートでは尊重せずテンプレ作者の制御に任せる（{ROLE_NAME} 自動ラップでも、抑止指示はせず素直に役職名を出す）。

#### 追加: 一括入力パーサ — 「協力」役職の文脈依存リネーム（→「キャスティング協力」）

クレジットでは「協力」という役職名が文脈に応じて意味が変わる：声優ロールに付随していれば「キャスティング協力」、それ以外（制作協力／取材協力など）の文脈では別の意味になる。一括入力では文字列上の見分けが付かないため、**カード単位の文脈** から `CreditBulkInputParser` が自動推定する。

**動作**：
- パース完了後の後処理として、各カードを走査
- そのカードに **「声の出演」または「キャスト」を DisplayName に含むロール** が 1 つ以上ある場合
- そのカード内の DisplayName=「協力」のロールを **DisplayName=「キャスティング協力」に書き換える**

**例**：

```
声の出演:
<美墨なぎさ>本名 陽子
...
協力:               ← このカードに「声の出演」がある → 「キャスティング協力」に書き換え
[東映アカデミー]
---
制作:
[東映アニメーション]
協力:               ← このカードに VOICE_CAST 系が無い → 「協力」のまま
[XX社]
```

**マスタ引き当て後段との連携**：
- パーサはマスタを知らないため、`ResolvedRoleCode` には何もセットせず DisplayName を変えるのみ。
- 後段の `CreditBulkApplyService.ResolveRolesAsync` で `name_ja="キャスティング協力"` 完全一致でマスタを引く。
- マスタに該当行が無ければ `UnresolvedRoles` に積まれ、`QuickAddRoleDialog` 起動時に `PrefilledNameJa="キャスティング協力"` として運用者に追加を促す。
- 役職コードは運用者が自由に決められるが、`CASTING_COOPERATION` を推奨する（このコードはマスタ初期シードはせず、運用者が手動でマスタに追加する）。

#### 追加: プレビュー — キャスティング協力の VOICE_CAST テーブル末尾追記

「キャスティング協力」役職（`role_code = "CASTING_COOPERATION"`）が **同一カード内の VOICE_CAST 役職と共存する場合**、その VOICE_CAST 役職のフォールバック描画テーブル（3 カラム表）の **末尾に「協力」行として 1 行追記** する仕様。

**動作**：
- レンダラがカード単位で「VOICE_CAST 役職と CASTING_COOPERATION 役職が両方存在するか」を事前判定
- 両方ある場合、CASTING_COOPERATION 役職配下の全エントリを集約して VOICE_CAST 描画関数に渡す
- VOICE_CAST テーブル末尾に 1 行追加。表記は `<strong>協力</strong>　屋号1　屋号2 …`（太字「協力」+ 全角SP + 屋号列）
- CASTING_COOPERATION 役職本体の通常描画はスキップ（二重表示防止）
- VOICE_CAST が無いカードの CASTING_COOPERATION は通常通り独立描画される

**例**：

```
雪城さなえ          野沢 雅子
協力               東映アカデミー
```

レンダラ内のハードコード描画なのでテンプレ不要。実装は `CreditPreviewRenderer.cs` の `CollectCardCastingCooperationContextAsync`（DB 描画用）/ `CollectDraftCardCastingCooperationContext`（Draft 描画用）でカード単位の事前収集を行い、`RenderVoiceCastFallbackAsync` の新引数 `appendedCooperationEntries` で末尾追記する。

#### 追加: シリーズマスタ — 「絵コンテを明示しないシリーズ」フラグ

一部のプリキュアシリーズ（『ふたりはプリキュア』〜『スマイルプリキュア！』が対象想定）のエンディングクレジットでは「絵コンテ」と「演出」が独立した役職として並列表記されず、両者を 1 行にまとめてクレジットしていた慣習がある。この表示をプレビューで再現するため、`series` テーブルに `hide_storyboard_role TINYINT(1) NOT NULL DEFAULT 0` 列を追加した。

**動作（フラグ ON のとき）**：
- 同 Group 内に **`STORYBOARD`（絵コンテ）役職** と **`EPISODE_DIRECTOR`（演出）役職** がちょうど 1 つずつ存在し、各役職のエントリ数がともにちょうど 1 件のときのみ融合描画を発動
- **同名**（person_alias_id 一致 OR raw_text 一致）→ 役職名「（絵コンテ・）演出」+ 1 行で名前のみ
- **異名** → 役職名「演出」+ 2 行で「名前A （絵コンテ）<br>名前B （演出）」
- 共同絵コンテや共同演出（複数エントリ）の場合 → 融合せず通常描画にフォールバック（仕様未定のため安全側）

**新規ファイル**：
- `db/migrations/v1.2.1_series_hide_storyboard_role.sql`：`series` テーブルへの列追加（INFORMATION_SCHEMA で冪等 ALTER）
- `db/migrations/v1.2.1_seed_storyboard_and_director_roles.sql`：`STORYBOARD`「絵コンテ」と `EPISODE_DIRECTOR`「演出」の 2 役職を `roles` マスタにシード（INSERT ... ON DUPLICATE KEY UPDATE で冪等、既存値は尊重）。display_order は 100 / 110

**既存ファイル変更**：
- `Models/Series.cs`：`HideStoryboardRole` プロパティ追加
- `Repositories/SeriesRepository.cs`：3 つの SELECT、INSERT、UPDATE すべてに `hide_storyboard_role` 列を追加
- `db/schema.sql`：`series` の CREATE TABLE に `hide_storyboard_role` 列を追加（コメント付き）
- `Forms/SeriesEditForm.{cs,Designer.cs}`（Episodes プロジェクト）：チェックボックス `chkHideStoryboardRole` を Amazon Prime URL 行の下に追加。Clear / Load / Save の 3 箇所に双方向バインディング
- `Forms/Preview/CreditPreviewRenderer.cs`：`GetHideStoryboardRoleAsync` ヘルパ（軽量 SQL でフラグ取得）、`RenderStoryboardDirectorMergedAsync`（融合描画本体）、`TryDetectMergeableStoryboardDirector<TRole>`（ジェネリック判定ヘルパ、DB 描画と Draft 描画で共用）、`CollectEntriesUnderCardRoleAsync`（DB 側のエントリ集約）を追加。DB 描画ループと Draft 描画ループの Group 単位で融合分岐を実装

#### 補助変更: `PersonsRepository.QuickAddWithSingleAliasAsync` の引数追加

人物クイック追加時に `family_name` / `given_name` 列にも値が入るよう、引数に `familyName` / `givenName` を追加した。呼び出し側（`QuickAddPersonDialog`、および新規追加の `CreditBulkApplyService`）では氏名から素朴に分解して渡す（半角/全角SP区切り → family/given、「・」区切り → given/family、区切りなし → 両方 NULL）。

#### 仕様変更: ハイフン区切りの再マッピング（- = ブロック、-- = グループ、--- = ティア、---- = カード）

実運用で最も頻度の高い「同一ロール内のブロック区切り」を、最も短いハイフン 1 個に割り当てるため、ハイフン区切りの解釈を 1 段ずつシフトした：

| 入力 | v1.2.0 / v1.2.1 旧 | v1.2.1 新 |
|---|---|---|
| `-` | グループ区切り（ロール閉じる） | **ブロック区切り**（ロール継続、同ロール内で次のブロック開始） |
| `--` | ティア区切り | **グループ区切り** |
| `---` | カード区切り | **ティア区切り**（最大 tier_no=2） |
| `----` | (4個以上は警告) | **カード区切り** |
| `-----` 以上 | 警告 | 警告（5個以上） |

`-` は **ロールを閉じない** ため、同じロール内で「ブロックだけ分けたい」場面（例: 制作協力で先頭の屋号を leading_company にしたい複数ブロックを連結したい場合）に空行と等価で使える。同名ロール自動継承は `--` `---` `----` の 3 種で発動し、`-` では発動しない（そもそもロールを閉じないため）。

#### 改良: プレビュー — 階層余白とブロック区切りの視覚化

プレビュー HTML の CSS 階層余白を、序列が見て分かるよう以下のように調整した（基準は「グループ内のロール間 = 6px」）：

| 階層 | margin-top | 説明 |
|---|---|---|
| `.card` | 40px | 最大、明確に区切る |
| `.tier` | 24px | カードよりは小さく |
| `.group` | 14px | ティアよりは小さく |
| `.role` | 6px | グループ内のロール間（基準値） |
| `tr.block-break > td`（同ロール内のブロック区切り） | padding-top: 2px | ロール間より小さく（最小） |
| `tr.cooperation-row > td`（VOICE_CAST 末尾の「協力」追記行） | padding-top: 6px | 別ロール扱い相当の余白 |

これにより「カード ＞ ティア ＞ グループ ＞ ロール ＞ ブロック」の序列が視覚的に判別できるようになる。

`fallback-table` / `fallback-vc-table` のブロック跨ぎは、各ブロックの最初の `<tr>` に `class="block-break"` を付与する形で表現する。複数ブロック間の余白だけ縮めて、ロール内の視覚的なまとまりを保つ意図。

#### 修正: NewCreditDialog のラジオボタン排他バグ

`新規クレジット作成` ダイアログ（`CreditNewDialog`）で、`ED（エンディング）` と `CARDS（複数カード）` を同時にチェックできない不具合を修正。これは種別 (OP/ED) と presentation (CARDS/ROLL) の **4 つのラジオボタンが Form の Controls に直接 Add されていた** ことが原因で、WinForms のラジオボタンは「同じ親 Container 内のラジオが排他選択グループになる」ルールにより、4 つすべてが排他になっていた。

修正は各軸を別々の `Panel`（`pnlCreditKind` / `pnlPresentation`）で囲み、それぞれの Panel 配下のラジオボタン同士だけが排他選択となるようにした。フィールド名（`rbKindOp` / `rbKindEd` / `rbPresentationCards` / `rbPresentationRoll`）は変えていないので、`.cs` 側の `.Checked` 参照ロジックは無修正で動く。

#### 修正: DnD で Role / Entry を別親に移動して保存すると消える致命バグ

クレジット編集画面の TreeView 上で、`CardRole`（役職）を別 Card / Tier / Group へ DnD で移動する操作（`DropDraftRole`）、または `Entry` を別 Block へ DnD で移動する操作（`DropDraftEntry`）を行ってから「💾 保存」を押した場合、移動した行が **DB 上に反映されない／場合により消えたように見える** という致命的な保存ロストバグを修正した。

**根本原因（3 段階の複合バグ）**：

1. **`UpdateRoleAsync` / `UpdateBlockAsync` の SET 句に親 FK 列（`card_group_id` / `card_role_id`）が含まれていなかった**：Modified 状態の Role や Block が UPDATE されても、親変更が DB に伝播しなかった。
2. **`DropDraftRole` / `DropDraftEntry` の親 FK 設定タイミングが不安定**：移動先の親 Group / Block が Added で RealId が未確定だった場合、`movedRole.Entity.CardGroupId` / `movedEntry.Entity.BlockId` は古い値のまま残っていた。
3. **`Resequence2PhaseAsync` の `alreadyOk` 早期 return が Memory 値を見ていた**：DB 上が退避値（30000+）でも Memory 上が連番なら return してしまい、退避値が DB に残るケースが理論上存在した。

**対策（CreditSaveService.cs の保存パイプラインを刷新）**：

- **Phase 2.5（FK 再同期）を新設**：Phase 2 で全階層の `Added` に `RealId` が確定した直後に、全 Draft 階層を再帰的に走査して `Entity.<親FK>` 列を親の `RealId` で強制再代入する。値が変化した場合のみ Modified に格上げする（無駄な UPDATE を抑止）。
- **Phase 2.6（seq 列の退避）を新設**：Phase 3 で UPDATE を発行する前に、`Modified` 状態の Card / Role / Block / Entry の seq 列（`card_seq` / `order_in_group` / `block_seq` / `entry_seq`）を退避値（30000+）に一括 UPDATE しておく。これにより Phase 3 で同じ親グループ内の複数行が同時に UNIQUE 制約と衝突するシナリオを完全に回避する。
- **Phase 3 の UPDATE SQL を改修**：
  - `UpdateTierAsync` に `card_id =`、`UpdateGroupAsync` に `card_tier_id =`、`UpdateRoleAsync` に `card_group_id =`、`UpdateBlockAsync` に `card_role_id =` を SET 句に追加（階層対称化）。
  - 全 UPDATE SQL から seq 列（`card_seq` / `order_in_group` / `block_seq` / `entry_seq`）を SET 句から除外。これらは Phase 4 の Resequence で確定する。
- **Phase 4（Resequence）の改修**：
  - 退避値レンジを 30000+ から **50000+** に変更（Phase 2.6 の退避値レンジと衝突しないように分離）。
  - `alreadyOk` による早期 return を撤去し、無条件で 2 段階更新を走らせる（同値での UPDATE は冪等で副作用なし）。

これにより「DnD 操作 → 保存」で：
- Role を別 Group / Tier / Card に動かしても、DB の `card_group_id` が正しく書き換わる ✅
- Entry を別 Block に動かしても、DB の `block_id` が正しく書き換わる（こちらは元から SET 句に含まれていたが、新親が Added だったときの FK 値同期が Phase 2.5 で確実になる）✅
- 移動先・移動元の seq 値が UNIQUE 衝突せず正しい連番に確定する ✅

実装ファイル: `Forms/Drafting/CreditSaveService.cs` の 1 ファイル修正のみ。

#### 既知の制限事項（v1.2.1 ではスコープ外）

- `person_aliases` / `company_aliases` には DB 側に `valid_from` / `valid_to` 列が無いにもかかわらず、Model と UI（人物名義タブ・企業屋号タブの DateTimePicker）にはこれらが残っている。Dapper のマッピングが該当列の不在を黙ってスキップするため、UI で入力しても DB には保存されない（**実質的な死コード**）。本バージョンでは character_aliases のみのスコープに絞ったため、person_aliases / company_aliases の入力欄整理は次バージョンに送る。

### v1.2.0 — クレジット管理基盤の追加（Phase A: DB+Data / Phase B: マスタ管理 GUI）

TV シリーズおよび映画作品の OP/ED クレジットを構造化して管理するための基盤を新規導入した。クレジット中の「役職: 名義列」の繰り返し、声の出演（キャラクター × 声優ペア）、企業の制作著作・レーベル表記、ロゴ単独表示、主題歌情報、シリーズによって異なる「漫画・連載」等の特殊書式までを、型付きで一意に表現できるテーブル群を追加。

#### 追加されたテーブル群（16 表 + 既存 2 表への列追加）

**人物層**

- `persons` … 人物の同一性を持つ器（family_name / given_name / full_name / 読み / name_en / 監査列 / `is_deleted`）。表記揺れや改名は `person_aliases` 側で管理する。
- `person_aliases` … 人物の名義（表記）。改名時は `predecessor_alias_id` / `successor_alias_id` の自参照 FK で前後をリンクする。`valid_from` / `valid_to` で期間も指定可能。
- `person_alias_persons` … 名義 ⇄ 人物の多対多中間表。通常 1 alias = 1 person だが、共同名義の稀ケースに備えて多対多に正規化している。

**企業層**

- `companies` … 企業の同一性を持つ器。設立日・解散日も保持できる。
- `company_aliases` … 企業の屋号（表記）。屋号変更や分社化等で前後の屋号を辿れるよう `predecessor_alias_id` / `successor_alias_id` を持つ。
- `logos` … 屋号配下の CI バージョン別ロゴ。`(company_alias_id, ci_version_label)` UNIQUE。

**キャラクター層**

- `characters` … キャラクターマスタ（全プリキュアを通じて統一管理する設計のため `series_id` を持たない）。`character_kind` は v1.2.0 工程 F で `character_kinds` マスタへの FK 参照に変更（旧 ENUM の `MAIN/SUPPORT/GUEST/MOB/OTHER` は廃止）。初期投入される 4 類型は PRECURE（プリキュア本人）/ ALLY（仲間たち）/ VILLAIN（敵）/ SUPPORTING（とりまく人々）。All Stars・春映画・コラボ等で複数シリーズに登場するキャラは同一 `character_id` を共有する。
- `character_kinds` … キャラクター区分マスタ（v1.2.0 工程 F で新設）。`character_kind` を PK、`name_ja` / `name_en` / `display_order` を持つ。`series_kinds` / `part_types` と同形のマスタ表で、運用者が後から類型を追加・改名できる。
- `character_aliases` … キャラクターの名義（"美墨なぎさ" / "キュアブラック" / "ブラック" のような表記揺れ）。**v1.2.1 で `valid_from` / `valid_to` 列を物理削除**し、表記揺れを期間ではなく「別 alias 行として並存させる」運用に統一した（声優交代等の期間管理は `character_voice_castings` 側で行う）。
- `character_voice_castings` … キャラクター ⇄ 声優のキャスティング情報。`casting_kind` は REGULAR / SUBSTITUTE（代役）/ TEMPORARY（暫定）/ MOB（モブ）の 4 区分で、`valid_from` / `valid_to` の期間管理付き。

**役職層**

- `roles` … クレジット内の役職マスタ。`role_format_kind` で「この役職下のエントリは どのような書式・参照を取るか」を分類する 6 区分: NORMAL（普通の役職: 名義列）／ SERIAL（連載、`format_template` でシリーズ別表記を切り替え）／ THEME_SONG（主題歌、entry が song_recording と label を持つ）／ VOICE_CAST（声の出演、entry がキャラクター名義 + 人物名義のペア）／ COMPANY_ONLY（企業のみ）／ LOGO_ONLY（ロゴのみ）。**初期データ投入は行わない方針** — テーブル定義のみを用意し、運用者が業務で必要な役職を後から登録する。
- `series_role_format_overrides` … シリーズ × 役職 × 期間で書式テンプレを上書きする。書式解決の優先順は (1) 当該シリーズ × 役職 × 該当期間の本テーブル → (2) `roles.default_format_template` → (3) 単純連結。シリーズ途中の表記変更にも対応できるよう PK に `valid_from` を含む。

**クレジット本体（4 段階の階層）**

- `credits` … クレジット 1 件 = 1 行。`scope_kind` が SERIES のときは `series_id` を、EPISODE のときは `episode_id` を保持し、もう一方は NULL（FK 参照アクション列のため CHECK は使えず、整合性はトリガーで担保）。`credit_kind` は OP / ED の 2 値、`presentation` は CARDS / ROLL の 2 値。`(series_id, credit_kind)` と `(episode_id, credit_kind)` の 2 本の UNIQUE で「シリーズまたはエピソードに対し OP/ED 各 1 件まで」を担保。`part_type` を NULL にすると「規定位置（OP=OPENING / ED=ENDING）」を意味する。
- `credit_cards` … クレジット内のカード 1 枚 = 1 行。`presentation`=ROLL のクレジットでは `card_seq`=1 の 1 行のみ。
- `credit_card_tiers` … カード内の Tier（段組）1 つ = 1 行（v1.2.0 工程 G で新設）。`tier_no` は 1=上段 / 2=下段。カード新規作成時に `tier_no=1` を 1 行自動投入する運用（`CreditCardsRepository.InsertAsync` で 1 トランザクション）。役職ゼロのブランク Tier も独立に保持できる。
- `credit_card_groups` … Tier 内の Group（サブグループ）1 つ = 1 行（v1.2.0 工程 G で新設）。`group_no` は 1 始まり。Tier 新規作成時に `group_no=1` が自動投入される。同 Tier 内で役職が視覚的にサブグループを成すケース（例：[美術監督・色彩設計] と [撮影監督・撮影助手] が同 tier の中で別塊として表示される）を表現する。
- `credit_card_roles` … カード内に登場する役職 1 つ = 1 行。所属する Group を `card_group_id` で参照し、グループ内左右順は `order_in_group`（1 始まり）で表現する。Card / Tier / Group の階層関係は FK チェーン（card_role → card_group → card_tier → card）で一意に決まる。v1.2.0 工程 G で旧 4 列複合キー (`card_id`, `tier`, `group_in_tier`, `order_in_group`) から `card_group_id` 単一 FK + `order_in_group` の 2 列構成に刷新。`role_code` を NULL にできるのは「ブランクロール」用途（ロゴ単独表示の枠など）。役職新規作成時に Block 1 が自動投入される（`CreditCardRolesRepository.InsertAsync` で 1 トランザクション）。
- `credit_role_blocks` … 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロック。`row_count` × `col_count` は表示の枠（左→右、行が埋まれば次の行、v1.2.0 工程 F-fix3 で旧 `rows` / `cols` から改名）。`leading_company_alias_id` でブロック先頭に企業名を出すケースに対応。
- `credit_block_entries` … ブロック内のエントリ 1 つ = 1 行。`entry_kind` に応じて参照先カラムが決まる: PERSON / CHARACTER_VOICE（人物 + キャラクター）／ COMPANY / LOGO / TEXT（マスタ未登録のフリーテキスト退避口）。v1.2.0 工程 H で `SONG` 種別と `song_recording_id` 列を物理削除（主題歌は `episode_theme_songs` を真実の源泉とし、役職レベルでテンプレ展開時に動的取得する設計に切り替え）。`affiliation_company_alias_id` / `affiliation_text` で人物名義の小カッコ所属、`parallel_with_entry_id` で「A / B」併記の自参照を表現可能。整合性はトリガーで担保。

**主題歌**

- `episode_theme_songs` … エピソード × 主題歌の紐付け。`theme_kind` は OP / ED / INSERT の 3 値、OP / ED は `insert_seq=0` の 1 行のみ、INSERT は `insert_seq=1, 2, ...` と複数可（CHECK で担保）。クレジットの主題歌役職（`roles.role_format_kind='THEME_SONG'`）はここから歌情報を JOIN で引いてテンプレ展開時にレンダリングする。v1.2.0 工程 H 補修で `label_company_alias_id` 列・関連 FK・関連 INDEX を物理削除（楽曲の事実とレーベル表示は本来独立した関心事であり、レーベル名はクレジット側の COMPANY エントリで持つ運用に整理した）。

**既存 2 表への列追加**

- `series_kinds.credit_attach_to ENUM('SERIES','EPISODE') NOT NULL DEFAULT 'EPISODE'` … 当該シリーズ種別のクレジットがシリーズ単位で付くか、エピソード単位で付くかを宣言する。映画系（MOVIE / MOVIE_SHORT / SPRING）は `SERIES`、TV 系（TV / SPIN-OFF）は `EPISODE` にバックフィル。
- `part_types.default_credit_kind ENUM('OP','ED') NULL` … 当該パート種別が「規定で OP/ED クレジットを伴う」かを宣言する。`OPENING` を OP、`ENDING` を ED にバックフィル、その他は NULL（クレジットを伴わない）。`credits.part_type` が NULL のクレジットは、ここの値が `credit_kind` と一致するパート（OP=OPENING、ED=ENDING）で流れる、と解釈する。

#### 追加されたデータ層クラス（PrecureDataStars.Data）

- Models: `Person` / `PersonAlias` / `PersonAliasPerson` / `Company` / `CompanyAlias` / `Logo` / `Character` / `CharacterAlias` / `CharacterVoiceCasting` / `Role` / `SeriesRoleFormatOverride` / `Credit` / `CreditCard` / `CreditCardRole` / `CreditRoleBlock` / `CreditBlockEntry` / `EpisodeThemeSong`（17 ファイル新規）。
- Models（既存更新）: `SeriesKind` に `CreditAttachTo`、`PartType` に `DefaultCreditKind` プロパティを追加。
- Repositories: 上記 17 モデルに 1:1 対応する CRUD リポジトリ（`PersonsRepository` など、検索 / UPSERT / 論理削除を完備）。`SeriesKindsRepository` / `PartTypesRepository` の SELECT に新カラムを追加。

#### 追加された UI（PrecureDataStars.Catalog）

- メインメニューの「マスタ管理...」直下に「**クレジット系マスタ管理...**」を新設し、`CreditMastersEditorForm`（`Forms/CreditMastersEditorForm.cs`）を開く。
- フォームは TabControl による 13 タブ構成: **人物** / **人物名義** / **企業** / **企業屋号** / **ロゴ** / **キャラクター** / **キャラクター名義** / **声優キャスティング** / **役職** / **シリーズ書式上書き** / **エピソード主題歌** / **シリーズ種別** / **パート種別**。各タブに DataGridView + 編集パネル + 「新規」「保存」「削除」ボタンを配置（既存 `MastersEditorForm` と同じ操作流儀）。タブ並びは「親マスタ → その名義」を隣接させて編集動線を最短化している。
- **人物名義タブ**: 人物選択 → その人物に紐づく名義一覧。前任名義 ID / 後任名義 ID で改名前後を自参照リンク、有効期間を date で保持。共同名義（中間表 `person_alias_persons`）の追加・解除も同タブ右下のリスト UI で行う（通常は 1 alias = 1 person のため、共同名義はオプション扱い）。新規名義を保存した時点で中間表に主人物が `person_seq=1` で自動投入される。
- **企業屋号タブ**: 企業選択 → 屋号一覧。前任屋号 ID / 後任屋号 ID で改名・分社化前後を自参照リンク、有効期間を date で保持。
- **ロゴタブ**: 企業選択 → 屋号選択（連動）→ ロゴ一覧。CI バージョンラベル + 有効期間 + 概要説明で同一屋号配下の複数 CI を時期で並べる（UNIQUE は `(company_alias_id, ci_version_label)`）。
- **キャラクター名義タブ**: キャラクター選択 → 名義一覧。「美墨なぎさ」「キュアブラック」「ブラック」のような同一キャラ別表記を期間付きで列挙する。前後リンクや共同名義の概念は持たないシンプル構造。
- 親マスタ（人物 / 企業 / キャラクター）を保存・更新したときに、子タブのコンボボックスにも即座に反映する追随更新を行う。
- 監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）は全グリッドで自動非表示にする `HideAuditColumns` ヘルパを内蔵。
- **v1.2.0 工程 H-16 — テンプレ DSL に `{#THEME_SONGS}` ループ構文を追加**: 主題歌役職のテンプレ作者が「曲名のカギ括弧の種類」「『作詞:』『作曲:』ラベルの表記」「項目順」「改行位置」「レーベル名の出力位置」を完全制御できるよう、新しいループ構文 `{#THEME_SONGS:opts}...{/THEME_SONGS}` を導入した。**(1) AST 拡張**：`Forms/TemplateRendering/TemplateNode.cs` に `ThemeSongsLoopNode` クラス（Options + Body）を追加。**(2) パーサ拡張**：`Forms/TemplateRendering/TemplateParser.cs` に `{#THEME_SONGS:...}...{/THEME_SONGS}` 構文の解析を追加（既存 `{#BLOCKS:...}` と同じ「対応する閉じタグまで子テンプレとして読む」方式）。**(3) ハンドラのリファクタリング**：`Forms/TemplateRendering/Handlers/ThemeSongsHandler.cs` の SQL 取得部分を `internal static FetchAsync` メソッドに切り出して、旧 `{THEME_SONGS}` プレースホルダ用と新 `{#THEME_SONGS}` ループ用で SQL を共有。`ThemeSongRow` DTO は `private` から `internal` へ昇格させ、Renderer から楽曲フィールドに直接アクセスできるようにした。**(4) レンダラ拡張**：`Forms/TemplateRendering/RoleTemplateRenderer.cs` の `RenderNodesAsync` / `ResolvePlaceholderAsync` にパラメータ `ThemeSongsHandler.ThemeSongRow? currentSong` を追加。switch に `ThemeSongsLoopNode` ケースを追加して、SQL から取得した楽曲行を順に `currentSong` として子テンプレを再帰評価する。`ResolvePlaceholderAsync` には新プレースホルダ `{SONG_TITLE}` / `{SONG_KIND}` / `{LYRICIST}` / `{COMPOSER}` / `{ARRANGER}` / `{SINGER}` / `{VARIANT_LABEL}` の解決ロジックを追加した（`currentSong` が null のときは空文字を返す）。BLOCKS ループ内では `currentSong=null`、THEME_SONGS ループ内では `currentBlock=null` に上書きすることでスコープ混同を防ぐ。**(5) 互換性**：旧 `{THEME_SONGS:columns=N,kind=...}` プレースホルダ版もそのまま残置（`ThemeSongsHandler.RenderAsync` 経由で動作）、既存テンプレを破壊しない。**典型的な書き換え例**：旧 `{THEME_SONGS:columns=2,kind=OP+ED}` を新構文では `{#THEME_SONGS:kind=OP+ED}「{SONG_TITLE}」<改行>作詞:{LYRICIST}<改行>作曲:{COMPOSER}<改行>編曲:{ARRANGER}<改行>うた:{SINGER}<空行>{/THEME_SONGS}{#BLOCKS:first}{COMPANIES:wrap=""}{/BLOCKS:first}` のように書く（最後の `{COMPANIES:wrap=""}` でレーベル名を末尾に追加、`wrap=""` でデフォルトのカギ括弧囲みを無効化）。実装ファイル: `Forms/TemplateRendering/TemplateNode.cs`、`Forms/TemplateRendering/TemplateParser.cs`、`Forms/TemplateRendering/RoleTemplateRenderer.cs`、`Forms/TemplateRendering/Handlers/ThemeSongsHandler.cs`。

- **v1.2.0 工程 H-15 — テンプレ展開結果の自動 2 カラムラップ機能**: テンプレ未定義の役職（フォールバック表示）と、テンプレ定義済みの役職（DSL 展開）でプレビュー上の整列がずれる問題を解決するため、テンプレ展開結果を自動的にフォールバック表と同じ「役職名カラム + 内容カラム」の 2 カラム HTML テーブルでラップする機能を追加した。**(1) 判定ルール**：テンプレ内に `{ROLE_NAME}` プレースホルダが含まれているかをチェック。**含まれていない場合**は、レンダラが自動的に `<table class="fallback-table"><tr><td class="role-name">{役職名}</td><td class="entry-cell">{展開結果}</td></tr></table>` でラップし、フォールバック役職と完全に視覚整列。**含まれている場合**は従来通り `<div class="role-rendered">` で素通し（テンプレ作者がレイアウトを完全制御する想定）。**(2) 移行容易性**：既存テンプレで `{ROLE_NAME}` を使っているものはそのまま動作（破壊的変更なし）、新規テンプレは `{ROLE_NAME}` を書かなければ自動的に整列に揃う。連載・主題歌・原作などの「役職名 + 内容」型テンプレでは特に有用。実装ファイル: `Forms/Preview/CreditPreviewRenderer.cs`。

- **v1.2.0 工程 H-14 — 改行コードの全面正規化**: 役職テンプレの改行が「マスタエディタで開いても表示されない」「保存後に展開すると二重改行になる」という問題を解決するため、改行コードの取り扱いを 3 段階で全面修正した。**(1) 真因 1**：MySQL TEXT 列から戻ってきた文字列が `\n` 単独の場合、Windows の TextBox は `\r\n` を前提としているため改行を表示しない。**(2) 真因 2**：CSS `white-space: pre-wrap` が有効な要素内では `\r` も改行扱いされるため、`\r\n` を含む文字列で `\n` のみを `<br>` 化すると `\r<br>` の組み合わせになり二重改行になる。**(3) 真因 3**：レンダラ側の改行置換が `\n` のみを対象にしていた。**修正**：(a) `CreditMastersEditorForm.OnRoleOverrideRowSelected` で TextBox に表示する直前に `\r\n / \r / \n` を統一して `\r\n` に正規化、(b) `CreditPreviewRenderer` で `\r\n / \r / \n` を `\n` に正規化してから `<br>` に置換、(c) CSS から `white-space: pre-wrap` を撤去して `<br>` のみで改行制御する方針に変更、(d) `ThemeSongsHandler` のセル内改行処理も同様に正規化。実装ファイル: `Forms/Preview/CreditPreviewRenderer.cs`、`Forms/CreditMastersEditorForm.cs`、`Forms/TemplateRendering/Handlers/ThemeSongsHandler.cs`。

- **v1.2.0 工程 H-13 — 役職テンプレートタブの UI 再設計**: 旧設計の役職テンプレートタブが「上部フィルタ」「下部詳細パネル」の役割分担が不明瞭で、保存・削除ボタンも詳細パネル右上で見落としやすかった問題を解決するため、UI を全面再設計した。**(1) 新構成**：(a) ヘッダ説明文 2 行（このタブの使い方）、(b) 上部に役職コンボ 1 個（フィルタ兼編集対象）、(c) 一覧グリッド、(d) グリッド直下に操作ボタン 3 個横並び（[+ 新規追加] [💾 保存 / 更新] [🗑 選択行を削除]）、(e) 詳細編集パネル（シリーズ・書式テンプレ・備考の 3 項目のみ）。**(2) 操作フロー**：上部の「役職」コンボを変えると一覧が絞り込まれ、新規追加・編集対象もこの役職になる。グリッドの行をクリックすると詳細パネルにロードされ、編集 → 「💾 保存 / 更新」で UPSERT。「+ 新規追加」で詳細パネルクリア → 新規作成モード。「🗑 選択行を削除」でグリッドの選択行を物理削除。**(3) 互換性**：旧フィールド名（cboOvSeries, gridRoleOverrides, cboOvRole, cboOvTemplateSeries, txtOvFormatTemplate, txtOvNotes, btnSaveOverride, btnDeleteOverride）は参照箇所が多いため流用しつつ、配置とラベルだけ変更。`cboOvRole` は使わなくなったため `Visible=false` で残置。実装ファイル: `Forms/CreditMastersEditorForm.{cs,Designer.cs}`。

- **v1.2.0 工程 H-12 — プレビュー幅 2 倍化 + 主題歌横並び HTML テーブル化 + EPISODE スコープのシリーズ別テンプレ反映**: クレジット編集画面のプレビューペイン幅が狭すぎて、主題歌のように横並び表示する役職で内容が見切れる問題を解決するため、3 件の改修を実施。**(1) プレビュー幅**：460px → 920px に倍化。`ClientSize` を 1780×880 → 2240×880、`MinimumSize` を 1600×700 → 1920×700 に拡大。各 SplitContainer の `Panel*MinSize` も新幅に追従。**(2) 主題歌の横並び**：旧 `ThemeSongsHandler` は `columns>=2` のとき半角スペース 4 個で「桁揃え風」に並べる方式だったが、HTML レンダラ側で連続空白が折り畳まれるため列が食い込んで見えていた。本工程で `<table>` 出力に変更：各曲を独立した `<td vertical-align:top; padding:0 32px 12px 0;>` セルに入れることで、確実に列が分離される。**(3) 役職テンプレートタブ**：上部の「役職フィルタ」コンボの DataSource がシリーズリストになっていて `SelectedValue as string roleCode` の型キャスト失敗で空表示になる問題を修正（コンボに役職リストを正しくバインド）。詳細パネルのシリーズコンボには「（既定 / 全シリーズ）」+ 全シリーズを `IdLabelNullable` で混在バインドし、保存時は `SelectedItem as IdLabelNullable` 経由で `int?` として取得。**(4) EPISODE スコープのシリーズ別テンプレ**：旧来 EPISODE スコープでは `RoleTemplatesRepository.ResolveAsync` に常に `null` を渡していたため、シリーズ別テンプレが効かなかった。`CreditPreviewRenderer.ResolveTemplateSeriesIdAsync` ヘルパー新設：SERIES スコープなら `credit.SeriesId`、EPISODE スコープなら `episodes` テーブルを 1 SQL で逆引きして所属シリーズ ID を取得。`CreditEditorForm` のツリー構築側（columns 抽出ルート）でも同じ逆引きを実装。実装ファイル: `Forms/Preview/CreditPreviewRenderer.cs`、`Forms/CreditEditorForm.{cs,Designer.cs}`、`Forms/CreditMastersEditorForm.{cs,Designer.cs}`、`Forms/TemplateRendering/Handlers/ThemeSongsHandler.cs`。

- **v1.2.0 工程 H-11 — クレジットプレビューの常時表示化 + Draft リアルタイム反映 + 4 ペイン化**: 「プレビューボタンを押さないと見えない」「未保存編集が反映されない」という UX 問題を解決するため、クレジット編集画面を 3 ペインから 4 ペインに変更し、プレビューを常時表示・Draft リアルタイム反映に改修した。**(1) レイアウト**：3 段ネスト SplitContainer（splitMain → splitCenterRest → splitPreviewRight）で「左 320 / 中央 / プレビュー / 右 380」の 4 ペイン構成に。`ClientSize` 1320×820 → 1780×880、`MinimumSize` 1240×650 → 1600×700。`BuildPreviewPane()` 新設：`pnlPreview` + 上部 Label「🌐 ライブプレビュー」+ Dock=Fill の WebBrowser、Padding=8 で枠ギリギリを避ける。**(2) Draft リアルタイム反映**：`CreditPreviewRenderer.RenderDraftAsync(CreditDraftSession)` 新設：DB を経由せず Draft オブジェクトの Card/Tier/Group/Role/Block/Entry を直接走査して HTML 化（仮 ID 含む）。`CreditEditorForm` 側で `_previewRenderer` をフィールドとして 1 回だけ生成・使い回し、`_previewDebounceTimer`（250ms）で連打を吸収。`RebuildTreeFromDraftAsync` の末尾で `RequestPreviewRefresh()` を呼ぶことで全 Draft 編集に追従。クレジット切替・保存・取消のタイミングでは即時 `await RefreshPreviewAsync()`。**(3) 旧 CreditPreviewForm 廃止**：`Forms/Preview/CreditPreviewForm.{cs,Designer.cs}` を物理削除、`btnPreviewHtml` ボタンと関連メソッド（`OnPreviewHtml` / `EnsurePreviewFormOpened` / `RefreshPreviewIfOpen`）も撤去。`grpScope` の高さを 360 に戻し、`grpCreditProps` の Y 位置を 368 に戻した（H-9 で 392 / 400 に拡げていた分）。**(4) 並び順 OP→ED 固定**：`KindOrder()` 関数（OP=1, ED=2, 他=999）で ListBox およびプレビュー両方をソートし、文字列辞書順では `ED < OP` になってしまう問題を解消。**(5) 階層余白**：プレビュー HTML の CSS に `.card { margin-top: 18px } .tier { margin-top: 12px } .group { margin-top: 8px } .role { margin-top: 4px }` を追加し、構造の切れ目を視覚化。`:first-child` で先頭余白を抑制。実装ファイル: `Forms/Preview/CreditPreviewRenderer.cs`、`Forms/CreditEditorForm.{cs,Designer.cs}`（Forms/Preview/CreditPreviewForm.{cs,Designer.cs} は削除）。

- **v1.2.0 工程 H-10 — credit_kinds マスタ化 + role_templates 統合テーブル化**: クレジット種別（OP/ED）と役職テンプレートのデータモデルを大規模リファクタリング。**(1) credit_kinds テーブル新設**：旧 `credits.credit_kind ENUM('OP','ED')` および `part_types.default_credit_kind ENUM('OP','ED')` をマスタ化し、表示名（`name_ja` =「オープニングクレジット」「エンディングクレジット」、`name_en`、`display_order`、`notes`、監査列）を持てるようにした。既存列は `VARCHAR(16)` + `credit_kinds(kind_code)` への FK 化。シードは OP / ED の 2 行のみ（必要に応じて GUI から追加可能だがマスタ管理 UI のクレジット種別タブは次工程で対応）。**(2) role_templates テーブル新設**：旧 `roles.default_format_template`（既定テンプレを `roles` の列に持つハイブリッド設計）と旧 `series_role_format_overrides`（シリーズ別書式上書き）の二箇所運用を、(role_code, series_id) UNIQUE の単一テーブルに統合。`series_id IS NULL` で既定、非 NULL でシリーズ専用テンプレを表現する。`valid_from / valid_to` の期間制限は廃止（実運用での要件が無い判断）。**`RoleTemplatesRepository.ResolveAsync(role_code, series_id)`** が「(role_code, series_id) で検索 → 無ければ (role_code, NULL) フォールバック」を 1 SQL（UNION ALL + priority 列）で実行し、レンダラ側のテンプレ取得を一元化。`UpsertAsync` は MySQL の NULL 値を含む UNIQUE の仕様（複数 NULL 行を許容する）を踏まえ、「既存検索 → INSERT or UPDATE」の 2 段階トランザクション実装としている。**(3) マイグレーション SQL `db/migrations/v1.2.0_h10_credit_kinds_and_role_templates.sql`**：8 ステップで冪等に実行（INFORMATION_SCHEMA で各オブジェクトの存在を確認してから ALTER）。旧 `roles.default_format_template` の値は `role_templates(series_id=NULL)` に、旧 `series_role_format_overrides` は同 (role_code, series_id) で valid_from が最も新しい行を `role_templates(series_id=...)` に移送してから旧構造を DROP。**(4) Catalog 側の改修**：`Program.cs` の DI を `CreditKindsRepository` / `RoleTemplatesRepository` に切り替え、`MainForm` のフィールド・コンストラクタ引数・`CreditMastersEditorForm` 起動引数を更新。`CreditEditorForm` の `_overridesRepo` を `_roleTemplatesRepo` + `_creditKindsRepo` に置き換え、主題歌役職の columns 抽出は `RoleTemplatesRepository.ResolveAsync` 経由でテンプレを引いてから行うよう変更。`QuickAddRoleDialog` から書式テンプレ入力欄を撤去（テンプレ編集はマスタ管理画面の「役職テンプレート」タブで行う設計に分離）。**(5) `CreditMastersEditorForm` の「シリーズ書式上書き」タブを「役職テンプレート」タブに転換**：上部に役職コンボ（`cboOvRole`）と一覧グリッド、下部にシリーズコンボ（`cboOvTemplateSeries`、「（既定/全シリーズ）」または特定シリーズを選択）と書式テンプレ複数行 TextBox（`Multiline=true, ScrollBars=Vertical, Font=Consolas 10F, AcceptsReturn=true, Height=160px`）を配置。改行・インデントを伴う複数行 DSL テンプレを実用的に編集可能に。`valid_from / valid_to / chkOvToNull` 関連 UI は廃止。**(6) `CreditPreviewRenderer` の全面改修**：(a) テンプレ取得を `RoleTemplatesRepository.ResolveAsync` 経由に切替、(b) CSS から枠線・カード見出し・段組階層インデントを撤廃しプレーンテキスト風レイアウトに、(c) テンプレ展開結果は HTML エスケープせず `Replace("\\n","<br>")` のみで素通し（`<b>` 等のタグがそのまま効く）、(d) クレジット種別の見出しは `credit_kinds.name_ja` から「オープニングクレジット」「エンディングクレジット」と日本語化、(e) LOGO エントリは `LookupCache.GetLogoForRenderingAsync` でロゴ→`company_alias_id`→屋号名に解決し屋号名のみ表示（CI バージョンラベル非表示）。**(7) `Models/Role.cs` から `DefaultFormatTemplate` プロパティを撤去**、`Models/SeriesRoleFormatOverride.cs` および `Repositories/SeriesRoleFormatOverridesRepository.cs` を物理削除、`Repositories/RolesRepository.cs` の SELECT/INSERT/UPDATE 列群から `default_format_template` を撤去。`db/schema.sql` も新スキーマに同期（新規構築時には 1 ファイルで完結）。実装ファイル: `db/migrations/v1.2.0_h10_credit_kinds_and_role_templates.sql`、`Models/CreditKind.cs`、`Models/RoleTemplate.cs`、`Repositories/CreditKindsRepository.cs`、`Repositories/RoleTemplatesRepository.cs`、`Forms/Preview/CreditPreviewRenderer.cs`、`Forms/CreditMastersEditorForm.{cs,Designer.cs}`、`Forms/CreditEditorForm.cs`、`Forms/Dialogs/QuickAddRoleDialog.{cs,Designer.cs}`、`Forms/LookupCache.cs`、`Program.cs`、`MainForm.cs`、その他。

- **v1.2.0 工程 H-9 — クレジット HTML プレビュー機能の追加**: クレジット編集中に「テンプレ展開後の最終形」を確認したい要望に応えて、非モーダルの HTML プレビューウィンドウを新設した。左ペインの「📋 話数コピー...」ボタンの下に **「🌐 HTMLプレビュー」ボタン** を配置し、押下すると `Forms/Preview/CreditPreviewForm`（`WebBrowser` コントロール 1 個を Dock=Fill で持つだけのシンプルな非モーダル Form）が画面右側に開く。中身は新設した `Forms/Preview/CreditPreviewRenderer` が組み立てる：(1) シリーズ書式上書き（`series_role_format_overrides`）が設定されていればそれを優先、無ければ `roles.default_format_template` を採用、(2) テンプレが取れた役職は既存の `RoleTemplateRenderer.RenderAsync`（`Forms/TemplateRendering/`）で DSL を展開して `<div class="role-rendered">` に流し込み、(3) テンプレが空の役職は **画像「vlcsnap」風のフォールバック表示**（役職名を左 8em 固定幅、ブロック内エントリを `col_count` で横並びの `<table>` レイアウト）に落とす、(4) Card / Tier / Group / Block の階層は CSS の枠線とインデントで視覚的に区切る。HTML には `<meta http-equiv="X-UA-Compatible" content="IE=edge">` を埋めて WebBrowser の既定 IE7 互換モードを IE11 互換に上書きする。エピソードスコープのクレジットを表示するときは、同エピソードに属する全クレジット（OP / ED / 挿入歌等）を CreditKind 順に縦に並べて 1 つの HTML 内に出力するため、ボタン 1 押下で第 N 話のクレジット完成形を一望できる。プレビューは非モーダルで開いたまま編集を続けられ、**クレジット切替（`OnCreditSelectedAsync`）・保存（`OnSaveDraftAsync`）・取消（`OnCancelDraftAsync`）の各タイミングで `RefreshPreviewIfOpen` ヘルパが呼ばれてプレビューを自動再描画する**ため、別エピソードへ切り替えても最新のクレジットが追従して見える。未保存 Draft があるとプレビューに反映されない（DB ベースで描画する設計のため）ため、ボタン押下時に「未保存の編集があります」確認ダイアログを出してユーザーに保存を促す。話数コピー直後の未保存クレジット（CreditId == 0）は DB 不在なのでプレビュー不可とし、保存後に再度試すよう案内する。実装ファイル: `Forms/Preview/CreditPreviewForm.{cs,Designer.cs}`、`Forms/Preview/CreditPreviewRenderer.cs`、`CreditEditorForm.{cs,Designer.cs}` の改修（btnPreviewHtml 追加、`_factory` / `_overridesRepo` / `_previewForm` フィールド追加、`OnPreviewHtml` / `EnsurePreviewFormOpened` / `RefreshPreviewIfOpen` メソッド追加、各保存系フックの末尾に再描画呼び出し追加）。

- **v1.2.0 工程 H-8 — クレジット編集の全面メモリ化（Draft セッション方式）+ クレジット話数コピー + 未保存ライフサイクル管理**: クレジット編集画面の編集体系を全面的に「Draft セッション方式」に作り替えた。旧仕様では各操作（カード追加・役職追加・エントリ編集・並べ替え・削除など）が即時 DB 反映で、ユーザーが途中で取りやめる手段が無く、複数の編集を連続して行うとそれぞれが個別トランザクションで実行されるため一貫性のあるロールバックも難しかった。本工程では中間状態を全部メモリ上の Draft オブジェクト（`Forms/Drafting/Draft*.cs`、9 クラス：`DraftCredit` / `DraftCard` / `DraftTier` / `DraftGroup` / `DraftRole` / `DraftBlock` / `DraftEntry` / `CreditDraftSession` / `DraftState` enum）に保持し、画面下部の「💾 保存」ボタン押下時に `CreditSaveService.SaveAsync` が 4 フェーズ（① 削除：深い階層から DELETE、② 新規作成：浅い階層から INSERT して RealId を伝播、③ 更新：Modified 状態を UPDATE、④ seq 整合性：退避値経由 2 段階更新で 1, 2, 3, ... 連番に再採番）を **1 トランザクション**で実行する設計に切り替えた。各 Draft オブジェクトは `RealId`（DB の実 ID）/ `TempId`（セッションが負数で払い出す仮 ID）/ `State`（Unchanged / Modified / Added / Deleted）の 3 値を持ち、`CurrentId` プロパティが「RealId があればそれ、無ければ TempId」を返すことで TreeView の Tag.Id にユニーク値を提供する。クレジット選択時に `CreditDraftLoader.LoadAsync` が DB から全階層（Card → Tier → Group → Role → Block → Entry）を読み込んで Draft セッションを構築し、編集中はこの Draft 木に対してのみ操作する。Block プロパティ編集（`BlockEditorPanel`）の「適用」ボタンと、Entry プロパティ編集（`EntryEditorPanel`）の「保存」「削除」ボタンも、DB を直接触るのではなく Draft の `Entity` を書き換えて `MarkModified()` / `MarkDeleted()` を呼ぶだけで済むようになった。**未保存中の視覚化**として、`treeStructure.BackColor` を `Color.FromArgb(0xFF, 0xFF, 0xE0)`（薄い黄色）に切り替え、ステータスバー末尾に「★ 未保存の変更あり」を表示、保存・取消ボタンを Enabled に切り替える `ApplyDraftBackgroundColor` ヘルパを設ける。**未保存ライフサイクル管理**として、クレジット切替（`OnCreditSelectedAsync`）・シリーズ切替（`OnSeriesChangedAsync`）・エピソード切替（`OnEpisodeChangedAsync`）・フォーム閉じ（`OnFormClosing`）の各タイミングで、未保存変更があれば `ConfirmUnsavedChangesAsync` ヘルパが「保存して続行 / 破棄して続行 / キャンセル」の 3 択ダイアログを出す。「キャンセル」が選ばれた場合は `_lastCreditListIndex` で記憶していた元のインデックスへ `lstCredits.SelectedIndex` を戻す（再帰防止フラグ `_suppressCreditSelection` で SelectedIndexChanged 連鎖を抑止）。フォーム閉じは FormClosing が同期コンテキストのため、一度 `e.Cancel = true` で閉じるのを止め、await 完了後に `_isClosingProgrammatically = true` を立てて `Close()` を再発行するパターンで対応。**`ListBox.SelectedIndexChanged` の連鎖発火対策**として、`_isLoadingCredit` / `_isReloadingSeries` / `_isReloadingCredits` の 3 つの再入防止フラグを各ハンドラ冒頭でチェックし、`DataSource = ...` の再代入や内部状態変化で複数回発火する Windows Forms の悪名高い挙動による多重 `_draftSession` 生成を防ぐ。**クレジット話数コピー機能**を新設：左ペインの「📋 話数コピー...」ボタン → `CreditCopyDialog`（コピー先シリーズ・エピソード・presentation・part_type・備考を選択。クレジット種別はコピー元と同じで固定）→ `CreditDraftLoader.CloneForCopyAsync` でコピー元を読み込んでから配下を全部 `State = Added` の新インスタンスに deep clone してコピー先 Draft を構築 → 画面をコピー先 Draft に切り替え → ユーザーが内容確認・編集してから「💾 保存」で 1 トランザクション INSERT。**シリーズ跨ぎ対応**：別シリーズの任意エピソードへもコピー可能で、「前作の OP 構造を新シリーズの第 1 話に流用してから差分編集」運用に対応。コピー先に同種クレジット（同 episode_id × credit_kind）が既存の場合は「上書き／中止」を選べ、上書き選択時には既存を即時論理削除（`SoftDeleteAsync`）してから新規 Draft を保存する。コピー時のコンボボックス（cboSeries / cboEpisode）切替は `_suppressComboCascade` フラグで連鎖発火を抑止しつつ実行され、ステータスバーや lstCredits の表示母集合がコピー先に正しく追従する。`CreditSaveService` 側は `Root.State == Added` を見て `INSERT INTO credits` を発行し採番された `credit_id` を `Root.RealId` に書き戻す処理を追加（`InsertCreditAsync` ヘルパ新設）。**クレジット本体プロパティ編集の復活**（H-8 ターン 6.5）：左ペインの「選択中クレジットのプロパティ」セクション（presentation / part_type / 備考）の「プロパティ保存」「クレジット削除」ボタンを Enabled に戻し、即時 DB 反映で動作させる（Draft とは別系統）。CARDS → ROLL 切替時は Draft 上のカード数が 2 枚以上だと拒否（ROLL は 1 枚固定）、未保存 Draft あり時は警告して中止。「ED を誤って ROLL で作っても後から CARDS に変更できる」要件を満たす。**Block 適用の Func<Task>+await 化**：`BlockEditorPanel.BlockSaved` / `EntryEditorPanel.EntrySaved` / `EntryDeleted` を `event EventHandler?` から `Func<Task>?` に変更し、購読側のツリー再構築 async を確実に await することで「適用ボタンを押しても画面が更新されない」問題を解消。Block 編集時に NumericUpDown のキー入力中の値が Value プロパティに反映されない問題には `ValidateChildren()` を `OnApplyAsync` 冒頭で呼ぶことで対応。実装は `Forms/Drafting/` 配下（9 クラス + Loader + SaveService）と `Forms/Dialogs/CreditCopyDialog.{cs,Designer.cs}` の新設、`Forms/CreditEditorForm.{cs,Designer.cs}` / `Forms/BlockEditorPanel.cs` / `Forms/EntryEditorPanel.cs` の改修で構成される。

- **v1.2.0 工程 H 補修（H-8 第 1 弾） — Entry の自由乗り換え DnD + エピソード主題歌の範囲コピー**: クレジットエディタの Entry ノードを別 Block へ DnD で移動できるよう拡張した（旧仕様では「同 (block_id, is_broadcast_only) 内のみ」並べ替え可能で、別ブロックへの移動は削除→新規作成しか手段が無かった）。CardRole の自由乗り換え DnD と同型の設計で、`OnTreeDragOver` で Entry のドロップ先として「別 Entry（上下半分で前後判定）」「Block ノード本体（同 is_broadcast_only グループの末尾）」の 2 種類を許容、`OnTreeDragDropAsync` の Entry 分岐で同 Block 内ならば既存 `BulkUpdateSeqAsync`、別 Block への移動なら新設の `RelocateBlockEntryAsync` を呼び分ける。`CreditBlockEntriesRepository.RelocateAsync` は単一トランザクション内で 3 段階更新を実行：(1) 影響を受ける全エントリ（旧 Block 残り + 移動対象 + 新 Block 既存）を退避値 30000+ に逃がして UNIQUE 衝突を回避 → (2) 移動対象の `block_id` を新 Block に書き換え → (3) 旧 Block を 1, 2, 3, ... に詰め、新 Block も挿入位置に移動対象を挟んで 1, 2, 3, ... に再採番。`is_broadcast_only` 値は移動元の値を保持する仕様（同フラグ違いの Entry にドロップした場合は移動先グループの末尾に正規化）。同時に **エピソード主題歌の範囲コピー機能** を追加：「クレジット系マスタ管理 → エピソード主題歌」タブに「範囲コピー...」ボタンを新設し、`EpisodeThemeSongRangeCopyDialog` を起動する。コピー元エピソード（シリーズ + エピソード ID）と、コピー先範囲（シリーズと `series_ep_no` の開始〜終了）を指定し、コピー元の OP / ED / INSERT 行を範囲内の各話に一括投入する。衝突時は「上書き」or「スキップ」をチェックボックスで選択、本放送限定行（`is_broadcast_only=1`）も同時にコピーするか選択可能。プレビュー欄では「第 N 話に X 件投入予定」のように処理予定を可視化する。1 話の主題歌を 2 話〜49 話に同じ内容で流し込む、等の用途を想定。

- **v1.2.0 工程 H 補修（H-7） — `credit_role_blocks.row_count` 撤廃 + ブロックプロパティ編集 UI + 連続番号表示**: クレジットエディタのブロックは `row_count × col_count` の 2 列で「表示の枠」を保持していたが、`row_count` は **エントリ数 ÷ `col_count` の切り上げ** で実行時に算出できる従属値であり、独立した列として持つと「`row_count` と実エントリ数の不整合」という不正状態を生む余地があった。本工程で `row_count` 列・関連 CHECK 制約 (`ck_block_row_count_pos`) を物理削除し、`col_count` のみが運用者の明示する設定値となる設計に整理した（マイグレーション STEP 8-J、新規 CREATE TABLE と schema.sql からも削除、`Models/CreditRoleBlock.cs` の `RowCount` プロパティと `CreditRoleBlocksRepository` の SELECT/INSERT/UPDATE 列群からも削除）。同時に **ブロックプロパティ編集 UI（`BlockEditorPanel`）** を新設：右ペインに `EntryEditorPanel` と並んでスタックされ、ツリーで Block ノードが選択されたときに表示される。`col_count`（NumericUpDown 1〜10）/ `block_seq`（NumericUpDown 1〜50）/ `leading_company_alias_id`（NumericUpDown + 検索ピッカー + 「+ 新規屋号」QuickAdd ボタン + 未指定チェックボックス）/ `notes`（複数行テキスト）を編集でき、保存後はツリーが自動再構築される。**ツリー上の番号表示は 1 始まりの連続番号**に変更：Card #N（card_seq の表示連番化）/ Role (order N)（order_in_group の表示連番化）/ Block #N（block_seq の表示連番化）/ Entry #N（entry_seq の表示連番化）。DB 上の seq 値には飛び番号や退避用 200 系が一時的に残り得るが、ユーザーから見える表記は常に詰めた連番で、削除・移動の直後に同階層の seq を 1, 2, 3, ... に詰める処理（`ResequenceSiblingsAsync`）を `OnDeleteNodeAsync` 末尾に追加した（各リポジトリの `BulkUpdateSeqAsync` がトランザクション内で「対象行を退避値 200 系に逃がす → 本来の値で再採番」の 2 段階更新を実行することで UNIQUE 制約との一時衝突を回避する設計）。Block ラベル表記は旧 `(1×1)` から **`(N cols, M entries)`** 形式に変更。ウィンドウタイトルは `クレジット編集 (v1.2.0 工程 B-3：エントリ編集)` から **`クレジット編集 (v1.2.0)`** に短縮。

- **v1.2.0 工程 H — SONG エントリ撤廃 + 主題歌の動的取得 + 役職テンプレ DSL エンジン + 主題歌役職体系の整備 + episode_theme_songs.label_company_alias_id 撤去**: 主題歌の表現を「クレジット側で楽曲を持つ」設計から「`episode_theme_songs` を真実の源泉として、役職レベルでテンプレ展開時に動的取得する」設計に切り替えた。これに伴い `credit_block_entries.entry_kind = 'SONG'` を **物理削除**（ENUM 値から除去 + 既存行 DELETE + `song_recording_id` 列 DROP + FK DROP + トリガから SONG 分岐削除）。クレジット側に楽曲を再指定するのは二重管理になり、検索性も損なう（「マーベラスエンターテイメントがレーベルとして登場するクレジット」を引きたい時、Company エントリだけを見れば全件が辿れる構造の方が明快）という判断による。代わりに、Mustache 風の簡易テンプレ DSL を `roles.default_format_template` で持ち、`{THEME_SONGS}` プレースホルダの解決時に `episode_theme_songs` × `song_recordings` × `songs` を JOIN して楽曲群を取得 → 整形済み文字列で返す専用ハンドラを実装した。連載役職（`SERIALIZED_IN`）も同 DSL で `{#BLOCKS:first}{ROLE_NAME}／{LEADING_COMPANY}「{COMPANIES}」\n漫画・{PERSONS}{/BLOCKS:first}{#BLOCKS:rest}\n　「{COMPANIES}」{/BLOCKS:rest}ほか` のように記述、屋号と名義のプレースホルダだけ持って、役職名称や区切り文字・改行・全角スペースインデント等の見た目はテンプレ文字列に直接記述する設計とした。DSL は 3 種類の構文を備える：(1) `{NAME}` または `{NAME:opt=val,opt=val}` のプレースホルダ展開（ROLE_NAME / LEADING_COMPANY / COMPANIES / PERSONS / LOGOS / TEXTS / THEME_SONGS）、(2) `{#BLOCKS}...{/BLOCKS}`（filter として `:first` / `:rest` / `:last` 指定可）のブロック繰り返し、(3) `{?NAME}...{/?NAME}` の条件分岐（値が非空のときだけ展開）。`{THEME_SONGS}` プレースホルダは `kind=OP` / `kind=ED` / `kind=INSERT` / `kind=OP+ED` / `kind=ED+INSERT` / `kind=ALL`（または省略）のフィルタオプションと、`columns=N` の横並びカラム数オプションを取れる。実装は `PrecureDataStars.Catalog/Forms/TemplateRendering/` 配下に `TemplateNode`（AST）/ `TemplateContext`（展開コンテキスト）/ `TemplateParser`（Mustache 風パーサ、ステートマシン）/ `RoleTemplateRenderer`（DSL 展開エンジン）/ `Handlers/ThemeSongsHandler`（{THEME_SONGS} 専用、JOIN 付き SQL を発行、kind フィルタを SQL の `IN` 句で動的構築）の 5 ファイルで構成.

  **主題歌役職の体系**：シリーズの時期に応じてクレジット表現が異なるため、5 つの役職を使い分ける設計とした：(1) `THEME_SONG_OP_COMBINED`「主題歌」（黎明期最初の 10 年程度の OP 枠用、OP と ED を 2 カラム横並びで「主題歌」として 1 ブロックに表示する書式、テンプレは `{ROLE_NAME}\n{THEME_SONGS:kind=OP+ED,columns=2}`）、(2) `THEME_SONG_OP`「オープニング主題歌」（中期以降の OP 枠用、OP 曲のみ、テンプレは `{ROLE_NAME}\n{THEME_SONGS:kind=OP}`）、(3) `THEME_SONG_ED`「エンディング主題歌」（中期以降の ED 枠用、ED 曲のみ、テンプレは `{ROLE_NAME}\n{THEME_SONGS:kind=ED}`）、(4) `INSERT_SONG`「挿入歌」（12 年目以降に挿入歌が独立してクレジットされるようになった以降の挿入歌枠、複数曲ありうる、テンプレは `{ROLE_NAME}\n{THEME_SONGS:kind=INSERT}`）、(5) `INSERT_SONGS_NONCREDITED`「挿入歌（ノンクレジット）」（実放送ではクレジットされなかったが楽曲事実としてデータベースに保持しておきたい挿入歌枠、`INSERT_SONG` と SQL 上は同じ `kind=INSERT` を引くが運用上は一方だけ置く前提、両方置かれた場合は両方とも楽曲を表示してユーザー判断を尊重する設計）。レーベル名（販売元）は伝統的にロゴではなくテキストでクレジットされるため、これらの主題歌役職にはレーベル枠を含めず、レーベルはクレジット側の `credit_block_entries` で `COMPANY` エントリ（屋号マスタ参照）または `TEXT` エントリ（マスタ未登録のフリーテキスト）として保持する運用とする。カードあたり 1 箇所のみ表示される慣例。

  クレジットエディタのツリーには `📋 Role` ノード配下に `📀 Song(OP/ED/INSERT): 『曲名』 [作詞:○○ / 作曲:○○ / 編曲:○○ / うた:○○]` の楽曲仮想サブノード（`NodeKind.ThemeSongVirtual`、読み取り専用、削除/並べ替え不可）を `episode_theme_songs` から動的に差し込み、役職コードに応じて theme_kind を絞り込む。`INSERT_SONGS_NONCREDITED` 役職の楽曲ノードには `🚫[ノンクレジット]` マークが付与され、本放送限定の楽曲には `🎬[本放送限定]` マークが付与される。

  マイグレーション STEP 8-H は冪等な 4 段階構成：(1) `SERIALIZED_IN` の `default_format_template` 初期投入、(2) 旧誤投入役職 `THEME_SONGS` を参照する `credit_card_roles` の `role_code` を NULL に書き換えてブランクロール化（配下の Block / Entry は CASCADE で消えずに残る）、(3) 旧 `THEME_SONGS` 役職を物理削除（誤った設計を将来に残さないため）、(4) 新 5 役職を `INSERT IGNORE` で投入。STEP 8-G（5 段階：SONG 行 DELETE → トリガ DROP→CREATE → ENUM 変更 → FK DROP → 列 DROP）と合わせて既存環境に冪等に流れる。

  **さらに同工程の補修として、`episode_theme_songs.label_company_alias_id` 列・関連 FK (`fk_ets_label_company`) ・関連 INDEX (`ix_ets_label_company`) を物理削除**（STEP 8-I）。「このエピソードの OP は何か」というのは楽曲の事実であって、レーベル会社（販売元）が何かはクレジット表示にだけ現れる事情。同じ楽曲が違うエピソードで違うレーベル名で表示されることもあれば、レーベル名なしで出る場合もあるため、`episode_theme_songs` に持たせるのは関心の混在となる。レーベル名は `credit_block_entries` の COMPANY/TEXT エントリで保持するのが正しい設計と判断した。これに合わせてクレジット系マスタ管理フォーム（`CreditMastersEditorForm`）の主題歌タブからは「label company_alias_id」入力行・「未指定」チェック・関連ピッカーボタンを撤去し、`Models/EpisodeThemeSong.cs` から `LabelCompanyAliasId` プロパティを削除、`EpisodeThemeSongsRepository` の SELECT/INSERT/UPDATE 列群からも対応する記述を削除した。

- **v1.2.0 工程 G — Tier / Group 階層の実体テーブル化と自動連動作成**: クレジット編集の操作手数を構造的に減らすため、Tier（段組）と Group（サブグループ）を「役職の集約結果」ではなく「実体テーブル」として持つように刷新した。`credit_card_tiers`（カード内の Tier 1 つ = 1 行、tier_no 1/2）と `credit_card_groups`（Tier 内の Group 1 つ = 1 行、group_no 1 始まり）を新設し、`credit_card_roles` の構造を **旧 4 列複合キー (card_id, tier, group_in_tier, order_in_group) → 新 2 列構成 (card_group_id, order_in_group)** に簡素化。Card / Tier / Group の階層関係は FK チェーン（card_role → card_group → card_tier → card）で一意に決まる。これにより「役職ゼロのブランク Tier / ブランク Group」も保持できるようになり、「+ Tier」ボタンで空 Tier を作って役職をそこに移動するワークフローが可能になった。さらに、操作手数削減のため **自動連動作成** を導入：(a) カード新規作成時に Tier 1 + Group 1 を 1 トランザクションで自動投入（`CreditCardsRepository.InsertAsync`）、(b) Tier 新規作成時に Group 1 を自動投入（`CreditCardTiersRepository.InsertAsync`）、(c) 役職新規作成時に Block 1（row_count=1, col_count=1）を自動投入（`CreditCardRolesRepository.InsertAsync`）。クレジット編集 UI には「+ Tier」「+ Group」の 2 ボタンを追加し、選択ノード種別による Enabled 制御を整理。Tier / Group ノードは実体テーブル化に伴い削除可能になり、CASCADE で配下の Group / Role / Block / Entry がまとめて連動削除される。データ移行は冪等な STEP 8-F（10 段階）で構成：(1) 新テーブル 2 つ作成 (2-3) 既存 credit_card_roles の (card_id, tier) と (card_id, tier, group_in_tier) を集約して INSERT IGNORE (4) credit_card_roles に card_group_id 列追加 (5) 旧 (card_id, tier, group_in_tier) JOIN で card_group_id を埋める (6) 全カードに Tier 1 / Group 1 を保証する漏れ補填 INSERT IGNORE (7) 旧 UNIQUE / 旧 CHECK / 旧 FK / 旧列を DROP (8) 新 UNIQUE と新 FK 追加 (9) card_group_id を NOT NULL 化。

- **v1.2.0 工程 F — クレジット入力中の即時マスタ投入（役職・キャラ名義）+ キャラクター区分マスタ化**: クレジット編集をマスタ画面に戻らず完遂できるように、未対応だった「役職」と「キャラクター名義」の即時投入を追加した。役職側は `RolePickerDialog` に「+ 新規役職...」ボタンを足し、押下で `QuickAddRoleDialog`（役職コード・表示名 和英・書式区分・既定書式テンプレ・表示順・備考の入力フォーム）が開く。登録すると `roles` に 1 行 INSERT され、ピッカーは新 role_code で自動 OK 扱いとなって閉じ、呼び出し元の役職追加処理がそのまま新規役職で進むワンクリック完結フロー。表示順の既定値は「既存最大 + 10」で、工程 D の DnD 並べ替えと同じ 10 単位飛び番運用に整合する。キャラ名義側は `EntryEditorPanel` の `btnCharacterAliasNew` を結線し、`QuickAddCharacterAliasDialog` をモード切替式（既存キャラに名義追加 / キャラごと新規作成）で実装。新規作成モードでは `CharactersRepository.QuickAddWithSingleAliasAsync` が `characters + character_aliases` を 1 トランザクションで 2 行投入する。これに伴い、旧来 ENUM (`MAIN/SUPPORT/GUEST/MOB/OTHER`) で固定だった `characters.character_kind` を **マスタテーブル化** し、`character_kinds` 表と FK を新設。初期データとして「PRECURE / プリキュア」「ALLY / 仲間たち」「VILLAIN / 敵」「SUPPORTING / とりまく人々」の 4 類型を投入する（運用者が後から追加・改名できる独立テーブル）。`QuickAddCharacterAliasDialog` のキャラ区分コンボはこのマスタから動的に取得する。マイグレーション SQL は冪等で、既存環境では ENUM → VARCHAR(32) への型変更 → FK 追加 → 4 類型 INSERT IGNORE がすべて安全に流れる。

- **v1.2.0 工程 E — クレジット内サブグループと役職の自由乗り換え DnD**: クレジットエディタの構造ツリーを「クレジット → カード → Tier → Group → 役職 → ブロック → エントリ」の 7 階層に拡張し、同 tier 内で役職同士が視覚的にサブグループ（例：[美術監督・色彩設計] と [撮影監督・撮影助手] が同 tier の中で別塊として表示される）を成すケースを表現できるようにした。`credit_card_roles` テーブルには `group_in_tier TINYINT UNSIGNED NOT NULL DEFAULT 1` を追加し、旧 `order_in_tier` を `order_in_group` に改名（同 (card_id, tier, group_in_tier) 内の左右順）。UNIQUE 制約は `(card_id, tier, group_in_tier, order_in_group)` の 4 列複合に拡張された。サブグループが 1 個しかない（従来の 2 列構成と等価な）カードは `group_in_tier=1` だけを使う。ツリー上の Tier ノードと Group ノードは仮想ノード（DB 行を直接持たず、配下の役職の値を集約して生成）で、自身の削除・並べ替えはできないが、子要素の追加先ヒントとして機能する。役職追加ボタンは選択中ノード種別に応じて挿入先 (card, tier, group) を推測：Card 選択時は tier=1 / group=1、Tier 選択時はその tier の末尾グループ、Group 選択時はそのグループ末尾、Role 選択時は同グループ末尾。ツリー DnD は CardRole に「自由乗り換え」モードを実装し、別 Card・別 Tier・別 Group へ同じクレジット内であれば自由にドロップして移動できる（コピー後の試行錯誤に対応）：CardRole にドロップ → そのカードロールの上下半分で前後判定して同グループ内に挿入、Group にドロップ → そのグループ末尾、Tier にドロップ → その tier の末尾グループの末尾、Card にドロップ → tier=1 / group=1 の末尾。↑↓ ボタンによる並べ替えは引き続き同 (card, tier, group) 内のみで動作する（厳密な順序操作は ↑↓、自由な乗り換えは DnD という棲み分け）。`SeqReorderHelper` には旧 `ReorderCardRolesInTierAsync` を `ReorderCardRolesInGroupAsync` に改名・拡張したものと、自由乗り換え用の `RelocateCardRoleAsync`（旧グループ詰め直し + 新グループ挿入を退避値経由の 2 段階更新で実行）を新設した。

- **v1.2.0 工程 D — マスタ系タブの DnD 並べ替え**: クレジット系マスタ管理フォーム（`CreditMastersEditorForm`）の役職タブとエピソード主題歌タブに、行ヘッダドラッグによる DnD 並べ替えを追加した。役職タブでは `DataGridView` 上で行ヘッダをつかんで任意の位置にドロップすることで `roles.display_order` を 10 単位の飛び番（10, 20, 30, ...）で再採番できる。`RolesRepository.BulkUpdateDisplayOrderAsync` が 1 トランザクションで全件 UPDATE する実装で、`display_order` には UNIQUE 制約が無いため退避値経由の 2 段階更新は不要。エピソード主題歌タブでは挿入歌（`theme_kind='INSERT'`）の行のみが DnD 対象で、同 `(episode_id, is_broadcast_only, theme_kind='INSERT')` グループ内のみで並べ替え可能。グループ間（別エピソード・別 is_broadcast_only 値・OP/ED 行とのドロップ）はマウスカーソルが ⊘ になり拒否される。OP/ED は `episode_theme_songs.ck_ets_op_ed_no_insert_seq` CHECK 制約により `insert_seq=0` 固定で 1 グループに 1 行しか持てないため、もとより並べ替えの概念が存在しない。`EpisodeThemeSongsRepository.BulkUpdateInsertSeqAsync` は当該グループの INSERT 行をいったん DELETE してから新順序で INSERT し直すトランザクション設計（PK が `(episode_id, is_broadcast_only, theme_kind, insert_seq)` の自然キー 4 列複合のため）。

- **v1.2.0 工程 B-3 — エントリ編集 UI とマスタ自動投入（B-3a / B-3b / B-3c）**: 右ペインを `EntryEditorPanel` UserControl に置き換え、エントリ追加・編集・削除を完全な動作 UI として実装した。種別ラジオで PERSON / CHARACTER_VOICE / COMPANY / LOGO / SONG / TEXT のいずれかを選び、種別ごとの動的パネルが切り替わる方式で、人物・キャラ・企業・ロゴ・歌・自由テキストの取り違えを構造的に排除する。既存エントリの編集モードでは種別ラジオが無効化され（種別変更は「削除→新規追加」フローへ誘導）、保存時の DB 制約衝突（同 (block_id, is_broadcast_only, entry_seq) の重複）は分かりやすいダイアログで案内する。共通属性として「本放送限定エントリ」チェックボックス、entry_seq、備考を入力できる。エントリの並べ替えは↑↓ ボタンと TreeView ドラッグ＆ドロップの両方に対応し、同 (block_id, is_broadcast_only) グループ内のみで動作する（フラグ 0 行とフラグ 1 行は別グループとして扱い、グループ間の並べ替えは UI 側でブロックされる）。各種別の「検索...」ボタンは既存ピッカー（PersonAliasPickerDialog / CompanyAliasPickerDialog / SongRecordingPickerDialog）と工程 B-3b で新設したピッカー（CharacterAliasPickerDialog / LogoPickerDialog）に結線され、ID 直接入力が不要でマスタからの選択が可能。**工程 B-3c では「+ 新規...」ボタンを 4 個結線し、編集中のままマスタへ自動投入できるようにした**：`QuickAddPersonDialog`（人物 1 名 + 単独名義 1 件を `persons → person_aliases → person_alias_persons` の 3 段階トランザクションで投入、`PersonsRepository.QuickAddWithSingleAliasAsync` で実装）、`QuickAddCompanyAliasDialog`（モード切替で「既存企業に屋号追加」または「企業ごと新規作成」を選択。後者は `companies + company_aliases` を 1 トランザクションで投入、`CompaniesRepository.QuickAddWithSingleAliasAsync` で実装）、`QuickAddLogoDialog`（既存屋号に CI バージョンラベル + 有効期間付きでロゴを 1 件投入）。投入完了後は `LookupCache` の対応キャッシュを `Invalidate*` で破棄し、新 ID の名前解決を確実に DB から行う。これによりクレジット入力中に「マスタにまだ無い」と気付いても、入力フローを中断せずその場でマスタへ追加して続行できる。共同名義（複数人 1 名義）と複雑な屋号バリエーションは引き続き `CreditMastersEditorForm` のタブで管理する運用。
- **v1.2.0 工程 B-2 — クレジット本体構造の編集機能**: 工程 B-1 の表示専用 UI に編集機能を追加。左ペインに新規クレジット作成ダイアログ `CreditNewDialog`（OP/ED 選択 + presentation + 本放送限定フラグ + part_type + 備考）を結線し、選択中クレジットのプロパティ保存と論理削除（`is_deleted=1`）が可能になった。中央ペインの 4 階層ツリーには Card / Role / Block の追加・並べ替え・削除を実装。役職追加時には新規ピッカー `RolePickerDialog`（200ms デバウンス検索）で `roles` マスタから役職コードを選択する。並べ替えはボタン式 ↑↓ と TreeView ドラッグ＆ドロップの両方に対応し、内部ロジックは共通ヘルパ `SeqReorderHelper` に集約（DB 側は各リポジトリの `BulkUpdateSeqAsync` がトランザクション 1 本で「対象行を退避値 200 系に逃がす → 本来の値で再採番」の 2 段階更新を実行し、UNIQUE 制約 `(credit_id, card_seq)` 等との一時衝突を回避する）。CreditCardRole の並べ替えは同 tier 内のみサポート（DnD でも別 tier へのドロップは不可）。presentation=ROLL のクレジットでは「カード 1 枚固定」のルールに従いカード追加時に警告で阻止、新規作成時に同 scope/フラグ/credit_kind の UNIQUE 衝突が起きた場合（Error 1062）は分かりやすいダイアログで案内する。Entry の追加・編集・削除・並べ替え、および種別ごとの「+ 新規...」によるマスタ自動投入は工程 B-3 で扱うため、エントリ操作系のボタンは引き続き無効状態。
- **v1.2.0 工程 B-1 — クレジット本体編集 UI の骨組み**: メインメニューに「**クレジット編集...**」を新設し、3 ペイン構成の専用フォーム `CreditEditorForm`（`Forms/CreditEditorForm.cs` + `Forms/LookupCache.cs`）を追加。左ペインで「scope_kind（SERIES/EPISODE）／シリーズ／エピソード／本放送限定行も表示するか」によるクレジット絞込み選択、中央ペインで Card → Role → Block → Entry の 4 階層構造を `TreeView` で表示、右ペインで選択中エントリの種別とプレビュー文字列を表示する。エントリは種別ごとに **アイコン色＋プレフィックス文字（[PERSON] / [CHARACTER_VOICE] / [COMPANY] / [LOGO] / [SONG] / [TEXT]）** で識別され、人物・企業・キャラ・ロゴ・歌・自由テキストの曖昧さを排除する。プレビュー文字列は `LookupCache` ヘルパが各種マスタ（person_aliases / company_aliases / logos / character_aliases / song_recordings / roles）を辞書キャッシュ付きで解決して組み立てる。工程 B-1 段階では編集機能は無効化されており、構造ツリーは read-only。クレジット本体への CRUD は工程 B-2（カード／役職／ブロック／エントリの追加・並べ替え・削除を、ボタン式 ↑↓ と TreeView ドラッグ＆ドロップの両方で）と工程 B-3（エントリ編集 UI と「+ 新規...」によるマスタ自動投入機能）で順次追加される。
- **v1.2.0 工程 B' — 本放送限定フラグの導入とエピソード主題歌のコピー機能**: 本放送と Blu-ray・配信で異なるのは、実態としては「OP/ED の主題歌そのものが差し替わるケース」と「クレジット内の特定エントリ（ロゴ画像のバージョン違い等）が差し替わるケース」の 2 つに集約される。クレジット本体の役職構成までもが丸ごと差し替わるわけではないため、本放送限定フラグは以下の 2 箇所だけに持たせる設計とした：(1) `episode_theme_songs.is_broadcast_only`（PK の一部）— 同一エピソード／同一 theme_kind に対して既定 0 行（全媒体共通）と 1 行（本放送限定）を並立させて主題歌差し替えを表現、(2) `credit_block_entries.is_broadcast_only`（UNIQUE の一部）— 同一 (block_id, entry_seq) に 0 行（円盤・配信用）と 1 行（本放送用）を並立させてロゴ画像差し替え等を表現。クライアント側は再生媒体に応じて、本放送なら「同位置に 1 行があればそれ、なければ 0 行」を、円盤・配信なら「0 行のみ」を表示するロジックで解釈する。マイグレーション SQL は冪等で、過去に試作した `release_context ENUM(4)` 列や `credits.is_broadcast_only` 列の名残があれば自動で DROP してから新仕様に作り直す。エピソード主題歌タブには「**他話からコピー...**」ボタンを追加し、専用ダイアログ `EpisodeThemeSongCopyDialog` で 3 段階の操作（[1] コピー元読み込み: 全媒体共通行 / 本放送限定行をチェックボックスで選択 → [2] コピー先範囲・本放送フラグ扱いの指定 → [3] プレビュー編集）を経て、最終的な「すべて保存」を押した時点で初めて `BulkUpsertAsync` がトランザクションで動く（プレビュー段階では DB は一切触らない）。クレジット編集フォームの中央ペインのツリーでは、本放送限定エントリのプレビュー行先頭に 🎬 マークが付いて視認できる。
- **v1.2.0 工程 C — ピッカー UI と検索の高速化**: ID 直入力では使い勝手が悪い場面（声優キャスティングの person_id、エピソード主題歌の song_recording_id / label_company_alias_id、人物名義の前任・後任名義 ID と共同名義 person_id、企業屋号の前任・後任屋号 ID）に「検索...」ボタンを追加。押下するとそれぞれ専用のピッカーダイアログ（`PersonPickerDialog` / `CompanyPickerDialog` / `CharacterPickerDialog` / `SongRecordingPickerDialog` / `PersonAliasPickerDialog` / `CompanyAliasPickerDialog`）が開き、キーワード入力 → リアルタイム検索（200ms デバウンス）→ ListView から選択 → ID が編集欄に自動反映、という流れで操作できる。前任／後任名義・屋号のピッカーは選択中の親（人物 / 企業）配下に絞って検索するスコープ機能付き。歌録音ピッカーは親曲タイトル（`songs.title`）も JOIN 表示するため、`SongRecordingsRepository` に `SearchAsync` と専用 DTO `SongRecordingSearchResult` を追加した。
- クレジット本体（カード / ブロック / エントリ）の DnD 編集 UI および全面メモリ化（Draft セッション方式）は **工程 H〜H-8 で実装完了**（マスタ群の整備までを工程 A で完了。続いて検索・オートコンプリート（工程 C）、クレジット本体編集（工程 B-1〜B-3）、Tier/Group 階層化（工程 G）、SONG エントリ撤廃と DSL テンプレ（工程 H）、Entry の自由乗り換え DnD（H-8 第 1 弾）、Draft セッション方式と話数コピー（H-8 ターン群）の順で順次追加された。詳細は本変更履歴の各エントリを参照）。

#### 設計判断のポイント

- **キャラクターは全プリキュア統一**: シリーズ非依存で `character_id` を共有することで、All Stars や春映画など複数シリーズが交わる作品で同一キャラを別レコードとして扱う冗長性を避けた。
- **人物名義の多対多**: 通常は 1 alias = 1 person で十分だが、共同ペンネームのような稀ケースのためにあえて中間表 `person_alias_persons` を導入し、後から拡張する際のスキーマ変更を不要にした。
- **書式テンプレ**: シリーズ × 役職 × 期間の `series_role_format_overrides` のみで上書きを表現（カード内・エントリ単位の上書きは持たない）。シリーズ内では書式が統一されている、という前提の運用ルールを優先したシンプル設計。
- **CHECK と FK CASCADE の併用回避**: MySQL 8.0 では `ON DELETE CASCADE` / `SET NULL` の参照アクションを持つ FK 列を CHECK 制約に含められない（Error 3823）。`credits.scope_kind` ⇄ `series_id`/`episode_id` の排他、`credit_block_entries.entry_kind` ⇄ 各参照列の整合性は、いずれも `BEFORE INSERT` / `BEFORE UPDATE` トリガーで実装している（既存 `tracks` テーブルと同じパターンを踏襲）。

### v1.1.5 — CDAnalyzer のドライブ占有解消 + Blu-ray プレイリスト全走査の導入

**DB スキーマ変更なし**。`PrecureDataStars.CDAnalyzer` の SCSI 周りに対する局所修正と、`PrecureDataStars.BDAnalyzer` への Blu-ray PLAYLIST フォルダ全走査機能の追加。

#### (1) CDAnalyzer のメディア種別自動判定によるドライブ占有解消

##### 背景

`PrecureDataStars.CDAnalyzer` と `PrecureDataStars.BDAnalyzer` は同一 PC 上で同時起動して使うことが想定されている（CD / BD / DVD のいずれが投入されても対応アプリで取り込めるようにするため）。しかし、v1.1.4 までの CDAnalyzer は以下の挙動だった:

1. `WM_DEVICECHANGE` の `DBT_DEVICEARRIVAL` を受けた時点で、メディア種別を確認せずに `LoadAll()` を自動実行する。
2. `LoadAll()` は `CreateFile(\\.\X:, GENERIC_READ | GENERIC_WRITE)` でデバイスハンドルを取得し、その上で連続して MMC コマンドを発行する（`READ TOC` / `READ SUB-CHANNEL`（MCN） / `READ SUB-CHANNEL`（ISRC）×トラック数 / `READ TOC Format=0x05`（CD-Text））。
3. これらの SCSI コマンドはドライブ側でハードウェア的に直列化されるため、同時に BDAnalyzer が同一ドライブのファイルシステム経由で `VIDEO_TS.IFO` や `*.mpls` を読みに行くと、CDAnalyzer のコマンド列が完了するまで詰まったりタイムアウトしたりする。
4. しかも DVD では `READ TOC` がエラーにならず空でない応答を返すケースがあるため、CDAnalyzer は「TOC が空でないので CD として読み続行」と判定して、結局 SCSI 列を最後まで流してしまう。

つまり、DVD/BD を挿入したときに CDAnalyzer が「自分には関係ないメディアだ」と気付けず、ドライブを長時間握り続けて BDAnalyzer の足を引っ張っていた。

##### 修正内容

CDAnalyzer がデバイスハンドルを取得した直後に **MMC `GET CONFIGURATION`（CDB 0x46、RT=01b 現在のフィーチャのみ）** を発行し、レスポンスの Feature Header オフセット 6–7 から **Current Profile**（ビッグエンディアン 16bit）を読み取る。Profile 値を MMC-6 仕様の Profile List に基づいて `MediaProfile` enum（`None` / `Cd` / `Dvd` / `BluRay` / `HdDvd` / `Other`）に分類し、CD 系（0x0008–0x000A: CD-ROM / CD-R / CD-RW）以外であれば後続の SCSI コマンドを一切発行せずに `using` スコープを抜けてハンドルをクローズし、即時 return する。

具体的な分類は以下のとおり:

| 生プロファイル値 | 分類 | CDAnalyzer の挙動 |
|---|---|---|
| `0x0000` | `None`（メディア無し） | 静かに離脱（手動時のみ「メディア未挿入」案内） |
| `0x0008`–`0x000A` | `Cd`（CD-ROM / CD-R / CD-RW） | 従来どおり TOC・MCN・ISRC・CD-Text を読む |
| `0x0010`–`0x002B` | `Dvd`（DVD-ROM / DVD±R / DVD-RAM / DVD±RW / DL 等） | 即時離脱・BDAnalyzer に委ねる |
| `0x0040`–`0x0043` | `BluRay`（BD-ROM / BD-R Seq / BD-R Rand / BD-RE） | 即時離脱・BDAnalyzer に委ねる |
| `0x0050`–`0x0053` | `HdDvd`（HD DVD 系。参考扱い） | 即時離脱（実機ではほぼ遭遇しない） |
| 上記以外、または `GET CONFIGURATION` 失敗 | `Other` | 安全側に倒し従来動作（TOC 読み取り）にフォールバック |

`Other` フォールバックを設けているのは、`GET CONFIGURATION` 自体に対応していない非常に古いドライブでも従来どおり動かすため。

##### 自動トリガと手動操作の分岐（silent パラメータ）

`LoadAll(bool silent = false)` にパラメータを追加し、呼び出し元で振る舞いを切り替える:

- `WndProc` の `DBT_DEVICEARRIVAL` 経路（メディア挿入の自動検知）→ `LoadAll(silent: true)` を呼ぶ。非 CD 検知時にもメッセージボックスを出さず、画面下部のステータスラベルに「Drive X: DVD を検知したため読み取りをスキップ（BDAnalyzer 側で読み込んでください）」とだけ表示する。BDAnalyzer の操作中に余計なダイアログが割り込まない。
- `btnLoad_Click` の「読み取り」ボタン経路（手動操作）→ `LoadAll()` を既定値（`silent: false`）で呼ぶ。非 CD 検知時はダイアログで「挿入されているメディアは DVD (Profile 0x0010) です。CDAnalyzer は CD-DA 専用のため、このディスクは読み取りません。Blu-ray / DVD のチャプター情報は BDAnalyzer をご利用ください。」と案内する。
- `try` 全体を覆う catch も `silent` を尊重し、自動トリガ時の読み取りエラーはダイアログを抑止してステータスラベルにのみ反映する。

これにより、CDAnalyzer が DVD/BD のドライブにかける負荷は **`CreateFile` 1 回 + `GET CONFIGURATION` 1 回（合計でおおむね数 ms）** だけに圧縮され、その後すぐにハンドルが閉じるため、BDAnalyzer 側のファイル I/O はほぼ無干渉になる。CD を挿入したときの挙動は完全に従来どおり。

#### (2) Blu-ray のプレイリスト全走査でディスク内の全タイトルを抽出

##### 背景

v1.1.4 までの BDAnalyzer の Blu-ray 解析パスは、入力された 1 個の `.mpls` ファイルだけを `MplsParser.Parse()` でパースして表示する設計だった。`MplsParser.Parse()` 自体には「章が 1 個以下なら隣の MPLS（00000→00001）を試す → それでもダメなら同フォルダをスイープ」というフォールバックが組み込まれているが、これは「単一 MPLS の解析が失敗したときにマシな代替を 1 個探す」用途であり、複数の意味あるプレイリストを同時に並べて取り出す用途では使えない。

ドライブ自動検知も `BDMV/PLAYLIST/00000.mpls` → `00001.mpls` のピンポイント存在チェックに依存していた。

このため、以下のディスク構成で「すべての本編タイトルを取り込む」ことが事実上できていなかった:

- 複数話を独立プレイリストで収録した Blu-ray（番組 BD でよくあるパターン。00000=DISC メニュー、00001=第1話、00002=第2話、…のような並び）
- 本編 + 特典映像 + ブックレット連動映像、それぞれが独立プレイリストになっているディスク
- 00000/00001 が本編ではなく警告画面・ロゴ・メニューになっているディスク

加えて、Blu-ray ディスクには以下のようなノイズが大量に含まれることが多い:

- FBI / Interpol 警告画面（5〜15 秒）
- 配給会社ロゴアニメ（5〜10 秒）
- レーベル / スタジオロゴ（数十秒）
- メニューBGM のループプレイリスト
- anti-rip スキームによる同一内容の重複プレイリスト（最悪のケースでは 99 個並ぶ）

これらをユーザが手動で除外するのは現実的ではない。

##### 修正内容

DVD 側で v1.1.1 から動作している `IfoParser.ExtractTitlesFromVideoTs`（VIDEO_TS フォルダ全走査）と同等の仕組みを Blu-ray 側にも導入する。

`MplsParser` に以下の API を追加した:

- **`MplsTitleInfo`** クラス: フォルダ全走査時に 1 個の MPLS から得られる個別タイトル情報。`PlaylistFile`（`"00000.mpls"` 等のファイル名）、`TotalDuration`、フィルタ後の `Chapters`、`PlayItemCount`、`MarkCount` を保持する。DVD 側の `IfoParser.TitleInfo` の Blu-ray 版。
- **`BdmvScanResult`** クラス: 走査全体の結果。`Titles` と各種除外件数（`ExcludedShortCount` / `ExcludedZeroChapterCount` / `ExcludedBoundaryShortCount` / `DuplicateTitlesRemoved`）を保持する。DVD 側の `IfoParser.TitleScanResult` の Blu-ray 版。
- **`Parse(string mplsPath, bool allowFallback)`** オーバーロード: フォルダ全走査経路から `allowFallback: false` で呼ぶことで、隣接 MPLS への自動フォールバックを抑止する。既存の `Parse(string mplsPath)` 1 引数版は `allowFallback: true` で本オーバーロードに委譲する形にして、既存呼び出しの互換性を保つ。
- **`ExtractTitlesFromBdmv(string playlistFolderPath, int minPlaylistDurationSec = 60, long minChapterDurationMs = 1, long minBoundaryChapterMs = 500)`**: 指定フォルダ内の `*.mpls` を全列挙して下記 4 段フィルタを適用し、`BdmvScanResult` を返すメソッド。

フィルタ仕様:

| 段 | フィルタ名 | しきい値（既定） | 効果 |
|---|---|---|---|
| **A** | 短尺ダミー除外 | 60 秒 | プレイリスト総尺がしきい値未満を除外。FBI 警告・配給ロゴ・レーベルロゴ・短尺メニューBGM 等を弾く |
| **B** | ゼロ尺チャプター除外 | 1 ms | 章尺 0ms 級のゴミチャプターを除去 |
| **C** | 境界極短チャプター除外 | 500 ms | プレイリスト先頭・末尾の極短チャプターを剥がす（黒みフレーム自動補正） |
| **D** | 重複プレイリスト畳み込み | 完全一致 | `(総尺 ticks, マーク数)` を重複キーとし、同一キーの 2 個目以降を除外。anti-rip 99 個重複や視聴順違いの繰り返しに対処 |

フィルタ A〜C 通過後にチャプター 0 個になったプレイリストは「短尺扱い」として `ExcludedShortCount` に加算される。残ったプレイリストは `MplsTitleInfo` として返却され、各チャプターの `Start` はタイトル先頭からの相対時刻に再計算される（`video_chapters.start_time_ms` をタイトル単位の相対時刻で記録する DVD 側の運用と完全に揃える）。

##### MainForm 側のルーティング

`BDAnalyzer/MainForm.cs` の `LoadMpls(string path)` を二段階ルータに改修:

- 親フォルダが `PLAYLIST`（大文字小文字無視）であれば `LoadMplsFolderScan` を呼ぶ → フォルダ全走査モード（v1.1.5 推奨）
- それ以外は `LoadMplsSingle` を呼ぶ → 単一プレイリストモード（v1.1.4 互換動作。旧 `LoadMpls` 本体をリネーム）

`LoadMplsFolderScan` は DVD の `LoadIfoFolderScan` と同じ流儀で ListView 階層表示する:

- タイトルヘッダ行: `[00000.mpls]` 形式（薄いグレー背景）
- チャプター行: `    1`、`    2`、… の 2 段インデント
- 末尾の除外サマリ行（除外があった場合のみ、グレー文字）: `短尺 X / 0ms Y / 境界極短 Z / 重複 W`
- 既定チェック: 総尺最大タイトルとそのチャプターのみオン、他は未チェック（DVD 側と同じ流儀。本編 1 個 + 短尺特典多数の典型構成で本編以外を 1 つずつ外す手間を省く）

`lblInfo` には `00000.mpls - (Blu-ray PLAYLIST scan) Titles: N   Chapters: M   Aggregated: hh:mm:ss.ff` 形式のサマリを表示する。集約総尺は `totalOfAllTitles`（重複プレイリストはフィルタ D で既に畳まれているため単純合計が妥当）。

`TryFindDiscFile` のドライブ自動検知も拡張: 既知ファイル名のピンポイント探索 (`00000.mpls` / `00001.mpls`) から、`BDMV/PLAYLIST` 配下に `*.mpls` が 1 個でもあれば代表 1 個（ファイル名昇順の先頭）を返す方式に変更した。`LoadMpls` 側がフォルダ全走査モードに振るので、代表 1 個はあくまで「このディスクは Blu-ray である」というマーカーとして機能すれば足りる。

##### 影響範囲

- `BDMV/PLAYLIST` 配下にない単発 `.mpls`（コピー先や個別確認用）の解析は従来どおり単一プレイリストモードで動く。互換性破壊なし。
- 既存 `MplsParser.Parse(string)` を直接呼んでいたコードは、新 `Parse(string, bool)` の `allowFallback: true` への委譲で完全に従来挙動を維持する。
- `ExtractTitlesFromBdmv` のしきい値は MainForm からは既定（60 / 1 / 500）固定で呼んでいる。30 秒スポット等を取り込む必要が出たら、別途 UI からのパラメータ変更を入れる。

#### (3) 新規商品登録ダイアログの操作性改善 + 品番例・既定値の見直し

##### 背景

CDAnalyzer / BDAnalyzer の「新規商品＋ディスクとして登録」フローで使われる `NewProductDialog`（`PrecureDataStars.Catalog.Common`）は、価格欄が `NumericUpDown` で組まれており、税抜と税込が独立した手入力フィールドだった。実運用上の不満点:

1. **NumericUpDown のスピンボタンが価格入力と相性が悪い**: 数千円単位の商品価格を ↑↓ ボタンで動かして入力するユーザは存在しない。スピンボタンが常に表示されることで横幅も無駄に取られる。
2. **税込価格を手で打ち直す手間**: 税抜価格が決まれば消費税率から税込は機械的に決まるため、ユーザに 2 回入力させる必然性がない。打ち間違いの温床にもなっていた。
3. **発売元・販売元を毎回手入力**: 既定値が空のため、新規登録のたびに発売元・販売元を入力していた。

##### 修正内容

- **価格欄の TextBox 化**: `numPriceEx` (NumericUpDown) → `txtPriceEx` (TextBox)、`numPriceInc` (NumericUpDown) → `txtPriceInc` (TextBox) に置換。両者とも右寄せ表示。`txtPriceInc` は `ReadOnly = true` + 背景色 `SystemColors.ControlLight` で「触れない自動計算欄」であることを視覚化する。
- **税込価格の自動計算**: `txtPriceEx.TextChanged` および `dtReleaseDate.ValueChanged` の両方をトリガとして `RecalculateIncTax()` を呼ぶ。`int.TryParse` で税抜が読めて 0 以上なら、発売日に対応する税率を `GetConsumptionTaxRate(DateTime)` で取得し、`Math.Floor(priceEx × (1 + rate))` を税込として `txtPriceInc.Text` に書き込む。読めなければ税込側を空に戻す。端数処理は切り捨て。
- **発売日に応じた消費税率**: `GetConsumptionTaxRate(DateTime)` は日本の標準消費税率の改正履歴を反映する:
  - `~ 1989-03-31`: 0%（消費税導入前）
  - `1989-04-01 ～ 1997-03-31`: 3%
  - `1997-04-01 ～ 2014-03-31`: 5%
  - `2014-04-01 ～ 2019-09-30`: 8%
  - `2019-10-01 ～`: 10%（現行）

  軽減税率（食品・新聞）は本ダイアログの取り扱う商品ジャンルには該当しないため考慮しない。

- **発売元・販売元の既定値**: コンストラクタで `txtManufacturer.Text = "MARV"` / `txtDistributor.Text = "SMS"` を埋める。該当しないリリースに当たった場合はユーザがその場で書き換える運用。
- **OK 時のバリデーション**: 価格を TextBox から読み出すようになったため、`BtnOk_Click` で `int.TryParse` ＋ 非負チェックを行う。空欄は NULL（価格不明扱い）として登録、非数や負値は MessageBox で停止して `txtPriceEx.Focus()` で再入力に戻す。`PriceIncTax` も TextBox から取り直し（自動計算結果がそのまま入る）。

##### 影響範囲

- `Product.PriceExTax` / `Product.PriceIncTax` の型は `int?` のままで、DB スキーマ変更なし。
- 既存の動作（価格未入力時に NULL を入れる）は維持される。
- 過去のリリース（消費税 5% 当時など）を新規登録する場合も発売日を変更すれば自動で正しい税率が当たる。
- ダイアログ枚数欄は `numDiscCount` (NumericUpDown) のまま据え置き（枚数は 1〜数枚のレンジで実際にスピン操作が役立つため）。

#### (4) 商品・ディスク管理画面で 1 枚物商品のディスク詳細フォームが空になる不具合を修正

##### 背景

`ProductDiscsEditorForm`（商品・ディスク管理画面）では、商品グリッドで商品を選択するとその商品配下のディスク一覧が下段の `gridDiscs` に表示され、さらに先頭ディスクの詳細がディスク詳細フォームに自動表示される、という流れを期待していた。しかし v1.1.4 までの実装は以下の構造になっていた:

```csharp
_discs = (await _discsRepo.GetByProductCatalogNoAsync(...)).ToList();
gridDiscs.DataSource = null;
gridDiscs.DataSource = _discs;
ClearDiscForm();
```

ディスク詳細フォームへの値反映は `gridDiscs.SelectionChanged` イベントを受けた `OnDiscSelected()` に任せきりだった。ところが DataGridView の `SelectionChanged` は **新旧 DataSource の現在行 index がいずれも 0 のままだと発火しない**ことがあり、特に行数が 1 のときは確実にこの状況になる。

挙動の差:

- **2 枚以上のディスクを持つ商品**: ユーザがディスクリストの別行をクリックすれば `SelectionChanged` が発火するため、`OnDiscSelected` が呼ばれてディスク詳細フォームが埋まる。「動いて見える」のはこのため。
- **1 枚しかディスクを持たない商品**: 別行が存在しないので、ユーザが何度クリックしても `SelectionChanged` が発火する経路が無く、ディスク詳細フォームは永久に空のまま。「ディスクリストが全く反応しない」と見える。

同じ構造の DataSource 再代入が `SaveDiscAsync` / `DeleteDiscAsync` の保存・削除直後リフレッシュにもあり、これらの操作直後に 1 枚物商品を見ているケースでも同症状が出ていた。

##### 修正内容

`ProductDiscsEditorForm` にヘルパメソッド `RebindDiscGrid()` を新設し、ディスクグリッドの再バインドと先頭行の明示選択 + 詳細フォーム反映を一貫した経路に集約した:

```csharp
private void RebindDiscGrid()
{
    gridDiscs.DataSource = null;
    gridDiscs.DataSource = _discs;
    if (_discs.Count > 0)
    {
        gridDiscs.ClearSelection();
        gridDiscs.Rows[0].Selected = true;
        gridDiscs.CurrentCell = gridDiscs.Rows[0].Cells[0];
    }
    OnDiscSelected();
}
```

明示的な `ClearSelection` + `Selected = true` + `CurrentCell` 代入で先頭行を選択状態にしてから、`OnDiscSelected()` を **直接呼び出す**ことで、`SelectionChanged` の発火タイミングへの依存を排除する。`OnDiscSelected` は内部で `CurrentRow == null` の判定があるため、ディスク 0 件のときは従来どおり `ClearDiscForm` 相当に倒れる。これで行数 0 / 1 / N+ のすべてで一貫した詳細フォーム更新が保証される。

呼び出し側の修正:

- `OnProductSelectedAsync` 末尾の `gridDiscs.DataSource = null; gridDiscs.DataSource = _discs; ClearDiscForm();` の 3 行を `RebindDiscGrid();` 1 行に置き換え。
- `SaveDiscAsync` 末尾の `gridDiscs.DataSource = null; gridDiscs.DataSource = _discs;` を `RebindDiscGrid();` に置き換え。
- `DeleteDiscAsync` 内の同パターンも `RebindDiscGrid();` に置き換え。商品未選択分岐の `ClearDiscForm()` は通常経路では到達しないので保険として残す。

##### 副次効果

- 保存・削除直後にも先頭ディスクが詳細フォームに自動表示されるようになった（従来は空欄のままでユーザが行を再クリックする必要があった）。
- 複数ディスク商品でも「商品選択直後に先頭ディスクの詳細が見える」挙動に統一された（従来は `SelectionChanged` の非同期発火に依存していたため、フォームが一瞬空のまま見える状態があった）。

##### 影響範囲

- `ProductDiscsEditorForm.cs` 1 ファイルのみの局所修正。DB スキーマ変更なし。
- 商品グリッドの再選択時挙動・既存ディスクの編集・削除の動作は変わらない。先頭ディスク自動表示が追加されただけ。

#### コードレベルの変更（v1.1.5）

- `Directory.Build.props`: `Version` を `1.1.4` → `1.1.5` に更新（`AssemblyVersion` / `FileVersion` も同期）
- `PrecureDataStars.CDAnalyzer/ScsiMmci.cs`:
  - `enum MediaProfile { None, Cd, Dvd, BluRay, HdDvd, Other }` を追加
  - `static (MediaProfile profile, ushort raw) GetCurrentProfile(SafeFileHandle h)` を追加（GET CONFIGURATION を最小サイズ 8 バイトで発行し、Feature Header から Current Profile を抽出）
  - `static MediaProfile ClassifyProfile(ushort raw)` を追加（生プロファイル値から enum への分類ヘルパ。switch 式の範囲パターン使用）
  - SCSI コマンド失敗時や例外発生時は `(Other, 0)` で返却し、呼び出し側のフォールバックを許容する
- `PrecureDataStars.CDAnalyzer/MainForm.cs`:
  - `LoadAll()` のシグネチャを `LoadAll(bool silent = false)` に拡張
  - `OpenCdDevice` 直後・TOC 読み取り前に `GetCurrentProfile(h)` を呼び、`switch` 式でプロファイルごとに早期 return / フォールスルーを分岐
  - 非 CD 検知時の UI 状態リセット（gridTracks/gridAlbum クリア、_lastRead = null、ステータスラベル更新）と、`silent=false` 時のみ MessageBox 案内
  - 既存の TOC 失敗時警告および try/catch のエラーダイアログも `silent` を尊重して抑止する
  - `WndProc` の `DBT_DEVICEARRIVAL` 経路で `LoadAll(silent: true)` を呼ぶよう変更
- `PrecureDataStars.BDAnalyzer/MplsParser.cs`:
  - `MplsTitleInfo` クラスを追加（PlaylistFile / TotalDuration / Chapters / PlayItemCount / MarkCount）
  - `BdmvScanResult` クラスを追加（Titles と 4 種の除外件数カウンタ）
  - `Parse(string mplsPath, bool allowFallback)` オーバーロードを追加し、既存の 1 引数版は `allowFallback: true` への委譲に変更（呼び出し側互換維持）
  - `ExtractTitlesFromBdmv(string playlistFolderPath, int minPlaylistDurationSec = 60, long minChapterDurationMs = 1, long minBoundaryChapterMs = 500)` を追加（フィルタ A〜D 適用、重複キー `(総尺ticks, マーク数)` の `HashSet` で重複畳み込み）
- `PrecureDataStars.BDAnalyzer/MainForm.cs`:
  - 旧 `LoadMpls(string path)` の本体を `LoadMplsSingle(string path)` にリネームして単一プレイリストモードとして温存
  - `LoadMpls(string path)` を二段階ルータに刷新: 親フォルダが `PLAYLIST` なら `LoadMplsFolderScan`、それ以外なら `LoadMplsSingle`
  - `LoadMplsFolderScan(string representativeMplsPath)` を新設（DVD の `LoadIfoFolderScan` と同じ階層表示・既定チェック方針・除外サマリ行・VideoChapter 紐付け処理）
  - `TryFindDiscFile` の Blu-ray 検索を `BDMV/PLAYLIST` フォルダ内 `*.mpls` 全列挙＋ファイル名昇順先頭採用方式に拡張（既知ファイル名 `00000.mpls` / `00001.mpls` 限定のピンポイント探索を撤回）
- `PrecureDataStars.Catalog.Common/Dialogs/NewProductDialog.Designer.cs`:
  - 価格欄の宣言を `numPriceEx` / `numPriceInc` (NumericUpDown) から `txtPriceEx` / `txtPriceInc` (TextBox) に置き換え
  - `txtPriceEx.TextAlign` / `txtPriceInc.TextAlign` を `HorizontalAlignment.Right` に設定
  - `txtPriceInc.ReadOnly = true` + `BackColor = SystemColors.ControlLight` で自動計算結果の表示専用化
  - `Controls.AddRange` の参照名と Numeric の Maximum/Minimum 設定を更新
- `PrecureDataStars.Catalog.Common/Dialogs/NewProductDialog.cs`:
  - コンストラクタで `txtManufacturer.Text = "MARV"` / `txtDistributor.Text = "SMS"` を既定値として埋める
  - `txtPriceEx.TextChanged` および `dtReleaseDate.ValueChanged` に `RecalculateIncTax()` を結線
  - `RecalculateIncTax()` を新設: `int.TryParse` で税抜を取り、`GetConsumptionTaxRate(発売日)` で税率を取得、`Math.Floor(priceEx × (1 + rate))` を税込として表示
  - `GetConsumptionTaxRate(DateTime)` を新設: 1989-04-01（3%）／1997-04-01（5%）／2014-04-01（8%）／2019-10-01（10%）の 4 段階の境界で日本の標準消費税率を返す。それより前は 0%
  - `BtnOk_Click` を TextBox 入力ベースに改修: 税抜は空 → NULL、非数 / 負値はバリデーションで停止して `txtPriceEx.Focus()`、税込は自動計算結果を取り直し
- `PrecureDataStars.Catalog/Forms/ProductDiscsEditorForm.cs`:
  - `RebindDiscGrid()` ヘルパを新設（DataSource 再代入 + 先頭行の明示選択 + `OnDiscSelected()` 直接呼び出しを集約）
  - `OnProductSelectedAsync` / `SaveDiscAsync` / `DeleteDiscAsync` の DataSource 再代入ブロック計 3 箇所を `RebindDiscGrid()` 呼び出しに置換
  - `DataGridView.SelectionChanged` の発火タイミング（新旧 DataSource の現在行 index がいずれも 0 だと発火しない場合がある仕様）への依存を解消し、ディスク 1 枚物の商品で詳細フォームが空のままになる事象を修正
- `README.md`: 先頭バナーに v1.1.5 の Blu-ray 全走査機能および NewProductDialog 改善を追記、プロジェクト詳細表の BDAnalyzer / CDAnalyzer 説明を更新、「音楽カタログ登録 → B. BD/DVD の登録」セクションの Blu-ray 行を全走査モードに対応する記述に書き換え + 「Blu-ray PLAYLIST フォルダ全走査の仕様」サブセクションを新設、本変更履歴エントリを (1) (2) (3) (4) のサブセクション構造に再編して Blu-ray 全走査機能・NewProductDialog 改善・1 枚物商品ディスク詳細フォーム不具合修正の詳細を追記

### v1.1.4 — 商品・ディスク管理と既存商品への追加ディスク登録の挙動改善

**DB スキーマ変更なし**。Catalog GUI と CDAnalyzer / BDAnalyzer から呼ばれる共通サービス層のバグ修正および UX 改善のみ。

#### (1) 商品・ディスク管理画面でディスクを保存しても物理情報を消さない

- v1.1.3 までは、`ProductDiscsEditorForm` でディスクタイトル等を保存すると `DiscsRepository.UpsertAsync` が `INSERT ... ON DUPLICATE KEY UPDATE` を発行し、フォームが値を保持していない物理系列（`total_length_frames` / `total_length_ms` / `num_chapters` / CD-Text 系 8 列 / `cddb_disc_id` / `musicbrainz_disc_id` / `last_read_at`）を NULL で上書きしていた。CDAnalyzer / BDAnalyzer で取り込んだディスク総尺・チャプター数がタイトル編集の副作用で消える状態。
- v1.1.4 では `SaveDiscAsync` が UPSERT 直前に `DiscsRepository.GetByCatalogNoAsync` で既存レコードを引き直し、フォーム編集対象外のフィールド（上記 13 列）を既存値からコピーして UPSERT する。タイトル系・組内番号・ディスク種別・メディアフォーマット・MCN・総トラック数・ボリュームラベル・備考といったメタ情報は従来どおりフォーム値で更新される。
- 既存レコードの `CreatedBy` も保全する（初出ユーザー名を奪わない）。`UpdatedBy` だけを今回の操作者で上書きする運用。
- 既に被害を受けたレコードは、CDAnalyzer / BDAnalyzer から該当ディスクを読み直して「選択したディスクに反映」（`SyncPhysicalInfoAsync`）を選ぶことで、Catalog 側の磨き込み情報を保ちつつ物理情報のみ復旧できる。

#### (2) 既存商品への追加ディスク登録で、既存品番を入れても上書きされないように

- `DiscRegistrationService.AttachDiscToExistingProductAsync` は内部で `DiscsRepository.UpsertAsync` を呼んでおり、新ディスクとして指定した `catalog_no` が他商品下も含めて DB 上に既存していた場合、その既存ディスクを新ディスクの内容で上書きしてしまう挙動だった。
- v1.1.4 では `AttachDiscToExistingProductAsync` の引数バリデーション直後に `DiscsRepository.GetByCatalogNoAsync` で重複検証を行い、ヒットしたら `InvalidOperationException("品番 [XXX] は既に登録されています。別の品番を指定してください。")` を送出。後続の DB 更新には進まない。CDAnalyzer / BDAnalyzer 側はこの例外を `ShowError` で MessageBox に出すため、ユーザーには「重複していたので登録されなかった」ことが明確に伝わる。
- 論理削除済み (`is_deleted = 1`) の品番もヒット扱いとする。論理削除済み品番の再利用は明示的な削除取消フローを想定しており、本フローからは禁じる方針。

#### (3) DiscMatchDialog の候補が 1 件のとき自動選択する

- `DiscMatchDialog` の自動照合候補グリッド (`gridCandidates`) と手動検索結果グリッド (`gridSearch`) の両方で、行が 1 件以上ある場合は先頭行を自動選択するよう `BindGrid` を修正。MCN 完全一致や品番ピンポイント検索で 1 件しかヒットしないケースで、ユーザが行をクリックせずにそのまま「選択したディスクに反映」「商品に追加」を押せるようになる。
- 自動選択は `grid.ClearSelection()` → `grid.Rows[0].Selected = true;` → `grid.CurrentCell = grid.Rows[0].Cells[0];` の 3 段で行い、DataGridView のバインド直後の不安定な選択状態を確定させる。
- コンストラクタ末尾で `UpdateAttachButtonEnabled()` を 1 回明示呼び出し、初期候補の自動選択を「商品に追加」ボタンの Enabled 状態にも反映する（`SelectionChanged` ハンドラのワイヤより前に `BindGrid` が走るため、自動選択時のイベントが拾われないのを補う）。
- 「選択したディスクに反映」「商品に追加」両ボタンの未選択ガードはそのまま維持されており、空グリッドや何らかの理由で選択が外れた状態でボタンを押した場合は、従来どおり MessageBox で警告して DB 操作には進まない。

#### (4) ディスク・トラック閲覧画面の上下ペインを半々で自動追従

- `DiscBrowserForm` の上下分割（上=ディスク一覧 / 下=トラック一覧）はこれまで Designer 上 `SplitterDistance = 320` の固定値で初期化されており、初期高さ 700 のうち上が約 46% / 下が約 54% と若干偏っていた。さらにウインドウを縦方向に拡大した際は WinForms の SplitContainer 既定挙動により下ペイン側が大きく伸びてバランスが崩れていた。
- v1.1.4 では、`splitMain.SizeChanged` イベントで都度 `(splitMain.Height - splitMain.SplitterWidth) / 2` を SplitterDistance に書き戻す `RecenterSplitter()` メソッドを追加。常にディスク一覧とトラック一覧が縦方向に半々で表示され、ウインドウサイズ変更にも自動追従する。
- ユーザがバーを手動でドラッグすることは引き続き可能だが、次のリサイズで自動的に半々に戻る挙動とした（要望どおり）。極端に縦方向を縮めた場合（高さがスプリッタ幅以下）は SplitterDistance の代入で例外が発生しないよう、利用可能領域とパネル最小サイズで明示的にクランプしてから設定する。

#### (5) 商品・ディスク管理画面のレイアウト刷新

- v1.1.3 の構造（左=商品一覧 / 右上=商品詳細 / 右下=所属ディスク一覧 + ディスク詳細）では、商品詳細とディスク詳細のフィールド幅が 240〜260 px 程度しか確保できず、エディタ領域が窮屈という指摘を受けたため、レイアウトを全面的に刷新した。
- 新構造は上下 2 段:
  - **上段（商品エリア）**: `splitProduct`（新設）で左右 60:40 に分割。左 60% に商品一覧 (`gridProducts`)、右 40% に商品詳細エディタ (`pnlProductDetail`)。
  - **下段（ディスクエリア、高さ 400 px 固定）**: `splitDisc` で左右 60:40 に分割。左 60% に所属ディスク一覧 (`gridDiscs`)、右 40% にディスク詳細エディタ (`pnlDiscDetail`)。
- 下段の固定高さ 400 px の根拠: 「所属ディスク 10 行表示 ≈ 264 px」と「ディスク詳細エディタ全フィールド表示 ≈ 366 px」のうち大きい方 + 余裕。`splitMain.FixedPanel = FixedPanel.Panel2` で固定し、上段の縦サイズだけがウインドウ拡縮に追従する。
- 60:40 の比率は `splitProduct.SizeChanged` / `splitDisc.SizeChanged` イベントで都度 `(Width × 0.6)` を SplitterDistance に書き戻す `Apply60To40()` で常時維持。
- 旧 `splitRight`（右上＝商品詳細 / 右下＝ディスク群の上下分割）は廃止された。

**ウインドウサイズと位置（v1.1.4 改）**

- Form の `ClientSize` は **1200×820**。1366×768 のノート PC でも縦は窮屈ながらはみ出さないサイズ（v1.1.4 初版で 1400×950 にした際、1280×800 程度のディスプレイでフォーム右端と下端がデスクトップ外に飛び出すという指摘があったため縮小）。
- `StartPosition` は `CenterScreen`。親フォーム（メインフォーム）の位置・サイズに依存せず常にディスプレイ中央に出る。
- 商品エディタの全フィールドは縦 484 px 必要だが、上段に確保できるのは `820 - 40(検索バー) - 6(splitter) - 400(下段) = 374 px` のため、`pnlProductDetail.AutoScroll = true` で縦スクロールを許容する設計。

**詳細パネルのレイアウト（v1.1.4 改）**

- ラベル左端 x=18、入力欄左端 x=22+labelW で配置し、パネル左端から **10 px 程度の視覚的余白**を確保（閲覧 UI の `pnlBody.Padding` と同程度のゆとり）。
- **入力欄／ボタン群はいずれも Anchor を付けず（Top|Left デフォルト）**、`LayoutProductDetailPanel()` / `LayoutDiscDetailPanel()` メソッドで動的に Width と Location を再計算する方式に統一した。これは AutoScroll=true の Panel に Anchor=Right のコントロールを置いた際に WinForms 内部で起きるレイアウト循環バグ（`DisplayRectangle.Right` を参照する Anchor=Right コントロールと AutoScrollMinSize 計算が相互再計算でフォームが右に伸び続ける現象）を確実に回避するための措置。
- `LayoutProductDetailPanel()` の動作:
  - パネル右端から「ボタン幅 80 + 余白 10 px」の位置にボタン列 X 座標を計算
  - 入力欄の右端は「ボタン左端 - 16 px」とし、ラベル左端 22+labelW から逆算して `fieldW = fieldEndX - fieldX` を都度計算
  - 通常入力欄（タイトル系・コンボ系・テキスト系）はすべて同じ Width で配置、Y 座標は Designer で確定済みの値をそのまま使用
  - 税込価格行は特例: `numPriceInc` は固定幅 170 px、`btnAutoTax` はその直後 6 px 余白を空けて配置
  - 最小幅 100 px を保証してゼロ以下にならないようクランプ
- ヘルパは Form.Load 時と各詳細パネルの SizeChanged 時に呼ばれる。Form のリサイズに対しても入力欄・ボタンの位置と幅が即座に追従する。

#### (6) マスタ管理画面の改善

`MastersEditorForm`（メニュー「マスタ管理」）について、以下の点を改善した。

**外周余白の確保（キワキワ問題の解消）**

- `BuildTab` ヘルパの入力欄パネル `Padding` を 8 → 18 に増やし、各コントロール座標も +10 シフトしてパネル左端からの視覚余白（≈ 10 px）を確保。`TabPage` 自体にも `Padding(8)` を入れてグリッドと入力欄パネルがタブ枠に密着しないように。`BuildBgmSessionsTab` も同様の余白調整。
- `ClientSize` を 900×560 → **1000×680** に拡大（ボタン列が 4 つに増えたぶんの縦サイズ追加 + 外周余白）。
- `StartPosition` を `CenterParent` → `CenterScreen` に変更。

**「新規」ボタンの追加（新規追加と既存編集の操作を明確化）**

- 6 つのマスタタブ（商品種別 / ディスク種別 / トラック内容 / 曲・音楽種別 / 曲・サイズ種別 / 曲・パート種別）に「新規」ボタンを追加し、4 ボタン構成（新規 / 保存・更新 / 選択行を削除 / 並べ替えを反映）に統一した。「新規」ボタンはフォーム入力欄をすべてクリアし、グリッド選択を解除する `ClearMasterForm` ヘルパを呼ぶ。
- 従来は「保存 / 更新」ボタン 1 つでコードに基づいて新規／既存を自動判別する設計だったため、既存行の編集中にコードを書き換えてしまうと意図せず別レコードとして INSERT される事故が起きやすかった。新規ボタン経由で明示的にフォームをクリアする操作フローを設けることで、この事故を防げる。
- 既存の `bgm_sessions` タブは元から「新規追加」「保存 / 更新」「選択行を削除」の 3 ボタン構成（PK が自動採番のため新規は明示的）で分かりやすかったため、今回は変更せず維持。

**`display_order` のドラッグ&ドロップ並べ替え**

- 6 つのマスタタブで DataGridView の行をマウスドラッグで上下に並べ替えできるよう実装。`EnableRowDrag` 共通ヘルパが `MouseDown` / `MouseMove` / `DragOver` / `DragDrop` を結線する。`SystemInformation.DragSize` を超える移動でドラッグ開始判定し、通常のクリック選択操作と誤動作しないように。
- ドラッグだけでは DB は変わらず、`DataSource` の `IList` 内で要素を入れ替えるだけ。実際の `display_order` 反映は新設の「並べ替えを反映」ボタンから、確認ダイアログを経て一斉 UPSERT で実行する。
- 反映時は List の先頭から `display_order = 1, 2, 3, ...` を割り当てて各行の `UpsertAsync` を順次呼ぶ。Repository の SQL が `INSERT ... ON DUPLICATE KEY UPDATE` の `UPDATE` 部分で `created_by` を含めないため、既存行の `CreatedBy` は DB レベルで保全される。
- ドラッグ前提として `LoadAllAsync` および「並べ替えを反映」後の再読み込みで `(await Repo.GetAllAsync()).ToList()` を `DataSource` にバインドするよう変更（旧実装は `IEnumerable<T>` のまま渡しており、要素入れ替えの操作に対応していなかった）。
- `bgm_sessions` タブは `session_no` が表示順を兼ねるため、ドラッグ並べ替えと「並べ替えを反映」ボタンの対象外。

**監査列の自動非表示（CreatedBy / UpdatedBy / CreatedAt / UpdatedAt）**

- 全 7 グリッドで `DataBindingComplete` イベント時に `CreatedAt` / `UpdatedAt` / `CreatedBy` / `UpdatedBy` 列を Visible = false に設定する `HideAuditColumns` 共通ヘルパを追加し、コンストラクタで全グリッドに結線。マスタ実運用で参照する必要のないノイズ列を画面から排除した。
- 従来は `bgm_sessions` タブだけが個別に同等の処理を行っていたが、共通ヘルパに統一。

#### コードレベルの変更（v1.1.4）

- `Directory.Build.props`: `Version` を `1.1.3` → `1.1.4` に更新（`AssemblyVersion` / `FileVersion` も同期）
- `PrecureDataStars.Catalog/Forms/ProductDiscsEditorForm.cs`: `SaveDiscAsync` で UPSERT 前に既存レコードを取得し、フォーム編集対象外のフィールド 13 列と `CreatedBy` を保全。コンストラクタ末尾にレイアウト追従ロジック（`InitializeLayout` / `Apply60To40` / `LayoutProductDetailPanel` / `LayoutDiscDetailPanel`）を追加
- `PrecureDataStars.Catalog/Forms/ProductDiscsEditorForm.Designer.cs`: 上下 2 段レイアウトに刷新（`splitMain` を上下分割化、`splitRight` を廃止して `splitProduct` を新設）。Anchor 設定を全廃し、AddRow ヘルパのオフセットを +10 して左側余白を確保。`ClientSize` を `1200×820`、`StartPosition` を `CenterScreen` に変更
- `PrecureDataStars.Catalog/Forms/DiscBrowserForm.cs`: 上下ペインを常に半々で維持する `RecenterSplitter` メソッドと `splitMain.SizeChanged` ハンドラを追加
- `PrecureDataStars.Catalog/Forms/DiscBrowserForm.Designer.cs`: `splitMain.SplitterDistance` の Designer 初期値を SplitContainer のデフォルトサイズ制約に触れない安全な小さい値（100）に変更
- `PrecureDataStars.Catalog/Forms/MastersEditorForm.cs`: コンストラクタで全グリッドに `HideAuditColumns` と `EnableRowDrag` を結線。`LoadAllAsync` および各 UPSERT 後の再読み込みで `(await Repo.GetAllAsync()).ToList()` をバインドするよう変更（行ドラッグの IList 要件のため）。「新規」ボタンハンドラ × 6（`ClearMasterForm` を呼ぶ）と「並べ替えを反映」ボタンハンドラ × 6（`ApplyDisplayOrderAsync<T>` を呼ぶ）を追加。共通ヘルパとして `HideAuditColumns` / `ClearMasterForm` / `EnableRowDrag` / `ApplyDisplayOrderAsync<T>` を新設
- `PrecureDataStars.Catalog/Forms/MastersEditorForm.Designer.cs`: 6 つのマスタタブのフィールドに「新規」「並べ替えを反映」ボタンを追加（4 ボタン構成）。`BuildTab` の引数を 4 ボタン受け取りに拡張し、入力欄パネルの `Padding` を 8 → 18 に増、各コントロール座標を +10 シフト、`TabPage.Padding = 8` を追加して外周余白を確保。`BuildBgmSessionsTab` も同様の余白調整。`ClientSize` を 900×560 → 1000×680、`StartPosition` を `CenterParent` → `CenterScreen` に変更
- `PrecureDataStars.Catalog.Common/Services/DiscRegistrationService.cs`: `AttachDiscToExistingProductAsync` 冒頭に新ディスクの品番重複検証を追加
- `PrecureDataStars.Catalog.Common/Dialogs/DiscMatchDialog.cs`: `BindGrid` で先頭行を自動選択する処理を追加（行が 1 件以上ある場合のみ。`ClearSelection` → 先頭行 `Selected = true` → `CurrentCell` を先頭行の先頭セルへ、の 3 段操作で確定）。コンストラクタ末尾で `UpdateAttachButtonEnabled()` を 1 回明示呼び出し、`SelectionChanged` ハンドラのワイヤ前に走った初期 `BindGrid` の自動選択結果を「商品に追加」ボタンの Enabled 状態に反映する

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