-- =====================================================================
-- v1.9.0_add_books.sql
--
-- 書籍（紙 / Kindle）を扱うための 6 テーブル新設。
-- 音楽商品（products / discs）とは独立した系統として持つ。書籍には品番に相当する
-- 自然キーが無い（ISBN は紙のみで Kindle 版には無い）ため、books は代理キー book_id を PK とする。
--
-- 1) book_genres        ジャンルマスタ（設定資料集 / ムック / 絵本 …）
-- 2) book_credit_roles  書籍役職マスタ（著 / 監修 / イラスト / 編集 …）
--                       アニメクレジットの roles とは別マスタにする。roles に混ぜると
--                       /creators/roles/ 側の集計に書籍役職が混入するため。
-- 3) books              書籍本体。紙・Kindle の ASIN / 表紙 URL / 価格を 2 系統で持つ
-- 4) book_series        書籍 ↔ シリーズ の多対多（オールスターズ本・合同本に対応）
-- 5) book_genre_links   書籍 ↔ ジャンル の多対多（「ムック かつ 設定資料集」がある）
-- 6) book_credits       書籍のクレジット。person_aliases への紐付けとフリーテキストの併用可
--
-- 表紙画像は products と同じ運用：実体は保存せず Amazon CDN の URL のみ保持（ホットリンク）。
-- cover_image_source は 'amazon_print' / 'amazon_kindle' / NULL（未選択）。
--
-- 冪等性: すべて CREATE TABLE IF NOT EXISTS + INSERT IGNORE。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 1) 書籍ジャンルマスタ
--    product_kinds と同じ流儀（コード PK + 和名 / 英名 / 表示順）。
--    display_order は UNIQUE にして並びの重複を防ぐ。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `book_genres` (
  `genre_code`    varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja`       varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`       varchar(64) DEFAULT NULL,
  `display_order` int NOT NULL,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`genre_code`),
  UNIQUE KEY `uq_book_genres_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍ジャンルマスタ';

INSERT IGNORE INTO `book_genres` (`genre_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('SETTING_BOOK',     '設定資料集',        'Setting Material Book',  1),
  ('FAN_BOOK',         'ファンブック',      'Fan Book',               2),
  ('MOOK',             'ムック',            'Mook',                   3),
  ('ART_BOOK',         '画集・イラスト集',  'Art Book',               4),
  ('PICTURE_BOOK',     '絵本',              'Picture Book',           5),
  ('COMIC',            'コミカライズ',      'Comic',                  6),
  ('NOVEL',            'ノベライズ',        'Novel',                  7),
  ('SCORE',            '楽譜・スコア',      'Musical Score',          8),
  ('MAGAZINE_SPECIAL', '雑誌増刊・別冊',    'Magazine Special',       9),
  ('GUIDE',            'ガイドブック',      'Guide Book',            10),
  ('ACTIVITY',         'ぬりえ・シール・知育', 'Activity Book',      11),
  ('OTHER',            'その他',            'Other',                 99);

