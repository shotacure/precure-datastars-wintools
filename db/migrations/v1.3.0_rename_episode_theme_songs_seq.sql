-- =============================================================================
-- Migration: v1.3.0 — episode_theme_songs.insert_seq を seq にリネーム + 再採番
-- =============================================================================
-- 背景:
--   旧仕様：
--     - theme_kind = 'OP' / 'ED' は insert_seq = 0 固定
--     - theme_kind = 'INSERT' は insert_seq = 1, 2, 3, ...
--     - CHECK 制約 ck_ets_op_ed_no_insert_seq でこの排他を担保
--
--   新仕様：
--     - 列名は seq に変更（劇中で流れる順序を表す汎用カラム）
--     - 値は劇中順（OP/INSERT/ED に関係なく、エピソード単位で 1, 2, 3, ...）
--     - OP が冒頭に、ED が末尾に置かれるとは限らない作品があり得るため、
--       単純な「劇中順」という意味付けに統一する
--     - 既存値の再採番ルール：
--         OP        → seq = 1
--         INSERT(n) → 旧 insert_seq の昇順で seq = 2..N
--         ED        → seq = (max(seq) + 1)
--       これは多くの作品の典型的な流れに従う初期値。実際の流れと違う場合は
--       Catalog 側 GUI で個別に調整可能。
--
-- 実装手順:
--   (1) CHECK 制約 ck_ets_op_ed_no_insert_seq を DROP（新仕様では不要）
--   (2) 一時的なテーブル変数を経由して再採番のための seq 値を計算
--   (3) ALTER TABLE で insert_seq → seq へリネーム
--   (4) 計算済みの seq 値で UPDATE
--
--   PK が (episode_id, is_broadcast_only, theme_kind, insert_seq) の 4 列複合
--   なので、列名変更だけは ALTER TABLE CHANGE COLUMN で完結する（PK 定義は
--   列のリネームに自動追従する）。再採番は退避値経由ではなく、いったん全行を
--   一時テーブルに退避 → DELETE → 新値で INSERT し直すパターンで安全に行う。
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で列名が seq か insert_seq かを確認
--   - 既に seq に変更済みなら何もしない（再実行可）
-- =============================================================================

-- 列名の現在状態を確認
SET @col_name = (
  SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'episode_theme_songs'
    AND COLUMN_NAME IN ('insert_seq', 'seq')
  LIMIT 1
);

-- 既に seq になっていれば何もせずに終了
SET @already_done = IF(@col_name = 'seq', 1, 0);

-- ============================================================
-- ステップ 1: CHECK 制約の DROP（旧仕様のみ）
-- ============================================================
-- ck_ets_op_ed_no_insert_seq は theme_kind と insert_seq の組み合わせを縛るが、
-- 新仕様では seq は劇中順を表す汎用カラムなのでこの制約は不要。
-- 既存環境にしか存在しない可能性があるので INFORMATION_SCHEMA で確認してから DROP。

SET @has_check = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME       = 'episode_theme_songs'
    AND CONSTRAINT_NAME  = 'ck_ets_op_ed_no_insert_seq'
);

