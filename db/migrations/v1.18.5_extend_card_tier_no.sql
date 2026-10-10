-- =====================================================================
-- v1.18.5_extend_card_tier_no.sql
--
-- クレジットのカードのティア（credit_card_tiers.tier_no）を 1〜9 まで持てるようにする。
--
-- 流れるクレジット（ROLL）は 1 枚のカードの中で役職の横位置が何度も変わる（左 → 右 → 左 …）ので、
-- 横位置のまとまりごとに分けるティアが 3 つでは足りない（DANCE LIVE の ED は 7 段）。
-- 制約 ck_card_tier_no を BETWEEN 1 AND 3 から BETWEEN 1 AND 9 に広げる。
--
-- 冪等性: 制約を落としてから付け直すだけなので、何度流しても同じ状態になる
-- （MySQL 8.0.19 以降の ALTER TABLE ... DROP CHECK を使う）。
-- =====================================================================

ALTER TABLE `credit_card_tiers` DROP CHECK `ck_card_tier_no`;
ALTER TABLE `credit_card_tiers` ADD CONSTRAINT `ck_card_tier_no` CHECK ((`tier_no` BETWEEN 1 AND 9));
