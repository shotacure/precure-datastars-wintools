-- =====================================================================
-- v1.5.14_special_trailer_reassign_meitantei_2026tv.sql
--
-- 『名探偵プリキュア！』（series slug: 2026tv）第 20〜24 話に入っていた
-- 「各種告知(NOTICE)」= 連作コーナー「キュアエクレールの正体は誰！？」の
-- 5 パートを、新設の「特別予告(SPECIAL_TRAILER)」へ付け替える。
--
-- 前提: 先に db/migrations/v1.5.14_add_special_trailer_part_type.sql を適用し、
--       part_types に SPECIAL_TRAILER が存在すること。
--
-- 対象 episode_id: 1087(20話) / 1088(21話) / 1089(22話) / 1090(23話) / 1091(24話)。
-- 各話に NOTICE は 1 件ずつ、計 5 行。part_type='NOTICE' でも絞って誤爆を防ぐ。
-- 冪等性: 2 回目以降は該当 NOTICE 行が無くなるため 0 行更新（安全）。
-- =====================================================================

UPDATE `episode_parts`
   SET `part_type`  = 'SPECIAL_TRAILER',
       `updated_by` = 'special-trailer reassignment (v1.5.14)'
 WHERE `part_type`  = 'NOTICE'
   AND `episode_id` IN (1087, 1088, 1089, 1090, 1091);