SET @stmt = IF(@has_check > 0,
  'ALTER TABLE `episode_theme_songs` DROP CHECK `ck_ets_op_ed_no_insert_seq`',
  'SELECT ''ck_ets_op_ed_no_insert_seq does not exist, skipping DROP'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ============================================================
-- ステップ 2: 列名 insert_seq → seq へリネーム
-- ============================================================
-- 既に seq になっていれば飛ばす。
-- TINYINT UNSIGNED NOT NULL のまま（型は変えない）。
SET @stmt = IF(@already_done = 0 AND @col_name = 'insert_seq',
  'ALTER TABLE `episode_theme_songs` CHANGE COLUMN `insert_seq` `seq` TINYINT UNSIGNED NOT NULL',
  'SELECT ''episode_theme_songs.seq already exists, skipping rename'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ============================================================
-- ステップ 3: 既存データの再採番
-- ============================================================
-- 各 (episode_id, is_broadcast_only) グループ内で、
--   OP        → seq = 1
--   INSERT    → 旧 seq の昇順で seq = 2..N
--   ED        → seq = (max + 1)
-- に振り直す。
--
-- PK が (episode_id, is_broadcast_only, theme_kind, seq) の 4 列複合のため、
-- 単純な UPDATE では PK 衝突する可能性がある（特に OP の旧 seq=0 → 新 seq=1 と
-- INSERT の旧 seq=1 → 新 seq=2 のチェーン更新）。
-- いったん全行を一時テーブルに退避 → DELETE → 新値で INSERT し直す方法で安全化する。
--
-- @already_done が 1 のときはスキップ（既に新仕様で運用中の DB）。

DROP TEMPORARY TABLE IF EXISTS `tmp_ets_renumber`;

-- 一時テーブルを作って新 seq を計算する（@already_done=0 のときだけ実体ある内容）
CREATE TEMPORARY TABLE `tmp_ets_renumber` (
  `episode_id`        int          NOT NULL,
  `is_broadcast_only` tinyint(1)   NOT NULL,
  `theme_kind`        varchar(8)   NOT NULL,
  `old_seq`           tinyint unsigned NOT NULL,
  `new_seq`           tinyint unsigned NOT NULL,
  `song_recording_id` int          NOT NULL,
  `notes`             text             NULL,
  `created_by`        varchar(64)      NULL,
  `updated_by`        varchar(64)      NULL,
  KEY (`episode_id`, `is_broadcast_only`)
);

-- 既に新仕様（@already_done=1）の場合、ここで挿入する元データが無いので空のまま終わる。
-- 旧仕様 → 新仕様 への再採番計算は @already_done=0 のときのみ実施。
SET @sql_populate = IF(@already_done = 0, '
  INSERT INTO `tmp_ets_renumber`
    (`episode_id`, `is_broadcast_only`, `theme_kind`, `old_seq`, `new_seq`,
     `song_recording_id`, `notes`, `created_by`, `updated_by`)
  SELECT
    e.`episode_id`,
    e.`is_broadcast_only`,
    e.`theme_kind`,
    e.`seq` AS `old_seq`,
    -- 新 seq の計算：theme_kind と旧 seq の組み合わせから決定
    -- OP        → 1
    -- INSERT(n) → 旧 seq + 1（旧仕様で 1..N だったので、+1 して 2..N+1）
    -- ED        → グループ内 INSERT 件数 + 2（OP の 1 と INSERT の 2..N+1 の次）
    CASE
      WHEN e.`theme_kind` = ''OP''     THEN 1
      WHEN e.`theme_kind` = ''INSERT'' THEN e.`seq` + 1
      WHEN e.`theme_kind` = ''ED''     THEN
        (SELECT COUNT(*) + 2
         FROM `episode_theme_songs` e2
         WHERE e2.`episode_id`        = e.`episode_id`
           AND e2.`is_broadcast_only` = e.`is_broadcast_only`
           AND e2.`theme_kind`        = ''INSERT'')
      ELSE e.`seq`
    END AS `new_seq`,
    e.`song_recording_id`,
    e.`notes`,
    e.`created_by`,
    e.`updated_by`
  FROM `episode_theme_songs` e
', 'SELECT 1');

PREPARE s FROM @sql_populate; EXECUTE s; DEALLOCATE PREPARE s;

-- 既存行を全削除してから一時テーブルから新 seq で書き戻す。
SET @sql_delete = IF(@already_done = 0,
  'DELETE FROM `episode_theme_songs` WHERE `episode_id` > 0',
  'SELECT 1');
PREPARE s FROM @sql_delete; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql_reinsert = IF(@already_done = 0, '
  INSERT INTO `episode_theme_songs`
    (`episode_id`, `is_broadcast_only`, `theme_kind`, `seq`,
     `song_recording_id`, `notes`, `created_by`, `updated_by`)
  SELECT
    `episode_id`, `is_broadcast_only`, `theme_kind`, `new_seq`,
    `song_recording_id`, `notes`, `created_by`, `updated_by`
  FROM `tmp_ets_renumber`
', 'SELECT 1');
PREPARE s FROM @sql_reinsert; EXECUTE s; DEALLOCATE PREPARE s;

DROP TEMPORARY TABLE IF EXISTS `tmp_ets_renumber`;

SELECT 'v1.3.0 migration completed: episode_theme_songs.insert_seq renamed to seq and renumbered (1..N in airing order)' AS final_status;
