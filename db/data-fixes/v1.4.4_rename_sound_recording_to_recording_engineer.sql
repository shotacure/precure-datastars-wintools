-- v1.4.4: 役職 SOUND_RECORDING を RECORDING_ENGINEER にリネーム + name_en を「Recording Engineer」に修正。
--
-- 背景：
--   子役職「録音助手 / ASSISTANT_RECORDING_ENGINEER / Assistant Recording Engineer」を登録した時点で、
--   親役職「録音 / SOUND_RECORDING / Sound Recording」との英語表記の意味論が破綻していた。
--   "Assistant Recording Engineer" は人物の肩書（録音技師の助手）なので、親も人物肩書の
--   "Recording Engineer"（録音技師）に揃える方が辻褄が合う。「Sound Recording」は学問領域
--   ／工程名寄りで、クレジット欄に出る人物の肩書としてはずれていた。
--
-- 影響：
--   roles.role_code (PRIMARY KEY) の値変更は v1.4.4 で全 FK が ON UPDATE CASCADE のため、
--   子テーブル 7 個（bgm_cue_credits / credit_card_roles / role_successions×2 / role_templates /
--   song_credits / song_recording_singers）に自動追従する（運用環境では credit_card_roles の
--   60 行が CASCADE で更新された）。手動の子テーブル UPDATE は不要。
--
-- 冪等：
--   既に RECORDING_ENGINEER に置換済み（または SOUND_RECORDING が存在しない）の場合、
--   WHERE 句で対象 0 行となり何も起こらない。
--
-- 適用：祥太さんの手元で root 接続して実行（DDL/DML のため SELECT 専用ユーザー claude_ro では不可）。

UPDATE roles
SET role_code = 'RECORDING_ENGINEER',
    name_en  = 'Recording Engineer'
WHERE role_code = 'SOUND_RECORDING';
