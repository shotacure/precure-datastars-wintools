-- ============================================================================
-- Migration: v1.3.0 ブラッシュアップ stage 20 — 商品の社名構造化（product_companies）
--
-- 目的：
--   音楽商品のレーベル名・販売元名をフリーテキストで持つ運用から、専用の
--   社名マスタ（product_companies）に和名・かな・英名で 1 行ずつ登録し、
--   products 側は社名 ID のみで保持する構造に切り替える。クレジット系の
--   companies / company_aliases とは独立した、商品メタ情報専用の社名マスタ
--   とすることで、クレジット側の集計に商品個別の流通情報が混ざらないようにする。
--
--   v1.3.0 stage 20 確定版（フリーテキスト全廃）：
--     旧 products.manufacturer / label / distributor は本マイグレで全て撤去する。
--     新規商品作成時の既定社（マーベラス等）は product_companies.is_default_label /
--     is_default_distributor フラグで指定する運用に切り替え。
--
-- 全体フロー：
--   1. product_companies テーブル作成（フラグ列含む）
--   2. 過去版で作成済みなら ADD COLUMN でフラグ列を追加（冪等対応）
--   3. products.manufacturer を label にマージしてから DROP
--   4. products に label_product_company_id / distributor_product_company_id の FK 列追加
--   5. 既存 products.label / distributor の distinct 値を product_companies に自動 INSERT
--   6. products の FK 列を name_ja 一致で埋める
--   7. products.label / distributor フリーテキスト列を DROP
--   8. 最頻出社に is_default_label / is_default_distributor フラグを立てる
--   9. 列順を「disc_count → label_pc_id → distributor_pc_id → amazon_asin」に整理
--
-- 冪等性：
--   全ステップで CREATE/ALTER/UPDATE の前に存在確認を行う。再実行で破壊的な
--   挙動はしない。マイグレを途中まで実行済みの環境でも、続きから安全に実行できる。
--
-- safe update mode（MySQL Workbench 既定）対応：
--   merge_manufacturer の UPDATE / auto_migrate_freetext の UPDATE / fill_pc_ids の
--   UPDATE は WHERE 句に PRIMARY KEY を含まないため、PROCEDURE 冒頭で当該セッションの
--   SQL_SAFE_UPDATES を一旦保存→0 に設定し、PROCEDURE 末尾と EXIT HANDLER で
--   元の値に必ず復元する。CALL 終了後の呼び出し元セッションの設定は変わらない。
-- ============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET TIME_ZONE='+00:00' */;

