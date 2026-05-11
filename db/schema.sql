CREATE DATABASE  IF NOT EXISTS `precure_datastars` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */ /*!80016 DEFAULT ENCRYPTION='N' */;
USE `precure_datastars`;
-- MySQL dump 10.13  Distrib 8.0.43, for Win64 (x86_64)
--
-- Host: localhost    Database: precure_datastars
-- ------------------------------------------------------
-- Server version	8.0.43

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Table structure for table `episode_parts`
--

DROP TABLE IF EXISTS `episode_parts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `episode_parts` (
  `episode_id` int NOT NULL,
  `episode_seq` tinyint unsigned NOT NULL,
  `part_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `oa_length` smallint unsigned DEFAULT NULL,
  `disc_length` smallint unsigned DEFAULT NULL,
  `vod_length` smallint unsigned DEFAULT NULL,
  `notes` varchar(255) DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`episode_id`,`episode_seq`),
  UNIQUE KEY `uq_ep_part_type` (`episode_id`,`part_type`),
  KEY `fk_ep_parts_type` (`part_type`),
  KEY `ix_ep_parts_episode` (`episode_id`),
  CONSTRAINT `fk_ep_parts_episode` FOREIGN KEY (`episode_id`) REFERENCES `episodes` (`episode_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_ep_parts_type` FOREIGN KEY (`part_type`) REFERENCES `part_types` (`part_type`),
  CONSTRAINT `ck_disc_len_nonneg` CHECK (((`disc_length` is null) or (`disc_length` >= 0))),
  CONSTRAINT `ck_ep_seq_pos` CHECK ((`episode_seq` >= 1)),
  CONSTRAINT `ck_oa_len_nonneg` CHECK (((`oa_length` is null) or (`oa_length` >= 0))),
  CONSTRAINT `ck_vod_len_nonneg` CHECK (((`vod_length` is null) or (`vod_length` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `episodes`
--

DROP TABLE IF EXISTS `episodes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `episodes` (
  `episode_id` int NOT NULL AUTO_INCREMENT,
  `series_id` int NOT NULL,
  `series_ep_no` int NOT NULL,
  `total_ep_no` int DEFAULT NULL,
  `total_oa_no` int DEFAULT NULL,
  `nitiasa_oa_no` int DEFAULT NULL,
  `title_text` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_rich_html` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `title_kana` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_char_stats` json DEFAULT NULL,
  `on_air_at` datetime NOT NULL,
  -- v1.3.0：1 話あたりの放送尺（分単位）。既存の TV シリーズは例外なく 30 分番組
  -- だったため、マイグレでは全有効エピソードに 30 をバックフィル。新規エピソード
  -- は NULL 許可のまま運用し、エディタ側で都度入力する方針。SiteBuilder 側で
  -- 「8:30〜9:00」のような放送枠表示や、将来の番組枠管理（15 分番組混在）に使う。
  `duration_minutes` tinyint unsigned DEFAULT NULL,
  `toei_anim_summary_url` varchar(1024) DEFAULT NULL,
  `toei_anim_lineup_url` varchar(1024) DEFAULT NULL,
  `youtube_trailer_url` varchar(1024) DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  `on_air_date` date GENERATED ALWAYS AS (cast(`on_air_at` as date)) STORED,
  PRIMARY KEY (`episode_id`),
  UNIQUE KEY `uq_series_ep` (`series_id`,`series_ep_no`),
  UNIQUE KEY `total_ep_no_UNIQUE` (`total_ep_no`),
  UNIQUE KEY `total_oa_no_UNIQUE` (`total_oa_no`),
  UNIQUE KEY `nitiasa_oa_no_UNIQUE` (`nitiasa_oa_no`),
  KEY `ix_series_air` (`series_id`,`on_air_at`),
  KEY `ix_air_at` (`on_air_at`),
  KEY `ix_on_air_date` (`on_air_date`),
  KEY `ix_series_on_air_date` (`series_id`,`on_air_date`),
  CONSTRAINT `fk_ep_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_nitiasa_matches` CHECK (((`nitiasa_oa_no` is null) or (`total_oa_no` is null) or (`nitiasa_oa_no` = (`total_oa_no` + 978)))),
  CONSTRAINT `ck_nitiasa_oa_no_pos` CHECK (((`nitiasa_oa_no` is null) or (`nitiasa_oa_no` >= 1))),
  CONSTRAINT `ck_series_ep_no_pos` CHECK ((`series_ep_no` >= 1)),
  CONSTRAINT `ck_total_ep_no_pos` CHECK (((`total_ep_no` is null) or (`total_ep_no` >= 1))),
  CONSTRAINT `ck_total_oa_no_pos` CHECK (((`total_oa_no` is null) or (`total_oa_no` >= 1))),
  CONSTRAINT `episodes_chk_1` CHECK (((`title_char_stats` is null) or json_valid(`title_char_stats`)))
) ENGINE=InnoDB AUTO_INCREMENT=1073 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_kinds`（v1.2.0 工程 H-10 追加）
--
-- クレジット種別マスタ。OP=オープニングクレジット、ED=エンディングクレジット の 2 件をシードする。
-- 旧設計では credits.credit_kind / part_types.default_credit_kind が ENUM 直書きだったが、
-- 表示名 i18n や display_order を持てるようマスタ化した。
--

DROP TABLE IF EXISTS `credit_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_kinds` (
  `kind_code`     varchar(16)       NOT NULL,
  `name_ja`       varchar(64)       NOT NULL,
  `name_en`       varchar(64)           NULL,
  `display_order` smallint unsigned NOT NULL DEFAULT 0,
  `notes`         text                  NULL,
  `created_at`    datetime(6)       NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`    datetime(6)       NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `created_by`    varchar(64)           NULL,
  `updated_by`    varchar(64)           NULL,
  PRIMARY KEY (`kind_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `credit_kinds` WRITE;
INSERT INTO `credit_kinds` (`kind_code`,`name_ja`,`name_en`,`display_order`,`created_by`,`updated_by`) VALUES
  ('OP', 'オープニングクレジット', 'Opening Credits', 10, 'schema_seed', 'schema_seed'),
  ('ED', 'エンディングクレジット', 'Ending Credits', 20, 'schema_seed', 'schema_seed');
UNLOCK TABLES;

--
-- Table structure for table `part_types`
--

DROP TABLE IF EXISTS `part_types`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `part_types` (
  `part_type` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `display_order` tinyint unsigned DEFAULT NULL,
  -- v1.2.0 追加。当該パート種別が「規定で OP/ED クレジットを伴う」かを宣言する。
  -- OPENING=OP、ENDING=ED、それ以外=NULL（クレジットを伴わない）。
  -- credits.part_type が NULL のクレジットは、ここの値が credit_kind と一致する
  -- パート（OP=OPENING、ED=ENDING）で流れる、と解釈する。
  `default_credit_kind` varchar(16) DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`part_type`),
  UNIQUE KEY `uq_part_types_display_order` (`display_order`),
  KEY `ix_part_types_default_credit_kind` (`default_credit_kind`),
  CONSTRAINT `fk_part_types_default_credit_kind`
    FOREIGN KEY (`default_credit_kind`) REFERENCES `credit_kinds` (`kind_code`)
    ON UPDATE CASCADE ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- part_types の初期データ。エピソード内パート種別 22 種。
-- 監査列（created_at/updated_at/created_by/updated_by）はデフォルト値に任せる。
-- v1.2.0 で追加された default_credit_kind は、OPENING=OP / ENDING=ED 以外は NULL。
LOCK TABLES `part_types` WRITE;
INSERT INTO `part_types` (`part_type`,`name_ja`,`name_en`,`display_order`,`default_credit_kind`) VALUES
  ('AVANT',              'アバンタイトル',       'avant title',             1, NULL),
  ('OPENING',            'オープニング',         'opening',                 2, 'OP'),
  ('SPONSOR_CREDIT_A',   '前提供クレジット',     'sponsor credit (pre)',    3, NULL),
  ('CM1',                'CM①',                  'CM (1)',                  4, NULL),
  ('PART_A',             'Aパート',              'A part',                  5, NULL),
  ('CM2',                'CM②',                  'CM (2)',                  6, NULL),
  ('PART_B',             'Bパート',              'B part',                  7, NULL),
  ('CM3',                'CM③',                  'CM (3)',                  8, NULL),
  ('ENDING',             'エンディング',         'ending',                  9, 'ED'),
  ('TRAILER',            '予告',                 'trailer',                10, NULL),
  ('SPONSOR_CREDIT_B',   '後提供クレジット',     'sponsor credit (post)',  11, NULL),
  ('END_CARD',           'エンドカード',         'end card',               12, NULL),
  ('PRESENT_NOTICE',     'プレゼントのお知らせ', 'present notice',         13, NULL),
  ('NEXT_SERIES_TRAILER','新番組予告',           'next series trailor',    14, NULL),
  ('MOVIE_TRAILER',      '映画予告',             'movie trailer',          15, NULL),
  ('BATON',              'バトンタッチ',         'baton pass',             16, NULL),
  ('PART_C',             'Cパート',              'C part',                 17, NULL),
  ('CORNER',             'コーナー',             'corner',                 18, NULL),
  ('TVER_PROMOTION',     'TVer告知',             'TVer promotion',         19, NULL),
  ('NOTICE',             '各種告知',             'notice',                 20, NULL),
  ('CALL_YOUR_NAME',     '名前呼び企画',         'call your name',         21, NULL),
  ('CM4',                'CM④',                  'CM (4)',                 22, NULL);
UNLOCK TABLES;

--
-- Table structure for table `series`
--

DROP TABLE IF EXISTS `series`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `series` (
  `series_id` int NOT NULL AUTO_INCREMENT,
  `kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `parent_series_id` int DEFAULT NULL,
  `relation_to_parent` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `seq_in_parent` tinyint unsigned DEFAULT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_kana` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_short` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_short_kana` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en` varchar(255) DEFAULT NULL,
  `title_short_en` varchar(128) DEFAULT NULL,
  `slug` varchar(128) NOT NULL,
  `start_date` date NOT NULL,
  `end_date` date DEFAULT NULL,
  `episodes` smallint unsigned DEFAULT NULL,
  `run_time_seconds` smallint unsigned DEFAULT NULL,
  `toei_anim_official_site_url` varchar(1024) DEFAULT NULL,
  `toei_anim_lineup_url` varchar(1024) DEFAULT NULL,
  `abc_official_site_url` varchar(1024) DEFAULT NULL,
  `amazon_prime_distribution_url` varchar(1024) DEFAULT NULL,
  `vod_intro` smallint unsigned DEFAULT NULL,
  `font_subtitle` varchar(64) DEFAULT NULL,
  -- v1.2.1 追加: 絵コンテ役職を独立表示せず演出と融合表示するか（プレビュー描画専用フラグ）。
  -- 初期のプリキュアシリーズで「（絵コンテ・）演出 名前」のような融合表記が慣習的だった
  -- ため、シリーズ単位で ON にすることで CreditPreviewRenderer が STORYBOARD と
  -- EPISODE_DIRECTOR を突き合わせて融合描画する。
  `hide_storyboard_role` tinyint(1) NOT NULL DEFAULT '0',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`series_id`),
  UNIQUE KEY `uq_series_slug` (`slug`),
  UNIQUE KEY `uq_series_parent_relation_seq` (`parent_series_id`,`relation_to_parent`,`seq_in_parent`),
  KEY `ix_series_kind` (`kind_code`),
  KEY `ix_series_parent` (`parent_series_id`),
  KEY `ix_series_dates` (`start_date`),
  KEY `ix_series_relation_parent` (`relation_to_parent`,`parent_series_id`),
  KEY `ix_series_parent_rel_seq_date` (`parent_series_id`,`relation_to_parent`,`seq_in_parent`,`start_date`),
  CONSTRAINT `fk_series_kind` FOREIGN KEY (`kind_code`) REFERENCES `series_kinds` (`kind_code`),
  CONSTRAINT `fk_series_parent` FOREIGN KEY (`parent_series_id`) REFERENCES `series` (`series_id`) ON DELETE RESTRICT,
  CONSTRAINT `fk_series_relation` FOREIGN KEY (`relation_to_parent`) REFERENCES `series_relation_kinds` (`relation_code`),
  CONSTRAINT `ck_dates_order` CHECK (((`end_date` is null) or (`start_date` <= `end_date`))),
  CONSTRAINT `ck_parent_relation` CHECK ((((`parent_series_id` is null) and (`relation_to_parent` is null) and (`seq_in_parent` is null)) or ((`parent_series_id` is not null) and (`relation_to_parent` is not null)))),
  CONSTRAINT `ck_seq_cofeature` CHECK (((`relation_to_parent` <> _utf8mb4'COFEATURE') or ((`seq_in_parent` is not null) and (`seq_in_parent` >= 1)))),
  CONSTRAINT `ck_seq_segment` CHECK (((`relation_to_parent` <> _utf8mb4'SEGMENT') or ((`seq_in_parent` is not null) and (`seq_in_parent` >= 1)))),
  CONSTRAINT `ck_slug_format` CHECK (regexp_like(`slug`,_utf8mb4'^[a-z0-9-]+$'))
) ENGINE=InnoDB AUTO_INCREMENT=69 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `series_kinds`
--

DROP TABLE IF EXISTS `series_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `series_kinds` (
  `kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  -- v1.2.0 追加。当該シリーズ種別のクレジットがシリーズ単位で付くか、
  -- エピソード単位で付くかを宣言する。
  -- TV / SPIN-OFF は EPISODE、MOVIE / MOVIE_SHORT / SPRING は SERIES が既定。
  `credit_attach_to` enum('SERIES','EPISODE') NOT NULL DEFAULT 'EPISODE',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`kind_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- series_kinds の初期データ。シリーズ種別 5 種。
-- MOVIE = 秋（夏〜秋公開）、SPRING = 春（春休み期）、MOVIE_SHORT = 秋映画の同時上映短編、
-- SPIN-OFF = 本編から派生した別枠作品。
-- v1.2.0 で追加された credit_attach_to は、TV/SPIN-OFF が EPISODE、映画系 3 種が SERIES。
LOCK TABLES `series_kinds` WRITE;
INSERT INTO `series_kinds` (`kind_code`,`name_ja`,`name_en`,`credit_attach_to`) VALUES
  ('TV',         'TVシリーズ',   'Regular TV Series', 'EPISODE'),
  ('MOVIE',      '秋映画',       'Movie',             'SERIES'),
  ('MOVIE_SHORT','秋映画(併映)', 'Short Movie',       'SERIES'),
  ('SPRING',     '春映画',       'Spring Movie',      'SERIES'),
  ('SPIN-OFF',   'スピンオフ',   'Spin-off',          'EPISODE');
UNLOCK TABLES;

--
-- Table structure for table `series_relation_kinds`
--

DROP TABLE IF EXISTS `series_relation_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `series_relation_kinds` (
  `relation_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja` varchar(64) NOT NULL,
  `name_en` varchar(64) DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`relation_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- series_relation_kinds の初期データ。シリーズ間関係 4 種。
-- SEQUEL = 続編、MOVIE = 本編に対応する映画、COFEATURE = 同時上映、
-- SEGMENT = 番組内パート（複数シリーズ合体編成時の内部構成要素）。
LOCK TABLES `series_relation_kinds` WRITE;
INSERT INTO `series_relation_kinds` (`relation_code`,`name_ja`,`name_en`) VALUES
  ('SEQUEL',   '続編', 'Sequel to'),
  ('MOVIE',    '映画', 'Movie version of'),
  ('COFEATURE','併映', 'Co-feature'),
  ('SEGMENT',  'パート','Segment of Program');
UNLOCK TABLES;

-- ===========================================================================
-- 音楽・映像カタログ系テーブル群 (v1.1.0 追加)
--   products        ... 販売単位としての商品（価格・発売日・レーベル等）
--   discs           ... 物理ディスク（CD/BD/DVD/DL。品番が主キー）
--   tracks          ... ディスク上の物理トラック（chapter も含む）
--   songs           ... 歌マスタ（作品としての 1 曲）
--   song_recordings ... 歌の録音バージョン（歌唱者違い・カラオケ・サイズ違い等）
--   bgm_cues        ... 劇伴マスタ（シリーズ × M 番号で 1 意）
--   bgm_recordings  ... 劇伴の録音バージョン（短縮版・再録等）
--
--   付随マスタ:
--   product_kinds / disc_kinds / track_content_kinds
--   song_music_classes / song_arrange_classes / song_size_variants
-- ===========================================================================

--
-- Table structure for table `product_kinds`
-- 商品種別マスタ（シングル・アルバム・サントラ・ドラマCD 等）
--

DROP TABLE IF EXISTS `product_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `product_kinds` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `product_kinds`
--
LOCK TABLES `product_kinds` WRITE;
INSERT INTO `product_kinds` (`kind_code`,`name_ja`,`name_en`,`display_order`) VALUES
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
UNLOCK TABLES;

--
-- Table structure for table `disc_kinds`
-- ディスク種別マスタ（物理形状ではなく「本編・特典・ボーナス」などの用途種別）
--

DROP TABLE IF EXISTS `disc_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `disc_kinds` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

-- disc_kinds は初期データを持たない。
-- ディスクの用途区分（本編・特典等）は運用時に Catalog GUI の「マスタ管理」タブから
-- プロジェクトの運用実態に合わせて登録する設計。

--
-- Table structure for table `track_content_kinds`
-- トラック内容種別マスタ（歌・劇伴・ドラマ・ラジオ 等）
--

DROP TABLE IF EXISTS `track_content_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `track_content_kinds` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `track_content_kinds` WRITE;
INSERT INTO `track_content_kinds` (`kind_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('SONG','歌','Song',1),
  ('BGM','劇伴','BGM',2),
  ('DRAMA','ドラマ','Drama',3),
  ('RADIO','ラジオ','Radio',4),
  ('LIVE','ライブ','Live',5),
  ('TIE_UP','タイアップ','Tie-up',6),
  ('OTHER','その他','Other',99);
UNLOCK TABLES;

--
-- Table structure for table `song_music_classes`
-- 曲の音楽種別マスタ（OP/ED/挿入歌/キャラソン 等）
--

DROP TABLE IF EXISTS `song_music_classes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_music_classes` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `song_music_classes` WRITE;
INSERT INTO `song_music_classes` (`class_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('OP','オープニング主題歌','Opening Theme',1),
  ('ED','エンディング主題歌','Ending Theme',2),
  ('INSERT','挿入歌','Insert Song',3),
  ('CHARA','キャラクターソング','Character Song',3),
  ('IMAGE','イメージソング','Image Song',4),
  ('MOVIE','映画主題歌','Movie Theme',6),
  ('OTHER','その他','Other',99);
UNLOCK TABLES;

--
-- 曲のアレンジ種別マスタ（song_arrange_classes）は v1.1.0 で廃止した。
-- songs がアレンジ単位（メロディ + アレンジ）となったため、アレンジを別マスタで
-- 分類管理する必要が無くなった。songs.title の中に「Ver. MaxHeart」等のアレンジ名を含める。
--

--
-- Table structure for table `song_size_variants`
-- 曲のサイズ種別マスタ（TVサイズ・フル・ショート 等）
--

DROP TABLE IF EXISTS `song_size_variants`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_size_variants` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `song_size_variants` WRITE;
INSERT INTO `song_size_variants` (`variant_code`,`name_ja`,`name_en`,`display_order`) VALUES
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
UNLOCK TABLES;

--
-- Table structure for table `song_part_variants`
-- 曲のパート種別マスタ（ボーカル/カラオケ/ガイドメロディ等のバリエーション）
-- 旧データの tracks.song_type に相当する軸。size_variants とは直交し、
-- 1 トラックは (song_recording_id, size_variant_code, part_variant_code) で一意に特定される。
--

DROP TABLE IF EXISTS `song_part_variants`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_part_variants` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

LOCK TABLES `song_part_variants` WRITE;
INSERT INTO `song_part_variants` (`variant_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('VOCAL',          '歌入り',                                     'Vocal',                            1),
  ('INST',           'オリジナル・カラオケ',                       'Instrumental',                     2),
  ('INST_STR',       'ストリングス入りオリジナル・メロディ・カラオケ','Inst+Strings',                   3),
  ('INST_GUIDE',     'オリジナル・メロディ・カラオケ',             'Inst+Guide Melody',                4),
  ('INST_CHO',       'コーラス入りオリジナル・カラオケ',           'Inst+Chorus',                      5),
  ('INST_CHO_GUIDE', 'コーラス入りオリジナル・メロディ・カラオケ', 'Inst+Chorus+Guide',                6),
  ('INST_PART_VO',   'パート歌入りオリジナル・カラオケ',           'Inst+Partial Vocal',               7),
  ('OTHER',          'その他',                                     'Other',                           99);
UNLOCK TABLES;

--
-- Table structure for table `products`
-- 商品テーブル：価格・発売日・販売元などの「販売単位」メタ情報を管理する。
-- 主キーは「代表品番」(product_catalog_no)。1枚物は唯一のディスクの catalog_no、
-- 複数枚組は 1 枚目のディスクの catalog_no を採用する。
-- v1.1.1 よりシリーズ所属 (series_id) は discs 側の属性に移設された。
--

--
-- Table structure for table `product_companies`
-- v1.3.0 ブラッシュアップ stage 20 新設。
-- 商品（products）の発売元（label）／販売元（distributor）として紐付ける、
-- クレジット非依存の社名マスタ。クレジット系の companies / company_aliases とは独立。
-- 屋号系譜（前任/後任）は持たず、1 社 = 1 行・和名/かな/英名のみ保持する。
-- is_default_label / is_default_distributor は NewProductDialog の既定社指定フラグ。
-- 排他性（最大 1 行）はアプリ側（ProductCompaniesRepository 内のトランザクション）で担保する。
--

DROP TABLE IF EXISTS `product_companies`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `product_companies` (
  `product_company_id`     int NOT NULL AUTO_INCREMENT,
  `name_ja`                varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`              varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`                varchar(128) DEFAULT NULL,
  `is_default_label`       tinyint NOT NULL DEFAULT '0',
  `is_default_distributor` tinyint NOT NULL DEFAULT '0',
  `notes`                  text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`             varchar(64) DEFAULT NULL,
  `updated_by`             varchar(64) DEFAULT NULL,
  `is_deleted`             tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`product_company_id`),
  KEY `ix_product_companies_name_ja`   (`name_ja`),
  KEY `ix_product_companies_name_kana` (`name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `products`
--

DROP TABLE IF EXISTS `products`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `products` (
  `product_catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_short` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en` varchar(255) DEFAULT NULL,
  `product_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `release_date` date NOT NULL,
  `price_ex_tax` int DEFAULT NULL,
  `price_inc_tax` int DEFAULT NULL,
  `disc_count` tinyint unsigned NOT NULL DEFAULT '1',
  -- v1.3.0 ブラッシュアップ stage 20 確定版：旧 manufacturer / label / distributor フリーテキスト列を
  -- 撤去し、社名マスタ FK 列 2 つに完全置換。列順は「disc_count → label_pc_id → distributor_pc_id
  -- → amazon_asin」の意味的な順序に整えてある（流通系を中央に集約）。
  `label_product_company_id` int DEFAULT NULL,
  `distributor_product_company_id` int DEFAULT NULL,
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
  KEY `ix_products_kind` (`product_kind_code`),
  KEY `ix_products_release` (`release_date`),
  KEY `ix_products_label_pc` (`label_product_company_id`),
  KEY `ix_products_distributor_pc` (`distributor_product_company_id`),
  CONSTRAINT `fk_products_kind` FOREIGN KEY (`product_kind_code`) REFERENCES `product_kinds` (`kind_code`),
  CONSTRAINT `fk_products_label_pc` FOREIGN KEY (`label_product_company_id`) REFERENCES `product_companies` (`product_company_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_products_distributor_pc` FOREIGN KEY (`distributor_product_company_id`) REFERENCES `product_companies` (`product_company_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_products_disc_count_pos` CHECK ((`disc_count` >= 1)),
  CONSTRAINT `ck_products_price_ex_nonneg` CHECK (((`price_ex_tax` is null) or (`price_ex_tax` >= 0))),
  CONSTRAINT `ck_products_price_inc_nonneg` CHECK (((`price_inc_tax` is null) or (`price_inc_tax` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `discs`
-- 物理ディスクテーブル：品番を主キーとする（商品が複数枚組でも品番は各ディスク固有）。
-- 単品商品は disc_no_in_set=NULL、複数枚組は 1,2,3... を格納する。
-- product_catalog_no は「商品の代表品番」を指し、複数枚組の場合は全ディスクが同じ代表品番を持つ。
-- v1.1.1 よりシリーズ所属 (series_id) は本テーブル側の属性となった。NULL はオールスターズ扱い。
--
-- 長さ・構造情報の列は、メディアに応じて排他的に使う（どちらかが NULL）:
--   CD / CD_ROM:    total_tracks + total_length_frames を使用、num_chapters / total_length_ms は NULL
--   BD / DVD:       num_chapters + total_length_ms       を使用、total_tracks / total_length_frames は NULL
--   DL / OTHER:     いずれも NULL でよい（運用任意）
--

DROP TABLE IF EXISTS `discs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `discs` (
  `catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `product_catalog_no` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_short` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `title_en` varchar(255) DEFAULT NULL,
  `series_id` int DEFAULT NULL,
  `disc_no_in_set` int unsigned DEFAULT NULL,
  `disc_kind_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `media_format` enum('CD','CD_ROM','DVD','BD','DL','OTHER') NOT NULL DEFAULT 'CD',
  `mcn` varchar(13) DEFAULT NULL,
  `total_tracks` tinyint unsigned DEFAULT NULL,
  `total_length_frames` int unsigned DEFAULT NULL,
  `total_length_ms` bigint unsigned DEFAULT NULL,
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
  KEY `ix_discs_series` (`series_id`),
  KEY `ix_discs_mcn` (`mcn`),
  KEY `ix_discs_cddb` (`cddb_disc_id`),
  KEY `ix_discs_musicbrainz` (`musicbrainz_disc_id`),
  CONSTRAINT `fk_discs_product` FOREIGN KEY (`product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_discs_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_discs_kind` FOREIGN KEY (`disc_kind_code`) REFERENCES `disc_kinds` (`kind_code`),
  CONSTRAINT `ck_discs_disc_no_pos` CHECK (((`disc_no_in_set` is null) or (`disc_no_in_set` >= 1))),
  CONSTRAINT `ck_discs_total_tracks_nonneg` CHECK (((`total_tracks` is null) or (`total_tracks` >= 0))),
  CONSTRAINT `ck_discs_total_length_nonneg` CHECK (((`total_length_frames` is null) or (`total_length_frames` >= 0))),
  CONSTRAINT `ck_discs_total_length_ms_nonneg` CHECK (((`total_length_ms` is null) or (`total_length_ms` >= 0))),
  CONSTRAINT `ck_discs_num_chapters_nonneg` CHECK (((`num_chapters` is null) or (`num_chapters` >= 0)))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `songs`
-- 歌マスタ：作品としての 1 曲（作詞・作曲者を軸にした 1 意）。
-- 歌唱者違いやアレンジ違いは song_recordings 側で表現する。
--

DROP TABLE IF EXISTS `songs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `songs` (
  `song_id` int NOT NULL AUTO_INCREMENT,
  `title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `title_kana` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `music_class_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `series_id` int DEFAULT NULL,
  `lyricist_name` varchar(255) DEFAULT NULL,
  `lyricist_name_kana` varchar(255) DEFAULT NULL,
  `composer_name` varchar(255) DEFAULT NULL,
  `composer_name_kana` varchar(255) DEFAULT NULL,
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `song_recordings`
-- 歌の録音バージョン：同じ曲（= メロディ + アレンジ）に対する歌唱者違い・バリエーション違いを管理する。
-- 同じ曲 (song_id) に複数の録音が紐づく想定（例: 五條真由美版・うちやえゆか版・劇場版タイアップ版など）。
-- サイズ/パート（フル/TV/カラオケ等）は tracks 側の列で表現するため、ここには持たない。
--

DROP TABLE IF EXISTS `song_recordings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_recordings` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bgm_sessions`
-- 劇伴の録音セッションマスタ。シリーズごとに session_no を 1, 2, 3, ... と採番する。
-- 同一シリーズ内にセッションが 1 つしか無くても session_no=1 を持つ（0 は使わない）。
-- 将来的に録音日・スタジオ名等の属性を追加するための器。
--

DROP TABLE IF EXISTS `bgm_sessions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bgm_sessions` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bgm_cues`
-- 劇伴（BGM）の音源 1 件 = 1 行。シリーズ × m_no_detail で 1 意。
-- m_no_detail は旧データ準拠の詳細表記（例: "M220b Rhythm Cut", "M01", "M224 ShortVer A"）。
-- 音源は (series_id, session_no, m_no_detail) の 3 階層に位置するが、シリーズ内では
-- m_no_detail だけで 1 意になる運用（同一シリーズ内で同じ m_no_detail が複数セッションに出現しない）のため、
-- PK は (series_id, m_no_detail)、session_no は属性として持つ。
-- m_no_class は枝番を畳んだグループキー（例: "M220"）。PK ではないが検索・ソート用にインデックスを張る。
-- v1.1.0 の旧 bgm_cues + bgm_recordings の二階層構造は廃止し、1 テーブルに統合した。
--

DROP TABLE IF EXISTS `bgm_cues`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bgm_cues` (
  `series_id` int NOT NULL,
  `m_no_detail` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `session_no` tinyint unsigned NOT NULL DEFAULT 1,
  -- v1.3.0：同一 bgm_session 内での並び順を表す。マイグレ初期投入では M 番号を
  -- 自然順 + 枝番無し優先でソートして 1, 2, 3... を振る。Catalog 側の劇伴管理画面
  -- から DnD で随時更新可能。0 はマイグレ未実行・新規追加直後の暫定値。
  `seq_in_session` int NOT NULL DEFAULT 0,
  `m_no_class` varchar(64) DEFAULT NULL,
  `menu_title` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `composer_name` varchar(255) DEFAULT NULL,
  `composer_name_kana` varchar(255) DEFAULT NULL,
  `arranger_name` varchar(255) DEFAULT NULL,
  `arranger_name_kana` varchar(255) DEFAULT NULL,
  `length_seconds` smallint unsigned DEFAULT NULL,
  `notes` text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  -- 仮 M 番号フラグ（v1.1.3 追加）。
  -- M 番号が判明していない音源に対して "_temp_034108" のような暫定値を m_no_detail に入れている運用があるため、
  -- 「この行の m_no_detail は内部管理用の仮番号である」ことを示す。
  -- 1 の行は閲覧 UI / Web 公開側で m_no_detail を素で出さず「(番号不明)」等に差し替える。
  -- マスタメンテ画面ではフラグごと可視にして、判明した時点で実番号に直して 0 に戻す運用。
  `is_temp_m_no` tinyint NOT NULL DEFAULT 0,
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `tracks`
-- 物理トラックテーブル：ディスクの物理位置を表す。
-- content_kind_code により、内容は SONG / BGM / DRAMA / RADIO / LIVE / TIE_UP / OTHER のいずれかに分類される。
-- SONG 時は song_recording_id、BGM 時は bgm_series_id + bgm_m_no_detail が NOT NULL となる整合性制約付き
-- （MySQL の CHECK は ON DELETE SET NULL と同列を参照する FK との併用が禁止されているため、
--  INSERT/UPDATE 時の整合性はトリガーで担保する）。
-- DRAMA / RADIO 等のタイトルは track_title_override に格納する。
-- track_title_override は SONG/BGM でも収録盤固有の表記を保持するために使用してよい。
--

DROP TABLE IF EXISTS `tracks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `tracks` (
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
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `video_chapters`
-- BD/DVD のチャプター情報を格納する物理層テーブル。`tracks` が CD-DA 専用なのと同様に、
-- `video_chapters` は光学ディスク (discs.media_format IN ('BD','DVD')) のチャプター専用。
-- BDAnalyzer の MPLS/IFO パース結果が投入される。title・part_type・notes は Catalog GUI 側で
-- 後から手動補完する前提で、読み取り直後は NULL。
-- 複合 PK (catalog_no, chapter_no) でシーケンシャルな 1 始まり。
-- start_time_ms はプレイリスト先頭からの開始時刻（ミリ秒）、duration_ms は各章の尺。
-- source_kind でパース元を区別（MPLS=Blu-ray .mpls、IFO=DVD .IFO、MANUAL=手動追加）。
--

DROP TABLE IF EXISTS `video_chapters`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `video_chapters` (
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
  `is_deleted` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`catalog_no`,`chapter_no`),
  KEY `ix_video_chapters_part_type` (`part_type`),
  CONSTRAINT `fk_video_chapters_disc` FOREIGN KEY (`catalog_no`) REFERENCES `discs` (`catalog_no`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_video_chapters_part_type` FOREIGN KEY (`part_type`) REFERENCES `part_types` (`part_type`),
  CONSTRAINT `ck_video_chapters_chapter_no_pos` CHECK ((`chapter_no` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Triggers for table `tracks`
-- content_kind_code と SONG/BGM 参照列の整合性、および複合 PK (catalog_no, track_no, sub_order) の
-- sub_order 分割行のルールをトリガーで担保する:
--   (1) content_kind_code に応じて song_recording_id / bgm_* が必須 or NULL でなければならない
--   (2) sub_order > 0 の行は物理情報 (start_lba / length_frames / isrc / is_data_track /
--       has_pre_emphasis / is_copy_permitted / cd_text_title / cd_text_performer) が全て NULL
--       でなければならない。物理情報は必ず sub_order=0 の親行にだけ持つ
--   (3) 同一 (catalog_no, track_no) に複数の sub_order 行がある場合、全ての行の
--       content_kind_code が一致していなければならない (SONG と BGM の混在を禁止)
-- BGM 参照は (bgm_series_id, bgm_m_no_detail) の 2 列セットで、
-- いずれか 1 つでも NOT NULL なら BGM とみなす運用（通常は 2 列すべて NOT NULL / すべて NULL のどちらか）。
--

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

-- ===========================================================================
-- クレジット管理基盤 (v1.2.0 追加)
--   persons / person_aliases / person_alias_persons
--     ... 人物マスタ・人物名義（時期別表記、前後リンク）・共同名義の多対多
--   companies / company_aliases / logos
--     ... 企業マスタ・屋号（前後リンク）・屋号配下の CI バージョン別ロゴ
--   characters / character_aliases
--     ... キャラクターマスタ（全プリキュア統一・series 非依存）と
--         キャラクター名義（話数別表記）。声優のキャスティング情報は v1.2.4 で
--         専用テーブルを廃止し、credit_block_entries（CHARACTER_VOICE エントリ）
--         に登場している事実を「キャスティング」とみなす運用に統一した。
--   precures
--     ... プリキュアの基本属性（変身前後の名義 4 本・誕生日・声優・肌色 HSL/RGB・
--         学校・クラス・家業）。v1.2.4 新設。
--   character_relation_kinds / character_family_relations
--     ... キャラクター続柄マスタと家族関係（characters 同士、双方向）。
--         プリキュア以外のキャラ（敵・とりまく人々）でも汎用的に使える。v1.2.4 新設。
--   roles / series_role_format_overrides
--     ... 役職マスタ（NORMAL/SERIAL/THEME_SONG/VOICE_CAST/COMPANY_ONLY/LOGO_ONLY）
--         ・シリーズ × 役職ごとの書式上書き（期間管理付き）
--   credits / credit_cards / credit_card_roles / credit_role_blocks /
--   credit_block_entries
--     ... クレジット本体。シリーズ or エピソードに紐付き、OP/ED の 2 種、
--         CARDS（複数枚）or ROLL（巻物）の 2 形式。カード内で役職を tier=1/2 の
--         2 段、ブロックで役職下のレイアウト（rows×cols）、エントリで実値（人物名義／
--         キャラクター名義／企業名義／ロゴ／歌録音／フリーテキスト）を持つ。
--   episode_theme_songs
--     ... エピソード × 主題歌（OP/ED 各 1、INSERT 複数可）の紐付け。
--         クレジットの THEME_SONG エントリはここから引いてレンダリングする。
-- ===========================================================================

--
-- Table structure for table `persons`
-- 人物マスタ。同一人物の同一性を持たせる単位。表記揺れは person_aliases で管理する。
--
DROP TABLE IF EXISTS `persons`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `persons` (
  `person_id`        int                                                                  NOT NULL AUTO_INCREMENT,
  `family_name`      varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks   DEFAULT NULL,
  `given_name`       varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks   DEFAULT NULL,
  `full_name`        varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  NOT NULL,
  `full_name_kana`   varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  DEFAULT NULL,
  `name_en`          varchar(128)                                                         DEFAULT NULL,
  `notes`            text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`       timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`       varchar(64)  DEFAULT NULL,
  `updated_by`       varchar(64)  DEFAULT NULL,
  `is_deleted`       tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`person_id`),
  KEY `ix_persons_full_name`      (`full_name`),
  KEY `ix_persons_full_name_kana` (`full_name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `person_aliases`
-- 人物の名義（表記）マスタ。改名時は predecessor_alias_id / successor_alias_id で
-- 前後リンクし、データ的に同一人物の表記履歴を辿れる。
--
DROP TABLE IF EXISTS `person_aliases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `person_aliases` (
  `alias_id`              int                                                                 NOT NULL AUTO_INCREMENT,
  `name`                  varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`               varchar(128) DEFAULT NULL,
  -- v1.2.3 追加：表示用上書き文字列。ユニット名義などで定形外の長い表記が必要なケース用。
  -- 非 NULL のときアプリ側の表示ロジックは name より優先してこの値を使う。
  `display_text_override` varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `predecessor_alias_id`  int          DEFAULT NULL,
  `successor_alias_id`    int          DEFAULT NULL,
  `valid_from`            date         DEFAULT NULL,
  `valid_to`              date         DEFAULT NULL,
  `notes`                 text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`            varchar(64)  DEFAULT NULL,
  `updated_by`            varchar(64)  DEFAULT NULL,
  `is_deleted`            tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`alias_id`),
  KEY `ix_person_aliases_name`         (`name`),
  KEY `ix_person_aliases_name_kana`    (`name_kana`),
  KEY `ix_person_aliases_predecessor`  (`predecessor_alias_id`),
  KEY `ix_person_aliases_successor`    (`successor_alias_id`),
  CONSTRAINT `fk_person_aliases_predecessor` FOREIGN KEY (`predecessor_alias_id`) REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_person_aliases_successor`   FOREIGN KEY (`successor_alias_id`)   REFERENCES `person_aliases` (`alias_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `person_alias_persons`
-- 名義 ⇄ 人物の多対多。通常 1 alias = 1 person。共同名義（稀）のみ複数行が立つ。
--
DROP TABLE IF EXISTS `person_alias_persons`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `person_alias_persons` (
  `alias_id`   int             NOT NULL,
  `person_id`  int             NOT NULL,
  `person_seq` tinyint unsigned NOT NULL DEFAULT '1',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`alias_id`,`person_id`),
  UNIQUE KEY `uq_pap_alias_seq` (`alias_id`,`person_seq`),
  KEY `ix_pap_person` (`person_id`),
  CONSTRAINT `fk_pap_alias`  FOREIGN KEY (`alias_id`)  REFERENCES `person_aliases` (`alias_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_pap_person` FOREIGN KEY (`person_id`) REFERENCES `persons`        (`person_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `companies`
-- 企業マスタ。分社化等で別企業として登録する場合は新規レコードを立て、
-- company_aliases 側の前後リンクで系譜を辿る。
--
DROP TABLE IF EXISTS `companies`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `companies` (
  `company_id`      int                                                                 NOT NULL AUTO_INCREMENT,
  `name`            varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`       varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`         varchar(128) DEFAULT NULL,
  `founded_date`    date         DEFAULT NULL,
  `dissolved_date`  date         DEFAULT NULL,
  `notes`           text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64)  DEFAULT NULL,
  `updated_by`      varchar(64)  DEFAULT NULL,
  `is_deleted`      tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`company_id`),
  KEY `ix_companies_name`      (`name`),
  KEY `ix_companies_name_kana` (`name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `company_aliases`
-- 企業の名義（屋号）マスタ。屋号変更や分社化等で前後の屋号を辿れるよう
-- predecessor_alias_id / successor_alias_id を持つ（FK は自テーブルへの自参照）。
--
DROP TABLE IF EXISTS `company_aliases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `company_aliases` (
  `alias_id`              int                                                                 NOT NULL AUTO_INCREMENT,
  `company_id`            int                                                                 NOT NULL,
  `name`                  varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`               varchar(128) DEFAULT NULL,
  `predecessor_alias_id`  int          DEFAULT NULL,
  `successor_alias_id`    int          DEFAULT NULL,
  `valid_from`            date         DEFAULT NULL,
  `valid_to`              date         DEFAULT NULL,
  `notes`                 text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`            timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`            varchar(64)  DEFAULT NULL,
  `updated_by`            varchar(64)  DEFAULT NULL,
  `is_deleted`            tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`alias_id`),
  KEY `ix_company_aliases_company`     (`company_id`),
  KEY `ix_company_aliases_name`        (`name`),
  KEY `ix_company_aliases_predecessor` (`predecessor_alias_id`),
  KEY `ix_company_aliases_successor`   (`successor_alias_id`),
  CONSTRAINT `fk_company_aliases_company`     FOREIGN KEY (`company_id`)           REFERENCES `companies`       (`company_id`) ON DELETE CASCADE   ON UPDATE CASCADE,
  CONSTRAINT `fk_company_aliases_predecessor` FOREIGN KEY (`predecessor_alias_id`) REFERENCES `company_aliases` (`alias_id`)   ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_company_aliases_successor`   FOREIGN KEY (`successor_alias_id`)   REFERENCES `company_aliases` (`alias_id`)   ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `logos`
-- 屋号配下の CI バージョン別ロゴ。クレジット中で entry が指す対象は
-- 屋号（company_alias）か、特定 CI バージョンのロゴ（logo）かのいずれか。
--
DROP TABLE IF EXISTS `logos`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `logos` (
  `logo_id`           int                                                                NOT NULL AUTO_INCREMENT,
  `company_alias_id`  int                                                                NOT NULL,
  `ci_version_label`  varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `valid_from`        date  DEFAULT NULL,
  `valid_to`          date  DEFAULT NULL,
  `description`       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`             text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`        varchar(64)  DEFAULT NULL,
  `updated_by`        varchar(64)  DEFAULT NULL,
  `is_deleted`        tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`logo_id`),
  UNIQUE KEY `uq_logos_alias_ci` (`company_alias_id`,`ci_version_label`),
  CONSTRAINT `fk_logos_company_alias` FOREIGN KEY (`company_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `character_kinds`
-- キャラクター区分マスタ（v1.2.0 工程 F でマスタ化）。
-- 旧 characters.character_kind ENUM（MAIN/SUPPORT/GUEST/MOB/OTHER）は廃止し、
-- ユーザーの分類軸である「プリキュア / 仲間たち / 敵 / とりまく人々」の 4 類型を
-- マスタとして保持する。運用者が後から類型を追加・改名できるよう独立テーブル化。
--
DROP TABLE IF EXISTS `character_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `character_kinds` (
  `character_kind` varchar(32)                                                       NOT NULL,
  `name_ja`        varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`        varchar(64)                                                         DEFAULT NULL,
  `display_order`  tinyint unsigned                                                    DEFAULT NULL,
  `notes`          text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64)  DEFAULT NULL,
  `updated_by`     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`character_kind`),
  UNIQUE KEY `uq_character_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- character_kinds の初期データ。プリキュア作品の典型的な人物分類 4 類型。
-- 業務側で必要があれば後から追加・並べ替えできる。
--
LOCK TABLES `character_kinds` WRITE;
INSERT INTO `character_kinds` (`character_kind`,`name_ja`,`name_en`,`display_order`) VALUES
  ('PRECURE',    'プリキュア',     'Precure',                10),
  ('ALLY',       '仲間たち',       'Allies',                 20),
  ('VILLAIN',    '敵',             'Villains',               30),
  ('SUPPORTING', 'とりまく人々',   'Supporting Characters',  40);
UNLOCK TABLES;

--
-- Table structure for table `characters`
-- キャラクターマスタ。全プリキュアを通じて統一的に管理（series_id は持たない）。
-- All Stars・春映画・コラボ等でシリーズをまたいで再登場するキャラは同一行を共有する。
-- character_kind は character_kinds マスタを参照する FK（v1.2.0 工程 F で ENUM から変更）。
-- 旧仕様の MAIN/SUPPORT/GUEST/MOB/OTHER は廃止され、PRECURE/ALLY/VILLAIN/SUPPORTING の 4 類型に置換。
--
DROP TABLE IF EXISTS `characters`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `characters` (
  `character_id`    int                                                                 NOT NULL AUTO_INCREMENT,
  `name`            varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`       varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`         varchar(128) DEFAULT NULL,
  `character_kind`  varchar(32)                                                          NOT NULL DEFAULT 'PRECURE',
  `notes`           text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64)  DEFAULT NULL,
  `updated_by`      varchar(64)  DEFAULT NULL,
  `is_deleted`      tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`character_id`),
  KEY `ix_characters_name`      (`name`),
  KEY `ix_characters_name_kana` (`name_kana`),
  KEY `ix_characters_kind`      (`character_kind`),
  CONSTRAINT `fk_characters_kind` FOREIGN KEY (`character_kind`) REFERENCES `character_kinds` (`character_kind`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `character_aliases`
-- キャラクターの名義（表記）マスタ。話数・状況による表記揺れを記録する。
-- 例: "キュアブラック" / "ブラック" / "美墨なぎさ" / "ふたりはプリキュア　なぎさ"。
--
DROP TABLE IF EXISTS `character_aliases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `character_aliases` (
  `alias_id`     int                                                                  NOT NULL AUTO_INCREMENT,
  `character_id` int                                                                  NOT NULL,
  `name`         varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  NOT NULL,
  `name_kana`    varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks  DEFAULT NULL,
  `name_en`      varchar(128) DEFAULT NULL,
  -- v1.2.1 で valid_from / valid_to を撤去。alias 自体は時系列情報を持たず、
  -- 表記揺れごとに別 alias 行として並存させる運用に統一した。
  -- v1.2.4 で character_voice_castings は廃止し、声優キャスティングの所在は
  -- credit_block_entries の CHARACTER_VOICE エントリ（実際にクレジットされた事実）に
  -- 統合した（ノンクレ除いて、その役柄でクレジットされている＝キャスティング、という
  -- 業務ルール）。
  `notes`        text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`   varchar(64)  DEFAULT NULL,
  `updated_by`   varchar(64)  DEFAULT NULL,
  `is_deleted`   tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`alias_id`),
  KEY `ix_character_aliases_character` (`character_id`),
  KEY `ix_character_aliases_name`      (`name`),
  CONSTRAINT `fk_character_aliases_character` FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `character_relation_kinds` (v1.2.4 追加)
-- キャラクター続柄マスタ。「自分から見た続柄」を表すコード（FATHER / MOTHER /
-- BROTHER_OLDER / SISTER_YOUNGER / GRANDMOTHER 等）を持ち、family_relations から参照される。
--
DROP TABLE IF EXISTS `character_relation_kinds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `character_relation_kinds` (
  `relation_code`   varchar(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin       NOT NULL,
  `name_ja`         varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_en`         varchar(64)  DEFAULT NULL,
  `display_order`   tinyint unsigned DEFAULT NULL,
  `notes`           text         CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`      timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`      varchar(64)  DEFAULT NULL,
  `updated_by`      varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`relation_code`),
  UNIQUE KEY `uq_character_relation_kinds_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- character_relation_kinds の初期データ。プリキュア作品で頻出する続柄を網羅。
-- 業務側で必要があれば後から追加・並べ替え可能（display_order は 10 単位飛び番）。
LOCK TABLES `character_relation_kinds` WRITE;
INSERT INTO `character_relation_kinds` (`relation_code`,`name_ja`,`name_en`,`display_order`) VALUES
  ('FATHER',         '父',           'Father',                 10),
  ('MOTHER',         '母',           'Mother',                 20),
  ('BROTHER_OLDER',  '兄',           'Older Brother',          30),
  ('BROTHER_YOUNGER','弟',           'Younger Brother',        40),
  ('SISTER_OLDER',   '姉',           'Older Sister',           50),
  ('SISTER_YOUNGER', '妹',           'Younger Sister',         60),
  ('GRANDFATHER',    '祖父',         'Grandfather',            70),
  ('GRANDMOTHER',    '祖母',         'Grandmother',            80),
  ('UNCLE',          '叔父・伯父',   'Uncle',                  90),
  ('AUNT',           '叔母・伯母',   'Aunt',                  100),
  ('COUSIN',         'いとこ',       'Cousin',                110),
  ('PET',            'ペット',       'Pet',                   120),
  ('OTHER_FAMILY',   'その他家族',   'Other Family Member',   130);
UNLOCK TABLES;

--
-- Table structure for table `character_family_relations` (v1.2.4 追加)
-- キャラクター ⇄ キャラクターの続柄関係（汎用、プリキュア以外でも使える）。
-- 1 行が「character_id から見た related_character_id の続柄」を表す。
-- 双方向で完全表現するときは、A→B（FATHER）と B→A（SISTER_YOUNGER 等）の 2 行を
-- 立てる運用（自動補完はトリガでは行わず、UI 側の明示操作に委ねる）。
--
DROP TABLE IF EXISTS `character_family_relations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `character_family_relations` (
  `character_id`         int             NOT NULL,
  `related_character_id` int             NOT NULL,
  `relation_code`        varchar(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `display_order`        tinyint unsigned DEFAULT NULL,
  `notes`                text            CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`           timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`           varchar(64)     DEFAULT NULL,
  `updated_by`           varchar(64)     DEFAULT NULL,
  PRIMARY KEY (`character_id`,`related_character_id`,`relation_code`),
  KEY `ix_cfr_related`        (`related_character_id`),
  KEY `ix_cfr_relation_code`  (`relation_code`),
  CONSTRAINT `fk_cfr_character`     FOREIGN KEY (`character_id`)         REFERENCES `characters`             (`character_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_cfr_related`       FOREIGN KEY (`related_character_id`) REFERENCES `characters`             (`character_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_cfr_relation_kind` FOREIGN KEY (`relation_code`)        REFERENCES `character_relation_kinds`(`relation_code`) ON DELETE RESTRICT ON UPDATE CASCADE
  -- 自己参照禁止（character_id = related_character_id）は当初 CHECK 制約 ck_cfr_no_self で
  -- 表現したかったが、MySQL 8.0.16+ では「FK の参照アクション（CASCADE 等）で使う列を CHECK
  -- 制約から参照できない」(Error 3823) という制約があり、character_id / related_character_id が
  -- いずれも fk_cfr_character / fk_cfr_related の ON DELETE CASCADE / ON UPDATE CASCADE で
  -- 使われている本表ではその CHECK が定義不能。よって自己参照禁止は本テーブル直後の
  -- BEFORE INSERT / BEFORE UPDATE トリガ tr_cfr_check_no_self_bi / _bu に統合して
  -- INSERT/UPDATE 時点で SIGNAL する方式に変更した（precures の character_id 整合性検証と
  -- 同じ運用パターン）。
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Triggers for table `character_family_relations` (v1.2.4 修正)
-- 自己参照禁止（character_id = related_character_id）を SIGNAL で強制する。
-- CHECK 制約で表現できない MySQL 8.0 の制約（Error 3823）を回避するため、
-- precures の character_id 整合性検証と同じトリガパターンを採用。
--
DROP TRIGGER IF EXISTS `tr_cfr_check_no_self_bi`;
DROP TRIGGER IF EXISTS `tr_cfr_check_no_self_bu`;

DELIMITER ;;
CREATE TRIGGER `tr_cfr_check_no_self_bi` BEFORE INSERT ON `character_family_relations`
FOR EACH ROW
BEGIN
  IF NEW.character_id = NEW.related_character_id THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'character_family_relations: character_id and related_character_id must differ (self-relation forbidden)';
  END IF;
END;;
CREATE TRIGGER `tr_cfr_check_no_self_bu` BEFORE UPDATE ON `character_family_relations`
FOR EACH ROW
BEGIN
  IF NEW.character_id = NEW.related_character_id THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'character_family_relations: character_id and related_character_id must differ (self-relation forbidden)';
  END IF;
END;;
DELIMITER ;

--
-- Table structure for table `precures` (v1.2.4 追加)
-- プリキュア本体マスタ。1 行 = 1 プリキュア。
-- 名義は character_aliases を参照する 4 本の FK で表現する：
--   pre_transform_alias_id ... 変身前（必須、例: 美墨なぎさ）
--   transform_alias_id     ... 変身後（必須、例: キュアブラック）
--   transform2_alias_id    ... 変身後 2（任意、強化形態など）
--   alt_form_alias_id      ... 別形態（任意）
-- これら 4 本の alias が指す character_id は同一でなければならない（変身前後で
-- 別キャラ扱いにするレギュラー枠は無いという業務ルール）。MySQL 8.0 の制約で
-- CHECK から別テーブルを参照できないため、整合性は BEFORE INSERT/UPDATE
-- トリガ tr_precures_check_character_bi / _bu で SIGNAL する方式で担保する。
--
-- 誕生日は birth_month (1-12) と birth_day (1-31) の 2 列に正規化保持し、
-- 「m月d日」「Month d」の表示はアプリ側で生成する（DB に和文・英文の
-- 文字列を二重に持たない）。
--
-- 肌色は HSL（H 0-360 / S 0-100 / L 0-100）と RGB（R/G/B 0-255）の両方を
-- 持ち、運用安定までは GUI 側で「HSL から復元した色」「RGB から復元した色」を
-- 並べて表示し、両者の整合性（CIE76 ΔE）を画面上で目視確認する設計（v1.2.4）。
--
DROP TABLE IF EXISTS `precures`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `precures` (
  `precure_id`             int                NOT NULL AUTO_INCREMENT,
  `pre_transform_alias_id` int                NOT NULL,
  `transform_alias_id`     int                NOT NULL,
  `transform2_alias_id`    int                DEFAULT NULL,
  `alt_form_alias_id`      int                DEFAULT NULL,
  `birth_month`            tinyint unsigned   DEFAULT NULL,
  `birth_day`              tinyint unsigned   DEFAULT NULL,
  `voice_actor_person_id`  int                DEFAULT NULL,
  `skin_color_h`           smallint unsigned  DEFAULT NULL,
  `skin_color_s`           tinyint unsigned   DEFAULT NULL,
  `skin_color_l`           tinyint unsigned   DEFAULT NULL,
  `skin_color_r`           tinyint unsigned   DEFAULT NULL,
  `skin_color_g`           tinyint unsigned   DEFAULT NULL,
  `skin_color_b`           tinyint unsigned   DEFAULT NULL,
  `school`                 varchar(128)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `school_class`           varchar(64)        CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `family_business`        varchar(255)       CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`                  text               CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`             varchar(64)        DEFAULT NULL,
  `updated_by`             varchar(64)        DEFAULT NULL,
  `is_deleted`             tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`precure_id`),
  UNIQUE KEY `uq_precures_transform_alias`     (`transform_alias_id`),
  KEY `ix_precures_pre_transform_alias`        (`pre_transform_alias_id`),
  KEY `ix_precures_transform2_alias`           (`transform2_alias_id`),
  KEY `ix_precures_alt_form_alias`             (`alt_form_alias_id`),
  KEY `ix_precures_voice_actor`                (`voice_actor_person_id`),
  CONSTRAINT `fk_precures_pre_transform`  FOREIGN KEY (`pre_transform_alias_id`) REFERENCES `character_aliases` (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_transform`      FOREIGN KEY (`transform_alias_id`)     REFERENCES `character_aliases` (`alias_id`)  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_transform2`     FOREIGN KEY (`transform2_alias_id`)    REFERENCES `character_aliases` (`alias_id`)  ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_alt_form`       FOREIGN KEY (`alt_form_alias_id`)      REFERENCES `character_aliases` (`alias_id`)  ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_precures_voice_actor`    FOREIGN KEY (`voice_actor_person_id`)  REFERENCES `persons`           (`person_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_precures_birth_month`    CHECK (`birth_month` IS NULL OR (`birth_month` BETWEEN 1 AND 12)),
  CONSTRAINT `ck_precures_birth_day`      CHECK (`birth_day`   IS NULL OR (`birth_day`   BETWEEN 1 AND 31)),
  CONSTRAINT `ck_precures_skin_h`         CHECK (`skin_color_h` IS NULL OR (`skin_color_h` BETWEEN 0 AND 360)),
  CONSTRAINT `ck_precures_skin_s`         CHECK (`skin_color_s` IS NULL OR (`skin_color_s` BETWEEN 0 AND 100)),
  CONSTRAINT `ck_precures_skin_l`         CHECK (`skin_color_l` IS NULL OR (`skin_color_l` BETWEEN 0 AND 100))
  -- skin_color_r / _g / _b は TINYINT UNSIGNED の型範囲が 0-255 と一致するため CHECK 不要。
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Triggers for table `precures` (v1.2.4 追加)
-- 「変身前 / 変身後 / 変身後 2 / 別形態」の 4 alias が指す character_id が
-- すべて同一であることを INSERT / UPDATE のたびに検証する。NULL 列はスキップ。
-- MySQL 8.0 では CHECK 制約から別テーブル参照ができないため、トリガで SIGNAL。
--
DROP TRIGGER IF EXISTS `tr_precures_check_character_bi`;
DROP TRIGGER IF EXISTS `tr_precures_check_character_bu`;

DELIMITER ;;
CREATE TRIGGER `tr_precures_check_character_bi` BEFORE INSERT ON `precures`
FOR EACH ROW
BEGIN
  DECLARE c_pre   INT DEFAULT NULL;
  DECLARE c_main  INT DEFAULT NULL;
  DECLARE c_main2 INT DEFAULT NULL;
  DECLARE c_alt   INT DEFAULT NULL;
  SELECT character_id INTO c_pre   FROM character_aliases WHERE alias_id = NEW.pre_transform_alias_id;
  SELECT character_id INTO c_main  FROM character_aliases WHERE alias_id = NEW.transform_alias_id;
  IF NEW.transform2_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_main2 FROM character_aliases WHERE alias_id = NEW.transform2_alias_id;
    IF c_main2 <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform2_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF NEW.alt_form_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_alt FROM character_aliases WHERE alias_id = NEW.alt_form_alias_id;
    IF c_alt <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: alt_form_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF c_main <> c_pre THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform_alias_id must point to the same character as pre_transform_alias_id';
  END IF;
END;;
CREATE TRIGGER `tr_precures_check_character_bu` BEFORE UPDATE ON `precures`
FOR EACH ROW
BEGIN
  DECLARE c_pre   INT DEFAULT NULL;
  DECLARE c_main  INT DEFAULT NULL;
  DECLARE c_main2 INT DEFAULT NULL;
  DECLARE c_alt   INT DEFAULT NULL;
  SELECT character_id INTO c_pre   FROM character_aliases WHERE alias_id = NEW.pre_transform_alias_id;
  SELECT character_id INTO c_main  FROM character_aliases WHERE alias_id = NEW.transform_alias_id;
  IF NEW.transform2_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_main2 FROM character_aliases WHERE alias_id = NEW.transform2_alias_id;
    IF c_main2 <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform2_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF NEW.alt_form_alias_id IS NOT NULL THEN
    SELECT character_id INTO c_alt FROM character_aliases WHERE alias_id = NEW.alt_form_alias_id;
    IF c_alt <> c_pre THEN
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: alt_form_alias_id must point to the same character as pre_transform_alias_id';
    END IF;
  END IF;
  IF c_main <> c_pre THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'precures: transform_alias_id must point to the same character as pre_transform_alias_id';
  END IF;
END;;
DELIMITER ;

--
-- Table structure for table `series_precures` (v1.3.0 公開直前のデザイン整理で追加)
-- シリーズとプリキュアの多対多関連テーブル。1 プリキュアが複数シリーズに渡って
-- レギュラー出演するケース（クロスオーバー映画でのレギュラー扱い、続編シリーズで
-- 引き続き登場、変身前の姿で出てきて変身しない出演 等）に対応するため、純粋な
-- 多対多関連テーブルとして設計する。precures テーブル側に series_id 列を追加する
-- 案は採用しない。
--
-- display_order は同シリーズ内のプリキュア並び順（0 始まり、昇順表示、デフォルト 0）。
-- 同値時は precure_id 昇順でタイブレーク。複数プリキュアが居る作品で「主役 → サブ」の
-- 順序を明示的に制御する用途。
--

DROP TABLE IF EXISTS `series_precures`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `series_precures` (
  `series_id`     int                NOT NULL,
  `precure_id`    int                NOT NULL,
  `display_order` tinyint unsigned   NOT NULL DEFAULT 0,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)        DEFAULT NULL,
  `updated_by`    varchar(64)        DEFAULT NULL,
  PRIMARY KEY (`series_id`, `precure_id`),
  KEY `ix_series_precures_precure` (`precure_id`),
  CONSTRAINT `fk_series_precures_series`
    FOREIGN KEY (`series_id`)  REFERENCES `series`   (`series_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_precures_precure`
    FOREIGN KEY (`precure_id`) REFERENCES `precures` (`precure_id`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `roles`
-- クレジット内の役職マスタ。role_format_kind により entry の取り回しが変わる。
--   NORMAL       … 役職: 名義列（脚本／演出／作画監督 等）
--   SERIAL       … 連載。書式テンプレートは role_templates テーブルで持つ
--   THEME_SONG   … 主題歌。entry が song_recording を持つ
--   VOICE_CAST   … 声の出演。entry がキャラクター名義 + 人物名義のペアを持つ
--   COMPANY_ONLY … 企業のみが並ぶ役職（制作著作・製作協力・レーベル等）
--   LOGO_ONLY    … ロゴのみが並ぶ役職
-- v1.2.0 工程 H-10 で default_format_template 列を撤去（書式は role_templates に統合）。
--
DROP TABLE IF EXISTS `roles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `roles` (
  `role_code`               varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `name_ja`                 varchar(64)  NOT NULL,
  `name_en`                 varchar(64)  DEFAULT NULL,
  `role_format_kind`        enum('NORMAL','SERIAL','THEME_SONG','VOICE_CAST','COMPANY_ONLY','LOGO_ONLY') NOT NULL DEFAULT 'NORMAL',
  `display_order`           smallint unsigned DEFAULT NULL,
  -- v1.3.0 ブラッシュアップ stage 16 Phase 4：HTML クレジット階層描画で
  -- 左カラム（役職名）を表示するかの制御フラグ。0=表示（既定）、1=非表示。
  -- 例：LABEL 役職は屋号だけを主題歌セクション末尾に並べて表示したいケースで 1 にする。
  -- 集計（CreditInvolvementIndex / 役職別ランキング / 企業関与一覧）には影響せず、
  -- 純粋に表示テンプレ側でのみ役職名カラムを抑止する。
  `hide_role_name_in_credit` tinyint NOT NULL DEFAULT '0',
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`role_code`),
  UNIQUE KEY `uq_roles_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- roles マスタの中身は運用者が業務側で投入する方針のため、schema.sql でも
-- データ INSERT は記載しない（テーブル定義のみ）。ダンプを取り直したときに
-- LOCK TABLES が再生成される可能性があるが、その場合はこの位置でデータ部分を
-- 取り除いて運用すること。

--
-- 役職系譜の関係テーブル（v1.3.0 ブラッシュアップ続編で追加）
--
-- 役職の系譜（変更元 → 変更先）は分裂・併合を含む多対多の関係なので、
-- 1 対 1 のカラム（旧 roles.successor_role_code）ではなく独立した関係テーブルで保持する。
-- from_role_code → to_role_code の有向辺を 1 行 1 関係として持ち、
-- 「無向辺」とみなして連結成分（クラスタ）をたどる運用を想定。
-- クラスタ代表は display_order が最小の役職（同点は role_code 昇順）。
--
-- 自己ループ（from = to）の防止について：
-- 本来 CHECK 制約で from_role_code <> to_role_code を強制したいが、MySQL 8 では
-- FK の参照アクション（CASCADE 等）で変更される列を CHECK で参照できない仕様
-- （Error 3823）のため、DB 制約としては設定しない。
-- アプリ層（RoleSuccessionsRepository.UpsertAsync）で自己ループを弾く方針。
--

DROP TABLE IF EXISTS `role_successions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `role_successions` (
  `from_role_code` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `to_role_code`   varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `notes`          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64) DEFAULT NULL,
  `updated_by`     varchar(64) DEFAULT NULL,
  PRIMARY KEY (`from_role_code`, `to_role_code`),
  KEY `idx_role_successions_to` (`to_role_code`),
  CONSTRAINT `fk_role_successions_from` FOREIGN KEY (`from_role_code`) REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE CASCADE,
  CONSTRAINT `fk_role_successions_to`   FOREIGN KEY (`to_role_code`)   REFERENCES `roles`(`role_code`) ON UPDATE CASCADE ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `role_templates`（v1.2.0 工程 H-10 で導入）
-- 役職テンプレート。旧設計の roles.default_format_template（既定）と
-- series_role_format_overrides（シリーズ別上書き）を統合した単一テーブル。
--   - series_id IS NULL ：既定テンプレ（全シリーズ共通）
--   - series_id IS NOT NULL：そのシリーズ専用のテンプレ
-- 解決ロジック：(role_code, series_id) で検索 → 無ければ (role_code, NULL) フォールバック。
-- 期間制限（valid_from/valid_to）は当面持たない（シンプルさを優先）。
--
DROP TABLE IF EXISTS `role_templates`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `role_templates` (
  `template_id`     int          NOT NULL AUTO_INCREMENT,
  `role_code`       varchar(32)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `series_id`       int              NULL,
  `format_template` text         NOT NULL,
  `notes`           text             NULL,
  `created_at`      datetime(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at`      datetime(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `created_by`      varchar(64)      NULL,
  `updated_by`      varchar(64)      NULL,
  PRIMARY KEY (`template_id`),
  UNIQUE KEY `uk_role_templates_role_series` (`role_code`,`series_id`),
  KEY `ix_role_templates_role` (`role_code`),
  KEY `ix_role_templates_series` (`series_id`),
  CONSTRAINT `fk_role_templates_role`   FOREIGN KEY (`role_code`) REFERENCES `roles`  (`role_code`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_role_templates_series` FOREIGN KEY (`series_id`) REFERENCES `series` (`series_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credits`
-- クレジット 1 件 = 1 行。シリーズ単位 or エピソード単位で OP/ED 各 1 件まで。
-- 本放送と円盤・配信の差し替えは個々のエントリ単位（credit_block_entries.is_broadcast_only）で
-- 行うため、クレジット本体（クレジット 1 件 = OP または ED の役職構成）には
-- 本放送限定フラグを持たせない（v1.2.0 工程 B' 再修正で is_broadcast_only 列を削除）。
-- scope=SERIES なら series_id 必須・episode_id NULL、scope=EPISODE はその逆。
-- part_type が NULL の行は「規定位置（part_types.default_credit_kind が
-- credit_kind と一致するパート）で流れる」を意味する。
--
-- なお scope_kind と series_id / episode_id の整合性は、本来 CHECK 制約で
-- 表現したいところだが、MySQL 8.0 では「ON DELETE CASCADE / SET NULL の参照
-- アクションを持つ FK が参照する列」を CHECK 制約に含めることができない
-- （Error 3823）ため、整合性チェックは下流の BEFORE INSERT/UPDATE トリガー
-- (trg_credits_b{i,u}_scope_consistency) として実装している。
--
DROP TABLE IF EXISTS `credits`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credits` (
  `credit_id`         int                                                          NOT NULL AUTO_INCREMENT,
  `scope_kind`        enum('SERIES','EPISODE')                                     NOT NULL,
  `series_id`         int                                                          DEFAULT NULL,
  `episode_id`        int                                                          DEFAULT NULL,
  `credit_kind`       varchar(16)                                                  NOT NULL,
  `part_type`         varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin        DEFAULT NULL,
  `presentation`      enum('CARDS','ROLL')                                         NOT NULL DEFAULT 'CARDS',
  `notes`             text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`        timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`        varchar(64)  DEFAULT NULL,
  `updated_by`        varchar(64)  DEFAULT NULL,
  `is_deleted`        tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`credit_id`),
  UNIQUE KEY `uq_credit_series_kind`  (`series_id`,`credit_kind`),
  UNIQUE KEY `uq_credit_episode_kind` (`episode_id`,`credit_kind`),
  KEY `ix_credit_part_type` (`part_type`),
  KEY `ix_credit_credit_kind` (`credit_kind`),
  CONSTRAINT `fk_credits_series`      FOREIGN KEY (`series_id`)   REFERENCES `series`       (`series_id`)  ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_credits_episode`     FOREIGN KEY (`episode_id`)  REFERENCES `episodes`     (`episode_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_credits_part_type`   FOREIGN KEY (`part_type`)   REFERENCES `part_types`   (`part_type`),
  CONSTRAINT `fk_credits_credit_kind` FOREIGN KEY (`credit_kind`) REFERENCES `credit_kinds` (`kind_code`)  ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_cards`
-- クレジット内のカード 1 枚 = 1 行。presentation=ROLL のクレジットでは card_seq=1 の
-- 1 行のみが立ち、その下に複数の役職／ブロックがぶら下がる。
--
DROP TABLE IF EXISTS `credit_cards`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_cards` (
  `card_id`    int             NOT NULL AUTO_INCREMENT,
  `credit_id`  int             NOT NULL,
  `card_seq`   tinyint unsigned NOT NULL,
  `notes`      text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64)  DEFAULT NULL,
  `updated_by` varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_id`),
  UNIQUE KEY `uq_credit_cards_credit_seq` (`credit_id`,`card_seq`),
  CONSTRAINT `fk_credit_cards_credit` FOREIGN KEY (`credit_id`) REFERENCES `credits` (`credit_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_credit_cards_seq_pos` CHECK ((`card_seq` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_card_tiers`
-- カード内の Tier（段組）1 つ = 1 行。tier_no=1（上段）／2（下段）。
-- v1.2.0 工程 G で追加。それ以前は credit_card_roles 内の `tier` 列で表現していたが、
-- 「ブランク Tier（役職ゼロの空 Tier）」を表現できないという問題があったため、独立テーブル化した。
-- カードが新規作成されると tier_no=1 が 1 行自動投入される（CreditCardsRepository.InsertAsync で実装）。
--
DROP TABLE IF EXISTS `credit_card_tiers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_card_tiers` (
  `card_tier_id`  int             NOT NULL AUTO_INCREMENT,
  `card_id`       int             NOT NULL,
  `tier_no`       tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_tier_id`),
  UNIQUE KEY `uq_card_tier` (`card_id`,`tier_no`),
  CONSTRAINT `fk_card_tier_card`  FOREIGN KEY (`card_id`) REFERENCES `credit_cards` (`card_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_tier_no`    CHECK ((`tier_no` BETWEEN 1 AND 2))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_card_groups`
-- Tier 内の Group（サブグループ）1 つ = 1 行。group_no は 1 始まり。
-- v1.2.0 工程 G で追加。同 tier 内で役職同士が視覚的にサブグループを成すケース
-- （例：[美術監督・色彩設計] と [撮影監督・撮影助手] が同 tier の中で別塊として表示される）を表現。
-- Tier が新規作成されると group_no=1 が 1 行自動投入される（CreditCardTiersRepository.InsertAsync で実装）。
-- group_no は単純なシーケンスで、ユーザーが「+ Group」したときにインクリメントされる。
--
DROP TABLE IF EXISTS `credit_card_groups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_card_groups` (
  `card_group_id` int             NOT NULL AUTO_INCREMENT,
  `card_tier_id`  int             NOT NULL,
  `group_no`      tinyint unsigned NOT NULL,
  `notes`         text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)  DEFAULT NULL,
  `updated_by`    varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_group_id`),
  UNIQUE KEY `uq_card_group` (`card_tier_id`,`group_no`),
  CONSTRAINT `fk_card_group_tier` FOREIGN KEY (`card_tier_id`) REFERENCES `credit_card_tiers` (`card_tier_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `ck_card_group_no`   CHECK ((`group_no` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_card_roles`
-- カード内に登場する役職 1 つ = 1 行。レイアウト位置は所属する Group（card_group_id）と
-- グループ内左右順（order_in_group）で表現する。
-- v1.2.0 工程 G で大幅刷新：旧 (card_id, tier, group_in_tier, order_in_group) の 4 列複合キーから、
--   - card_id / tier / group_in_tier 列を削除
--   - card_group_id（→ credit_card_groups.card_group_id）の FK 1 本に集約
-- に変更。Card / Tier / Group の階層関係は FK チェーンで一意に決まる
-- （card_role → card_group → card_tier → card）。
-- role_code を NULL にできるのは「ブランクロール（ロゴ単独表示用の枠）」用途。
--
DROP TABLE IF EXISTS `credit_card_roles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_card_roles` (
  `card_role_id`   int                                                   NOT NULL AUTO_INCREMENT,
  `card_group_id`  int                                                   NOT NULL,
  `role_code`      varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `order_in_group` tinyint unsigned                                      NOT NULL,
  `notes`          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`     varchar(64)  DEFAULT NULL,
  `updated_by`     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`card_role_id`),
  UNIQUE KEY `uq_card_role_pos`  (`card_group_id`,`order_in_group`),
  KEY `ix_card_role_role` (`role_code`),
  CONSTRAINT `fk_card_role_group` FOREIGN KEY (`card_group_id`) REFERENCES `credit_card_groups` (`card_group_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_card_role_role`  FOREIGN KEY (`role_code`)     REFERENCES `roles`              (`role_code`)     ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_card_role_order_pos` CHECK ((`order_in_group` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_role_blocks`
-- 役職下のブロック 1 つ = 1 行。多くは 1 役職 1 ブロック。
-- col_count はブロック内エントリを「何カラムで並べるか」の表示意図を保持する列。
-- 行数はカラム数とエントリ数の従属関係で実行時に決まるため、独立した row_count 列は
-- v1.2.0 工程 H 補修で物理削除した（旧 row_count は『運用者が明示する余地』を持って
-- いたが、実エントリ数との不整合という不正状態を生む余地もあったため）。
-- 旧来のコメント：「rows / cols は v1.2.0 工程 F-fix3 で row_count / col_count に
--   リネームされた（MySQL 8.0 で ROWS がウィンドウ関数用の予約語に追加されたため、
--   SELECT 等でバッククォート漏れによる構文エラーが起きやすかった）」を併記。
-- leading_company_alias_id にはブロック先頭に企業名を出すケースの企業名義を入れる。
--
DROP TABLE IF EXISTS `credit_role_blocks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_role_blocks` (
  `block_id`                  int             NOT NULL AUTO_INCREMENT,
  `card_role_id`              int             NOT NULL,
  `block_seq`                 tinyint unsigned NOT NULL,
  `col_count`                 tinyint unsigned NOT NULL DEFAULT '1',
  `leading_company_alias_id`  int             DEFAULT NULL,
  `notes`                     text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                varchar(64)  DEFAULT NULL,
  `updated_by`                varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`block_id`),
  UNIQUE KEY `uq_block_card_role_seq` (`card_role_id`,`block_seq`),
  KEY `ix_block_lead_company` (`leading_company_alias_id`),
  CONSTRAINT `fk_block_card_role`    FOREIGN KEY (`card_role_id`)             REFERENCES `credit_card_roles` (`card_role_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_block_lead_company` FOREIGN KEY (`leading_company_alias_id`) REFERENCES `company_aliases`   (`alias_id`)     ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_block_seq_pos`       CHECK ((`block_seq` >= 1)),
  CONSTRAINT `ck_block_col_count_pos` CHECK ((`col_count` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `credit_block_entries`
-- ブロック内のエントリ 1 つ = 1 行。entry_kind に応じて参照先カラムが決まる:
--   PERSON          → person_alias_id
--   CHARACTER_VOICE → person_alias_id (声優側) + character_alias_id か raw_character_text
--   COMPANY         → company_alias_id
--   LOGO            → logo_id
--   TEXT            → raw_text (マスタ未登録のフリーテキスト)
-- v1.2.0 工程 H で SONG エントリ種別と song_recording_id 列を物理削除。
-- 主題歌の楽曲は episode_theme_songs が真実の源泉となり、クレジット側では
-- 役職レベルで episode_theme_songs を JOIN したテンプレ展開で表示する運用に切り替え。
-- entry_kind と各参照列の整合性は trigger trg_credit_block_entries_* で担保する。
-- affiliation_company_alias_id / affiliation_text は人物名義の小カッコ所属用。
-- parallel_with_entry_id は「A / B」併記の相手 entry を自参照する任意フィールド。
--
DROP TABLE IF EXISTS `credit_block_entries`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `credit_block_entries` (
  `entry_id`                       int             NOT NULL AUTO_INCREMENT,
  `block_id`                       int             NOT NULL,
  `is_broadcast_only`              tinyint(1)      NOT NULL DEFAULT 0,
  `entry_seq`                      smallint unsigned NOT NULL,
  `entry_kind`                     enum('PERSON','CHARACTER_VOICE','COMPANY','LOGO','TEXT') NOT NULL,
  `person_alias_id`                int             DEFAULT NULL,
  `character_alias_id`             int             DEFAULT NULL,
  `raw_character_text`             varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `company_alias_id`               int             DEFAULT NULL,
  `logo_id`                        int             DEFAULT NULL,
  `raw_text`                       varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_company_alias_id`   int             DEFAULT NULL,
  `affiliation_text`               varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `parallel_with_entry_id`         int             DEFAULT NULL,
  `notes`                          text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                     timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                     timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                     varchar(64)  DEFAULT NULL,
  `updated_by`                     varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`entry_id`),
  -- v1.2.0 工程 B' 再修正：is_broadcast_only を含めた 3 列 UNIQUE。
  -- 同一 (block_id, entry_seq) 位置に「円盤・配信用 (フラグ 0)」と「本放送用 (フラグ 1)」を
  -- 並立させてロゴ等の差し替えを表現する用途。
  UNIQUE KEY `uq_block_entries_block_seq` (`block_id`,`is_broadcast_only`,`entry_seq`),
  KEY `ix_be_person`         (`person_alias_id`),
  KEY `ix_be_character`      (`character_alias_id`),
  KEY `ix_be_company`        (`company_alias_id`),
  KEY `ix_be_logo`           (`logo_id`),
  KEY `ix_be_aff_company`    (`affiliation_company_alias_id`),
  KEY `ix_be_parallel`       (`parallel_with_entry_id`),
  CONSTRAINT `fk_be_block`             FOREIGN KEY (`block_id`)                     REFERENCES `credit_role_blocks`   (`block_id`)          ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_be_person_alias`      FOREIGN KEY (`person_alias_id`)              REFERENCES `person_aliases`       (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_character_alias`   FOREIGN KEY (`character_alias_id`)           REFERENCES `character_aliases`    (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_company_alias`     FOREIGN KEY (`company_alias_id`)             REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_logo`              FOREIGN KEY (`logo_id`)                      REFERENCES `logos`                (`logo_id`)           ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_aff_company_alias` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases`      (`alias_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_be_parallel`          FOREIGN KEY (`parallel_with_entry_id`)       REFERENCES `credit_block_entries` (`entry_id`)          ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `ck_be_seq_pos` CHECK ((`entry_seq` >= 1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `episode_theme_songs`
-- 各エピソードに紐づく OP 主題歌（最大 1）／ED 主題歌（最大 1）／挿入歌（複数可）。
-- v1.2.0 工程 B' で is_broadcast_only フラグを導入。本放送限定の例外的な主題歌を
-- 持たせたい場合に 1 を立てた追加行を別途持たせる運用とする。デフォルト 0 行が
-- 「本放送・Blu-ray・配信ともに同じ」を表す（多くの作品ではフラグ 0 行のみ）。
-- PK は 4 列複合 (episode_id, is_broadcast_only, theme_kind, seq)。
-- クレジットの THEME_SONG ロールエントリは、このテーブルから歌情報を引いて
-- レンダリングする想定。
-- v1.3.0：旧 insert_seq 列を seq にリネーム。値は劇中で流れた順を表す汎用カラム
-- （OP/ED/INSERT を区別せずエピソード単位で 1, 2, 3, ... と劇中順）。旧 CHECK 制約
-- ck_ets_op_ed_no_insert_seq（OP/ED は 0 固定、INSERT は >=1）は撤廃。
--
DROP TABLE IF EXISTS `episode_theme_songs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `episode_theme_songs` (
  `episode_id`              int                                                  NOT NULL,
  `is_broadcast_only`       tinyint(1)                                           NOT NULL DEFAULT 0,
  `theme_kind`              enum('OP','ED','INSERT')                             NOT NULL,
  `seq`                     tinyint unsigned                                     NOT NULL DEFAULT '0',
  `usage_actuality`         enum('NORMAL','BROADCAST_NOT_CREDITED','CREDITED_NOT_BROADCAST') NOT NULL DEFAULT 'NORMAL',
  `song_recording_id`       int                                                  NOT NULL,
  `notes`                   text  CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`              timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`              varchar(64)  DEFAULT NULL,
  `updated_by`              varchar(64)  DEFAULT NULL,
  PRIMARY KEY (`episode_id`,`is_broadcast_only`,`theme_kind`,`seq`),
  KEY `ix_ets_song_recording` (`song_recording_id`),
  CONSTRAINT `fk_ets_episode`        FOREIGN KEY (`episode_id`)             REFERENCES `episodes`        (`episode_id`)        ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_ets_song_recording` FOREIGN KEY (`song_recording_id`)      REFERENCES `song_recordings` (`song_recording_id`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `episode_uses`（v1.3.0 新設）
-- 各エピソードのパート（アバン・OP・A パート・B パート・ED・予告 等）で使用された
-- 音声コンテンツ（歌・劇伴・ドラマパート・ラジオ・ジングル・その他）を記録するテーブル。
-- content_kind_code で SONG / BGM / DRAMA / RADIO / JINGLE / OTHER を区別し、
-- SONG なら song_recording_id、BGM なら (bgm_series_id, bgm_m_no_detail) を非 NULL とする
-- 整合性制約はトリガー側で担保（MySQL 8.0 では FK と参照アクションを含む CHECK が併用不可）。
-- SiteBuilder のエピソード詳細・劇伴詳細「使用回数」・楽曲詳細「使用エピソード」逆引きで使う。
--
DROP TABLE IF EXISTS `episode_uses`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `episode_uses` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

-- 注意：episode_uses の content_kind_code ⇄ 各参照列の整合性は、本 schema.sql には
-- トリガ定義を含めない（純粋なテーブル定義スナップショットとする）。マイグレ
-- v1.3.0_add_episode_uses.sql 側の trg_episode_uses_bi_fk_consistency /
-- trg_episode_uses_bu_fk_consistency を別途適用する想定。

--
-- Triggers for tables `credits` and `credit_block_entries`
-- MySQL 8.0 では FK の参照アクション列を CHECK に含められない（Error 3823）ため、
-- credits の scope_kind ⇄ series_id/episode_id 排他、および
-- credit_block_entries の entry_kind ⇄ 各参照列の整合性は、
-- いずれも BEFORE INSERT/UPDATE トリガーで担保する（tracks と同パターン）。
--

DROP TRIGGER IF EXISTS `trg_credits_bi_scope_consistency`;
DROP TRIGGER IF EXISTS `trg_credits_bu_scope_consistency`;
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bi_consistency`;
DROP TRIGGER IF EXISTS `trg_credit_block_entries_bu_consistency`;

DELIMITER ;;

CREATE TRIGGER `trg_credits_bi_scope_consistency`
BEFORE INSERT ON `credits`
FOR EACH ROW
BEGIN
  IF NEW.scope_kind = 'SERIES' AND (NEW.series_id IS NULL OR NEW.episode_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=SERIES requires series_id NOT NULL and episode_id NULL';
  END IF;
  IF NEW.scope_kind = 'EPISODE' AND (NEW.episode_id IS NULL OR NEW.series_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=EPISODE requires episode_id NOT NULL and series_id NULL';
  END IF;
END;;

CREATE TRIGGER `trg_credits_bu_scope_consistency`
BEFORE UPDATE ON `credits`
FOR EACH ROW
BEGIN
  IF NEW.scope_kind = 'SERIES' AND (NEW.series_id IS NULL OR NEW.episode_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=SERIES requires series_id NOT NULL and episode_id NULL';
  END IF;
  IF NEW.scope_kind = 'EPISODE' AND (NEW.episode_id IS NULL OR NEW.series_id IS NOT NULL) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credits: scope_kind=EPISODE requires episode_id NOT NULL and series_id NULL';
  END IF;
END;;

CREATE TRIGGER `trg_credit_block_entries_bi_consistency`
BEFORE INSERT ON `credit_block_entries`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;

  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END;;

CREATE TRIGGER `trg_credit_block_entries_bu_consistency`
BEFORE UPDATE ON `credit_block_entries`
FOR EACH ROW
BEGIN
  IF NEW.entry_kind = 'PERSON' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=PERSON requires person_alias_id';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.person_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires person_alias_id (the seiyuu side)';
  END IF;
  IF NEW.entry_kind = 'CHARACTER_VOICE' AND NEW.character_alias_id IS NULL AND (NEW.raw_character_text IS NULL OR NEW.raw_character_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=CHARACTER_VOICE requires character_alias_id or raw_character_text';
  END IF;
  IF NEW.entry_kind = 'COMPANY' AND NEW.company_alias_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=COMPANY requires company_alias_id';
  END IF;
  IF NEW.entry_kind = 'LOGO' AND NEW.logo_id IS NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=LOGO requires logo_id';
  END IF;
  IF NEW.entry_kind = 'TEXT' AND (NEW.raw_text IS NULL OR NEW.raw_text = '') THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: entry_kind=TEXT requires non-empty raw_text';
  END IF;
  IF NEW.entry_kind <> 'PERSON' AND NEW.entry_kind <> 'CHARACTER_VOICE' AND NEW.person_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: person_alias_id allowed only for entry_kind in (PERSON, CHARACTER_VOICE)';
  END IF;
  IF NEW.entry_kind <> 'CHARACTER_VOICE' AND (NEW.character_alias_id IS NOT NULL OR (NEW.raw_character_text IS NOT NULL AND NEW.raw_character_text <> '')) THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: character_alias_id / raw_character_text allowed only for entry_kind=CHARACTER_VOICE';
  END IF;
  IF NEW.entry_kind <> 'COMPANY' AND NEW.company_alias_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: company_alias_id allowed only for entry_kind=COMPANY';
  END IF;
  IF NEW.entry_kind <> 'LOGO' AND NEW.logo_id IS NOT NULL THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: logo_id allowed only for entry_kind=LOGO';
  END IF;
  IF NEW.entry_kind <> 'TEXT' AND NEW.raw_text IS NOT NULL AND NEW.raw_text <> '' THEN
    SIGNAL SQLSTATE '45000'
      SET MESSAGE_TEXT = 'credit_block_entries: raw_text allowed only for entry_kind=TEXT';
  END IF;
END;;

DELIMITER ;

-- ===========================================================================
-- v1.2.3 追加: 音楽系クレジット構造化テーブル群
-- ===========================================================================

--
-- Table structure for table `person_alias_members`
-- ユニット名義の構成メンバー（連名の中身）。
-- メンバーは PERSON（人物名義）または CHARACTER（キャラクター名義）。
-- ユニットのネスト（ユニットがユニットを内包）はトリガーで禁止。
-- 自己参照禁止（自分自身を PERSON メンバーにする）も同トリガーで担保する。
-- 当初は CHECK 制約 ck_pam_no_self で表現したかったが、MySQL 8.0.16+ の
-- 「FK 参照アクション列を CHECK で参照不可」(Error 3823) 制限により
-- トリガーに統合した。
--
DROP TABLE IF EXISTS `person_alias_members`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `person_alias_members` (
  `parent_alias_id`            int              NOT NULL,
  `member_seq`                 tinyint unsigned NOT NULL,
  `member_kind`                enum('PERSON','CHARACTER') NOT NULL,
  `member_person_alias_id`     int              DEFAULT NULL,
  `member_character_alias_id`  int              DEFAULT NULL,
  `notes`                      text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                 timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                 timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                 varchar(64)      DEFAULT NULL,
  `updated_by`                 varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`parent_alias_id`, `member_seq`),
  UNIQUE KEY `uq_pam_person_member`    (`parent_alias_id`, `member_person_alias_id`),
  UNIQUE KEY `uq_pam_character_member` (`parent_alias_id`, `member_character_alias_id`),
  KEY `ix_pam_member_person`    (`member_person_alias_id`),
  KEY `ix_pam_member_character` (`member_character_alias_id`),
  CONSTRAINT `ck_pam_kind_columns` CHECK (
       (`member_kind` = 'PERSON'    AND `member_person_alias_id`    IS NOT NULL AND `member_character_alias_id` IS NULL)
    OR (`member_kind` = 'CHARACTER' AND `member_character_alias_id` IS NOT NULL AND `member_person_alias_id`    IS NULL)
  ),
  CONSTRAINT `fk_pam_parent`    FOREIGN KEY (`parent_alias_id`)           REFERENCES `person_aliases`    (`alias_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_pam_person`    FOREIGN KEY (`member_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_pam_character` FOREIGN KEY (`member_character_alias_id`) REFERENCES `character_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- ネスト禁止 + 自己参照禁止トリガー（PERSON メンバーのみ対象）
--
DELIMITER ;;
CREATE TRIGGER `tr_pam_no_nested_unit_bi`
BEFORE INSERT ON `person_alias_members`
FOR EACH ROW
BEGIN
  IF NEW.member_kind = 'PERSON' THEN
    IF NEW.member_person_alias_id IS NOT NULL
       AND NEW.member_person_alias_id = NEW.parent_alias_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain itself as a member';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE parent_alias_id = NEW.member_person_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain another unit (member is already a unit)';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE member_kind = 'PERSON'
                 AND member_person_alias_id = NEW.parent_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot be nested (parent is already a member of another unit)';
    END IF;
  END IF;
END;;

CREATE TRIGGER `tr_pam_no_nested_unit_bu`
BEFORE UPDATE ON `person_alias_members`
FOR EACH ROW
BEGIN
  IF NEW.member_kind = 'PERSON' THEN
    IF NEW.member_person_alias_id IS NOT NULL
       AND NEW.member_person_alias_id = NEW.parent_alias_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain itself as a member';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE parent_alias_id = NEW.member_person_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot contain another unit (member is already a unit)';
    END IF;
    IF EXISTS (SELECT 1 FROM person_alias_members
               WHERE member_kind = 'PERSON'
                 AND member_person_alias_id = NEW.parent_alias_id) THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'unit cannot be nested (parent is already a member of another unit)';
    END IF;
  END IF;
END;;
DELIMITER ;

--
-- Table structure for table `song_credits`
-- 歌の作家連名（作詞 / 作曲 / 編曲）を順序付きで保持。
-- 既存 songs.{lyricist|composer|arranger}_name はフォールバックとして温存。
--
DROP TABLE IF EXISTS `song_credits`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_credits` (
  `song_id`             int              NOT NULL,
  `credit_role`         varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `credit_seq`          tinyint unsigned NOT NULL,
  `person_alias_id`     int              NOT NULL,
  `preceding_separator` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`song_id`, `credit_role`, `credit_seq`),
  KEY `ix_song_credits_alias` (`person_alias_id`),
  KEY `ix_song_credits_role` (`credit_role`),
  KEY `ix_song_credits_song` (`song_id`),
  CONSTRAINT `ck_song_credits_seq_pos` CHECK (`credit_seq` >= 1),
  CONSTRAINT `fk_song_credits_song`  FOREIGN KEY (`song_id`)         REFERENCES `songs`          (`song_id`)  ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_song_credits_alias` FOREIGN KEY (`person_alias_id`) REFERENCES `person_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_song_credits_role`  FOREIGN KEY (`credit_role`)     REFERENCES `roles`          (`role_code`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `song_recording_singers`
-- 歌唱者連名。billing_kind = PERSON / CHARACTER_WITH_CV の 2 値。
-- 既存 song_recordings.singer_name はフォールバックとして温存。
--
DROP TABLE IF EXISTS `song_recording_singers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `song_recording_singers` (
  `song_recording_id`         int              NOT NULL,
  `role_code`                 varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'VOCALS',
  `singer_seq`                tinyint unsigned NOT NULL,
  `billing_kind`              enum('PERSON','CHARACTER_WITH_CV') NOT NULL,
  `person_alias_id`           int              DEFAULT NULL,
  `character_alias_id`        int              DEFAULT NULL,
  `voice_person_alias_id`     int              DEFAULT NULL,
  `slash_person_alias_id`     int              DEFAULT NULL,
  `slash_character_alias_id`  int              DEFAULT NULL,
  `preceding_separator`       varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `affiliation_text`          varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`                     text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                varchar(64)      DEFAULT NULL,
  `updated_by`                varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`song_recording_id`, `role_code`, `singer_seq`),
  KEY `ix_srs_role`            (`role_code`),
  KEY `ix_srs_person`          (`person_alias_id`),
  KEY `ix_srs_character`       (`character_alias_id`),
  KEY `ix_srs_voice`           (`voice_person_alias_id`),
  KEY `ix_srs_slash_person`    (`slash_person_alias_id`),
  KEY `ix_srs_slash_character` (`slash_character_alias_id`),
  CONSTRAINT `ck_srs_seq_pos` CHECK (`singer_seq` >= 1),
  CONSTRAINT `ck_srs_kind_columns` CHECK (
       (`billing_kind` = 'PERSON'
          AND `person_alias_id`       IS NOT NULL
          AND `character_alias_id`    IS NULL
          AND `voice_person_alias_id` IS NULL
          AND `slash_character_alias_id` IS NULL)
    OR (`billing_kind` = 'CHARACTER_WITH_CV'
          AND `character_alias_id`    IS NOT NULL
          AND `voice_person_alias_id` IS NOT NULL
          AND `person_alias_id`       IS NULL
          AND `slash_person_alias_id` IS NULL)
  ),
  CONSTRAINT `fk_srs_recording`        FOREIGN KEY (`song_recording_id`)        REFERENCES `song_recordings`   (`song_recording_id`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_srs_role`             FOREIGN KEY (`role_code`)                REFERENCES `roles`             (`role_code`)         ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_srs_person`           FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_character`        FOREIGN KEY (`character_alias_id`)       REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_voice`            FOREIGN KEY (`voice_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_slash_person`     FOREIGN KEY (`slash_person_alias_id`)    REFERENCES `person_aliases`    (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_srs_slash_character`  FOREIGN KEY (`slash_character_alias_id`) REFERENCES `character_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bgm_cue_credits`
-- 劇伴の作家連名（作曲 / 編曲）。bgm_cues は (series_id, m_no_detail) 複合 PK。
-- 既存 bgm_cues.{composer|arranger}_name はフォールバックとして温存。
--
DROP TABLE IF EXISTS `bgm_cue_credits`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bgm_cue_credits` (
  `series_id`           int              NOT NULL,
  `m_no_detail`         varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `credit_role`         varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `credit_seq`          tinyint unsigned NOT NULL,
  `person_alias_id`     int              NOT NULL,
  `preceding_separator` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `notes`               text             CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`          timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`          varchar(64)      DEFAULT NULL,
  `updated_by`          varchar(64)      DEFAULT NULL,
  PRIMARY KEY (`series_id`, `m_no_detail`, `credit_role`, `credit_seq`),
  KEY `ix_bgm_cue_credits_alias` (`person_alias_id`),
  KEY `ix_bgm_cue_credits_role` (`credit_role`),
  KEY `ix_bgm_cue_credits_cue` (`series_id`, `m_no_detail`),
  CONSTRAINT `ck_bgm_cue_credits_seq_pos` CHECK (`credit_seq` >= 1),
  CONSTRAINT `fk_bgm_cue_credits_cue`   FOREIGN KEY (`series_id`, `m_no_detail`) REFERENCES `bgm_cues` (`series_id`, `m_no_detail`) ON DELETE CASCADE  ON UPDATE CASCADE,
  CONSTRAINT `fk_bgm_cue_credits_alias` FOREIGN KEY (`person_alias_id`)          REFERENCES `person_aliases` (`alias_id`)            ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_bgm_cue_credits_role`  FOREIGN KEY (`credit_role`)              REFERENCES `roles`          (`role_code`)           ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-05-07 (precure schema v1.2.4)