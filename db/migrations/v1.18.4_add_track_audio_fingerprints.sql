-- =====================================================================
-- v1.18.4_add_track_audio_fingerprints.sql
--
-- CD の各トラックの音から取った特徴量（ランドマーク指紋）を持つ track_audio_fingerprints テーブルを新設する。
--
-- CDAnalyzer が盤の音を SCSI の READ CD で読み、PrecureDataStars.AudioFingerprint の LandmarkFingerprinter で
-- スペクトログラムのピークの対をハッシュにした指紋（1 項目 6 バイト：ハッシュ 3 バイト LE ＋ フレーム番号 3 バイト LE）を
-- 1 トラック 1 行で入れる。音どうしの突き合わせ（盤どうしの比較や、ほかの音声に含まれる曲の検出）に使う。
-- あわせて、読んだ PCM 全体の SHA-256 を持ち、同じ音源かをビット単位で見分けられるようにする。
--
-- 物理トラック単位なので sub_order は常に 0。tracks の行が消えれば一緒に消える。
-- 取り方の版（method_version）が違う指紋どうしは突き合わせない。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `track_audio_fingerprints` (
  `catalog_no`     varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '所属ディスクの品番（→ tracks）',
  `track_no`       tinyint unsigned NOT NULL COMMENT 'トラック番号（→ tracks）',
  `sub_order`      tinyint unsigned NOT NULL DEFAULT 0 COMMENT 'トラック内順序。物理トラック単位なので常に 0',
  `method_version` tinyint unsigned NOT NULL COMMENT '特徴量の取り方の版（LandmarkFingerprinter.MethodVersion）',
  `sample_rate_hz` int unsigned NOT NULL COMMENT '解析時のサンプリング周波数（Hz）',
  `fft_size`       smallint unsigned NOT NULL COMMENT 'FFT の点数',
  `hop_samples`    smallint unsigned NOT NULL COMMENT 'フレームの間隔（サンプル数）。フレーム番号 × これ ÷ sample_rate_hz が秒',
  `hash_count`     int unsigned NOT NULL COMMENT '指紋の項目数',
  `duration_ms`    int unsigned NOT NULL COMMENT '読んだ音の長さ（ミリ秒）',
  `pcm_sha256`     char(64) NOT NULL COMMENT '読んだ PCM 全体（16 ビット LE ステレオ）の SHA-256（16 進小文字）',
  `fingerprint`    longblob NOT NULL COMMENT '指紋のバイト列（1 項目 6 バイト：ハッシュ 3 バイト LE ＋ フレーム番号 3 バイト LE。時刻順）',
  `read_at`        datetime NOT NULL COMMENT '音を読んだ日時',
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64) DEFAULT NULL,
  `updated_by`     varchar(64) DEFAULT NULL,
  PRIMARY KEY (`catalog_no`, `track_no`, `sub_order`),
  KEY `idx_track_audio_fingerprints_sha` (`pcm_sha256`),
  CONSTRAINT `fk_track_audio_fingerprints_track`
    FOREIGN KEY (`catalog_no`, `track_no`, `sub_order`) REFERENCES `tracks` (`catalog_no`, `track_no`, `sub_order`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci
  COMMENT='CD のトラックの音の特徴量（ランドマーク指紋）と PCM のハッシュ';

COMMIT;
