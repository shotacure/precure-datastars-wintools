-- ============================================================
-- v1.3.0 ブラッシュアップ stage 16 Phase 4：
--   roles マスタに hide_role_name_in_credit 列を追加
-- ============================================================
--
-- HTML プレビュー（Catalog 側 CreditPreviewRenderer）と
-- 静的サイト生成（SiteBuilder 側 CreditTreeRenderer）の
-- クレジット階層描画で、特定役職について「左カラムの役職名表示」
-- だけを抑止するためのフラグ列。
--
-- 用途例：LABEL（レーベル）役職は、データ上は「マーベラス
-- エンターテイメント」という企業のクレジット関与を
-- 「LABEL 役職としての関与」として正規に保持したい一方で、
-- HTML 表示上はその直前の主題歌ブロックの末尾に並べて
-- 「レーベル」という役職名は出さずに屋号だけ出したい、
-- という運用要望に応える。
--
-- 関与集計（CreditInvolvementIndex）・役職別ランキング
-- （/stats/roles/{role_code}/）・企業詳細の関与一覧などは
-- すべて従来通り role_code='LABEL' でヒットする。本フラグは
-- 純粋に表示テンプレ側の挙動だけを切り替える。
--
-- 既定値は 0（=役職名を表示する従来動作）。LABEL 役職のみ
-- 1 にバックフィルする。他の役職を将来追加で非表示扱いに
-- したい場合は、Catalog のクレジット系マスタ管理 → 役職
-- タブのチェックボックス、または直接 UPDATE で切り替える。
-- ============================================================

ALTER TABLE roles
  ADD COLUMN hide_role_name_in_credit TINYINT NOT NULL DEFAULT 0
  COMMENT 'クレジット HTML 描画で役職名カラムを非表示にするか（0=表示, 1=非表示）。集計には影響しない'
  AFTER display_order;

-- LABEL 役職のみバックフィル。
-- roles マスタの中身は運用者が業務側で投入する方針のため、
-- 該当役職が存在しない環境では本 UPDATE が 0 行更新となり何もしない。
UPDATE roles
   SET hide_role_name_in_credit = 1,
       updated_by               = COALESCE(updated_by, 'migration_v1.3.0_brushup_phase4')
 WHERE role_code = 'LABEL';
