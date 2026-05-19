-- =============================================================================
-- v1.3.5 差分マイグレーション（Stage 1）：persons / characters に誕生日カラムを追加し、
--                                       既存 precures の誕生日をキャラへバックフィルする
-- =============================================================================
--
-- 背景:
--   ホームに「今月のカレンダー」「今日の記念日（誕生日対応）」を導入するにあたり、
--   誕生日を人物（persons）とキャラクター（characters）の両マスタで持てるようにする。
--   生年は「不明」「判明・公開」「判明・非公開（本人スタンス尊重）」の 3 状態を
--   表現する必要があるため、DATE 1 本ではなく月日正規化カラム + 生年 + 公開可否で持つ。
--
-- 追加カラム（persons / characters 共通）:
--   birth_year             smallint unsigned NULL   判明していれば西暦。不明は NULL
--   birth_year_visibility  varchar(16) NOT NULL      'PUBLIC'（生成に出す）/'PRIVATE'（出さない）
--                                                    既定 'PUBLIC'
--   birth_month            tinyint unsigned NULL     1-12
--   birth_day              tinyint unsigned NULL     1-31（月が無いと持てない）
--
-- 制約:
--   ck_*_birth_year_visibility  : visibility は PUBLIC / PRIVATE のみ
--   ck_*_birth_month            : NULL もしくは 1-12
--   ck_*_birth_day              : NULL もしくは 1-31
--   ck_*_birth_day_needs_month  : 日があるなら月必須
--
-- バックフィル:
--   既存の precures.birth_month / birth_day を、対応するキャラクター
--   （transform_alias_id → character_aliases.character_id）の characters へ移送する。
--   characters 側が未設定（NULL）の行に対してのみ適用し、手動設定値・既存値は
--   上書きしない（非破壊）。precures は元々生年を持たないため birth_year は NULL のまま、
--   birth_year_visibility は既定の 'PUBLIC' のままで情報欠落はない。
--
-- スコープ（重要）:
--   本マイグレーションは「純粋追加 + バックフィル」のみ。precures.birth_month /
--   birth_day の物理削除は、アプリ側がそれらを参照しなくなる後続バージョン
--   （Stage 2）の別マイグレーションで行う。これにより各段階で稼働中アプリと
--   DB の互換性が保たれる。
--
-- 適用対象:
--   v1.3.5（precure_key_color 適用済み）の precure_datastars データベース。
--
-- 冪等性:
--   列・制約の追加は INFORMATION_SCHEMA で存在を確認してから動的 SQL で実行
--   （存在時は DO 0 で素通り）。バックフィル UPDATE は characters 側 NULL 条件付きで
--   再実行しても既設定値を書き換えない。
--
-- 適用方法:
--   mysql -u YOUR_USER -p precure_datastars < db/migrations/v1.3.5_migration_persons_characters_birthday.sql
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;
/*!50503 SET character_set_client = utf8mb4 */;

-- ---------------------------------------------------------------------------
-- 共通ヘルパ的パターン:
--   列追加 → INFORMATION_SCHEMA.COLUMNS を見て未存在時のみ ALTER ... ADD COLUMN
--   制約追加 → INFORMATION_SCHEMA.TABLE_CONSTRAINTS を見て未存在時のみ ALTER ... ADD CONSTRAINT
-- ---------------------------------------------------------------------------

