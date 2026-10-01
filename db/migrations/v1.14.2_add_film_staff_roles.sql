-- =====================================================================
-- v1.14.2_add_film_staff_roles.sql
--
-- 映画のクレジットで使われる役職を追加する。
--   COMPOSITING_DIRECTOR   撮影監督。デジタル撮影監督（DIGITAL_COMPOSITING_DIRECTOR）の次に並べる。
--   CG_SUPERVISOR          CG監督。CGディレクター（CG_DIRECTOR）の次に並べる。
--   SPECIAL_EFFECTS        特殊効果。デジタル特殊効果（DIGITAL_SPECIAL_EFFECTS）の次に並べる。
-- あわせて役職の系譜（role_successions）で、それぞれを近い既存の役職と同じ系列につなぐ
-- （集計では系列の代表役職にまとめて数える）。
--
-- 冪等性: INSERT IGNORE。display_order は空いている番号を使うので、ずらしは行わない。
-- =====================================================================

INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `display_order`, `created_by`, `updated_by`) VALUES
  ('CG_SUPERVISOR',        'CG監督',   'CG Supervisor',        'NORMAL',  653, 'migration', 'migration'),
  ('COMPOSITING_DIRECTOR', '撮影監督', 'Compositing Director', 'NORMAL',  971, 'migration', 'migration'),
  ('SPECIAL_EFFECTS',      '特殊効果', 'Special Effects',      'NORMAL', 1021, 'migration', 'migration');

INSERT IGNORE INTO `role_successions` (`from_role_code`, `to_role_code`, `created_by`, `updated_by`) VALUES
  ('CG_DIRECTOR',                  'CG_SUPERVISOR',        'migration', 'migration'),
  ('DIGITAL_COMPOSITING_DIRECTOR', 'COMPOSITING_DIRECTOR', 'migration', 'migration'),
  ('DIGITAL_SPECIAL_EFFECTS',      'SPECIAL_EFFECTS',      'migration', 'migration');
