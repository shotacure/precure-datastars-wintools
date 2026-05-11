-- ===========================================================================
-- Migration: v1.2.4 -> v1.3.0
--   episode_uses テーブルを新設し、エピソードのパート内で流れた歌・劇伴・
--   その他音声（DRAMA / RADIO / JINGLE / OTHER）の使用記録を構造化する。
--
-- 設計方針:
--   discs → tracks の関係を、episodes → episode_uses にスライドした構造。
--   tracks と同じく content_kind_code で歌（SONG）／劇伴（BGM）／その他を
--   区別し、種別ごとに参照列を切り替える。
--   主キー (episode_id, part_kind, use_order, sub_order) で「どのエピソードの
--   どのパートの何番目の音声か」を一意に識別する。
--
--   FK は tracks と同じ流儀で、SONG なら song_recordings / song_size_variants
--   / song_part_variants に、BGM なら bgm_cues に、それ以外は use_title_override
--   テキストのみという扱い。FK は ON DELETE SET NULL（参照先が消えても
--   episode_uses 行自体は履歴として残す）、ON UPDATE CASCADE。
--
--   integrity を保証するため、tracks の trg_tracks_bi/bu_fk_consistency と
--   同じパターンで episode_uses にも BEFORE INSERT / BEFORE UPDATE トリガを
--   仕込む（content_kind_code と参照列の対応を検証）。
--
-- 変更内容:
--   1. episode_uses テーブル新設
--   2. episode_uses 用の整合性検証トリガを 2 本（BEFORE INSERT / UPDATE）
--
-- 実行方法（既に v1.2.4 の DB に対して）:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.3.0_add_episode_uses.sql
--
-- 冪等性:
--   テーブル作成は CREATE TABLE IF NOT EXISTS で安全に再実行可能。
--   トリガは DROP TRIGGER IF EXISTS で既存を削除してから再作成する。
-- ===========================================================================

