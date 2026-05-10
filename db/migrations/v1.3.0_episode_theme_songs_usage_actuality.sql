
-- =============================================================================
-- Migration: v1.3.0 — episode_theme_songs に usage_actuality 列を追加
-- =============================================================================
-- 背景:
--   現実のエピソードでは「クレジットされていないが実際には流れた」「クレジットされて
--   いるが実際には流れていない」という乖離が稀に発生する。
--   この使用実態を表現するため、enum 1 列で 3 値を持たせる。
--
-- 値の意味:
--   NORMAL                   ... クレジット通り、実際に流れた（既定）
--   BROADCAST_NOT_CREDITED   ... クレジットされていないが確かに流れた
--                                  → クレジットページには表示しない
--                                  → エピソードの主題歌・挿入歌セクションには表示する
--   CREDITED_NOT_BROADCAST   ... クレジットされているが実際には流れていない
--                                  → クレジットページには「実際には不使用」の注記付きで表示
--                                  → エピソードの主題歌・挿入歌セクションには表示しない
--
-- is_broadcast_only との関係:
--   is_broadcast_only は「TV 放送版限定の主題歌」を表す既存フラグ（BD 版で差し替えあり等）。
--   今回追加する usage_actuality は「クレジットと実際使用の乖離」を表す別軸の概念。
--   並列で保持し、両者が組み合わせ可能。
--
-- PK への影響:
--   既存 PK は (episode_id, is_broadcast_only, theme_kind, seq) の 4 列複合。
--   usage_actuality は属性扱いとし、PK には含めない。
--   同じ seq で「クレジット通り」と「クレジット欠」の 2 行が並立する状況は想定しない
--   （seq は劇中順なので、流れた順番が重要）。
--
-- 冪等性:
--   - INFORMATION_SCHEMA.COLUMNS で列の存在を確認してから ALTER TABLE
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

SET @has_col = (
  SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME   = 'episode_theme_songs'
    AND COLUMN_NAME  = 'usage_actuality'
);

-- 列が無ければ追加。seq の直後（劇中順情報の隣）に置く。
-- 既存行は全て NORMAL（既定値）で埋まる。
SET @stmt = IF(@has_col = 0,
  'ALTER TABLE `episode_theme_songs`
     ADD COLUMN `usage_actuality` ENUM(''NORMAL'',''BROADCAST_NOT_CREDITED'',''CREDITED_NOT_BROADCAST'') NOT NULL DEFAULT ''NORMAL'' AFTER `seq`',
  'SELECT ''episode_theme_songs.usage_actuality already exists, skipping ALTER'' AS msg');
PREPARE s FROM @stmt; EXECUTE s; DEALLOCATE PREPARE s;

SELECT 'v1.3.0 migration completed: episode_theme_songs.usage_actuality added' AS final_status;
