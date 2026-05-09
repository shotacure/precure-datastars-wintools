-- =============================================================================
-- Migration: v1.3.0 — 主題歌系 5 役職を roles マスタにシード
-- =============================================================================
-- 背景:
--   主題歌（OP/ED/挿入歌）に関わる作詞・作曲・編曲・歌・レーベルを、
--   従来は songs テーブルの lyricist_name / composer_name / arranger_name /
--   singer_name のテキスト列に直接持たせていた（マスタ駆動の人物・企業紐付けではない）。
--   
--   v1.3.0 ではこれらを「他のクレジットと同じ仕組みで集計可能」にするため、
--   5 つの役職を roles マスタに正式追加する：
--
--     LYRICS       作詞       Lyrics       NORMAL
--     COMPOSITION  作曲       Composition  NORMAL
--     ARRANGEMENT  編曲       Arrangement  NORMAL
--     VOCALS       歌         Vocals       NORMAL
--     LABEL        レーベル   Label        COMPANY_ONLY
--
--   これらの役職を OP/ED の credit_card 配下に置けば、人物・企業マスタへの紐付けが
--   できるようになり、CreditInvolvementIndex 経由でクレジット話数のランキング集計に
--   主題歌作家・歌手・レーベルが他の役職と同列で並ぶようになる。
--
--   既存運用との互換性：songs.lyricist_name 等のテキスト列は撤廃せず、フォールバック
--   として残す。新方式（クレジット階層に LYRICS/COMPOSITION/ARRANGEMENT/VOCALS が
--   ある場合）が優先、無ければ songs テーブルのテキスト列を引く設計。
--
-- 冪等性:
--   - INSERT ... ON DUPLICATE KEY UPDATE で再実行に対応
--   - 既存値（運用者が手動で変更したかもしれない name_ja / role_format_kind / notes）
--     は上書きしない。display_order が NULL の場合のみ補填する。
-- =============================================================================

/*!40101 SET NAMES utf8mb4 */;

INSERT INTO `roles`
  (`role_code`,    `name_ja`,    `name_en`,    `role_format_kind`, `display_order`, `notes`,
   `created_by`,   `updated_by`)
VALUES
  ('LYRICS',       '作詞',       'Lyrics',     'NORMAL',           900,             'v1.3.0 シード。主題歌の作詞担当。OP/ED の credit_card 配下にこの役職を置いて人物名義をエントリ追加すると、ランキング統計の対象に含まれる。',
   'v1.3.0_seed',  'v1.3.0_seed'),
  ('COMPOSITION',  '作曲',       'Composition','NORMAL',           910,             'v1.3.0 シード。主題歌の作曲担当。',
   'v1.3.0_seed',  'v1.3.0_seed'),
  ('ARRANGEMENT',  '編曲',       'Arrangement','NORMAL',           920,             'v1.3.0 シード。主題歌の編曲担当。',
   'v1.3.0_seed',  'v1.3.0_seed'),
  ('VOCALS',       '歌',         'Vocals',     'NORMAL',           930,             'v1.3.0 シード。主題歌の歌唱担当。歌唱者は人物または屋号（声優ユニット名義など）。',
   'v1.3.0_seed',  'v1.3.0_seed'),
  ('LABEL',        'レーベル',   'Label',      'COMPANY_ONLY',     940,             'v1.3.0 シード。主題歌の所属レーベル。COMPANY_ONLY の役職なので会社・屋号エントリのみが置かれる。',
   'v1.3.0_seed',  'v1.3.0_seed')
ON DUPLICATE KEY UPDATE
  -- 既存の name_ja / name_en / role_format_kind / notes は上書きしない。
  -- display_order が NULL の場合のみ補填する（運用者が個別に並びを変更している可能性を尊重）。
  display_order = COALESCE(`display_order`, VALUES(`display_order`)),
  updated_by    = VALUES(updated_by);

SELECT 'v1.3.0 migration completed: LYRICS / COMPOSITION / ARRANGEMENT / VOCALS / LABEL seeded into roles.' AS status;
