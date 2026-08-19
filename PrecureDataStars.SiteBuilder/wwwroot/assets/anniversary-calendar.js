/*
 * anniversary-calendar.js — 記念日カレンダー索引（/anniversary/）の配列合わせと「今日」強調
 *
 * 索引はビルド時に 12 か月ぶんを静的に描くが、升目の配列（1 日が何曜の列か・土日の色・
 * 平年 2/29 の扱い）と「今日」は年によって変わる。ビルド年の配列で組んでおき、閲覧年が
 * ずれていればこのスクリプトが閲覧年の配列へ組み替える。ホームの当月カレンダー
 * （calendar.js）が常に閲覧時の年月で描くのと同じ状態へ揃えるため。
 *
 * 期待する DOM 構造（anniversary-index.sbn 側で生成）：
 *   <section id="anniv-calendar" data-cal-year="2026">
 *     <div class="cal-grid anniv-cal-grid" data-month="2">
 *       <div class="cal-dow ...">日</div> ... （曜日見出し 7 個）
 *       <div class="cal-cell anniv-cell cal-sun" data-md="2-1" style="grid-column-start: 1">
 *         <a class="cal-daynum anniv-daynum" href="/anniversary/02-01/">
 *           <span class="anniv-daynum-md">2/</span>1<span class="anniv-daynum-dow">（日）</span></a>
 *         <div class="cal-chips"> ... </div>
 *       </div>
 *       ...
 *       ※ 2/28 のセル末尾に 2/29 を畳んだ <div class="cal-leap-section"> が入りうる
 *
 * 組み替えでやること：
 *   - 各月の 1 日セルの grid-column-start をその年の曜日へ
 *   - 全セルの cal-sun / cal-sat と（曜日）ラベルを引き直す
 *   - 2 月：閏年なら畳んである 2/29 を独立セルへ展開、平年なら 2/28 の末尾へ畳む
 *   - 凡例の「カレンダー配列は◯年」を書き換える
 *
 * 既定（JS 無効時）はビルド年の配列。年をまたいで再ビルドされるまでのあいだだけ
 * 配列がずれるが、日付そのものとリンク先は常に正しい。
 */
