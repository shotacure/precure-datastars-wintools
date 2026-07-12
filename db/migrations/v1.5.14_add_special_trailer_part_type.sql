-- =====================================================================
-- v1.5.14_add_special_trailer_part_type.sql
--
-- 「特別予告」パート種別（SPECIAL_TRAILER）を part_types マスタに追加する。
-- 予告系（予告 / 映画予告 / 新番組予告）に連なる新種別。同一エピソード内に
-- 複数回出現しうる運用（各種告知 NOTICE と同様）のため singleton_per_episode = 0。
-- display_order は既存最大（22）の次、末尾 23（並び順はエディタ・統計のカタログ
-- 表示専用で、エピソードページのパート描画順は episode_seq 依存＝本値の影響を受けない）。
--
-- 冪等性: part_type は PK。既存時は表示名・並び順・区分を上書きするだけで、
-- 再実行しても結果は同一。
-- =====================================================================

INSERT INTO `part_types`
  (`part_type`, `name_ja`, `name_en`, `display_order`, `default_credit_kind`, `singleton_per_episode`)
VALUES
  ('SPECIAL_TRAILER', '特別予告', 'special trailer', 23, NULL, 0)
ON DUPLICATE KEY UPDATE
  `name_ja`               = VALUES(`name_ja`),
  `name_en`               = VALUES(`name_en`),
  `display_order`         = VALUES(`display_order`),
  `default_credit_kind`   = VALUES(`default_credit_kind`),
  `singleton_per_episode` = VALUES(`singleton_per_episode`);
