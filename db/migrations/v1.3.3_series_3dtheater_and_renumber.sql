-- =============================================================================
--  v1.3.2 → v1.3.3 差分用
--    3D シアター枠（series_kind = 'EVENT'）を新規シリーズ ID:20 として導入し、
--    既存の series_id 20〜68 を 21〜69 へ全テーブル一括で繰り上げる。
--
--  方針（外部未公開のうちに ID を整理するための一回限りの再採番）:
--    - DDL を一切使わない（ALTER/DROP は MySQL で暗黙コミットが走り、
--      単一トランザクションでのロールバック保証を壊すため）。
--    - FOREIGN_KEY_CHECKS をセッションで OFF にし、ON UPDATE CASCADE に
--      頼らず全参照列を自前で更新する。これにより
--        * ON UPDATE CASCADE を持たない自己参照 FK fk_series_parent も処理でき、
--        * cascade の二重発火を避けて書き込みを完全に制御できる。
--      FOREIGN_KEY_CHECKS はセッション変数でありロールバック安全（DDL ではない）。
--    - 二段オフセット法で主キー衝突を回避する。
--        第 1 段: 対象ブロック（>=20）を一律 +1,000,000（空きスクラッチ域へ退避）
--        第 2 段: スクラッチ域を -999,999（= 元値 +1）へ確定
--      いずれの段でも移動先 ID 群は無人なので、文中の行処理順に依存せず
--      duplicate key は発生しない。
--    - 冪等。適用済み（series_id=20 かつ slug='3dtheater' が存在）なら
--      再採番系 UPDATE はすべて 0 件マッチで素通りし、INSERT は NOT EXISTS で
--      無視される。途中失敗時はトランザクションごとロールバックされるため、
--      「全適用済み」か「未適用」のいずれかの状態しか取り得ない。
--
--  実行方法（人手によるロールバック確認を組み込むため対話実行を推奨）:
--    mysql -u YOUR_USER -p precure_datastars
--    mysql> SOURCE db/migrations/v1.3.3_series_3dtheater_and_renumber.sql;
--    -- 末尾に出力される整合性サマリ（VERDICT 行）を確認し、
--    --   ALL_OK なら:  COMMIT;
--    --   それ以外なら: ROLLBACK;
--    -- を手で実行する。本スクリプト自体は COMMIT を発行しない（誤適用防止）。
--    -- 非対話で `mysql < file` 実行した場合は EOF で接続が閉じ、未コミットの
--    -- トランザクションは暗黙ロールバックされる（フェイルクローズで安全）。
--
--  影響を受ける series_id 参照（全 13 箇所、本スクリプトで網羅）:
--    series.series_id / series.parent_series_id / episodes.series_id /
--    discs.series_id / songs.series_id / bgm_sessions.series_id /
--    bgm_cues.series_id / bgm_cue_credits.series_id / tracks.bgm_series_id /
--    episode_uses.bgm_series_id / series_precures.series_id /
--    role_templates.series_id / credits.series_id
--
--  トリガー考慮:
--    credits / tracks の BEFORE UPDATE トリガーは FOREIGN_KEY_CHECKS と独立に
--    発火するが、いずれも「NULL 組合せ」の整合性検査のみ。本処理は非 NULL の
--    series_id / bgm_series_id を非 NULL のまま増減させ、対となる
--    episode_id / m_no_detail 列には触れないため、不変条件は維持される。
-- =============================================================================

START TRANSACTION;

-- セッションの FK チェックを退避して無効化（ロールバック安全なセッション変数）。
SET @OLD_FK := @@SESSION.foreign_key_checks;
SET SESSION foreign_key_checks = 0;

-- 適用要否フラグ。未適用なら 1、適用済みなら 0。
-- 以降の再採番 UPDATE はすべて「AND @apply = 1」で守る（冪等化の要）。
SET @apply := (
  SELECT IF(EXISTS(
    SELECT 1 FROM series WHERE series_id = 20 AND slug = '3dtheater'
  ), 0, 1)
);

-- スクラッチオフセット。最大 series_id=68 に対し十分大きく衝突しない値。
SET @SCRATCH := 1000000;

