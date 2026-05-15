-- ============================================================================
-- v1.3.1 migration : series_relation_kinds に逆向き表示名カラムを追加
-- ============================================================================
-- 背景：
--   series_relation_kinds はシリーズ親子関係の種別マスタで、parent_series_id 側
--   （子→親）の向きでの表示名 (name_ja / name_en) のみを保持していた。
--   サイト側で「親シリーズページから子作品を一覧表示する」シーンでは、子→親の
--   表示名そのまま使うと文意がおかしくなる（例：『映画 MH』が『無印 MH』の "映画"
--   関係子 だとして、無印 MH 側ページでも子作品リストにバッジ「映画」と出すのは
--   正しい一方、「続編」の場合は親側からは「前作」として参照したい等、対称的でない）。
--
--   そこで「逆向き（親→子方向）の表示ラベル」を name_ja_reverse / name_en_reverse
--   として保持する。サイト生成側で、関係を表示する向きに応じて出し分ける。
--
-- 実行手順：
--   1. ALTER TABLE で 2 列追加（既存行は空文字で初期化）。
--   2. UPDATE 文で 4 行ぶんマスタ値を流し込む。
--   3. 確認：SELECT * FROM series_relation_kinds; で 4 行とも逆向き値が入っていること。
--
-- ロールバック：
--   ALTER TABLE series_relation_kinds DROP COLUMN name_ja_reverse;
--   ALTER TABLE series_relation_kinds DROP COLUMN name_en_reverse;
--   （ロールバックすると逆向き表示は素朴な name_ja を流用する旧挙動に戻る）
-- ============================================================================

-- ---- ステップ 1：列追加 ----------------------------------------------------
-- name_ja の直後に name_ja_reverse、name_en の直後に name_en_reverse を置く。
-- 既存データは default '' で初期化（NULL 許容にすると Site 側で null チェックが
-- 増えるため、空文字運用とする。空文字なら従来通り name_ja を流用するフォールバック
-- ロジックを Site 側で持たせる方針）。
ALTER TABLE `series_relation_kinds`
    ADD COLUMN `name_ja_reverse` VARCHAR(64) NOT NULL DEFAULT '' AFTER `name_ja`,
    ADD COLUMN `name_en_reverse` VARCHAR(64) DEFAULT NULL AFTER `name_en`;

-- ---- ステップ 2：マスタ値を流し込み ----------------------------------------
-- COFEATURE は対称関係（同時上映どうし）なので逆向きも同じ「併映」。
UPDATE `series_relation_kinds`
SET `name_ja_reverse` = '併映',
    `name_en_reverse` = 'Co-feature'
WHERE `relation_code` = 'COFEATURE';

-- MOVIE：子→親（映画→TV シリーズ）方向は「映画」、逆向きは「TVシリーズ」。
UPDATE `series_relation_kinds`
SET `name_ja_reverse` = 'TVシリーズ',
    `name_en_reverse` = 'Original TV series of'
WHERE `relation_code` = 'MOVIE';

-- SEGMENT：子→親（パート→セット）方向は「パート作品」、逆向きは「セット作品」。
-- name_ja も従来「パート」だったが、表示時に「パート作品」と書き分けたいので
-- 同時に更新する（運用上 name_ja の表記を整える意味合い）。
UPDATE `series_relation_kinds`
SET `name_ja`         = 'パート作品',
    `name_ja_reverse` = 'セット作品',
    `name_en_reverse` = 'Program of'
WHERE `relation_code` = 'SEGMENT';

-- SEQUEL：続編⇔前作の関係。
UPDATE `series_relation_kinds`
SET `name_ja_reverse` = '前作',
    `name_en_reverse` = 'Prequel to'
WHERE `relation_code` = 'SEQUEL';

-- ---- ステップ 3：確認用 SELECT（手動で実行）---------------------------------
-- SELECT relation_code, name_ja, name_ja_reverse, name_en, name_en_reverse
-- FROM series_relation_kinds
-- ORDER BY relation_code;
