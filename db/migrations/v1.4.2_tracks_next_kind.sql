-- ===========================================================================
-- v1.4.2_tracks_next_kind
--
-- 目的:
--   tracks.content_kind_code='NEXT'（次回予告）のトラックが song_recording を
--   持てるようにし、size='NEXT' + part='INST' の固定セットで運用するルールを
--   トリガで強制する。NEXT は「同シリーズの OP recording の予告サイズ・
--   インストバージョン」を指す紐付けとして使う。
--
-- 対応:
--   1) song_size_variants に 'NEXT' (次回予告 / Next Preview) を追加（display_order=3）。
--      v1.4.1 以前で既に追加されている場合（典型例：Catalog 側のマスタ編集で
--      ad-hoc に投入されたケース、display_order や name_en にズレがある場合）も
--      ON DUPLICATE KEY UPDATE で正規値に揃え直す。
--   2) trg_tracks_bi_fk_consistency / trg_tracks_bu_fk_consistency を再作成：
--      - song_recording_id / song_size/part の許容 content_kind を SONG → (SONG, NEXT) に拡張
--      - 新規ルール「NEXT のとき (recording NOT NULL + size='NEXT' + part='INST') 必須」
--   3) 既存 NEXT トラック 21 件を backfill：song_recording_id = 同シリーズの OP
--      recording、size='NEXT'、part='INST'。Trigger ON のまま UPDATE するため、
--      行ごとの最終状態が NEXT 必須条件を満たすことが保証される。
--
-- 冪等性:
--   - song_size_variants は ON DUPLICATE KEY UPDATE で正規化、再実行で同状態に収束。
--   - トリガーは DROP IF EXISTS → CREATE で全置換、再実行で同状態に収束。
--   - 21 UPDATE は WHERE で個別ターゲット指定のため、再実行しても同値の上書きに
--     なるだけで挙動が変わらない。
--
-- 前提バージョン: v1.4.2（Directory.Build.props）
-- ===========================================================================

INSERT INTO `song_size_variants` (`variant_code`, `name_ja`, `name_en`, `display_order`)
VALUES ('NEXT', '次回予告', 'Next Preview', 3)
ON DUPLICATE KEY UPDATE
  `name_ja` = VALUES(`name_ja`),
  `name_en` = VALUES(`name_en`);

