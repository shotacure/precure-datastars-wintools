-- v1.4.4: roles.role_code および参照側 7 列を VARCHAR(32) → VARCHAR(64) に拡張する。
--
-- 背景：
--   既存 32 文字制限は CHARACTER_VOICE (15) / CASTING_COOPERATION (19) / PRODUCTION_COOPERATION (22)
--   程度では足りていたが、英訳ベースで命名する長めの役職（例 IN_BETWEEN_AND_PAINT_PRODUCTION_ASSISTANT、
--   DIGITAL_SPECIAL_EFFECTS_COORDINATOR 等）が 32 文字を超えて INSERT 時に
--   "Data too long for column 'role_code'" でエラーになるケースが発生したため、上限を緩める。
--   英訳由来の "WORD_BY_WORD" コードでも 64 字あればまず収まる想定。
--
-- 影響範囲：
--   親： roles.role_code
--   子（FK で参照）:
--     - bgm_cue_credits.credit_role         (FK: fk_bgm_cue_credits_role)
--     - credit_card_roles.role_code         (FK: fk_card_role_role)
--     - role_successions.from_role_code     (FK: fk_role_successions_from)
--     - role_successions.to_role_code       (FK: fk_role_successions_to)
--     - role_templates.role_code            (FK: fk_role_templates_role)
--     - song_credits.credit_role            (FK: fk_song_credits_role)
--     - song_recording_singers.role_code    (FK: fk_srs_role)
--
-- 手順：
--   1) 参照 FK 7 個を DROP（親側の型変更には子側の参照が外れている必要があるため）
--   2) 親 roles.role_code を VARCHAR(64) に MODIFY
--   3) 子 7 列を VARCHAR(64) に MODIFY（親と完全一致の型にする）
--   4) FK 7 個を再追加（元の ON DELETE / ON UPDATE 仕様を維持）
--
-- 冪等性：
--   - 既に拡張済み（CHARACTER_MAXIMUM_LENGTH=64）であればスキップする条件分岐は MySQL の
--     ストアド経由で実装。手動再実行しても二度処理にならない。
--   - FK DROP/ADD のセクションは「存在すれば DROP / 同名で ADD」の冪等構造。
--
-- 既存データの収まり：
--   全列は VARCHAR で固定長ではないため、32→64 拡張で既存行のデータは一切変化しない。
--   インデックス・FK 制約はそのまま再構築するだけ。
--
-- 適用：祥太さんの手元で root 接続して実行（DDL のため SELECT 専用ユーザー claude_ro では不可）。

DROP PROCEDURE IF EXISTS _extend_role_code_to_64;
DELIMITER $$
CREATE PROCEDURE _extend_role_code_to_64()
BEGIN
    DECLARE cur_len INT DEFAULT NULL;

    SELECT CHARACTER_MAXIMUM_LENGTH INTO cur_len
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'roles'
      AND COLUMN_NAME = 'role_code';

    IF cur_len IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'roles.role_code が見つかりません';
    END IF;

    IF cur_len >= 64 THEN
        -- 既に 64 文字以上に拡張済み。何もしない。
        SELECT CONCAT('skip: roles.role_code は既に VARCHAR(', cur_len, ') です') AS msg;
    ELSE
        -- ── 1) FK DROP ──
        ALTER TABLE bgm_cue_credits         DROP FOREIGN KEY fk_bgm_cue_credits_role;
        ALTER TABLE credit_card_roles       DROP FOREIGN KEY fk_card_role_role;
        ALTER TABLE role_successions        DROP FOREIGN KEY fk_role_successions_from;
        ALTER TABLE role_successions        DROP FOREIGN KEY fk_role_successions_to;
        ALTER TABLE role_templates          DROP FOREIGN KEY fk_role_templates_role;
        ALTER TABLE song_credits            DROP FOREIGN KEY fk_song_credits_role;
        ALTER TABLE song_recording_singers  DROP FOREIGN KEY fk_srs_role;

        -- ── 2) 親 列 を VARCHAR(64) に MODIFY ──
        ALTER TABLE roles
            MODIFY COLUMN role_code VARCHAR(64) NOT NULL;

        -- ── 3) 子 7 列を VARCHAR(64) に MODIFY（親と同型） ──
        ALTER TABLE bgm_cue_credits
            MODIFY COLUMN credit_role VARCHAR(64) NOT NULL;
        ALTER TABLE credit_card_roles
            MODIFY COLUMN role_code VARCHAR(64) NOT NULL;
        ALTER TABLE role_successions
            MODIFY COLUMN from_role_code VARCHAR(64) NOT NULL,
            MODIFY COLUMN to_role_code   VARCHAR(64) NOT NULL;
        ALTER TABLE role_templates
            MODIFY COLUMN role_code VARCHAR(64) NOT NULL;
        ALTER TABLE song_credits
            MODIFY COLUMN credit_role VARCHAR(64) NOT NULL;
        ALTER TABLE song_recording_singers
            MODIFY COLUMN role_code VARCHAR(64) NOT NULL;

        -- ── 4) FK 再追加（元仕様：いずれも親 roles.role_code 参照、ON DELETE/UPDATE は元と同じ） ──
        ALTER TABLE bgm_cue_credits
            ADD CONSTRAINT fk_bgm_cue_credits_role
                FOREIGN KEY (credit_role) REFERENCES roles(role_code)
                ON DELETE RESTRICT ON UPDATE CASCADE;
        ALTER TABLE credit_card_roles
            ADD CONSTRAINT fk_card_role_role
                FOREIGN KEY (role_code) REFERENCES roles(role_code)
                ON DELETE RESTRICT ON UPDATE CASCADE;
        ALTER TABLE role_successions
            ADD CONSTRAINT fk_role_successions_from
                FOREIGN KEY (from_role_code) REFERENCES roles(role_code)
                ON DELETE CASCADE ON UPDATE CASCADE,
            ADD CONSTRAINT fk_role_successions_to
                FOREIGN KEY (to_role_code) REFERENCES roles(role_code)
                ON DELETE CASCADE ON UPDATE CASCADE;
        ALTER TABLE role_templates
            ADD CONSTRAINT fk_role_templates_role
                FOREIGN KEY (role_code) REFERENCES roles(role_code)
                ON DELETE CASCADE ON UPDATE CASCADE;
        ALTER TABLE song_credits
            ADD CONSTRAINT fk_song_credits_role
                FOREIGN KEY (credit_role) REFERENCES roles(role_code)
                ON DELETE RESTRICT ON UPDATE CASCADE;
        ALTER TABLE song_recording_singers
            ADD CONSTRAINT fk_srs_role
                FOREIGN KEY (role_code) REFERENCES roles(role_code)
                ON DELETE RESTRICT ON UPDATE CASCADE;

        SELECT 'done: roles.role_code および参照側 7 列を VARCHAR(64) に拡張しました' AS msg;
    END IF;
END$$
DELIMITER ;

CALL _extend_role_code_to_64();
DROP PROCEDURE _extend_role_code_to_64;