-- ---------------------------------------------------------------------
-- 2) 書籍役職マスタ
--    amazon_role_type は Creators API の contributors[].roleType（"author" / "editor" 等）
--    からの自動マッピング用。表示名 role（"著" / "編集"）より安定しているためこちらで引く。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `book_credit_roles` (
  `role_code`        varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja`          varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`          varchar(64) DEFAULT NULL,
  `amazon_role_type` varchar(64) DEFAULT NULL COMMENT 'Creators API contributors[].roleType の対応値',
  `display_order`    int NOT NULL,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`role_code`),
  UNIQUE KEY `uq_book_credit_roles_display_order` (`display_order`),
  KEY `ix_book_credit_roles_amazon_role_type` (`amazon_role_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍役職マスタ（アニメクレジットの roles とは別系統）';

INSERT IGNORE INTO `book_credit_roles` (`role_code`,`name_ja`,`name_en`,`amazon_role_type`,`display_order`) VALUES
  ('AUTHOR',       '著',            'Author',       'author',       1),
  ('SUPERVISOR',   '監修',          'Supervisor',   'supervisor',   2),
  ('EDITOR',       '編集',          'Editor',       'editor',       3),
  ('WRITER',       '構成・執筆',    'Writer',       'writer',       4),
  ('ILLUSTRATOR',  'イラスト',      'Illustrator',  'illustrator',  5),
  ('COVER_ILLUST', '表紙イラスト',  'Cover Illustrator', NULL,      6),
  ('DESIGNER',     'デザイン',      'Designer',     'designer',     7),
  ('PHOTOGRAPHER', '撮影',          'Photographer', 'photographer', 8),
  ('TRANSLATOR',   '翻訳',          'Translator',   'translator',   9),
  ('PLANNER',      '企画',          'Planner',      NULL,          10),
  ('OTHER',        'その他',        'Other',        NULL,          99);

-- ---------------------------------------------------------------------
-- 3) 書籍本体
--    release_date は代表発売日（紙があれば紙の発売日、電子のみの本は配信日）。NOT NULL。
--    release_date_kindle は Kindle 版が後日配信のときだけ入れる。
--    isbn13 は紙のみが持つ。Amazon からは externalIds.eans（13 桁）を採る
--    （isbns は ISBN-10 で紙の ASIN と同値になることが多く、13 桁の器には合わない）。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `books` (
  `book_id`                      int NOT NULL AUTO_INCREMENT,
  `title`                        varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_kana`                   varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en`                     varchar(255) DEFAULT NULL,
  -- 出版社。音楽商品の発売元／販売元と同じ product_companies マスタを流用する。
  `publisher_product_company_id` int DEFAULT NULL,
  `release_date`                 date NOT NULL COMMENT '代表発売日（紙があれば紙、電子のみなら配信日）',
  `release_date_kindle`          date DEFAULT NULL COMMENT 'Kindle 版が後日配信のときの配信日',
  `isbn13`                       char(13) DEFAULT NULL COMMENT '紙のみ。Amazon externalIds.eans 由来',
  `page_count`                   smallint unsigned DEFAULT NULL,
  -- Amazon classifications.binding の生値（"ムック" / "大型本" / "単行本（ソフトカバー）" 等）。
  -- 取り込みの受け皿で、人手で整えた判型は trim_size に入れる。
  `binding_text`                 varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `trim_size`                    varchar(32) DEFAULT NULL COMMENT '判型（A4 / B5 / 新書判 等）',
  `price_ex_tax`                 int DEFAULT NULL,
  `price_inc_tax`                int DEFAULT NULL,
  `price_kindle_inc_tax`         int DEFAULT NULL,
  -- 版の存在フラグ。ASIN 未取得でも「電子版は出ていない」は事実として持てるので独立させる。
  `has_print`                    tinyint(1) NOT NULL DEFAULT '1',
  `has_kindle`                   tinyint(1) NOT NULL DEFAULT '0',
  -- Amazon ASIN は紙と Kindle で別 ID が振られるため 2 列で持ち、詳細ページで並列リンクする。
  `amazon_asin_print`            varchar(16) DEFAULT NULL,
  `amazon_asin_kindle`           varchar(16) DEFAULT NULL,
  -- 表紙画像キャッシュ。実体は保存せず Amazon CDN URL のみ保持（ホットリンク運用）。
  --   cover_image_source ... 表示採用ソース。'amazon_print' / 'amazon_kindle' / NULL（未選択）。既定は Kindle 優先
  --   cover_image_show_both ... 詳細ページで紙・Kindle 両方を並べるか（1=両方 / 0=代表 1 枚）
  `cover_image_url_print`        varchar(512) DEFAULT NULL,
  `cover_image_url_kindle`       varchar(512) DEFAULT NULL,
  `cover_image_source`           varchar(16) DEFAULT NULL,
  `cover_image_show_both`        tinyint(1) NOT NULL DEFAULT '0',
  `cover_image_fetched_at`       datetime DEFAULT NULL,
  -- 外部リンク：詳細ページ末尾の「外部リンク」セクションに公式ページとして出す。
  `official_url`                 varchar(1024) DEFAULT NULL,
  `notes`                        text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                   varchar(64) DEFAULT NULL,
  `updated_by`                   varchar(64) DEFAULT NULL,
  `is_deleted`                   tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`book_id`),
  UNIQUE KEY `uq_books_isbn13` (`isbn13`),
  KEY `ix_books_release`   (`release_date`),
  KEY `ix_books_publisher` (`publisher_product_company_id`),
  KEY `ix_books_title`     (`title`),
  CONSTRAINT `fk_books_publisher_pc` FOREIGN KEY (`publisher_product_company_id`)
    REFERENCES `product_companies` (`product_company_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_books_has_any_edition`     CHECK ((`has_print` = 1) OR (`has_kindle` = 1)),
  CONSTRAINT `ck_books_price_ex_nonneg`     CHECK ((`price_ex_tax` IS NULL) OR (`price_ex_tax` >= 0)),
  CONSTRAINT `ck_books_price_inc_nonneg`    CHECK ((`price_inc_tax` IS NULL) OR (`price_inc_tax` >= 0)),
  CONSTRAINT `ck_books_price_kindle_nonneg` CHECK ((`price_kindle_inc_tax` IS NULL) OR (`price_kindle_inc_tax` >= 0))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍（紙 / Kindle）';

-- ---------------------------------------------------------------------
-- 4) 書籍 ↔ シリーズ（多対多）
--    1 冊が複数シリーズにまたがる合同本のため多対多。
--    行が 1 件も無い書籍はオールスターズ／シリーズ横断として扱う（discs.series_id IS NULL と同義）。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `book_series` (
  `book_id`       int NOT NULL,
  `series_id`     int NOT NULL,
  `display_order` int NOT NULL DEFAULT '1',
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`book_id`,`series_id`),
  KEY `ix_book_series_series` (`series_id`),
  CONSTRAINT `fk_book_series_book`   FOREIGN KEY (`book_id`)   REFERENCES `books` (`book_id`)     ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_book_series_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍 ↔ シリーズ（合同本対応の多対多）';

-- ---------------------------------------------------------------------
-- 5) 書籍 ↔ ジャンル（多対多）
--    is_primary は索引ページで代表として出すジャンル。1 冊につき最大 1 行という排他性は
--    アプリ側（BooksRepository のトランザクション）で担保する。product_companies の
--    is_default_label / is_default_distributor と同じ流儀。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `book_genre_links` (
  `book_id`    int NOT NULL,
  `genre_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `is_primary` tinyint(1) NOT NULL DEFAULT '0',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`book_id`,`genre_code`),
  KEY `ix_book_genre_links_genre` (`genre_code`),
  CONSTRAINT `fk_book_genre_links_book`  FOREIGN KEY (`book_id`)    REFERENCES `books` (`book_id`)            ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_book_genre_links_genre` FOREIGN KEY (`genre_code`) REFERENCES `book_genres` (`genre_code`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍 ↔ ジャンル（多対多）';

-- ---------------------------------------------------------------------
-- 6) 書籍クレジット
--    person_alias_id（マスタ紐付け）と credit_text（フリーテキスト）の併用可。
--    どちらか一方は必ず入る。両方入っている場合は「マスタに紐付いているが、
--    誌面の表記が別」を意味し、表示は credit_text を優先してリンク先を alias に取る。
--    サイト表示では alias 紐付け有り = <a>（hover 下線）、テキストのみ = <span>（下線なし）。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `book_credits` (
  `book_credit_id`     int NOT NULL AUTO_INCREMENT,
  `book_id`            int NOT NULL,
  `role_code`          varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `person_alias_id`    int DEFAULT NULL,
  `credit_text`        varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `display_order`      int NOT NULL DEFAULT '1',
  `amazon_source_role` varchar(64) DEFAULT NULL COMMENT '取り込み由来の追跡用。Creators API の role / roleType 生値',
  `notes`              text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`         varchar(64) DEFAULT NULL,
  `updated_by`         varchar(64) DEFAULT NULL,
  `is_deleted`         tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`book_credit_id`),
  KEY `ix_book_credits_book`  (`book_id`,`display_order`),
  KEY `ix_book_credits_role`  (`role_code`),
  KEY `ix_book_credits_alias` (`person_alias_id`),
  CONSTRAINT `fk_book_credits_book`  FOREIGN KEY (`book_id`)         REFERENCES `books` (`book_id`)                    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_book_credits_role`  FOREIGN KEY (`role_code`)       REFERENCES `book_credit_roles` (`role_code`)      ON UPDATE CASCADE,
  -- 参照アクション（ON DELETE SET NULL / ON UPDATE CASCADE）は付けない。付けると
  -- person_alias_id が CHECK 制約に使えなくなる（MySQL 8 の制限：参照アクションで
  -- 書き換わる列は CHECK に含められない）。alias_id は不変の AUTO_INCREMENT PK で、
  -- 名義の削除も is_deleted による論理削除運用のため、既定の RESTRICT で問題ない。
  CONSTRAINT `fk_book_credits_alias` FOREIGN KEY (`person_alias_id`) REFERENCES `person_aliases` (`alias_id`),
  CONSTRAINT `ck_book_credits_alias_or_text` CHECK ((`person_alias_id` IS NOT NULL) OR (`credit_text` IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='書籍クレジット（マスタ紐付け + フリーテキスト併用）';

COMMIT;
