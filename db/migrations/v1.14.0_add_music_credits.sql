-- =====================================================================
-- v1.14.0_add_music_credits.sql
--
-- 音盤のブックレットに載る音楽クレジット（演奏・コーラス等 / レコーディング / 音盤製作）を持てるようにする。
--
--   roles + 1 列:
--     music_credit_group  音楽クレジットの区分。NULL は本編クレジットだけで使う役職。
--                         WRITING     作詞・作曲・編曲（song_credits / bgm_cue_credits と共通）
--                         PERFORMANCE 演奏・コーラス等（song_recording_singers の歌唱系役職もここ）
--                         RECORDING   レコーディング（録音側のスタッフ・スタジオ）
--                         RELEASE     音盤製作（盤側のスタッフ）
--   roles に音楽クレジット用の役職を追加（楽器・録音・音盤製作。ストリングス編曲は song_credits で使う）。
--
--   music_credits 新設:
--     1 行 = 1 名義。紐付け先（target_kind）は次のいずれか 1 つ。
--       SONG            曲（伴奏の演奏者など、曲に共通のクレジット）
--       SONG_RECORDING  録音（カバー等、録音ごとに違うクレジット）
--       BGM_SESSION     劇伴の録音セッション
--       PRODUCT         商品（音盤製作のスタッフ、録音の割り振りが決まらない録音側スタッフ）
--     名義は PERSON / CHARACTER / COMPANY / TEXT のいずれか。盤の印刷表記は名義と別に持つ
--     （printed_text：ローマ字・大文字小文字・誤記、role_label_text：役職の印刷表記、ensemble_note：編成注記）。
--     source_product_catalog_no はそのクレジットの根拠にした盤。
--
-- 冪等性: 列・表・役職の存在を確認してから追加する。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1140_music_credits;
DELIMITER $$
CREATE PROCEDURE _v1140_music_credits()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'roles'
                    AND COLUMN_NAME = 'music_credit_group') THEN
    ALTER TABLE `roles`
      ADD COLUMN `music_credit_group` enum('WRITING','PERFORMANCE','RECORDING','RELEASE') DEFAULT NULL
        COMMENT '音楽クレジットの区分（NULL は本編クレジットだけで使う役職）'
        AFTER `role_format_kind`;
  END IF;
END$$
DELIMITER ;
CALL _v1140_music_credits();
DROP PROCEDURE IF EXISTS _v1140_music_credits;

-- 既存の楽曲系役職に区分を付ける。
UPDATE `roles` SET `music_credit_group` = 'WRITING'
 WHERE `role_code` IN ('LYRICS','COMPOSITION','ARRANGEMENT') AND `music_credit_group` IS NULL;
UPDATE `roles` SET `music_credit_group` = 'PERFORMANCE'
 WHERE `role_code` IN ('VOCALS','BACKING_VOCALS','DIALOGUE') AND `music_credit_group` IS NULL;

