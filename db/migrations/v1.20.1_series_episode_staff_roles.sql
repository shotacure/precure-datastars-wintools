-- =====================================================================
-- v1.20.1_series_episode_staff_roles.sql
--
-- 作品ごとの、各話のスタッフの札の役職を持つ表を作る。作品のページのエピソード一覧・トップページの
-- エピソード欄・エピソード詳細のスタッフ欄・/episodes/ の札（と各話の OGP カード）は、この表に行のある
-- 役職だけを、並び順の順に、行に書いた色で出す。行の無い作品には札を出さない。
--   series_episode_staff_roles
--     series_id      作品（→ series.series_id）
--     role_code      役職（→ roles.role_code）
--     display_order  作品の中の並び順（小さいほど先）
--     badge_color    札の色（#rrggbb の小文字）。札の地・枠・字の色はこの色から作る
--
-- 初期の行は、これまでの表示と同じになるように、話のクレジットのある作品にだけ、作品の種別とクレジットから作る。
-- 種別ごとの役職のうち、その作品の話のクレジットに出ている役職だけを入れる：
--   TV     脚本・絵コンテ・演出・総作画監督・作画監督・美術（ART_DIRECTOR_TV）
--   OTONA  脚本・絵コンテ・演出・作画監督と同じカードに出る役職（脚本・絵コンテ・演出・作画監督・美術の
--          区分の役職を除く。クレジットに初めて出た順）・美術デザイン
-- 並び順は作品ごとに 1 から振る。行のある作品と、話のクレジットの無い作品には足さない（画面で決める）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS と、行の無い作品だけへの INSERT。
-- =====================================================================

