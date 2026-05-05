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

-- 役職マスタの初期データはアプリケーション側（運用者）で投入する方針のため、
-- マイグレーションでは roles へのデータ INSERT は行わない。
-- スキーマ（テーブル定義）だけを用意し、中身は空のままにする。


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

-- -- credit_card_tiers ----------------------------------------------------------
-- カード内の Tier（段組）1 つ = 1 行。v1.2.0 工程 G で追加。
-- カード作成時に tier_no=1 を 1 行自動投入する運用（アプリ側で実装）。
CREATE TABLE IF NOT EXISTS `credit_card_tiers` (
  `card_tier_id`  int             NOT NULL AUTO_INCREMENT,
  `card_id`       int             NOT NULL,
  `tier_no`       tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_tier_id`),
  UNIQUE KEY `uq_card_tier` (`card_id`,`tier_no`),
  CONSTRAINT `fk_card_tier_card` FOREIGN KEY (`card_id`) REFERENCES `credit_cards` (`card_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_tier_no`   CHECK (`tier_no` BETWEEN 1 AND 2)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_card_groups ---------------------------------------------------------
-- Tier 内の Group（サブグループ）1 つ = 1 行。v1.2.0 工程 G で追加。
-- Tier 作成時に group_no=1 を 1 行自動投入する運用。
CREATE TABLE IF NOT EXISTS `credit_card_groups` (
  `card_group_id` int             NOT NULL AUTO_INCREMENT,
  `card_tier_id`  int             NOT NULL,
  `group_no`      tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_group_id`),
  UNIQUE KEY `uq_card_group` (`card_tier_id`,`group_no`),
  CONSTRAINT `fk_card_group_tier` FOREIGN KEY (`card_tier_id`) REFERENCES `credit_card_tiers` (`card_tier_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_group_no`   CHECK (`group_no` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_card_roles ----------------------------------------------------------
-- カード内に登場する役職 1 つ = 1 行。所属する Group を card_group_id で参照する。
-- レイアウト位置は Group（→ Tier → Card のチェーン）と order_in_group（グループ内左右順）で表現。
-- v1.2.0 工程 G で旧 (card_id, tier, group_in_tier, order_in_group) 4 列構成から、
-- card_group_id への単一 FK + order_in_group の 2 列構成へ刷新。
CREATE TABLE IF NOT EXISTS `credit_card_roles` (
  `card_role_id`   int                                                   NOT NULL AUTO_INCREMENT,
  `card_group_id`  int                                                   NOT NULL,
  `role_code`      varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `order_in_group` tinyint unsigned                                      NOT NULL,
  `notes`          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64)  DEFAULT NULL,
  `updated_by`     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_role_id`),
  UNIQUE KEY `uq_card_role_pos` (`card_group_id`,`order_in_group`),
  KEY `ix_card_role_role` (`role_code`),
  CONSTRAINT `fk_card_role_group` FOREIGN KEY (`card_group_id`) REFERENCES `credit_card_groups` (`card_group_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_card_role_role`  FOREIGN KEY (`role_code`)     REFERENCES `roles`              (`role_code`)     ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_card_role_order_pos` CHECK (`order_in_group` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_role_blocks ---------------------------------------------------------
-- 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロック。
-- row_count × col_count は表示の枠（左→右、行が埋まれば次の行）。
-- v1.2.0 工程 F-fix3 で旧 `rows` / `cols` から row_count / col_count にリネーム
-- （MySQL 8.0 で `ROWS` がウィンドウ関数用の予約語に追加されたため、SELECT 等で
--  バッククォート漏れによる構文エラーが起きやすかった）。
-- leading_company_alias_id にはブロック先頭に企業名を出すケースの企業名義を入れる。
CREATE TABLE IF NOT EXISTS `credit_role_blocks` (
  `block_id`                  int             NOT NULL AUTO_INCREMENT,
  `card_role_id`              int             NOT NULL,
  `block_seq`                 tinyint unsigned NOT NULL,
  `row_count`                 tinyint unsigned NOT NULL DEFAULT 1,
  `col_count`                 tinyint unsigned NOT NULL DEFAULT 1,
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
  CONSTRAINT `ck_block_seq_pos`         CHECK (`block_seq` >= 1),
  CONSTRAINT `ck_block_row_count_pos`   CHECK (`row_count` >= 1),
  CONSTRAINT `ck_block_col_count_pos`   CHECK (`col_count` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- -- credit_block_entries -------------------------------------------------------
-- ブロック内のエントリ 1 つ = 1 行。entry_kind に応じて参照先カラムが決まる:
--   PERSON          → person_alias_id
--   CHARACTER_VOICE → character_alias_id + person_alias_id（声の出演用ペア）
--                     character_alias_id を埋めずに raw_character_text で代用も可（モブ等）
--   COMPANY         → company_alias_id
--   LOGO            → logo_id
--   TEXT            → raw_text（マスタ未登録のフリーテキスト退避口）
-- v1.2.0 工程 H で SONG エントリ種別と song_recording_id 列を撤廃。
-- 主題歌は episode_theme_songs テーブルが真実の源泉となり、クレジット側では
-- 楽曲を参照しない（役職レベルのテンプレート展開時に episode_theme_songs を JOIN）。
-- 詳細整合性は STEP 6 の trigger で担保する。
-- affiliation_company_alias_id / affiliation_text は人物名義の小カッコ所属を
-- 表現する補助カラム（マスタ参照 or テキスト）。
-- parallel_with_entry_id は「A / B」併記の相手 entry_id を自参照する任意フィールド。
CREATE TABLE IF NOT EXISTS `credit_block_entries` (
  `entry_id`                       int             NOT NULL AUTO_INCREMENT,
  `block_id`                       int             NOT NULL,
  `entry_seq`                      smallint unsigned NOT NULL,
  `entry_kind`                     ENUM('PERSON','CHARACTER_VOICE','COMPANY','LOGO','TEXT') NOT NULL,
  `person_alias_id`                int             DEFAULT NULL,
  `character_alias_id`             int             DEFAULT NULL,
  `raw_character_text`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `company_alias_id`               int             DEFAULT NULL,
  `logo_id`                        int             DEFAULT NULL,
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
  KEY `ix_be_aff_company`    (`affiliation_company_alias_id`),
  KEY `ix_be_parallel`       (`parallel_with_entry_id`),
  CONSTRAINT `fk_be_block`             FOREIGN KEY (`block_id`)                     REFERENCES `credit_role_blocks`   (`block_id`)          ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_be_person_alias`      FOREIGN KEY (`person_alias_id`)              REFERENCES `person_aliases`       (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_character_alias`   FOREIGN KEY (`character_alias_id`)           REFERENCES `character_aliases`    (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_company_alias`     FOREIGN KEY (`company_alias_id`)             REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_logo`              FOREIGN KEY (`logo_id`)                      REFERENCES `logos`                (`logo_id`)           ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_aff_company_alias` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_parallel`          FOREIGN KEY (`parallel_with_entry_id`)       REFERENCES `credit_block_entries` (`entry_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_be_seq_pos` CHECK (`entry_seq` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- =============================================================================
-- STEP 5: エピソード主題歌の登録テーブル
-- =============================================================================
-- 各エピソードに紐づく OP 主題歌（最大 1）／ED 主題歌（最大 1）／挿入歌（複数可）。
-- クレジットの主題歌役職（roles.role_format_kind='THEME_SONG'）は、このテーブルから
-- 歌情報を JOIN で引いてテンプレ展開時にレンダリングする運用。
-- v1.2.0 工程 H 補修：レーベル会社（販売元）は本来クレジット表示専用の関心事であり、
-- 楽曲の事実とは独立しているため、label_company_alias_id 列・関連 FK・関連 INDEX は
-- 撤去（レーベル名は credit_block_entries の COMPANY エントリで保持する設計に整理）。
CREATE TABLE IF NOT EXISTS `episode_theme_songs` (
  `episode_id`              int                                          NOT NULL,
  `theme_kind`              ENUM('OP','ED','INSERT')                     NOT NULL,
  `insert_seq`              tinyint unsigned                             NOT NULL DEFAULT 0,
  `song_recording_id`       int                                          NOT NULL,
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`episode_id`,`theme_kind`,`insert_seq`),
  KEY `ix_ets_song_recording` (`song_recording_id`),
  CONSTRAINT `fk_ets_episode`        FOREIGN KEY (`episode_id`)             REFERENCES `episodes`        (`episode_id`)        ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_ets_song_recording` FOREIGN KEY (`song_recording_id`)      REFERENCES `song_recordings` (`song_recording_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
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
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END;;

DELIMITER ;


-- =============================================================================
-- STEP 7: episode_theme_songs に is_broadcast_only 列を追加し、PK を再構築する
--          （v1.2.0 工程 B' で追加。本放送限定の例外的な OP/ED 主題歌だけを
--           本放送フラグ=1 の追加行として持たせ、既定の全媒体共通行と区別する。
--           パッケージ版・配信版でも本放送と同じ主題歌が流れるのが大半なので、
--           デフォルトの 0 行が「本放送・Blu-ray・配信ともに同じ」を意味する）
-- =============================================================================
-- 旧仕様の release_context（BROADCAST/PACKAGE/STREAMING/OTHER の 4 値 ENUM）は
-- ユースケースに対して過剰だったため、TINYINT(1) フラグに置き換える。
-- ・既に release_context 列が存在する環境（v1.2.0 工程 B' 旧版を流した直後の DB）も
--   想定し、旧列を DROP したうえで新列を ADD する流れを冪等に組む。
-- ・PK は (episode_id, theme_kind, insert_seq) もしくは旧 4 列構成
--   (episode_id, release_context, theme_kind, insert_seq) を、新 4 列構成
--   (episode_id, is_broadcast_only, theme_kind, insert_seq) に作り直す。
-- ・冪等性: 列の存在は INFORMATION_SCHEMA.COLUMNS で、PK の列構成は
--   INFORMATION_SCHEMA.STATISTICS で確認してから ALTER する。
-- ・CHECK 制約 ck_ets_op_ed_no_insert_seq は theme_kind / insert_seq のみを参照しており
--   フラグ列・FK 参照アクション列とは無関係なので、そのまま残す。

-- 旧 release_context 列が残っていれば DROP（PK に含まれている場合は先に PK を取り外す）
SET @has_old_col_ets = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND COLUMN_NAME = 'release_context'
);
SET @pk_has_old_ctx_ets = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND INDEX_NAME = 'PRIMARY' AND COLUMN_NAME = 'release_context'
);
SET @stmt = IF(@has_old_col_ets = 1 AND @pk_has_old_ctx_ets = 1,
  'ALTER TABLE `episode_theme_songs`
     DROP PRIMARY KEY,
     ADD PRIMARY KEY (`episode_id`,`theme_kind`,`insert_seq`),
     DROP COLUMN `release_context`',
  IF(@has_old_col_ets = 1,
    'ALTER TABLE `episode_theme_songs` DROP COLUMN `release_context`',
    'SELECT ''episode_theme_songs.release_context not present. nothing to drop.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

-- 新 is_broadcast_only 列を追加（既に存在すればスキップ）
SET @has_new_col_ets = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@has_new_col_ets = 0,
  'ALTER TABLE `episode_theme_songs`
     ADD COLUMN `is_broadcast_only` TINYINT(1) NOT NULL DEFAULT 0 AFTER `episode_id`',
  'SELECT ''episode_theme_songs.is_broadcast_only already exists. skipping ADD COLUMN.'' AS msg'
);
PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;

-- PK 再構築（PK に is_broadcast_only が含まれていなければ作り直す）
SET @pk_has_flag_ets = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND INDEX_NAME = 'PRIMARY' AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@pk_has_flag_ets = 0,
  'ALTER TABLE `episode_theme_songs`
     DROP PRIMARY KEY,
     ADD PRIMARY KEY (`episode_id`,`is_broadcast_only`,`theme_kind`,`insert_seq`)',
  'SELECT ''episode_theme_songs PK already includes is_broadcast_only. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt;
EXECUTE _stmt;
DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8: 本放送・円盤の差し替えはエントリ単位で行う設計に再修正する
--          （v1.2.0 工程 B' 再修正）。
-- =============================================================================
-- 当初は credits.is_broadcast_only でクレジット単位に「本放送限定行」を持たせる設計
-- としたが、実態として本放送と円盤・配信で異なるのは「ロゴ画像のバージョン」と
-- 「主題歌」だけで、クレジット本体の役職構成までもが丸ごと差し替わるわけではない。
-- そこで以下のように再修正する:
--   ・credits.is_broadcast_only 列を削除（UNIQUE も 2 列に戻す）
--   ・代わりに credit_block_entries.is_broadcast_only 列を追加し、
--     UNIQUE を (block_id, is_broadcast_only, entry_seq) の 3 列に拡張
--   ・episode_theme_songs.is_broadcast_only は STEP 7 のまま（主題歌差し替えに使う）
--
-- フラグの意味（credit_block_entries 側）:
--   ・0 = 円盤・配信用エントリ。本放送では同位置に 1 行があればそちらが優先表示される。
--   ・1 = 本放送用エントリ。円盤・配信では無視される。
--   同じ (block_id, entry_seq) に 0 と 1 が並立する形で「本放送だけロゴが違う」等を表現。
--
-- マイグレーションは冪等:
--   ・credits.is_broadcast_only が存在すれば DROP（UNIQUE を 2 列に戻してから）
--   ・credit_block_entries.is_broadcast_only が無ければ ADD
--   ・credit_block_entries の UNIQUE が 2 列なら 3 列に再構築

-- ── 8-A: credits の is_broadcast_only を撤去（あれば） ──

-- まず credits の UNIQUE が is_broadcast_only を含む 3 列構成なら、2 列に戻す
SET @uq_series_has_flag = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits'
    AND INDEX_NAME = 'uq_credit_series_kind' AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@uq_series_has_flag = 1,
  'ALTER TABLE `credits`
     DROP INDEX `uq_credit_series_kind`,
     ADD UNIQUE KEY `uq_credit_series_kind` (`series_id`,`credit_kind`)',
  'SELECT ''credits.uq_credit_series_kind not 3-col with flag. skipping rollback.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @uq_ep_has_flag = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits'
    AND INDEX_NAME = 'uq_credit_episode_kind' AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@uq_ep_has_flag = 1,
  'ALTER TABLE `credits`
     DROP INDEX `uq_credit_episode_kind`,
     ADD UNIQUE KEY `uq_credit_episode_kind` (`episode_id`,`credit_kind`)',
  'SELECT ''credits.uq_credit_episode_kind not 3-col with flag. skipping rollback.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- credits.is_broadcast_only 列が存在すれば DROP
SET @has_col_cr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits'
    AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@has_col_cr = 1,
  'ALTER TABLE `credits` DROP COLUMN `is_broadcast_only`',
  'SELECT ''credits.is_broadcast_only not present. nothing to drop.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 旧 release_context 列の名残があれば一応 DROP（古い B' 旧版の環境向け）
SET @has_old_release_ctx = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credits'
    AND COLUMN_NAME = 'release_context'
);
SET @stmt = IF(@has_old_release_ctx = 1,
  'ALTER TABLE `credits` DROP COLUMN `release_context`',
  'SELECT ''credits.release_context not present. nothing to drop.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- ── 8-B: credit_block_entries に is_broadcast_only を追加し UNIQUE を 3 列化 ──

-- 列追加（既に存在すればスキップ）
SET @has_col_be = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@has_col_be = 0,
  'ALTER TABLE `credit_block_entries`
     ADD COLUMN `is_broadcast_only` TINYINT(1) NOT NULL DEFAULT 0 AFTER `block_id`',
  'SELECT ''credit_block_entries.is_broadcast_only already exists. skipping ADD COLUMN.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- UNIQUE (block_id, entry_seq) → (block_id, is_broadcast_only, entry_seq) に再構築
SET @uq_be_cols = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND INDEX_NAME = 'uq_block_entries_block_seq'
);
SET @uq_be_has_flag = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND INDEX_NAME = 'uq_block_entries_block_seq' AND COLUMN_NAME = 'is_broadcast_only'
);
SET @stmt = IF(@uq_be_cols = 2 AND @uq_be_has_flag = 0,
  'ALTER TABLE `credit_block_entries`
     DROP INDEX `uq_block_entries_block_seq`,
     ADD UNIQUE KEY `uq_block_entries_block_seq` (`block_id`,`is_broadcast_only`,`entry_seq`)',
  'SELECT ''credit_block_entries.uq_block_entries_block_seq already includes is_broadcast_only (or absent). skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-C: credit_card_roles に group_in_tier 列を追加し、order_in_tier を
--           order_in_group にリネームする（v1.2.0 工程 E）。
-- =============================================================================
-- 当初の credit_card_roles は (tier, order_in_tier) の 2 列で位置を保持していたが、
-- 工程 E で「同 tier 内のサブグループ番号」を表す group_in_tier を追加し、
-- 旧 order_in_tier を「同グループ内の左右順」を表す order_in_group にリネームした。
-- UNIQUE は (card_id, tier, group_in_tier, order_in_group) の 4 列複合になる。
--
-- 【ガード条件】v1.2.0 工程 G（STEP 8-F）で credit_card_roles の `tier` / `group_in_tier`
-- / `card_id` 列が既に削除されている場合、本ステップは旧スキーマ向けの処理で
-- もはや実行する意味がないため、まるごとスキップする。
-- 旧スキーマの `tier` 列が無くなっている = 工程 G が既に完了している、ということを意味する。
SET @ccr_has_tier_col_for_8c = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'tier'
);
-- 8-C 全体のスキップ判定。@skip_8c=1 のとき、本セクション内のすべての ALTER は no-op に置き換える。
SET @skip_8c = IF(@ccr_has_tier_col_for_8c = 0, 1, 0);

-- マイグレーションは冪等。実行順序は以下のとおり:
--   8-C-0: 旧 CHECK 制約 ck_card_role_order_pos を一旦 DROP
--          （MySQL 8.0 は CHECK が参照する列の RENAME を許さない Error 3959 のため、
--           RENAME より前に CHECK を外しておく必要がある）
--   8-C-1: order_in_tier → order_in_group の RENAME COLUMN
--   8-C-2: group_in_tier 列の ADD COLUMN（DEFAULT 1）
--   8-C-3: UNIQUE uq_card_role_pos を 4 列構成に再構築
--   8-C-4: CHECK 制約を再作成（order 用と group 用、それぞれ無ければ追加）

-- ── 8-C-0: 旧 CHECK ck_card_role_order_pos を DROP（あれば） ──
-- 旧定義は (`order_in_tier` >= 1) なので、そのままでは RENAME に失敗する。
-- いったん DROP し、8-C-4 で新定義 (`order_in_group` >= 1) として作り直す。
SET @has_old_check_ccr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_card_role_order_pos'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-0.'' AS msg',
  IF(@has_old_check_ccr = 1,
    'ALTER TABLE `credit_card_roles` DROP CHECK `ck_card_role_order_pos`',
    'SELECT ''credit_card_roles.ck_card_role_order_pos not present. skipping drop.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-C-1: order_in_tier を order_in_group に RENAME ──
SET @has_old_col_ccr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'order_in_tier'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-1.'' AS msg',
  IF(@has_old_col_ccr = 1,
    'ALTER TABLE `credit_card_roles` RENAME COLUMN `order_in_tier` TO `order_in_group`',
    'SELECT ''credit_card_roles.order_in_tier not present (already renamed). skipping.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-C-2: group_in_tier 列を追加 ──
SET @has_new_col_ccr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'group_in_tier'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-2.'' AS msg',
  IF(@has_new_col_ccr = 0,
    'ALTER TABLE `credit_card_roles`
       ADD COLUMN `group_in_tier` TINYINT UNSIGNED NOT NULL DEFAULT 1 AFTER `tier`',
    'SELECT ''credit_card_roles.group_in_tier already exists. skipping ADD COLUMN.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-C-3: UNIQUE uq_card_role_pos を 4 列に再構築 ──
SET @uq_ccr_has_group = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND INDEX_NAME = 'uq_card_role_pos' AND COLUMN_NAME = 'group_in_tier'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-3.'' AS msg',
  IF(@uq_ccr_has_group = 0,
    'ALTER TABLE `credit_card_roles`
       DROP INDEX `uq_card_role_pos`,
       ADD UNIQUE KEY `uq_card_role_pos` (`card_id`,`tier`,`group_in_tier`,`order_in_group`)',
    'SELECT ''credit_card_roles.uq_card_role_pos already 4-col. skipping rebuild.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-C-4-A: 新 CHECK ck_card_role_order_pos (order_in_group >= 1) を再作成 ──
SET @has_new_check_order_ccr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_card_role_order_pos'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-4-A.'' AS msg',
  IF(@has_new_check_order_ccr = 0,
    'ALTER TABLE `credit_card_roles` ADD CONSTRAINT `ck_card_role_order_pos` CHECK (`order_in_group` >= 1)',
    'SELECT ''credit_card_roles.ck_card_role_order_pos already exists. skipping.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-C-4-B: CHECK ck_card_role_group_pos (group_in_tier >= 1) を追加 ──
SET @has_group_check_ccr = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_card_role_group_pos'
);
SET @stmt = IF(@skip_8c = 1,
  'SELECT ''STEP 8-C: tier column already removed (Phase G applied). skipping 8-C-4-B.'' AS msg',
  IF(@has_group_check_ccr = 0,
    'ALTER TABLE `credit_card_roles` ADD CONSTRAINT `ck_card_role_group_pos` CHECK (`group_in_tier` >= 1)',
    'SELECT ''credit_card_roles.ck_card_role_group_pos already exists. skipping.'' AS msg'
  )
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-D: character_kinds マスタテーブルを新設し、characters.character_kind を
--           ENUM から FK に切り替える（v1.2.0 工程 F）。
-- =============================================================================
-- 旧仕様: characters.character_kind ENUM('MAIN','SUPPORT','GUEST','MOB','OTHER')
-- 新仕様: character_kinds マスタ表（PRECURE / ALLY / VILLAIN / SUPPORTING の 4 類型を初期投入）+
--         characters.character_kind は VARCHAR(32) として上記マスタを FK 参照
--
-- データはまだ空の前提なので、既存値の変換は行わない（FK 化前にいったん DEFAULT 'PRECURE' に揃える）。
-- すべて冪等。

-- ── 8-D-1: character_kinds マスタテーブル作成 ──
CREATE TABLE IF NOT EXISTS `character_kinds` (
  `character_kind` varchar(32)                                                       NOT NULL,
  `name_ja`        varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`        varchar(64)                                                         DEFAULT NULL,
  `display_order`  tinyint unsigned                                                    DEFAULT NULL,
  `notes`          text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64)  DEFAULT NULL,
  `updated_by`     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`character_kind`),
  UNIQUE KEY `uq_character_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ── 8-D-2: 4 類型の初期データを INSERT IGNORE で投入 ──
INSERT IGNORE INTO `character_kinds` (`character_kind`,`name_ja`,`name_en`,`display_order`) VALUES
  ('PRECURE',    'プリキュア',     'Precure',                10),
  ('ALLY',       '仲間たち',       'Allies',                 20),
  ('VILLAIN',    '敵',             'Villains',               30),
  ('SUPPORTING', 'とりまく人々',   'Supporting Characters',  40);

-- ── 8-D-3: characters.character_kind の型を ENUM → VARCHAR(32) に変更 ──
-- 既に VARCHAR(32) になっていれば SQL は成功するが冗長な書き換えになるので、
-- ENUM の場合のみ MODIFY を実行する。判定は INFORMATION_SCHEMA.COLUMNS.DATA_TYPE。
SET @col_type_is_enum = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND COLUMN_NAME = 'character_kind' AND DATA_TYPE = 'enum'
);
SET @stmt = IF(@col_type_is_enum = 1,
  'ALTER TABLE `characters` MODIFY COLUMN `character_kind` VARCHAR(32) NOT NULL DEFAULT ''PRECURE''',
  'SELECT ''characters.character_kind is already VARCHAR. skipping MODIFY.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-D-4: 旧 ENUM の既存値（'MAIN' 等）が残っていれば 'PRECURE' に置換 ──
-- データは空の前提だが、リハーサルで入れていた既存行に対する保険として実行する。
UPDATE `characters`
   SET `character_kind` = 'PRECURE'
 WHERE `character_kind` IN ('MAIN','SUPPORT','GUEST','MOB','OTHER');

-- ── 8-D-5: FK 制約 fk_characters_kind を追加 ──
SET @has_fk_chr_kind = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND CONSTRAINT_NAME = 'fk_characters_kind' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk_chr_kind = 0,
  'ALTER TABLE `characters` ADD CONSTRAINT `fk_characters_kind` FOREIGN KEY (`character_kind`) REFERENCES `character_kinds` (`character_kind`) ON DELETE RESTRICT ON UPDATE CASCADE',
  'SELECT ''characters.fk_characters_kind already exists. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-E: credit_role_blocks の `rows` / `cols` 列を row_count / col_count に
--           リネームする（v1.2.0 工程 F-fix3）。
-- =============================================================================
-- MySQL 8.0 で `ROWS` がウィンドウ関数用の予約語に追加されたため、SELECT 等で
-- バッククォート漏れによる構文エラーが起きやすかった（Dapper のエイリアスを
-- バッククォート無しで AS Rows と書くと予約語衝突）。
-- 列名そのものを衝突しない名前にすることで根本的に解消する。
-- 同時に `cols` も対称性のため col_count にリネームする（こちらは予約語ではないが、
-- 片方だけリネームすると名前の対が崩れて読みづらいため）。
--
-- マイグレーションは冪等。CHECK 制約は列名を参照するため、RENAME より先に
-- 旧 CHECK を DROP しておく必要がある（MySQL 8.0 Error 3959 回避、
-- credit_card_roles の改名と同じパターン）。

-- ── 8-E-0: 旧 CHECK ck_block_rows_pos / ck_block_cols_pos を DROP（あれば） ──
SET @has_old_check_rows_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_block_rows_pos'
);
SET @stmt = IF(@has_old_check_rows_crb = 1,
  'ALTER TABLE `credit_role_blocks` DROP CHECK `ck_block_rows_pos`',
  'SELECT ''credit_role_blocks.ck_block_rows_pos not present. skipping drop.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @has_old_check_cols_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_block_cols_pos'
);
SET @stmt = IF(@has_old_check_cols_crb = 1,
  'ALTER TABLE `credit_role_blocks` DROP CHECK `ck_block_cols_pos`',
  'SELECT ''credit_role_blocks.ck_block_cols_pos not present. skipping drop.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-E-1: rows を row_count に RENAME ──
SET @has_old_col_rows_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks'
    AND COLUMN_NAME = 'rows'
);
SET @stmt = IF(@has_old_col_rows_crb = 1,
  'ALTER TABLE `credit_role_blocks` RENAME COLUMN `rows` TO `row_count`',
  'SELECT ''credit_role_blocks.rows not present (already renamed). skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-E-2: cols を col_count に RENAME ──
SET @has_old_col_cols_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_role_blocks'
    AND COLUMN_NAME = 'cols'
);
SET @stmt = IF(@has_old_col_cols_crb = 1,
  'ALTER TABLE `credit_role_blocks` RENAME COLUMN `cols` TO `col_count`',
  'SELECT ''credit_role_blocks.cols not present (already renamed). skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-E-3: 新 CHECK ck_block_row_count_pos (row_count >= 1) を再作成 ──
SET @has_new_check_row_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_block_row_count_pos'
);
SET @stmt = IF(@has_new_check_row_crb = 0,
  'ALTER TABLE `credit_role_blocks` ADD CONSTRAINT `ck_block_row_count_pos` CHECK (`row_count` >= 1)',
  'SELECT ''credit_role_blocks.ck_block_row_count_pos already exists. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-E-4: 新 CHECK ck_block_col_count_pos (col_count >= 1) を再作成 ──
SET @has_new_check_col_crb = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_block_col_count_pos'
);
SET @stmt = IF(@has_new_check_col_crb = 0,
  'ALTER TABLE `credit_role_blocks` ADD CONSTRAINT `ck_block_col_count_pos` CHECK (`col_count` >= 1)',
  'SELECT ''credit_role_blocks.ck_block_col_count_pos already exists. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-F: credit_card_tiers / credit_card_groups テーブルを新設し、
--           credit_card_roles の (card_id, tier, group_in_tier, order_in_group) を
--           card_group_id への単一 FK + order_in_group の 2 列構成へ刷新する
--           （v1.2.0 工程 G）。
-- =============================================================================
-- 旧仕様: credit_card_roles に card_id / tier / group_in_tier / order_in_group の 4 列
--         （Tier や Group はカラム値の集約結果として「役職が存在するときだけ」表現される）
-- 新仕様: credit_card_tiers / credit_card_groups の 2 つの実体テーブルを新設し、
--         credit_card_roles は card_group_id を介して所属を表現
--         （Tier や Group は実体行として独立に存在でき、役職ゼロのブランク Tier / Group も保持できる）
--
-- データ移行の流れ（既存環境向け、すべて冪等）:
--   8-F-1: credit_card_tiers テーブル作成
--   8-F-2: credit_card_groups テーブル作成
--   8-F-3: 既存の credit_card_roles から (card_id, tier) の組合せを集約して credit_card_tiers へ INSERT IGNORE
--   8-F-4: 既存の credit_card_roles から (card_id, tier, group_in_tier) の組合せを集約して credit_card_groups へ INSERT IGNORE
--   8-F-5: credit_card_roles に card_group_id 列を追加（既存に列が無ければ）
--   8-F-6: 旧 (card_id, tier, group_in_tier) で credit_card_groups を引いて card_group_id を埋める
--   8-F-7: 既存のカードで Tier=1 / Group=1 を持っていない（= 役職ゼロのカード）が無いことを担保するため、
--          credit_cards 全件に対して Tier=1 / Group=1 が無ければ INSERT IGNORE で投入
--   8-F-8: credit_card_roles の旧 UNIQUE / 旧 CHECK / 旧 FK / 旧列 (card_id, tier, group_in_tier) を削除
--   8-F-9: 新 UNIQUE (card_group_id, order_in_group) と 新 FK fk_card_role_group を追加
--   8-F-10: card_group_id を NOT NULL 化

-- ── 8-F-1: credit_card_tiers テーブル作成 ──
CREATE TABLE IF NOT EXISTS `credit_card_tiers` (
  `card_tier_id`  int             NOT NULL AUTO_INCREMENT,
  `card_id`       int             NOT NULL,
  `tier_no`       tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_tier_id`),
  UNIQUE KEY `uq_card_tier` (`card_id`,`tier_no`),
  CONSTRAINT `fk_card_tier_card` FOREIGN KEY (`card_id`) REFERENCES `credit_cards` (`card_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_tier_no`   CHECK (`tier_no` BETWEEN 1 AND 2)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ── 8-F-2: credit_card_groups テーブル作成 ──
CREATE TABLE IF NOT EXISTS `credit_card_groups` (
  `card_group_id` int             NOT NULL AUTO_INCREMENT,
  `card_tier_id`  int             NOT NULL,
  `group_no`      tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_group_id`),
  UNIQUE KEY `uq_card_group` (`card_tier_id`,`group_no`),
  CONSTRAINT `fk_card_group_tier` FOREIGN KEY (`card_tier_id`) REFERENCES `credit_card_tiers` (`card_tier_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_group_no`   CHECK (`group_no` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ── 8-F-3: 既存の credit_card_roles から (card_id, tier) の組合せを集約して credit_card_tiers へ ──
-- 旧構成 (card_id, tier 列を持つ) のときだけ実行する。
SET @ccr_has_tier_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'tier'
);
SET @stmt = IF(@ccr_has_tier_col = 1,
  'INSERT IGNORE INTO `credit_card_tiers` (`card_id`,`tier_no`)
   SELECT DISTINCT `card_id`, `tier` FROM `credit_card_roles`',
  'SELECT ''credit_card_roles.tier already removed. skipping tier seed.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-4: 既存の credit_card_roles から (card_id, tier, group_in_tier) を集約して credit_card_groups へ ──
SET @ccr_has_grp_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'group_in_tier'
);
SET @stmt = IF(@ccr_has_grp_col = 1,
  'INSERT IGNORE INTO `credit_card_groups` (`card_tier_id`,`group_no`)
   SELECT DISTINCT t.`card_tier_id`, r.`group_in_tier`
   FROM `credit_card_roles` r
   JOIN `credit_card_tiers`  t ON t.`card_id` = r.`card_id` AND t.`tier_no` = r.`tier`',
  'SELECT ''credit_card_roles.group_in_tier already removed. skipping group seed.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-5: credit_card_roles に card_group_id 列を追加 ──
SET @ccr_has_group_id_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'card_group_id'
);
SET @stmt = IF(@ccr_has_group_id_col = 0,
  'ALTER TABLE `credit_card_roles` ADD COLUMN `card_group_id` INT NULL AFTER `card_role_id`',
  'SELECT ''credit_card_roles.card_group_id already exists. skipping ADD COLUMN.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-6: 旧 (card_id, tier, group_in_tier) から credit_card_groups を引いて card_group_id を埋める ──
-- 旧列がまだ残っていて、かつ card_group_id が NULL の行を対象に UPDATE。
SET @ccr_has_old_cols = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME IN ('card_id','tier','group_in_tier')
);
SET @stmt = IF(@ccr_has_old_cols = 3,
  'UPDATE `credit_card_roles` r
   JOIN `credit_card_tiers`  t ON t.`card_id` = r.`card_id` AND t.`tier_no` = r.`tier`
   JOIN `credit_card_groups` g ON g.`card_tier_id` = t.`card_tier_id` AND g.`group_no` = r.`group_in_tier`
   SET r.`card_group_id` = g.`card_group_id`
   WHERE r.`card_group_id` IS NULL',
  'SELECT ''credit_card_roles old columns already removed. skipping UPDATE join.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-7: 全カードに Tier=1 / Group=1 が存在することを保証（ブランクカード対応） ──
-- 工程 G の新仕様では「カード作成時に Tier=1 / Group=1 が自動投入される」が、
-- 既存環境では「役職ゼロのカード」があれば Tier / Group が無いままになっている可能性があるため、
-- ここで漏れを INSERT IGNORE で補填する。
INSERT IGNORE INTO `credit_card_tiers` (`card_id`,`tier_no`)
SELECT c.`card_id`, 1 FROM `credit_cards` c
LEFT JOIN `credit_card_tiers` t ON t.`card_id` = c.`card_id` AND t.`tier_no` = 1
WHERE t.`card_tier_id` IS NULL;

INSERT IGNORE INTO `credit_card_groups` (`card_tier_id`,`group_no`)
SELECT t.`card_tier_id`, 1 FROM `credit_card_tiers` t
LEFT JOIN `credit_card_groups` g ON g.`card_tier_id` = t.`card_tier_id` AND g.`group_no` = 1
WHERE g.`card_group_id` IS NULL;

-- ── 8-F-8: 旧構成（FK / UNIQUE / CHECK / 列）の削除 ──
-- 順序が重要：旧 UNIQUE `uq_card_role_pos` は card_id を含む 4 列構成のため、
-- credit_card_roles.card_id を子側とする FK `fk_card_role_card` がそれを必要とする
-- インデックスとして解釈してしまい、UNIQUE を先に DROP しようとすると 1553 で失敗する。
-- 必ず先に FK を外してから UNIQUE を外す。
--   1) 旧 FK fk_card_role_card を DROP
--   2) 旧 UNIQUE uq_card_role_pos（4 列）を DROP
--   3) 旧 CHECK 群を DROP
--   4) 旧列 (card_id, tier, group_in_tier) を DROP

-- 1) 旧 FK fk_card_role_card を DROP
SET @has_fk_card = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND CONSTRAINT_NAME = 'fk_card_role_card' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk_card = 1,
  'ALTER TABLE `credit_card_roles` DROP FOREIGN KEY `fk_card_role_card`',
  'SELECT ''credit_card_roles.fk_card_role_card already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 2) 旧 UNIQUE uq_card_role_pos が 4 列構成なら DROP（新 UNIQUE は 8-F-9 で再作成）
SET @uq_old_col_count = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND INDEX_NAME = 'uq_card_role_pos'
);
SET @uq_has_card_id = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND INDEX_NAME = 'uq_card_role_pos' AND COLUMN_NAME = 'card_id'
);
SET @stmt = IF(@uq_old_col_count > 0 AND @uq_has_card_id = 1,
  'ALTER TABLE `credit_card_roles` DROP INDEX `uq_card_role_pos`',
  'SELECT ''credit_card_roles.uq_card_role_pos already new shape (or absent). skipping drop.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 3) 旧 CHECK ck_card_role_tier / ck_card_role_group_pos を DROP
SET @has_check_tier = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_card_role_tier'
);
SET @stmt = IF(@has_check_tier = 1,
  'ALTER TABLE `credit_card_roles` DROP CHECK `ck_card_role_tier`',
  'SELECT ''credit_card_roles.ck_card_role_tier already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @has_check_group_pos = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.CHECK_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'ck_card_role_group_pos'
);
SET @stmt = IF(@has_check_group_pos = 1,
  'ALTER TABLE `credit_card_roles` DROP CHECK `ck_card_role_group_pos`',
  'SELECT ''credit_card_roles.ck_card_role_group_pos already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- 4) 旧列（card_id, tier, group_in_tier）を DROP
SET @ccr_has_tier_col2 = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'tier'
);
SET @stmt = IF(@ccr_has_tier_col2 = 1,
  'ALTER TABLE `credit_card_roles` DROP COLUMN `tier`',
  'SELECT ''credit_card_roles.tier already dropped. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @ccr_has_grp_col2 = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'group_in_tier'
);
SET @stmt = IF(@ccr_has_grp_col2 = 1,
  'ALTER TABLE `credit_card_roles` DROP COLUMN `group_in_tier`',
  'SELECT ''credit_card_roles.group_in_tier already dropped. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @ccr_has_card_id_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'card_id'
);
SET @stmt = IF(@ccr_has_card_id_col = 1,
  'ALTER TABLE `credit_card_roles` DROP COLUMN `card_id`',
  'SELECT ''credit_card_roles.card_id already dropped. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-9: 新 UNIQUE / 新 FK の追加 ──
SET @uq_new_exists = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND INDEX_NAME = 'uq_card_role_pos' AND COLUMN_NAME = 'card_group_id'
);
SET @stmt = IF(@uq_new_exists = 0,
  'ALTER TABLE `credit_card_roles` ADD UNIQUE KEY `uq_card_role_pos` (`card_group_id`,`order_in_group`)',
  'SELECT ''credit_card_roles.uq_card_role_pos already new shape. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

SET @has_fk_group = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND CONSTRAINT_NAME = 'fk_card_role_group' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk_group = 0,
  'ALTER TABLE `credit_card_roles` ADD CONSTRAINT `fk_card_role_group` FOREIGN KEY (`card_group_id`) REFERENCES `credit_card_groups` (`card_group_id`) ON DELETE CASCADE ON UPDATE CASCADE',
  'SELECT ''credit_card_roles.fk_card_role_group already exists. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-F-10: card_group_id を NOT NULL 化 ──
SET @group_id_is_nullable = (
  SELECT IF(IS_NULLABLE = 'YES', 1, 0)
  FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles'
    AND COLUMN_NAME = 'card_group_id'
);
SET @stmt = IF(@group_id_is_nullable = 1,
  'ALTER TABLE `credit_card_roles` MODIFY COLUMN `card_group_id` INT NOT NULL',
  'SELECT ''credit_card_roles.card_group_id already NOT NULL. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-G: credit_block_entries から SONG エントリ種別を物理削除する
--           （v1.2.0 工程 H）。
-- =============================================================================
-- 設計判断：主題歌は episode_theme_songs テーブルが「真実の源泉」であり、
-- クレジット側で楽曲を再指定するのは二重管理になるため、SONG 種別を撤廃する。
-- 主題歌役職（role_format_kind='THEME_SONG'）の表示時には episode_theme_songs を
-- JOIN で引いてアプリケーション側でレンダリングする運用に切り替える。
--
-- 処理の流れ（すべて冪等）:
--   8-G-1: 既存の entry_kind='SONG' 行を物理削除
--   8-G-2: トリガ trg_credit_block_entries_bi_consistency / _bu_consistency を
--          SONG 分岐を含まない版で再作成（DROP → CREATE）
--   8-G-3: credit_block_entries.entry_kind の ENUM 値リストから 'SONG' を除去
--   8-G-4: credit_block_entries.fk_be_song_recording FK を DROP
--   8-G-5: credit_block_entries.song_recording_id 列を DROP

-- ── 8-G-1: 既存の SONG 行を物理削除 ──
-- 0 行でも 1 行でも DELETE は安全に動く。
DELETE FROM `credit_block_entries` WHERE `entry_kind` = 'SONG';

-- ── 8-G-2: トリガを SONG 分岐なしで再作成 ──
-- 既存トリガ名は trg_credit_block_entries_bi_consistency / _bu_consistency。
-- DROP TRIGGER IF EXISTS は冪等。
-- CHARACTER_VOICE は person_alias_id を「声優側の名義」として共用する仕様（独立した
-- voice_person_alias_id 列は持たない）に注意。
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bi_consistency`;
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bu_consistency`;

-- 既存環境では entry_kind 列がまだ ENUM('...','SONG',...) のままだが、
-- INSERT / UPDATE 時にアプリ側が SONG を投入することはもう無いので、
-- トリガは SONG 分岐を持たない形で先に作成しても整合する
-- （後続の 8-G-3 で ENUM 自体から SONG を除去）。
-- 本トリガの定義は db/schema.sql に同名で記述されているもの（v1.2.0 工程 H 時点）と一致させる。
DELIMITER //
CREATE TRIGGER `trg_credit_block_entries_bi_consistency`
BEFORE INSERT ON `credit_block_entries`
FOR EACH ROW
BEGIN
  -- 必須参照のチェック（entry_kind ごと）
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
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;

  -- entry_kind 別の禁止カラムチェック（無関係な参照を立てるのを禁止）
  -- person_alias_id は PERSON / CHARACTER_VOICE 共用なので、両者以外で立っていたらエラー。
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
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END//

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
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END//
DELIMITER ;

-- ── 8-G-3: entry_kind ENUM から 'SONG' を除去 ──
-- 既に SONG 行は 8-G-1 で削除済みなので、ENUM 値リストの差し替えは安全。
SET @entry_kind_has_song = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND COLUMN_NAME = 'entry_kind'
    AND COLUMN_TYPE LIKE '%''SONG''%'
);
SET @stmt = IF(@entry_kind_has_song = 1,
  'ALTER TABLE `credit_block_entries` MODIFY COLUMN `entry_kind` ENUM(''PERSON'',''CHARACTER_VOICE'',''COMPANY'',''LOGO'',''TEXT'') NOT NULL',
  'SELECT ''credit_block_entries.entry_kind already without SONG. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-G-4: fk_be_song_recording FK を DROP ──
SET @has_fk_be_song = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND CONSTRAINT_NAME = 'fk_be_song_recording' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk_be_song = 1,
  'ALTER TABLE `credit_block_entries` DROP FOREIGN KEY `fk_be_song_recording`',
  'SELECT ''credit_block_entries.fk_be_song_recording already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-G-5: song_recording_id 列を DROP ──
SET @has_song_rec_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_block_entries'
    AND COLUMN_NAME = 'song_recording_id'
);
SET @stmt = IF(@has_song_rec_col = 1,
  'ALTER TABLE `credit_block_entries` DROP COLUMN `song_recording_id`',
  'SELECT ''credit_block_entries.song_recording_id already dropped. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 8-H: 役職テンプレ初期投入と主題歌役職体系の整備（v1.2.0 工程 H 最終形）。
-- =============================================================================
-- 設計方針：
--   * 連載（SERIALIZED_IN）：default_format_template に初期テンプレを投入（WHERE
--     default_format_template IS NULL で、ユーザーが手動編集済みの場合は尊重）。
--   * 主題歌系：シリーズの時期によって表現が異なるため、5 つの役職を使い分ける。
--       - THEME_SONG_OP_COMBINED      … 黎明期（最初の 10 年程度）の OP 枠用。
--                                       OP 曲と ED 曲を 2 カラム横並びで「主題歌」として
--                                       1 ブロックに表示する。挿入歌は通常クレジットされない
--                                       時期だが、レコード上に存在する場合は INSERT_SONGS_*
--                                       役職で別途保持する（重複表示は運用で回避）。
--       - THEME_SONG_OP               … 中期以降の OP 枠用。OP 曲のみ。
--       - THEME_SONG_ED               … 中期以降の ED 枠用。ED 曲のみ
--                                       （挿入歌が EDクレジットに併記される時期も
--                                        12 年目以降は INSERT_SONG 役職に分離する設計）。
--       - INSERT_SONG                 … 12 年目以降に挿入歌が独立してクレジットされる
--                                       ようになった以降の挿入歌枠。複数曲ありうる。
--       - INSERT_SONGS_NONCREDITED    … 実放送ではクレジットされなかったが、楽曲事実
--                                       としてデータベースに保持しておきたい挿入歌枠。
--                                       同一カードに INSERT_SONG と並置すると楽曲が
--                                       二重に表示されるが、運用上 1 つだけ置く前提
--                                       （意図して両方置いた場合は両方表示される）。
--   * 旧 'THEME_SONGS' 役職は誤投入だったため物理削除する。
--     credit_card_roles から参照されている可能性があるため、参照行は role_code を NULL
--     に書き換えてブランクロール化（配下の Block / Entry は CASCADE で消えずに残る）。
--     ユーザーは UI から正しい役職を割り当て直すことで復活させられる。
--
-- 主題歌役職のテンプレ DSL：
--   {THEME_SONGS:kind=OP}          … OP 曲のみ
--   {THEME_SONGS:kind=ED}          … ED 曲のみ
--   {THEME_SONGS:kind=INSERT}      … 挿入歌のみ（複数あれば縦並びで全部）
--   {THEME_SONGS:kind=OP+ED,columns=2} … OP と ED を 2 カラム横並び（黎明期 OP_COMBINED 用）
--   {THEME_SONGS:kind=ALL} or 省略 … OP+ED+INSERT 全部
--
-- マイグレーションは冪等。実行順序は以下のとおり:
--   8-H-1: SERIALIZED_IN の default_format_template に初期テンプレ投入
--   8-H-2: 旧 THEME_SONGS 役職を参照する credit_card_roles の role_code を NULL に書き換え
--   8-H-3: 旧 THEME_SONGS 役職を物理削除
--   8-H-4: 新 5 役職を INSERT IGNORE で投入（既に存在すれば何もしない）

-- ── 8-H-1: SERIALIZED_IN（連載）テンプレ初期投入 ──
-- {#BLOCKS:first} で最初のブロックを「連載／講談社「なかよし」/ 漫画・上北ふたご」形式に。
-- {#BLOCKS:rest} で 2 番目以降のブロックを「  「○○」」形式に（先頭に全角スペース）。
-- 末尾に「ほか」を付与。
UPDATE `roles`
SET `default_format_template` =
  '{#BLOCKS:first}{ROLE_NAME}／{LEADING_COMPANY}「{COMPANIES:wrap=""}」\n漫画・{PERSONS}{/BLOCKS:first}{#BLOCKS:rest}\n　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか'
WHERE `role_code` = 'SERIALIZED_IN' AND `default_format_template` IS NULL;

-- ── 8-H-2: 旧 THEME_SONGS 役職を参照する credit_card_roles の role_code を NULL 化 ──
-- credit_card_roles.role_code は NULL 許容（DEFAULT NULL）で、FK は ON DELETE RESTRICT。
-- 直接 DELETE すると参照行があれば失敗するため、先に参照を切断する。
-- role_code=NULL は「ブランクロール（役職未割当）」を意味し、配下の Block / Entry は CASCADE で
-- 消えずに残るので、ユーザーは UI から正しい役職を割り当て直すだけで復活できる。
UPDATE `credit_card_roles` SET `role_code` = NULL WHERE `role_code` = 'THEME_SONGS';

-- ── 8-H-3: 旧 THEME_SONGS 役職を物理削除 ──
-- ここまでで参照は切断済みなので、安全に DELETE できる。
-- 旧役職を将来に残さず完全撤去する（誤った設計は記録に残さない方針）。
DELETE FROM `roles` WHERE `role_code` = 'THEME_SONGS';

-- ── 8-H-4: 主題歌系 5 役職を INSERT IGNORE で投入 ──
-- 既に同 role_code が存在する場合は何もしない（運用者が手動で先に登録していたケース）。
-- 表示順は 510 から 10 単位の飛び番（既存役職並べ替えの慣例に整合）。
INSERT IGNORE INTO `roles`
  (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `display_order`, `default_format_template`)
VALUES
  ('THEME_SONG_OP_COMBINED', '主題歌',
   'Theme Song (OP+ED Combined)', 'THEME_SONG', 510,
   '{ROLE_NAME}\n{THEME_SONGS:kind=OP+ED,columns=2}'),
  ('THEME_SONG_OP', 'オープニング主題歌',
   'Opening Theme Song', 'THEME_SONG', 520,
   '{ROLE_NAME}\n{THEME_SONGS:kind=OP}'),
  ('THEME_SONG_ED', 'エンディング主題歌',
   'Ending Theme Song', 'THEME_SONG', 530,
   '{ROLE_NAME}\n{THEME_SONGS:kind=ED}'),
  ('INSERT_SONG', '挿入歌',
   'Insert Song', 'THEME_SONG', 540,
   '{ROLE_NAME}\n{THEME_SONGS:kind=INSERT}'),
  ('INSERT_SONGS_NONCREDITED', '挿入歌（ノンクレジット）',
   'Insert Songs (Non-credited)', 'THEME_SONG', 550,
   '{ROLE_NAME}\n{THEME_SONGS:kind=INSERT}');


-- =============================================================================
-- STEP 8-I: episode_theme_songs から label_company_alias_id 列を物理削除する
--           （v1.2.0 工程 H 補修）。
-- =============================================================================
-- 設計判断：episode_theme_songs は「このエピソードの OP/ED/挿入歌は何か」という
-- 楽曲の事実だけを保持すべきで、レーベル会社（販売元）の情報は本来クレジット表示の
-- ためだけに必要となる関心事である。同じ楽曲が異なるエピソードで異なるレーベル
-- 表記で出る／レーベル表記が出ない場合もあり、楽曲側に持たせると関心が混ざる。
-- レーベル名は credit_block_entries の COMPANY エントリで保持するのが正しい設計と
-- 判断したため、列・FK・INDEX を撤去する。
--
-- 【事前確認 SQL（任意）】既存環境で本列に値が入っていた行を把握したい場合は、
-- 本マイグレーション流す前に以下を実行して結果を控えておく：
--   SELECT episode_id, theme_kind, insert_seq, song_recording_id, label_company_alias_id
--     FROM episode_theme_songs WHERE label_company_alias_id IS NOT NULL;
--
-- 処理の流れ（すべて冪等）:
--   8-I-1: fk_ets_label_company FK を DROP
--   8-I-2: ix_ets_label_company INDEX を DROP
--   8-I-3: label_company_alias_id 列を DROP

-- ── 8-I-1: fk_ets_label_company FK を DROP ──
SET @has_fk_ets_label = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND CONSTRAINT_NAME = 'fk_ets_label_company' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
);
SET @stmt = IF(@has_fk_ets_label = 1,
  'ALTER TABLE `episode_theme_songs` DROP FOREIGN KEY `fk_ets_label_company`',
  'SELECT ''episode_theme_songs.fk_ets_label_company already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-I-2: ix_ets_label_company INDEX を DROP ──
SET @has_ix_ets_label = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND INDEX_NAME = 'ix_ets_label_company'
);
SET @stmt = IF(@has_ix_ets_label > 0,
  'ALTER TABLE `episode_theme_songs` DROP INDEX `ix_ets_label_company`',
  'SELECT ''episode_theme_songs.ix_ets_label_company already absent. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;

-- ── 8-I-3: label_company_alias_id 列を DROP ──
SET @has_label_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'episode_theme_songs'
    AND COLUMN_NAME = 'label_company_alias_id'
);
SET @stmt = IF(@has_label_col = 1,
  'ALTER TABLE `episode_theme_songs` DROP COLUMN `label_company_alias_id`',
  'SELECT ''episode_theme_songs.label_company_alias_id already dropped. skipping.'' AS msg'
);
PREPARE _stmt FROM @stmt; EXECUTE _stmt; DEALLOCATE PREPARE _stmt;


-- =============================================================================
-- STEP 9: セッション変数の復元
-- =============================================================================
SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;


-- =============================================================================
-- STEP 10: 確認用サマリ（参考出力）
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
