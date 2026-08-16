-- =====================================================================
-- v1.6.5_add_cow_catcher_part_type.sql
--
-- 「カウキャッチャー」パート種別（COW_CATCHER）を part_types マスタに追加する。
-- 番組本編の頭（アバンタイトルより前）に置かれる CM 枠を表す種別で、
-- 同一エピソード内に 2 度は出現しないため singleton_per_episode = 1。
-- display_order は既存最大（23）の次、末尾 24（並び順はエディタ・統計のカタログ
-- 表示専用で、エピソードページのパート描画順は episode_seq 依存＝本値の影響を受けない）。
--
-- 冪等性: part_type は PK。既存時は表示名・並び順・区分を上書きするだけで、
-- 再実行しても結果は同一。
-- =====================================================================

INSERT INTO `part_types`
  (`part_type`, `name_ja`, `name_en`, `display_order`, `default_credit_kind`, `singleton_per_episode`)
VALUES
  ('COW_CATCHER', 'カウキャッチャー', 'cow catcher', 24, NULL, 1)
ON DUPLICATE KEY UPDATE
  `name_ja`               = VALUES(`name_ja`),
  `name_en`               = VALUES(`name_en`),
  `display_order`         = VALUES(`display_order`),
  `default_credit_kind`   = VALUES(`default_credit_kind`),
  `singleton_per_episode` = VALUES(`singleton_per_episode`);