CREATE TABLE IF NOT EXISTS `series_episode_staff_roles` (
  `series_id`     int              NOT NULL COMMENT '作品',
  `role_code`     varchar(64)      NOT NULL COMMENT '各話の札に出す役職',
  `display_order` tinyint unsigned NOT NULL COMMENT '作品の中の並び順（小さいほど先）',
  `badge_color`   char(7)          NOT NULL COMMENT '札の色（#rrggbb の小文字）',
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64) DEFAULT NULL,
  `updated_by`    varchar(64) DEFAULT NULL,
  PRIMARY KEY (`series_id`, `role_code`),
  KEY `ix_series_episode_staff_roles_role` (`role_code`),
  CONSTRAINT `fk_series_episode_staff_roles_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_episode_staff_roles_role` FOREIGN KEY (`role_code`) REFERENCES `roles` (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_series_episode_staff_roles_color` CHECK (regexp_like(`badge_color`, '^#[0-9a-f]{6}$', 'c'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='作品ごとの各話のスタッフの札の役職（エピソード一覧・トップ・エピソード詳細・/episodes/）';

-- 話のクレジットの役職を、作品・話数・クレジットの中の位置つきで並べる
DROP TABLE IF EXISTS `_v1201_ep_roles`;
CREATE TABLE `_v1201_ep_roles` AS
SELECT e.series_id, e.series_ep_no, cc.card_id, r.role_code, ro.name_ja,
       (c.credit_id * 1000000 + cc.card_seq * 10000 + t.tier_no * 1000 + g.group_no * 100 + r.order_in_group) AS pos
  FROM credit_card_roles r
  JOIN roles ro             ON ro.role_code    = r.role_code
  JOIN credit_card_groups g ON g.card_group_id = r.card_group_id
  JOIN credit_card_tiers  t ON t.card_tier_id  = g.card_tier_id
  JOIN credit_cards      cc ON cc.card_id      = t.card_id
  JOIN credits            c ON c.credit_id     = cc.credit_id
  JOIN episodes           e ON e.episode_id    = c.episode_id;

-- 行の無い作品のうち、話のクレジットのある作品
DROP TABLE IF EXISTS `_v1201_ep_target`;
CREATE TABLE `_v1201_ep_target` AS
SELECT s.series_id, s.kind_code
  FROM series s
 WHERE NOT EXISTS (SELECT 1 FROM series_episode_staff_roles k WHERE k.series_id = s.series_id)
   AND EXISTS (SELECT 1 FROM _v1201_ep_roles x WHERE x.series_id = s.series_id);

-- TV：固定の 6 役職
INSERT INTO series_episode_staff_roles (series_id, role_code, display_order, badge_color, created_by, updated_by)
SELECT ts.series_id, d.role_code, d.display_order, d.badge_color, 'migration_v1.20.1', 'migration_v1.20.1'
  FROM _v1201_ep_target ts
  JOIN (
              SELECT 'SCREENPLAY'               AS role_code, 1 AS display_order, '#3b82c4' AS badge_color
    UNION ALL SELECT 'STORYBOARD',               2, '#2ea7ad'
    UNION ALL SELECT 'EPISODE_DIRECTOR',         3, '#e91e63'
    UNION ALL SELECT 'CHIEF_ANIMATION_DIRECTOR', 4, '#4ca36b'
    UNION ALL SELECT 'ANIMATION_DIRECTOR',       5, '#4ca36b'
    UNION ALL SELECT 'ART_DIRECTOR_TV',          6, '#d4a017'
  ) d
 WHERE ts.kind_code = 'TV'
   AND EXISTS (SELECT 1 FROM _v1201_ep_roles x WHERE x.series_id = ts.series_id AND x.role_code = d.role_code);

-- OTONA：脚本・絵コンテ・演出（1〜3）
INSERT INTO series_episode_staff_roles (series_id, role_code, display_order, badge_color, created_by, updated_by)
SELECT ts.series_id, d.role_code, d.display_order, d.badge_color, 'migration_v1.20.1', 'migration_v1.20.1'
  FROM _v1201_ep_target ts
  JOIN (
              SELECT 'SCREENPLAY'       AS role_code, 1 AS display_order, '#3b82c4' AS badge_color
    UNION ALL SELECT 'STORYBOARD',       2, '#2ea7ad'
    UNION ALL SELECT 'EPISODE_DIRECTOR', 3, '#e91e63'
  ) d
 WHERE ts.kind_code = 'OTONA'
   AND EXISTS (SELECT 1 FROM _v1201_ep_roles x WHERE x.series_id = ts.series_id AND x.role_code = d.role_code);

-- OTONA：作画監督と同じカードに出る役職（各話のチーフの区分の役職を除く）を 4 から。
-- 並びは各話のクレジットの中の並びに合わせる：カードごと（版の違うカードは別々）にそのカードの中で何番目に出たかを数え、
-- その最大の小さい順（回によって出ない役職があっても、出るカードの並びを崩さない）。同じなら作品で初めて出た順
DROP TABLE IF EXISTS `_v1201_ep_anim_by_ep`;
CREATE TABLE `_v1201_ep_anim_by_ep` AS
SELECT x.series_id, x.series_ep_no, x.role_code,
       MIN(x.series_ep_no * 1000000000000 + x.pos) AS first_key,
       DENSE_RANK() OVER (PARTITION BY x.series_id, x.series_ep_no, x.card_id ORDER BY MIN(x.pos)) AS rank_in_ep
  FROM _v1201_ep_roles x
  JOIN _v1201_ep_target ts ON ts.series_id = x.series_id AND ts.kind_code = 'OTONA'
 WHERE x.card_id IN (SELECT a.card_id FROM _v1201_ep_roles a WHERE a.role_code = 'ANIMATION_DIRECTOR')
   AND x.role_code NOT IN ('SCREENPLAY', 'STORYBOARD', 'EPISODE_DIRECTOR', 'ANIMATION_DIRECTOR', 'ART_DIRECTOR')
   AND x.name_ja   NOT IN ('脚本', '絵コンテ', '演出', '作画監督', '美術')
 GROUP BY x.series_id, x.series_ep_no, x.card_id, x.role_code;

DROP TABLE IF EXISTS `_v1201_ep_anim`;
CREATE TABLE `_v1201_ep_anim` AS
SELECT series_id, role_code, MAX(rank_in_ep) AS max_rank, MIN(first_key) AS first_key
  FROM _v1201_ep_anim_by_ep
 GROUP BY series_id, role_code;

INSERT INTO series_episode_staff_roles (series_id, role_code, display_order, badge_color, created_by, updated_by)
SELECT a.series_id, a.role_code,
       3 + ROW_NUMBER() OVER (PARTITION BY a.series_id ORDER BY a.max_rank, a.first_key, a.role_code),
       '#4ca36b', 'migration_v1.20.1', 'migration_v1.20.1'
  FROM _v1201_ep_anim a;

-- OTONA：美術デザイン（最後）
INSERT INTO series_episode_staff_roles (series_id, role_code, display_order, badge_color, created_by, updated_by)
SELECT ts.series_id, 'ART_DESIGN',
       1 + COALESCE((SELECT MAX(k.display_order) FROM series_episode_staff_roles k WHERE k.series_id = ts.series_id), 0),
       '#d4a017', 'migration_v1.20.1', 'migration_v1.20.1'
  FROM _v1201_ep_target ts
 WHERE ts.kind_code = 'OTONA'
   AND EXISTS (SELECT 1 FROM _v1201_ep_roles x WHERE x.series_id = ts.series_id AND x.role_code = 'ART_DESIGN');

-- 入れた作品の並び順を 1 から詰める
UPDATE series_episode_staff_roles k
  JOIN (SELECT series_id, role_code, ROW_NUMBER() OVER (PARTITION BY series_id ORDER BY display_order, role_code) AS rn
          FROM series_episode_staff_roles) x ON x.series_id = k.series_id AND x.role_code = k.role_code
  JOIN _v1201_ep_target ts ON ts.series_id = k.series_id
   SET k.display_order = x.rn
 WHERE k.display_order <> x.rn;

DROP TABLE IF EXISTS `_v1201_ep_roles`;
DROP TABLE IF EXISTS `_v1201_ep_target`;
DROP TABLE IF EXISTS `_v1201_ep_anim_by_ep`;
DROP TABLE IF EXISTS `_v1201_ep_anim`;
