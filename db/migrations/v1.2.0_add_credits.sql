-- =============================================================================
-- Migration: v1.1.x -> v1.2.0 クレジット管理基盤の追加
-- =============================================================================
-- 適用対象:
--   v1.1.5 までの `precure_datastars` データベース（既存テーブルに対して
--   series_kinds / part_types に列を追加し、人物・企業・キャラクター・役職・
--   クレジット本体・エピソード主題歌の各テーブル群を新規追加する）。
--
-- 概要:
--   TV・映画のオープニング/エンディングクレジット、および音楽クレジットを
--   構造化してデータ管理するための基盤を導入する。詳細は README.md の
--   「v1.2.0 — クレジット管理基盤」節を参照。
--
-- 安全性:
--   - 本スクリプトは冪等（何度流しても既存テーブル・既存データを破壊しない）
--   - 既存テーブルに対する ALTER は INFORMATION_SCHEMA で列の存在確認後にのみ実行
--   - 新規テーブルは CREATE TABLE IF NOT EXISTS
--   - 初期マスタデータは INSERT IGNORE（既にコードがあればスキップ）
--   - トリガーは DROP TRIGGER IF EXISTS してから再作成
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.0_add_credits.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;
SET FOREIGN_KEY_CHECKS = 0;  -- 追加順を柔軟にするため一時的に無効化
SET SQL_SAFE_UPDATES   = 0;  -- WHERE 節がサブクエリのみ等の UPDATE を許す


-- =============================================================================
-- STEP 1: 既存マスタへのカラム追加
-- =============================================================================
-- series_kinds.credit_attach_to ENUM('SERIES','EPISODE') NOT NULL DEFAULT 'EPISODE'
-- part_types.default_credit_kind ENUM('OP','ED') NULL
--
-- どちらも既存運用に影響を与えないデフォルト値で投入し、その後シリーズ種別
-- 別／パート種別別に既知の値を一括 UPDATE で流し込む。
-- =============================================================================

-- -- series_kinds.credit_attach_to ----------------------------------------------
SET @has_col = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'series_kinds'
    AND COLUMN_NAME  = 'credit_attach_to'
);

SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `series_kinds`
     ADD COLUMN `credit_attach_to` ENUM(''SERIES'',''EPISODE'') NOT NULL DEFAULT ''EPISODE''
     AFTER `name_en`',
  'SELECT ''credit_attach_to already exists on series_kinds. skipping ADD COLUMN.'' AS msg');
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 既知の初期値を流し込む（v1.1.x までの 5 種別。映画系 3 種は SERIES、TV/SPIN-OFF は EPISODE）。
-- 既に既知でない値が入っている場合は何もしない。
UPDATE `series_kinds` SET `credit_attach_to` = 'SERIES'  WHERE `kind_code` IN ('MOVIE','MOVIE_SHORT','SPRING') AND `credit_attach_to` <> 'SERIES';
UPDATE `series_kinds` SET `credit_attach_to` = 'EPISODE' WHERE `kind_code` IN ('TV','SPIN-OFF')                AND `credit_attach_to` <> 'EPISODE';


-- -- part_types.default_credit_kind ---------------------------------------------
SET @has_col = (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'part_types'
    AND COLUMN_NAME  = 'default_credit_kind'
);

SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `part_types`
     ADD COLUMN `default_credit_kind` ENUM(''OP'',''ED'') NULL
     AFTER `display_order`',
  'SELECT ''default_credit_kind already exists on part_types. skipping ADD COLUMN.'' AS msg');
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 既知の初期値を流し込む（OPENING=OP / ENDING=ED）。
UPDATE `part_types` SET `default_credit_kind` = 'OP' WHERE `part_type` = 'OPENING' AND (`default_credit_kind` IS NULL OR `default_credit_kind` <> 'OP');
UPDATE `part_types` SET `default_credit_kind` = 'ED' WHERE `part_type` = 'ENDING'  AND (`default_credit_kind` IS NULL OR `default_credit_kind` <> 'ED');


-- =============================================================================
-- STEP 2: 人物・企業・キャラクター層のマスタテーブル群
-- =============================================================================

