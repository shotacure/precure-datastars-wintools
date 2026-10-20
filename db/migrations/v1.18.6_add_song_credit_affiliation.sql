-- =====================================================================
-- v1.18.6_add_song_credit_affiliation.sql
--
-- 歌の作家連名（song_credits）に、名義に添える所属を持てるようにする。
--   affiliation_company_alias_id  INT NULL          所属の会社名義（company_aliases.alias_id）
--   affiliation_text              VARCHAR(64) NULL  会社マスタに無い所属の自由記述
--
-- 用途：盤に「岡部啓一(MONACA)」のように作家の所属が印刷されているとき、名義とは別に所属を持つ。
-- サイトの曲・商品のトラック・エピソードの主題歌欄では「岡部 啓一（MONACA）」の形で添え、
-- 会社名義に紐付いていれば企業詳細へリンクする（盤スタッフの所属と同じ見た目）。
-- 各話のクレジット画面（役職テンプレの主題歌カード）には出さない（画面の表記どおりにするため）。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1186_add_song_credit_affiliation;
DELIMITER $$
CREATE PROCEDURE _v1186_add_song_credit_affiliation()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'song_credits' AND COLUMN_NAME = 'affiliation_company_alias_id'
  ) THEN
    ALTER TABLE `song_credits`
      ADD COLUMN `affiliation_company_alias_id` int DEFAULT NULL
        COMMENT '名義に添える所属の会社名義（company_aliases.alias_id）'
        AFTER `preceding_separator`,
      ADD COLUMN `affiliation_text` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '会社マスタに無い所属の自由記述（会社名義があればそちらを使う）'
        AFTER `affiliation_company_alias_id`,
      ADD KEY `ix_song_credits_aff_company` (`affiliation_company_alias_id`),
      ADD CONSTRAINT `fk_song_credits_aff_company` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1186_add_song_credit_affiliation();
DROP PROCEDURE IF EXISTS _v1186_add_song_credit_affiliation;
