-- =====================================================================
-- v1.20.1_series_key_staff_roles.sql
--
-- 作品ごとのメインスタッフの役職を持つ表を作る。シリーズ一覧のカードのバッジと、作品詳細の
-- メインスタッフ（と作品詳細の OGP カード）は、この表に行のある役職だけを、並び順の順に、
-- 行に書いた色で出す。行の無い作品にはメインスタッフを出さない。
--   series_key_staff_roles
--     series_id      作品（→ series.series_id）
--     role_code      役職（→ roles.role_code）
--     display_order  作品の中の並び順（小さいほど先）
--     badge_color    バッジの色（#rrggbb の小文字）。バッジの地・枠・字の色はこの色から作る
--
-- 初期の行は、これまでの表示と同じになるように作品の種別とクレジットから作る：
--   TV                     プロデューサー・シリーズ構成・シリーズディレクター・キャラクターデザイン・
--                          衣装・プロップデザイン（その役職のクレジットがある作品だけ）・美術デザイン
--   OTONA                  プロデューサー・シリーズ構成・シリーズディレクター・キャラクターデザイン・美術監督
--   MOVIE / MOVIE_SHORT /  企画（企画のクレジットがあってプロデューサーのクレジットが無い作品だけ）・
--   SPRING / EVENT         プロデューサー・脚本・監督・キャラクターデザイン・作画監督・美術監督
-- 行のある作品には足さない（画面で直した行を上書きしない）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS と、行の無い作品だけへの INSERT。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_key_staff_roles` (
  `series_id`     int              NOT NULL COMMENT '作品',
  `role_code`     varchar(64)      NOT NULL COMMENT 'メインスタッフとして出す役職',
  `display_order` tinyint unsigned NOT NULL COMMENT '作品の中の並び順（小さいほど先）',
  `badge_color`   char(7)          NOT NULL COMMENT 'バッジの色（#rrggbb の小文字）',
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64) DEFAULT NULL,
  `updated_by`    varchar(64) DEFAULT NULL,
  PRIMARY KEY (`series_id`, `role_code`),
  KEY `ix_series_key_staff_roles_role` (`role_code`),
  CONSTRAINT `fk_series_key_staff_roles_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_key_staff_roles_role` FOREIGN KEY (`role_code`) REFERENCES `roles` (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_series_key_staff_roles_color` CHECK (regexp_like(`badge_color`, '^#[0-9a-f]{6}$', 'c'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='作品ごとのメインスタッフの役職（シリーズ一覧のバッジ・作品詳細のメインスタッフ）';

-- 作品ごとに、どの役職のクレジットがあるか（話のクレジットは話の作品で数える）
DROP TABLE IF EXISTS `_v1201_credited_roles`;
CREATE TABLE `_v1201_credited_roles` AS
SELECT DISTINCT s.series_id, r.role_code
  FROM credit_card_roles r
  JOIN credit_card_groups g ON g.card_group_id = r.card_group_id
  JOIN credit_card_tiers  t ON t.card_tier_id  = g.card_tier_id
  JOIN credit_cards      cc ON cc.card_id      = t.card_id
  JOIN credits            c ON c.credit_id     = cc.credit_id
  LEFT JOIN episodes      e ON e.episode_id    = c.episode_id
  JOIN series             s ON s.series_id     = COALESCE(c.series_id, e.series_id)
 WHERE r.role_code IN ('PLANNING', 'PRODUCER', 'COSTUME_PROP_DESIGN');

-- 行の無い作品（この時点で行を持つ作品は除く）
DROP TABLE IF EXISTS `_v1201_target_series`;
CREATE TABLE `_v1201_target_series` AS
SELECT s.series_id, s.kind_code
  FROM series s
 WHERE NOT EXISTS (SELECT 1 FROM series_key_staff_roles k WHERE k.series_id = s.series_id);

-- 種別ごとの役職の組（cond：ALWAYS＝いつも / CREDITED＝その役職のクレジットがあるとき /
-- PLANNING_ONLY＝企画のクレジットがあってプロデューサーのクレジットが無いとき）
INSERT INTO series_key_staff_roles (series_id, role_code, display_order, badge_color, created_by, updated_by)
SELECT ts.series_id, d.role_code, d.display_order, d.badge_color, 'migration_v1.20.1', 'migration_v1.20.1'
  FROM _v1201_target_series ts
  JOIN (
              SELECT 'TV'    AS kind_code, 'PRODUCER'            AS role_code, 1 AS display_order, '#7e57c2' AS badge_color, 'ALWAYS'   AS cond
    UNION ALL SELECT 'TV',    'SERIES_COMPOSITION',  2, '#3b82c4', 'ALWAYS'
    UNION ALL SELECT 'TV',    'SERIES_DIRECTOR',     3, '#e91e63', 'ALWAYS'
    UNION ALL SELECT 'TV',    'CHARACTER_DESIGN',    4, '#4ca36b', 'ALWAYS'
    UNION ALL SELECT 'TV',    'COSTUME_PROP_DESIGN', 5, '#4ca36b', 'CREDITED'
    UNION ALL SELECT 'TV',    'ART_DESIGN',          6, '#d4a017', 'ALWAYS'
    UNION ALL SELECT 'OTONA', 'PRODUCER',            1, '#7e57c2', 'ALWAYS'
    UNION ALL SELECT 'OTONA', 'SERIES_COMPOSITION',  2, '#3b82c4', 'ALWAYS'
    UNION ALL SELECT 'OTONA', 'SERIES_DIRECTOR',     3, '#e91e63', 'ALWAYS'
    UNION ALL SELECT 'OTONA', 'CHARACTER_DESIGN',    4, '#4ca36b', 'ALWAYS'
    UNION ALL SELECT 'OTONA', 'ART_DIRECTOR',        5, '#d4a017', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'PLANNING',            1, '#7e57c2', 'PLANNING_ONLY'
    UNION ALL SELECT 'MOVIE', 'PRODUCER',            2, '#7e57c2', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'SCREENPLAY',          3, '#3b82c4', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'DIRECTOR',            4, '#e91e63', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'CHARACTER_DESIGN',    5, '#4ca36b', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'ANIMATION_DIRECTOR',  6, '#4ca36b', 'ALWAYS'
    UNION ALL SELECT 'MOVIE', 'ART_DIRECTOR',        7, '#d4a017', 'ALWAYS'
  ) d
    ON d.kind_code = CASE WHEN ts.kind_code IN ('MOVIE', 'MOVIE_SHORT', 'SPRING', 'EVENT') THEN 'MOVIE' ELSE ts.kind_code END
 WHERE d.cond = 'ALWAYS'
    OR (d.cond = 'CREDITED'
        AND EXISTS (SELECT 1 FROM _v1201_credited_roles cr WHERE cr.series_id = ts.series_id AND cr.role_code = d.role_code))
    OR (d.cond = 'PLANNING_ONLY'
        AND EXISTS     (SELECT 1 FROM _v1201_credited_roles cr WHERE cr.series_id = ts.series_id AND cr.role_code = 'PLANNING')
        AND NOT EXISTS (SELECT 1 FROM _v1201_credited_roles cr WHERE cr.series_id = ts.series_id AND cr.role_code = 'PRODUCER'));

DROP TABLE IF EXISTS `_v1201_credited_roles`;
DROP TABLE IF EXISTS `_v1201_target_series`;