-- -- persons --------------------------------------------------------------------
-- 人物マスタ。同一人物としての同一性を持たせる単位。
-- 名義（表記）は person_aliases 側で時期別に管理し、本テーブルは「個人」一意の器。
CREATE TABLE IF NOT EXISTS `persons` (
  `person_id`        int                                                                  NOT NULL AUTO_INCREMENT,
  `family_name`      varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks   DEFAULT NULL,
  `given_name`       varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks   DEFAULT NULL,
  `full_name`        varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  NOT NULL,
  `full_name_kana`   varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  DEFAULT NULL,
  `name_en`          varchar(128)                                                         DEFAULT NULL,
  `notes`            text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64)  DEFAULT NULL,
  `updated_by`       varchar(64)  DEFAULT NULL,
  `is_deleted`       tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`person_id`),
  KEY `ix_persons_full_name` (`full_name`),
  KEY `ix_persons_full_name_kana` (`full_name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- person_aliases -------------------------------------------------------------
-- 人物の名義（表記）マスタ。1 person に多くの alias が紐付き、改名時は前後 alias を
-- predecessor_alias_id / successor_alias_id でリンクする。
-- 通常 1 alias = 1 人物だが、共同名義（稀）対応のために person_alias_persons を別途用意。
CREATE TABLE IF NOT EXISTS `person_aliases` (
  `alias_id`              int                                                                 NOT NULL AUTO_INCREMENT,
  `name`                  varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `predecessor_alias_id`  int          DEFAULT NULL,
  `successor_alias_id`    int          DEFAULT NULL,
  `valid_from`            date         DEFAULT NULL,
  `valid_to`              date         DEFAULT NULL,
  `notes`                 text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`            varchar(64)  DEFAULT NULL,
  `updated_by`            varchar(64)  DEFAULT NULL,
  `is_deleted`            tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`alias_id`),
  KEY `ix_person_aliases_name` (`name`),
  KEY `ix_person_aliases_name_kana` (`name_kana`),
  KEY `ix_person_aliases_predecessor` (`predecessor_alias_id`),
  KEY `ix_person_aliases_successor`   (`successor_alias_id`),
  CONSTRAINT `fk_person_aliases_predecessor` FOREIGN KEY (`predecessor_alias_id`) REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_person_aliases_successor`   FOREIGN KEY (`successor_alias_id`)   REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- person_alias_persons -------------------------------------------------------
-- 名義 ⇄ 人物の多対多。通常は 1 alias につき 1 行（同一人物 1 名義）。
-- 共同名義（稀）の場合のみ 1 alias に複数行が立つ。person_seq は表示順の保持用。
CREATE TABLE IF NOT EXISTS `person_alias_persons` (
  `alias_id`   int             NOT NULL,
  `person_id`  int             NOT NULL,
  `person_seq` tinyint unsigned NOT NULL DEFAULT 1,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`alias_id`,`person_id`),
  UNIQUE KEY `uq_pap_alias_seq` (`alias_id`,`person_seq`),
  KEY `ix_pap_person` (`person_id`),
  CONSTRAINT `fk_pap_alias`  FOREIGN KEY (`alias_id`)  REFERENCES `person_aliases` (`alias_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_pap_person` FOREIGN KEY (`person_id`) REFERENCES `persons`        (`person_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -- companies ------------------------------------------------------------------
-- 企業マスタ。同一企業としての同一性を持たせる単位。分社化等で別企業として
-- 登録された場合は別レコードを立て、屋号（company_aliases）側の前後リンクで
-- 系譜を辿れるようにする。
CREATE TABLE IF NOT EXISTS `companies` (
  `company_id`      int                                                                 NOT NULL AUTO_INCREMENT,
  `name`            varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`       varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`         varchar(128) DEFAULT NULL,
  `founded_date`    date         DEFAULT NULL,
  `dissolved_date`  date         DEFAULT NULL,
  `notes`           text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64)  DEFAULT NULL,
  `updated_by`      varchar(64)  DEFAULT NULL,
  `is_deleted`      tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`company_id`),
  KEY `ix_companies_name`      (`name`),
  KEY `ix_companies_name_kana` (`name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- company_aliases ------------------------------------------------------------
-- 企業の名義（屋号）マスタ。1 company に多くの alias が紐付き、屋号変更時は
-- predecessor_alias_id / successor_alias_id でリンクする。分社化など別 company に
-- またがるリンクもこの 2 列で表現できる（FK は許容）。
CREATE TABLE IF NOT EXISTS `company_aliases` (
  `alias_id`              int                                                                 NOT NULL AUTO_INCREMENT,
  `company_id`            int                                                                 NOT NULL,
  `name`                  varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `predecessor_alias_id`  int          DEFAULT NULL,
  `successor_alias_id`    int          DEFAULT NULL,
  `valid_from`            date         DEFAULT NULL,
  `valid_to`              date         DEFAULT NULL,
  `notes`                 text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`            varchar(64)  DEFAULT NULL,
  `updated_by`            varchar(64)  DEFAULT NULL,
  `is_deleted`            tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`alias_id`),
  KEY `ix_company_aliases_company` (`company_id`),
  KEY `ix_company_aliases_name`    (`name`),
  KEY `ix_company_aliases_predecessor` (`predecessor_alias_id`),
  KEY `ix_company_aliases_successor`   (`successor_alias_id`),
  CONSTRAINT `fk_company_aliases_company`     FOREIGN KEY (`company_id`)           REFERENCES `companies`       (`company_id`) ON DELETE CASCADE   ON UPDATE CASCADE,
  CONSTRAINT `fk_company_aliases_predecessor` FOREIGN KEY (`predecessor_alias_id`) REFERENCES `company_aliases` (`alias_id`)   ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_company_aliases_successor`   FOREIGN KEY (`successor_alias_id`)   REFERENCES `company_aliases` (`alias_id`)   ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- logos ----------------------------------------------------------------------
-- 企業ロゴマスタ。屋号（company_alias）配下に CI バージョン違いのロゴが紐付く構造。
-- 役職下に置く名義は alias または logo のどちらか一方を指す（XOR は entry 側で担保）。
CREATE TABLE IF NOT EXISTS `logos` (
  `logo_id`           int                                                                NOT NULL AUTO_INCREMENT,
  `company_alias_id`  int                                                                NOT NULL,
  `ci_version_label`  varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `valid_from`        date  DEFAULT NULL,
  `valid_to`          date  DEFAULT NULL,
  `description`       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`             text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`        varchar(64)  DEFAULT NULL,
  `updated_by`        varchar(64)  DEFAULT NULL,
  `is_deleted`        tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`logo_id`),
  UNIQUE KEY `uq_logos_alias_ci` (`company_alias_id`,`ci_version_label`),
  CONSTRAINT `fk_logos_company_alias` FOREIGN KEY (`company_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- -- characters -----------------------------------------------------------------
