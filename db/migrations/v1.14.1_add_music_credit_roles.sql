-- =====================================================================
-- v1.14.1_add_music_credit_roles.sql
--
-- 音楽クレジットの役職を追加する。
--   TRACKDOWN_STUDIO           トラックダウンスタジオ（レコーディングの区分）。録音とトラックダウン（ミックス）を
--                              別のスタジオで行った盤で、トラックダウンの側を持つ。
--   DISC_EXECUTIVE_PRODUCER    エグゼクティブプロデューサー（音盤製作の区分）。盤の製作側の総責任者。
--                              本編クレジットの EXECUTIVE_PRODUCER とは別の役職にして、音楽クレジットの区分を持たせる。
--   ORGAN                      オルガン（演奏の区分）。ピアノの次に並べる（ギター以降の演奏系の display_order を 1 つずつ後ろへずらす）。
--   HARMONICA                  ハーモニカ（演奏の区分）。ファゴットの次に並べる（ハープ以降の演奏系の display_order を 1 つずつ後ろへずらす）。
--
-- あわせて SYNTH_OPERATION の表示名を「シンセサイザー」に縮める（「シンセサイザー・オペレート」は長いため）。
--
-- 冪等性: INSERT IGNORE / 同じ値への UPDATE。display_order のずらしは、その役職がまだ無いときだけ行う。
-- =====================================================================

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('TRACKDOWN_STUDIO',        'トラックダウンスタジオ',       'Trackdown Studio',   'NORMAL', 'RECORDING', 2308, 'migration', 'migration'),
  ('DISC_EXECUTIVE_PRODUCER', 'エグゼクティブプロデューサー', 'Executive Producer', 'NORMAL', 'RELEASE',   2407, 'migration', 'migration');

-- ORGAN の入る場所（2204）を空ける。display_order は一意なので、後ろの行から順にずらす。
UPDATE `roles` SET `display_order` = `display_order` + 1, `updated_by` = 'migration'
 WHERE `display_order` BETWEEN 2204 AND 2229
   AND NOT EXISTS (SELECT 1 FROM (SELECT `role_code` FROM `roles` WHERE `role_code` = 'ORGAN') AS `t`)
 ORDER BY `display_order` DESC;

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('ORGAN', 'オルガン', 'Organ', 'NORMAL', 'PERFORMANCE', 2204, 'migration', 'migration');

-- HARMONICA の入る場所（ファゴットの次の 2223）を空ける。ORGAN を入れた後の並びが前提。
UPDATE `roles` SET `display_order` = `display_order` + 1, `updated_by` = 'migration'
 WHERE `display_order` BETWEEN 2223 AND 2230
   AND NOT EXISTS (SELECT 1 FROM (SELECT `role_code` FROM `roles` WHERE `role_code` = 'HARMONICA') AS `t`)
 ORDER BY `display_order` DESC;

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('HARMONICA', 'ハーモニカ', 'Harmonica', 'NORMAL', 'PERFORMANCE', 2223, 'migration', 'migration');

UPDATE `roles` SET `name_ja` = 'シンセサイザー', `updated_by` = 'migration' WHERE `role_code` = 'SYNTH_OPERATION';
