-- =====================================================================
-- v1.18.4_add_subtitle_fonts.sql
--
-- サブタイトルのテロップ画像（エピソード詳細のサブタイトル欄・OGP カードのサブタイトル）に使う
-- フォントのマスタ subtitle_fonts を新設する。免責事項ページの「使用フォントの一覧」の出どころ。
--
-- 1 行 = 1 フォント。font_name は series_subtitle_styles.font_subtitle / font_subtitle_ruby に入れている
-- Windows の書体名（「FOT-ハミング ProN B」のように重さまで含む）と同じ文字列で、ここが結び付きの鍵になる。
-- license_kind はそのフォントを使うライセンス（FONTWORKS_LETS ＝ フォントワークス LETS /
-- MORISAWA_FONTS ＝ Morisawa Fonts。写研の A-SK 書体も Morisawa Fonts）。
-- display_name は提供元の書き方の製品名（「ハミング B」など。NULL なら font_name をそのまま出す）。
-- product_url は提供元の製品ページ（一覧でリンクする先。NULL ならリンクしない）。
--
-- 行の無いフォントも SiteBuilder は一覧に出す（名前だけ・リンク無し。ライセンスは書体名の接頭辞から判定し、
-- ビルドログに警告を出す）。値は SQL で入れる（Catalog に編集画面は無い）。
--
-- 冪等性: CREATE TABLE IF NOT EXISTS。再実行しても副作用は無い。
-- =====================================================================

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `subtitle_fonts` (
  `font_name`     varchar(64)   NOT NULL COMMENT 'Windows の書体名（series_subtitle_styles の font_subtitle / font_subtitle_ruby と同じ文字列）',
  `license_kind`  varchar(16)   NOT NULL COMMENT 'ライセンス区分 FONTWORKS_LETS / MORISAWA_FONTS',
  `display_name`  varchar(64)   DEFAULT NULL COMMENT '提供元の書き方の製品名（NULL なら font_name をそのまま出す）',
  `product_url`   varchar(1024) DEFAULT NULL COMMENT '提供元の製品ページ（免責事項の使用フォント一覧でリンクする先）',
  `created_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`    timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`font_name`),
  CONSTRAINT `ck_subtitle_fonts_license` CHECK (`license_kind` IN ('FONTWORKS_LETS', 'MORISAWA_FONTS'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='サブタイトルのテロップ画像に使うフォントのマスタ';

COMMIT;