-- キャラクターマスタ。全プリキュアを通じて統一的に管理（series_id は持たない）。
-- 春映画・All Stars・コラボ等でシリーズをまたいで再登場するキャラは同一 character_id を共有する。
-- character_kind は MAIN（主役級）／SUPPORT（準主役）／GUEST（ゲスト）／MOB（モブ・チョイ役）／OTHER。
CREATE TABLE IF NOT EXISTS `characters` (
  `character_id`    int                                                                 NOT NULL AUTO_INCREMENT,
  `name`            varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`       varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `character_kind`  ENUM('MAIN','SUPPORT','GUEST','MOB','OTHER') NOT NULL DEFAULT 'MAIN',
  `notes`           text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64)  DEFAULT NULL,
  `updated_by`      varchar(64)  DEFAULT NULL,
  `is_deleted`      tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`character_id`),
  KEY `ix_characters_name`      (`name`),
  KEY `ix_characters_name_kana` (`name_kana`),
  KEY `ix_characters_kind`      (`character_kind`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- character_aliases ----------------------------------------------------------
-- キャラクターの名義（表記）マスタ。話数によって表記が変化する場合に時期を分けて
-- 記録する（"キュアブラック" / "ブラック" / "美墨なぎさ" 等）。
-- 1 character に多数 alias が紐付く。
CREATE TABLE IF NOT EXISTS `character_aliases` (
  `alias_id`     int                                                                  NOT NULL AUTO_INCREMENT,
  `character_id` int                                                                  NOT NULL,
  `name`         varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  NOT NULL,
  `name_kana`    varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  DEFAULT NULL,
  `valid_from`   date  DEFAULT NULL,
  `valid_to`     date  DEFAULT NULL,
  `notes`        text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`   varchar(64)  DEFAULT NULL,
  `updated_by`   varchar(64)  DEFAULT NULL,
  `is_deleted`   tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`alias_id`),
  KEY `ix_character_aliases_character` (`character_id`),
  KEY `ix_character_aliases_name`      (`name`),
  CONSTRAINT `fk_character_aliases_character` FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- character_voice_castings ---------------------------------------------------
