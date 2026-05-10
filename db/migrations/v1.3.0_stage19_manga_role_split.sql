-- ============================================================================================
-- v1.3.0 stage 19: 連載クレジット整理 — 漫画家を MANGA 役職に分離
-- ============================================================================================
--
-- 背景:
--   従来 SERIALIZED_IN「連載」役職下に PERSON（漫画家）と COMPANY（雑誌名）が同居していて、
--   CreditInvolvementIndex の役職集計で漫画家が「連載」役職として誤集計されていた。
--   このマイグレーションは roles に新規役職 MANGA「漫画」を追加し、既存 SERIALIZED_IN 配下の
--   PERSON エントリをすべて MANGA 役職下に移送する。HTML 出力は SERIALIZED_IN テンプレが
--   {ROLE:MANGA.PERSONS} を経由して兄弟 MANGA 役職の人物を取り込む形で、画像のレイアウトを保つ。
--
-- 実行前提:
--   - 旧 v1.3.0 のスキーマ（roles, role_templates, credit_card_roles, credit_role_blocks,
--     credit_block_entries の構造）が存在している
--   - 現状 DB の SERIALIZED_IN クレジットは 4 件程度（小規模）。本 SQL はすべての SERIALIZED_IN
--     役職を機械的に処理する想定。
--
-- 冪等性:
--   既に MANGA 役職が存在する場合は INSERT も DML も再実行で重複しない（WHERE NOT EXISTS）。
--   再実行しても安全だが、初回実行後は何も変わらない。
--
-- ロールバック:
--   トランザクション内で完結する。途中で失敗すれば全部巻き戻る。
--
-- ============================================================================================

START TRANSACTION;

-- ──────────────────────────────────────────────────────────────────────────────────────────
-- Phase 1: roles マスタに MANGA 役職を追加（既存なら何もしない）
-- ──────────────────────────────────────────────────────────────────────────────────────────
--
-- name_en は「Manga」（ユーザー指定）。role_format_kind は NORMAL（人物名義の通常役職）。
-- hide_role_name_in_credit は 0（連載クレジット表示上は SERIALIZED_IN 側のテンプレに
-- {ROLE:MANGA.PERSONS} で取り込んで表示するため、MANGA 役職自身を独立に描画したい場合の
-- 役職名は通常通り「漫画」が出る）。
-- role_code が UNIQUE 制約を持っている想定で、ON DUPLICATE KEY UPDATE で「変更しない」
-- 形にすることで冪等性を確保する（INSERT IGNORE と同等だが、エラー抑止を明示）。

INSERT INTO roles (role_code, name_ja, name_en, role_format_kind, hide_role_name_in_credit,
                   created_at, updated_at, created_by, updated_by)
VALUES ('MANGA', '漫画', 'Manga', 'NORMAL', 0,
        NOW(), NOW(), 'migration_v1.3.0_s19', 'migration_v1.3.0_s19')
ON DUPLICATE KEY UPDATE role_code = role_code; -- no-op (冪等)

-- ──────────────────────────────────────────────────────────────────────────────────────────
-- Phase 2: role_templates に MANGA テンプレを追加 + SERIALIZED_IN テンプレを {ROLE:…} 経由に更新
-- ──────────────────────────────────────────────────────────────────────────────────────────
--
-- MANGA テンプレ: 単純に {PERSONS} だけ。MANGA 役職を独立にプレビューしたとき
-- （クレジット画面で MANGA だけのカードを作って表示確認したい等のレアケース）に備える。
-- 普段は SERIALIZED_IN 側からの {ROLE:MANGA.PERSONS} 経由で間接的に評価される。

INSERT INTO role_templates (role_code, series_id, format_template, notes,
                            created_at, updated_at, created_by, updated_by)
SELECT 'MANGA', NULL, '{PERSONS}', '兄弟役職参照（{ROLE:MANGA.PERSONS}）経由でも使用される',
       NOW(), NOW(), 'migration_v1.3.0_s19', 'migration_v1.3.0_s19'
WHERE NOT EXISTS (
    SELECT 1 FROM role_templates WHERE role_code = 'MANGA' AND series_id IS NULL
);

