/*
 * calendar.js — ホームの「今月のカレンダー」
 *
 * home.sbn が埋め込む共有 JSON（<script id="home-anniversary-data">）を読み、
 * 初期表示は「閲覧した瞬間の今月」1 か月分のカレンダーを #home-calendar-grid に描画する。
 * キャプションの前月／翌月ボタンで表示月を送り、その都度再描画する。
 * 表示データは月日ベース（記念日）のためデータ追加なしでクライアント完結し、
 * 「本日」セルの強調は実際の当月を表示しているときのみ付与する。
 *
 * JSON は anniversaries.js と共有。各要素は種別タグ k を持つ：
 *   ep … エピソード放送日（ts=シリーズ略称, en=話数, eu=URL）
 *   mv … 映画公開日（ts=シリーズ略称, su=シリーズ URL）
 *   cb … キャラクター誕生日（pn=変身前名義/表示名, cu=URL, kc/kf/kb=バッジ色）
 *   pb … 人物誕生日（pn=氏名, pu=URL）
 *
 * 1 日のセル内チップの優先順（上から）：
 *   キャラクター誕生日(cb) > 映画公開日(mv) > 人物誕生日(pb) > TV 放送(ep)
 *
 * 注記：カレンダーはコンパクト表示のため、ep / mv はシリーズ略称（title_short）を
 * 用いる。これは「生成出力では title_short を参照しない」サイト方針の明示例外で、
 * 当カレンダー UI に限り適用する（README に明記）。
 */
