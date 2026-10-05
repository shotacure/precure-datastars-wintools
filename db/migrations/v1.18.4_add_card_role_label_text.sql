-- =====================================================================
-- v1.18.4_add_card_role_label_text.sql
--
-- クレジットの役職（credit_card_roles）に、画面の役職の表記を役職ごとに持てるようにする。
--   role_label_text  VARCHAR(64) NULL   画面の役職の表記（役職マスタの name_ja と表記が違うときだけ入れる）。
--                                        NULL なら name_ja で出す。集計は role_code のまま。
--   join_separator   VARCHAR(16) NULL   1 行にまとめた役職（join_previous = 1）の、直前の役職との区切りの文字
--                                        （「・」「／」など画面どおり）。NULL は区切りなし。
--
-- 1 行にまとめた行の役職名は、先頭の役職の表記（role_label_text か name_ja）に、
-- 後続の役職ごとに「join_separator + 表記」をつなげて組み立てる。
-- まとめた行の文字を 1 本で持っていた joined_label は、この組み立てに置き換えて削除する。
--
-- 既存の joined_label の移し替え:
--   joined_label が「先頭の役職名 + 区切り + 後続の役職名」の形（2 役職）にちょうど分けられるものは、
--   区切りを後続の役職の join_separator に移す。分けられないものが 1 件でもあれば、
--   joined_label を削除せずにエラーで止める（手で role_label_text / join_separator を入れてから流し直す）。
--
-- 冪等性: 列ごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1184_add_card_role_label_text;
DELIMITER $$
CREATE PROCEDURE _v1184_add_card_role_label_text()
BEGIN
  DECLARE unconvertible INT DEFAULT 0;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'role_label_text'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `role_label_text` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '画面の役職の表記（役職名と表記が違うときだけ）。NULL なら役職名で出す'
        AFTER `affiliation_layout`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'join_separator'
  ) THEN
    ALTER TABLE `credit_card_roles`
      ADD COLUMN `join_separator` varchar(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
        COMMENT '1 行にまとめた役職の、直前の役職との区切りの文字（画面どおり）。NULL は区切りなし'
        AFTER `join_previous`;
  END IF;

  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'credit_card_roles' AND COLUMN_NAME = 'joined_label'
  ) THEN
    -- 移し替えられない joined_label（後続が 1 役職でない / 役職名 + 区切り + 役職名に分けられない）を数える。
    SELECT COUNT(*) INTO unconvertible
      FROM credit_card_roles l
      JOIN roles rl ON rl.role_code = l.role_code
      LEFT JOIN credit_card_roles f
        ON f.card_group_id = l.card_group_id AND f.order_in_group = l.order_in_group + 1 AND f.join_previous = 1
      LEFT JOIN roles rf ON rf.role_code = f.role_code
      LEFT JOIN credit_card_roles f2
        ON f2.card_group_id = l.card_group_id AND f2.order_in_group = l.order_in_group + 2 AND f2.join_previous = 1
     WHERE l.joined_label IS NOT NULL
       AND (
             f.card_role_id IS NULL
          OR f2.card_role_id IS NOT NULL
          OR CHAR_LENGTH(l.joined_label) < CHAR_LENGTH(rl.name_ja) + CHAR_LENGTH(rf.name_ja)
          OR CAST(LEFT(l.joined_label, CHAR_LENGTH(rl.name_ja)) AS BINARY) <> CAST(rl.name_ja AS BINARY)
          OR CAST(RIGHT(l.joined_label, CHAR_LENGTH(rf.name_ja)) AS BINARY) <> CAST(rf.name_ja AS BINARY)
       );

    IF unconvertible > 0 THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'joined_label の中に、役職名 + 区切り + 役職名に分けられない行があります。role_label_text / join_separator を手で入れてから流し直してください。';
    END IF;

    -- 区切り（先頭の役職名と後続の役職名のあいだの文字）を後続の役職へ移す。空なら NULL。
    UPDATE credit_card_roles f
      JOIN credit_card_roles l
        ON l.card_group_id = f.card_group_id AND l.order_in_group = f.order_in_group - 1
      JOIN roles rl ON rl.role_code = l.role_code
      JOIN roles rf ON rf.role_code = f.role_code
       SET f.join_separator = NULLIF(
             SUBSTRING(l.joined_label, CHAR_LENGTH(rl.name_ja) + 1,
                       CHAR_LENGTH(l.joined_label) - CHAR_LENGTH(rl.name_ja) - CHAR_LENGTH(rf.name_ja)),
             '')
     WHERE f.join_previous = 1
       AND l.joined_label IS NOT NULL;

    ALTER TABLE `credit_card_roles` DROP COLUMN `joined_label`;
  END IF;
END$$
DELIMITER ;

CALL _v1184_add_card_role_label_text();
DROP PROCEDURE IF EXISTS _v1184_add_card_role_label_text;
