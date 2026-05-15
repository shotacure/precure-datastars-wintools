-- v1.3.1 stage B-10：主題歌系 role_templates の役職ラベルを {ROLE_LINK:code=...,label=...} 化。
--
-- 設計動機：
--   stage B-4 で `ThemeSongsHandler.RenderSingleSongBlockHtml` の役職ラベルを `lookup.LookupRoleHtmlAsync` 経由で
--   リンク化したが、実際のクレジット階層描画では `RoleTemplateRenderer` の `{#THEME_SONGS}` ループブロック構文経由で
--   テンプレ展開しており、テンプレ DSL に「作詞:{LYRICIST}」のように **役職ラベルが生テキストとして埋め込まれている**ため、
--   コード側の改修ではラベル部分はリンク化されない。
--
--   テンプレ DSL の運用方針は「文脈ごとに表記揺れ（うた／歌、漫画／マンガ等）を管理したい」というもので、
--   `roles.name_ja` を機械的に引いてしまうと「主題歌では『うた』、声の出演では『歌』」のような使い分けが
--   できなくなる。そこで `{ROLE_LINK}` プレースホルダに `label=...` オプションを新設し、テンプレ作者が
--   表示ラベルを明示できる経路を作った（stage B-10 でコード側を改修）。
--
-- 本マイグレーション：
--   テンプレ DSL の役職ラベル「作詞」「作曲」「編曲」「うた」を {ROLE_LINK:code=...,label=...} に置換する。
--   各テンプレの元の表記（「うた」のひらがな等）を label= に保持して見た目を維持しつつ、href だけ
--   /stats/roles/{role_code}/ に飛ばせるようになる。
--
--   ・label= 指定時は {ROLE_LINK} は <strong> ラップを掛けない（テンプレ作者が完全制御）
--   ・主題歌では役職ラベルが過度に太字になると主役の楽曲名が霞むので、軽量リンクのみとなるのが意図的
--
-- 対象テンプレ（role_format_kind = 'THEME_SONG' の 5 役職分）：
--   INSERT_SONG / INSERT_SONGS_NONCREDITED / THEME_SONG_ED / THEME_SONG_OP / THEME_SONG_OP_COMBINED
--
-- 既存テンプレの区切り記号（半角コロン「:」と全角コロン「：」）はそのまま維持する：
--   ・INSERT_SONG / INSERT_SONGS_NONCREDITED / THEME_SONG_ED / THEME_SONG_OP は半角 ':'
--   ・THEME_SONG_OP_COMBINED は全角 '：'

UPDATE role_templates
   SET format_template = '{#THEME_SONGS:kind=INSERT}「{SONG_TITLE}」\r\n{ROLE_LINK:code=LYRICS,label=作詞}:{LYRICIST}\r\n{ROLE_LINK:code=COMPOSITION,label=作曲}:{COMPOSER}\r\n{ROLE_LINK:code=ARRANGEMENT,label=編曲}:{ARRANGER}\r\n{ROLE_LINK:code=VOCALS,label=うた}:{SINGER}\r\n{/THEME_SONGS}'
 WHERE role_code = 'INSERT_SONG';

UPDATE role_templates
   SET format_template = '{#THEME_SONGS:kind=INSERT}「{SONG_TITLE}」\r\n{ROLE_LINK:code=LYRICS,label=作詞}:{LYRICIST}\r\n{ROLE_LINK:code=COMPOSITION,label=作曲}:{COMPOSER}\r\n{ROLE_LINK:code=ARRANGEMENT,label=編曲}:{ARRANGER}\r\n{ROLE_LINK:code=VOCALS,label=うた}:{SINGER}\r\n{/THEME_SONGS}'
 WHERE role_code = 'INSERT_SONGS_NONCREDITED';

UPDATE role_templates
   SET format_template = '{#THEME_SONGS:kind=ED}「{SONG_TITLE}」\r\n{ROLE_LINK:code=LYRICS,label=作詞}:{LYRICIST}\r\n{ROLE_LINK:code=COMPOSITION,label=作曲}:{COMPOSER}\r\n{ROLE_LINK:code=ARRANGEMENT,label=編曲}:{ARRANGER}\r\n{ROLE_LINK:code=VOCALS,label=うた}:{SINGER}\r\n{/THEME_SONGS}{#BLOCKS:first}{COMPANIES:wrap=""}{/BLOCKS:first}'
 WHERE role_code = 'THEME_SONG_ED';

UPDATE role_templates
   SET format_template = '{#THEME_SONGS:kind=OP}「{SONG_TITLE}」\r\n{ROLE_LINK:code=LYRICS,label=作詞}:{LYRICIST}\r\n{ROLE_LINK:code=COMPOSITION,label=作曲}:{COMPOSER}\r\n{ROLE_LINK:code=ARRANGEMENT,label=編曲}:{ARRANGER}\r\n{ROLE_LINK:code=VOCALS,label=うた}:{SINGER}\r\n{/THEME_SONGS}{#BLOCKS:first}{COMPANIES:wrap=""}{/BLOCKS:first}'
 WHERE role_code = 'THEME_SONG_OP';

UPDATE role_templates
   SET format_template = '{#THEME_SONGS:kind=OP+ED}「{SONG_TITLE}」\r\n{ROLE_LINK:code=LYRICS,label=作詞}：{LYRICIST}\r\n{ROLE_LINK:code=COMPOSITION,label=作曲}：{COMPOSER}\r\n{ROLE_LINK:code=ARRANGEMENT,label=編曲}：{ARRANGER}\r\n{ROLE_LINK:code=VOCALS,label=うた}：{SINGER}\r\n\r\n{/THEME_SONGS}{#BLOCKS:first}{COMPANIES:wrap=""}{/BLOCKS:first}'
 WHERE role_code = 'THEME_SONG_OP_COMBINED';
