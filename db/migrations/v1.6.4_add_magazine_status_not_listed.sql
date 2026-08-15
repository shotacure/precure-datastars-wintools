-- =====================================================================
-- v1.6.4_add_magazine_status_not_listed.sql
--
-- episodes.magazine_subtitle_status に NOT_LISTED（掲載なし）を追加する。
--
-- 背景:
--   誌面を確認した結果「その号に当該作品の枠自体が無かった」ケースが実在する
--   （例: アニメディア 2025年2月号 は 1/10〜2/9 を扱う号だが、2/2 放送開始の
--   『キミとアイドルプリキュア♪』の枠が一切なく、前作の最終回 1/26 までで
--   リストが終わっている。一方 2026年2月号 は同じ状況で 2/1・2/8 の枠を
--   前作の話数として確保し「非公開」と記載していた）。
--
--   これは「まだ誌面を確認していない」を表す NULL とは別の事実で、
--   確認済みの一次情報として保持する価値があるため独立した値にする。
--     NULL          ... データなし（未調査。サイトには何も出さない）
--     PUBLISHED     ... 掲載（誌面にサブタイトルが載った）
--     NOT_DISCLOSED ... 非公開（誌面で「サブタイトル非公開」と案内された）
--     UNDECIDED     ... 未定（誌面で「未定」と案内された）
--     NOT_LISTED    ... 掲載なし（誌面に当該作品の枠自体が無かった）★今回追加
--
-- サブタイトル未確定（title_text NULL）の許容:
--   掲載枠が無い以上その号でサブタイトルは判明しないため、NOT_LISTED も
--   NOT_DISCLOSED / UNDECIDED と同じく title_text NULL を許す側に入れる。
--   CHECK 制約 ck_ep_title_or_magazine_reason を張り替えて反映する。
--
-- 冪等性: ENUM 定義と CHECK 定義の現状を INFORMATION_SCHEMA で確認してから
--         ALTER する。既に適用済みなら何もしない。
-- =====================================================================

START TRANSACTION;

-- ---------------------------------------------------------------------
-- 1) ENUM に NOT_LISTED を追加（未追加のときだけ）
-- ---------------------------------------------------------------------
SET @needs_enum = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'episodes'
    AND COLUMN_NAME  = 'magazine_subtitle_status'
    AND COLUMN_TYPE NOT LIKE '%NOT_LISTED%'
);
SET @stmt = IF(@needs_enum = 1,
  "ALTER TABLE `episodes`
     MODIFY COLUMN `magazine_subtitle_status`
       ENUM('PUBLISHED','NOT_DISCLOSED','UNDECIDED','NOT_LISTED') DEFAULT NULL
       COMMENT 'アニメ雑誌でのサブタイトル掲載状態（NULL=データなし）'",
  "SELECT 'episodes.magazine_subtitle_status already has NOT_LISTED, skipping ALTER' AS msg");
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- ---------------------------------------------------------------------
-- 2) CHECK 制約の張り替え（NOT_LISTED も「サブタイトル無しを許す」側に入れる）
--    既存制約を落としてから同名で作り直す。
-- ---------------------------------------------------------------------
SET @has_ck = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE()
    AND TABLE_NAME        = 'episodes'
    AND CONSTRAINT_NAME   = 'ck_ep_title_or_magazine_reason'
);
SET @stmt = IF(@has_ck = 1,
  "ALTER TABLE `episodes` DROP CHECK `ck_ep_title_or_magazine_reason`",
  "SELECT 'ck_ep_title_or_magazine_reason not found, skipping DROP' AS msg");
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

ALTER TABLE `episodes`
  ADD CONSTRAINT `ck_ep_title_or_magazine_reason`
  CHECK ((`title_text` IS NOT NULL)
      OR (COALESCE(`magazine_subtitle_status`, '') IN ('NOT_DISCLOSED','UNDECIDED','NOT_LISTED')));

COMMIT;

SELECT 'v1.6.4 migration completed: magazine_subtitle_status += NOT_LISTED' AS final_status;
