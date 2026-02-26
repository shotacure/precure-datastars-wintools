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
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by` varchar(64) DEFAULT NULL,
  `updated_by` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`part_type`),
  UNIQUE KEY `uq_part_types_display_order` (`display_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  PRIMARY KEY (`kind_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  PRIMARY KEY (`relation_code`)
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

-- Dump completed on 2026-02-25 15:02:56