-- SERIALIZED_IN テンプレを {ROLE:MANGA.PERSONS} 経由版に更新。
-- 旧テンプレ:
--   {#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
--   <strong>漫画</strong>・{PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
--   　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
-- 新テンプレ:
--   {#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
--   <strong>漫画</strong>・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
--   　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
--
-- 変更点は中央 1 行の {PERSONS} → {ROLE:MANGA.PERSONS} のみ。
-- マイグレーション後の SERIALIZED_IN 配下には PERSON エントリが存在しないため、
-- 仮に旧テンプレ（{PERSONS}）のままだと「漫画・」だけが残って空表示になる。
-- 新テンプレで MANGA 役職側の PERSON を取り込むことで連続表示を保つ。

UPDATE role_templates
SET format_template = '{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」\r\n<strong>漫画</strong>・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}\r\n　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか',
    updated_at = NOW(),
    updated_by = 'migration_v1.3.0_s19'
WHERE role_code = 'SERIALIZED_IN'
  AND series_id IS NULL
  AND format_template NOT LIKE '%{ROLE:MANGA.PERSONS}%'; -- 既に更新済みなら何もしない（冪等）

-- ──────────────────────────────────────────────────────────────────────────────────────────
-- Phase 3: 既存 SERIALIZED_IN 配下の PERSON エントリを MANGA 役職に移送
-- ──────────────────────────────────────────────────────────────────────────────────────────
--
-- 処理単位は SERIALIZED_IN 役職（credit_card_roles.card_role_id）1 件ごと。
-- 各 SERIALIZED_IN 役職について:
--   A. 同 card_group_id 配下に「MANGA 役職」が無ければ作る（order_in_group は
--      SERIALIZED_IN.order_in_group + 1）。
--   B. MANGA 役職下に Block を 1 つ作る（block_seq=1, col_count=1, leading_company_alias_id=NULL）。
--   C. SERIALIZED_IN 役職下の PERSON エントリすべてを、その新 Block に block_id 付け替えで移送。
--      entry_seq は移送時に 1..N で振り直す。
--   D. 移送後、SERIALIZED_IN 役職下の credit_role_blocks のうちエントリが 0 件かつ
--      leading_company_alias_id が NULL の Block は削除（PERSON だけが入っていた空 Block を掃除）。
--
-- ループ実装: MySQL のストアド機能を使わずに、SET ベースで全 SERIALIZED_IN 役職を一気に処理する。
-- 4 件程度の小規模データのみを想定するので、PROCEDURE 化はしない。

-- ─── 3-A: MANGA 役職を必要分だけ作成 ─────────────────────────────────────────
-- 既存 SERIALIZED_IN 役職と同じ card_group_id で MANGA 役職がまだ無いものについて作る。
-- order_in_group は SERIALIZED_IN の +1（連載の直後に表示順を置く）。
-- 同 group 内で「order_in_group の重複」を避けるため、既存の同 group・同 order_in_group の役職を
-- まず +10 シフトして空ける運用も考えられるが、現状連載グループは SERIALIZED_IN 単独で
-- 他役職と同居しない前提（実データ確認済み）なので、+1 だけで衝突しない想定。

INSERT INTO credit_card_roles (card_group_id, role_code, order_in_group, notes,
                               created_at, updated_at, created_by, updated_by)
SELECT s.card_group_id, 'MANGA', s.order_in_group + 1, NULL,
       NOW(), NOW(), 'migration_v1.3.0_s19', 'migration_v1.3.0_s19'
FROM credit_card_roles s
WHERE s.role_code = 'SERIALIZED_IN'
  AND NOT EXISTS (
      SELECT 1 FROM credit_card_roles m
      WHERE m.card_group_id = s.card_group_id AND m.role_code = 'MANGA'
  );

-- ─── 3-B: MANGA 役職下に新規 Block を作成 ────────────────────────────────────
-- 各 MANGA 役職に Block が無ければ追加（block_seq=1）。col_count=1 / leading=NULL の素朴な Block。

INSERT INTO credit_role_blocks (card_role_id, block_seq, col_count, leading_company_alias_id, notes,
                                created_at, updated_at, created_by, updated_by)
SELECT m.card_role_id, 1, 1, NULL, NULL,
       NOW(), NOW(), 'migration_v1.3.0_s19', 'migration_v1.3.0_s19'
FROM credit_card_roles m
WHERE m.role_code = 'MANGA'
  AND NOT EXISTS (
      SELECT 1 FROM credit_role_blocks b WHERE b.card_role_id = m.card_role_id
  );

-- ─── 3-C: PERSON エントリを MANGA Block に移送 ──────────────────────────────
-- 元エントリの entry_seq を捨て、移送先 Block 内で 1..N に振り直す。
-- 同一 Block 内で entry_seq に UNIQUE が無い想定（既存スキーマでも UNIQUE 制約は無い）。
-- 同じ SERIALIZED_IN 役職に複数 Block があり、それぞれに PERSON が散らばっている場合、
-- すべて統合して MANGA 役職の Block #1 にまとめる（連載クレジットでは「漫画家」は通常 1 名 or
-- 共同名義の数名なので、1 Block に集約しても表示は壊れない）。
--
-- 一旦移送先 block_id を一時カラム代わりに変数で持つ実装が美しいが、MySQL のクライアント側変数で
-- 完結させる方が SQL 1 ファイルで完結して可読性が高い。各 SERIALIZED_IN 役職について
-- 「同 card_group 配下の MANGA 役職の Block #1」を特定する形で 1 UPDATE 文に縮める。

UPDATE credit_block_entries e
JOIN credit_role_blocks bSrc      ON e.block_id = bSrc.block_id
JOIN credit_card_roles  srcRole   ON bSrc.card_role_id = srcRole.card_role_id
JOIN credit_card_roles  mangaRole ON mangaRole.card_group_id = srcRole.card_group_id
                                  AND mangaRole.role_code = 'MANGA'
JOIN credit_role_blocks mangaBlk  ON mangaBlk.card_role_id = mangaRole.card_role_id
                                  AND mangaBlk.block_seq = 1
SET e.block_id = mangaBlk.block_id,
    e.updated_at = NOW(),
    e.updated_by = 'migration_v1.3.0_s19'
WHERE srcRole.role_code = 'SERIALIZED_IN'
  AND e.entry_kind = 'PERSON';

-- entry_seq の振り直し（移送後の MANGA Block 内で 1..N 連番）。
-- ウィンドウ関数 ROW_NUMBER() で再採番。MySQL 8.0+ 前提。

UPDATE credit_block_entries e
JOIN (
    SELECT entry_id,
           ROW_NUMBER() OVER (PARTITION BY block_id ORDER BY entry_id) AS new_seq
    FROM credit_block_entries
    WHERE updated_by = 'migration_v1.3.0_s19'
) reseq ON e.entry_id = reseq.entry_id
SET e.entry_seq = reseq.new_seq;

-- ─── 3-D: SERIALIZED_IN 役職下で空になった Block を削除 ───────────────────────
-- PERSON を抜いた結果、エントリ 0 件かつ leading_company_alias_id IS NULL の Block は
-- 「PERSON だけが入っていたが移送で空になった」Block なので削除する。
-- leading_company_alias_id が非 NULL の Block（先頭企業屋号だけがあって PERSON が混在していた、
-- というあまり起き得ないケース）は安全側に倒して残す。

DELETE bDel FROM credit_role_blocks bDel
JOIN credit_card_roles srcRole ON bDel.card_role_id = srcRole.card_role_id
WHERE srcRole.role_code = 'SERIALIZED_IN'
  AND bDel.leading_company_alias_id IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM credit_block_entries eExist WHERE eExist.block_id = bDel.block_id
  );

-- ─── 3-E: SERIALIZED_IN 役職下 Block の block_seq 振り直し ────────────────────
-- 空 Block を削除した後で block_seq に欠番が出ているので、各 SERIALIZED_IN 役職について
-- block_seq を 1..N で振り直す。

UPDATE credit_role_blocks b
JOIN (
    SELECT block_id,
           ROW_NUMBER() OVER (PARTITION BY card_role_id ORDER BY block_seq, block_id) AS new_seq
    FROM credit_role_blocks
    WHERE card_role_id IN (
        SELECT card_role_id FROM credit_card_roles WHERE role_code = 'SERIALIZED_IN'
    )
) reseq ON b.block_id = reseq.block_id
SET b.block_seq = reseq.new_seq;

COMMIT;

-- ============================================================================================
-- 動作確認:
--
-- 1) MANGA 役職が roles マスタに存在することを確認
--      SELECT * FROM roles WHERE role_code = 'MANGA';
--
-- 2) role_templates に MANGA / SERIALIZED_IN（更新版）が存在することを確認
--      SELECT role_code, format_template FROM role_templates
--        WHERE role_code IN ('MANGA', 'SERIALIZED_IN') AND series_id IS NULL;
--
-- 3) 全 SERIALIZED_IN 役職について、同 card_group_id に MANGA 役職が 1 対 1 で存在することを確認
--      SELECT s.card_group_id, COUNT(m.card_role_id) AS manga_role_count
--        FROM credit_card_roles s
--        LEFT JOIN credit_card_roles m ON m.card_group_id = s.card_group_id AND m.role_code = 'MANGA'
--        WHERE s.role_code = 'SERIALIZED_IN'
--        GROUP BY s.card_group_id;
--
-- 4) SERIALIZED_IN 配下に PERSON エントリが 1 件も無いことを確認
--      SELECT COUNT(*) AS person_in_serialized_in
--        FROM credit_block_entries e
--        JOIN credit_role_blocks b ON e.block_id = b.block_id
--        JOIN credit_card_roles r  ON b.card_role_id = r.card_role_id
--        WHERE r.role_code = 'SERIALIZED_IN' AND e.entry_kind = 'PERSON';
--      -- 期待値: 0
--
-- 5) MANGA 配下に PERSON エントリが存在することを確認（移送が成功している）
--      SELECT r.card_role_id, COUNT(e.entry_id) AS person_count
--        FROM credit_card_roles r
--        JOIN credit_role_blocks b ON b.card_role_id = r.card_role_id
--        JOIN credit_block_entries e ON e.block_id = b.block_id
--        WHERE r.role_code = 'MANGA' AND e.entry_kind = 'PERSON'
--        GROUP BY r.card_role_id;
-- ============================================================================================
