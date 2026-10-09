-- ============================================================================
-- クレジットの役職の表記の一覧（READ ONLY、SELECT のみ）
-- ============================================================================
-- 画面の役職の表記（credit_card_roles.role_label_text）・役職名の誤記（role_misprint_text）が入っている役職と、
-- 1 行にまとめて出る役職（join_previous = 1）の区切りを、作品・話数つきで一覧にする。
-- 役職の表記揺れを点検・整理するときの台帳として使う。異常検出ではないので件数は 0 でなくてよい。
--
-- 副作用: 一切無し（全 SELECT）。
-- ============================================================================

-- 1: 画面の表記が役職名と違う役職（役職ごと・表記ごとの件数）
SELECT r.role_code,
       r.name_ja           AS role_name,
       ccr.role_label_text AS printed_label,
       COUNT(*)            AS uses
FROM credit_card_roles ccr
JOIN roles r ON r.role_code = ccr.role_code
WHERE ccr.role_label_text IS NOT NULL
GROUP BY r.role_code, r.name_ja, ccr.role_label_text
ORDER BY r.display_order, ccr.role_label_text;

-- 2: 画面の表記が役職名と違う役職（使っている作品・話数・クレジットの種類）
SELECT s.title             AS series_title,
       e.series_ep_no      AS episode_no,
       c.credit_kind,
       r.name_ja           AS role_name,
       ccr.role_label_text AS printed_label,
       ccr.card_role_id
FROM credit_card_roles ccr
JOIN roles r               ON r.role_code       = ccr.role_code
JOIN credit_card_groups g  ON g.card_group_id   = ccr.card_group_id
JOIN credit_card_tiers t   ON t.card_tier_id    = g.card_tier_id
JOIN credit_cards cc       ON cc.card_id        = t.card_id
JOIN credits c             ON c.credit_id       = cc.credit_id
LEFT JOIN episodes e       ON e.episode_id      = c.episode_id
JOIN series s              ON s.series_id       = COALESCE(c.series_id, e.series_id)
WHERE ccr.role_label_text IS NOT NULL
ORDER BY s.start_date, e.series_ep_no, c.credit_kind, cc.card_seq, t.tier_no, g.group_no, ccr.order_in_group;

-- 3: 画面に出た役職名の誤記（作品・話数つき）
SELECT s.title             AS series_title,
       e.series_ep_no      AS episode_no,
       c.credit_kind,
       COALESCE(ccr.role_label_text, r.name_ja) AS correct_label,
       ccr.role_misprint_text AS printed_misprint,
       ccr.card_role_id
FROM credit_card_roles ccr
JOIN roles r               ON r.role_code       = ccr.role_code
JOIN credit_card_groups g  ON g.card_group_id   = ccr.card_group_id
JOIN credit_card_tiers t   ON t.card_tier_id    = g.card_tier_id
JOIN credit_cards cc       ON cc.card_id        = t.card_id
JOIN credits c             ON c.credit_id       = cc.credit_id
LEFT JOIN episodes e       ON e.episode_id      = c.episode_id
JOIN series s              ON s.series_id       = COALESCE(c.series_id, e.series_id)
WHERE ccr.role_misprint_text IS NOT NULL
ORDER BY s.start_date, e.series_ep_no, c.credit_kind, cc.card_seq, t.tier_no, g.group_no, ccr.order_in_group;

-- 4: 1 行にまとめて出る役職（後続の役職ごとに、直前の役職と区切り）
SELECT s.title                                  AS series_title,
       e.series_ep_no                           AS episode_no,
       c.credit_kind,
       COALESCE(prev.role_label_text, rp.name_ja) AS previous_label,
       ccr.join_separator                       AS separator_text,
       COALESCE(ccr.role_label_text, r.name_ja) AS label,
       ccr.card_role_id
FROM credit_card_roles ccr
JOIN roles r               ON r.role_code       = ccr.role_code
JOIN credit_card_roles prev ON prev.card_group_id = ccr.card_group_id AND prev.order_in_group = ccr.order_in_group - 1
LEFT JOIN roles rp         ON rp.role_code      = prev.role_code
JOIN credit_card_groups g  ON g.card_group_id   = ccr.card_group_id
JOIN credit_card_tiers t   ON t.card_tier_id    = g.card_tier_id
JOIN credit_cards cc       ON cc.card_id        = t.card_id
JOIN credits c             ON c.credit_id       = cc.credit_id
LEFT JOIN episodes e       ON e.episode_id      = c.episode_id
JOIN series s              ON s.series_id       = COALESCE(c.series_id, e.series_id)
WHERE ccr.join_previous = 1
ORDER BY s.start_date, e.series_ep_no, c.credit_kind, cc.card_seq, t.tier_no, g.group_no, ccr.order_in_group;
