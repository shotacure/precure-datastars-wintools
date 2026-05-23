-- v1.4.1: song_recording_bgm_assignments 中間テーブル新設
--
-- 目的：
--   1 つの song_recordings が「歌として収録された」のと同時に「劇伴としても扱う」ケースを表現する。
--   通常 tracks.content_kind_code は 'SONG' / 'BGM' のどちらか排他で、tracks 本体に
--   両方の参照を同時に持つことは既存トリガー trg_tracks_bi_fk_consistency / _bu_fk_consistency で
--   禁止されている。この排他制約はそのまま維持し、SONG なのに BGM 性も持つ「両性」の関係だけを
--   この中間テーブルで表現する。
--
-- キー設計：
--   主キーは (song_recording_id, song_part_variant_code, bgm_series_id, bgm_m_no_detail) の 4 列複合。
--   同一録音でも VOCAL（歌入り）と INST（カラオケ）等のパート違いで紐付く M ナンバーが変わる
--   ケースに対応する。「パート区別なく適用」したい場合は song_part_variants マスタに
--   sentinel として用意した '_ANY' を指定する（NULL を許容して既定マッチさせる方式は採らない。
--   tracks 側 song_part_variant_code が NULL のトラックは中間テーブルとマッチしない）。
--
-- 表示側の利用：
--   - 劇伴詳細ページ /bgms/{slug}/ の cue カード収録盤リストに、この中間テーブル経由で
--     紐付くトラック（SONG）も普通に表示される。
--   - 商品詳細ページ /products/{catalog}/ のトラックカードで、SONG トラックでも
--     この中間テーブルに行があれば、歌の役職クレジット行の下に追加で
--     「シリーズ略記 + Mナンバー [メニュー]（→劇伴詳細リンク）」が出る。
--   - トラックカードの円バッジ色は SONG（赤）+ BGM（緑）の斜め分割塗りに変わる。

-- song_part_variants マスタへの sentinel 行追加：
--   実パート（VOCAL / INST / KARAOKE / 等）と並べて、本テーブル専用の
--   「パート区別なく適用」を意味する '_ANY' エントリを 1 行追加する。
--   表示用 name_ja は (指定なし) とし、変則的な人工値であることを示す。
--   display_order は NULL（既存パートの順序に割り込ませない）。
INSERT INTO `song_part_variants` (`variant_code`, `name_ja`, `display_order`)
VALUES ('_ANY', '(指定なし)', NULL)
ON DUPLICATE KEY UPDATE `name_ja` = VALUES(`name_ja`);

DROP TABLE IF EXISTS `song_recording_bgm_assignments`;

CREATE TABLE `song_recording_bgm_assignments` (
  `song_recording_id`      int NOT NULL,
  -- パート指定。実パートコード（'VOCAL' / 'INST' / 'KARAOKE' 等）か、
  -- パート区別なく適用する場合は sentinel '_ANY' を入れる。NULL は許容しない。
  `song_part_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `bgm_series_id`          int NOT NULL,
  `bgm_m_no_detail`        varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `created_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`             varchar(64) DEFAULT NULL,
  `updated_by`             varchar(64) DEFAULT NULL,
  PRIMARY KEY (`song_recording_id`, `song_part_variant_code`, `bgm_series_id`, `bgm_m_no_detail`),
  KEY `ix_srba_cue` (`bgm_series_id`, `bgm_m_no_detail`),
  KEY `ix_srba_part` (`song_part_variant_code`),
  CONSTRAINT `fk_srba_recording` FOREIGN KEY (`song_recording_id`)
    REFERENCES `song_recordings` (`song_recording_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_srba_part` FOREIGN KEY (`song_part_variant_code`)
    REFERENCES `song_part_variants` (`variant_code`)
    ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_srba_cue` FOREIGN KEY (`bgm_series_id`, `bgm_m_no_detail`)
    REFERENCES `bgm_cues` (`series_id`, `m_no_detail`)
    ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- トリガー（B 案：tracks.content_kind_code の SONG → 他種別 変更を拒否）：
--   ある song_recording_id を参照している tracks 行があり、かつその song_recording_id が
--   song_recording_bgm_assignments に紐付いている状態で、tracks.content_kind_code を SONG から
--   別の値（BGM / DRAMA / RADIO / JINGLE / CHAPTER / OTHER）に変えようとする UPDATE を拒否する。
--   先に song_recording_bgm_assignments の対応行を手動で削除してから種別変更する運用とする。
--   これにより「中間テーブルに紐付いた録音が、いつの間にか SONG ではないトラックを指してる
--   孤児状態」を構造的に防ぐ。
--

DROP TRIGGER IF EXISTS `trg_tracks_bu_block_kind_change_when_srba`;

DELIMITER ;;
CREATE TRIGGER `trg_tracks_bu_block_kind_change_when_srba`
BEFORE UPDATE ON `tracks`
FOR EACH ROW
BEGIN
  IF OLD.content_kind_code = 'SONG'
     AND NEW.content_kind_code <> 'SONG'
     AND OLD.song_recording_id IS NOT NULL
     AND EXISTS (
       SELECT 1 FROM song_recording_bgm_assignments
        WHERE song_recording_id = OLD.song_recording_id
     ) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: cannot change content_kind_code from SONG while this song_recording has rows in song_recording_bgm_assignments. Delete the assignment first.';
  END IF;
END;;
DELIMITER ;