DROP TRIGGER IF EXISTS `trg_tracks_bi_fk_consistency`;
DROP TRIGGER IF EXISTS `trg_tracks_bu_fk_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_tracks_bi_fk_consistency`
BEFORE INSERT ON `tracks`
FOR EACH ROW
BEGIN
  -- song_recording_id は content_kind_code = SONG / NEXT のときのみ許容。
  IF NEW.song_recording_id IS NOT NULL
     AND NEW.content_kind_code NOT IN ('SONG', 'NEXT') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_recording_id requires content_kind_code IN (SONG, NEXT)';
  END IF;
  -- song_size_variant_code / song_part_variant_code も SONG / NEXT のみ許容。
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code NOT IN ('SONG', 'NEXT') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_size/part columns require content_kind_code IN (SONG, NEXT)';
  END IF;
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: bgm_* columns require content_kind_code = BGM';
  END IF;
  IF NEW.content_kind_code = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = SONG requires song_recording_id';
  END IF;
  -- NEXT は (recording NOT NULL + size='NEXT' + part='INST') のセットで必須。
  IF NEW.content_kind_code = 'NEXT' AND (
       NEW.song_recording_id IS NULL
       OR NEW.song_size_variant_code IS NULL OR NEW.song_size_variant_code <> 'NEXT'
       OR NEW.song_part_variant_code IS NULL OR NEW.song_part_variant_code <> 'INST'
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = NEXT requires (song_recording_id, song_size_variant_code = NEXT, song_part_variant_code = INST)';
  END IF;
  IF NEW.content_kind_code = 'BGM' AND
     (NEW.bgm_series_id IS NULL OR NEW.bgm_m_no_detail IS NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = BGM requires (bgm_series_id, bgm_m_no_detail) all NOT NULL';
  END IF;
  IF NEW.sub_order > 0 AND (
       NEW.start_lba IS NOT NULL OR NEW.length_frames IS NOT NULL OR
       NEW.isrc IS NOT NULL OR
       NEW.is_data_track <> 0 OR NEW.has_pre_emphasis <> 0 OR NEW.is_copy_permitted <> 0 OR
       NEW.cd_text_title IS NOT NULL OR NEW.cd_text_performer IS NOT NULL
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: sub_order > 0 rows must have NULL/0 for all physical columns (start_lba, length_frames, isrc, is_data_track, has_pre_emphasis, is_copy_permitted, cd_text_title, cd_text_performer)';
  END IF;
  IF NOT EXISTS (
       SELECT 1 FROM tracks
        WHERE catalog_no = NEW.catalog_no
          AND track_no   = NEW.track_no
          AND sub_order  = NEW.sub_order
     )
     AND EXISTS (
       SELECT 1 FROM tracks
        WHERE catalog_no = NEW.catalog_no
          AND track_no   = NEW.track_no
          AND sub_order <> NEW.sub_order
          AND content_kind_code <> NEW.content_kind_code
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: all sub_order rows in the same (catalog_no, track_no) must share the same content_kind_code';
  END IF;
END;;

CREATE TRIGGER `trg_tracks_bu_fk_consistency`
BEFORE UPDATE ON `tracks`
FOR EACH ROW
BEGIN
  IF NEW.song_recording_id IS NOT NULL
     AND NEW.content_kind_code NOT IN ('SONG', 'NEXT') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_recording_id requires content_kind_code IN (SONG, NEXT)';
  END IF;
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code NOT IN ('SONG', 'NEXT') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_size/part columns require content_kind_code IN (SONG, NEXT)';
  END IF;
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: bgm_* columns require content_kind_code = BGM';
  END IF;
  -- NEXT の必須セット条件は UPDATE 経路でも保全する。
  IF NEW.content_kind_code = 'NEXT' AND (
       NEW.song_recording_id IS NULL
       OR NEW.song_size_variant_code IS NULL OR NEW.song_size_variant_code <> 'NEXT'
       OR NEW.song_part_variant_code IS NULL OR NEW.song_part_variant_code <> 'INST'
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = NEXT requires (song_recording_id, song_size_variant_code = NEXT, song_part_variant_code = INST)';
  END IF;
  IF NEW.sub_order > 0 AND (
       NEW.start_lba IS NOT NULL OR NEW.length_frames IS NOT NULL OR
       NEW.isrc IS NOT NULL OR
       NEW.is_data_track <> 0 OR NEW.has_pre_emphasis <> 0 OR NEW.is_copy_permitted <> 0 OR
       NEW.cd_text_title IS NOT NULL OR NEW.cd_text_performer IS NOT NULL
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: sub_order > 0 rows must have NULL/0 for all physical columns';
  END IF;
  IF EXISTS (
       SELECT 1 FROM tracks
        WHERE catalog_no = NEW.catalog_no
          AND track_no   = NEW.track_no
          AND sub_order <> NEW.sub_order
          AND content_kind_code <> NEW.content_kind_code
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: all sub_order rows in the same (catalog_no, track_no) must share the same content_kind_code';
  END IF;
END;;

DELIMITER ;

-- ── NEXT トラックの backfill（21 件）──
-- 各 NEXT トラックを、同シリーズの OP recording (music_class_code='OP') にリンク。
-- complex OP（複数 OP recording を持つシリーズ）は番号の若い方を採用。
UPDATE tracks SET song_recording_id = 75,  song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01312' AND track_no=14;
UPDATE tracks SET song_recording_id = 102, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJCD-20133' AND track_no=29;
UPDATE tracks SET song_recording_id = 141, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJCD-20169' AND track_no=41;
UPDATE tracks SET song_recording_id = 168, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJCD-20188' AND track_no=33;
UPDATE tracks SET song_recording_id = 206, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01007' AND track_no=33;
UPDATE tracks SET song_recording_id = 234, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01057' AND track_no=33;
UPDATE tracks SET song_recording_id = 262, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01066' AND track_no=43;
UPDATE tracks SET song_recording_id = 299, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01109' AND track_no=38;
UPDATE tracks SET song_recording_id = 327, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01166' AND track_no=36;
UPDATE tracks SET song_recording_id = 364, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01191' AND track_no=40;
UPDATE tracks SET song_recording_id = 384, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01200' AND track_no=30;
UPDATE tracks SET song_recording_id = 400, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01220' AND track_no=39;
UPDATE tracks SET song_recording_id = 443, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01243' AND track_no=34;
UPDATE tracks SET song_recording_id = 470, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01274' AND track_no=33;
UPDATE tracks SET song_recording_id = 496, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01284' AND track_no=40;
UPDATE tracks SET song_recording_id = 522, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01314' AND track_no=33;
UPDATE tracks SET song_recording_id = 550, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01334' AND track_no=27;
UPDATE tracks SET song_recording_id = 580, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01370' AND track_no=41;
UPDATE tracks SET song_recording_id = 656, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01396' AND track_no=36;
UPDATE tracks SET song_recording_id = 691, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01421' AND track_no=37;
UPDATE tracks SET song_recording_id = 730, song_size_variant_code='NEXT', song_part_variant_code='INST' WHERE catalog_no='MJSA-01435' AND track_no=33;
