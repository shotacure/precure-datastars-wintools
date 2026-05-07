-- ===========================================================================
-- Migration: v1.2.2 -> v1.2.3
--   音楽系（歌・歌唱者・劇伴）のクレジット情報を構造化する。
--
-- 変更内容:
--   1. person_aliases に display_text_override 列を追加（VARCHAR(1024)）。
--      ユニット名義などで定形外の表示文字列が必要なケース用。非 NULL のとき
--      アプリ側の表示ロジックは name より優先してこの値を使う。
--   2. person_alias_members を新設。ユニット名義の構成メンバーを順序付きで持つ。
--      メンバーは PERSON（人物名義）または CHARACTER（キャラ名義）。
--      ユニットのネスト（ユニットがユニットを内包）はトリガーで禁止する。
--   3. song_credits を新設。songs に対する作家連名（作詞 / 作曲 / 編曲）を順序付きで持つ。
--      既存の songs.lyricist_name / composer_name / arranger_name は温存し、
--      中間表に行があるとき表示ロジック側で優先する運用とする。
--   4. song_recording_singers を新設。song_recordings に対する歌唱者連名を順序付きで持つ。
--      billing_kind は PERSON（個人歌唱）と CHARACTER_WITH_CV（キャラ + CV 表記）の 2 値。
--      「キュアブラック / 美墨なぎさ」のような同 CV のスラッシュ並列はスラッシュ相方列で表現。
--      既存 song_recordings.singer_name は温存（表示優先規則は songs と同じ）。
--   5. bgm_cue_credits を新設。bgm_cues に対する作家連名を順序付きで持つ。
--      既存 bgm_cues.composer_name / arranger_name は温存。
--   6. ネスト禁止トリガー tr_pam_no_nested_unit_bi / _bu を新設。
--
-- 実行方法（既に v1.2.2 の DB に対して）:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.2.3_add_music_credits.sql
--
-- 冪等性:
--   各オブジェクトは INFORMATION_SCHEMA で存在確認してから DDL を発行するため
--   再実行しても安全。
-- ===========================================================================

-- ===========================================================================
-- STEP 1: person_aliases.display_text_override 列追加
-- ===========================================================================
-- 通常表示は name を使うが、ユニット名義などで定形外の長い表記が必要なときに
-- ここに 1 行で表記用文字列を入れて優先表示させる。
SET @col_exists := (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'person_aliases'
    AND COLUMN_NAME  = 'display_text_override'
);
SET @sql := IF(@col_exists = 0,
  'ALTER TABLE `person_aliases` ADD COLUMN `display_text_override` VARCHAR(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL AFTER `name_kana`',
  'SELECT ''person_aliases.display_text_override already exists. skipping ADD COLUMN.'' AS msg');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- ===========================================================================