-- ----------------------------------------------------------------------------
-- 1) product_companies テーブル新設（クレジット非依存・商品メタ専用の社名マスタ）
-- ----------------------------------------------------------------------------
-- 1 社 = 1 行。和名・かな・英名のみのシンプル構造で、屋号系譜（前任/後任）も持たない。
-- 屋号系譜が必要な場面（クレジット記載名の年代変動など）は別途 companies /
-- company_aliases 側で表現する運用とし、本マスタはあくまで「商品の流通元として
-- レコードにどう書かれるか」だけを表すスナップショット名義として扱う。
--
-- is_default_label / is_default_distributor フラグ：
--   NewProductDialog 起動時の既定社を指定する。フラグの排他性（最大 1 行）は DB 制約
--   ではなくアプリ側（ProductCompaniesRepository.InsertAsync / UpdateAsync 内の
--   トランザクションで他の行を 0 に落としてからセット）で担保する。GUI でチェック ON に
--   すれば自動的に他社のフラグが落ちる設計。
CREATE TABLE IF NOT EXISTS `product_companies` (
  `product_company_id`     int                                                                 NOT NULL AUTO_INCREMENT,
  `name_ja`                varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks NOT NULL,
  `name_kana`              varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks DEFAULT NULL,
  `name_en`                varchar(128) DEFAULT NULL,
  `is_default_label`       tinyint NOT NULL DEFAULT 0,
  `is_default_distributor` tinyint NOT NULL DEFAULT 0,
  `notes`                  text CHARACTER SET utf8mb4 COLLATE utf8mb4_ja_0900_as_cs_ks,
  `created_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`             timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`             varchar(64) DEFAULT NULL,
  `updated_by`             varchar(64) DEFAULT NULL,
  `is_deleted`             tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`product_company_id`),
  KEY `ix_product_companies_name_ja`   (`name_ja`),
  KEY `ix_product_companies_name_kana` (`name_kana`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ----------------------------------------------------------------------------
-- 2) フラグ列の追加（過去版マイグレで作成済みの product_companies に対する補完）
-- ----------------------------------------------------------------------------
-- 旧 stage20 マイグレ版を既に実行している環境では product_companies は存在するが
-- フラグ列が無い。冪等性のため、列存在確認後に ADD COLUMN する。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_add_default_flag_columns //
CREATE PROCEDURE sp_v130_stage20_add_default_flag_columns()
BEGIN
  DECLARE col_exists INT DEFAULT 0;

  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'product_companies'
     AND COLUMN_NAME  = 'is_default_label';
  IF col_exists = 0 THEN
    ALTER TABLE product_companies
      ADD COLUMN `is_default_label` tinyint NOT NULL DEFAULT 0 AFTER `name_en`;
  END IF;

  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'product_companies'
     AND COLUMN_NAME  = 'is_default_distributor';
  IF col_exists = 0 THEN
    ALTER TABLE product_companies
      ADD COLUMN `is_default_distributor` tinyint NOT NULL DEFAULT 0 AFTER `is_default_label`;
  END IF;
END //

DELIMITER ;

CALL sp_v130_stage20_add_default_flag_columns();
DROP PROCEDURE IF EXISTS sp_v130_stage20_add_default_flag_columns;

-- ----------------------------------------------------------------------------
-- 3) products.manufacturer → products.label データマージ
-- ----------------------------------------------------------------------------
-- 旧 manufacturer 列が存在する場合のみ実施（再実行時の冪等性確保）。
-- まず両方に異なる値が入っている行が無いことを確認する。1 件でもあれば
-- SIGNAL SQLSTATE で停止し、運用者に手動整理を促す。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_merge_manufacturer //
CREATE PROCEDURE sp_v130_stage20_merge_manufacturer()
BEGIN
  DECLARE col_exists INT DEFAULT 0;
  DECLARE conflict_count INT DEFAULT 0;
  -- safe update mode の現在値を保存。PROCEDURE 終了時／例外時に必ず元の値に戻す。
  DECLARE saved_safe_updates INT DEFAULT @@SESSION.SQL_SAFE_UPDATES;

  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
    RESIGNAL;
  END;

  SET SESSION SQL_SAFE_UPDATES = 0;

  -- 列存在確認（manufacturer 列があるときだけ移送・撤去処理を走らせる）
  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'manufacturer';

  IF col_exists > 0 THEN
    -- 衝突検出：manufacturer / label 両方が非 NULL かつ値が異なる行をカウント
    SELECT COUNT(*) INTO conflict_count
      FROM products
     WHERE manufacturer IS NOT NULL
       AND label        IS NOT NULL
       AND manufacturer <> label;

    IF conflict_count > 0 THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'manufacturer と label の値が異なる行があります。事前に手で整理してから再実行してください。';
    END IF;

    -- 衝突なし：label が NULL の行に manufacturer の値を移送
    UPDATE products
       SET label = manufacturer
     WHERE label IS NULL
       AND manufacturer IS NOT NULL;

    -- 旧 manufacturer 列を撤去
    ALTER TABLE products DROP COLUMN manufacturer;
  END IF;

  SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
END //

DELIMITER ;

-- 衝突行を運用者の目に触れる形でレポートしてから移送・DROP を実行する。
-- products.manufacturer 列がもう存在しないと SELECT 自体が失敗するので、列存在確認を
-- PROCEDURE 内に閉じ込めた形で表示する。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_report_manufacturer_conflicts //
CREATE PROCEDURE sp_v130_stage20_report_manufacturer_conflicts()
BEGIN
  DECLARE col_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'manufacturer';
  IF col_exists > 0 THEN
    SELECT product_catalog_no, manufacturer, label
      FROM products
     WHERE manufacturer IS NOT NULL
       AND label        IS NOT NULL
       AND manufacturer <> label;
  END IF;
END //

DELIMITER ;

CALL sp_v130_stage20_report_manufacturer_conflicts();
DROP PROCEDURE IF EXISTS sp_v130_stage20_report_manufacturer_conflicts;

CALL sp_v130_stage20_merge_manufacturer();
DROP PROCEDURE IF EXISTS sp_v130_stage20_merge_manufacturer;

-- ----------------------------------------------------------------------------
-- 4) products に社名 ID 列を追加（label / distributor 各々）
-- ----------------------------------------------------------------------------
-- ALTER TABLE のみなので safe update mode の影響は受けない。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_add_pc_id_columns //
CREATE PROCEDURE sp_v130_stage20_add_pc_id_columns()
BEGIN
  DECLARE col_exists INT DEFAULT 0;

  -- label_product_company_id 列
  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'label_product_company_id';
  IF col_exists = 0 THEN
    -- 列追加時点では label 列がまだ存在している可能性があるので、AFTER label で挿入
    -- （後段の列順整理で disc_count 直後に並べ替える）。
    ALTER TABLE products
      ADD COLUMN `label_product_company_id` int NULL,
      ADD KEY `ix_products_label_pc` (`label_product_company_id`),
      ADD CONSTRAINT `fk_products_label_pc`
        FOREIGN KEY (`label_product_company_id`)
        REFERENCES `product_companies` (`product_company_id`)
        ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;

  -- distributor_product_company_id 列
  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'distributor_product_company_id';
  IF col_exists = 0 THEN
    ALTER TABLE products
      ADD COLUMN `distributor_product_company_id` int NULL,
      ADD KEY `ix_products_distributor_pc` (`distributor_product_company_id`),
      ADD CONSTRAINT `fk_products_distributor_pc`
        FOREIGN KEY (`distributor_product_company_id`)
        REFERENCES `product_companies` (`product_company_id`)
        ON DELETE SET NULL ON UPDATE CASCADE;
  END IF;
END //

DELIMITER ;

CALL sp_v130_stage20_add_pc_id_columns();
DROP PROCEDURE IF EXISTS sp_v130_stage20_add_pc_id_columns;

-- ----------------------------------------------------------------------------
-- 5) フリーテキストの自動移行（label / distributor → product_companies へ）
-- ----------------------------------------------------------------------------
-- 既存 products の label / distributor から distinct な値を拾い、product_companies に
-- まだ無いものを INSERT する。release_date 昇順で拾うので、AUTO_INCREMENT は古い社順に
-- 並ぶ（マイグレ後の picker 表示で古参社が ID 順では先頭に来る）。
--
-- products.label / distributor 列が既に DROP 済みなら何もしない（冪等性）。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_auto_migrate_freetext //
CREATE PROCEDURE sp_v130_stage20_auto_migrate_freetext()
BEGIN
  DECLARE label_col_exists INT DEFAULT 0;
  DECLARE distr_col_exists INT DEFAULT 0;
  DECLARE saved_safe_updates INT DEFAULT @@SESSION.SQL_SAFE_UPDATES;

  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
    RESIGNAL;
  END;

  SET SESSION SQL_SAFE_UPDATES = 0;

  SELECT COUNT(*) INTO label_col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'label';

  SELECT COUNT(*) INTO distr_col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'distributor';

  IF label_col_exists > 0 THEN
    -- products.label の distinct 値 → product_companies。既に同じ name_ja があれば INSERT しない。
    -- release_date 昇順で「初出順」に拾うため、サブクエリで MIN(release_date) を取って ORDER BY する。
    INSERT INTO product_companies (name_ja, created_by, updated_by)
    SELECT t.label_trim, 'migration_v1.3.0_stage20', 'migration_v1.3.0_stage20'
      FROM (
        SELECT TRIM(p.label) AS label_trim,
               MIN(p.release_date)        AS first_release,
               MIN(p.product_catalog_no)  AS first_catno
          FROM products p
         WHERE p.label IS NOT NULL
           AND TRIM(p.label) <> ''
           AND p.is_deleted = 0
         GROUP BY TRIM(p.label)
      ) t
     WHERE NOT EXISTS (
        SELECT 1 FROM product_companies pc
         WHERE pc.name_ja = t.label_trim
              COLLATE utf8mb4_ja_0900_as_cs_ks
     )
     ORDER BY t.first_release ASC, t.first_catno ASC;
  END IF;

  IF distr_col_exists > 0 THEN
    -- products.distributor の distinct 値 → product_companies。
    INSERT INTO product_companies (name_ja, created_by, updated_by)
    SELECT t.distr_trim, 'migration_v1.3.0_stage20', 'migration_v1.3.0_stage20'
      FROM (
        SELECT TRIM(p.distributor) AS distr_trim,
               MIN(p.release_date)        AS first_release,
               MIN(p.product_catalog_no)  AS first_catno
          FROM products p
         WHERE p.distributor IS NOT NULL
           AND TRIM(p.distributor) <> ''
           AND p.is_deleted = 0
         GROUP BY TRIM(p.distributor)
      ) t
     WHERE NOT EXISTS (
        SELECT 1 FROM product_companies pc
         WHERE pc.name_ja = t.distr_trim
              COLLATE utf8mb4_ja_0900_as_cs_ks
     )
     ORDER BY t.first_release ASC, t.first_catno ASC;
  END IF;

  -- products の FK 列を埋める（フリーテキスト列がまだ存在していて、かつ FK 列が NULL の行のみ）。
  -- COLLATE は product_companies 側に揃える（JOIN/比較のため）。
  IF label_col_exists > 0 THEN
    UPDATE products p
      INNER JOIN product_companies pc
              ON pc.name_ja = TRIM(p.label) COLLATE utf8mb4_ja_0900_as_cs_ks
       SET p.label_product_company_id = pc.product_company_id
     WHERE p.label IS NOT NULL
       AND TRIM(p.label) <> ''
       AND p.label_product_company_id IS NULL;
  END IF;

  IF distr_col_exists > 0 THEN
    UPDATE products p
      INNER JOIN product_companies pc
              ON pc.name_ja = TRIM(p.distributor) COLLATE utf8mb4_ja_0900_as_cs_ks
       SET p.distributor_product_company_id = pc.product_company_id
     WHERE p.distributor IS NOT NULL
       AND TRIM(p.distributor) <> ''
       AND p.distributor_product_company_id IS NULL;
  END IF;

  SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
END //

DELIMITER ;

CALL sp_v130_stage20_auto_migrate_freetext();
DROP PROCEDURE IF EXISTS sp_v130_stage20_auto_migrate_freetext;

-- ----------------------------------------------------------------------------
-- 6) products.label / distributor フリーテキスト列を DROP（フリーテキスト全廃）
-- ----------------------------------------------------------------------------
-- ID 紐付けが完了したので、旧フリーテキスト列を撤去して完全に構造化に一本化する。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_drop_freetext_columns //
CREATE PROCEDURE sp_v130_stage20_drop_freetext_columns()
BEGIN
  DECLARE col_exists INT DEFAULT 0;

  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'label';
  IF col_exists > 0 THEN
    ALTER TABLE products DROP COLUMN `label`;
  END IF;

  SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE()
     AND TABLE_NAME   = 'products'
     AND COLUMN_NAME  = 'distributor';
  IF col_exists > 0 THEN
    ALTER TABLE products DROP COLUMN `distributor`;
  END IF;
END //

DELIMITER ;

CALL sp_v130_stage20_drop_freetext_columns();
DROP PROCEDURE IF EXISTS sp_v130_stage20_drop_freetext_columns;

-- ----------------------------------------------------------------------------
-- 7) 最頻出社に既定フラグをセット
-- ----------------------------------------------------------------------------
-- products での使用頻度 TOP1 を既定値として採用する。これにより、ユーザーが MARV を
-- 「マーベラス」にリネームしても、最頻出社（＝マーベラス）が自動的に既定として
-- ついてくる。同数の場合は product_company_id が小さい方（先に登録された方）が優先。
--
-- 既にフラグが立っている社があればその社の頻度をチェック対象とせず、何もしない
-- （ユーザーが意図的に手動指定した既定を尊重する）。
DELIMITER //

DROP PROCEDURE IF EXISTS sp_v130_stage20_set_default_flags //
CREATE PROCEDURE sp_v130_stage20_set_default_flags()
BEGIN
  DECLARE existing_label_flag INT DEFAULT 0;
  DECLARE existing_distr_flag INT DEFAULT 0;
  DECLARE target_label_pc_id INT DEFAULT NULL;
  DECLARE target_distr_pc_id INT DEFAULT NULL;
  DECLARE saved_safe_updates INT DEFAULT @@SESSION.SQL_SAFE_UPDATES;

  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
    RESIGNAL;
  END;

  SET SESSION SQL_SAFE_UPDATES = 0;

  -- 既存フラグの有無を確認
  SELECT COUNT(*) INTO existing_label_flag
    FROM product_companies WHERE is_default_label = 1;
  SELECT COUNT(*) INTO existing_distr_flag
    FROM product_companies WHERE is_default_distributor = 1;

  -- label 既定が未設定なら、label_product_company_id の使用頻度 TOP1 にフラグを立てる
  IF existing_label_flag = 0 THEN
    SELECT pc.product_company_id INTO target_label_pc_id
      FROM product_companies pc
      INNER JOIN products p ON p.label_product_company_id = pc.product_company_id
     WHERE p.is_deleted = 0
       AND pc.is_deleted = 0
     GROUP BY pc.product_company_id
     ORDER BY COUNT(*) DESC, pc.product_company_id ASC
     LIMIT 1;

    IF target_label_pc_id IS NOT NULL THEN
      UPDATE product_companies
         SET is_default_label = 1
       WHERE product_company_id = target_label_pc_id;
    END IF;
  END IF;

  -- distributor 既定が未設定なら、distributor_product_company_id の使用頻度 TOP1 に
  IF existing_distr_flag = 0 THEN
    SELECT pc.product_company_id INTO target_distr_pc_id
      FROM product_companies pc
      INNER JOIN products p ON p.distributor_product_company_id = pc.product_company_id
     WHERE p.is_deleted = 0
       AND pc.is_deleted = 0
     GROUP BY pc.product_company_id
     ORDER BY COUNT(*) DESC, pc.product_company_id ASC
     LIMIT 1;

    IF target_distr_pc_id IS NOT NULL THEN
      UPDATE product_companies
         SET is_default_distributor = 1
       WHERE product_company_id = target_distr_pc_id;
    END IF;
  END IF;

  SET SESSION SQL_SAFE_UPDATES = saved_safe_updates;
END //

DELIMITER ;

CALL sp_v130_stage20_set_default_flags();
DROP PROCEDURE IF EXISTS sp_v130_stage20_set_default_flags;

-- ----------------------------------------------------------------------------
-- 8) 列順整理：disc_count → label_pc_id → distributor_pc_id → amazon_asin
-- ----------------------------------------------------------------------------
-- 旧 label / distributor が DROP されたあと、FK 列の物理位置を意味のある順序に整える。
-- MODIFY COLUMN ... AFTER は内部的にはテーブル再構築を伴う重い操作だが、サイズが
-- それほど大きくないテーブルなので問題なし。ALTER TABLE のみで safe update mode 影響なし。
ALTER TABLE products
  MODIFY COLUMN `label_product_company_id`       int NULL AFTER `disc_count`,
  MODIFY COLUMN `distributor_product_company_id` int NULL AFTER `label_product_company_id`;

SELECT 'v1.3.0 stage20 migration completed: product_companies + flags + freetext migrated and dropped' AS status;
