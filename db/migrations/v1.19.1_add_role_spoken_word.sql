-- =====================================================================
-- v1.19.1_add_role_spoken_word.sql
--
-- 役職 SPOKEN_WORD「語り」を足す（歌の録音の歌唱者・劇伴の歌い手で、盤に「語り：」と載る担当）。
-- 表示順は台詞（DIALOGUE、932）の次の 933 に置き、クワイア（933）・スキャット（934）を 1 つずつ後ろへずらす。
-- 書式・並びは台詞と同じ（キャラは「キャラ(CV:声優)」）。サイトは台詞の次の行に出す。
--
-- 冪等性: 役職がまだ無いときだけ、表示順をずらしてから INSERT する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1191_add_role_spoken_word;
DELIMITER $$
CREATE PROCEDURE _v1191_add_role_spoken_word()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM roles WHERE role_code = 'SPOKEN_WORD') THEN
    UPDATE roles SET display_order = display_order + 1, updated_by = 'migration'
     WHERE display_order BETWEEN 933 AND 934 ORDER BY display_order DESC;
    INSERT INTO roles (role_code, name_ja, name_en, role_format_kind, music_credit_group, display_order, created_by, updated_by)
    VALUES ('SPOKEN_WORD', '語り', 'Spoken Word', 'NORMAL', 'PERFORMANCE', 933, 'migration', 'migration');
  END IF;
END$$
DELIMITER ;

CALL _v1191_add_role_spoken_word();
DROP PROCEDURE IF EXISTS _v1191_add_role_spoken_word;
