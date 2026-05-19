/*
 * calendar.js — ホームの「今月のカレンダー」
 *
 * home.sbn が埋め込む共有 JSON（<script id="home-anniversary-data">）を読み、
 * 「閲覧した瞬間の今月」1 か月分のカレンダーを #home-calendar-grid に描画する。
 * ナビゲーション（前月／翌月）は持たず、常に当月のみを表示する。
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

    var now = new Date();
    var year = now.getFullYear();
    var month = now.getMonth() + 1; // 1-12
    var todayDay = now.getDate();

    // 当月分のみ：月一致で日別バケットへ。
    var byDay = {};
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      if (it.m !== month) continue;
      if (!byDay[it.d]) byDay[it.d] = [];
      byDay[it.d].push(it);
    }

    var titleEl = document.getElementById('home-calendar-title');
    if (titleEl) {
      titleEl.textContent = year + '\u5e74' + month + '\u6708';
    }

    grid.innerHTML = buildGrid(year, month, todayDay, byDay);
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
        list.sort(function (a, b2) {
          var oa = KIND_ORDER[a.k]; if (oa === undefined) oa = 9;
          var ob = KIND_ORDER[b2.k]; if (ob === undefined) ob = 9;
          if (oa !== ob) return oa - ob;
          // 同種内はシリーズ年→話数で安定的に。
          var ya = a.sy || a.y || 0, yb = b2.sy || b2.y || 0;
          if (ya !== yb) return ya - yb;
          return (a.en || 0) - (b2.en || 0);
        });
        html += '<div class="cal-chips">';
        for (var c = 0; c < list.length; c++) {
          html += renderChip(list[c]);
        }
        html += '</div>';
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
          + escapeHtml(it.pn) + '</a>';
      }
      return '<a class="cal-chip cal-chip-bday cal-chip-plain" href="' + escapeAttr(it.cu)
        + '" title="' + escapeAttr(it.cn) + '">' + escapeHtml(it.pn) + '</a>';
    }
    if (it.k === 'mv') {
      // 映画は識別用に小さな「映」マークを前置（CSS でピル装飾）。
      return '<a class="cal-chip cal-chip-movie" href="' + escapeAttr(it.su)
        + '" title="' + escapeAttr(it.st) + '">'
        + '<span class="cal-chip-tag">\u6620</span>' + escapeHtml(it.ts) + '</a>';
    }
    if (it.k === 'pb') {
      return '<a class="cal-chip cal-chip-person" href="' + escapeAttr(it.pu)
        + '" title="' + escapeAttr(it.pn) + '">' + escapeHtml(it.pn) + '</a>';
    }
    // ep（TV 放送）：シリーズ略称 + #話数。
    var label = escapeHtml(it.ts) + '#' + escapeHtml(String(it.en));
    return '<a class="cal-chip cal-chip-ep" href="' + escapeAttr(it.eu)
      + '" title="' + escapeAttr(it.st + ' 第' + it.en + '\u8a71 ' + (it.et || '')) + '">'
      + label + '</a>';
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
