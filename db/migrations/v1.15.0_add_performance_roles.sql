-- =====================================================================
-- v1.15.0_add_performance_roles.sql
--
-- 演奏の区分（PERFORMANCE）に楽器の役職を追加する。
--   FOLK_GUITAR     フォークギター。ギターの次（エレキギターの前）に並べる。
--   BASS_TROMBONE   バストロンボーン。トロンボーンの次に並べる。
--   PICCOLO         ピッコロ。フルートの次に並べる。
-- それぞれ入る場所を空けるため、後ろにある演奏系の display_order を 1 つずつ後ろへずらす。
--
-- 冪等性: INSERT IGNORE。display_order のずらしは、その役職がまだ無いときだけ行う。
-- =====================================================================

-- FOLK_GUITAR の入る場所（ギター 2205 の次の 2206）を空ける。display_order は一意なので、後ろの行から順にずらす。
UPDATE `roles` SET `display_order` = `display_order` + 1, `updated_by` = 'migration'
 WHERE `display_order` BETWEEN 2206 AND 2299
   AND NOT EXISTS (SELECT 1 FROM (SELECT `role_code` FROM `roles` WHERE `role_code` = 'FOLK_GUITAR') AS `t`)
 ORDER BY `display_order` DESC;

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('FOLK_GUITAR', 'フォークギター', 'Folk Guitar', 'NORMAL', 'PERFORMANCE', 2206, 'migration', 'migration');

-- BASS_TROMBONE の入る場所（トロンボーン 2214 の次の 2215）を空ける。FOLK_GUITAR を入れた後の並びが前提。
UPDATE `roles` SET `display_order` = `display_order` + 1, `updated_by` = 'migration'
 WHERE `display_order` BETWEEN 2215 AND 2299
   AND NOT EXISTS (SELECT 1 FROM (SELECT `role_code` FROM `roles` WHERE `role_code` = 'BASS_TROMBONE') AS `t`)
 ORDER BY `display_order` DESC;

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('BASS_TROMBONE', 'バストロンボーン', 'Bass Trombone', 'NORMAL', 'PERFORMANCE', 2215, 'migration', 'migration');

-- PICCOLO の入る場所（フルート 2221 の次の 2222）を空ける。BASS_TROMBONE を入れた後の並びが前提。
UPDATE `roles` SET `display_order` = `display_order` + 1, `updated_by` = 'migration'
 WHERE `display_order` BETWEEN 2222 AND 2299
   AND NOT EXISTS (SELECT 1 FROM (SELECT `role_code` FROM `roles` WHERE `role_code` = 'PICCOLO') AS `t`)
 ORDER BY `display_order` DESC;

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('PICCOLO', 'ピッコロ', 'Piccolo', 'NORMAL', 'PERFORMANCE', 2222, 'migration', 'migration');
