-- =====================================================================
-- v1.14.1_add_music_credit_roles.sql
--
-- 音楽クレジットの役職を追加する。
--   TRACKDOWN_STUDIO           トラックダウンスタジオ（レコーディングの区分）。録音とトラックダウン（ミックス）を
--                              別のスタジオで行った盤で、トラックダウンの側を持つ。
--   DISC_EXECUTIVE_PRODUCER    エグゼクティブプロデューサー（音盤製作の区分）。盤の製作側の総責任者。
--                              本編クレジットの EXECUTIVE_PRODUCER とは別の役職にして、音楽クレジットの区分を持たせる。
--
-- あわせて SYNTH_OPERATION の表示名を「シンセサイザー」に縮める（「シンセサイザー・オペレート」は長いため）。
--
-- 冪等性: INSERT IGNORE / 同じ値への UPDATE。
-- =====================================================================

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('TRACKDOWN_STUDIO',        'トラックダウンスタジオ',       'Trackdown Studio',   'NORMAL', 'RECORDING', 2308, 'migration', 'migration'),
  ('DISC_EXECUTIVE_PRODUCER', 'エグゼクティブプロデューサー', 'Executive Producer', 'NORMAL', 'RELEASE',   2407, 'migration', 'migration');

UPDATE `roles` SET `name_ja` = 'シンセサイザー', `updated_by` = 'migration' WHERE `role_code` = 'SYNTH_OPERATION';
