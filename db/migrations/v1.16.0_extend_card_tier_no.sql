-- =====================================================================
-- v1.16.0_extend_card_tier_no.sql
--
-- クレジットのカードのティア（credit_card_tiers.tier_no）を 1〜3 まで持てるようにする。
--
-- 2 列に並んだ役職の左の列・右の列と、その下の中央に置かれた役職のように、
-- 横位置の違う 3 つのまとまりを持つカード（フレッシュプリキュア！の ED など）を表すため、
-- 制約 ck_card_tier_no を BETWEEN 1 AND 2 から BETWEEN 1 AND 3 に広げる。
--
-- 冪等性: 制約を落としてから付け直すだけなので、何度流しても同じ状態になる
-- （MySQL 8.0.19 以降の ALTER TABLE ... DROP CHECK を使う）。
-- =====================================================================

ALTER TABLE `credit_card_tiers` DROP CHECK `ck_card_tier_no`;
ALTER TABLE `credit_card_tiers` ADD CONSTRAINT `ck_card_tier_no` CHECK ((`tier_no` BETWEEN 1 AND 3));
