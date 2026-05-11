-- =============================================================================
-- Migration: v1.3.0 — bgm_cues テーブルに seq_in_session 列を追加
-- =============================================================================
-- 背景:
--   劇伴の音源（bgm_cues）はセッション単位で並び順を持たせたい。
--   M 番号は文字列としてソートすると "M1, M10, M11, M2, ..." のような
--   不自然な並びになるため、初期投入時には数字部分を抽出して数値ソートする。
--   さらに枝番（"M-7-2"、"M3a" など）が付いた行は、付かない素の番号より後ろに
--   並べる。並び替えは GUI（Catalog 側の劇伴管理画面）から DnD で随時更新可能。
--
-- 対象列:
--   bgm_cues.seq_in_session  INT  NOT NULL  DEFAULT 0
--
-- 自然順ロジック:
--   1) m_no_detail 文字列を「数字 / 非数字」のトークン列に分解
--   2) 数字トークンは数値として比較（"M1" の 1、"M10" の 10）
--   3) 非数字トークンは文字列として比較（"M-7-2" の "M-" や "-"）
--   4) 「枝番無し < 枝番有り」を実現するため、枝番判定: 数字トークン以降に
--      非数字（ハイフン・英字等）が続けば枝番ありとみなす
--
-- 実装の都合:
--   MySQL の純粋な SQL だけでは複雑な自然順ソートは難しいため、
--   stored procedure を使って各セッションごとに並べ替え + 連番を振る。
--   procedure はこのマイグレ実行後に DROP するので残らない。
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で列の存在を確認
--   - 初期投入は seq_in_session = 0 の行のみが対象（再実行しても上書きしない）
-- =============================================================================

-- 列の存在を確認
SET @has_seq = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'bgm_cues'
    AND COLUMN_NAME  = 'seq_in_session'
);

-- 列が無ければ追加。session_no の直後に置く。
SET @stmt = IF(@has_seq = 0,
  'ALTER TABLE `bgm_cues` ADD COLUMN `seq_in_session` INT NOT NULL DEFAULT 0 AFTER `session_no`',
  'SELECT ''bgm_cues.seq_in_session already exists, skipping ALTER'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

-- 初期投入用ストアドプロシージャ。
-- m_no_detail を自然順 + 枝番無し優先でソートして、各セッション内で 1, 2, 3 ... の連番を振る。
DROP PROCEDURE IF EXISTS `sp_bgm_cues_init_seq_in_session`;

DELIMITER $$

CREATE PROCEDURE `sp_bgm_cues_init_seq_in_session`()
BEGIN
  -- 全セッションを 1 つずつ走査して順位付けする。
  -- 自然順ソートは「m_no_detail 内の最初の数字トークンを数値抽出した値」を主キーとし、
  -- そのあと「枝番が付いているか（has_branch）」、最後に m_no_detail 文字列そのものでタイブレーク。
  --
  -- has_branch の判定:
  --   m_no_detail から先頭の数字トークン部分を取り除いた残りに、英字・ハイフン・空白以外の
  --   何かしらの非空文字が含まれていれば「枝番あり」とみなす。素朴には、
  --   先頭の数字以外の文字 + 数字部分 を取り除いた残り（suffix）が空でなければ枝番あり。
  --
  -- MySQL 8 の REGEXP_SUBSTR / REGEXP_REPLACE を使う。
  --
  -- セッションごとに連番を振り直すため、ROW_NUMBER() OVER (PARTITION BY ...) を使う。

  UPDATE `bgm_cues` AS t
  JOIN (
    SELECT
      `series_id`,
      `m_no_detail`,
      ROW_NUMBER() OVER (
        PARTITION BY `series_id`, `session_no`
        ORDER BY
          -- 1) 主キー：先頭の数字トークンを数値抽出した値
          CAST(
            COALESCE(
              REGEXP_SUBSTR(`m_no_detail`, '[0-9]+'),
              '0'
            ) AS UNSIGNED
          ) ASC,
          -- 2) 枝番無し優先：suffix（先頭の非数字 + 先頭数字を取り除いた残り）が空なら 0、
          --    残りがあれば 1。これにより M-7 (suffix="") < M-7-2 (suffix="-2")、
          --    M3 (suffix="") < M3a (suffix="a") となる。
          CASE WHEN
            REGEXP_REPLACE(
              REGEXP_REPLACE(`m_no_detail`, '^[^0-9]*', ''),
              '^[0-9]+', ''
            ) = ''
          THEN 0 ELSE 1 END ASC,
          -- 3) suffix の文字列そのもの（枝番有り同士の安定ソート用）
          REGEXP_REPLACE(
            REGEXP_REPLACE(`m_no_detail`, '^[^0-9]*', ''),
            '^[0-9]+', ''
          ) ASC,
          -- 4) 最後のタイブレーク：m_no_detail 全体
          `m_no_detail` ASC
      ) AS rn
    FROM `bgm_cues`
    WHERE `is_deleted` = 0
      AND `seq_in_session` = 0
  ) AS r
    ON  r.`series_id`   = t.`series_id`
    AND r.`m_no_detail` = t.`m_no_detail`
  SET t.`seq_in_session` = r.rn
  WHERE t.`is_deleted` = 0
    AND t.`seq_in_session` = 0
    AND t.`series_id` > 0;
END$$

DELIMITER ;

-- 実行 → 後始末
CALL `sp_bgm_cues_init_seq_in_session`();
DROP PROCEDURE IF EXISTS `sp_bgm_cues_init_seq_in_session`;

SELECT 'v1.3.0 migration completed: bgm_cues.seq_in_session added and natural-sorted' AS final_status;