-- ===== persons：カラム追加 =====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'birth_year');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD COLUMN `birth_year` smallint unsigned DEFAULT NULL AFTER `name_en`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'birth_year_visibility');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD COLUMN `birth_year_visibility` varchar(16) NOT NULL DEFAULT ''PUBLIC'' AFTER `birth_year`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'birth_month');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD COLUMN `birth_month` tinyint unsigned DEFAULT NULL AFTER `birth_year_visibility`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'persons' AND COLUMN_NAME = 'birth_day');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD COLUMN `birth_day` tinyint unsigned DEFAULT NULL AFTER `birth_month`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- ===== persons：CHECK 制約追加 =====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
    AND CONSTRAINT_NAME = 'ck_persons_birth_year_visibility' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD CONSTRAINT `ck_persons_birth_year_visibility` CHECK (`birth_year_visibility` IN (''PUBLIC'',''PRIVATE''))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
    AND CONSTRAINT_NAME = 'ck_persons_birth_month' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD CONSTRAINT `ck_persons_birth_month` CHECK (`birth_month` IS NULL OR (`birth_month` BETWEEN 1 AND 12))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
    AND CONSTRAINT_NAME = 'ck_persons_birth_day' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD CONSTRAINT `ck_persons_birth_day` CHECK (`birth_day` IS NULL OR (`birth_day` BETWEEN 1 AND 31))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'persons'
    AND CONSTRAINT_NAME = 'ck_persons_birth_day_needs_month' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `persons` ADD CONSTRAINT `ck_persons_birth_day_needs_month` CHECK (`birth_day` IS NULL OR `birth_month` IS NOT NULL)',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- ===== characters：カラム追加 =====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'birth_year');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD COLUMN `birth_year` smallint unsigned DEFAULT NULL AFTER `character_kind`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'birth_year_visibility');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD COLUMN `birth_year_visibility` varchar(16) NOT NULL DEFAULT ''PUBLIC'' AFTER `birth_year`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'birth_month');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD COLUMN `birth_month` tinyint unsigned DEFAULT NULL AFTER `birth_year_visibility`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'characters' AND COLUMN_NAME = 'birth_day');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD COLUMN `birth_day` tinyint unsigned DEFAULT NULL AFTER `birth_month`',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- ===== characters：CHECK 制約追加 =====
SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND CONSTRAINT_NAME = 'ck_characters_birth_year_visibility' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD CONSTRAINT `ck_characters_birth_year_visibility` CHECK (`birth_year_visibility` IN (''PUBLIC'',''PRIVATE''))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND CONSTRAINT_NAME = 'ck_characters_birth_month' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD CONSTRAINT `ck_characters_birth_month` CHECK (`birth_month` IS NULL OR (`birth_month` BETWEEN 1 AND 12))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND CONSTRAINT_NAME = 'ck_characters_birth_day' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD CONSTRAINT `ck_characters_birth_day` CHECK (`birth_day` IS NULL OR (`birth_day` BETWEEN 1 AND 31))',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'characters'
    AND CONSTRAINT_NAME = 'ck_characters_birth_day_needs_month' AND CONSTRAINT_TYPE = 'CHECK');
SET @s := IF(@c = 0,
  'ALTER TABLE `characters` ADD CONSTRAINT `ck_characters_birth_day_needs_month` CHECK (`birth_day` IS NULL OR `birth_month` IS NOT NULL)',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- ---------------------------------------------------------------------------
-- precures.birth_month / birth_day → characters へバックフィル（非破壊）
--   precures の 4 alias は同一 character_id を指す（DB トリガで保証）ため、
--   transform_alias_id 経由で対象キャラを特定する。複数 precure が同一キャラに
--   紐付き値が割れている稀ケースは MIN を採って決定的にする。
--   characters 側が NULL の月日にのみ適用（既存値・手動設定を保持）。
--   precures が誕生日カラムを持たない DB（後続バージョン適用済み等）では
--   この UPDATE は 0 件で安全に素通りする。
-- ---------------------------------------------------------------------------
SET @has_precure_birth := (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'precures' AND COLUMN_NAME = 'birth_month');
SET @s := IF(@has_precure_birth > 0,
  'UPDATE `characters` c
     JOIN (
       SELECT ca.character_id,
              MIN(p.birth_month) AS bm,
              MIN(p.birth_day)   AS bd
       FROM `precures` p
       JOIN `character_aliases` ca ON ca.alias_id = p.transform_alias_id
       WHERE p.is_deleted = 0
         AND (p.birth_month IS NOT NULL OR p.birth_day IS NOT NULL)
       GROUP BY ca.character_id
     ) src ON src.character_id = c.character_id
   SET c.birth_month = COALESCE(c.birth_month, src.bm),
       c.birth_day   = COALESCE(c.birth_day,  src.bd)
   WHERE c.birth_month IS NULL AND c.birth_day IS NULL',
  'DO 0');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
