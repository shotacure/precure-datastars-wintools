/**
 * anniversaries.js — ホームの「今日の記念日」「今週の記念日」セクションをクライアント側で動的描画する。
 *
 * ビルド時生成では「ビルドした日」が「今日」になってしまうため、ビルド頻度が低いと
 * 記念日表示がズレる。そこで全エピソードの放送日 (year, month, day) を JSON として埋め込み、
 * ブラウザで「今日」を判定して該当エピソードを抽出する方式に切り替えた。
 *
 * 期待する DOM 構造（home.sbn 側で生成）：
 *   <section id="home-anniversary-today">
 *     <ul id="home-anniversary-today-list"></ul>
 *   </section>
 *   <section id="home-anniversary-week">
 *     <ul id="home-anniversary-week-list"></ul>
 *   </section>
 *   <script id="home-anniversary-data" type="application/json">[ ... ]</script>
 *
 * JSON の各要素：
 *   { y: year, m: month, d: day,
 *     st: series title, ss: series slug,
 *     en: episode no, et: episode title text, eu: episode url }
 */

(function () {
  'use strict';

  // ── 設定 ──
  // 今週の記念日：今日を含まない過去 6 日間（昨日 / 2日前 / ... / 6日前）。
  // ※今日分は「今日の記念日」セクションに行くので、ここでは除外する。
  var WEEK_LOOKBACK_DAYS = 6;

  // 表示打ち切り件数（多すぎる場合の防衛）。
  var SECTION_MAX_ITEMS = 30;

  /**
   * メインエントリ。DOMContentLoaded 後に呼ばれる。
   */
  function init() {
    var dataEl = document.getElementById('home-anniversary-data');
    if (!dataEl) return;

    var items;
    try {
      items = JSON.parse(dataEl.textContent || '[]');
    } catch (e) {
      // JSON 不正時は何もしない（セクションは空のまま、CSS で非表示）。
      return;
    }
    if (!Array.isArray(items) || items.length === 0) {
      hideEmptySections();
      return;
    }

    var now = new Date();
    var today = { y: now.getFullYear(), m: now.getMonth() + 1, d: now.getDate() };

    var todayItems = filterToday(items, today);
    var weekItems = filterThisWeek(items, today, todayItems);

    renderTodaySection(todayItems, today);
    renderWeekSection(weekItems, today);

    // どちらのセクションも空なら丸ごと非表示にする。
    hideEmptySections();
  }

  /**
   * 今日と同じ月日 / かつ放送年が今年より過去のエピソードを抽出。
   * 並び順：放送年降順（新しい年が先頭）。
   */
  function filterToday(items, today) {
    var matched = [];
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      if (it.m === today.m && it.d === today.d && it.y < today.y) {
        matched.push(it);
      }
    }
    matched.sort(function (a, b) { return b.y - a.y; });
    if (matched.length > SECTION_MAX_ITEMS) matched.length = SECTION_MAX_ITEMS;
    return matched;
  }

  /**
   * 今日を含まない直近 WEEK_LOOKBACK_DAYS 日間に該当する月日のエピソードを抽出。
   * 並び順：「何日前か」昇順 → 同日内では放送年降順。
   * todayItems で既に拾われたエピソードは除外する。
   */
  function filterThisWeek(items, today, todayItems) {
    // 「何日前 → ラベル」のマップを作る。
    // 1日前 = 昨日、2..6 日前 = N 日前、というラベル。
    var monthDayMap = {};
    var todayDateUtc = Date.UTC(today.y, today.m - 1, today.d);
    for (var back = 1; back <= WEEK_LOOKBACK_DAYS; back++) {
      var d = new Date(todayDateUtc - back * 24 * 60 * 60 * 1000);
      var key = (d.getUTCMonth() + 1) + '-' + d.getUTCDate();
      var label = back === 1 ? '昨日' : (back + '日前');
      monthDayMap[key] = { back: back, label: label };
    }

    // 既に「今日」に拾われたエピソード ID を除外集合へ（en + ss で識別）。
    var excluded = {};
    for (var j = 0; j < todayItems.length; j++) {
      excluded[todayItems[j].ss + '#' + todayItems[j].en] = true;
    }

    var matched = [];
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var k = it.m + '-' + it.d;
      var info = monthDayMap[k];
      if (!info) continue;
      if (excluded[it.ss + '#' + it.en]) continue;
      // 放送年は今年より過去のものを対象（同月日でも今年のものは記念日でない）。
      if (it.y >= today.y) continue;
      matched.push({ item: it, daysAgo: info.back, label: info.label });
    }
    matched.sort(function (a, b) {
      if (a.daysAgo !== b.daysAgo) return a.daysAgo - b.daysAgo;
      return b.item.y - a.item.y;
    });
    if (matched.length > SECTION_MAX_ITEMS) matched.length = SECTION_MAX_ITEMS;
    return matched;
  }

  /**
   * 「今日の記念日」セクションを描画。
   */
  function renderTodaySection(items, today) {
    var ul = document.getElementById('home-anniversary-today-list');
    if (!ul) return;
    if (items.length === 0) return;
    var html = '';
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var yearsAgo = today.y - it.y;
      html += renderRow({
        labelText: yearsAgo + '年前',
        item: it
      });
    }
    ul.innerHTML = html;
  }

  /**
   * 「今週の記念日」セクションを描画。各行に「N日前・M年前」のラベルが付く。
   */
  function renderWeekSection(weekItems, today) {
    var ul = document.getElementById('home-anniversary-week-list');
    if (!ul) return;
    if (weekItems.length === 0) return;
    var html = '';
    for (var i = 0; i < weekItems.length; i++) {
      var w = weekItems[i];
      var yearsAgo = today.y - w.item.y;
      html += renderRow({
        labelText: w.label + '・' + yearsAgo + '年前',
        item: w.item
      });
    }
    ul.innerHTML = html;
  }

  /**
   * 1 行分の HTML を組み立て。home-anniversary-list の <li> 構造に合わせる。
   * テキストは escapeHtml で必ず HTML エスケープする。
   */
  function renderRow(opts) {
    var it = opts.item;
    var dateStr = it.y + '年' + it.m + '月' + it.d + '日';
    return ''
      + '<li>'
      + '<span class="home-anniversary-label">' + escapeHtml(opts.labelText) + '</span>'
      + '<span class="home-anniversary-date muted">' + escapeHtml(dateStr) + '</span>'
      + '<a class="home-anniversary-series" href="/series/' + encodeURIComponent(it.ss) + '/">'
      + escapeHtml(it.st)
      + '</a>'
      + '<a class="home-anniversary-episode" href="' + escapeAttr(it.eu) + '">'
      + '第' + it.en + '話　' + escapeHtml(it.et)
      + '</a>'
      + '</li>';
  }

  /**
   * 「今日の記念日」「今週の記念日」セクションのうち、ul が空のものは section ごと非表示にする。
   * 空セクションをそのまま見せても情報量がないので隠す方がきれい。
   */
  function hideEmptySections() {
    var pairs = [
      { sec: 'home-anniversary-today', list: 'home-anniversary-today-list' },
      { sec: 'home-anniversary-week',  list: 'home-anniversary-week-list' }
    ];
    for (var i = 0; i < pairs.length; i++) {
      var sec = document.getElementById(pairs[i].sec);
      var lst = document.getElementById(pairs[i].list);
      if (!sec || !lst) continue;
      if (lst.children.length === 0) {
        sec.style.display = 'none';
      }
    }
  }

  function escapeHtml(s) {
    if (s === null || s === undefined) return '';
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function escapeAttr(s) {
    return escapeHtml(s);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
