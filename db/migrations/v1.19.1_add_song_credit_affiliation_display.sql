-- =====================================================================
-- v1.19.1_add_song_credit_affiliation_display.sql
--
-- 歌の作家連名（song_credits）の所属の出し方を、行ごとに選べるようにする。
--   affiliation_display  ENUM('NAME_WITH_COMPANY','COMPANY_ONLY','NAME_ONLY') NOT NULL DEFAULT 'NAME_WITH_COMPANY'
--     NAME_WITH_COMPANY … 名義のあとに所属を添える（「藤本 記子（Nostalgic Orchestra）」。従来どおり）
--     COMPANY_ONLY      … 名義を出さず所属の会社名だけを出す（盤に「Nostalgic Orchestra」とだけ載っているとき）
--     NAME_ONLY         … 所属を持ったまま名義だけを出す（盤には名前だけだが、実質はその団体の仕事のとき）
-- どれもデータは人物＋所属で持ち、人物の集計・企業詳細の所属スタッフは同じに効く。
-- COMPANY_ONLY で所属が無い（会社名義が消えたなど）行は、名義に戻して出す。
--
-- 冪等性: INFORMATION_SCHEMA で列の存在を確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_song_credit_affiliation_display;
DELIMITER $$
CREATE PROCEDURE _v1191_add_song_credit_affiliation_display()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'song_credits' AND COLUMN_NAME = 'affiliation_display'
  ) THEN
    ALTER TABLE `song_credits`
      ADD COLUMN `affiliation_display` enum('NAME_WITH_COMPANY','COMPANY_ONLY','NAME_ONLY') NOT NULL DEFAULT 'NAME_WITH_COMPANY'
        COMMENT '所属の出し方（名義＋所属 / 所属の会社名だけ / 名義だけ）'
        AFTER `affiliation_text`;
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_song_credit_affiliation_display();
DROP PROCEDURE IF EXISTS _v1191_add_song_credit_affiliation_display;
