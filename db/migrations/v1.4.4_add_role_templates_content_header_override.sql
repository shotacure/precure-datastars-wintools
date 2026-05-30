-- v1.4.4: role_templates に content_header_override 列を追加。
--
-- 背景：
--   役職テンプレ（role_templates）は (role_code, series_id) でテンプレ本体（format_template）の
--   フル上書きはできるが、「役職名の表示位置・表示テキストだけをシリーズ別に変えて、本体は
--   通常フォールバック描画のまま」というユースケースが扱えなかった。
--   例：シリーズ 3（映画 ふたりはプリキュアMax Heart）の「製作委員会」役職は、通常の左カラム
--   役職名「製作委員会」を出さず、コンテンツ領域に「映画ふたりはプリキュアM製作委員会」を
--   太字＋役職詳細リンク付きで出したい。配下の名義・所属の描画ロジックは触らない。
--
-- 仕様：
--   content_header_override が非 NULL のとき、レンダラ（SiteBuilder CreditTreeRenderer /
--   Catalog CreditPreviewRenderer）は：
--     (a) 役職ラッパ <div class="role"> の先頭にコンテンツヘッダ
--         <div class="role-content-header"><strong><a href="/stats/roles/{role_code}/">{header}</a></strong></div>
--         を出力（VOICE_CAST 役職は /creators/voice-cast/ へ）。
--     (b) 続けて通常のフォールバック描画を行うが、左カラム役職名は空表示にする
--         （見出しはコンテンツヘッダ側で出し済みのため）。
--     (c) format_template が同行に同時設定されていた場合は従来どおりテンプレ展開が優先される
--         （ヘッダ + テンプレ本体の併用も理論上可能だが、運用上は「ヘッダだけ上書き、本体は
--         フォールバック」の使い方を想定）。
--
-- 影響：
--   既存行は content_header_override = NULL になり、従来挙動と完全互換。
--   既存テンプレを 1 件も変更しないので、ビルド出力・プレビュー出力は本マイグレーション単体では
--   一切変化しない（データ投入で初めて新表示が出る）。
--
-- 冪等：
--   INFORMATION_SCHEMA.COLUMNS で列存在チェックしてから ADD COLUMN するため再実行可。

DROP PROCEDURE IF EXISTS _add_role_templates_content_header_override;
DELIMITER $$
CREATE PROCEDURE _add_role_templates_content_header_override()
BEGIN
    DECLARE col_exists INT DEFAULT 0;
    SELECT COUNT(*) INTO col_exists
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'role_templates'
      AND COLUMN_NAME = 'content_header_override';

    IF col_exists = 0 THEN
        ALTER TABLE role_templates
            ADD COLUMN content_header_override VARCHAR(256) NULL
                COMMENT 'コンテンツ領域に出すヘッダ文字列（左カラム役職名の代替）。非 NULL のときレンダラがヘッダを出力して左カラム役職名を抑止する。'
                AFTER format_template;
        SELECT 'done: role_templates に content_header_override 列を追加しました' AS msg;
    ELSE
        SELECT 'skip: content_header_override 列は既に存在します' AS msg;
    END IF;
END$$
DELIMITER ;

CALL _add_role_templates_content_header_override();
DROP PROCEDURE _add_role_templates_content_header_override;
