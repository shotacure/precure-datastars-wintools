-- =====================================================================
-- renumber_episodes.sql
--
-- エピソードの ID（episodes.episode_id）を、全作品（TV・スピンオフ）を通した放送順の 1 からの連番に振り直す。
-- 並びは 放送日時 → 作品の開始日 → series_id → 作品の中の話数。ずれている話だけを動かす（並んでいれば何も変えない）。
-- 通算話数・通算放送回（total_ep_no ほか）は専用の列が持つので、この道具は触らない。
--
-- エピソードの ID を参照する列：
--   ON UPDATE CASCADE で追従する … credits / episode_parts / episode_theme_songs / episode_uses / bd_playlists
--   外部キーが無く、ここで書き換える … bd_chapters.episode_id
-- 新しい参照列を足したら、下の「前後の照合」と「bd_chapters の書き換え」に同じように足す。
--
-- 流し方：bash db/data-fixes/_run_sql.sh db/tools/renumber_episodes.sql（流す前に自動でバックアップを取る）
-- 安全装置：振り直しの前後で、各表の行を「作品・話数・その行の主キー」で照合し、件数か照合値が 1 つでも違えば
--   ROLLBACK して止める（SIGNAL）。成功したら AUTO_INCREMENT を最大 + 1 に戻す。
-- =====================================================================

DROP PROCEDURE IF EXISTS _renumber_episodes;
DELIMITER $$
CREATE PROCEDURE _renumber_episodes()
BEGIN
  DECLARE v_moved INT DEFAULT 0;
  DECLARE v_bad INT DEFAULT 0;
  DECLARE EXIT HANDLER FOR SQLEXCEPTION
  BEGIN
    ROLLBACK;
    RESIGNAL;
  END;

  DROP TEMPORARY TABLE IF EXISTS _ep_map;
  CREATE TEMPORARY TABLE _ep_map (old_id INT NOT NULL PRIMARY KEY, new_id INT NOT NULL UNIQUE);
  DROP TEMPORARY TABLE IF EXISTS _ep_sig_before;
  CREATE TEMPORARY TABLE _ep_sig_before (tbl VARCHAR(32) NOT NULL PRIMARY KEY, n BIGINT NOT NULL, sig BIGINT NOT NULL);
  DROP TEMPORARY TABLE IF EXISTS _ep_sig_after;
  CREATE TEMPORARY TABLE _ep_sig_after (tbl VARCHAR(32) NOT NULL PRIMARY KEY, n BIGINT NOT NULL, sig BIGINT NOT NULL);

  START TRANSACTION;

  -- 放送順の新しい番号
  INSERT INTO _ep_map (old_id, new_id)
  SELECT e.episode_id,
         ROW_NUMBER() OVER (ORDER BY e.on_air_at, s.start_date, e.series_id, e.series_ep_no)
    FROM episodes e JOIN series s ON s.series_id = e.series_id;

  SELECT COUNT(*) INTO v_moved FROM _ep_map WHERE old_id <> new_id;

  IF v_moved > 0 THEN
    -- 前の照合値（ID ではなく「作品・話数・行の主キー」で数える）
    INSERT INTO _ep_sig_before
    SELECT 'credits', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.credit_id))), 0)
      FROM credits t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_parts', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.episode_seq))), 0)
      FROM episode_parts t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_theme_songs', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.is_broadcast_only, t.theme_kind, t.seq))), 0)
      FROM episode_theme_songs t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_uses', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.part_kind, t.use_order, t.sub_order))), 0)
      FROM episode_uses t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'bd_playlists', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.disc_id, t.playlist_file))), 0)
      FROM bd_playlists t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'bd_chapters', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.disc_id, t.playlist_file, t.chapter_no))), 0)
      FROM bd_chapters t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episodes', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', series_id, series_ep_no, on_air_at))), 0)
      FROM episodes;

    -- 1 段目：動かす話を負の新番号へ退避（CASCADE の 5 表は追従。bd_chapters は手で）
    UPDATE bd_chapters t JOIN _ep_map m ON m.old_id = t.episode_id
       SET t.episode_id = -m.new_id
     WHERE m.old_id <> m.new_id;
    UPDATE episodes e JOIN _ep_map m ON m.old_id = e.episode_id
       SET e.episode_id = -m.new_id
     WHERE m.old_id <> m.new_id;

    -- 2 段目：負の番号を正に戻す
    UPDATE bd_chapters SET episode_id = -episode_id WHERE episode_id < 0;
    UPDATE episodes SET episode_id = -episode_id WHERE episode_id < 0;

    -- 後の照合値
    INSERT INTO _ep_sig_after
    SELECT 'credits', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.credit_id))), 0)
      FROM credits t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_parts', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.episode_seq))), 0)
      FROM episode_parts t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_theme_songs', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.is_broadcast_only, t.theme_kind, t.seq))), 0)
      FROM episode_theme_songs t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episode_uses', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.part_kind, t.use_order, t.sub_order))), 0)
      FROM episode_uses t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'bd_playlists', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.disc_id, t.playlist_file))), 0)
      FROM bd_playlists t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'bd_chapters', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', e.series_id, e.series_ep_no, t.disc_id, t.playlist_file, t.chapter_no))), 0)
      FROM bd_chapters t JOIN episodes e ON e.episode_id = t.episode_id
    UNION ALL
    SELECT 'episodes', COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('|', series_id, series_ep_no, on_air_at))), 0)
      FROM episodes;

    -- 照合：表ごとの件数と照合値が前後で同じか、ID が 1 からの欠番なしの放送順になったか
    SELECT COUNT(*) INTO v_bad
      FROM _ep_sig_before b LEFT JOIN _ep_sig_after a ON a.tbl = b.tbl
     WHERE a.tbl IS NULL OR a.n <> b.n OR a.sig <> b.sig;
    SELECT v_bad + COUNT(*) INTO v_bad
      FROM (SELECT e.episode_id,
                   ROW_NUMBER() OVER (ORDER BY e.on_air_at, s.start_date, e.series_id, e.series_ep_no) rn
              FROM episodes e JOIN series s ON s.series_id = e.series_id) x
     WHERE x.episode_id <> x.rn;

    IF v_bad > 0 THEN
      ROLLBACK;
      SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '振り直しの前後で行が合わなかったので ROLLBACK した。何も変えていない。';
    END IF;
  END IF;

  COMMIT;
  SELECT v_moved AS moved_episodes,
         (SELECT COUNT(*) FROM episodes) AS episodes,
         (SELECT MIN(episode_id) FROM episodes) AS min_id,
         (SELECT MAX(episode_id) FROM episodes) AS max_id;
  SELECT b.tbl, b.n AS rows_before, a.n AS rows_after, b.sig = a.sig AS same
    FROM _ep_sig_before b JOIN _ep_sig_after a ON a.tbl = b.tbl;

  DROP TEMPORARY TABLE IF EXISTS _ep_map;
  DROP TEMPORARY TABLE IF EXISTS _ep_sig_before;
  DROP TEMPORARY TABLE IF EXISTS _ep_sig_after;
END$$
DELIMITER ;

CALL _renumber_episodes();
DROP PROCEDURE IF EXISTS _renumber_episodes;

-- 採番を最大 + 1 に戻す（DDL なので COMMIT の後に）
ALTER TABLE episodes AUTO_INCREMENT = 1;
