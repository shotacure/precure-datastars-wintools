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
--                       クレジットは人名（size_px）と役職名（role_size_px）を分けて持ち、人名を割り付ける幅（name_span_em。五字取りなら 5）と、役職名の組み（字取り・人名との空き・ベースラインのずれ・揃え）も持つ。
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
  `is_broadcast_only`  tinyint(1)        NOT NULL DEFAULT 0 COMMENT '本放送だけの書体なら 1（BD などは通常の書体）',
  `first_episode_no`   smallint unsigned DEFAULT NULL COMMENT '使った最初の話（NULL は時期の全話）',
  `last_episode_no`    smallint unsigned DEFAULT NULL COMMENT '使った最後の話（NULL は時期の全話）',
  `font_id`            int               NOT NULL,
  `is_weight_unknown`  tinyint(1)        NOT NULL DEFAULT 0 COMMENT 'ウェイトが決めきれていなければ 1（サイトに「ウェイト不明」と出す）',
  `modification_note`  varchar(64)       DEFAULT NULL COMMENT '書体に手を加えた旨（「太め加工」など。サイトに出す）',
  `size_px`            decimal(5,1)      DEFAULT NULL COMMENT '字の大きさ（画面の高さ 1080 換算の px、仮想ボディ）。クレジットは人名',
  `role_size_px`       decimal(5,1)      DEFAULT NULL COMMENT 'クレジットの役職名の大きさ（同上）',
  `name_span_em`       decimal(3,2)      DEFAULT NULL COMMENT 'クレジットの人名を割り付ける幅（字の大きさを 1 とした字数。五字取りなら 5.00）',
  `role_span_em`            decimal(3,2)      DEFAULT NULL COMMENT 'クレジットの役職名を割り付ける幅（役職名の字の大きさを 1 とした字数）',
  `role_name_gap_em`        decimal(4,2)      DEFAULT NULL COMMENT '役職名の右端から人名の左端までの空き（人名の字の大きさを 1 とした値）',
  `role_baseline_offset_em` decimal(4,2)      DEFAULT NULL COMMENT '役職名のベースラインの人名のベースラインからの高さ（上を正、人名の字の大きさを 1 とした値）',
  `role_align`              varchar(16)       DEFAULT NULL COMMENT '役職名の揃え RIGHT / LEFT / CENTER / JUSTIFY（縦に並ぶ役職名のどこがそろうか。役職が 1 つだけなら NULL）',
  `display_note`       varchar(255)      DEFAULT NULL COMMENT 'サイトに添える注記',
  `notes`              text              COMMENT '内部メモ（サイトに出さない）',
  `created_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`usage_id`),
  KEY `ix_sfu_series` (`series_id`, `usage_kind`, `period_no`),
  KEY `ix_sfu_font` (`font_id`),
  CONSTRAINT `fk_sfu_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_sfu_font`   FOREIGN KEY (`font_id`)   REFERENCES `fonts`  (`font_id`)   ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_sfu_role_align` CHECK (`role_align` IS NULL OR `role_align` IN ('RIGHT', 'LEFT', 'CENTER', 'JUSTIFY')),
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

  -- 0b. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足した人名の字取りの列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'name_span_em'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `name_span_em` decimal(3,2) DEFAULT NULL COMMENT 'クレジットの人名を割り付ける幅（字の大きさを 1 とした字数。五字取りなら 5.00）' AFTER `role_size_px`;
  END IF;

  -- 0c. 先にこのマイグレーションで series_font_usages を作った DB には、あとから足した役職名の組みの 4 列を足す。
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'series_font_usages' AND COLUMN_NAME = 'role_span_em'
  ) THEN
    ALTER TABLE `series_font_usages`
      ADD COLUMN `role_span_em` decimal(3,2) DEFAULT NULL COMMENT 'クレジットの役職名を割り付ける幅（役職名の字の大きさを 1 とした字数）' AFTER `name_span_em`,
      ADD COLUMN `role_name_gap_em` decimal(4,2) DEFAULT NULL COMMENT '役職名の右端から人名の左端までの空き（人名の字の大きさを 1 とした値）' AFTER `role_span_em`,
      ADD COLUMN `role_baseline_offset_em` decimal(4,2) DEFAULT NULL COMMENT '役職名のベースラインの人名のベースラインからの高さ（上を正、人名の字の大きさを 1 とした値）' AFTER `role_name_gap_em`,
      ADD COLUMN `role_align` varchar(16) DEFAULT NULL COMMENT '役職名の揃え RIGHT / LEFT / CENTER / JUSTIFY（縦に並ぶ役職名のどこがそろうか。役職が 1 つだけなら NULL）' AFTER `role_baseline_offset_em`,
      ADD CONSTRAINT `ck_sfu_role_align` CHECK (`role_align` IS NULL OR `role_align` IN ('RIGHT', 'LEFT', 'CENTER', 'JUSTIFY'));
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