-- キャラクター ⇄ 声優のキャスティング情報。
-- casting_kind:
--   REGULAR    … 標準のレギュラー担当（1 character につき通常 1 person）
--   SUBSTITUTE … 代役（病気・スケジュール都合等）。期間限定
--   TEMPORARY  … 引き継ぎ・交代後の暫定担当
--   MOB        … 1 話限りのモブ等への当て込み
-- valid_from / valid_to で期間を限定可能。声優交代の節目を valid_from で記録する。
CREATE TABLE IF NOT EXISTS `character_voice_castings` (
  `casting_id`    int                                                                NOT NULL AUTO_INCREMENT,
  `character_id`  int                                                                NOT NULL,
  `person_id`     int                                                                NOT NULL,
  `casting_kind`  ENUM('REGULAR','SUBSTITUTE','TEMPORARY','MOB') NOT NULL DEFAULT 'REGULAR',
  `valid_from`    date  DEFAULT NULL,
  `valid_to`      date  DEFAULT NULL,
  `notes`         text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  `is_deleted`    tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`casting_id`),
  KEY `ix_cvc_character` (`character_id`),
  KEY `ix_cvc_person`    (`person_id`),
  KEY `ix_cvc_kind`      (`casting_kind`),
  CONSTRAINT `fk_cvc_character` FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_cvc_person`    FOREIGN KEY (`person_id`)    REFERENCES `persons`    (`person_id`)    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =============================================================================
-- STEP 3: 役職マスタ・シリーズ別書式上書き
-- =============================================================================

-- -- roles ----------------------------------------------------------------------
-- クレジット内の役職マスタ。
-- role_format_kind:
--   NORMAL       … 単純な「役職: 名義列」（例: 脚本、演出、作画監督）
--   SERIAL       … 連載。format_template でシリーズ別表記が異なる
--   THEME_SONG   … 主題歌。entry が song_recording と label company_alias を持つ
--   VOICE_CAST   … 声の出演。entry がキャラクター名義 + 人物名義のペアを持つ
--   COMPANY_ONLY … 制作著作・製作協力等、企業のみが並ぶ役職
--   LOGO_ONLY    … ロゴのみが並ぶ役職（制作著作ロゴ単独表示等）
-- default_format_template は NORMAL/SERIAL のときに使うテンプレ文字列のデフォルト。
-- シリーズ別の上書きは series_role_format_overrides で行う。
CREATE TABLE IF NOT EXISTS `roles` (
  `role_code`               varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja`                 varchar(64)  NOT NULL,
  `name_en`                 varchar(64)  DEFAULT NULL,
  `role_format_kind`        ENUM('NORMAL','SERIAL','THEME_SONG','VOICE_CAST','COMPANY_ONLY','LOGO_ONLY') NOT NULL DEFAULT 'NORMAL',
  `default_format_template` varchar(255) DEFAULT NULL,
  `display_order`           smallint unsigned DEFAULT NULL,
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`role_code`),
  UNIQUE KEY `uq_roles_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 初期役職データ（典型的なクレジット項目を投入）。
-- display_order は飛び番（10 単位）で並べ、後から間に追加できるようにする。
INSERT IGNORE INTO `roles` (`role_code`,`name_ja`,`name_en`,`role_format_kind`,`default_format_template`,`display_order`) VALUES
  ('ORIGINAL_WORK',   '原作',                'Original Work',                 'NORMAL',       NULL,            10),
  ('SERIAL',          '連載',                'Serialization',                 'SERIAL',       '{name}',        20),
  ('SERIES_DIRECTOR', 'シリーズディレクター','Series Director',                'NORMAL',       NULL,            30),
  ('SERIES_COMPOSER', 'シリーズ構成',        'Series Composition',            'NORMAL',       NULL,            40),
  ('CHARACTER_DESIGN','キャラクターデザイン','Character Design',              'NORMAL',       NULL,            50),
  ('ART_DIRECTOR',    '美術監督',            'Art Director',                  'NORMAL',       NULL,            60),
  ('COLOR_DESIGN',    '色彩設計',            'Color Design',                  'NORMAL',       NULL,            70),
  ('PHOTO_DIRECTOR',  '撮影監督',            'Director of Photography',       'NORMAL',       NULL,            80),
  ('EDITOR',          '編集',                'Editor',                        'NORMAL',       NULL,            90),
  ('SOUND_DIRECTOR',  '音響監督',            'Sound Director',                'NORMAL',       NULL,           100),
  ('MUSIC',           '音楽',                'Music',                         'NORMAL',       NULL,           110),
  ('OP_THEME',        'オープニング主題歌',  'Opening Theme',                 'THEME_SONG',   NULL,           120),
  ('ED_THEME',        'エンディング主題歌',  'Ending Theme',                  'THEME_SONG',   NULL,           130),
  ('INSERT_THEME',    '挿入歌',              'Insert Song',                   'THEME_SONG',   NULL,           140),
  ('SCRIPT',          '脚本',                'Script',                        'NORMAL',       NULL,           150),
  ('STORYBOARD',      '絵コンテ',            'Storyboard',                    'NORMAL',       NULL,           160),
  ('EPISODE_DIRECTOR','演出',                'Episode Director',              'NORMAL',       NULL,           170),
  ('ANIMATION_DIR',   '作画監督',            'Animation Director',            'NORMAL',       NULL,           180),
  ('VOICE_CAST',      '声の出演',            'Voice Cast',                    'VOICE_CAST',   NULL,           190),
  ('PRODUCER',        'プロデューサー',      'Producer',                      'NORMAL',       NULL,           200),
  ('PRODUCTION',      '制作',                'Production',                    'COMPANY_ONLY', NULL,           210),
  ('PRODUCTION_COOP', '製作協力',            'Production Cooperation',        'COMPANY_ONLY', NULL,           220),
  ('PRODUCTION_AUTH', '制作著作',            'Production / Copyright',        'COMPANY_ONLY', NULL,           230),
  ('PRESENTED_BY',    '製作',                'Presented by',                  'COMPANY_ONLY', NULL,           240),
  ('LABEL',           'レーベル',            'Label',                         'COMPANY_ONLY', NULL,           250),
  ('LOGO',            'ロゴ',                'Logo',                          'LOGO_ONLY',    NULL,           260);


-- -- series_role_format_overrides ----------------------------------------------
-- シリーズ × 役職ごとの書式上書き。SERIAL ロールの「漫画・{name}」のような
-- シリーズ依存の表記を集約管理する。同一 (series, role) でシリーズ途中の表記変更を
-- 許すため (valid_from, valid_to) を主キーに含める。
-- valid_from が NULL の行は「期間境界なし（シリーズ全体に適用）」とみなす。
CREATE TABLE IF NOT EXISTS `series_role_format_overrides` (
  `series_id`        int                                                                  NOT NULL,
  `role_code`        varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin                NOT NULL,
  `valid_from`       date NOT NULL DEFAULT '1900-01-01',
  `valid_to`         date          DEFAULT NULL,
  `format_template`  varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  NOT NULL,
  `notes`            text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64)  DEFAULT NULL,
  `updated_by`       varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`series_id`,`role_code`,`valid_from`),
  KEY `ix_srfo_role` (`role_code`),
  CONSTRAINT `fk_srfo_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_srfo_role`   FOREIGN KEY (`role_code`) REFERENCES `roles`  (`role_code`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_srfo_dates`  CHECK ((`valid_to` IS NULL) OR (`valid_from` <= `valid_to`))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =============================================================================
