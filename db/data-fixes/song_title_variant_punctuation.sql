-- 楽曲タイトル / variant_label の記号正規化（表記揺れ解消）
--
-- 方針:
--   ・語中の音引き（ビミョ〜 等）          → 波ダッシュ U+301C 〜（日本語の波線として正）
--   ・囲み/範囲（曲名側 ~Subtitle~ 等）     → 半角チルダ ~（開きチルダ直前に半角SPが無ければ追加）
--   ・！は据え置き（公式タイトルの一部）/ ？ は元から半角
--   ・シリーズタイトル（series）は対象外（公式維持）
--   ・variant_label が曲名と完全一致する冗長行は NULL 化（suffix 化）
--
-- 文字コード: ～ U+FF5E = EFBD9E / 〜 U+301C = E3809C / ~ U+007E = 7E
-- 列ごとに照合順序が異なるため REPLACE/INSTR は COLLATE utf8mb4_bin で統一する。

START TRANSACTION;

-- ===== Part 0: variant_label が曲名と一致する冗長行を NULL 化 =====
UPDATE song_recordings
SET variant_label = NULL
WHERE song_recording_id IN (711, 722, 730, 731);

-- ===== Part 1: songs.title — 語中の音引き ～(FF5E) → 波ダッシュ 〜(301C) =====
-- 各行の FF5E は語中の1箇所のみ（囲みFF5Eは含まない）ため REPLACE で確定的に置換できる。
UPDATE songs
SET title = REPLACE(title COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin, _utf8mb4 X'E3809C' COLLATE utf8mb4_bin)
WHERE song_id IN (7, 23, 278, 308, 458, 461, 499, 553, 482, 493);

-- ===== Part 2: songs.title — 囲み ～(FF5E) → 半角 ~（前SP既存の行はここで完結） =====
UPDATE songs
SET title = REPLACE(title COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin, _utf8mb4 X'7E' COLLATE utf8mb4_bin)
WHERE song_id IN (466, 484, 532, 533, 534, 535, 560, 580);

-- ===== Part 3: songs.title — 囲み 〜(301C) → 半角 ~（前SP追加は Part 4 で） =====
UPDATE songs
SET title = REPLACE(title COLLATE utf8mb4_bin, _utf8mb4 X'E3809C' COLLATE utf8mb4_bin, _utf8mb4 X'7E' COLLATE utf8mb4_bin)
WHERE song_id IN (252, 489, 491);

-- ===== Part 4: songs.title — 開きチルダ直前の半角SP追加（半角~化後の確定パターンを置換） =====
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'Love~'    COLLATE utf8mb4_bin, 'Love ~'    COLLATE utf8mb4_bin) WHERE song_id = 466;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, '！~'      COLLATE utf8mb4_bin, '！ ~'      COLLATE utf8mb4_bin) WHERE song_id = 484;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'Part3~'   COLLATE utf8mb4_bin, 'Part3 ~'   COLLATE utf8mb4_bin) WHERE song_id = 560;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'SWORD~'   COLLATE utf8mb4_bin, 'SWORD ~'   COLLATE utf8mb4_bin) WHERE song_id = 252;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'ア~'      COLLATE utf8mb4_bin, 'ア ~'      COLLATE utf8mb4_bin) WHERE song_id = 489;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'ア~'      COLLATE utf8mb4_bin, 'ア ~'      COLLATE utf8mb4_bin) WHERE song_id = 491;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, '涙~'      COLLATE utf8mb4_bin, '涙 ~'      COLLATE utf8mb4_bin) WHERE song_id = 25;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'う~'      COLLATE utf8mb4_bin, 'う ~'      COLLATE utf8mb4_bin) WHERE song_id = 46;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, '歌~'      COLLATE utf8mb4_bin, '歌 ~'      COLLATE utf8mb4_bin) WHERE song_id = 350;
UPDATE songs SET title = REPLACE(title COLLATE utf8mb4_bin, 'MISSION~' COLLATE utf8mb4_bin, 'MISSION ~' COLLATE utf8mb4_bin) WHERE song_id = 380;

-- ===== Part 5: song_recordings.variant_label — 囲み ～(FF5E) → 半角 ~（全件） =====
-- variant_label の FF5E はすべて囲み用途。表示は title + 半角SP + variant_label のため先頭SPは持たせない。
UPDATE song_recordings
SET variant_label = REPLACE(variant_label COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin, _utf8mb4 X'7E' COLLATE utf8mb4_bin)
WHERE INSTR(variant_label COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin) > 0;

-- ===== 検証（コミット前のトランザクション内状態を確認） =====
SELECT 'remaining FF5E in songs.title (expect 0)' AS chk,
       COUNT(*) AS n FROM songs
WHERE INSTR(title COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin) > 0;

SELECT 'remaining FF5E in variant_label (expect 0)' AS chk,
       COUNT(*) AS n FROM song_recordings
WHERE INSTR(variant_label COLLATE utf8mb4_bin, _utf8mb4 X'EFBD9E' COLLATE utf8mb4_bin) > 0;

SELECT song_id, title FROM songs
WHERE song_id IN (7,23,278,308,458,461,499,553,482,493,466,484,532,533,534,535,560,580,252,489,491,25,46,350,380)
ORDER BY song_id;

COMMIT;
