-- =====================================================================
-- v1.6.5_smile_ep21_cow_catcher_cm_shift.sql
--
-- 『スマイルプリキュア！』第 21 話（episode_id = 410）の CM 番号を 1 つずつ繰り下げ、
-- 冒頭の CM を新設の「カウキャッチャー(COW_CATCHER)」へ付け替える。
--
-- 当該話はアバンタイトルより前に 30 秒の CM が入る構成で、その 1 枠ぶん
-- 以降の CM 番号が全体にずれて登録されていた（CM②が A パート前、CM③が中 CM、
-- CM④が B パート後）。スマイル全 48 話の実尺分布（CM①=90 秒が 47 話 /
-- CM②=90 秒が 48 話 / CM③=60 秒が 46 話）に対し、本話だけが CM①=30 秒・
-- CM④=60 秒という外れ値になっており、繰り下げ後は前後の第 20・22 話と
-- パート構成が完全一致する。
--
-- 併せて、この付け替えは統計にも効く：CM 入り時刻ランキング
-- （EpisodePartStatsRepository.GetCmTimeRankingAsync）は CM2 パートの
-- 開始オフセットで順位付けするため、本話は A パート前の CM を CM② と
-- 見なされて 162 秒＝歴代最速 1 位（2 位は 469 秒）に居座っていた。
-- 繰り下げ後は真の中 CM を指して 700 秒となり、順当な位置に落ちる。
--
-- 前提: 先に db/migrations/v1.6.5_add_cow_catcher_part_type.sql を適用し、
--       part_types に COW_CATCHER が存在すること。
--
-- 更新順序: 番号の若い側から回す。各 UPDATE が次の UPDATE の移動先を
--           空けてから進むため、途中で同一種別が二重に存在しない。
-- 冪等性: (episode_seq, part_type) の対で行を釘付けするので、2 回目以降は
--         いずれの UPDATE も 0 行更新（安全）。
-- =====================================================================

START TRANSACTION;

-- 冒頭 CM（seq 1, 30 秒）→ カウキャッチャー。種別名で表現できるようになるため備考も消す。
UPDATE `episode_parts`
   SET `part_type`  = 'COW_CATCHER',
       `notes`      = NULL,
       `updated_by` = 'cow catcher reassignment (v1.6.5)'
 WHERE `episode_id`  = 410
   AND `episode_seq` = 1
   AND `part_type`   = 'CM1';

-- 前提供クレジット直後・A パート前の CM（seq 5, 90 秒）→ CM①
UPDATE `episode_parts`
   SET `part_type`  = 'CM1',
       `updated_by` = 'cow catcher reassignment (v1.6.5)'
 WHERE `episode_id`  = 410
   AND `episode_seq` = 5
   AND `part_type`   = 'CM2';

-- A/B パート間の中 CM（seq 7, 90 秒）→ CM②
UPDATE `episode_parts`
   SET `part_type`  = 'CM2',
       `updated_by` = 'cow catcher reassignment (v1.6.5)'
 WHERE `episode_id`  = 410
   AND `episode_seq` = 7
   AND `part_type`   = 'CM3';

-- B パート後・ED 前の CM（seq 9, 60 秒）→ CM③
UPDATE `episode_parts`
   SET `part_type`  = 'CM3',
       `updated_by` = 'cow catcher reassignment (v1.6.5)'
 WHERE `episode_id`  = 410
   AND `episode_seq` = 9
   AND `part_type`   = 'CM4';

COMMIT;

-- 確認用（期待値: seq 1=COW_CATCHER/30, 5=CM1/90, 7=CM2/90, 9=CM3/60。CM4 は 0 行）
SELECT `episode_seq`, `part_type`, `oa_length`, `notes`
  FROM `episode_parts`
 WHERE `episode_id` = 410
 ORDER BY `episode_seq`;
