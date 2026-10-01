-- =====================================================================
-- v1.15.0_add_published_slugs_last_published_at.sql
--
-- 本番に公開した URL の記録（published_entity_slugs）に、最後に公開した日時の列を足す。
--   last_published_at  TIMESTAMP NULL  その URL を最後に本番で公開したデプロイの日時
--
-- 記録はデプロイのたびに「まだ無いスラッグだけを追記」していたので、created_at（最初に公開した日時）しか無く、
-- 一度別の URL に変わってから元の URL に戻ると、いま公開している URL のほうが古い記録に見えていた。
-- SiteBuilder は本番デプロイのたびに、いまの URL の行の last_published_at をデプロイ時刻に更新し、
-- 人物の「いま公開している名義」を last_published_at が最も新しい記録から選ぶ。
-- 既存の行は created_at で埋める（次の本番デプロイで、いま公開している URL の行が更新される）。
--
-- 冪等性: 列の存在を INFORMATION_SCHEMA で確認してから ALTER する。埋め込みは NULL の行だけ。
-- =====================================================================

DROP PROCEDURE IF EXISTS _v1150_add_published_slugs_last_published_at;
DELIMITER $$
CREATE PROCEDURE _v1150_add_published_slugs_last_published_at()
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'published_entity_slugs' AND COLUMN_NAME = 'last_published_at'
  ) THEN
    ALTER TABLE `published_entity_slugs`
      ADD COLUMN `last_published_at` timestamp NULL DEFAULT NULL
        COMMENT '最後に本番で公開したデプロイの日時（デプロイのたびに更新）'
        AFTER `created_at`;
  END IF;
END$$
DELIMITER ;

CALL _v1150_add_published_slugs_last_published_at();
DROP PROCEDURE IF EXISTS _v1150_add_published_slugs_last_published_at;

UPDATE `published_entity_slugs` SET `last_published_at` = `created_at` WHERE `last_published_at` IS NULL;