-- 音楽クレジット用の役職。display_order は 2100 番台以降（本編の役職とは並びを分ける）。
INSERT IGNORE INTO `roles` (`role_code`, `name_ja`, `name_en`, `role_format_kind`, `music_credit_group`, `display_order`, `created_by`, `updated_by`) VALUES
  ('STRINGS_ARRANGEMENT',      'ストリングス編曲',           'Strings Arrangement',   'NORMAL', 'WRITING',     2100, 'migration', 'migration'),
  ('SYNTH_OPERATION',          'シンセサイザー・オペレート', 'Synth Operate',         'NORMAL', 'PERFORMANCE', 2200, 'migration', 'migration'),
  ('PROGRAMMING',              'プログラミング',             'Programming',           'NORMAL', 'PERFORMANCE', 2201, 'migration', 'migration'),
  ('KEYBOARD',                 'キーボード',                 'Keyboard',              'NORMAL', 'PERFORMANCE', 2202, 'migration', 'migration'),
  ('PIANO',                    'ピアノ',                     'Piano',                 'NORMAL', 'PERFORMANCE', 2203, 'migration', 'migration'),
  ('GUITAR',                   'ギター',                     'Guitar',                'NORMAL', 'PERFORMANCE', 2204, 'migration', 'migration'),
  ('ELECTRIC_GUITAR',          'エレキギター',               'Electric Guitar',       'NORMAL', 'PERFORMANCE', 2205, 'migration', 'migration'),
  ('BASS',                     'ベース',                     'Bass',                  'NORMAL', 'PERFORMANCE', 2206, 'migration', 'migration'),
  ('ELECTRIC_BASS',            'エレキベース',               'Electric Bass',         'NORMAL', 'PERFORMANCE', 2207, 'migration', 'migration'),
  ('DRUMS',                    'ドラム',                     'Drums',                 'NORMAL', 'PERFORMANCE', 2208, 'migration', 'migration'),
  ('PERCUSSION',               'パーカッション',             'Percussion',            'NORMAL', 'PERFORMANCE', 2209, 'migration', 'migration'),
  ('LATIN_PERCUSSION',         'ラテン・パーカッション',     'Latin Percussion',      'NORMAL', 'PERFORMANCE', 2210, 'migration', 'migration'),
  ('TRUMPET',                  'トランペット',               'Trumpet',               'NORMAL', 'PERFORMANCE', 2211, 'migration', 'migration'),
  ('TROMBONE',                 'トロンボーン',               'Trombone',              'NORMAL', 'PERFORMANCE', 2212, 'migration', 'migration'),
  ('HORN',                     'ホルン',                     'Horn',                  'NORMAL', 'PERFORMANCE', 2213, 'migration', 'migration'),
  ('SAXOPHONE',                'サックス',                   'Saxophone',             'NORMAL', 'PERFORMANCE', 2214, 'migration', 'migration'),
  ('ALTO_SAX',                 'アルト・サックス',           'Alto Sax',              'NORMAL', 'PERFORMANCE', 2215, 'migration', 'migration'),
  ('TENOR_SAX',                'テナー・サックス',           'Tenor Sax',             'NORMAL', 'PERFORMANCE', 2216, 'migration', 'migration'),
  ('BARITONE_SAX',             'バリトン・サックス',         'Baritone Sax',          'NORMAL', 'PERFORMANCE', 2217, 'migration', 'migration'),
  ('FLUTE',                    'フルート',                   'Flute',                 'NORMAL', 'PERFORMANCE', 2218, 'migration', 'migration'),
  ('CLARINET',                 'クラリネット',               'Clarinet',              'NORMAL', 'PERFORMANCE', 2219, 'migration', 'migration'),
  ('OBOE',                     'オーボエ',                   'Oboe',                  'NORMAL', 'PERFORMANCE', 2220, 'migration', 'migration'),
  ('BASSOON',                  'ファゴット',                 'Bassoon',               'NORMAL', 'PERFORMANCE', 2221, 'migration', 'migration'),
  ('HARP',                     'ハープ',                     'Harp',                  'NORMAL', 'PERFORMANCE', 2222, 'migration', 'migration'),
  ('VIOLIN',                   'ヴァイオリン',               'Violin',                'NORMAL', 'PERFORMANCE', 2223, 'migration', 'migration'),
  ('STRINGS',                  'ストリングス',               'Strings',               'NORMAL', 'PERFORMANCE', 2224, 'migration', 'migration'),
  ('BAND',                     '演奏',                       'Performed by',          'NORMAL', 'PERFORMANCE', 2225, 'migration', 'migration'),
  ('CONDUCTOR',                '指揮',                       'Conductor',             'NORMAL', 'PERFORMANCE', 2226, 'migration', 'migration'),
  ('SHOUT',                    'シャウト',                   'Shout',                 'NORMAL', 'PERFORMANCE', 2227, 'migration', 'migration'),
  ('HAND_CLAP',                '手拍子',                     'Hand Clap',             'NORMAL', 'PERFORMANCE', 2228, 'migration', 'migration'),
  ('CHORUS_DIRECTION',         '合唱指導',                   'Chorus Direction',      'NORMAL', 'PERFORMANCE', 2229, 'migration', 'migration'),
  ('SOUND_PRODUCER',           'サウンドプロデューサー',     'Sound Producer',        'NORMAL', 'RECORDING',   2300, 'migration', 'migration'),
  ('MUSIC_DIRECTOR',           'ディレクター',               'Director',              'NORMAL', 'RECORDING',   2301, 'migration', 'migration'),
  ('MUSIC_RECORDING_ENGINEER', 'レコーディングエンジニア',   'Recording Engineer',    'NORMAL', 'RECORDING',   2302, 'migration', 'migration'),
  ('MUSIC_ASSISTANT_ENGINEER', 'アシスタントエンジニア',     'Assistant Engineer',    'NORMAL', 'RECORDING',   2303, 'migration', 'migration'),
  ('RECORDING_AND_MIXING',     'レコーディング&ミックス',    'Recorded & Mixed',      'NORMAL', 'RECORDING',   2304, 'migration', 'migration'),
  ('MUSICIAN_COORDINATION',    'ミュージシャンコーディネイト', 'Musician Coordinate', 'NORMAL', 'RECORDING',   2305, 'migration', 'migration'),
  ('ARTIST_COORDINATION',      'アーティストコーディネイト', 'Artist Coordinate',     'NORMAL', 'RECORDING',   2306, 'migration', 'migration'),
  ('MUSIC_RECORDING_STUDIO',   'レコーディングスタジオ',     'Recording Studio',      'NORMAL', 'RECORDING',   2307, 'migration', 'migration'),
  ('A_AND_R',                  'A&R',                        'A&R',                   'NORMAL', 'RELEASE',     2400, 'migration', 'migration'),
  ('DISC_PROMOTION',           'プロモーション',             'Promotion',             'NORMAL', 'RELEASE',     2401, 'migration', 'migration'),
  ('MASTERING_ENGINEER',       'マスタリングエンジニア',     'Mastering Engineer',    'NORMAL', 'RELEASE',     2402, 'migration', 'migration'),
  ('MASTERING_STUDIO',         'マスタリングスタジオ',       'Mastering Studio',      'NORMAL', 'RELEASE',     2403, 'migration', 'migration'),
  ('COVER_ILLUSTRATION',       'ジャケットイラスト',         'Cover Illustration',    'NORMAL', 'RELEASE',     2404, 'migration', 'migration'),
  ('DISC_ART_DESIGN',          'アートデザイン',             'Art Design',            'NORMAL', 'RELEASE',     2405, 'migration', 'migration'),
  ('LINER_NOTES',              'ライナーノーツ',             'Liner Notes',           'NORMAL', 'RELEASE',     2406, 'migration', 'migration');

