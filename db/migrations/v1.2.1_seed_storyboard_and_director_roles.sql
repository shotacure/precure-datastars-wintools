-- =============================================================================
-- Migration: v1.2.1 — roles マスタに STORYBOARD と EPISODE_DIRECTOR をシード投入
-- =============================================================================
-- 背景:
--   v1.2.0 工程 H 時点では「役職マスタは初期投入しない」方針だったが、v1.2.1 で
--   追加されたシリーズフラグ hide_storyboard_role は「STORYBOARD（絵コンテ）」と
--   「EPISODE_DIRECTOR（演出）」という具体的な role_code を参照する専用ロジックを
--   プレビューレンダラに実装する都合上、これら 2 役職だけはマイグレーションで
--   初期シードする方針に切り替える。
--
--   他の役職（脚本／作画監督／美術監督／声の出演 等）の取り扱いは従来どおり
--   「役職マスタに必要な行を運用者が後から登録する」運用を維持する。
--
-- シード内容:
--   STORYBOARD       絵コンテ        Storyboard           (NORMAL書式)
--   EPISODE_DIRECTOR 演出            Episode Director     (NORMAL書式)
--
--   display_order は 100 / 110（10 単位飛び番運用）。実運用での並び順は他役職を
--   登録するときに DnD 等で調整される想定。
--
-- 本スクリプトは INSERT ... ON DUPLICATE KEY UPDATE で冪等。既に同 role_code が
-- 存在する場合は表示名や書式区分を上書きせず（既存運用を尊重）、display_order だけ
-- 未設定（NULL）であれば埋める動作とする。再実行に強い設計。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

INSERT INTO `roles`
  (`role_code`,        `name_ja`,    `name_en`,         `role_format_kind`, `display_order`, `notes`,
   `created_by`,       `updated_by`)
VALUES
  ('STORYBOARD',       '絵コンテ',   'Storyboard',      'NORMAL',           100,             'v1.2.1 シード。絵コンテ単体表示の役職。シリーズ別フラグ hide_storyboard_role が ON のシリーズではプレビューで演出と融合表示される。',
   'v1.2.1_seed',      'v1.2.1_seed'),
  ('EPISODE_DIRECTOR', '演出',       'Episode Director','NORMAL',           110,             'v1.2.1 シード。各話演出の役職。',
   'v1.2.1_seed',      'v1.2.1_seed')
ON DUPLICATE KEY UPDATE
  -- 既存の name_ja / name_en / role_format_kind / notes は上書きしない（運用者が変更している場合があるため）。
  -- display_order が NULL の場合のみ補填する。
  display_order = COALESCE(`display_order`, VALUES(`display_order`)),
  updated_by    = VALUES(updated_by);

SELECT 'v1.2.1 migration completed: STORYBOARD / EPISODE_DIRECTOR seeded into roles.' AS status;
