/*
 * calendar.js — ホームの「今月のカレンダー」
 *
 * home.sbn が埋め込む共有 JSON（<script id="home-anniversary-data">）を読み、
 * #home-calendar-grid に描画する。表示モードはビューポート幅で 2 系統：
 *   - 通常幅（> 720px）：閲覧した瞬間の当月を 7 列カレンダーで描画し、
 *     前月／翌月ボタンで月単位に遷移する。
 *   - 狭い幅（≤ 720px）：「今日から 7 日間」を 1 列の縦並びで描画し、
 *     前へ／次へボタンで 7 日単位に遷移する。日付＋曜日＋「今日」マークを
 *     行ヘッダに出して何日のチップ群かを一目で識別できるようにする。
 * 表示データは月日ベース（記念日）のためクライアント完結で、ビューポート幅の
 * 切替（リサイズ／回転）は matchMedia で検知して両モードの基準点を「今日」に
 * リセットしたうえで再描画する。
 *
 * JSON は anniversaries.js と共有。各要素は種別タグ k を持つ：
 *   ep … エピソード放送日（ts=シリーズ略称, en=話数, eu=URL,
 *         ef=1 話フラグ任意, el=最終話フラグ任意, o=オトナシリーズの話フラグ任意,
 *         ra=サブタイトル解禁時刻任意。未解禁ならチップの title 属性からサブタイトルを外す）
 *   mv … 映画公開日（ts=シリーズ略称, su=シリーズ URL）
 *   cb … キャラクター誕生日（pn=変身前名義/表示名, cu=URL, kc/kf/kb=バッジ色）
 *   pb … 人物誕生日（pn=氏名, pu=URL, pt=段〔0=プリキュア役の声優さん, 1=メインスタッフ, 2=そのほか〕, pc=本編のクレジット回数）
 *
 * 1 日のセル／行内チップの優先順（上から）：
 *   キャラクター誕生日(cb) > 映画公開日(mv) > プリキュア役の声優さん(pb, pt=0) > メインスタッフ(pb, pt=1) > TV 放送(ep)
 *   > そのほかの人物(pb, pt=2)。人物は同じ段の中で本編のクレジット回数の多い順。
 *
 * 月グリッドでは、ふだんはキャラクター誕生日のチップだけを出し、ほか（映画公開・人物誕生日・TV 放送）は
 * 種別ごとの件数（🎥1 🎂2 📺4）にたたむ。週（グリッドの 1 行）にマウスを載せるとその週のチップが開き、
 * それより下の週は閉じる（上の週は、閉じるとマウスの下の行がずれるので開いたままにする）。
 * カレンダーからマウスが出たら、全部の週を閉じる。今日のある週も、最初はたたんでおく。
 * タッチだけの端末（タブレットなど。マウスやペンが 1 つも無い端末）では、スクロールのたびに、画面の縦のまんなかにいちばん近い週を開く。
 * PC では、タッチ対応でもマウスを載せたときだけ開く。
 * 開閉は CSS の grid-template-rows の遷移でなめらかに動かす（site.css の .cal-fold）。
 *
 * 注記：カレンダーはコンパクト表示のため、ep / mv はシリーズ略称（title_short）を
 * 用いる。これは「生成出力では title_short を参照しない」サイト方針の明示例外で、
 * 当カレンダー UI に限り適用する（README に明記）。
 */