CREATE TABLE IF NOT EXISTS `music_credits` (
  `music_credit_id`              int NOT NULL AUTO_INCREMENT,
  -- 紐付け先。target_kind に対応する列だけを埋める（ck_music_credits_target）。
  `target_kind`                  enum('SONG','SONG_RECORDING','BGM_SESSION','PRODUCT') NOT NULL,
  `song_id`                      int DEFAULT NULL,
  `song_recording_id`            int DEFAULT NULL,
  `bgm_series_id`                int DEFAULT NULL,
  `bgm_session_no`               tinyint unsigned DEFAULT NULL,
  `product_catalog_no`           varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  -- roles.role_code と同じ型・照合順序にする（稼働中の DB では varchar(64) / utf8mb4_0900_ai_ci）。
  `role_code`                    varchar(64) NOT NULL,
  -- 紐付け先の中での表示順（盤の並び）。同じ役職の連名はこの順に並ぶ。
  `credit_seq`                   smallint unsigned NOT NULL,
  `entry_kind`                   enum('PERSON','CHARACTER','COMPANY','TEXT') NOT NULL,
  `person_alias_id`              int DEFAULT NULL,
  `character_alias_id`           int DEFAULT NULL,
  `company_alias_id`             int DEFAULT NULL,
  -- TEXT のときの表記（名義マスタに載せない名前）。
  `raw_text`                     varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- 盤の印刷表記が名義の表記と違うとき（ローマ字・大文字小文字・空白・誤記）の印刷どおりの表記。
  `printed_text`                 varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- printed_text が誤記（Fanky Y.K. など）なら 1。
  `is_misprint`                  tinyint(1) NOT NULL DEFAULT 0,
  -- 盤の役職の印刷表記（Guiter / Condu / レコーディングコーディネイト など）。NULL なら役職名で出す。
  `role_label_text`              varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- 編成の注記（Tp.3 / 86443 / ×8 / Vn×6、Vc×2 / 302st.304st. など）。
  `ensemble_note`                varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- 所属。屋号が名義マスタにあれば affiliation_company_alias_id、無ければ affiliation_text。
  `affiliation_company_alias_id` int DEFAULT NULL,
  `affiliation_text`             varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- 直前の名義との区切り（／ , & 、 など）。先頭や役職の切り替わりでは NULL。
  `preceding_separator`          varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  -- このクレジットの根拠にした盤。
  `source_product_catalog_no`    varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL,
  `notes`                        text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`                   timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`                   varchar(64) DEFAULT NULL,
  `updated_by`                   varchar(64) DEFAULT NULL,
  PRIMARY KEY (`music_credit_id`),
  KEY `ix_music_credits_song` (`song_id`),
  KEY `ix_music_credits_recording` (`song_recording_id`),
  KEY `ix_music_credits_session` (`bgm_series_id`, `bgm_session_no`),
  KEY `ix_music_credits_product` (`product_catalog_no`),
  KEY `ix_music_credits_role` (`role_code`),
  KEY `ix_music_credits_person` (`person_alias_id`),
  KEY `ix_music_credits_character` (`character_alias_id`),
  KEY `ix_music_credits_company` (`company_alias_id`),
  KEY `ix_music_credits_affiliation` (`affiliation_company_alias_id`),
  KEY `ix_music_credits_source` (`source_product_catalog_no`),
  -- CHECK が参照する列の FK は参照動作を RESTRICT に限る（MySQL は CASCADE / SET NULL の列を CHECK で参照できない）。
  CONSTRAINT `fk_music_credits_song`        FOREIGN KEY (`song_id`)           REFERENCES `songs` (`song_id`)                    ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_recording`   FOREIGN KEY (`song_recording_id`) REFERENCES `song_recordings` (`song_recording_id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_session`     FOREIGN KEY (`bgm_series_id`, `bgm_session_no`) REFERENCES `bgm_sessions` (`series_id`, `session_no`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_product`     FOREIGN KEY (`product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_role`        FOREIGN KEY (`role_code`)         REFERENCES `roles` (`role_code`)                  ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_music_credits_person`      FOREIGN KEY (`person_alias_id`)   REFERENCES `person_aliases` (`alias_id`)          ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_character`   FOREIGN KEY (`character_alias_id`) REFERENCES `character_aliases` (`alias_id`)      ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_company`     FOREIGN KEY (`company_alias_id`)  REFERENCES `company_aliases` (`alias_id`)         ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_affiliation` FOREIGN KEY (`affiliation_company_alias_id`) REFERENCES `company_aliases` (`alias_id`) ON DELETE RESTRICT ON UPDATE RESTRICT,
  CONSTRAINT `fk_music_credits_source`      FOREIGN KEY (`source_product_catalog_no`) REFERENCES `products` (`product_catalog_no`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `ck_music_credits_target` CHECK (
       (`target_kind` = 'SONG'           AND `song_id` IS NOT NULL AND `song_recording_id` IS NULL AND `bgm_series_id` IS NULL AND `bgm_session_no` IS NULL AND `product_catalog_no` IS NULL)
    OR (`target_kind` = 'SONG_RECORDING' AND `song_id` IS NULL AND `song_recording_id` IS NOT NULL AND `bgm_series_id` IS NULL AND `bgm_session_no` IS NULL AND `product_catalog_no` IS NULL)
    OR (`target_kind` = 'BGM_SESSION'    AND `song_id` IS NULL AND `song_recording_id` IS NULL AND `bgm_series_id` IS NOT NULL AND `bgm_session_no` IS NOT NULL AND `product_catalog_no` IS NULL)
    OR (`target_kind` = 'PRODUCT'        AND `song_id` IS NULL AND `song_recording_id` IS NULL AND `bgm_series_id` IS NULL AND `bgm_session_no` IS NULL AND `product_catalog_no` IS NOT NULL)),
  CONSTRAINT `ck_music_credits_entry` CHECK (
       (`entry_kind` = 'PERSON'    AND `person_alias_id` IS NOT NULL AND `character_alias_id` IS NULL AND `company_alias_id` IS NULL)
    OR (`entry_kind` = 'CHARACTER' AND `person_alias_id` IS NULL AND `character_alias_id` IS NOT NULL AND `company_alias_id` IS NULL)
    OR (`entry_kind` = 'COMPANY'   AND `person_alias_id` IS NULL AND `character_alias_id` IS NULL AND `company_alias_id` IS NOT NULL)
    OR (`entry_kind` = 'TEXT'      AND `person_alias_id` IS NULL AND `character_alias_id` IS NULL AND `company_alias_id` IS NULL AND `raw_text` IS NOT NULL)),
  CONSTRAINT `ck_music_credits_seq_pos` CHECK (`credit_seq` >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
