-- =====================================================================
-- v1.22.1_add_petitcure_tables.sql
--
-- ショートアニメ「ぷちきゅあ」の話・動画・スタッフを持つ 4 テーブルを新設する。
-- ぷちきゅあは YouTube の配信で、サブタイトルが無く、クレジットは画面ではなく動画の概要欄に
-- 書かれている。episodes（放送の話）と credits（画面の OP/ED の 6 階層）には入れず、この系統だけで持つ。
-- 人物・企業の名義は既存の person_aliases / company_aliases につなぐ。
--
-- 1) petitcure_episodes        話（話数と、題に付く飾りの言葉）
-- 2) petitcure_videos          YouTube の動画（チャンネル・種類・題・公開時刻・長さ・概要欄の全文）
-- 3) petitcure_video_episodes  動画 ↔ 話 の多対多（1 本に 2 話入る動画に対応）
-- 4) petitcure_video_staff     動画の概要欄のスタッフ。1 行 1 名。役職と名前は書かれたままの文字も残す
--
-- 冪等性: すべて CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1) 話
--    話数は作品の中の通し番号（YouTube の題の「第N話」「＃N」）。サブタイトルは無いので持たない。
--    title_decoration は題に付く飾りの言葉（「【おやすみ用】眠れない夜に…」など）。無い回は NULL。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `petitcure_episodes` (
  `petitcure_episode_id` int NOT NULL AUTO_INCREMENT,
  `series_id`            int NOT NULL,
  `ep_no`                smallint unsigned NOT NULL COMMENT '作品の中の話数',
  `title_decoration`     varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT 'YouTube の題に付く飾りの言葉（NULL=なし）',
  `notes`                text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`           varchar(64) DEFAULT NULL,
  `updated_by`           varchar(64) DEFAULT NULL,
  `is_deleted`           tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`petitcure_episode_id`),
  UNIQUE KEY `uq_petitcure_ep` (`series_id`,`ep_no`),
  CONSTRAINT `fk_petitcure_ep_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_petitcure_ep_no_pos` CHECK ((`ep_no` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ぷちきゅあの話';

-- ---------------------------------------------------------------------
-- 2) YouTube の動画
--    video_id は YouTube の動画 ID（大文字小文字を区別するので utf8mb4_bin）。
--    channel：PETITCURE=ぷちきゅあ公式チャンネル / PRECURE_OFFICIAL=プリキュア公式YouTubeチャンネル /
--             TOEI_ANIMATION=東映アニメーションのチャンネル。
--    video_kind：MAIN=通常の動画 / SHORT=ショート動画。
--    published_at は日本時間、秒まで。description_text は取った時点の概要欄の全文（スタッフの出典）。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `petitcure_videos` (
  `video_id`         varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT 'YouTube の動画 ID',
  `channel`          enum('PETITCURE','PRECURE_OFFICIAL','TOEI_ANIMATION') NOT NULL COMMENT '投稿したチャンネル',
  `video_kind`       enum('MAIN','SHORT') NOT NULL DEFAULT 'MAIN' COMMENT 'MAIN=通常の動画 / SHORT=ショート動画',
  `title_text`       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT 'YouTube の題（そのまま）',
  `published_at`     datetime NOT NULL COMMENT '公開の時刻（日本時間）',
  `duration_seconds` int unsigned DEFAULT NULL COMMENT '長さ（秒）',
  `description_text` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks COMMENT '概要欄の全文（取った時点）',
  `fetched_at`       datetime DEFAULT NULL COMMENT '題・概要欄を取った日時（日本時間）',
  `notes`            text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64) DEFAULT NULL,
  `updated_by`       varchar(64) DEFAULT NULL,
  `is_deleted`       tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`video_id`),
  KEY `ix_petitcure_videos_published` (`published_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ぷちきゅあの YouTube の動画';

-- ---------------------------------------------------------------------
-- 3) 動画 ↔ 話
--    「第1話&第2話」のように 1 本に 2 話入る動画は 2 行。同じ話の写し（ショート・別チャンネル）も話につなぐ。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `petitcure_video_episodes` (
  `video_id`             varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `petitcure_episode_id` int NOT NULL,
  `created_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by`           varchar(64) DEFAULT NULL,
  PRIMARY KEY (`video_id`,`petitcure_episode_id`),
  KEY `ix_petitcure_video_episodes_ep` (`petitcure_episode_id`),
  CONSTRAINT `fk_petitcure_ve_video`   FOREIGN KEY (`video_id`)             REFERENCES `petitcure_videos`   (`video_id`)             ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_petitcure_ve_episode` FOREIGN KEY (`petitcure_episode_id`) REFERENCES `petitcure_episodes` (`petitcure_episode_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ぷちきゅあの動画と話のつなぎ';

-- ---------------------------------------------------------------------
-- 4) 動画の概要欄のスタッフ
--    概要欄の「♡今回のスタッフ♡」の 1 名を 1 行。1 行に何人も並ぶ役職は人数分の行にする。
--    role_label_text / name_text / affiliation_text は概要欄に書かれたままの文字（表記の揺れも残す）。
--    role_code は役職マスタ（roles）につなぐ（未決の間は NULL）。
--    person_alias_id / company_alias_id は名義につなぐ（未決の間は両方 NULL）。両方を埋めることはない
--    （MySQL は参照アクション付きの列を CHECK に使えないため、登録側で守る）。
--    affiliation_company_alias_id は「（ゴキンジョ）」のような所属を企業の名義につなぐ。
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `petitcure_video_staff` (
  `staff_id`                     int NOT NULL AUTO_INCREMENT,
  `video_id`                     varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `display_order`                smallint unsigned NOT NULL COMMENT '概要欄の中の並び順（1 から）',
  `role_label_text`              varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '概要欄の役職名（そのまま）',
  `role_code`                    varchar(64) DEFAULT NULL COMMENT '役職マスタの役職（NULL=未決）',
  `name_text`                    varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL COMMENT '概要欄の名前（そのまま）',
  `person_alias_id`              int DEFAULT NULL COMMENT '人物の名義（NULL=未決か企業）',
  `company_alias_id`             int DEFAULT NULL COMMENT '企業/団体の名義（NULL=未決か人物）',
  `affiliation_text`             varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT '名前に添えた所属（そのまま。括弧は除く）',
  `affiliation_company_alias_id` int DEFAULT NULL COMMENT '所属の企業/団体の名義',
  `notes`                        text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                   varchar(64) DEFAULT NULL,
  `updated_by`                   varchar(64) DEFAULT NULL,
  `is_deleted`                   tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`staff_id`),
  UNIQUE KEY `uq_petitcure_staff_pos` (`video_id`,`display_order`),
  KEY `ix_petitcure_staff_role` (`role_code`),
  KEY `ix_petitcure_staff_person` (`person_alias_id`),
  KEY `ix_petitcure_staff_company` (`company_alias_id`),
  KEY `ix_petitcure_staff_affiliation` (`affiliation_company_alias_id`),
  CONSTRAINT `fk_petitcure_staff_video`       FOREIGN KEY (`video_id`)                     REFERENCES `petitcure_videos` (`video_id`)  ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_petitcure_staff_role`        FOREIGN KEY (`role_code`)                    REFERENCES `roles`            (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_petitcure_staff_person`      FOREIGN KEY (`person_alias_id`)              REFERENCES `person_aliases`   (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_petitcure_staff_company`     FOREIGN KEY (`company_alias_id`)             REFERENCES `company_aliases`  (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_petitcure_staff_affiliation` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases`  (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_petitcure_staff_order_pos`   CHECK ((`display_order` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ぷちきゅあの動画の概要欄のスタッフ';
