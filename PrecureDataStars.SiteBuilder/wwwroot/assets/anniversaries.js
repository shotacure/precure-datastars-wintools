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
 *
 * 各エントリは <li class="home-anniversary-card"> のカード形（白背景・薄ボーダー・カード全域 = 1 タップ遷移リンク）。
 * クレジットカード / 楽曲カード（人物詳細）と同じ「外周 <a> でカードを丸ごとクリッカブルにする」流儀。
 * ホバー時のグローバル <code>a:hover { text-decoration: underline }</code> は site.css 側で抑止する。
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

    // 統合データを種別で振り分ける。エピソード（ep）は従来ロジックへ。
    // 誕生日（cb=キャラ / pb=人物）は「閲覧日と同じ月日」のものだけを抽出し、
    // 今日の記念日でエピソードより上に表示する。映画（mv）は今日の記念日には
    // 出さずカレンダー専用（calendar.js が同じ JSON を読む）。
    var eps = [];
    var bdays = [];
    for (var bi = 0; bi < items.length; bi++) {
      var bit = items[bi];
      if (bit.k === 'ep') {
        eps.push(bit);
      } else if ((bit.k === 'cb' || bit.k === 'pb') && bit.m === today.m && bit.d === today.d) {
        bdays.push(bit);
      }
    }

    var todayItems = filterToday(eps, today);
    var weekItems = filterThisWeek(eps, today, todayItems);

    renderTodaySection(bdays, todayItems, today);
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
  function renderTodaySection(birthdays, episodes, today) {
    var ul = document.getElementById('home-anniversary-today-list');
    if (!ul) return;
    if (birthdays.length === 0 && episodes.length === 0) return;
    var html = '';
    // 誕生日（キャラクター・人物）をエピソードより上に表示する。
    for (var b = 0; b < birthdays.length; b++) {
      html += renderBirthdayRow(birthdays[b], today);
    }
    for (var i = 0; i < episodes.length; i++) {
      var it = episodes[i];
      var yearsAgo = today.y - it.y;
      html += renderRow({
        labelText: yearsAgo + '年前',
        item: it
      });
    }
    ul.innerHTML = html;
  }

  /**
   * 誕生日 1 件分の <li>（カード形）。
   *  cb（キャラクター誕生日）… カード全域 = キャラ詳細ページへの 1 タップ遷移。
   *                            プリキュアはキーカラーバッジを <span> として内部に描画。
   *  pb（人物誕生日）        … カード全域 = 人物詳細ページへの遷移。
   *                            生年 by が公開かつ判明なら年齢を併記する。
   * カード内に anchor をネストできないため、シリーズ名等の従来 <a> 表記は全て <span> 化。
   * テキストは必ず escapeHtml / escapeAttr でエスケープする。
   */
  function renderBirthdayRow(it, today) {
    if (it.k === 'pb') {
      var ageStr = '';
      if (it.by) {
        ageStr = ' <span class="series-year muted">('
          + escapeHtml(String(today.y - it.by)) + '歳)</span>';
      }
      return ''
        + '<li class="home-anniversary-card home-anniversary-card-bday">'
        + '<a class="home-anniversary-card-link" href="' + escapeAttr(it.pu) + '">'
        + '<div class="home-anniversary-card-meta">'
        + '<span class="home-anniversary-label home-anniversary-label-bday">誕生日</span>'
        + '</div>'
        + '<div class="home-anniversary-card-main">'
        + '<span class="home-anniversary-person-name">' + escapeHtml(it.pn) + '</span>'
        + ageStr
        + '</div>'
        + '</a>'
        + '</li>';
    }
    // cb（キャラクター誕生日）。シリーズ名はカード内 <span>（カード自体が <a> なので
    // シリーズ別のサブアンカーは置けない。クリック時はキャラ詳細ページへ）。
    var seriesLine = '';
    if (it.st) {
      seriesLine = '<span class="home-anniversary-series">' + escapeHtml(it.st) + '</span>';
      if (it.sy) {
        seriesLine += ' <span class="series-year muted">('
          + escapeHtml(String(it.sy)) + ')</span>';
      }
    }
    var badge;
    if (it.kc) {
      badge = '<span class="home-bday-badge" style="background:' + escapeAttr(it.kc)
        + ';color:' + escapeAttr(it.kf) + ';border-color:' + escapeAttr(it.kb) + '">'
        + escapeHtml(it.cn) + '</span>';
    } else {
      badge = '<span class="home-bday-badge home-bday-badge-plain">'
        + escapeHtml(it.cn) + '</span>';
    }
    return ''
      + '<li class="home-anniversary-card home-anniversary-card-bday">'
      + '<a class="home-anniversary-card-link" href="' + escapeAttr(it.cu) + '">'
      + '<div class="home-anniversary-card-meta">'
      + '<span class="home-anniversary-label home-anniversary-label-bday">誕生日</span>'
      + seriesLine
      + '</div>'
      + '<div class="home-anniversary-card-main">' + badge + '</div>'
      + '</a>'
      + '</li>';
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
   * 1 件分の HTML を組み立てる（カード形）。
   *
   * カード全域 = エピソード詳細ページへの 1 タップ遷移リンク。
   * 内部はネストアンカー禁止のため、従来 <a> でリンクしていたシリーズ名・
   * サブタイトルは <span> 化する（クレジットカード / 楽曲カードと同じ流儀）。
   * 構造：
   *   meta（上段）: 「N年前」ラベル + シリーズ名 + (放送年度)
   *   main（下段）: 「第N話 / 放送日 / サブタイトル」の ep-row レイアウト
   * /episodes/ ランディングや episodes-index-section と同じ .ep-row 系
   * クラスを内部で流用する（左の第N話＋日付ブロックは ep-row-no-date）。
   * JS テキストは escapeHtml / escapeAttr で必ずエスケープする。
   */
  function renderRow(opts) {
    var it = opts.item;
    // /episodes/ ランディングと同一の「2024.2.4」形式（DotDate同形、月日は 0 詰めしない）。
    var dateStr = it.y + '.' + it.m + '.' + it.d;
    // メタ行：「N年前」ラベル + シリーズ名 + (放送年度)。
    var seriesMeta = '<span class="home-anniversary-series">' + escapeHtml(it.st) + '</span>';
    if (it.sy) {
      seriesMeta += ' <span class="series-year muted">(' + escapeHtml(String(it.sy)) + ')</span>';
    }
    return ''
      + '<li class="home-anniversary-card">'
      + '<a class="home-anniversary-card-link" href="' + escapeAttr(it.eu) + '">'
      + '<div class="home-anniversary-card-meta">'
      + '<span class="home-anniversary-label">' + escapeHtml(opts.labelText) + '</span>'
      + seriesMeta
      + '</div>'
      + '<div class="home-anniversary-card-main">'
      + '<div class="ep-row">'
      + '<div class="ep-row-main">'
      + '<div class="ep-row-no-date">'
      + '<span class="ep-row-no">第' + it.en + '話</span>'
      + '<span class="ep-row-date">' + escapeHtml(dateStr) + '</span>'
      + '</div>'
      + '<span class="ep-row-title">' + escapeHtml(it.et) + '</span>'
      + '</div>'
      + '</div>'
      + '</div>'
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