(function () {
  'use strict';

  // 日本時間の今日の年月日。記念日は日本での放送日・公開日・誕生日なので、海外から見ても
  // 端末の時間帯ではなく日本の日付で「今日」を決める（日本に夏時間は無いので UTC+9 固定でよい）。
  function japanToday() {
    var t = new Date(Date.now() + 9 * 60 * 60 * 1000);
    return { y: t.getUTCFullYear(), m: t.getUTCMonth() + 1, d: t.getUTCDate() };
  }


  var WEEKDAYS = ['日', '月', '火', '水', '木', '金', '土'];
  // チップ優先順位（小さいほど上）。人物の誕生日はプリキュア役の声優さん（pt=0）・メインスタッフ（pt=1）が TV 放送より上、
  // そのほか（pt=2）は TV 放送の下。
  var KIND_ORDER = { cb: 0, mv: 1, ep: 4 };
  function chipOrder(it) {
    if (it.k === 'pb') return it.pt === 0 ? 2 : (it.pt === 1 ? 3 : 5);
    var o = KIND_ORDER[it.k];
    return o === undefined ? 9 : o;
  }
  // 狭い幅判定の閾値。site.css の旧カレンダーフォールバックと同じ 720px に合わせる。
  var MOBILE_QUERY = '(max-width: 720px)';

  // 共有 JSON の全要素。月日ベース（記念日）なので、表示月／表示窓の切替は
  // この配列を月日でフィルタし直すだけで完結する（追加データ取得は不要）。
  var ALL_ITEMS = null;
  // 実際の「今日」。月グリッドの本日強調と 7 日縦並びの「今日」マークに使う。
  var REAL_YEAR = 0;
  var REAL_MONTH = 0;
  var REAL_DAY = 0;
  // 月グリッド表示中の年・月（前月／翌月ボタンで遷移する）。
  var viewYear = 0;
  var viewMonth = 0;
  // 7 日縦並び表示中の開始日（行頭の日。前へ／次へで ±7 日される）。
  var rollYear = 0;
  var rollMonth = 0;
  var rollDay = 0;
  // ビューポートが狭いかを返す matchMedia。両モードの切替を駆動する。
  var mq = null;

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

    var today = japanToday();
    REAL_YEAR = today.y;
    REAL_MONTH = today.m; // 1-12
    REAL_DAY = today.d;

    // 月グリッドの初期表示は当月、7 日縦並びの初期表示は今日。
    viewYear = REAL_YEAR;
    viewMonth = REAL_MONTH;
    rollYear = REAL_YEAR;
    rollMonth = REAL_MONTH;
    rollDay = REAL_DAY;

    // 前へ／次へボタンを結線（モードに応じて月単位 or 7 日単位で送る）。
    var prevBtn = document.getElementById('home-calendar-prev');
    var nextBtn = document.getElementById('home-calendar-next');
    if (prevBtn) {
      prevBtn.addEventListener('click', function () { shift(-1); });
    }
    if (nextBtn) {
      nextBtn.addEventListener('click', function () { shift(1); });
    }

    // ビューポート幅が閾値を跨いだら両モードの基準点を「今日」に戻して再描画。
    mq = window.matchMedia(MOBILE_QUERY);
    var onModeChange = function () {
      viewYear = REAL_YEAR;
      viewMonth = REAL_MONTH;
      rollYear = REAL_YEAR;
      rollMonth = REAL_MONTH;
      rollDay = REAL_DAY;
      render();
    };
    if (mq.addEventListener) {
      mq.addEventListener('change', onModeChange);
    } else if (mq.addListener) {
      // 旧 Safari 互換。
      mq.addListener(onModeChange);
    }

    bindWeekFolding(grid);
    render();
  }

  // 月グリッドの週の開閉。グリッドの入れ物に 1 回だけ結線する（中身は描き直しのたびに入れ替わるため）。
  function bindWeekFolding(grid) {
    var hoverTimer = 0;
    function cells() { return grid.querySelectorAll('.cal-cell[data-week]'); }
    function weekOf(target) {
      var cell = target && target.closest ? target.closest('.cal-cell[data-week]') : null;
      return cell ? parseInt(cell.getAttribute('data-week'), 10) : -1;
    }
    // week の週を開き、それより下の週を閉じる。
    function openWeek(week) {
      var list = cells();
      for (var i = 0; i < list.length; i++) {
        var w = parseInt(list[i].getAttribute('data-week'), 10);
        if (w === week) list[i].classList.add('is-open');
        else if (w > week) list[i].classList.remove('is-open');
      }
    }
    // 全部の週を閉じる。
    function closeAll() {
      var list = cells();
      for (var i = 0; i < list.length; i++) list[i].classList.remove('is-open');
    }

    // マウスの動きには、端末の種類を問わず必ず反応する（タッチ対応の PC は、ブラウザが「マウスなし」と
    // 申告することがあるので、申告では分けない）。
    {
      grid.addEventListener('mouseover', function (ev) {
        var week = weekOf(ev.target);
        if (week < 0) return;
        // 行を横切るだけのときに開かないよう、少しだけ待つ。
        clearTimeout(hoverTimer);
        hoverTimer = setTimeout(function () { openWeek(week); }, 90);
      });
      grid.addEventListener('mouseleave', function () {
        clearTimeout(hoverTimer);
        closeAll();
      });
    }
    // タッチだけの端末（タブレットなど）に限って：スクロールのたびに、画面の縦のまんなかにいちばん近い週を開く。
    // マウスやペンのような細かい操作の手段が 1 つでもある端末（PC。タッチ対応の PC を含む）では、この動きを付けない。
    var touchOnly = window.matchMedia
      && window.matchMedia('(any-pointer: coarse)').matches
      && !window.matchMedia('(any-pointer: fine)').matches
      && !window.matchMedia('(any-hover: hover)').matches;
    if (touchOnly) {
      // カレンダーが画面の外にあるときは、全部の週を閉じる。
      var ticking = false;
      var onScroll = function () {
        if (ticking) return;
        ticking = true;
        window.requestAnimationFrame(function () {
          ticking = false;
          var list = cells();
          if (!list.length) return;
          var rect = grid.getBoundingClientRect();
          var mid = window.innerHeight / 2;
          if (rect.bottom < 0 || rect.top > window.innerHeight) { closeAll(); return; }
          var best = -1;
          var bestDist = Infinity;
          for (var i = 0; i < list.length; i++) {
            var r = list[i].getBoundingClientRect();
            var dist = Math.abs((r.top + r.bottom) / 2 - mid);
            if (dist < bestDist) { bestDist = dist; best = parseInt(list[i].getAttribute('data-week'), 10); }
          }
          if (best >= 0) openWeek(best);
        });
      };
      window.addEventListener('scroll', onScroll, { passive: true });
    }
  }

  function isMobile() {
    return mq ? mq.matches : false;
  }

  // 前へ／次へを 1 段送る。月グリッドでは ±1 か月、7 日縦並びでは ±7 日。
  function shift(direction) {
    if (isMobile()) {
      var dt = new Date(rollYear, rollMonth - 1, rollDay + direction * 7);
      rollYear = dt.getFullYear();
      rollMonth = dt.getMonth() + 1;
      rollDay = dt.getDate();
    } else {
      var m = viewMonth + direction;
      var y = viewYear;
      while (m < 1) { m += 12; y -= 1; }
      while (m > 12) { m -= 12; y += 1; }
      viewYear = y;
      viewMonth = m;
    }
    render();
  }

  // モードに応じて月グリッド or 7 日縦並びを描き直し、タイトルと nav の
  // aria-label もモード対応に書き換える。
  function render() {
    var grid = document.getElementById('home-calendar-grid');
    if (!grid || !ALL_ITEMS) return;

    var titleEl = document.getElementById('home-calendar-title');
    var prevBtn = document.getElementById('home-calendar-prev');
    var nextBtn = document.getElementById('home-calendar-next');

    if (isMobile()) {
      if (titleEl) titleEl.textContent = formatRollingTitle();
      if (prevBtn) prevBtn.setAttribute('aria-label', '7日前');
      if (nextBtn) nextBtn.setAttribute('aria-label', '7日後');
      grid.innerHTML = buildRolling();
    } else {
      if (titleEl) titleEl.textContent = viewYear + '年' + viewMonth + '月';
      if (prevBtn) prevBtn.setAttribute('aria-label', '前の月');
      if (nextBtn) nextBtn.setAttribute('aria-label', '次の月');
      var byDay = {};
      for (var i = 0; i < ALL_ITEMS.length; i++) {
        var it = ALL_ITEMS[i];
        if (it.m !== viewMonth) continue;
        if (!byDay[it.d]) byDay[it.d] = [];
        byDay[it.d].push(it);
      }
      // 本日強調は実際の当月を表示しているときだけ（他月では 0=該当なし）。
      var todayDay = (viewYear === REAL_YEAR && viewMonth === REAL_MONTH)
        ? REAL_DAY : 0;
      grid.innerHTML = buildGrid(viewYear, viewMonth, todayDay, byDay);
    }
  }

  // 1 セル／行内のチップ並び：種別優先（cb→mv→プリキュア役の声優さん→メインスタッフ→ep→そのほかの人物）、
  // 人物は本編のクレジット回数の多い順、ほかはシリーズ年→話数で安定的に。
  function sortChips(a, b2) {
    var oa = chipOrder(a);
    var ob = chipOrder(b2);
    if (oa !== ob) return oa - ob;
    if (a.k === 'pb' && b2.k === 'pb') return (b2.pc || 0) - (a.pc || 0);
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
      var week = Math.floor((firstDow + d - 1) / 7);
      var cls = 'cal-cell';
      if (d === todayDay) cls += ' cal-today';
      if (dow === 0) cls += ' cal-sun';
      if (dow === 6) cls += ' cal-sat';

      html += '<div class="' + cls + '" data-week="' + week + '">';
      html += '<div class="cal-daynum">' + d + '</div>';

      // キャラクター誕生日はいつも出す。ほかのチップは、週が開いているときだけ出す側（more）へ回す。
      var list = byDay[d] || [];
      list.sort(sortChips);
      var always = '';
      var more = '';
      var counts = { mv: 0, pb: 0, ep: 0, ot: 0 };
      for (var c = 0; c < list.length; c++) {
        var chip = renderChip(list[c]);
        if (list[c].k === 'cb') { always += chip; continue; }
        more += chip;
        countChip(counts, list[c]);
      }
      if (always) html += '<div class="cal-chips">' + always + '</div>';

      // 平年の 2 月で 2/29 のデータがあれば、2/28 セル末尾に「(2/29)」見出し付きで併記する。
      // 単独の 29 日セルを足すとグリッドが崩れるため、月末セル内に小ブロックとして寄せる（開いている側に入れる）。
      var leapHtml = '';
      if (month === 2 && d === 28 && daysInMonth === 28) {
        var leap = byDay[29];
        if (leap && leap.length) {
          leap.sort(sortChips);
          leapHtml += '<div class="cal-leap-section">';
          leapHtml += '<div class="cal-leap-label">(2/29)</div>';
          leapHtml += '<div class="cal-chips">';
          for (var lc = 0; lc < leap.length; lc++) {
            leapHtml += renderChip(leap[lc]);
            countChip(counts, leap[lc]);
          }
          leapHtml += '</div>';
          leapHtml += '</div>';
        }
      }

      if (more || leapHtml) {
        // たたんでいる間の件数（種別ごと）と、開いたときのチップ。どちらも .cal-fold の中に入れ、高さの遷移で入れ替える。
        html += '<div class="cal-fold cal-fold-count"><div class="cal-fold-inner"><div class="cal-counts" aria-hidden="true">' + renderCounts(counts) + '</div></div></div>';
        html += '<div class="cal-fold cal-fold-more"><div class="cal-fold-inner">'
          + (more ? '<div class="cal-chips">' + more + '</div>' : '') + leapHtml + '</div></div>';
      }
      html += '</div>';
    }
    html += '</div>';
    return html;
  }

  // たたんだチップを種別ごとに数える（映画・人物誕生日・TV 放送・オトナシリーズの放送）。
  function countChip(counts, it) {
    if (it.k === 'mv') counts.mv++;
    else if (it.k === 'pb') counts.pb++;
    else if (it.k === 'ep') { if (it.o) counts.ot++; else counts.ep++; }
  }

  // 件数の表記。種別ごとに、開いたときのチップと同じ色の小さな札に絵文字と数を入れる（0 件の種別は出さない）。
  function renderCounts(counts) {
    var html = '';
    function add(cls, emoji, n) {
      if (n > 0) html += '<span class="cal-count ' + cls + '"><span class="cal-chip-emoji">' + emoji + '</span>' + n + '</span>';
    }
    add('cal-chip-movie', '🎥', counts.mv);
    add('cal-chip-person', '🎂', counts.pb);
    add('cal-chip-ep', '📺', counts.ep);
    add('cal-chip-ep', '🌙', counts.ot);
    return html;
  }

  // 「{m}月{d}日〜{d2}日」（同月）／「{m}月{d}日〜{m2}月{d2}日」（月跨ぎ）。
  function formatRollingTitle() {
    var start = new Date(rollYear, rollMonth - 1, rollDay);
    var end = new Date(rollYear, rollMonth - 1, rollDay + 6);
    var sm = start.getMonth() + 1, sd = start.getDate();
    var em = end.getMonth() + 1, ed = end.getDate();
    if (sm === em) {
      return sm + '月' + sd + '日〜' + ed + '日';
    }
    return sm + '月' + sd + '日〜' + em + '月' + ed + '日';
  }

  // 7 日縦並び：rollYear/Month/Day から 7 日分を連続描画する。
  // 各行は「(今日マーク？) {m}/{d} ({曜})」のヘッダ + チップ列 で構成。
  function buildRolling() {
    var html = '<div class="cal-rolling">';
    for (var i = 0; i < 7; i++) {
      var dt = new Date(rollYear, rollMonth - 1, rollDay + i);
      var y = dt.getFullYear();
      var m = dt.getMonth() + 1;
      var d = dt.getDate();
      var dow = dt.getDay();

      var list = [];
      for (var k = 0; k < ALL_ITEMS.length; k++) {
        var it = ALL_ITEMS[k];
        if (it.m === m && it.d === d) list.push(it);
      }
      list.sort(sortChips);

      var isToday = (y === REAL_YEAR && m === REAL_MONTH && d === REAL_DAY);
      var cls = 'cal-day-row';
      if (isToday) cls += ' cal-day-row-today';
      if (dow === 0) cls += ' cal-sun';
      if (dow === 6) cls += ' cal-sat';

      html += '<div class="' + cls + '">';
      html += '<div class="cal-day-head">';
      if (isToday) {
        html += '<span class="cal-day-today-mark">今日</span>';
      }
      html += '<span class="cal-day-md">' + m + '/' + d + '</span>';
      html += '<span class="cal-day-dow">(' + WEEKDAYS[dow] + ')</span>';
      html += '</div>';

      // 表示窓の中に「平年の 2/28」が入ったら、その行末尾に 2/29 のチップを併記する
      // （月グリッド側の併記ロジックと同じ振る舞いを縦並び側でも維持）。
      var renderedLeap = false;
      if (m === 2 && d === 28 && !isLeapYear(y)) {
        var leap = [];
        for (var lk = 0; lk < ALL_ITEMS.length; lk++) {
          var lit = ALL_ITEMS[lk];
          if (lit.m === 2 && lit.d === 29) leap.push(lit);
        }
        if (leap.length) {
          leap.sort(sortChips);
          if (list.length) {
            html += '<div class="cal-chips">';
            for (var c1 = 0; c1 < list.length; c1++) html += renderChip(list[c1]);
            html += '</div>';
          }
          html += '<div class="cal-leap-section">';
          html += '<div class="cal-leap-label">(2/29)</div>';
          html += '<div class="cal-chips">';
          for (var lc = 0; lc < leap.length; lc++) {
            html += renderChip(leap[lc], true);
          }
          html += '</div>';
          html += '</div>';
          renderedLeap = true;
        }
      }

      if (!renderedLeap) {
        if (list.length) {
          html += '<div class="cal-chips">';
          for (var c = 0; c < list.length; c++) html += renderChip(list[c], true);
          html += '</div>';
        } else {
          html += '<div class="cal-day-empty">—</div>';
        }
      }

      html += '</div>';
    }
    html += '</div>';
    return html;
  }

  function isLeapYear(y) {
    return (y % 4 === 0 && y % 100 !== 0) || (y % 400 === 0);
  }

  // useFullName=true で 7 日縦並びモード向けに正式タイトル／キャラフル名へ切替。
  // 月グリッドはセル幅が狭いため従来通り略称（cb は pn=変身前名義、mv/ep は ts=略称）。
  // チップ自身に overflow: hidden + text-overflow: ellipsis が効いているので、
  // どちらのモードでも 1 行内に収まらない場合はエリプシス省略になる。
  function renderChip(it, useFullName) {
    if (it.k === 'cb') {
      var cbLabel = useFullName ? it.cn : it.pn;
      if (it.kc) {
        return '<a class="cal-chip cal-chip-bday" href="' + escapeAttr(it.cu)
          + '" title="' + escapeAttr(it.cn) + '" style="background:' + escapeAttr(it.kc)
          + ';color:' + escapeAttr(it.kf) + ';border-color:' + escapeAttr(it.kb) + '">'
          + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(cbLabel) + '</a>';
      }
      return '<a class="cal-chip cal-chip-bday cal-chip-plain" href="' + escapeAttr(it.cu)
        + '" title="' + escapeAttr(it.cn) + '">'
        + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(cbLabel) + '</a>';
    }
    if (it.k === 'mv') {
      // 映画は識別用にフィルム絵文字を前置する（色区分と併用）。
      var mvLabel = useFullName ? it.st : it.ts;
      return '<a class="cal-chip cal-chip-movie" href="' + escapeAttr(it.su)
        + '" title="' + escapeAttr(it.st) + '">'
        + '<span class="cal-chip-emoji">🎥</span>' + escapeHtml(mvLabel) + '</a>';
    }
    if (it.k === 'pb') {
      // pb の pn は既に氏名フル表示なので useFullName は無関係。
      return '<a class="cal-chip cal-chip-person" href="' + escapeAttr(it.pu)
        + '" title="' + escapeAttr(it.pn) + '">'
        + '<span class="cal-chip-emoji">🎂</span>' + escapeHtml(it.pn) + '</a>';
    }
    // ep（TV 放送）：テレビ絵文字 + シリーズ名 + #話数。大人向けスピンオフの話（o）も同じ配色で、絵文字だけ 🌙 にする。
    var epTitle = useFullName ? it.st : it.ts;
    var label = escapeHtml(epTitle) + '#' + escapeHtml(String(it.en));
    // 1 話（ef）／最終話（el）には強調用の追加クラスを付与する。
    var epCls = 'cal-chip cal-chip-ep';
    if (it.ef) epCls += ' cal-chip-ep-first';
    if (it.el) epCls += ' cal-chip-ep-last';
    // title 属性はネイティブのホバーツールチップで CSS でぼかせないため、
    // 未解禁（前話の予告が未放送）ならサブタイトルを含めない。
    var embargo = window.PCDS && window.PCDS.subtitleEmbargo;
    var revealed = !it.ra || (embargo && embargo.isRevealed(it.ra));
    var epSubtitle = revealed ? (it.et || '') : '';
    return '<a class="' + epCls + '" href="' + escapeAttr(it.eu)
      + '" title="' + escapeAttr(it.st + ' 第' + it.en + '話 ' + epSubtitle) + '">'
      + '<span class="cal-chip-emoji">' + (it.o ? '🌙' : '📺') + '</span>' + label + '</a>';
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
