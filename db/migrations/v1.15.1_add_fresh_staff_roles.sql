-- =====================================================================
-- v1.15.1_add_fresh_staff_roles.sql
--
-- TV のクレジット（フレッシュプリキュア！の ED）で使われる役職を追加する。
--   COSTUME_DESIGN             コスチュームデザイン。演技事務（CASTING_MANAGER, 780）の次に並べる。
--   ENDING_THEME_CHOREOGRAPHY  エンディングテーマ振り付け。ダンス振付（DANCE_CHOREOGRAPHY, 790）の次に並べる。
--                              画面の表記どおりの役職として持ち、ダンス振付とは系譜でつながない。
--
-- 冪等性: INSERT IGNORE。display_order は空いている番号を使うので、ずらしは行わない。
-- =====================================================================

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `display_order`, `created_by`, `updated_by`) VALUES
  ('COSTUME_DESIGN',            'コスチュームデザイン',       'Costume Design',            'NORMAL', 785, 'migration', 'migration'),
  ('ENDING_THEME_CHOREOGRAPHY', 'エンディングテーマ振り付け', 'Ending Theme Choreography', 'NORMAL', 791, 'migration', 'migration');