-- STEP 2: person_alias_members 新設（ユニット名義の構成メンバー）
-- ===========================================================================
-- parent_alias_id は person_aliases（ユニット側の名義）を指す。
-- member_kind = 'PERSON'    のとき member_person_alias_id    が必須（→ person_aliases）
-- member_kind = 'CHARACTER' のとき member_character_alias_id が必須（→ character_aliases）
-- 同じユニット内に同じメンバーは 1 回まで（種別ごとに UNIQUE）。
-- ON DELETE は両方とも RESTRICT：メンバーが先に消えるとユニットの整合性が崩れるため、
-- 先にユニット側の構成行を片付けてから消す運用を強制する。
--
-- 自己参照禁止（自分自身を PERSON メンバーにする）は、当初 CHECK 制約
-- ck_pam_no_self で表現したかったが、MySQL 8.0.16+ では「FK の参照アクションで
-- 使う列を CHECK の比較で参照できない」(Error 3823) 制約があり、
-- parent_alias_id が fk_pam_parent ON DELETE CASCADE / ON UPDATE CASCADE で
-- 使われている本表ではその CHECK が定義不能。よって自己参照禁止は
-- 本ステップ末尾のネスト禁止トリガー（tr_pam_no_nested_unit_bi / _bu）に
-- 統合して INSERT/UPDATE 時点で SIGNAL する方式に変更した。
CREATE TABLE IF NOT EXISTS `person_alias_members` (
  `parent_alias_id`            INT              NOT NULL,
  `member_seq`                 TINYINT UNSIGNED NOT NULL,
  `member_kind`                ENUM('PERSON','CHARACTER') NOT NULL,
  `member_person_alias_id`     INT              DEFAULT NULL,
  `member_character_alias_id`  INT              DEFAULT NULL,
  `notes`                      TEXT             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                 TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                 TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                 VARCHAR(64)      DEFAULT NULL,
  `updated_by`                 VARCHAR(64)      DEFAULT NULL,
  PRIMARY KEY (`parent_alias_id`, `member_seq`),
  UNIQUE KEY `uq_pam_person_member`    (`parent_alias_id`, `member_person_alias_id`),
  UNIQUE KEY `uq_pam_character_member` (`parent_alias_id`, `member_character_alias_id`),
  KEY `ix_pam_member_person`    (`member_person_alias_id`),
  KEY `ix_pam_member_character` (`member_character_alias_id`),
  CONSTRAINT `ck_pam_kind_columns` CHECK (
       (`member_kind` = 'PERSON'    AND `member_person_alias_id`    IS NOT NULL AND `member_character_alias_id` IS NULL)
    OR (`member_kind` = 'CHARACTER' AND `member_character_alias_id` IS NOT NULL AND `member_person_alias_id`    IS NULL)
  ),
  CONSTRAINT `fk_pam_parent`    FOREIGN KEY (`parent_alias_id`)           REFERENCES `person_aliases`    (`alias_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  -- ※ ON UPDATE NO ACTION（v1.2.3 修正）：
  --    ck_pam_kind_columns CHECK が member_person_alias_id / member_character_alias_id を参照しているため、
  --    これらの列に ON UPDATE CASCADE を設定すると MySQL 8.0.16+ Error 3823 で CREATE TABLE が失敗する。
  --    alias_id は AUTO_INCREMENT 代理キーで実運用で値が更新されることはないため、
  --    NO ACTION（実質 RESTRICT）に下げても運用上の差は無い。ON DELETE RESTRICT は維持。
  CONSTRAINT `fk_pam_person`    FOREIGN KEY (`member_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_pam_character` FOREIGN KEY (`member_character_alias_id`) REFERENCES `character_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- ===========================================================================
-- STEP 3: ネスト禁止 + 自己参照禁止トリガー（PERSON メンバーのみ対象）
-- ===========================================================================
-- (0) 自己参照（自分自身を PERSON メンバーにする）
-- (1) 追加しようとしているメンバーが既にユニット（誰かを抱えている）か
-- (2) このユニット自身が既に他ユニットのメンバーになっていないか
-- 上記を BEFORE INSERT / BEFORE UPDATE で検査し、違反時は SIGNAL で弾く。
-- 自己参照禁止は当初 CHECK 制約（ck_pam_no_self）で表現したかったが、
-- MySQL 8.0.16+ の「FK 参照アクション列を CHECK で参照不可」(Error 3823) 制限により
-- 本トリガーに統合した（INSERT/UPDATE 時点で SIGNAL する）。
-- CHARACTER メンバーは alias の世界が分かれているのでネスト・自己参照問題は発生しない。
DROP TRIGGER IF EXISTS `tr_pam_no_nested_unit_bi`;
DROP TRIGGER IF EXISTS `tr_pam_no_nested_unit_bu`;

DELIMITER $$

CREATE TRIGGER `tr_pam_no_nested_unit_bi`
BEFORE INSERT ON `person_alias_members`
FOR EACH ROW
BEGIN
  IF NEW.member_kind = 'PERSON' THEN
    -- (0) 自己参照禁止
    IF NEW.member_person_alias_id IS NOT NULL
       AND NEW.member_person_alias_id = NEW.parent_alias_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain itself as a member';
    END IF;
    -- (1) メンバーが既にユニット
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE parent_alias_id = NEW.member_person_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain another unit (member is already a unit)';
    END IF;
    -- (2) 親が既に他ユニットのメンバー
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE member_kind = 'PERSON'
                 AND member_person_alias_id = NEW.parent_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot be nested (parent is already a member of another unit)';
    END IF;
  END IF;
END$$

CREATE TRIGGER `tr_pam_no_nested_unit_bu`
BEFORE UPDATE ON `person_alias_members`
FOR EACH ROW
BEGIN
  IF NEW.member_kind = 'PERSON' THEN
    -- (0) 自己参照禁止
    IF NEW.member_person_alias_id IS NOT NULL
       AND NEW.member_person_alias_id = NEW.parent_alias_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain itself as a member';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE parent_alias_id = NEW.member_person_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain another unit (member is already a unit)';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE member_kind = 'PERSON'
                 AND member_person_alias_id = NEW.parent_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot be nested (parent is already a member of another unit)';
    END IF;
  END IF;
END$$

DELIMITER ;


-- ===========================================================================
-- STEP 4: song_credits 新設（歌の作家連名）
-- ===========================================================================
-- 1 曲に対する作詞・作曲・編曲のクレジット連名を順序付きで持つ。
-- preceding_separator は seq>=2 の行で「前の seq との間に表示する区切り文字」を保持する
-- （初出盤通りの「・」「＆」「、」「 / 」「 with 」等）。seq=1 の行では NULL。
-- 既存 songs.{lyricist|composer|arranger}_name は本テーブル登場後も DROP しない：
-- 本テーブルに行が無いとき表示ロジックがフォールバックとしてそちらを使う。
CREATE TABLE IF NOT EXISTS `song_credits` (
  `song_id`             INT              NOT NULL,
  `credit_role`         ENUM('LYRICIST','COMPOSER','ARRANGER') NOT NULL,
  `credit_seq`          TINYINT UNSIGNED NOT NULL,
  `person_alias_id`     INT              NOT NULL,
  `preceding_separator` VARCHAR(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`               TEXT             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          VARCHAR(64)      DEFAULT NULL,
  `updated_by`          VARCHAR(64)      DEFAULT NULL,
  PRIMARY KEY (`song_id`, `credit_role`, `credit_seq`),
  KEY `ix_song_credits_alias` (`person_alias_id`),
  CONSTRAINT `ck_song_credits_seq_pos` CHECK (`credit_seq` >= 1),
  CONSTRAINT `fk_song_credits_song`  FOREIGN KEY (`song_id`)         REFERENCES `songs`          (`song_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_song_credits_alias` FOREIGN KEY (`person_alias_id`) REFERENCES `person_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- ===========================================================================
-- STEP 5: song_recording_singers 新設（歌唱者連名）
-- ===========================================================================
-- 1 録音に対する歌唱者連名を順序付きで持つ。
-- billing_kind:
--   PERSON           : 個人名義のみ（例: 五條真由美 / Machico）
--   CHARACTER_WITH_CV: キャラ名義 + CV 表記（例: 美墨なぎさ(CV:本名陽子)）
-- ※ CHARACTER のみ（CV なし）や CV(キャラ) の語順倒置は未正規化扱いのため設計から除外。
--    そのような表記は移行時にデータ側を整備するか、ユニット名義の display_text_override で逃がす。
-- 「キュアブラック / 美墨なぎさ (CV: 本名 陽子)」のような同 CV のスラッシュ並列は
-- slash_*_alias_id 列で表現する（最大 1 個。同 billing_kind 側のみ非 NULL になる前提）。
-- preceding_separator は seq>=2 の行で前 seq との区切り文字（「、」「&」等）を保持。
CREATE TABLE IF NOT EXISTS `song_recording_singers` (
  `song_recording_id`         INT              NOT NULL,
  `singer_seq`                TINYINT UNSIGNED NOT NULL,
  `billing_kind`              ENUM('PERSON','CHARACTER_WITH_CV') NOT NULL,
  -- 主名義（kind に応じて片方が必須、もう片方は NULL）
  `person_alias_id`           INT              DEFAULT NULL,
  `character_alias_id`        INT              DEFAULT NULL,
  -- CHARACTER_WITH_CV のときの CV（声優の人物名義）
  `voice_person_alias_id`     INT              DEFAULT NULL,
  -- スラッシュ並列の相方（最大 1 個、同 kind 側のみ）
  `slash_person_alias_id`     INT              DEFAULT NULL,
  `slash_character_alias_id`  INT              DEFAULT NULL,
  -- 表示補助
  `preceding_separator`       VARCHAR(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_text`          VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`                     TEXT             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                VARCHAR(64)      DEFAULT NULL,
  `updated_by`                VARCHAR(64)      DEFAULT NULL,
  PRIMARY KEY (`song_recording_id`, `singer_seq`),
  KEY `ix_srs_person`          (`person_alias_id`),
  KEY `ix_srs_character`       (`character_alias_id`),
  KEY `ix_srs_voice`           (`voice_person_alias_id`),
  KEY `ix_srs_slash_person`    (`slash_person_alias_id`),
  KEY `ix_srs_slash_character` (`slash_character_alias_id`),
  CONSTRAINT `ck_srs_seq_pos` CHECK (`singer_seq` >= 1),
  -- billing_kind と必須/不要列の整合
  CONSTRAINT `ck_srs_kind_columns` CHECK (
       (`billing_kind` = 'PERSON'
          AND `person_alias_id`       IS NOT NULL
          AND `character_alias_id`    IS NULL
          AND `voice_person_alias_id` IS NULL
          AND `slash_character_alias_id` IS NULL)
    OR (`billing_kind` = 'CHARACTER_WITH_CV'
          AND `character_alias_id`    IS NOT NULL
          AND `voice_person_alias_id` IS NOT NULL
          AND `person_alias_id`       IS NULL
          AND `slash_person_alias_id` IS NULL)
  ),
  -- ※ ON UPDATE NO ACTION（v1.2.3 修正）：
  --    ck_srs_kind_columns CHECK が person_alias_id / character_alias_id / voice_person_alias_id /
  --    slash_person_alias_id / slash_character_alias_id を参照しているため、これらの列の ON UPDATE は
  --    CASCADE のままだと MySQL 8.0.16+ Error 3823 で CREATE TABLE が失敗する。NO ACTION に下げる。
  --    （ON DELETE RESTRICT は維持。alias_id は AUTO_INCREMENT 代理キーで値更新は実運用で無い）
  CONSTRAINT `fk_srs_recording`        FOREIGN KEY (`song_recording_id`)        REFERENCES `song_recordings`   (`song_recording_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_srs_person`           FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_character`        FOREIGN KEY (`character_alias_id`)       REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_voice`            FOREIGN KEY (`voice_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_slash_person`     FOREIGN KEY (`slash_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_slash_character`  FOREIGN KEY (`slash_character_alias_id`) REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- ===========================================================================
-- STEP 6: bgm_cue_credits 新設（劇伴の作家連名）
-- ===========================================================================
-- bgm_cues は (series_id, m_no_detail) で 1 意。これを複合 FK で参照する。
CREATE TABLE IF NOT EXISTS `bgm_cue_credits` (
  `series_id`           INT              NOT NULL,
  `m_no_detail`         VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `credit_role`         ENUM('COMPOSER','ARRANGER') NOT NULL,
  `credit_seq`          TINYINT UNSIGNED NOT NULL,
  `person_alias_id`     INT              NOT NULL,
  `preceding_separator` VARCHAR(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`               TEXT             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          VARCHAR(64)      DEFAULT NULL,
  `updated_by`          VARCHAR(64)      DEFAULT NULL,
  PRIMARY KEY (`series_id`, `m_no_detail`, `credit_role`, `credit_seq`),
  KEY `ix_bgm_cue_credits_alias` (`person_alias_id`),
  CONSTRAINT `ck_bgm_cue_credits_seq_pos` CHECK (`credit_seq` >= 1),
  CONSTRAINT `fk_bgm_cue_credits_cue`   FOREIGN KEY (`series_id`, `m_no_detail`) REFERENCES `bgm_cues` (`series_id`, `m_no_detail`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_bgm_cue_credits_alias` FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases` (`alias_id`)            ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


-- ===========================================================================
-- 完了マーカー
-- ===========================================================================
SELECT 'v1.2.3 migration completed: music credits structured tables added.' AS status;