-- ===========================================================================
-- STEP 1: episode_uses テーブル新設
-- ===========================================================================
-- エピソードのパート内で流れた音声の使用記録。
-- tracks（discs配下）と同じ流儀で、content_kind_code により参照列を切り替える。
--
-- 主キー: (episode_id, part_kind, use_order, sub_order)
--   - episode_id: 対象エピソード
--   - part_kind:  どのパートか（part_types マスタ参照、AVANT / PART_A / PART_B 等）
--   - use_order:  パート内の使用順（1 始まり）
--   - sub_order:  同 use_order 内のサブ順（メドレー的に複数曲が連続するケース。既定 0）
--
-- 内容種別と参照（tracks の content_kind_code パターンを踏襲）:
--   - SONG:  song_recording_id + song_size_variant_code + song_part_variant_code を使う
--   - BGM:   (bgm_series_id, bgm_m_no_detail) で bgm_cues を参照
--   - DRAMA / RADIO / JINGLE / OTHER: use_title_override にテキストを入れる
--   - CHAPTER は tracks 用なのでここでは使わない（仮に入れても整合性は壊れないが用途外）
--
-- 補助情報（任意・段階入力可）:
--   - scene_label:       「ほのかとなぎさの再会」等の使用シーンの説明
--   - duration_seconds:  使用尺（秒）
--   - notes:             その他注記
--   - is_broadcast_only: 本放送のみ使用された劇伴の例外フラグ
CREATE TABLE IF NOT EXISTS `episode_uses` (
  `episode_id`        int                NOT NULL                COMMENT '対象エピソード（→ episodes.episode_id）',
  `part_kind`         varchar(32)        CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL
                                                                COMMENT 'パート種別（→ part_types.part_type）',
  `use_order`         tinyint unsigned   NOT NULL                COMMENT 'パート内の使用順（1 始まり）',
  `sub_order`         tinyint unsigned   NOT NULL DEFAULT 0      COMMENT '同 use_order 内のサブ順（メドレー時の連続曲の細分）',

  `content_kind_code` varchar(32)        CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'OTHER'
                                                                COMMENT '内容種別（→ track_content_kinds.kind_code、SONG/BGM/DRAMA/RADIO/JINGLE/OTHER）',

  -- SONG 用参照列。content_kind_code = SONG のときのみ非 NULL を許可（トリガで担保）。
  `song_recording_id`      int           DEFAULT NULL            COMMENT 'SONG 時：→ song_recordings.song_recording_id',
  `song_size_variant_code` varchar(32)   CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'SONG 時：歌のサイズ違い（→ song_size_variants.variant_code）',
  `song_part_variant_code` varchar(32)   CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'SONG 時：歌のパート違い（→ song_part_variants.variant_code）',

  -- BGM 用参照列。content_kind_code = BGM のときのみ両方 NOT NULL を要求（トリガで担保）。
  `bgm_series_id`     int                DEFAULT NULL            COMMENT 'BGM 時：→ bgm_cues.series_id（複合 FK 第 1 列）',
  `bgm_m_no_detail`   varchar(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL
                                                                COMMENT 'BGM 時：→ bgm_cues.m_no_detail（複合 FK 第 2 列）',

  -- テキスト系（DRAMA / RADIO / JINGLE / OTHER）の表示文字列。SONG / BGM では用途外。
  `use_title_override` varchar(255)      CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
                                                                COMMENT '内容種別がテキスト系のときの表示文字列。歌・劇伴では使わない',

  -- 補助情報（任意）
  `scene_label`       varchar(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL
                                                                COMMENT '使用シーンの説明（例: ほのかとなぎさの再会）',
  `duration_seconds`  smallint unsigned  DEFAULT NULL            COMMENT '使用尺（秒）',
  `notes`             varchar(1024)      DEFAULT NULL            COMMENT '備考',
  `is_broadcast_only` tinyint(1)         NOT NULL DEFAULT 0      COMMENT '本放送のみ使用された場合に立てるフラグ',

  -- 監査
  `created_at`        timestamp          NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp          NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`        varchar(64)        DEFAULT NULL,
  `updated_by`        varchar(64)        DEFAULT NULL,

  PRIMARY KEY (`episode_id`, `part_kind`, `use_order`, `sub_order`),
  KEY `ix_episode_uses_content_kind` (`content_kind_code`),
  KEY `ix_episode_uses_song_recording` (`song_recording_id`),
  KEY `ix_episode_uses_song_size` (`song_size_variant_code`),
  KEY `ix_episode_uses_song_part` (`song_part_variant_code`),
  -- 「この M ナンバーが使われたエピソード」逆引き用の重要インデックス。
  -- BGM 詳細ページ（将来）と SiteBuilder のシリーズ詳細「劇伴使用回数」列で使う。
  KEY `ix_episode_uses_bgm_ref` (`bgm_series_id`, `bgm_m_no_detail`),

  CONSTRAINT `fk_episode_uses_episode`
    FOREIGN KEY (`episode_id`) REFERENCES `episodes` (`episode_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_episode_uses_part_type`
    FOREIGN KEY (`part_kind`) REFERENCES `part_types` (`part_type`),
  CONSTRAINT `fk_episode_uses_content_kind`
    FOREIGN KEY (`content_kind_code`) REFERENCES `track_content_kinds` (`kind_code`),
  CONSTRAINT `fk_episode_uses_song_recording`
    FOREIGN KEY (`song_recording_id`) REFERENCES `song_recordings` (`song_recording_id`)
    ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_episode_uses_song_size`
    FOREIGN KEY (`song_size_variant_code`) REFERENCES `song_size_variants` (`variant_code`),
  CONSTRAINT `fk_episode_uses_song_part`
    FOREIGN KEY (`song_part_variant_code`) REFERENCES `song_part_variants` (`variant_code`),
  CONSTRAINT `fk_episode_uses_bgm_cue`
    FOREIGN KEY (`bgm_series_id`, `bgm_m_no_detail`) REFERENCES `bgm_cues` (`series_id`, `m_no_detail`)
    ON DELETE SET NULL ON UPDATE CASCADE,

  CONSTRAINT `ck_episode_uses_use_order_pos`
    CHECK ((`use_order` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='エピソードのパート内で流れた歌・劇伴・その他音声の使用記録（tracks 流儀）';

-- ===========================================================================
-- STEP 2: 整合性検証トリガ（content_kind_code と参照列の対応を担保）
-- ===========================================================================
-- tracks の trg_tracks_bi/bu_fk_consistency と同じ思想:
--   - SONG 用参照列は content_kind_code = SONG のときだけ非 NULL を許す
--   - BGM 用参照列は content_kind_code = BGM のときだけ非 NULL を許す
--   - SONG のときは song_recording_id 必須
--   - BGM のときは bgm_series_id と bgm_m_no_detail の両方が必須
--
-- なお tracks にあった「同一 (catalog_no, track_no) 内で content_kind_code 一致」
-- ルールは episode_uses では適用しない（同 use_order の sub_order=0/1 で異なる種別を
-- 並べたい使い方は今のところ想定していないため）。

DROP TRIGGER IF EXISTS `trg_episode_uses_bi_fk_consistency`;
DROP TRIGGER IF EXISTS `trg_episode_uses_bu_fk_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_episode_uses_bi_fk_consistency`
BEFORE INSERT ON `episode_uses`
FOR EACH ROW
BEGIN
  IF NEW.song_recording_id IS NOT NULL AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: song_recording_id requires content_kind_code = SONG';
  END IF;
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: song_size/part columns require content_kind_code = SONG';
  END IF;
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: bgm_* columns require content_kind_code = BGM';
  END IF;
  IF NEW.content_kind_code = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: content_kind_code = SONG requires song_recording_id';
  END IF;
  IF NEW.content_kind_code = 'BGM' AND
     (NEW.bgm_series_id IS NULL OR NEW.bgm_m_no_detail IS NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: content_kind_code = BGM requires (bgm_series_id, bgm_m_no_detail) all NOT NULL';
  END IF;
END;;

CREATE TRIGGER `trg_episode_uses_bu_fk_consistency`
BEFORE UPDATE ON `episode_uses`
FOR EACH ROW
BEGIN
  IF NEW.song_recording_id IS NOT NULL AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: song_recording_id requires content_kind_code = SONG';
  END IF;
  IF (NEW.song_size_variant_code IS NOT NULL OR NEW.song_part_variant_code IS NOT NULL)
     AND NEW.content_kind_code <> 'SONG' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: song_size/part columns require content_kind_code = SONG';
  END IF;
  IF (NEW.bgm_series_id IS NOT NULL OR NEW.bgm_m_no_detail IS NOT NULL)
     AND NEW.content_kind_code <> 'BGM' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: bgm_* columns require content_kind_code = BGM';
  END IF;
  IF NEW.content_kind_code = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: content_kind_code = SONG requires song_recording_id';
  END IF;
  IF NEW.content_kind_code = 'BGM' AND
     (NEW.bgm_series_id IS NULL OR NEW.bgm_m_no_detail IS NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'episode_uses: content_kind_code = BGM requires (bgm_series_id, bgm_m_no_detail) all NOT NULL';
  END IF;
END;;

DELIMITER ;
