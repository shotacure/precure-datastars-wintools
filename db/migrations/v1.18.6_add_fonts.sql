-- =====================================================================
-- v1.18.6_add_fonts.sql
--
-- 作品で使われた書体の表を新設し、書体の情報をすべてここから引くように作り替える。
--
--   fonts               書体のマスタ。1 行 = 放送で使われた書体 1 つ（「ファン蘭 B」のように書体名＋ウェイト）。
--                       見本・テロップ画像を描く手元の書体（render_font_name）を別に持ち、放送の書体が手元に無いときは
--                       別のウェイトや改刻版で描ける（render_family_name・render_weight が放送の書体と違えば、サイトに
--                       「代用」の旨を出す。改刻版のように代用でない描き方は render_note に見本へ添える文を書く）。
--                       手元に無い書体は render_font_name を NULL にする（見本を描かない）。
--   series_font_usages  作品のどこにどの書体を使ったか。1 行 = 作品 × 使う場所 × 時期（× 本放送だけの範囲）。
--                       使う場所は OP クレジット／OP 歌詞／OP 歌詞のルビ／サブタイトル／サブタイトルのルビ／
--                       ED クレジット／ED 歌詞／ED 歌詞のルビ。字の大きさは画面の高さ 1080 換算の px（仮想ボディ）で、
--                       クレジットは人名（size_px）と役職名（role_size_px）を分けて持ち、組み方（画面の中の位置・役職名と人名の並び・人名の字取りと行送り・
--                       声の出演の組み）を JSON 1 列（layout）で持つ。
--
-- 移し替え：
--   1. subtitle_fonts の各行を fonts に写す（書体名・ウェイトの整理はデータ側で行う）。
--   2. series_subtitle_styles.font_subtitle / font_subtitle_ruby を series_font_usages のサブタイトル・
--      サブタイトルのルビの行に写す（振り仮名の書体が NULL の作品は親字と同じ書体）。
--   3. series_subtitle_styles から書体の 2 列を外し、subtitle_fonts を消す。
--      サブタイトルのテロップ画像の書体は、以後 series_font_usages → fonts.render_font_name で引く。
--
-- 冪等性: テーブルは CREATE TABLE IF NOT EXISTS、移し替えは元のテーブル・列があるときだけ行う。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `fonts` (
  `font_id`           int           NOT NULL AUTO_INCREMENT,
  `foundry`           varchar(16)   NOT NULL COMMENT '書体の作り手 SHAKEN（写研）/ MORISAWA（モリサワ）/ FONTWORKS（フォントワークス）',
  `family_name`       varchar(64)   NOT NULL COMMENT '書体名（提供元の書き方。「ファン蘭」「UD丸ゴ_ラージ」）',
  `family_group`      varchar(64)   DEFAULT NULL COMMENT '同じページにまとめる書体の名前（写研の「石井中太ゴシック」「石井太ゴシック」を「石井ゴシック」に。NULL なら family_name）',
  `weight`            varchar(16)   DEFAULT NULL COMMENT 'ウェイト（「B」「DB」。ウェイトの無い書体・ウェイトの分からない書体は NULL）',
  `render_font_name`  varchar(64)   DEFAULT NULL COMMENT '見本・テロップ画像を描く手元の書体（Windows の書体名）。NULL なら描かない',
  `render_family_name` varchar(64)   DEFAULT NULL COMMENT '見本を描く書体の名前（NULL なら family_name と同じ）',
  `render_weight`     varchar(16)   DEFAULT NULL COMMENT '描く書体のウェイト。書体名かウェイトが放送の書体と違えば代用としてサイトに明記する',
  `render_note`       varchar(128)  DEFAULT NULL COMMENT '見本に添える注記（NULL なら、書体名かウェイトが違うときに「代用」の文を自動で出す）',
  `license_kind`      varchar(16)   DEFAULT NULL COMMENT '描く書体のライセンス FONTWORKS_LETS / MORISAWA_FONTS',
  `product_url`       varchar(1024) DEFAULT NULL COMMENT '提供元の製品ページ（モリサワ・フォントワークス）',
  `archive_url`       varchar(1024) DEFAULT NULL COMMENT '写研アーカイブの書体のページ',
  `notes`             text          COMMENT '内部メモ（サイトに出さない）',
  `created_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`font_id`),
  UNIQUE KEY `uq_fonts_family_weight` (`foundry`, `family_name`, `weight`),
  CONSTRAINT `ck_fonts_foundry` CHECK (`foundry` IN ('SHAKEN', 'MORISAWA', 'FONTWORKS')),
  CONSTRAINT `ck_fonts_license` CHECK (`license_kind` IS NULL OR `license_kind` IN ('FONTWORKS_LETS', 'MORISAWA_FONTS'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='作品で使われた書体のマスタ';

CREATE TABLE IF NOT EXISTS `series_font_usages` (
  `usage_id`           int               NOT NULL AUTO_INCREMENT,
  `series_id`          int               NOT NULL,
  `usage_kind`         varchar(24)       NOT NULL COMMENT '使う場所 OP_CREDIT / OP_LYRICS / OP_LYRICS_RUBY / SUBTITLE / SUBTITLE_RUBY / ED_CREDIT / ED_LYRICS / ED_LYRICS_RUBY',
  `period_no`          tinyint unsigned  NOT NULL DEFAULT 1 COMMENT '時期（1 = 前期・通期、2 = 後期 …）',
  `variant_no`         tinyint unsigned  NOT NULL DEFAULT 1 COMMENT '色違いの番号（同じ場所・時期で体裁の色だけが違うもの。1 から）',
  `variant_label`      varchar(32)       DEFAULT NULL COMMENT '色違いの呼び名（「ピンク」「水色」など。サイトに出す）',
  `is_broadcast_only`  tinyint(1)        NOT NULL DEFAULT 0 COMMENT '本放送だけの書体なら 1（BD などは通常の書体）',
  `first_episode_no`   smallint unsigned DEFAULT NULL COMMENT '使った最初の話（NULL は時期の全話）',
  `last_episode_no`    smallint unsigned DEFAULT NULL COMMENT '使った最後の話（NULL は時期の全話）',
  `font_id`            int               NOT NULL,
  `is_weight_unknown`  tinyint(1)        NOT NULL DEFAULT 0 COMMENT 'ウェイトが決めきれていなければ 1（サイトに「ウェイト不明」と出す）',
  `modification_note`  varchar(64)       DEFAULT NULL COMMENT '書体に手を加えた旨（「太め加工」など。サイトに出す）',
  `size_px`            decimal(5,1)      DEFAULT NULL COMMENT '字の大きさ（画面の高さ 1080 換算の px、仮想ボディ）。クレジットは人名',
  `oblique_deg`        decimal(3,1)      DEFAULT NULL COMMENT '斜体の角度（度、右に倒す。NULL は 0）',
  `ruby_raise_em`          decimal(4,3)      DEFAULT NULL COMMENT 'ルビのベースラインの、親字のベースラインからの高さ（親字の大きさに対する比）',
  `ruby_letter_spacing_em` decimal(4,3)      DEFAULT NULL COMMENT 'ルビの字と字の間に足す空き（ルビの大きさに対する比）',
  `ruby_grouping`          varchar(8)        DEFAULT NULL COMMENT 'ルビの置き方 MONO / JUKUGO / SPREAD',
  `role_size_px`       decimal(5,1)      DEFAULT NULL COMMENT 'クレジットの役職名の大きさ（同上）',
  `layout`             json              DEFAULT NULL COMMENT 'クレジットの組み方（画面の中の位置・役職名と人名の並び・人名の字取りと行送り・声の出演の組み。{version, screen, role, name, cast}。値は人名の字の大きさに対する比、画面の位置は画面の幅・高さに対する比）',
  `decoration`         json              DEFAULT NULL COMMENT '装飾（字の色・フチ・影・光彩。{version, fill, join, layers:[下から重ねる層]}。値は字の大きさに対する比。NULL はテロップの既定の体裁）',
  `display_note`       varchar(255)      DEFAULT NULL COMMENT 'サイトに添える注記',
  `notes`              text              COMMENT '内部メモ（サイトに出さない）',
  `created_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`usage_id`),
  KEY `ix_sfu_series` (`series_id`, `usage_kind`, `period_no`),
  KEY `ix_sfu_font` (`font_id`),
  CONSTRAINT `fk_sfu_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_sfu_font`   FOREIGN KEY (`font_id`)   REFERENCES `fonts`  (`font_id`)   ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_sfu_layout` CHECK (`layout` IS NULL OR json_valid(`layout`)),
  CONSTRAINT `ck_sfu_decoration` CHECK (`decoration` IS NULL OR json_valid(`decoration`)),
  CONSTRAINT `ck_sfu_ruby_grouping` CHECK (`ruby_grouping` IS NULL OR `ruby_grouping` IN ('MONO', 'JUKUGO', 'SPREAD')),
  CONSTRAINT `ck_sfu_usage_kind` CHECK (`usage_kind` IN ('OP_CREDIT', 'OP_LYRICS', 'OP_LYRICS_RUBY', 'SUBTITLE', 'SUBTITLE_RUBY', 'ED_CREDIT', 'ED_LYRICS', 'ED_LYRICS_RUBY'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='作品のどこにどの書体を使ったか';

DROP PROCEDURE IF EXISTS _v1186_add_fonts;
DELIMITER $$
CREATE PROCEDURE _v1186_add_fonts()
BEGIN
  -- 0. 先にこのマイグレーションで fonts を作った DB には、あとから足した 2 列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fonts' AND COLUMN_NAME = 'render_family_name'
  ) THEN
    ALTER TABLE `fonts`
      ADD COLUMN `render_family_name` varchar(64) DEFAULT NULL COMMENT '見本を描く書体の名前（NULL なら family_name と同じ）' AFTER `render_font_name`,
      ADD COLUMN `render_note` varchar(128) DEFAULT NULL COMMENT '見本に添える注記（NULL なら、書体名かウェイトが違うときに「代用」の文を自動で出す）' AFTER `render_weight`;
  END IF;

  -- 0d. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足した斜体の角度の列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'oblique_deg'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `oblique_deg` decimal(3,1) DEFAULT NULL COMMENT '斜体の角度（度、右に倒す。NULL は 0）' AFTER `size_px`;
  END IF;

  -- 0e. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足した装飾の列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'decoration'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `decoration` json DEFAULT NULL COMMENT '装飾（字の色・フチ・影・光彩。{version, fill, join, layers:[下から重ねる層]}。値は字の大きさに対する比。NULL はテロップの既定の体裁）' AFTER `role_size_px`,
      ADD CONSTRAINT `ck_sfu_decoration` CHECK (`decoration` IS NULL OR json_valid(`decoration`));
  END IF;

  -- 0f. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足したルビの置き方の列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'ruby_raise_em'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `ruby_raise_em` decimal(4,3) DEFAULT NULL COMMENT 'ルビのベースラインの、親字のベースラインからの高さ（親字の大きさに対する比）' AFTER `oblique_deg`,
      ADD COLUMN `ruby_letter_spacing_em` decimal(4,3) DEFAULT NULL COMMENT 'ルビの字と字の間に足す空き（ルビの大きさに対する比）' AFTER `ruby_raise_em`,
      ADD COLUMN `ruby_grouping` varchar(8) DEFAULT NULL COMMENT 'ルビの置き方 MONO / JUKUGO / SPREAD' AFTER `ruby_letter_spacing_em`,
      ADD CONSTRAINT `ck_sfu_ruby_grouping` CHECK (`ruby_grouping` IS NULL OR `ruby_grouping` IN ('MONO', 'JUKUGO', 'SPREAD'));
  END IF;

  -- 0g. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足した色違いの列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'variant_no'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `variant_no` tinyint unsigned NOT NULL DEFAULT 1 COMMENT '色違いの番号（同じ場所・時期で体裁の色だけが違うもの。1 から）' AFTER `period_no`,
      ADD COLUMN `variant_label` varchar(32) DEFAULT NULL COMMENT '色違いの呼び名（「ピンク」「水色」など。サイトに出す）' AFTER `variant_no`;
  END IF;

  -- 0h. 先にこのマイグレーションで series_font_usages を作った DB では、役職名・人名の組みの 5 列を外し、組み方の JSON 1 列にまとめる。
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'role_span_em'
  ) THEN
    ALTER TABLE `series_font_usages`
      DROP CHECK `ck_sfu_role_align`,
      DROP COLUMN `name_span_em`,
      DROP COLUMN `role_span_em`,
      DROP COLUMN `role_name_gap_em`,
      DROP COLUMN `role_baseline_offset_em`,
      DROP COLUMN `role_align`;
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'layout'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `layout` json DEFAULT NULL COMMENT 'クレジットの組み方（画面の中の位置・役職名と人名の並び・人名の字取りと行送り・声の出演の組み。{version, screen, role, name, cast}。値は人名の字の大きさに対する比、画面の位置は画面の幅・高さに対する比）' AFTER `role_size_px`,
      ADD CONSTRAINT `ck_sfu_layout` CHECK (`layout` IS NULL OR json_valid(`layout`));
  END IF;

  -- 0i. 先にこのマイグレーションで fonts を作った DB には、あとから足した書体名のまとめの列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fonts' AND COLUMN_NAME = 'family_group'
  ) THEN
    ALTER TABLE `fonts`
      ADD COLUMN `family_group` varchar(64) DEFAULT NULL COMMENT '同じページにまとめる書体の名前（写研の「石井中太ゴシック」「石井太ゴシック」を「石井ゴシック」に。NULL なら family_name）' AFTER `family_name`;
  END IF;

  -- 1. subtitle_fonts の行を fonts に写す（同じ描く書体の行がまだ無いものだけ）。
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'subtitle_fonts'
  ) THEN
    INSERT INTO `fonts` (`foundry`, `family_name`, `render_font_name`, `license_kind`, `product_url`)
    SELECT
      CASE WHEN sf.`font_name` LIKE 'A-SK %' THEN 'SHAKEN'
           WHEN sf.`font_name` LIKE 'FOT-%' THEN 'FONTWORKS'
           ELSE 'MORISAWA' END,
      COALESCE(sf.`display_name`, sf.`font_name`),
      sf.`font_name`, sf.`license_kind`, sf.`product_url`
    FROM `subtitle_fonts` sf
    WHERE NOT EXISTS (SELECT 1 FROM `fonts` f WHERE f.`render_font_name` = sf.`font_name`);
  END IF;

  -- 2. 作品のサブタイトルの書体を series_font_usages に写し、書体の 2 列を外す。
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_subtitle_styles' AND COLUMN_NAME = 'font_subtitle'
  ) THEN
    -- マスタに行の無い書体も、名前だけの行として fonts に足してから結び付ける。
    INSERT INTO `fonts` (`foundry`, `family_name`, `render_font_name`)
    SELECT DISTINCT
      CASE WHEN n.`font_name` LIKE 'A-SK %' THEN 'SHAKEN'
           WHEN n.`font_name` LIKE 'FOT-%' THEN 'FONTWORKS'
           ELSE 'MORISAWA' END,
      n.`font_name`, n.`font_name`
    FROM (
      SELECT `font_subtitle` AS `font_name` FROM `series_subtitle_styles` WHERE `font_subtitle` IS NOT NULL
      UNION
      SELECT `font_subtitle_ruby` FROM `series_subtitle_styles` WHERE `font_subtitle_ruby` IS NOT NULL
    ) n
    WHERE NOT EXISTS (SELECT 1 FROM `fonts` f WHERE f.`render_font_name` = n.`font_name`);

    INSERT INTO `series_font_usages` (`series_id`, `usage_kind`, `font_id`)
    SELECT s.`series_id`, 'SUBTITLE',
           (SELECT MIN(f.`font_id`) FROM `fonts` f WHERE f.`render_font_name` = s.`font_subtitle`)
    FROM `series_subtitle_styles` s
    WHERE s.`font_subtitle` IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM `series_font_usages` u WHERE u.`series_id` = s.`series_id` AND u.`usage_kind` = 'SUBTITLE');

    INSERT INTO `series_font_usages` (`series_id`, `usage_kind`, `font_id`)
    SELECT s.`series_id`, 'SUBTITLE_RUBY',
           (SELECT MIN(f.`font_id`) FROM `fonts` f WHERE f.`render_font_name` = COALESCE(s.`font_subtitle_ruby`, s.`font_subtitle`))
    FROM `series_subtitle_styles` s
    WHERE COALESCE(s.`font_subtitle_ruby`, s.`font_subtitle`) IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM `series_font_usages` u WHERE u.`series_id` = s.`series_id` AND u.`usage_kind` = 'SUBTITLE_RUBY');

    ALTER TABLE `series_subtitle_styles` DROP COLUMN `font_subtitle_ruby`, DROP COLUMN `font_subtitle`;
  END IF;

  -- 3. subtitle_fonts を消す（行は fonts に写し終えている）。
  DROP TABLE IF EXISTS `subtitle_fonts`;
END$$
DELIMITER ;

CALL _v1186_add_fonts();
DROP PROCEDURE IF EXISTS _v1186_add_fonts;
