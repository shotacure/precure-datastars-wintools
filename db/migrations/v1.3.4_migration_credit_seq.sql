-- ============================================================================
-- マイグレーション: credits テーブルへ明示順序カラム credit_seq を追加する
-- ----------------------------------------------------------------------------
-- 背景:
--   credits はクレジット階層（cards → tiers → groups → roles → blocks → entries）
--   の最上位テーブルだが、従来は順序を表す独立カラムを持たず、credit_kind の
--   暗黙順（OP→ED）に依存していた。これは OP より ED を先に流す回や、OP/ED 以外の
--   クレジットが同一スコープに増えた場合に表示順を表現できず、設計上の欠陥だった。
--   下位階層（card_seq, tier_no, group_no, order_in_group, block_seq, entry_seq）と
--   同様に、credits にも明示順序カラム credit_seq を持たせる。
--
-- 適用対象:
--   schema.sql v1.3.4 より前のスキーマで構築済みの既存データベース。
--   新規構築は schema.sql に同カラムが含まれるため本マイグレーションは不要。
--
-- 冪等性:
--   credit_seq カラムの存在を information_schema で確認し、未追加のときのみ
--   ALTER を実行する。再実行しても二重追加にならない。
--
-- 実行方法（例）:
--   mysql -u <user> -p precure_datastars < db/migration_credit_seq.sql
-- ============================================================================

SET @db := DATABASE();

-- ── 1) credit_seq カラムを追加（未追加時のみ） ───────────────────────────────
SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'credits' AND COLUMN_NAME = 'credit_seq'
);

SET @ddl := IF(@col_exists = 0,
  'ALTER TABLE `credits`
     ADD COLUMN `credit_seq` smallint unsigned NOT NULL DEFAULT 1 AFTER `credit_kind`',
  'SELECT 1');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

-- ── 2) 既存行へ初期 credit_seq を採番 ────────────────────────────────────────
-- 従来の暗黙順（OP=1 → ED=2 → その他は credit_kind 昇順）を踏襲して、
-- スコープ（同一 episode_id、または同一 series_id）ごとに 1 始まりで振り直す。
-- 既に手動で意味のある値が入っている可能性は無い（カラム新設直後のため
-- 全行が DEFAULT 1）なので、常に再採番して問題ない。
UPDATE `credits` c
JOIN (
  SELECT
    credit_id,
    ROW_NUMBER() OVER (
      PARTITION BY COALESCE(episode_id, -series_id)
      ORDER BY
        CASE credit_kind WHEN 'OP' THEN 1 WHEN 'ED' THEN 2 ELSE 999 END,
        credit_kind,
        COALESCE(part_type, ''),
        credit_id
    ) AS rn
  FROM `credits`
) ord ON ord.credit_id = c.credit_id
SET c.credit_seq = ord.rn;

-- ── 3) 一意制約を追加（未追加時のみ） ───────────────────────────────────────
-- 同一スコープ内で credit_seq の重複を許さない（表示順破綻の防止）。
SET @uq_series := (
  SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'credits'
    AND INDEX_NAME = 'uq_credit_series_seq'
);
SET @ddl := IF(@uq_series = 0,
  'ALTER TABLE `credits`
     ADD UNIQUE KEY `uq_credit_series_seq` (`series_id`,`credit_seq`)',
  'SELECT 1');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

SET @uq_episode := (
  SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'credits'
    AND INDEX_NAME = 'uq_credit_episode_seq'
);
SET @ddl := IF(@uq_episode = 0,
  'ALTER TABLE `credits`
     ADD UNIQUE KEY `uq_credit_episode_seq` (`episode_id`,`credit_seq`)',
  'SELECT 1');
PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;

-- 完了。