-- -----------------------------------------------------------------------------
-- 第 1 段: 対象ブロック（参照値 >= 20）を一律 +@SCRATCH でスクラッチ域へ退避。
--          移動先（1000020〜1000068）は無人のため衝突なし。
-- -----------------------------------------------------------------------------
UPDATE series          SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE series          SET parent_series_id = parent_series_id + @SCRATCH WHERE @apply = 1 AND parent_series_id >= 20;
UPDATE episodes        SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE discs           SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE songs           SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE bgm_sessions    SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE bgm_cues        SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE bgm_cue_credits SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE tracks          SET bgm_series_id    = bgm_series_id    + @SCRATCH WHERE @apply = 1 AND bgm_series_id    >= 20;
UPDATE episode_uses    SET bgm_series_id    = bgm_series_id    + @SCRATCH WHERE @apply = 1 AND bgm_series_id    >= 20;
UPDATE series_precures SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE role_templates  SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;
UPDATE credits         SET series_id        = series_id        + @SCRATCH WHERE @apply = 1 AND series_id        >= 20;

-- -----------------------------------------------------------------------------
-- 第 2 段: スクラッチ域（参照値 >= @SCRATCH + 20）を -(@SCRATCH - 1) で確定。
--          実効として元の ID を +1（20→21 … 68→69）。移動先は無人で衝突なし。
-- -----------------------------------------------------------------------------
UPDATE series          SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE series          SET parent_series_id = parent_series_id - (@SCRATCH - 1) WHERE parent_series_id >= @SCRATCH + 20;
UPDATE episodes        SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE discs           SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE songs           SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE bgm_sessions    SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE bgm_cues        SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE bgm_cue_credits SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE tracks          SET bgm_series_id    = bgm_series_id    - (@SCRATCH - 1) WHERE bgm_series_id    >= @SCRATCH + 20;
UPDATE episode_uses    SET bgm_series_id    = bgm_series_id    - (@SCRATCH - 1) WHERE bgm_series_id    >= @SCRATCH + 20;
UPDATE series_precures SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE role_templates  SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;
UPDATE credits         SET series_id        = series_id        - (@SCRATCH - 1) WHERE series_id        >= @SCRATCH + 20;

-- -----------------------------------------------------------------------------
-- シリーズ種別マスタに 'EVENT' を冪等投入（出荷 schema.sql の seed が
-- 5 種のみの環境でも本マイグレーションで補えるようにする）。
-- EVENT は 3D シアター上映等の特設枠。エピソード概念を持たないため
-- クレジットはシリーズ単位で保持する（credit_attach_to='SERIES'）。
-- -----------------------------------------------------------------------------
INSERT INTO series_kinds (kind_code, name_ja, name_en, credit_attach_to)
SELECT 'EVENT', 'イベント', '3D Theater', 'SERIES'
WHERE NOT EXISTS (SELECT 1 FROM series_kinds WHERE kind_code = 'EVENT');

-- -----------------------------------------------------------------------------
-- 空いた series_id=20 に 3D シアターのシリーズ行を冪等投入。
-- title は NOT NULL。slug は CHECK ^[a-z0-9-]+$ を満たす。
-- 任意列（title_kana / title_en / episodes / run_time_seconds / end_date 等）は
-- 値が無いため NULL のまま。is_deleted=0。
-- -----------------------------------------------------------------------------
INSERT INTO series (series_id, kind_code, slug, title, start_date, is_deleted)
SELECT 20, 'EVENT', '3dtheater', 'プリキュア 3Dシアター', DATE '2011-07-31', 0
WHERE NOT EXISTS (SELECT 1 FROM series WHERE series_id = 20);

-- FK チェックを元に戻す（既存行は再検証されないため、下の明示検査で担保する）。
SET SESSION foreign_key_checks = @OLD_FK;