(function () {
  'use strict';

  var WEEKDAYS = ['\u65e5', '\u6708', '\u706b', '\u6c34', '\u6728', '\u91d1', '\u571f'];
  // チップ優先順位（小さいほど上）。
  var KIND_ORDER = { cb: 0, mv: 1, pb: 2, ep: 3 };

  // 共有 JSON の全要素。月日ベース（記念日）なので、表示月の切替は
  // この配列を月でフィルタし直すだけで完結する（追加データ取得は不要）。
  var ALL_ITEMS = null;
  // 実際の「今日」。本日セル強調は閲覧月がこの年月に一致するときだけ付ける。
  var REAL_YEAR = 0;
  var REAL_MONTH = 0;
  var REAL_DAY = 0;
  // 現在カレンダーが表示している年・月（前月／翌月ボタンで遷移する）。
  var viewYear = 0;
  var viewMonth = 0;

  function init() {
    var dataEl = document.getElementById('home-anniversary-data');
    var grid = document.getElementById('home-calendar-grid');
    var sec = document.getElementById('home-calendar');
    if (!dataEl || !grid) return;

    var items;
    try {
      items = JSON.parse(dataEl.textContent || '[]');
    } catch (e) {
      if (sec) sec.style.display = 'none';
      return;
    }
    if (!Array.isArray(items) || items.length === 0) {
      if (sec) sec.style.display = 'none';
      return;
    }

    ALL_ITEMS = items;

    var now = new Date();
    REAL_YEAR = now.getFullYear();
    REAL_MONTH = now.getMonth() + 1; // 1-12
    REAL_DAY = now.getDate();

    // 初期表示は閲覧した瞬間の当月。
    viewYear = REAL_YEAR;
    viewMonth = REAL_MONTH;

    // 前月／翌月ボタンを結線（年跨ぎは shiftMonth 側で処理）。
    var prevBtn = document.getElementById('home-calendar-prev');
    var nextBtn = document.getElementById('home-calendar-next');
    if (prevBtn) {
      prevBtn.addEventListener('click', function () { shiftMonth(-1); });
    }
    if (nextBtn) {
      nextBtn.addEventListener('click', function () { shiftMonth(1); });
    }

    render();
  }

  // 表示月を delta（-1=前月, +1=翌月）だけ送る。1 月→前年 12 月、
  // 12 月→翌年 1 月のように年を繰り上げ／繰り下げる。
  function shiftMonth(delta) {
    var m = viewMonth + delta;
    var y = viewYear;
    while (m < 1) { m += 12; y -= 1; }
    while (m > 12) { m -= 12; y += 1; }
    viewYear = y;
    viewMonth = m;
    render();
  }

  // 現在の viewYear / viewMonth に基づきタイトルとグリッドを描き直す。
  function render() {
    var grid = document.getElementById('home-calendar-grid');
    if (!grid || !ALL_ITEMS) return;

    // 当該月の日別バケットへ。データは月日ベースなので月一致のみで抽出する。
    var byDay = {};
    for (var i = 0; i < ALL_ITEMS.length; i++) {
      var it = ALL_ITEMS[i];
      if (it.m !== viewMonth) continue;
      if (!byDay[it.d]) byDay[it.d] = [];
      byDay[it.d].push(it);
    }

    var titleEl = document.getElementById('home-calendar-title');
    if (titleEl) {
      titleEl.textContent = viewYear + '\u5e74' + viewMonth + '\u6708';
    }

    // 本日強調は実際の当月を表示しているときだけ（他月では 0=該当なし）。
    var todayDay = (viewYear === REAL_YEAR && viewMonth === REAL_MONTH)
      ? REAL_DAY : 0;

    grid.innerHTML = buildGrid(viewYear, viewMonth, todayDay, byDay);
  }

  // 1 セル内のチップ並び：種別優先（cb→mv→pb→ep）、同種はシリーズ年→話数で安定的に。
  function sortChips(a, b2) {
    var oa = KIND_ORDER[a.k]; if (oa === undefined) oa = 9;
    var ob = KIND_ORDER[b2.k]; if (ob === undefined) ob = 9;
    if (oa !== ob) return oa - ob;
    var ya = a.sy || a.y || 0, yb = b2.sy || b2.y || 0;
    if (ya !== yb) return ya - yb;
    return (a.en || 0) - (b2.en || 0);
  }

  function buildGrid(year, month, todayDay, byDay) {
    var firstDow = new Date(year, month - 1, 1).getDay(); // 0=日
    var daysInMonth = new Date(year, month, 0).getDate();

    var html = '<div class="cal-grid">';
    // 曜日見出し。
    for (var w = 0; w < 7; w++) {
      var hcls = 'cal-dow' + (w === 0 ? ' cal-sun' : (w === 6 ? ' cal-sat' : ''));
      html += '<div class="' + hcls + '">' + WEEKDAYS[w] + '</div>';
    }
    // 月初までの空白セル。
    for (var b = 0; b < firstDow; b++) {
      html += '<div class="cal-cell cal-cell-empty"></div>';
    }
    // 日セル。
    for (var d = 1; d <= daysInMonth; d++) {
      var dow = new Date(year, month - 1, d).getDay();
      var cls = 'cal-cell';
      if (d === todayDay) cls += ' cal-today';
      if (dow === 0) cls += ' cal-sun';
      if (dow === 6) cls += ' cal-sat';

      html += '<div class="' + cls + '">';
      html += '<div class="cal-daynum">' + d + '</div>';

      var list = byDay[d];
      if (list && list.length) {
        list.sort(sortChips);
        html += '<div class="cal-chips">';
        for (var c = 0; c < list.length; c++) {
          html += renderChip(list[c]);
        }
        html += '</div>';
      }

      // 平年の 2 月で 2/29 のデータがあれば、2/28 セル末尾に「(2/29)」見出し付きで併記する。
      // 単独の 29 日セルを足すとグリッドが崩れるため、月末セル内に小ブロックとして寄せる。
      if (month === 2 && d === 28 && daysInMonth === 28) {
        var leap = byDay[29];
        if (leap && leap.length) {
          leap.sort(sortChips);
          html += '<div class="cal-leap-section">';
          html += '<div class="cal-leap-label">(2/29)</div>';
          html += '<div class="cal-chips">';
          for (var lc = 0; lc < leap.length; lc++) {
            html += renderChip(leap[lc]);
          }
          html += '</div>';
          html += '</div>';
        }
      }
      html += '</div>';
    }
    html += '</div>';
    return html;
  }

  function renderChip(it) {
    if (it.k === 'cb') {
      if (it.kc) {
        return '<a class="cal-chip cal-chip-bday" href="' + escapeAttr(it.cu)
          + '" title="' + escapeAttr(it.cn) + '" style="background:' + escapeAttr(it.kc)
          + ';color:' + escapeAttr(it.kf) + ';border-color:' + escapeAttr(it.kb) + '">'
          + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(it.pn) + '</a>';
      }
      return '<a class="cal-chip cal-chip-bday cal-chip-plain" href="' + escapeAttr(it.cu)
        + '" title="' + escapeAttr(it.cn) + '">'
        + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(it.pn) + '</a>';
    }
    if (it.k === 'mv') {
      // 映画は識別用にフィルム絵文字を前置する（色区分と併用）。
      return '<a class="cal-chip cal-chip-movie" href="' + escapeAttr(it.su)
        + '" title="' + escapeAttr(it.st) + '">'
        + '<span class="cal-chip-emoji">🎥</span>' + escapeHtml(it.ts) + '</a>';
    }
    if (it.k === 'pb') {
      return '<a class="cal-chip cal-chip-person" href="' + escapeAttr(it.pu)
        + '" title="' + escapeAttr(it.pn) + '">'
        + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(it.pn) + '</a>';
    }
    // ep（TV 放送）：テレビ絵文字 + シリーズ略称 + #話数。
    var label = escapeHtml(it.ts) + '#' + escapeHtml(String(it.en));
    // 1 話（ef）／最終話（el）には強調用の追加クラスを付与する。
    var epCls = 'cal-chip cal-chip-ep';
    if (it.ef) epCls += ' cal-chip-ep-first';
    if (it.el) epCls += ' cal-chip-ep-last';
    return '<a class="' + epCls + '" href="' + escapeAttr(it.eu)
      + '" title="' + escapeAttr(it.st + ' 第' + it.en + '\u8a71 ' + (it.et || '')) + '">'
      + '<span class="cal-chip-emoji">📺</span>' + label + '</a>';
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