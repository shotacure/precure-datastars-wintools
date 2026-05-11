-- ============================================================================================
-- v1.3.1 stage 21: 役職テンプレ DSL に {ROLE_LINK:code=...} プレースホルダ導入
--                  SERIALIZED_IN テンプレ内の「漫画」役職ラベルをリンク化対応へ更新
-- ============================================================================================
--
-- 背景:
--   stage 19 で SERIALIZED_IN テンプレ内に <strong>漫画</strong> という「役職名のハードコード」が
--   残ってしまっていた（プレーンテキストの太字）。サイト側では他のクレジット要素（人物名義／
--   屋号／ロゴ／役職名カラム）がすべて詳細ページへリンク化されているのに対し、ここだけがリンク化
--   されておらず、ユーザーが「漫画」役職の集計ページに辿り着けない問題が残っていた。
--
--   v1.3.1 stage 21 で、テンプレ DSL に新プレースホルダ {ROLE_LINK:code=ROLE_CODE} を追加し、
--   役職コードから役職統計ページ /stats/roles/{role_code}/ へのリンク化済み HTML（太字付き）を
--   出力できるようにした。本マイグレーションは SERIALIZED_IN テンプレの <strong>漫画</strong>
--   部分をその新プレースホルダに置換する。
--
-- DSL 拡張仕様:
--   {ROLE_LINK:code=ROLE_CODE}
--     - SiteBuilder 側: <strong><a href="/stats/roles/{ROLE_CODE}/">表示名</a></strong>
--     - Catalog 側プレビュー: <strong>表示名（HTML エスケープ済み）</strong>
--   いずれも <strong> ラップはレンダラ側で一律付与するため、テンプレ作者は <strong> を書かない。
--   「役職リンクなら必ず太字、違えば太字ではない」という見た目ルールを DSL の責務として保証する。
--
-- 実行前提:
--   - stage 19 のマイグレーション（v1.3.0_stage19_manga_role_split.sql）が適用済みであること
--   - SERIALIZED_IN テンプレが {ROLE:MANGA.PERSONS} 構文を含む形になっていること
--   - MANGA 役職が roles マスタに登録済みであること（stage 19 で登録されるはず）
--
-- 冪等性:
--   format_template に既に {ROLE_LINK:code=MANGA} が含まれている場合は UPDATE 対象から除外する
--   （WHERE NOT LIKE '%{ROLE_LINK:code=MANGA}%'）。再実行しても安全だが、初回適用後は何も変わらない。
--
-- ロールバック:
--   トランザクション内で完結する。途中で失敗すれば全部巻き戻る。手動ロールバックする場合は
--   format_template を stage 19 の値（<strong>漫画</strong>・{ROLE:MANGA.PERSONS} を含む形）に
--   戻せばよい。
--
-- ============================================================================================

START TRANSACTION;

-- ──────────────────────────────────────────────────────────────────────────────────────────
-- SERIALIZED_IN テンプレを {ROLE_LINK:code=MANGA} 経由版に更新
-- ──────────────────────────────────────────────────────────────────────────────────────────
--
-- 旧テンプレ（stage 19 適用後）:
--   {#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
--   <strong>漫画</strong>・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
--   　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
--
-- 新テンプレ（stage 21 適用後）:
--   {#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」
--   {ROLE_LINK:code=MANGA}・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}
--   　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか
--
-- 変更点:
--   - 中央 1 行の <strong>漫画</strong> 部分を {ROLE_LINK:code=MANGA} に置換
--   - <strong> ラップはレンダラ側で自動付与されるためテンプレからは外す
--
-- 期待される HTML 出力（SiteBuilder 側）:
--   <a href="/companies/6/">講談社</a>「<a href="/companies/6/">なかよし</a>」
--   <strong><a href="/stats/roles/MANGA/">漫画</a></strong>・<a href="/persons/5/">上北 ふたご</a>
--   　「<a href="/companies/6/">たのしい幼稚園</a>」
--   　「<a href="/companies/6/">おともだち</a>」ほか
--
-- Catalog 側プレビュー（リンクなし版）:
--   講談社「なかよし」
--   <strong>漫画</strong>・上北 ふたご
--   　「たのしい幼稚園」
--   　「おともだち」ほか
--   ※ Catalog 側プレビューでは ILookupCache.LookupRoleHtmlAsync が「表示名のみ（HTML エスケープ済み）」
--     を返し、レンダラ側で <strong> ラップが付与されるため、現行の見た目（漫画だけ太字）と一致する。

UPDATE role_templates
SET format_template = '{#BLOCKS:first}{LEADING_COMPANY}「{COMPANIES:wrap=""}」\r\n{ROLE_LINK:code=MANGA}・{ROLE:MANGA.PERSONS}{/BLOCKS:first}{#BLOCKS:rest}\r\n　「{COMPANIES:wrap=""}」{/BLOCKS:rest}ほか',
    updated_at = NOW(),
    updated_by = 'migration_v1.3.1_s21'
WHERE role_code = 'SERIALIZED_IN'
  AND series_id IS NULL
  AND format_template NOT LIKE '%{ROLE\\_LINK:code=MANGA}%' ESCAPE '\\'; -- 既に更新済みなら何もしない（冪等）

-- ──────────────────────────────────────────────────────────────────────────────────────────
-- 完了
-- ──────────────────────────────────────────────────────────────────────────────────────────

COMMIT;

-- 確認用クエリ（手動実行）:
-- SELECT template_id, role_code, series_id, format_template, updated_by
-- FROM role_templates
-- WHERE role_code = 'SERIALIZED_IN'
-- ORDER BY IFNULL(series_id, 0);
