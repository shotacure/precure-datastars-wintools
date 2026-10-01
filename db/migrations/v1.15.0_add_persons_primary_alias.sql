-- =====================================================================
-- v1.15.0_add_persons_primary_alias.sql
--
-- 人物の「本名義」を指定する列を persons に追加する。
--   primary_alias_id  INT NULL  人物詳細の見出し・URL・各一覧の行表記に使う名義（person_aliases.alias_id）。
--
-- サイトは人物の名乗りを「指定した本名義 → いま公開している名義 → TV 系のクレジットで最後に使われた名義」
-- の順に決める。指定が無い人物も、いったん公開した名義を使い続けるので、クレジットの入力が進んでも
-- 見出しと URL は勝手には変わらない。改名した人物の現在の名義を出したいときにここで指定する。
-- 名義の ID を振り直したときは追従し（ON UPDATE CASCADE）、名義を消したときは指定を外す（ON DELETE SET NULL）。
--
-- 冪等性: 列・外部キーごとに INFORMATION_SCHEMA で存在確認してから ALTER を発行する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1150_add_persons_primary_alias;
DELIMITER $$
CREATE PROCEDURE _v1150_add_persons_primary_alias()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'primary_alias_id'
  ) THEN
    ALTER TABLE `persons`
      ADD COLUMN `primary_alias_id` int DEFAULT NULL
        COMMENT '本名義（person_aliases.alias_id）。見出し・URL・一覧の行表記に使う。NULL なら公開中の名義→最新名義'
        AFTER `full_name_kana`;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND CONSTRAINT_NAME = 'fk_persons_primary_alias'
  ) THEN
    ALTER TABLE `persons`
      ADD CONSTRAINT `fk_persons_primary_alias` FOREIGN KEY (`primary_alias_id`)
        REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END$$
DELIMITER ;

CALL _v1150_add_persons_primary_alias();
DROP PROCEDURE IF EXISTS _v1150_add_persons_primary_alias;