(function () {
  'use strict';

  var WEEKDAYS = ['日', '月', '火', '水', '木', '金', '土'];

  function isLeapYear(y) {
    return (y % 4 === 0 && y % 100 !== 0) || (y % 400 === 0);
  }

  /** 「2-29」形式のキーを {m, d} に割る。 */
  function parseMd(cell) {
    var parts = (cell.getAttribute('data-md') || '').split('-');
    return { m: parseInt(parts[0], 10), d: parseInt(parts[1], 10) };
  }

  /** セルへ曜日由来のクラスと（曜日）ラベルを当てる。 */
  function applyDow(cell, year) {
    var md = parseMd(cell);
    if (!md.m || !md.d) return;
    var dow = new Date(year, md.m - 1, md.d).getDay();
    cell.classList.toggle('cal-sun', dow === 0);
    cell.classList.toggle('cal-sat', dow === 6);
    var label = cell.querySelector('.anniv-daynum-dow');
    if (label) label.textContent = '（' + WEEKDAYS[dow] + '）';
  }

  /**
   * 平年で 2/28 のセルに畳んである 2/29 を、独立したセルへ展開する（閏年向け）。
   * 升目を 1 つ増やすだけなので、後続の月には影響しない（月ごとに別グリッドのため）。
   */
  function unfoldLeapDay(grid) {
    var section = grid.querySelector('.cal-leap-section');
    if (!section) return;

    var host = section.parentNode;
    var link = section.querySelector('.cal-leap-label');
    var chips = section.querySelector('.cal-chips');

    var cell = document.createElement('div');
    cell.className = 'cal-cell anniv-cell' + (chips ? '' : ' anniv-cell-empty');
    cell.setAttribute('data-md', '2-29');

    var daynum = document.createElement('a');
    daynum.className = 'cal-daynum anniv-daynum';
    daynum.setAttribute('href', link ? link.getAttribute('href') : '/anniversary/02-29/');
    daynum.innerHTML = '<span class="anniv-daynum-md">2/</span>29'
      + '<span class="anniv-daynum-dow"></span>';
    cell.appendChild(daynum);

    if (chips) {
      cell.appendChild(chips);
    } else {
      var dash = document.createElement('div');
      dash.className = 'cal-day-empty';
      dash.innerHTML = '&mdash;';
      cell.appendChild(dash);
    }

    section.parentNode.removeChild(section);
    grid.appendChild(cell);
    // 2/28 が出来事を持たないまま 2/29 だけ持っていた場合、畳みを解いた側の空表示を戻す。
    if (!host.querySelector('.cal-chips') && !host.querySelector('.cal-day-empty')) {
      var hostDash = document.createElement('div');
      hostDash.className = 'cal-day-empty';
      hostDash.innerHTML = '&mdash;';
      host.appendChild(hostDash);
      host.classList.add('anniv-cell-empty');
    }
  }

  /**
   * 閏年向けに展開されている 2/29 のセルを、2/28 のセル末尾へ畳み直す（平年向け）。
   * ビルド年が閏年で閲覧年が平年のときに通る経路。
   */
  function foldLeapDay(grid) {
    var cell = grid.querySelector('.anniv-cell[data-md="2-29"]');
    if (!cell) return;
    var host = grid.querySelector('.anniv-cell[data-md="2-28"]');
    if (!host) return;

    var chips = cell.querySelector('.cal-chips');
    var daynum = cell.querySelector('.cal-daynum');

    var section = document.createElement('div');
    section.className = 'cal-leap-section';

    var label = document.createElement('a');
    label.className = 'cal-leap-label';
    label.setAttribute('href', daynum ? daynum.getAttribute('href') : '/anniversary/02-29/');
    label.textContent = '(2/29)';
    section.appendChild(label);
    if (chips) section.appendChild(chips);

    host.appendChild(section);
    if (chips) host.classList.remove('anniv-cell-empty');
    cell.parentNode.removeChild(cell);
  }

  /** 升目の配列を指定年へ組み替える。 */
  function relayout(root, year) {
    var grids = root.querySelectorAll('.anniv-cal-grid');
    for (var i = 0; i < grids.length; i++) {
      var grid = grids[i];
      var month = parseInt(grid.getAttribute('data-month'), 10);
      if (!month) continue;

      if (month === 2) {
        if (isLeapYear(year)) unfoldLeapDay(grid); else foldLeapDay(grid);
      }

      var cells = grid.querySelectorAll('.anniv-cell');
      for (var c = 0; c < cells.length; c++) applyDow(cells[c], year);

      // 1 日セルだけが列位置を持つ。以降のセルは自然に流れる。
      if (cells.length > 0) {
        cells[0].style.gridColumnStart = String(new Date(year, month - 1, 1).getDay() + 1);
      }
    }

    var note = document.getElementById('anniv-cal-year');
    if (note) note.textContent = String(year);
  }

  /** 閲覧時の月日に一致するセルへ、ホームと同じ本日の強調を当てる。 */
  function markToday(root, now) {
    var key = (now.getMonth() + 1) + '-' + now.getDate();
    var cell = root.querySelector('.anniv-cell[data-md="' + key + '"]');
    if (!cell) return;

    cell.classList.add('cal-today');

    var daynum = cell.querySelector('.cal-daynum');
    if (!daynum) return;
    var mark = document.createElement('span');
    mark.className = 'cal-day-today-mark';
    mark.textContent = '今日';
    cell.insertBefore(mark, daynum);
  }

  function init() {
    var root = document.getElementById('anniv-calendar');
    if (!root) return;

    var now = new Date();
    var year = now.getFullYear();
    var builtYear = parseInt(root.getAttribute('data-cal-year'), 10);

    if (builtYear !== year) relayout(root, year);
    markToday(root, now);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