-- =============================================================================
--  整合性検査（COMMIT 前の必須確認）
--    各 FK 関係について孤児件数を集計し、想定どおりの再採番が行われたかを
--    1 行のサマリで提示する。VERDICT が 'ALL_OK' 以外なら必ず ROLLBACK する。
-- =============================================================================
SELECT
  -- 孤児（参照先を失った行）の総数。すべて 0 であるべき。
  (SELECT COUNT(*) FROM episodes        e LEFT JOIN series s ON e.series_id=s.series_id WHERE s.series_id IS NULL)                                              AS orphan_episodes,
  (SELECT COUNT(*) FROM series          c LEFT JOIN series p ON c.parent_series_id=p.series_id WHERE c.parent_series_id IS NOT NULL AND p.series_id IS NULL)    AS orphan_series_parent,
  (SELECT COUNT(*) FROM discs           d LEFT JOIN series s ON d.series_id=s.series_id WHERE d.series_id IS NOT NULL AND s.series_id IS NULL)                  AS orphan_discs,
  (SELECT COUNT(*) FROM songs           g LEFT JOIN series s ON g.series_id=s.series_id WHERE g.series_id IS NOT NULL AND s.series_id IS NULL)                  AS orphan_songs,
  (SELECT COUNT(*) FROM bgm_sessions    b LEFT JOIN series s ON b.series_id=s.series_id WHERE s.series_id IS NULL)                                              AS orphan_bgm_sessions,
  (SELECT COUNT(*) FROM bgm_cues        b LEFT JOIN series s ON b.series_id=s.series_id WHERE s.series_id IS NULL)                                              AS orphan_bgm_cues_series,
  (SELECT COUNT(*) FROM bgm_cues        b LEFT JOIN bgm_sessions ss ON b.series_id=ss.series_id AND b.session_no=ss.session_no WHERE ss.series_id IS NULL)      AS orphan_bgm_cues_session,
  (SELECT COUNT(*) FROM bgm_cue_credits x LEFT JOIN bgm_cues c ON x.series_id=c.series_id AND x.m_no_detail=c.m_no_detail WHERE c.series_id IS NULL)            AS orphan_bgm_cue_credits,
  (SELECT COUNT(*) FROM tracks          t LEFT JOIN bgm_cues c ON t.bgm_series_id=c.series_id AND t.bgm_m_no_detail=c.m_no_detail WHERE t.bgm_series_id IS NOT NULL AND c.series_id IS NULL) AS orphan_tracks_bgm,
  (SELECT COUNT(*) FROM episode_uses    u LEFT JOIN bgm_cues c ON u.bgm_series_id=c.series_id AND u.bgm_m_no_detail=c.m_no_detail WHERE u.bgm_series_id IS NOT NULL AND c.series_id IS NULL) AS orphan_episode_uses_bgm,
  (SELECT COUNT(*) FROM series_precures p LEFT JOIN series s ON p.series_id=s.series_id WHERE s.series_id IS NULL)                                              AS orphan_series_precures,
  (SELECT COUNT(*) FROM role_templates  r LEFT JOIN series s ON r.series_id=s.series_id WHERE r.series_id IS NOT NULL AND s.series_id IS NULL)                  AS orphan_role_templates,
  (SELECT COUNT(*) FROM credits         k LEFT JOIN series s ON k.series_id=s.series_id WHERE k.series_id IS NOT NULL AND s.series_id IS NULL)                  AS orphan_credits,
  -- スクラッチ域に取り残しが無いこと（すべて 0 であるべき）。
  (SELECT COUNT(*) FROM series          WHERE series_id        >= @SCRATCH OR parent_series_id >= @SCRATCH)                                                    AS stuck_series,
  (SELECT COUNT(*) FROM episodes        WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_episodes,
  (SELECT COUNT(*) FROM discs           WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_discs,
  (SELECT COUNT(*) FROM songs           WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_songs,
  (SELECT COUNT(*) FROM bgm_sessions    WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_bgm_sessions,
  (SELECT COUNT(*) FROM bgm_cues        WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_bgm_cues,
  (SELECT COUNT(*) FROM bgm_cue_credits WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_bgm_cue_credits,
  (SELECT COUNT(*) FROM tracks          WHERE bgm_series_id    >= @SCRATCH)                                                                                    AS stuck_tracks,
  (SELECT COUNT(*) FROM episode_uses    WHERE bgm_series_id    >= @SCRATCH)                                                                                    AS stuck_episode_uses,
  (SELECT COUNT(*) FROM series_precures WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_series_precures,
  (SELECT COUNT(*) FROM role_templates  WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_role_templates,
  (SELECT COUNT(*) FROM credits         WHERE series_id        >= @SCRATCH)                                                                                    AS stuck_credits,
  -- 3D シアター行が ID:20 / slug=3dtheater / kind=EVENT で正しく存在すること（1 であるべき）。
  (SELECT COUNT(*) FROM series WHERE series_id=20 AND slug='3dtheater' AND kind_code='EVENT' AND start_date=DATE '2011-07-31') AS new_3dtheater_row,
  -- series_kinds に EVENT が在ること（1 であるべき）。
  (SELECT COUNT(*) FROM series_kinds WHERE kind_code='EVENT')                                                                  AS event_kind_present;

-- 上記の全孤児・全 stuck が 0、new_3dtheater_row=1、event_kind_present=1 なら ALL_OK。
SELECT
  CASE WHEN
       (SELECT COUNT(*) FROM episodes e LEFT JOIN series s ON e.series_id=s.series_id WHERE s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM series c LEFT JOIN series p ON c.parent_series_id=p.series_id WHERE c.parent_series_id IS NOT NULL AND p.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM discs d LEFT JOIN series s ON d.series_id=s.series_id WHERE d.series_id IS NOT NULL AND s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM songs g LEFT JOIN series s ON g.series_id=s.series_id WHERE g.series_id IS NOT NULL AND s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM bgm_sessions b LEFT JOIN series s ON b.series_id=s.series_id WHERE s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM bgm_cues b LEFT JOIN series s ON b.series_id=s.series_id WHERE s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM bgm_cues b LEFT JOIN bgm_sessions ss ON b.series_id=ss.series_id AND b.session_no=ss.session_no WHERE ss.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM bgm_cue_credits x LEFT JOIN bgm_cues c ON x.series_id=c.series_id AND x.m_no_detail=c.m_no_detail WHERE c.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM tracks t LEFT JOIN bgm_cues c ON t.bgm_series_id=c.series_id AND t.bgm_m_no_detail=c.m_no_detail WHERE t.bgm_series_id IS NOT NULL AND c.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM episode_uses u LEFT JOIN bgm_cues c ON u.bgm_series_id=c.series_id AND u.bgm_m_no_detail=c.m_no_detail WHERE u.bgm_series_id IS NOT NULL AND c.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM series_precures p LEFT JOIN series s ON p.series_id=s.series_id WHERE s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM role_templates r LEFT JOIN series s ON r.series_id=s.series_id WHERE r.series_id IS NOT NULL AND s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM credits k LEFT JOIN series s ON k.series_id=s.series_id WHERE k.series_id IS NOT NULL AND s.series_id IS NULL) = 0
   AND (SELECT COUNT(*) FROM series WHERE series_id >= @SCRATCH OR parent_series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM episodes WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM discs WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM songs WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM bgm_sessions WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM bgm_cues WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM bgm_cue_credits WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM tracks WHERE bgm_series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM episode_uses WHERE bgm_series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM series_precures WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM role_templates WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM credits WHERE series_id >= @SCRATCH) = 0
   AND (SELECT COUNT(*) FROM series WHERE series_id=20 AND slug='3dtheater' AND kind_code='EVENT') = 1
   AND (SELECT COUNT(*) FROM series_kinds WHERE kind_code='EVENT') = 1
  THEN 'ALL_OK — 問題なし。COMMIT; を実行してください。'
  ELSE 'NG — 不整合あり。ROLLBACK; を実行し、上の詳細サマリを確認してください。'
  END AS VERDICT;

-- =============================================================================
--  ここで自動コミットしない。上の VERDICT を確認のうえ手動で:
--     COMMIT;     -- ALL_OK のとき
--     ROLLBACK;   -- それ以外のとき
--
--  COMMIT 後（トランザクション外・DDL のため別途実行）:
--     ALTER TABLE series AUTO_INCREMENT = 70;
--  ※ 移行後の最大 series_id は 69。次の自動採番が 70 になるよう是正する。
--    これを行わないと次回 INSERT が 69 を取りに行き衝突する可能性がある。
-- =============================================================================
