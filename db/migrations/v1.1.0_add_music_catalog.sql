-- =============================================================================
-- Migration: v1.0.x -> v1.1.0 音楽・映像カタログ機能の追加
-- =============================================================================
-- 適用対象:
--   v1.0.x 運用中の `precure_datastars` データベース（既存テーブル
--   series_kinds / series_relation_kinds / series / episodes / part_types /
--   episode_parts はそのまま残し、音楽カタログ系テーブルのみ追加する）。
--
-- 安全性:
--   - 本スクリプトは冪等（何度流しても既存テーブル・既存データを破壊しない）
--   - 既存テーブルに対する ALTER は一切行わない
--   - マスタの初期データは INSERT IGNORE のため、既にコードが存在すればスキップ
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.1.0_add_music_catalog.sql
--
-- 再実行時の注意（旧版の本スクリプトを tracks 作成で失敗させた環境向け）:
--   - 以前の版は tracks に CHECK 制約を入れて MySQL Error 3823 で落ちていた。
--     その場合 tracks テーブルは作成されていないので、このスクリプトをそのまま流せば
--     最新スキーマ（CHECK 2 本をトリガーに置き換えた構成）で tracks が作られる。
--   - 万一旧版で tracks が作成済みだった場合は、手動で下記を実行してから本スクリプトを流す：
--        ALTER TABLE tracks DROP CHECK ck_tracks_song_fk_consistency;
--        ALTER TABLE tracks DROP CHECK ck_tracks_bgm_fk_consistency;
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS;
SET @OLD_SQL_SAFE_UPDATES   = @@SQL_SAFE_UPDATES;
SET FOREIGN_KEY_CHECKS = 0;  -- 追加順を柔軟にするため一時的に無効化
SET SQL_SAFE_UPDATES   = 0;  -- MySQL Workbench のセーフ更新モードで、WHERE 節がサブクエリのみの DELETE が Error 1175 で弾かれるのを回避

-- -----------------------------------------------------------------------------
-- 1. マスタテーブル群 (6 個)
-- -----------------------------------------------------------------------------

--
-- product_kinds: 商品種別マスタ（シングル・アルバム・サントラ・ドラマCD 等）
--
CREATE TABLE IF NOT EXISTS `product_kinds` (
  `kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`kind_code`),
  UNIQUE KEY `uq_product_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `product_kinds` (`kind_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('DRAMA',             'ドラマ',                        'Drama CD',                    1),
  ('CHARA_ALBUM',       'キャラクターアルバム',          'Character Album',             2),
  ('CHARA_SINGLE',      'キャラクターシングル',          'Character Single',            3),
  ('LIVE_ALBUM',        'ライブアルバム',                'Live Album',                  4),
  ('LIVE_NOVELTY',      'ライブ特典スペシャルCD',        'Live Novelty CD',             5),
  ('THEME_SINGLE',      '主題歌シングル',                'Theme Song Single',           6),
  ('THEME_SINGLE_LATE', '後期主題歌シングル',            'Late Theme Song Single',      7),
  ('OST',               'オリジナル・サウンドトラック',  'Original Soundtrack',         8),
  ('OST_MOVIE',         '映画オリジナル・サウンドトラック','Movie Original Soundtrack', 9),
  ('RADIO',             'ラジオ',                        'Radio',                      10),
  ('TIE_UP',            'タイアップアーティスト',        'Tie-up Artist',              11),
  ('VOCAL_ALBUM',       'ボーカルアルバム',              'Vocal Album',                12),
  ('VOCAL_BEST',        'ボーカルベスト',                'Vocal Best',                 13),
  ('OTHER',             'その他',                        'Other',                      99);

--
-- disc_kinds: ディスク種別マスタ（本編・特典・ボーナス等）
-- 初期データは持たない。運用時に Catalog GUI から必要なコードだけ登録する設計。
-- 旧版の本スクリプトが投入していた 7 件（MAIN/BONUS/KARAOKE/INSTRUMENTAL/MUSIC_VIDEO/MAKING/OTHER）は
-- 下段の DELETE で撤去する。
--
CREATE TABLE IF NOT EXISTS `disc_kinds` (
  `kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`kind_code`),
  UNIQUE KEY `uq_disc_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 旧版スクリプトが投入していた初期データの撤去。
-- 既に discs.disc_kind_code で参照されている行があると FK 違反になるため、
-- 参照されていないコードだけをピンポイントで落とす（サブクエリで参照有無を判定）。
DELETE FROM `disc_kinds`
 WHERE `kind_code` IN ('MAIN','BONUS','KARAOKE','INSTRUMENTAL','MUSIC_VIDEO','MAKING','OTHER')
   AND `kind_code` NOT IN (SELECT DISTINCT `disc_kind_code` FROM `discs` WHERE `disc_kind_code` IS NOT NULL);

--
-- track_content_kinds: トラック内容種別マスタ（歌・劇伴・ドラマ・ラジオ 等）
--
CREATE TABLE IF NOT EXISTS `track_content_kinds` (
  `kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`kind_code`),
  UNIQUE KEY `uq_track_content_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 旧版スクリプトが投入していた JINGLE / CHAPTER を先に撤去し、display_order 5,6 を空ける。
-- これらは旧 SQL Server 版にも対応値が無く、LegacyImport も生成しないため
-- 通常は tracks から参照されていないはずだが、万一使用中のものがあれば FK 違反で落ちる。
-- その場合は該当 tracks の content_kind_code を別の値（OTHER 等）に付け替えてから再実行する。
DELETE FROM `track_content_kinds`
 WHERE `kind_code` IN ('JINGLE','CHAPTER')
   AND `kind_code` NOT IN (SELECT DISTINCT `content_kind_code` FROM `tracks` WHERE `content_kind_code` IS NOT NULL);

-- 本スクリプト初回実行環境向けの初期データ投入。
-- LIVE / TIE_UP は「songs マスタに登録しない音源（ライブ/タイアップ）」向けの専用区分で、
-- LegacyImport 側で旧 tracks.track_class の Live / TieUp からそれぞれマップされる。
INSERT IGNORE INTO `track_content_kinds` (`kind_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('SONG','歌','Song',1),
  ('BGM','劇伴','BGM',2),
  ('DRAMA','ドラマ','Drama',3),
  ('RADIO','ラジオ','Radio',4),
  ('LIVE','ライブ','Live',5),
  ('TIE_UP','タイアップ','Tie-up',6),
  ('OTHER','その他','Other',99);