-- STEP 4: クレジット本体（カード／ロール／ブロック／エントリ）
-- =============================================================================

-- -- credits --------------------------------------------------------------------
-- クレジット 1 件 = 1 行。シリーズ単位 or エピソード単位で OP/ED 各 1 件まで。
-- scope_kind = 'SERIES'  なら series_id  必須・episode_id NULL
-- scope_kind = 'EPISODE' なら episode_id 必須・series_id  NULL
-- part_type を NULL にすると「規定位置（part_types.default_credit_kind が credit_kind と
-- 一致するパート）で流れる」を意味する。OP/ED が他パート（CM 跨ぎ後の Bパート 等）で
-- 流れる稀ケースのみ part_type に値を入れる。
--
-- なお scope_kind と series_id / episode_id の整合性は、本来 CHECK 制約で
-- 表現したいところだが、MySQL 8.0 では「ON DELETE CASCADE / SET NULL の
-- 参照アクションを持つ FK が参照する列」を CHECK 制約に含めることができない
-- （Error 3823）。よって整合性チェックは下流の STEP 6 で BEFORE INSERT/UPDATE
-- トリガー (trg_credits_b{i,u}_scope_consistency) として実装している
-- （tracks テーブルと同じ運用パターン）。
CREATE TABLE IF NOT EXISTS `credits` (
  `credit_id`    int                                                          NOT NULL AUTO_INCREMENT,
  `scope_kind`   ENUM('SERIES','EPISODE')                                     NOT NULL,
  `series_id`    int                                                          DEFAULT NULL,
  `episode_id`   int                                                          DEFAULT NULL,
  `credit_kind`  ENUM('OP','ED')                                              NOT NULL,
  `part_type`    varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin        DEFAULT NULL,
  `presentation` ENUM('CARDS','ROLL')                                         NOT NULL DEFAULT 'CARDS',
  `notes`        text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`   varchar(64)  DEFAULT NULL,
  `updated_by`   varchar(64)  DEFAULT NULL,
  `is_deleted`   tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`credit_id`),
  -- シリーズスコープ用 UNIQUE。series_id が NULL の行（=エピソードスコープ行）は
  -- NULL 同士の重複を許す MySQL の挙動により制約に抵触しない。
  UNIQUE KEY `uq_credit_series_kind` (`series_id`,`credit_kind`),
  -- エピソードスコープ用 UNIQUE。同様の理由で、series 行には影響しない。
  UNIQUE KEY `uq_credit_episode_kind` (`episode_id`,`credit_kind`),
  KEY `ix_credit_part_type` (`part_type`),
  CONSTRAINT `fk_credits_series`    FOREIGN KEY (`series_id`)  REFERENCES `series`     (`series_id`)  ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_credits_episode`   FOREIGN KEY (`episode_id`) REFERENCES `episodes`   (`episode_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_credits_part_type` FOREIGN KEY (`part_type`)  REFERENCES `part_types` (`part_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_cards ---------------------------------------------------------------
-- クレジット内のカード 1 枚 = 1 行。presentation = 'ROLL' のクレジットでは card_seq=1 の
-- 1 行のみが立ち、その下に複数の役職／ブロックがぶら下がる。
CREATE TABLE IF NOT EXISTS `credit_cards` (
  `card_id`    int             NOT NULL AUTO_INCREMENT,
  `credit_id`  int             NOT NULL,
  `card_seq`   tinyint unsigned NOT NULL,
  `notes`      text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64)  DEFAULT NULL,
  `updated_by` varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_id`),
  UNIQUE KEY `uq_credit_cards_credit_seq` (`credit_id`,`card_seq`),
  CONSTRAINT `fk_credit_cards_credit` FOREIGN KEY (`credit_id`) REFERENCES `credits` (`credit_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_credit_cards_seq_pos` CHECK (`card_seq` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_card_roles ----------------------------------------------------------
-- カード内に登場する役職 1 つ = 1 行。tier=1（上段）／2（下段）+ order_in_tier で
-- カード内のレイアウト位置を保持する。横一列のみのカードは tier=1 のみが立つ。
-- role_code を NULL にすると「ブランクロール（ロゴ単独表示用の枠）」となる。
CREATE TABLE IF NOT EXISTS `credit_card_roles` (
  `card_role_id`   int                                                   NOT NULL AUTO_INCREMENT,
  `card_id`        int                                                   NOT NULL,
  `role_code`      varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `tier`           tinyint unsigned                                      NOT NULL DEFAULT 1,
  `order_in_tier`  tinyint unsigned                                      NOT NULL,
  `notes`          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64)  DEFAULT NULL,
  `updated_by`     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_role_id`),
  UNIQUE KEY `uq_card_role_pos` (`card_id`,`tier`,`order_in_tier`),
  KEY `ix_card_role_role` (`role_code`),
  CONSTRAINT `fk_card_role_card` FOREIGN KEY (`card_id`)   REFERENCES `credit_cards` (`card_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_card_role_role` FOREIGN KEY (`role_code`) REFERENCES `roles`        (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_card_role_tier`         CHECK (`tier` BETWEEN 1 AND 2),
  CONSTRAINT `ck_card_role_order_pos`    CHECK (`order_in_tier` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_role_blocks ---------------------------------------------------------
-- 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロック。
-- rows × cols は表示の枠（左→右、行が埋まれば次の行）。
-- leading_company_alias_id にはブロック先頭に企業名を出すケースの企業名義を入れる。
CREATE TABLE IF NOT EXISTS `credit_role_blocks` (
  `block_id`                  int             NOT NULL AUTO_INCREMENT,
  `card_role_id`              int             NOT NULL,
  `block_seq`                 tinyint unsigned NOT NULL,
  `rows`                      tinyint unsigned NOT NULL DEFAULT 1,
  `cols`                      tinyint unsigned NOT NULL DEFAULT 1,
  `leading_company_alias_id`  int             DEFAULT NULL,
  `notes`                     text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                varchar(64)  DEFAULT NULL,
  `updated_by`                varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`block_id`),
  UNIQUE KEY `uq_block_card_role_seq` (`card_role_id`,`block_seq`),
  KEY `ix_block_lead_company` (`leading_company_alias_id`),
  CONSTRAINT `fk_block_card_role`    FOREIGN KEY (`card_role_id`)             REFERENCES `credit_card_roles` (`card_role_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_block_lead_company` FOREIGN KEY (`leading_company_alias_id`) REFERENCES `company_aliases`   (`alias_id`)     ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_block_seq_pos`     CHECK (`block_seq` >= 1),
  CONSTRAINT `ck_block_rows_pos`    CHECK (`rows` >= 1),
  CONSTRAINT `ck_block_cols_pos`    CHECK (`cols` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_block_entries -------------------------------------------------------
-- ブロック内のエントリ 1 つ = 1 行。entry_kind に応じて参照先カラムが決まる:
--   PERSON          → person_alias_id
--   CHARACTER_VOICE → character_alias_id + person_alias_id（声の出演用ペア）
--                     character_alias_id を埋めずに raw_character_text で代用も可（モブ等）
--   COMPANY         → company_alias_id
--   LOGO            → logo_id
--   SONG            → song_recording_id（主題歌等）
--   TEXT            → raw_text（マスタ未登録のフリーテキスト退避口）
-- 詳細整合性は STEP 6 の trigger で担保する。
-- affiliation_company_alias_id / affiliation_text は人物名義の小カッコ所属を
-- 表現する補助カラム（マスタ参照 or テキスト）。
-- parallel_with_entry_id は「A / B」併記の相手 entry_id を自参照する任意フィールド。
CREATE TABLE IF NOT EXISTS `credit_block_entries` (
  `entry_id`                       int             NOT NULL AUTO_INCREMENT,
  `block_id`                       int             NOT NULL,
  `entry_seq`                      smallint unsigned NOT NULL,
  `entry_kind`                     ENUM('PERSON','CHARACTER_VOICE','COMPANY','LOGO','SONG','TEXT') NOT NULL,
  `person_alias_id`                int             DEFAULT NULL,
  `character_alias_id`             int             DEFAULT NULL,
  `raw_character_text`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `company_alias_id`               int             DEFAULT NULL,
  `logo_id`                        int             DEFAULT NULL,
  `song_recording_id`              int             DEFAULT NULL,
  `raw_text`                       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_company_alias_id`   int             DEFAULT NULL,
  `affiliation_text`               varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `parallel_with_entry_id`         int             DEFAULT NULL,
  `notes`                          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                     varchar(64)  DEFAULT NULL,
  `updated_by`                     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`entry_id`),
  UNIQUE KEY `uq_block_entries_block_seq` (`block_id`,`entry_seq`),
  KEY `ix_be_person`         (`person_alias_id`),
  KEY `ix_be_character`      (`character_alias_id`),
  KEY `ix_be_company`        (`company_alias_id`),
  KEY `ix_be_logo`           (`logo_id`),
  KEY `ix_be_song_recording` (`song_recording_id`),
  KEY `ix_be_aff_company`    (`affiliation_company_alias_id`),
  KEY `ix_be_parallel`       (`parallel_with_entry_id`),
  CONSTRAINT `fk_be_block`             FOREIGN KEY (`block_id`)                     REFERENCES `credit_role_blocks`   (`block_id`)          ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_be_person_alias`      FOREIGN KEY (`person_alias_id`)              REFERENCES `person_aliases`       (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_character_alias`   FOREIGN KEY (`character_alias_id`)           REFERENCES `character_aliases`    (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_company_alias`     FOREIGN KEY (`company_alias_id`)             REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_logo`              FOREIGN KEY (`logo_id`)                      REFERENCES `logos`                (`logo_id`)           ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_song_recording`    FOREIGN KEY (`song_recording_id`)            REFERENCES `song_recordings`      (`song_recording_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_aff_company_alias` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_parallel`          FOREIGN KEY (`parallel_with_entry_id`)       REFERENCES `credit_block_entries` (`entry_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_be_seq_pos` CHECK (`entry_seq` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =============================================================================
-- STEP 5: エピソード主題歌の登録テーブル
-- =============================================================================
-- 各エピソードに紐づく OP 主題歌（最大 1）／ED 主題歌（最大 1）／挿入歌（複数可）。
-- クレジットの THEME_SONG ロールエントリは、このテーブルから歌情報を引いて
-- レンダリングする想定。エントリ側で song_recording_id を直接持つ運用も可能。
CREATE TABLE IF NOT EXISTS `episode_theme_songs` (
  `episode_id`              int                                          NOT NULL,
  `theme_kind`              ENUM('OP','ED','INSERT')                     NOT NULL,
  `insert_seq`              tinyint unsigned                             NOT NULL DEFAULT 0,
  `song_recording_id`       int                                          NOT NULL,
  `label_company_alias_id`  int                                          DEFAULT NULL,
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`episode_id`,`theme_kind`,`insert_seq`),
  KEY `ix_ets_song_recording` (`song_recording_id`),
  KEY `ix_ets_label_company`  (`label_company_alias_id`),
  CONSTRAINT `fk_ets_episode`        FOREIGN KEY (`episode_id`)             REFERENCES `episodes`        (`episode_id`)        ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_ets_song_recording` FOREIGN KEY (`song_recording_id`)      REFERENCES `song_recordings` (`song_recording_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_ets_label_company`  FOREIGN KEY (`label_company_alias_id`) REFERENCES `company_aliases` (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  -- OP/ED は insert_seq=0 の 1 行のみ。INSERT は seq=1,2,... と複数可。
  CONSTRAINT `ck_ets_op_ed_no_insert_seq` CHECK (
       (`theme_kind` IN ('OP','ED') AND `insert_seq` = 0)
    OR (`theme_kind` = 'INSERT'      AND `insert_seq` >= 1)
  )
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =============================================================================
-- STEP 6: トリガーで credits / credit_block_entries の整合性を担保
-- =============================================================================
-- MySQL 8.0 では「ON DELETE CASCADE / SET NULL の参照アクションを持つ FK が
-- 参照する列」を CHECK 制約に含められない（Error 3823）。
-- そのため scope_kind ⇄ series_id / episode_id の排他、および
-- credit_block_entries の entry_kind ⇄ 各参照列の整合性は、
-- いずれも BEFORE INSERT/UPDATE トリガーで担保する（tracks と同じパターン）。
-- =============================================================================

-- -- credits の scope 整合性 ----------------------------------------------------
DROP TRIGGER IF EXISTS `trg_credits_bi_scope_consistency`;
DROP TRIGGER IF EXISTS `trg_credits_bu_scope_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_credits_bi_scope_consistency`
BEFORE INSERT ON `credits`
FOR EACH ROW
BEGIN
  -- SERIES スコープのとき: series_id 必須・episode_id 禁止
  IF NEW.scope_kind = 'SERIES' AND (NEW.series_id IS NULL OR NEW.episode_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=SERIES requires series_id NOT NULL and episode_id NULL';
  END IF;
  -- EPISODE スコープのとき: episode_id 必須・series_id 禁止
  IF NEW.scope_kind = 'EPISODE' AND (NEW.episode_id IS NULL OR NEW.series_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=EPISODE requires episode_id NOT NULL and series_id NULL';
  END IF;
END;;

CREATE TRIGGER `trg_credits_bu_scope_consistency`
BEFORE UPDATE ON `credits`
FOR EACH ROW
BEGIN
  -- INSERT 用と同じロジックを UPDATE にも適用する。
  IF NEW.scope_kind = 'SERIES' AND (NEW.series_id IS NULL OR NEW.episode_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=SERIES requires series_id NOT NULL and episode_id NULL';
  END IF;
  IF NEW.scope_kind = 'EPISODE' AND (NEW.episode_id IS NULL OR NEW.series_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=EPISODE requires episode_id NOT NULL and series_id NULL';
  END IF;
END;;

DELIMITER ;


-- -- credit_block_entries の entry_kind 整合性 ----------------------------------
-- entry_kind に応じて、必須カラムと禁止カラムを決める。

DROP TRIGGER IF EXISTS `trg_credit_block_entries_bi_consistency`;
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bu_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_credit_block_entries_bi_consistency`
BEFORE INSERT ON `credit_block_entries`
FOR EACH ROW
BEGIN
  -- entry_kind 別の必須カラムチェック
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=SONG requires song_recording_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;

  -- entry_kind 別の禁止カラムチェック（無関係な参照を立てるのを禁止）
  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'SONG' AND NEW.song_recording_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: song_recording_id allowed only for entry_kind=SONG';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END;;

CREATE TRIGGER `trg_credit_block_entries_bu_consistency`
BEFORE UPDATE ON `credit_block_entries`
FOR EACH ROW
BEGIN
  -- INSERT 用と同じロジックを UPDATE にも適用する。
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=SONG requires song_recording_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;
  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'SONG' AND NEW.song_recording_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: song_recording_id allowed only for entry_kind=SONG';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END;;

DELIMITER ;


-- =============================================================================
-- STEP 7: セッション変数の復元
-- =============================================================================
SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;


-- =============================================================================
-- STEP 8: 確認用サマリ（参考出力）
-- =============================================================================
-- 本マイグレーション後のテーブル件数（空であれば 0 と表示される）。
SELECT
  (SELECT COUNT(*) FROM persons)                       AS persons,
  (SELECT COUNT(*) FROM person_aliases)                AS person_aliases,
  (SELECT COUNT(*) FROM person_alias_persons)          AS person_alias_persons,
  (SELECT COUNT(*) FROM companies)                     AS companies,
  (SELECT COUNT(*) FROM company_aliases)               AS company_aliases,
  (SELECT COUNT(*) FROM logos)                         AS logos,
  (SELECT COUNT(*) FROM characters)                    AS `characters`,
  (SELECT COUNT(*) FROM character_aliases)             AS character_aliases,
  (SELECT COUNT(*) FROM character_voice_castings)      AS character_voice_castings,
  (SELECT COUNT(*) FROM roles)                         AS roles,
  (SELECT COUNT(*) FROM series_role_format_overrides)  AS series_role_format_overrides,
  (SELECT COUNT(*) FROM credits)                       AS credits,
  (SELECT COUNT(*) FROM credit_cards)                  AS credit_cards,
  (SELECT COUNT(*) FROM credit_card_roles)             AS credit_card_roles,
  (SELECT COUNT(*) FROM credit_role_blocks)            AS credit_role_blocks,
  (SELECT COUNT(*) FROM credit_block_entries)          AS credit_block_entries,
  (SELECT COUNT(*) FROM episode_theme_songs)           AS episode_theme_songs;

-- Migration v1.2.0 completed
