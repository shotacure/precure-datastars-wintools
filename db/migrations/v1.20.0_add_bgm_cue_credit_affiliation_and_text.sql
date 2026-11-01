-- =====================================================================
-- v1.20.0_add_bgm_cue_credit_affiliation_and_text.sql
--
-- 劇伴の作家連名（bgm_cue_credits）に、歌の作家連名（song_credits）と同じ所属と、名義でない「文字の行」を持てるようにする。
--   entry_kind                    ENUM('PERSON','TEXT') NOT NULL DEFAULT 'PERSON'
--     PERSON … 名義（person_alias_id）の行
--     TEXT   … 名義を持たない文字だけの行（raw_text。「不詳」「イギリス民謡」など、人物にできない作家の表記）
--   person_alias_id               INT NULL           PERSON の行の名義。TEXT の行では NULL
--   raw_text                      VARCHAR(255) NULL  TEXT の行の表記
--   affiliation_company_alias_id  INT NULL           所属の会社名義（company_aliases.alias_id）
--   affiliation_text              VARCHAR(64) NULL   会社マスタに無い所属の自由記述
--   affiliation_display           ENUM('NAME_WITH_COMPANY','COMPANY_ONLY','NAME_ONLY') NOT NULL DEFAULT 'NAME_WITH_COMPANY'
--     NAME_WITH_COMPANY … 名義のあとに所属を添える（「福富 雅之（Nostalgic Orchestra）」）
--     COMPANY_ONLY      … 名義を出さず所属の会社名だけを出す
--     NAME_ONLY         … 所属を持ったまま名義だけを出す
--
-- 用途：サントラに「福富雅之(Nostalgic Orchestra)」のように劇伴の作家の所属が印刷されているとき、名義とは別に所属を持つ。
-- 作曲者が「不詳」のような人物にできない表記も、フリーテキスト列（bgm_cues.composer_name など）に頼らず構造化行で持つ。
-- entry_kind と名義・文字・所属の列の整合性は、BEFORE INSERT/UPDATE トリガーで担保する
-- （MySQL 8.0 では FK の参照アクション列を CHECK に含められないため。credit_block_entries と同じパターン）。
--   PERSON … person_alias_id 必須、raw_text は NULL
--   TEXT   … raw_text 必須（空文字不可）、person_alias_id・affiliation_company_alias_id・affiliation_text は NULL
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。トリガーは DROP してから作り直す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1200_add_bgm_cue_credit_affiliation_and_text;
DELIMITER $$
CREATE PROCEDURE _v1200_add_bgm_cue_credit_affiliation_and_text()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bgm_cue_credits' AND COLUMN_NAME = 'entry_kind'
  ) THEN
    ALTER TABLE `bgm_cue_credits`
      ADD COLUMN `entry_kind` enum('PERSON','TEXT') NOT NULL DEFAULT 'PERSON'
        COMMENT '行の種類（PERSON＝名義の行 / TEXT＝名義を持たない文字だけの行）'
        AFTER `credit_seq`,
      MODIFY COLUMN `person_alias_id` int DEFAULT NULL
        COMMENT 'PERSON の行の名義（person_aliases.alias_id）。TEXT の行では NULL',
      ADD COLUMN `raw_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT 'TEXT の行の表記（「不詳」など）'
        AFTER `person_alias_id`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bgm_cue_credits' AND COLUMN_NAME = 'affiliation_company_alias_id'
  ) THEN
    ALTER TABLE `bgm_cue_credits`
      ADD COLUMN `affiliation_company_alias_id` int DEFAULT NULL
        COMMENT '名義に添える所属の会社名義（company_aliases.alias_id）'
        AFTER `preceding_separator`,
      ADD COLUMN `affiliation_text` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '会社マスタに無い所属の自由記述（会社名義があればそちらを使う）'
        AFTER `affiliation_company_alias_id`,
      ADD COLUMN `affiliation_display` enum('NAME_WITH_COMPANY','COMPANY_ONLY','NAME_ONLY') NOT NULL DEFAULT 'NAME_WITH_COMPANY'
        COMMENT '所属の出し方（名義＋所属 / 所属の会社名だけ / 名義だけ）'
        AFTER `affiliation_text`,
      ADD KEY `ix_bgm_cue_credits_aff_company` (`affiliation_company_alias_id`),
      ADD CONSTRAINT `fk_bgm_cue_credits_aff_company` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;

END$$
DELIMITER ;

CALL _v1200_add_bgm_cue_credit_affiliation_and_text();
DROP PROCEDURE IF EXISTS _v1200_add_bgm_cue_credit_affiliation_and_text;

DROP TRIGGER IF EXISTS `trg_bgm_cue_credits_bi_consistency`;
DROP TRIGGER IF EXISTS `trg_bgm_cue_credits_bu_consistency`;

DELIMITER $$
CREATE TRIGGER `trg_bgm_cue_credits_bi_consistency`
BEFORE INSERT ON `bgm_cue_credits`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND (NEW.person_alias_id IS NULL OR NEW.raw_text IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'bgm_cue_credits: entry_kind=PERSON requires person_alias_id and no raw_text';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '' OR NEW.person_alias_id IS NOT NULL
                                  OR NEW.affiliation_company_alias_id IS NOT NULL OR NEW.affiliation_text IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'bgm_cue_credits: entry_kind=TEXT requires non-empty raw_text and no person_alias_id / affiliation';
  END IF;
END$$

CREATE TRIGGER `trg_bgm_cue_credits_bu_consistency`
BEFORE UPDATE ON `bgm_cue_credits`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND (NEW.person_alias_id IS NULL OR NEW.raw_text IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'bgm_cue_credits: entry_kind=PERSON requires person_alias_id and no raw_text';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '' OR NEW.person_alias_id IS NOT NULL
                                  OR NEW.affiliation_company_alias_id IS NOT NULL OR NEW.affiliation_text IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'bgm_cue_credits: entry_kind=TEXT requires non-empty raw_text and no person_alias_id / affiliation';
  END IF;
END$$
DELIMITER ;
