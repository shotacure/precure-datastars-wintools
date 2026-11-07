-- =====================================================================
-- v1.20.0_entry_character_display_text_omitted_head.sql
--
-- credit_block_entries.character_display_text（画面の組みどおりの役名）の説明を、今の使い方に合わせる。
--   役名が画面で折れているとき（改行入り。改行を抜くと名義と同じ）に加えて、
--   画面の役名が名義の頭を省いた後ろの部分だけのとき（双子の姓を省いた「みつき」＝名義「菓彩みつき」など）にも使う。
--
-- 列の型・照合順序は変えない。何度流しても同じ結果になる。
-- =====================================================================

ALTER TABLE `credit_block_entries`
  MODIFY COLUMN `character_display_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
    COMMENT '画面の組みどおりの役名。役名が画面で折れているとき（改行入り。改行を抜いた字はキャラの名義と同じ）か、画面が名義の頭を省いた後ろの部分だけのとき（名義より短い）。NULL は名義の名前のまま';