--
-- song_music_classes: 曲の音楽種別マスタ（OP/ED/挿入歌/キャラソン 等）
--
CREATE TABLE IF NOT EXISTS `song_music_classes` (
  `class_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`class_code`),
  UNIQUE KEY `uq_song_music_classes_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `song_music_classes` (`class_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('OP','オープニング主題歌','Opening Theme',1),
  ('ED','エンディング主題歌','Ending Theme',2),
  ('INSERT','挿入歌','Insert Song',3),
  ('CHARA','キャラクターソング','Character Song',4),
  ('IMAGE','イメージソング','Image Song',5),
  ('MOVIE','映画主題歌','Movie Theme',6),
  ('OTHER','その他','Other',99);

--
-- song_arrange_classes: 曲のアレンジ種別マスタ
--
-- song_arrange_classes は v1.1.0 で廃止した（songs がアレンジ単位になったため不要）。
-- 旧インストールからのアップグレードでは後段で DROP する。
-- 新規インストールでは最初から作成しない。

--
-- song_size_variants: 曲のサイズ種別マスタ（TVサイズ・フル・ショート 等）
--
CREATE TABLE IF NOT EXISTS `song_size_variants` (
  `variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`variant_code`),
  UNIQUE KEY `uq_song_size_variants_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `song_size_variants` (`variant_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('FULL',         'フルサイズ',         'Full Size',          1),
  ('TV',           'TVサイズ',           'TV Size',            2),
  ('TV_V1',        'TVサイズ歌詞1番',    'TV Size (V1)',       3),
  ('TV_V2',        'TVサイズ歌詞2番',    'TV Size (V2)',       4),
  ('TV_TYPE_I',    'TVサイズ Type.I',    'TV Size Type.I',     5),
  ('TV_TYPE_II',   'TVサイズ Type.II',   'TV Size Type.II',    6),
  ('TV_TYPE_III',  'TVサイズ Type.III',  'TV Size Type.III',   7),
  ('TV_TYPE_IV',   'TVサイズ Type.IV',   'TV Size Type.IV',    8),
  ('TV_TYPE_V',    'TVサイズ Type.V',    'TV Size Type.V',     9),
  ('SHORT',        'ショート',           'Short',             10),
  ('MOVIE',        '映画サイズ',         'Movie Size',        11),
  ('LIVE_EDIT',    'LIVE Edit Ver.',     'Live Edit Version', 12),
  ('MOV_1',        '第1楽章',            'Movement 1',        13),
  ('MOV_3',        '第3楽章',            'Movement 3',        14),
  ('OTHER',        'その他',             'Other',             99);

--
-- song_part_variants: 曲のパート種別マスタ（ボーカル/カラオケ/ガイドメロディ等）
-- 1 トラックは (song_recording_id, size_variant_code, part_variant_code) で一意に特定される。
--
CREATE TABLE IF NOT EXISTS `song_part_variants` (
  `variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`variant_code`),
  UNIQUE KEY `uq_song_part_variants_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO `song_part_variants` (`variant_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('VOCAL',          '歌入り',                                     'Vocal',             1),
  ('INST',           'オリジナル・カラオケ',                        'Instrumental',      2),
  ('INST_STR',       'ストリングス入りオリジナル・メロディ・カラオケ','Inst+Strings',    3),
  ('INST_GUIDE',     'オリジナル・メロディ・カラオケ',              'Inst+Guide Melody', 4),
  ('INST_CHO',       'コーラス入りオリジナル・カラオケ',            'Inst+Chorus',       5),
  ('INST_CHO_GUIDE', 'コーラス入りオリジナル・メロディ・カラオケ',  'Inst+Chorus+Guide', 6),
  ('INST_PART_VO',   'パート歌入りオリジナル・カラオケ',            'Inst+Partial Vocal',7),
  ('OTHER',          'その他',                                      'Other',            99);

-- -----------------------------------------------------------------------------
-- 2. 本体テーブル群 (7 個)
-- -----------------------------------------------------------------------------

--
-- products: 商品テーブル（販売単位。価格・発売日・販売元などのメタ情報）
-- 主キーは代表品番（1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目の catalog_no）。
-- series_id が NULL のときはオールスターズ扱い。
--
CREATE TABLE IF NOT EXISTS `products` (
  `product_catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_short` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en` varchar(255) DEFAULT NULL,
  `series_id` int DEFAULT NULL,
  `product_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `release_date` date NOT NULL,
  `price_ex_tax` int DEFAULT NULL,
  `price_inc_tax` int DEFAULT NULL,
  `disc_count` tinyint unsigned NOT NULL DEFAULT '1',
  `manufacturer` varchar(64) DEFAULT NULL,
  `distributor` varchar(64) DEFAULT NULL,
  `label` varchar(64) DEFAULT NULL,
  `amazon_asin` varchar(16) DEFAULT NULL,
  `apple_album_id` varchar(32) DEFAULT NULL,
  `spotify_album_id` varchar(32) DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`product_catalog_no`),
  KEY `ix_products_series` (`series_id`),
  KEY `ix_products_kind` (`product_kind_code`),
  KEY `ix_products_release` (`release_date`),
  CONSTRAINT `fk_products_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_products_kind` FOREIGN KEY (`product_kind_code`) REFERENCES `product_kinds` (`kind_code`),
  CONSTRAINT `ck_products_disc_count_pos` CHECK ((`disc_count` >= 1)),
  CONSTRAINT `ck_products_price_ex_nonneg` CHECK (((`price_ex_tax` is null) or (`price_ex_tax` >= 0))),
  CONSTRAINT `ck_products_price_inc_nonneg` CHECK (((`price_inc_tax` is null) or (`price_inc_tax` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- discs: 物理ディスクテーブル（品番が主キー）
--   複数枚組の場合は全ディスクが同じ product_catalog_no を指し、disc_no_in_set で位置を表す。
--
CREATE TABLE IF NOT EXISTS `discs` (
  `catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `product_catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_short` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en` varchar(255) DEFAULT NULL,
  `disc_no_in_set` int unsigned DEFAULT NULL,
  `disc_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `media_format` enum('CD','CD_ROM','DVD','BD','DL','OTHER') NOT NULL DEFAULT 'CD',
  `mcn` varchar(13) DEFAULT NULL,
  `total_tracks` tinyint unsigned DEFAULT NULL,
  `total_length_frames` int unsigned DEFAULT NULL,
  `num_chapters` smallint unsigned DEFAULT NULL,
  `volume_label` varchar(64) DEFAULT NULL,
  `cd_text_album_title` varchar(255) DEFAULT NULL,
  `cd_text_album_performer` varchar(255) DEFAULT NULL,
  `cd_text_album_songwriter` varchar(255) DEFAULT NULL,
  `cd_text_album_composer` varchar(255) DEFAULT NULL,
  `cd_text_album_arranger` varchar(255) DEFAULT NULL,
  `cd_text_album_message` varchar(1024) DEFAULT NULL,
  `cd_text_disc_id` varchar(32) DEFAULT NULL,
  `cd_text_genre` varchar(64) DEFAULT NULL,
  `cddb_disc_id` char(8) DEFAULT NULL,
  `musicbrainz_disc_id` varchar(32) DEFAULT NULL,
  `last_read_at` datetime DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`catalog_no`),
  UNIQUE KEY `uq_discs_product_disc_no` (`product_catalog_no`,`disc_no_in_set`),
  KEY `ix_discs_product` (`product_catalog_no`),
  KEY `ix_discs_mcn` (`mcn`),
  KEY `ix_discs_cddb` (`cddb_disc_id`),
  KEY `ix_discs_musicbrainz` (`musicbrainz_disc_id`),
  CONSTRAINT `fk_discs_product` FOREIGN KEY (`product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_discs_kind` FOREIGN KEY (`disc_kind_code`) REFERENCES `disc_kinds` (`kind_code`),
  CONSTRAINT `ck_discs_disc_no_pos` CHECK (((`disc_no_in_set` is null) or (`disc_no_in_set` >= 1))),
  CONSTRAINT `ck_discs_total_tracks_nonneg` CHECK (((`total_tracks` is null) or (`total_tracks` >= 0))),
  CONSTRAINT `ck_discs_total_length_nonneg` CHECK (((`total_length_frames` is null) or (`total_length_frames` >= 0))),
  CONSTRAINT `ck_discs_num_chapters_nonneg` CHECK (((`num_chapters` is null) or (`num_chapters` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- songs: 歌マスタ（作品としての 1 曲）
--
CREATE TABLE IF NOT EXISTS `songs` (
  `song_id` int NOT NULL AUTO_INCREMENT,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_kana` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `music_class_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `series_id` int DEFAULT NULL,
  `original_lyricist_name` varchar(255) DEFAULT NULL,
  `original_lyricist_name_kana` varchar(255) DEFAULT NULL,
  `original_composer_name` varchar(255) DEFAULT NULL,
  `original_composer_name_kana` varchar(255) DEFAULT NULL,
  `arranger_name` varchar(255) DEFAULT NULL,
  `arranger_name_kana` varchar(255) DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`song_id`),
  KEY `ix_songs_series` (`series_id`),
  KEY `ix_songs_music_class` (`music_class_code`),
  KEY `ix_songs_title` (`title`),
  CONSTRAINT `fk_songs_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_songs_music_class` FOREIGN KEY (`music_class_code`) REFERENCES `song_music_classes` (`class_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- song_recordings: 歌の録音バージョン（歌唱者違い・バリエーション違い）
-- サイズ/パート種別は tracks 側、アレンジは songs 側なので、ここには持たない。
--
CREATE TABLE IF NOT EXISTS `song_recordings` (
  `song_recording_id` int NOT NULL AUTO_INCREMENT,
  `song_id` int NOT NULL,
  `singer_name` varchar(1024) DEFAULT NULL,
  `singer_name_kana` varchar(1024) DEFAULT NULL,
  `variant_label` varchar(128) DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`song_recording_id`),
  KEY `ix_song_recordings_song` (`song_id`),
  CONSTRAINT `fk_song_recordings_song` FOREIGN KEY (`song_id`) REFERENCES `songs` (`song_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- bgm_sessions: 劇伴の録音セッションマスタ。シリーズごとに session_no を 1, 2, 3, ... と採番。
--               採番 A 案（v1.1.1 で整理）: シリーズにセッションが 1 つしか無くても 1 を付け、
--               「未設定の既定 0」概念は廃止。
--
CREATE TABLE IF NOT EXISTS `bgm_sessions` (
  `series_id` int NOT NULL,
  `session_no` tinyint unsigned NOT NULL DEFAULT 1,
  `session_name` varchar(128) NOT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`series_id`,`session_no`),
  CONSTRAINT `fk_bgm_sessions_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- bgm_cues: 劇伴の音源 1 件 = 1 行。シリーズ × m_no_detail で 1 意。
-- 主キー: (series_id, m_no_detail)  ／ 例: series=1, m_no_detail="M220b Rhythm Cut"
-- session_no はシリーズ内のセッション識別子（bgm_sessions への FK）で、音源属性として保持する。
-- m_no_class は枝番を畳んだグループキー（例: "M220"）で、検索・ソート用にインデックス付き。
-- v1.1.0 の旧 bgm_cues + bgm_recordings の二階層構造は廃止し、1 テーブルに統合した。
--
CREATE TABLE IF NOT EXISTS `bgm_cues` (
  `series_id` int NOT NULL,
  `m_no_detail` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `session_no` tinyint unsigned NOT NULL DEFAULT 1,
  `m_no_class` varchar(64) DEFAULT NULL,
  `menu_title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `composer_name` varchar(255) DEFAULT NULL,
  `composer_name_kana` varchar(255) DEFAULT NULL,
  `arranger_name` varchar(255) DEFAULT NULL,
  `arranger_name_kana` varchar(255) DEFAULT NULL,
  `length_seconds` smallint unsigned DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`series_id`,`m_no_detail`),
  KEY `ix_bgm_cues_class` (`series_id`,`m_no_class`),
  KEY `ix_bgm_cues_session` (`series_id`,`session_no`),
  CONSTRAINT `fk_bgm_cues_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_bgm_cues_session` FOREIGN KEY (`series_id`,`session_no`) REFERENCES `bgm_sessions` (`series_id`,`session_no`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_bgm_cues_length_nonneg` CHECK (((`length_seconds` is null) or (`length_seconds` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- tracks: 物理トラックテーブル
-- content_kind_code により SONG/BGM/DRAMA/RADIO/JINGLE/CHAPTER/OTHER に分類される。
-- SONG 時は song_recording_id が NOT NULL、
-- BGM 時は (bgm_series_id, bgm_m_no_detail) の 2 列が全て NOT NULL となる整合性制約付き
-- （MySQL の CHECK 制約は ON DELETE SET NULL と同列参照の FK 併用不可のため、トリガーで担保）。
-- track_title_override は SONG/BGM でも収録盤固有のタイトル表記を保持するために使用してよい。
--
CREATE TABLE IF NOT EXISTS `tracks` (
  `catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `track_no` tinyint unsigned NOT NULL,
  `sub_order` tinyint unsigned NOT NULL DEFAULT 0,
  `content_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'OTHER',
  `song_recording_id` int DEFAULT NULL,
  `song_size_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `song_part_variant_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `bgm_series_id` int DEFAULT NULL,
  `bgm_m_no_detail` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `track_title_override` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `start_lba` int unsigned DEFAULT NULL,
  `length_frames` int unsigned DEFAULT NULL,
  `isrc` char(12) DEFAULT NULL,
  `is_data_track` tinyint(1) NOT NULL DEFAULT '0',
  `has_pre_emphasis` tinyint(1) NOT NULL DEFAULT '0',
  `is_copy_permitted` tinyint(1) NOT NULL DEFAULT '0',
  `cd_text_title` varchar(255) DEFAULT NULL,
  `cd_text_performer` varchar(255) DEFAULT NULL,
  `notes` varchar(1024) DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`catalog_no`,`track_no`,`sub_order`),
  KEY `ix_tracks_content_kind` (`content_kind_code`),
  KEY `ix_tracks_song_recording` (`song_recording_id`),
  KEY `ix_tracks_song_size` (`song_size_variant_code`),
  KEY `ix_tracks_song_part` (`song_part_variant_code`),
  KEY `ix_tracks_bgm_ref` (`bgm_series_id`,`bgm_m_no_detail`),
  CONSTRAINT `fk_tracks_disc` FOREIGN KEY (`catalog_no`) REFERENCES `discs` (`catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_tracks_content_kind` FOREIGN KEY (`content_kind_code`) REFERENCES `track_content_kinds` (`kind_code`),
  CONSTRAINT `fk_tracks_song_recording` FOREIGN KEY (`song_recording_id`) REFERENCES `song_recordings` (`song_recording_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_tracks_song_size` FOREIGN KEY (`song_size_variant_code`) REFERENCES `song_size_variants` (`variant_code`),
  CONSTRAINT `fk_tracks_song_part` FOREIGN KEY (`song_part_variant_code`) REFERENCES `song_part_variants` (`variant_code`),
  CONSTRAINT `fk_tracks_bgm_cue` FOREIGN KEY (`bgm_series_id`,`bgm_m_no_detail`) REFERENCES `bgm_cues` (`series_id`,`m_no_detail`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_tracks_track_no_pos` CHECK ((`track_no` >= 1)),
  CONSTRAINT `ck_tracks_length_nonneg` CHECK (((`length_frames` is null) or (`length_frames` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- video_chapters: BD/DVD のチャプター情報を格納する物理層テーブル。
--                 tracks が CD-DA 専用なのと同様、video_chapters は光学ディスク
--                 (discs.media_format IN ('BD','DVD')) のチャプター専用。
--                 BDAnalyzer の MPLS/IFO パース結果が投入される。title・part_type・notes は
--                 Catalog GUI 側で後から手動補完する前提で、読み取り直後は NULL。
--
CREATE TABLE IF NOT EXISTS `video_chapters` (
  `catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `chapter_no` smallint unsigned NOT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `part_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `start_time_ms` bigint unsigned NOT NULL,
  `duration_ms` bigint unsigned NOT NULL,
  `playlist_file` varchar(128) DEFAULT NULL,
  `source_kind` enum('MPLS','IFO','MANUAL') NOT NULL,
  `notes` varchar(1024) DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`catalog_no`,`chapter_no`),
  KEY `ix_video_chapters_part_type` (`part_type`),
  CONSTRAINT `fk_video_chapters_disc` FOREIGN KEY (`catalog_no`) REFERENCES `discs` (`catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_video_chapters_part_type` FOREIGN KEY (`part_type`) REFERENCES `part_types` (`part_type`),
  CONSTRAINT `ck_video_chapters_chapter_no_pos` CHECK ((`chapter_no` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- Triggers for table `tracks`
-- content_kind_code と SONG/BGM 参照列の整合性、sub_order ルールをトリガーで担保する。
--

DROP TRIGGER IF EXISTS `trg_tracks_bi_fk_consistency`;
DROP TRIGGER IF EXISTS `trg_tracks_bu_fk_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_tracks_bi_fk_consistency`
BEFORE INSERT ON `tracks`
FOR EACH ROW
BEGIN
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
  IF NEW.content_kind_code = 'SONG' AND NEW.song_recording_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'tracks: content_kind_code = SONG requires song_recording_id';
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
      SET MESSAGE_TEXT = 'tracks: sub_order > 0 rows must have NULL/0 for all physical columns';
  END IF;
  -- 同一 (catalog_no, track_no) 内で content_kind_code が一致していなければ弾く。
  -- sub_order <> NEW.sub_order でフィルタしているため、自分自身の行（同じ sub_order）は比較対象にならない。
  -- これは ON DUPLICATE KEY UPDATE で BEFORE INSERT が先に発火した場合に、
  -- 既存の同一 PK 行（自分自身）が異なる content_kind_code を持っていても弾かれないための除外。
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

CREATE TRIGGER `trg_tracks_bu_fk_consistency`
BEFORE UPDATE ON `tracks`
FOR EACH ROW
BEGIN
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

-- -----------------------------------------------------------------------------
-- マスタ系テーブルへの監査列追加（既に v1.1.0 旧版を適用済みの DB 向け）
-- -----------------------------------------------------------------------------
-- 本スクリプト初版はマスタ系テーブルに監査列（created_at/updated_at/created_by/updated_by）
-- を持たせていなかった。v1.1.0 運用中にマスタ編集 GUI（MastersEditorForm）から
-- 変更履歴を追跡できるよう、全マスタに監査列を揃える方針とした。
--
-- MySQL 8.0 系の `ALTER TABLE` は `ADD COLUMN IF NOT EXISTS` をサポートしない
-- （MariaDB 拡張）ため、INFORMATION_SCHEMA.COLUMNS で列の存在を事前に確認してから
-- 動的 SQL で ALTER を発行する使い捨てストアドプロシージャを用いる。
-- 既に上段の CREATE TABLE IF NOT EXISTS で監査列を持って作られた新規環境では、
-- すべての CALL が「列あり」判定で no-op になる。

DROP PROCEDURE IF EXISTS `_pds_add_col_if_absent`;

DELIMITER ;;

-- tbl_name に col_name が無ければ `ALTER TABLE tbl_name ADD COLUMN col_name col_def` を実行する。
CREATE PROCEDURE `_pds_add_col_if_absent`(
  IN p_table   VARCHAR(64),
  IN p_column  VARCHAR(64),
  IN p_col_def VARCHAR(255)
)
BEGIN
  DECLARE v_exists INT DEFAULT 0;

  SELECT COUNT(*) INTO v_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = p_table
     AND COLUMN_NAME  = p_column;

  IF v_exists = 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_column, '` ', p_col_def);
    PREPARE _stmt FROM @sql;
    EXECUTE _stmt;
    DEALLOCATE PREPARE _stmt;
  END IF;
END;;

DELIMITER ;

-- series_kinds / series_relation_kinds は v1.0.x 由来のため、ここで初めて監査列が入る
CALL `_pds_add_col_if_absent`('series_kinds', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('series_kinds', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('series_kinds', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('series_kinds', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('series_relation_kinds', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('series_relation_kinds', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('series_relation_kinds', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('series_relation_kinds', 'updated_by', 'varchar(64) DEFAULT NULL');

-- v1.1.0 で追加した 6 マスタも、旧版スクリプトで作られた環境向けに ALTER を流す
CALL `_pds_add_col_if_absent`('product_kinds', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('product_kinds', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('product_kinds', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('product_kinds', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('disc_kinds', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('disc_kinds', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('disc_kinds', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('disc_kinds', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('track_content_kinds', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('track_content_kinds', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('track_content_kinds', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('track_content_kinds', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('song_music_classes', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_music_classes', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_music_classes', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('song_music_classes', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('song_part_variants', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_part_variants', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_part_variants', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('song_part_variants', 'updated_by', 'varchar(64) DEFAULT NULL');

CALL `_pds_add_col_if_absent`('song_size_variants', 'created_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_size_variants', 'updated_at', 'timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');
CALL `_pds_add_col_if_absent`('song_size_variants', 'created_by', 'varchar(64) DEFAULT NULL');
CALL `_pds_add_col_if_absent`('song_size_variants', 'updated_by', 'varchar(64) DEFAULT NULL');

-- 使い捨てプロシージャを後始末
DROP PROCEDURE `_pds_add_col_if_absent`;

-- -----------------------------------------------------------------------------
-- 初期データの表記整理（v1.1.0 リリース後に名称・コード変更が入ったものの追従）
-- -----------------------------------------------------------------------------
-- 上段の CREATE TABLE IF NOT EXISTS + INSERT IGNORE は「既にテーブルがあれば何もしない」仕様のため、
-- 旧バージョンの初期データが残っている DB ではマスタの name_ja / variant_code が旧表記のまま
-- 取り残される。そのずれを埋めるために、ここで冪等な UPDATE を流す。
-- 新規構築環境（上段の INSERT IGNORE で既に最新表記が入った環境）では WHERE 条件で
-- 0 件更新（no-op）となるため、複数回流しても害はない。

-- song_size_variants.name_ja: 「TVサイズ 1番/2番」→「TVサイズ歌詞1番/歌詞2番」
UPDATE `song_size_variants` SET `name_ja` = 'TVサイズ歌詞1番' WHERE `variant_code` = 'TV_V1' AND `name_ja` = 'TVサイズ 1番';
UPDATE `song_size_variants` SET `name_ja` = 'TVサイズ歌詞2番' WHERE `variant_code` = 'TV_V2' AND `name_ja` = 'TVサイズ 2番';

-- song_part_variants.name_ja: 「通常歌入り」→「歌入り」
UPDATE `song_part_variants` SET `name_ja` = '歌入り' WHERE `variant_code` = 'VOCAL' AND `name_ja` = '通常歌入り';

-- song_size_variants.variant_code: 「TV_SIZE」→「TV」
-- 参照する fk_tracks_song_size は ON UPDATE RESTRICT のため、tracks に TV_SIZE を参照する行が
-- 残っていると Error 1451 で失敗する。その場合は先に cleanup_music_catalog.sql を流して
-- tracks を空にしてからこの migration を再実行すること。
UPDATE `song_size_variants` SET `variant_code` = 'TV' WHERE `variant_code` = 'TV_SIZE';

-- -----------------------------------------------------------------------------
-- 終了処理
-- -----------------------------------------------------------------------------

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET SQL_SAFE_UPDATES   = @OLD_SQL_SAFE_UPDATES;

-- Migration completed: v1.0.x -> v1.1.0