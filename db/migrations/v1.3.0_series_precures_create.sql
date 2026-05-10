-- ============================================================================
-- v1.3.0 公開直前のデザイン整理（series_precures テーブル新設）
--
-- シリーズとプリキュアの多対多関連テーブルを追加する。
-- プリキュアという作品の性質上、1 プリキュアが複数シリーズに渡ってレギュラー出演する
-- ケースがあるため（クロスオーバー映画でレギュラー扱いになる場合や、続編シリーズで
-- 引き続き登場する場合など）、純粋な関連テーブルで多対多を表現する。
--
-- precures テーブルに series_id 列を追加する案は採用しない：
--   - 1 プリキュア = 1 シリーズに紐付かない
--   - 変身前の姿で出てきて変身しない出演もあり得るが、その場合でも当該シリーズに
--     「プリキュアとして属している」事実は変わらないので、紐付け関係としては存在する
--
-- 紐付けの運用初期は SQL 手動 INSERT を想定。Catalog 側に UI が用意され次第、UI 経由で
-- 編集できるようになる予定。
--
-- 本マイグレーションは **冪等**：すでに作成済み環境で再実行しても問題なくスキップされる
-- （CREATE TABLE IF NOT EXISTS の挙動）。
-- ============================================================================

CREATE TABLE IF NOT EXISTS `series_precures` (
  -- シリーズ参照（FK: series.series_id）。
  `series_id`     int                NOT NULL,
  -- プリキュア参照（FK: precures.precure_id）。
  -- プリキュア = 変身前後 4 名義 + 誕生日 + CV 等を 1 行で保持するマスタ。
  `precure_id`    int                NOT NULL,
  -- 同シリーズ内のプリキュア並び順（0 始まり、デフォルト 0、昇順で表示する）。
  -- 同値時は precure_id 昇順でタイブレーク。複数プリキュアが居る作品で「主役 → サブ」の
  -- 表示順を明示的に制御するために使う。
  `display_order` tinyint unsigned   NOT NULL DEFAULT 0,
  -- 標準メタ。
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `created_by`    varchar(64)        DEFAULT NULL,
  `updated_by`    varchar(64)        DEFAULT NULL,
  -- 複合 PK で「同シリーズに同一プリキュアの重複登録」を不可にする。
  PRIMARY KEY (`series_id`, `precure_id`),
  -- プリキュア → シリーズ群の逆引き用インデックス。
  -- 「キュアブラックが登場するシリーズを全て探す」「複数シリーズに登場するプリキュアを
  -- 抽出する」等の用途で SiteBuilder / Catalog から引かれる。
  KEY `ix_series_precures_precure` (`precure_id`),
  -- FK 制約：両側 ON DELETE CASCADE（親レコード削除に伴い当該関連も自動削除）。
  -- シリーズ / プリキュアの論理削除（is_deleted=1）には連動しない設計：
  -- 論理削除フラグの取り扱いは呼び出し側（Repository / SiteBuilder）の責務とする。
  CONSTRAINT `fk_series_precures_series`
    FOREIGN KEY (`series_id`)  REFERENCES `series`   (`series_id`)
    ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_series_precures_precure`
    FOREIGN KEY (`precure_id`) REFERENCES `precures` (`precure_id`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
