-- ===========================================================================
-- v1.3.6_migration_tracks_content_kind_trigger_upsert_fix
--
-- 目的:
--   tracks の content_kind 一貫性トリガー trg_tracks_bi_fk_consistency が、
--   INSERT ... ON DUPLICATE KEY UPDATE で BEFORE INSERT が先に発火する際、
--   INSERT VALUES 側の暫定 content_kind_code（CDAnalyzer / BDAnalyzer の物理情報
--   UPSERT では 'OTHER'）と、同一 (catalog_no, track_no) のメドレー分割子行
--   （sub_order>0）の content_kind_code（例: 'BGM'）を誤って不一致と判定し、
--   既存ディスクへの物理情報同期を不当に弾いていた問題を修正する。
--
-- 対応:
--   trg_tracks_bi_fk_consistency の content_kind 一貫性チェックに、同一 PK
--   (catalog_no, track_no, sub_order) が既存する場合（＝実質 UPDATE）は本チェックを
--   スキップするガードを追加。整合性の最終判定は後続の BEFORE UPDATE トリガーが
--   保全後の確定値で行うため、制約は緩まない。真に新規 PK を別 content_kind_code で
--   挿入するケースは従来通り検出される。trg_tracks_bu_fk_consistency は内容変更なし。
--
-- 冪等性:
--   トリガーは DROP TRIGGER IF EXISTS → CREATE TRIGGER で全置換するため、
--   本マイグレーションは何度再実行しても最終状態が同一になる（冪等）。
--
-- 前提バージョン: v1.3.6（Directory.Build.props）
-- ===========================================================================

DROP TRIGGER IF EXISTS `trg_tracks_bi_fk_consistency`;
DROP TRIGGER IF EXISTS `trg_tracks_bu_fk_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_tracks_bi_fk_consistency`
BEFORE INSERT ON `tracks`
FOR EACH ROW
BEGIN
  -- content_kind=SONG 以外のときに song_recording_id が立っていたら弾く
  IF NEW.song_recording_id IS NOT NULL AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_recording_id requires content_kind_code = SONG';
  END IF;
  -- content_kind=SONG 以外のときに song_size_variant_code / song_part_variant_code が立っていたら弾く
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_size/part columns require content_kind_code = SONG';
  END IF;
  -- content_kind=BGM 以外のときに BGM 参照 2 列のいずれかが立っていたら弾く
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: bgm_* columns require content_kind_code = BGM';
  END IF;
  -- SONG は song_recording_id が必須
  IF NEW.content_kind_code = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = SONG requires song_recording_id';
  END IF;
  -- BGM は 2 列セットが必須（2 列すべて NOT NULL、または 2 列すべて NULL のどちらか）
  IF NEW.content_kind_code = 'BGM' AND
     (NEW.bgm_series_id IS NULL OR NEW.bgm_m_no_detail IS NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = BGM requires (bgm_series_id, bgm_m_no_detail) all NOT NULL';
  END IF;
  -- sub_order > 0 の行は物理情報を持てない（親 sub_order=0 行にだけ物理情報を持つ運用）
  IF NEW.sub_order > 0 AND (
       NEW.start_lba IS NOT NULL OR NEW.length_frames IS NOT NULL OR
       NEW.isrc IS NOT NULL OR
       NEW.is_data_track <> 0 OR NEW.has_pre_emphasis <> 0 OR NEW.is_copy_permitted <> 0 OR
       NEW.cd_text_title IS NOT NULL OR NEW.cd_text_performer IS NOT NULL
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: sub_order > 0 rows must have NULL/0 for all physical columns (start_lba, length_frames, isrc, is_data_track, has_pre_emphasis, is_copy_permitted, cd_text_title, cd_text_performer)';
  END IF;
  -- 同一 (catalog_no, track_no) 内で content_kind_code が一致していなければ弾く。
  -- sub_order <> NEW.sub_order でフィルタしているため、自分自身の行（同じ sub_order）は比較対象にならない。
  -- これは ON DUPLICATE KEY UPDATE で BEFORE INSERT が先に発火した場合に、
  -- 既存の同一 PK 行（自分自身）が異なる content_kind_code を持っていても弾かれないための除外。
  -- sub_order 分割行（親 sub_order=0 と子 sub_order>0）の間での content_kind_code 不一致は引き続き検出する。
  --
  -- 加えて、同一 PK (catalog_no, track_no, sub_order) の行が既に存在する場合、この INSERT は
  -- 実質 ON DUPLICATE KEY UPDATE である。その場合 content_kind_code は UPDATE 句で書き換えられず
  -- （物理情報 UPSERT の呼び出し側は content_kind_code を保全する）、INSERT VALUES 側の暫定値
  -- （例: 'OTHER'）は DB に反映されない。整合性の最終判定は後続の BEFORE UPDATE トリガーが保全後の
  -- 確定値で行うため、同一 PK が既存する場合は本チェックをスキップし、UPSERT 時のメドレー分割行
  -- （sub_order>0 の兄弟行）に対する誤検知を防ぐ。真に新規 PK を別 content_kind_code で挿入する
  -- ケースは NOT EXISTS が真のままなので従来通り検出される。
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
  -- FK の ON DELETE SET NULL カスケードも BEFORE UPDATE を発火させるため、
  -- 必須方向（SONG→recording_id NOT NULL 等）は INSERT トリガーだけに任せる。
  -- ここでは「禁止方向」のみチェック。

  IF NEW.song_recording_id IS NOT NULL AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_recording_id requires content_kind_code = SONG';
  END IF;
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: song_size/part columns require content_kind_code = SONG';
  END IF;
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: bgm_* columns require content_kind_code = BGM';
  END IF;
  -- sub_order > 0 の行は物理情報を持てない
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
