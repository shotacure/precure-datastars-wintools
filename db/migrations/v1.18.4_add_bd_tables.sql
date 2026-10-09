-- =====================================================================
-- v1.18.4_add_bd_tables.sql
--
-- Blu-ray の BDMV 管理ファイル（暗号化されていない範囲）から読めるものを、ディスクから取れる識別子だけを鍵にして貯める bd_* テーブル群。
-- 商品・盤（products / discs）の登録を前提にしない：BDAnalyzer がディスクを読んだ時点で記録でき、品番との結びつきは
-- bd_discs.catalog_no に後から任意で入れる。
--
--   鍵の disc_id は CERTIFICATE/id.bdmv のディスク ID（16 バイトの 16 進）。無い盤や 0 で埋まっている盤は管理ファイルのハッシュで代用（disc_id_source = HASH）
--
--   bd_discs                 ... 盤 1 行：識別子、ボリュームラベル、ディスク名（META/DL/bdmt_*.xml）、index.bdmv の映像形式・最初の再生・トップメニュー、
--                                各種の数、AACS / BD-J の有無、当てた作品（series_id）、結びつけた品番（catalog_no）、読んだ日時
--   bd_titles                ... index.bdmv のタイトル一覧（0=最初の再生、65535=トップメニュー、1〜=タイトル）
--   bd_movie_objects         ... MovieObject.bdmv のムービーオブジェクト（命令から解いた最初に再生するプレイリストつき）
--   bd_movie_object_commands ... ナビゲーション命令の生データ（命令 4 バイトの 16 進と 2 つのオペランド）
--   bd_playlists             ... PLAYLIST/*.mpls（総尺・PlayItem 数・SubPath 数・マーク数・再生種別・操作禁止マスク・種別・収める話）
--   bd_play_items            ... プレイリストの各区間（クリップ名・クリップ内 in/out・プレイリスト時間軸の開始位置・つなぎ方）
--   bd_playlist_marks        ... プレイリストの全マーク（Entry / Link を加工せずに）
--   bd_chapters              ... Entry マークで区切ったチャプター（プレイリスト時間軸の開始・尺。Blu-ray の生の値）と、当てた話のパート
--   bd_sub_paths             ... サブパス（副音声・副映像・PiP など）
--   bd_clips                 ... CLIPINF/*.clpi（提示時刻・記録レート・パケット数・用途種別）と STREAM/*.m2ts のサイズ
--   bd_clip_streams          ... クリップ内のストリーム（PID・種別・符号化種別・映像の形式・音声の提示種別とサンプリング周波数・言語）
--
-- 時刻はすべてミリ秒（45 kHz tick を換算）。コード値は規格のまま持つ（読み方は README）。
-- 話とパートの尺は episode_parts.disc_length を正とし、チャプターの尺は生の値のまま持つ（話の最後のチャプターには 1 秒の余白が付く）。
-- BDAnalyzer は盤単位で「全削除 → 置換」で投入する（bd_discs を消すと子はすべて消える）。
--
-- 同じ日に試作した video_playlists / video_play_items / video_clips / video_clip_streams / video_disc_info / video_titles /
-- video_movie_objects / video_movie_object_commands / video_playlist_marks / video_sub_paths（品番が鍵）と、
-- video_chapters に足した chapter_kind / episode_id / episode_seq は、この表に置き換えるので片付ける。
--
-- 冪等性: DROP TABLE IF EXISTS / CREATE TABLE IF NOT EXISTS、列は INFORMATION_SCHEMA で見てから DROP。
-- =====================================================================

START TRANSACTION;

DROP TABLE IF EXISTS `video_sub_paths`;
DROP TABLE IF EXISTS `video_playlist_marks`;
DROP TABLE IF EXISTS `video_movie_object_commands`;
DROP TABLE IF EXISTS `video_movie_objects`;
DROP TABLE IF EXISTS `video_titles`;
DROP TABLE IF EXISTS `video_disc_info`;
DROP TABLE IF EXISTS `video_clip_streams`;
DROP TABLE IF EXISTS `video_clips`;
DROP TABLE IF EXISTS `video_play_items`;
DROP TABLE IF EXISTS `video_playlists`;

DROP PROCEDURE IF EXISTS _v1184_drop_col_if_exists;
DROP PROCEDURE IF EXISTS _v1184_drop_fk_if_exists;
DELIMITER $$
CREATE PROCEDURE _v1184_drop_fk_if_exists(IN p_table VARCHAR(64), IN p_name VARCHAR(64))
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
   WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND CONSTRAINT_NAME = p_name AND CONSTRAINT_TYPE = 'FOREIGN KEY';
  IF v_exists > 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` DROP FOREIGN KEY `', p_name, '`');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END$$
CREATE PROCEDURE _v1184_drop_col_if_exists(IN p_table VARCHAR(64), IN p_col VARCHAR(64))
BEGIN
  DECLARE v_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO v_exists FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_col;
  IF v_exists > 0 THEN
    SET @sql := CONCAT('ALTER TABLE `', p_table, '` DROP COLUMN `', p_col, '`');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END$$
DELIMITER ;

CALL _v1184_drop_fk_if_exists('video_chapters', 'fk_video_chapters_episode_part');
CALL _v1184_drop_col_if_exists('video_chapters', 'episode_seq');
CALL _v1184_drop_col_if_exists('video_chapters', 'episode_id');
CALL _v1184_drop_col_if_exists('video_chapters', 'chapter_kind');

DROP PROCEDURE _v1184_drop_col_if_exists;
DROP PROCEDURE _v1184_drop_fk_if_exists;

CREATE TABLE IF NOT EXISTS `bd_discs` (
  `disc_id`              char(32) NOT NULL COMMENT 'ディスク ID（CERTIFICATE/id.bdmv の 16 バイトの 16 進。無い盤は管理ファイルのハッシュ）',
  `disc_id_source`       enum('ID_BDMV','HASH') NOT NULL DEFAULT 'ID_BDMV' COMMENT 'disc_id の出どころ',
  `org_id`               char(8)  DEFAULT NULL COMMENT 'CERTIFICATE/id.bdmv の組織 ID（4 バイトの 16 進）',
  `volume_label`         varchar(64) DEFAULT NULL COMMENT 'UDF のボリュームラベル',
  `bdmt_name`            varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL COMMENT 'ディスクの表示名（META/DL/bdmt_*.xml の name）',
  `bdmt_language`        char(3) DEFAULT NULL COMMENT 'ディスク名の言語（bdmt_jpn.xml → jpn）',
  `bdmt_thumbnail_count` tinyint unsigned DEFAULT NULL COMMENT 'ディスク名に添えられたサムネイル画像の数',
  `index_video_format`   tinyint unsigned DEFAULT NULL COMMENT 'index.bdmv の映像形式（規格のコード）',
  `index_frame_rate`     tinyint unsigned DEFAULT NULL COMMENT 'index.bdmv のフレームレート（規格のコード）',
  `first_playback_kind`  enum('NONE','HDMV','BDJ') DEFAULT NULL COMMENT '最初に再生されるオブジェクトの種別',
  `top_menu_kind`        enum('NONE','HDMV','BDJ') DEFAULT NULL COMMENT 'トップメニューのオブジェクトの種別',
  `title_count`          smallint unsigned DEFAULT NULL COMMENT 'index.bdmv のタイトル数',
  `playlist_count`       smallint unsigned DEFAULT NULL COMMENT 'PLAYLIST の .mpls の数',
  `clip_count`           smallint unsigned DEFAULT NULL COMMENT 'CLIPINF のクリップ数',
  `m2ts_total_bytes`     bigint unsigned DEFAULT NULL COMMENT 'STREAM/*.m2ts の合計サイズ（バイト）',
  `has_aacs`             tinyint NOT NULL DEFAULT 0 COMMENT 'AACS フォルダがあるか',
  `has_bdj`              tinyint NOT NULL DEFAULT 0 COMMENT 'BD-J オブジェクト（BDJO）があるか',
  `bdjo_count`           smallint unsigned DEFAULT NULL COMMENT 'BDJO/*.bdjo の数',
  `jar_count`            smallint unsigned DEFAULT NULL COMMENT 'JAR/*.jar の数',
  `sound_effect_count`   smallint unsigned DEFAULT NULL COMMENT 'AUXDATA/sound.bdmv のメニュー効果音の数',
  `series_id`            int DEFAULT NULL COMMENT '当てた作品（→ series。チャプターの尺の並びを各話のパートの円盤尺と突き合わせて決める）',
  `catalog_no`           varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL COMMENT '結びつけた盤の品番（→ discs。任意。品番はディスクのデータには無い）',
  `first_read_at`        datetime DEFAULT NULL COMMENT '最初に読んだ日時',
  `last_read_at`         datetime DEFAULT NULL COMMENT '最後に読んだ日時',
  `created_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`           varchar(64) DEFAULT NULL,
  `updated_by`           varchar(64) DEFAULT NULL,
  PRIMARY KEY (`disc_id`),
  KEY `ix_bd_discs_catalog` (`catalog_no`),
  CONSTRAINT `fk_bd_discs_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_bd_discs_disc` FOREIGN KEY (`catalog_no`) REFERENCES `discs` (`catalog_no`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='Blu-ray の盤（ディスク ID が鍵。商品・盤の登録を前提にしない）';

CREATE TABLE IF NOT EXISTS `bd_titles` (
  `disc_id`        char(32) NOT NULL,
  `title_no`       smallint unsigned NOT NULL COMMENT 'タイトル番号（0=最初の再生、65535=トップメニュー、1〜=タイトル）',
  `object_kind`    enum('HDMV','BDJ') NOT NULL COMMENT 'HDMV のムービーオブジェクトか BD-J オブジェクトか',
  `access_type`    tinyint unsigned DEFAULT NULL COMMENT 'タイトルのアクセス種別（規格のコード）',
  `playback_type`  tinyint unsigned DEFAULT NULL COMMENT '再生種別（規格のコード：0=映画、1=対話）',
  `mobj_no`        smallint unsigned DEFAULT NULL COMMENT 'HDMV のとき：参照するムービーオブジェクトの番号',
  `bdjo_file`      varchar(16) DEFAULT NULL COMMENT 'BD-J のとき：参照する BDJO のファイル名',
  `playlist_file`  varchar(16) DEFAULT NULL COMMENT 'HDMV のとき：ムービーオブジェクトが最初に再生するプレイリスト（命令から解いたもの）',
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by`     varchar(64) DEFAULT NULL,
  PRIMARY KEY (`disc_id`, `title_no`),
  CONSTRAINT `fk_bd_titles_disc` FOREIGN KEY (`disc_id`) REFERENCES `bd_discs` (`disc_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='index.bdmv のタイトル一覧';

CREATE TABLE IF NOT EXISTS `bd_movie_objects` (
  `disc_id`            char(32) NOT NULL,
  `mobj_no`            smallint unsigned NOT NULL COMMENT 'ムービーオブジェクトの番号（0 始まり）',
  `resume_intention`   tinyint NOT NULL DEFAULT 0 COMMENT '中断からの再開を意図するか',
  `menu_call_mask`     tinyint NOT NULL DEFAULT 0 COMMENT 'メニュー呼び出しを禁止するか',
  `title_search_mask`  tinyint NOT NULL DEFAULT 0 COMMENT 'タイトル検索を禁止するか',
  `command_count`      smallint unsigned NOT NULL COMMENT 'ナビゲーション命令の数',
  `playlist_file`      varchar(16) DEFAULT NULL COMMENT '最初の PlayPL 系の命令が再生するプレイリスト（命令から解いたもの）',
  `created_at`         timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by`         varchar(64) DEFAULT NULL,
  PRIMARY KEY (`disc_id`, `mobj_no`),
  CONSTRAINT `fk_bd_movie_objects_disc` FOREIGN KEY (`disc_id`) REFERENCES `bd_discs` (`disc_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='MovieObject.bdmv のムービーオブジェクト';

CREATE TABLE IF NOT EXISTS `bd_movie_object_commands` (
  `disc_id`      char(32) NOT NULL,
  `mobj_no`      smallint unsigned NOT NULL,
  `cmd_seq`      smallint unsigned NOT NULL COMMENT '命令の順（1 始まり）',
  `opcode_hex`   char(8) NOT NULL COMMENT '命令の 4 バイト（16 進 8 桁。生の値）',
  `dst_operand`  int unsigned NOT NULL COMMENT '第 1 オペランド',
  `src_operand`  int unsigned NOT NULL COMMENT '第 2 オペランド',
  PRIMARY KEY (`disc_id`, `mobj_no`, `cmd_seq`),
  CONSTRAINT `fk_bd_mobj_commands_mobj` FOREIGN KEY (`disc_id`, `mobj_no`) REFERENCES `bd_movie_objects` (`disc_id`, `mobj_no`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='ムービーオブジェクトのナビゲーション命令（生データ）';

CREATE TABLE IF NOT EXISTS `bd_playlists` (
  `disc_id`          char(32) NOT NULL,
  `playlist_file`    varchar(16) NOT NULL COMMENT 'プレイリストのファイル名（00000.mpls）',
  `duration_ms`      bigint unsigned NOT NULL COMMENT '総尺（PlayItem の in/out の合計、ミリ秒）',
  `play_item_count`  smallint unsigned NOT NULL,
  `sub_path_count`   smallint unsigned NOT NULL DEFAULT 0,
  `mark_count`       smallint unsigned NOT NULL DEFAULT 0 COMMENT 'PlayListMark の数',
  `playback_type`    tinyint unsigned DEFAULT NULL COMMENT '再生種別（規格のコード：1=連続、2=ランダム、3=シャッフル）',
  `uo_mask`          bigint unsigned DEFAULT NULL COMMENT 'ユーザー操作の禁止マスク（規格の 64 ビットをそのまま）',
  `playlist_kind`    enum('EPISODE','PLAY_ALL','BONUS','MENU','OTHER') DEFAULT NULL COMMENT 'プレイリストの種別（EPISODE=本編 1 話、PLAY_ALL=全話連続、BONUS=特典、MENU、OTHER。NULL=未判定）',
  `episode_id`       int DEFAULT NULL COMMENT '本編 1 話のプレイリストが収める話（→ episodes）',
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `created_by`       varchar(64) DEFAULT NULL,
  PRIMARY KEY (`disc_id`, `playlist_file`),
  CONSTRAINT `fk_bd_playlists_disc` FOREIGN KEY (`disc_id`) REFERENCES `bd_discs` (`disc_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_bd_playlists_episode` FOREIGN KEY (`episode_id`) REFERENCES `episodes` (`episode_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='Blu-ray のプレイリスト（BDMV/PLAYLIST/*.mpls）';

CREATE TABLE IF NOT EXISTS `bd_play_items` (
  `disc_id`              char(32) NOT NULL,
  `playlist_file`        varchar(16) NOT NULL,
  `item_seq`             smallint unsigned NOT NULL COMMENT 'プレイリスト内の並び順（1 始まり）',
  `clip_file`            varchar(16) NOT NULL COMMENT '参照するクリップ（00001.m2ts）',
  `codec_id`             varchar(8)  NOT NULL DEFAULT 'M2TS',
  `in_time_ms`           bigint unsigned NOT NULL COMMENT 'クリップ内の再生開始時刻（ミリ秒）',
  `out_time_ms`          bigint unsigned NOT NULL COMMENT 'クリップ内の再生終了時刻（ミリ秒）',
  `playlist_offset_ms`   bigint unsigned NOT NULL COMMENT 'プレイリスト時間軸での、この区間の開始位置（ミリ秒）',
  `connection_condition` tinyint unsigned NOT NULL DEFAULT 1 COMMENT '前の区間とのつなぎ方（規格のコード：1=通常、5/6=シームレス）',
  `stc_id`               tinyint unsigned NOT NULL DEFAULT 0 COMMENT '参照する STC シーケンスの番号',
  PRIMARY KEY (`disc_id`, `playlist_file`, `item_seq`),
  KEY `ix_bd_play_items_clip` (`disc_id`, `clip_file`),
  CONSTRAINT `fk_bd_play_items_playlist` FOREIGN KEY (`disc_id`, `playlist_file`) REFERENCES `bd_playlists` (`disc_id`, `playlist_file`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='プレイリストの各区間（PlayItem）';

CREATE TABLE IF NOT EXISTS `bd_playlist_marks` (
  `disc_id`        char(32) NOT NULL,
  `playlist_file`  varchar(16) NOT NULL,
  `mark_seq`       smallint unsigned NOT NULL COMMENT 'マークの順（1 始まり）',
  `mark_type`      tinyint unsigned NOT NULL COMMENT 'マークの種別（規格のコード：1=Entry〔チャプター〕、2=Link）',
  `play_item_ref`  smallint unsigned NOT NULL COMMENT '参照する PlayItem の番号（0 始まり）',
  `time_ms`        bigint unsigned NOT NULL COMMENT 'マークの時刻（その PlayItem のクリップ内の時刻、ミリ秒）',
  `entry_es_pid`   smallint unsigned DEFAULT NULL COMMENT 'マークが指すエレメンタリストリームの PID（無ければ 0）',
  `duration_ms`    bigint unsigned NOT NULL DEFAULT 0 COMMENT 'マークの尺（ミリ秒。無ければ 0）',
  PRIMARY KEY (`disc_id`, `playlist_file`, `mark_seq`),
  CONSTRAINT `fk_bd_playlist_marks_playlist` FOREIGN KEY (`disc_id`, `playlist_file`) REFERENCES `bd_playlists` (`disc_id`, `playlist_file`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='プレイリストの全マーク（PlayListMark）';

CREATE TABLE IF NOT EXISTS `bd_chapters` (
  `disc_id`        char(32) NOT NULL,
  `playlist_file`  varchar(16) NOT NULL,
  `chapter_no`     smallint unsigned NOT NULL COMMENT 'プレイリスト内のチャプター番号（1 始まり。Entry マークで区切る）',
  `start_time_ms`  bigint unsigned NOT NULL COMMENT 'プレイリスト時間軸での開始時刻（ミリ秒。生の値）',
  `duration_ms`    bigint unsigned NOT NULL COMMENT '尺（ミリ秒。生の値。話の最後のチャプターには 1 秒の余白が付く）',
  `chapter_kind`   enum('EPISODE_PART','BLANK','BONUS','OTHER') DEFAULT NULL COMMENT 'チャプターの種別（NULL=未判定）',
  `episode_id`     int DEFAULT NULL COMMENT 'チャプターが当たる話（→ episode_parts.episode_id）',
  `episode_seq`    tinyint unsigned DEFAULT NULL COMMENT 'チャプターが当たるパートの順（→ episode_parts.episode_seq）',
  PRIMARY KEY (`disc_id`, `playlist_file`, `chapter_no`),
  KEY `ix_bd_chapters_episode` (`episode_id`, `episode_seq`),
  CONSTRAINT `fk_bd_chapters_playlist` FOREIGN KEY (`disc_id`, `playlist_file`) REFERENCES `bd_playlists` (`disc_id`, `playlist_file`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_bd_chapters_episode_part` FOREIGN KEY (`episode_id`, `episode_seq`) REFERENCES `episode_parts` (`episode_id`, `episode_seq`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='プレイリストのチャプター（Entry マークで区切った生の区間）と当てた話のパート';

CREATE TABLE IF NOT EXISTS `bd_sub_paths` (
  `disc_id`              char(32) NOT NULL,
  `playlist_file`        varchar(16) NOT NULL,
  `sub_path_seq`         smallint unsigned NOT NULL COMMENT 'サブパスの順（1 始まり）',
  `sub_path_type`        tinyint unsigned NOT NULL COMMENT 'サブパスの種別（規格のコード）',
  `is_repeat`            tinyint NOT NULL DEFAULT 0,
  `sub_play_item_count`  smallint unsigned NOT NULL,
  `first_clip_file`      varchar(16) DEFAULT NULL COMMENT '最初のサブ PlayItem が参照するクリップ',
  PRIMARY KEY (`disc_id`, `playlist_file`, `sub_path_seq`),
  CONSTRAINT `fk_bd_sub_paths_playlist` FOREIGN KEY (`disc_id`, `playlist_file`) REFERENCES `bd_playlists` (`disc_id`, `playlist_file`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='プレイリストのサブパス';

CREATE TABLE IF NOT EXISTS `bd_clips` (
  `disc_id`                char(32) NOT NULL,
  `clip_file`              varchar(16) NOT NULL COMMENT 'クリップのファイル名（00001.m2ts）',
  `presentation_start_ms`  bigint unsigned DEFAULT NULL COMMENT '提示開始時刻（最初の STC シーケンス、ミリ秒）',
  `presentation_end_ms`    bigint unsigned DEFAULT NULL COMMENT '提示終了時刻（最後の STC シーケンス、ミリ秒）',
  `ts_recording_rate`      int unsigned DEFAULT NULL COMMENT 'TS の記録レート（バイト/秒）',
  `source_packets`         int unsigned DEFAULT NULL COMMENT 'ソースパケット数（192 バイト単位）',
  `application_type`       tinyint unsigned DEFAULT NULL COMMENT 'クリップの用途種別（規格のコード）',
  `clip_stream_type`       tinyint unsigned DEFAULT NULL COMMENT 'クリップのストリーム種別（規格のコード）',
  `file_size_bytes`        bigint unsigned DEFAULT NULL COMMENT 'STREAM/xxxxx.m2ts のファイルサイズ（バイト。中身は暗号化されていて読まない）',
  PRIMARY KEY (`disc_id`, `clip_file`),
  CONSTRAINT `fk_bd_clips_disc` FOREIGN KEY (`disc_id`) REFERENCES `bd_discs` (`disc_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='Blu-ray のクリップ（BDMV/CLIPINF/*.clpi）';

CREATE TABLE IF NOT EXISTS `bd_clip_streams` (
  `disc_id`             char(32) NOT NULL,
  `clip_file`           varchar(16) NOT NULL,
  `stream_pid`          smallint unsigned NOT NULL COMMENT 'ストリームの PID',
  `stream_kind`         enum('VIDEO','AUDIO','PG','IG','TEXT','OTHER') NOT NULL COMMENT 'ストリームの種別（符号化種別から判定）',
  `coding_type`         tinyint unsigned NOT NULL COMMENT '符号化種別（規格のコード：0x1B=H.264、0x80=LPCM、0x81=AC-3、0x82=DTS、0x90=PG 字幕 …）',
  `video_format`        tinyint unsigned DEFAULT NULL COMMENT '映像の形式（規格のコード：4=1080i、6=1080p …）',
  `frame_rate`          tinyint unsigned DEFAULT NULL COMMENT '映像のフレームレート（規格のコード：1=23.976、4=29.97 …）',
  `audio_presentation`  tinyint unsigned DEFAULT NULL COMMENT '音声の提示種別（規格のコード：1=モノラル、3=ステレオ、6=マルチチャンネル、12=ステレオ＋マルチ）',
  `sampling_rate`       tinyint unsigned DEFAULT NULL COMMENT '音声のサンプリング周波数（規格のコード：1=48 kHz、4=96 kHz、5=192 kHz …）',
  `language`            char(3) DEFAULT NULL COMMENT '言語コード（ISO 639-2。jpn など）',
  PRIMARY KEY (`disc_id`, `clip_file`, `stream_pid`),
  CONSTRAINT `fk_bd_clip_streams_clip` FOREIGN KEY (`disc_id`, `clip_file`) REFERENCES `bd_clips` (`disc_id`, `clip_file`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='クリップ内のストリーム（映像・音声・字幕）';

COMMIT;

SELECT 'v1.18.4 migration completed: bd_* tables' AS final_status;
