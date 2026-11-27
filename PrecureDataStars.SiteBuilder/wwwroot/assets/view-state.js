/*
 * view-state.js — タブ・絞り込み・スイッチの状態を URL の # に残す。
 *
 * リロードしたとき・URL を共有したときに、同じタブ・同じ絞り込みで開けるようにする。
 * 各ページのタブ切り替えなどの処理（テンプレのインライン JS・role-timeline.js）には手を入れず、
 * 開いたときは # を読んで該当するボタンを押し（click / change を起こす）、操作のたびに今の状態を # に書く。
 *
 * # の形（初期状態の項目は書かない。全部が初期状態なら # を付けない）：
 *   tab=<data-tab>                 タブ（.tab-btn）
 *   filter=<data-entity-filter-set> 個人のみ・団体のみなどの絞り込み（.entity-filter-btn）
 *   sort=<data-sort>               並べ替え・表示の切り替え（.sort-toggle-btn。見えている切り替えのもの）
 *   works=tv,movie                 TV・映画・オトナの出し分けで入っている種類（.wk-toggle。全部入っていれば書かない）
 *   main=all                       年表の「主な方のみ」を切った（.rtl-filter-input。「プリキュアを演じた方のみ」を入れている間は書かない）
 *   precure=1                      歴代プリキュア声優の「プリキュアを演じた方のみ」を入れた（.precure-only-input）
 *   csort=<data-count-sort-state>  件数バッジでの並べ替え（.count-sort-btn。見えているボタンのもの。例：movie-desc）
 *   hide=movie,spring              /series/ の年度順で OFF にした区分（input[data-kind-toggle]）
 *   例：#tab=timeline&filter=person&works=tv,movie&main=all
 *
 * # が「=」を含まない（セクションナビ・ページ内リンクの #セクションid）ときは、その要素を含むタブを開いてそこへ移る。
 * 書き換えは history.replaceState で行い、ブラウザの履歴は増やさない。
 * 件数バッジでの並べ替え（count-sort.js）は、開いたときに見えているボタンを状態が合うまで押して戻す
 * （count-sort.js の準備が済んでから行うため、ほかの項目を戻した後に回す）。
 *
 * タブが 1 つ 1 ページの一覧（タブの帯がリンク a.tab-btn[data-tab-page]。スタッフ一覧など）：
 *   - タブを押して移るとき、今の # の状態（絞り込みなど）と、タブの帯の画面の中の高さを sessionStorage に預け、
 *     移った先で # に戻し、帯が同じ高さに来るようにスクロールを合わせる（その場で切り替わったように見せる）。
 *   - 前の URL のタブの指定（#tab=debut）や、別のタブにあるセクションへのリンク（#debut-3。リンクの data-tab-anchors が id の頭）で
 *     開かれたら、そのタブのページへ移す。
 *   - 先読み：ページの speculationrules（対応するブラウザは、リンクに乗せたときに先に描いておく）。対応しないブラウザでは、
 *     リンクに乗せた・触れたときに <link rel="prefetch"> を足す。先に描かれたページは、表示されるまで状態の復元を待つ。
 */
(function () {
  'use strict';

  function qsa(sel, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(sel)); }

  // 隠れたタブの中にあるか（見えている切り替えだけを状態として扱う）。
  function isInHiddenPanel(el) {
    for (var p = el.parentElement; p; p = p.parentElement) {
      if (p.hasAttribute('hidden')) return true;
    }
    return false;
  }

  function activeKey(buttons, attr) {
    for (var i = 0; i < buttons.length; i++) {
      if (buttons[i].classList.contains('is-active')) return buttons[i].getAttribute(attr);
    }
    return null;
  }

  var CARRY_KEY = 'tabPageCarry';

  // タブのページどうしの受け渡し。移る前に預け、移った先で受け取る（20 秒より古いものは使わない）。
  function takeCarry() {
    try {
      var v = sessionStorage.getItem(CARRY_KEY);
      if (!v) return null;
      sessionStorage.removeItem(CARRY_KEY);
      var c = JSON.parse(v);
      return c && Date.now() - c.t < 20000 ? c : null;
    } catch (e) { return null; }
  }

  // # の状態から tab= を除いたもの（タブはページの URL が持つ）。
  function hashWithoutTab(raw) {
    return raw.split('&').filter(function (kv) { return kv && kv.indexOf('tab=') !== 0; }).join('&');
  }

  // タブの帯がリンクのページの準備。別のタブのページへ移したら true（その先の処理は要らない）。
  function initTabPages() {
    var links = qsa('a.tab-btn[data-tab-page]');
    if (!links.length) return false;
    var raw = (location.hash || '').replace(/^#/, '');

    // 前の URL のタブの指定・別のタブにあるセクションへのリンクは、そのタブのページへ移す。
    if (raw.indexOf('=') >= 0) {
      var m = /(?:^|&)tab=([^&]*)/.exec(raw);
      if (m) {
        var rest = hashWithoutTab(raw);
        for (var i = 0; i < links.length; i++) {
          if (links[i].getAttribute('data-tab-page') === m[1] && !links[i].classList.contains('is-active')) {
            location.replace(links[i].getAttribute('href') + (rest ? '#' + rest : ''));
            return true;
          }
        }
        try { history.replaceState(history.state, '', location.pathname + location.search + (rest ? '#' + rest : '')); } catch (e) { /* 残さない */ }
      }
    } else if (raw) {
      var id = raw;
      try { id = decodeURIComponent(raw); } catch (e) { /* そのまま */ }
      var el = document.getElementById(id);
      if (!el) {
        for (var j = 0; j < links.length; j++) {
          var prefixes = (links[j].getAttribute('data-tab-anchors') || '').split(/\s+/).filter(Boolean);
          if (!links[j].classList.contains('is-active') && prefixes.some(function (p) { return id.indexOf(p) === 0; })) {
            location.replace(links[j].getAttribute('href') + '#' + raw);
            return true;
          }
        }
      }
    }

    // 前のタブのページから預かった状態と帯の高さを戻す。
    var carry = takeCarry();
    var bar = links[0].parentElement;
    if (carry && !raw) {
      if (carry.hash) {
        try { history.replaceState(history.state, '', location.pathname + location.search + '#' + carry.hash); } catch (e) { /* 残さない */ }
      }
      if (typeof carry.top === 'number' && bar) {
        window.scrollTo(0, Math.max(0, window.scrollY + bar.getBoundingClientRect().top - carry.top));
      }
    }

    // 移るときに預ける（リンクの先は書き換えない。先に描かれたページと同じ URL で開くため）。
    document.addEventListener('click', function (ev) {
      var a = ev.target.closest ? ev.target.closest('a.tab-btn[data-tab-page]') : null;
      if (!a) return;
      var top = bar ? bar.getBoundingClientRect().top : 0;
      try {
        sessionStorage.setItem(CARRY_KEY, JSON.stringify({
          hash: hashWithoutTab((location.hash || '').replace(/^#/, '')),
          top: top,
          t: Date.now()
        }));
      } catch (e) { /* 預けられない環境では引き継がない */ }
    });

    // 先読み（speculationrules に対応しないブラウザ）。
    if (!(window.HTMLScriptElement && HTMLScriptElement.supports && HTMLScriptElement.supports('speculationrules'))) {
      links.forEach(function (a) {
        if (a.classList.contains('is-active')) return;
        var done = false;
        function prefetch() {
          if (done) return;
          done = true;
          var l = document.createElement('link');
          l.rel = 'prefetch';
          l.href = a.getAttribute('href');
          document.head.appendChild(l);
        }
        a.addEventListener('pointerenter', prefetch);
        a.addEventListener('touchstart', prefetch, { passive: true });
      });
    }
    return false;
  }

  function init() {
    if (initTabPages()) return;
    var tabButtons = qsa('.tab-btn[data-tab]');
    var filterButtons = qsa('.entity-filter-btn[data-entity-filter-set]');
    var workButtons = qsa('.wk-toggle[data-wk-toggle]');
    var mainInputs = qsa('.rtl-filter-input');
    var precureInputs = qsa('.precure-only-input');
    var kindInputs = qsa('input[data-kind-toggle]');
    var sortToggles = qsa('.sort-toggle');
    var countSortButtons = qsa('.count-sort-btn');
    if (!countSortButtons.length && !tabButtons.length && !filterButtons.length && !workButtons.length && !mainInputs.length && !precureInputs.length && !kindInputs.length && !sortToggles.length) return;

    // 初期状態（テンプレが is-active・checked を付けた状態）を覚えておき、それと違う項目だけを # に書く。
    var defaultTab = activeKey(tabButtons, 'data-tab');
    var defaultFilter = activeKey(filterButtons, 'data-entity-filter-set');
    // 入っている種類（.wk-toggle の is-active）を並べたもの。初期状態はすべて入っている。
    function activeWorks() {
      return workButtons
        .filter(function (b) { return b.classList.contains('is-active'); })
        .map(function (b) { return b.getAttribute('data-wk-toggle'); });
    }
    var defaultWorks = activeWorks().join(',');
    var defaultSorts = sortToggles.map(function (t) { return activeKey(qsa('.sort-toggle-btn', t), 'data-sort'); });

    function visibleSortToggle() {
      for (var i = 0; i < sortToggles.length; i++) {
        if (!isInHiddenPanel(sortToggles[i])) return i;
      }
      return -1;
    }

    function clickKey(buttons, attr, key) {
      for (var i = 0; i < buttons.length; i++) {
        if (buttons[i].getAttribute(attr) === key) {
          if (!buttons[i].classList.contains('is-active')) buttons[i].click();
          return true;
        }
      }
      return false;
    }

    // 見えている件数並べ替えボタン（タブで隠れていないもの）。
    function visibleCountSort() {
      for (var i = 0; i < countSortButtons.length; i++) {
        var b = countSortButtons[i];
        if (!b.hidden && !isInHiddenPanel(b) && b.hasAttribute('data-count-sort-state')) return b;
      }
      return null;
    }

    function setChecked(input, checked) {
      if (input.checked === checked) return;
      input.checked = checked;
      input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // ── 開いたとき：# を読んで状態を戻す ──
    var applying = true;
    var raw = (location.hash || '').replace(/^#/, '');
    if (raw.indexOf('=') >= 0) {
      var params = {};
      raw.split('&').forEach(function (kv) {
        var i = kv.indexOf('=');
        if (i <= 0) return;
        try { params[decodeURIComponent(kv.slice(0, i))] = decodeURIComponent(kv.slice(i + 1)); } catch (e) { /* 壊れた項目は無視 */ }
      });
      if (params.tab) clickKey(tabButtons, 'data-tab', params.tab);
      if (params.filter) clickKey(filterButtons, 'data-entity-filter-set', params.filter);
      if (params.sort) {
        var si = visibleSortToggle();
        if (si >= 0) clickKey(qsa('.sort-toggle-btn', sortToggles[si]), 'data-sort', params.sort);
      }
      if (params.works) {
        // 最後の 1 つは切れないので、入れる側を先に押してから切る側を押す。
        var wanted = params.works.split(',');
        workButtons.forEach(function (b) {
          if (wanted.indexOf(b.getAttribute('data-wk-toggle')) >= 0 && !b.classList.contains('is-active')) b.click();
        });
        if (workButtons.some(function (b) { return wanted.indexOf(b.getAttribute('data-wk-toggle')) >= 0; })) {
          workButtons.forEach(function (b) {
            if (wanted.indexOf(b.getAttribute('data-wk-toggle')) < 0 && b.classList.contains('is-active')) b.click();
          });
        }
      }
      if (params.main === 'all') mainInputs.forEach(function (input) { setChecked(input, false); });
      if (params.precure === '1') precureInputs.forEach(function (input) { setChecked(input, true); });
      if (params.csort) {
        // count-sort.js の準備（data-count-sort-state）が済み、タブの切り替えで見えるボタンが決まってから合わせる。
        var wanted = params.csort;
        setTimeout(function () {
          applying = true;
          var b = visibleCountSort();
          for (var n = 0; b && n < 32 && b.getAttribute('data-count-sort-state') !== wanted; n++) b.click();
          applying = false;
          write();
        }, 0);
      }
      if (params.hide) {
        var hidden = params.hide.split(',');
        kindInputs.forEach(function (input) {
          if (hidden.indexOf(input.getAttribute('data-kind-toggle')) >= 0) setChecked(input, false);
        });
      }
    } else if (raw) {
      // #セクションid：その要素が隠れたタブの中にあれば、そのタブを開いてから移る。
      var target = null;
      try { target = document.getElementById(decodeURIComponent(raw)); } catch (e) { target = null; }
      var panel = target && target.closest('[data-tab-panel]');
      if (panel && panel.hasAttribute('hidden')) {
        clickKey(tabButtons, 'data-tab', panel.getAttribute('data-tab-panel'));
        target.scrollIntoView();
      }
    }
    applying = false;

    // ── 操作のたび：今の状態を # に書く ──
    function write() {
      if (applying) return;
      var parts = [];
      var tab = activeKey(tabButtons, 'data-tab');
      if (tab && tab !== defaultTab) parts.push('tab=' + encodeURIComponent(tab));
      var filter = activeKey(filterButtons, 'data-entity-filter-set');
      if (filter && filter !== defaultFilter) parts.push('filter=' + encodeURIComponent(filter));
      var works = activeWorks().join(',');
      if (works && works !== defaultWorks) parts.push('works=' + works);
      var si = visibleSortToggle();
      if (si >= 0) {
        var sort = activeKey(qsa('.sort-toggle-btn', sortToggles[si]), 'data-sort');
        if (sort && sort !== defaultSorts[si]) parts.push('sort=' + encodeURIComponent(sort));
      }
      // 「プリキュアを演じた方のみ」の間は「主な方のみ」が強制で切れているので、主な方の状態は書かない。
      var precureOn = precureInputs.some(function (input) { return input.checked; });
      if (!precureOn && mainInputs.some(function (input) { return !input.checked; })) parts.push('main=all');
      if (precureOn) parts.push('precure=1');
      var cb = visibleCountSort();
      if (cb && cb.getAttribute('data-count-sort-state') !== cb.getAttribute('data-count-sort-default')) {
        parts.push('csort=' + encodeURIComponent(cb.getAttribute('data-count-sort-state')));
      }
      var hide = kindInputs
        .filter(function (input) { return !input.checked; })
        .map(function (input) { return input.getAttribute('data-kind-toggle'); });
      if (hide.length) parts.push('hide=' + hide.map(encodeURIComponent).join(','));

      var url = location.pathname + location.search + (parts.length ? '#' + parts.join('&') : '');
      try {
        history.replaceState(history.state, '', url);
      } catch (e) {
        // replaceState が使えない環境では残さない。
      }
    }

    // 各ボタンの処理（要素に直接付いたリスナー）が済んでから書く。
    document.addEventListener('click', function (ev) {
      if (ev.target.closest('.tab-btn, .entity-filter-btn, .wk-toggle, .sort-toggle-btn')) write();
    });
    // 件数並べ替えボタンは押すと中身（押した span）を描き直すので、document で受けると押した要素がもうボタンの中に無い。
    // ボタンに直接付ける（count-sort.js のリスナーの後に付くので、並べ替えが済んでから書く）。
    countSortButtons.forEach(function (b) { b.addEventListener('click', write); });
    document.addEventListener('change', function (ev) {
      if (ev.target.matches('.rtl-filter-input, .precure-only-input, input[data-kind-toggle]')) write();
    });
  }

  // 各ページのタブ処理（本文のインライン JS と、本文で読み込む defer のスクリプト）が揃ってから動かす。
  // defer のスクリプトはすべて DOMContentLoaded より前に実行される。
  var started = false;
  function start() {
    if (started) return;
    // 先に描かれている間（speculationrules の prerender）は、表示されてから始める（預かった状態は表示のときに届く）。
    if (document.prerendering) {
      document.addEventListener('prerenderingchange', start, { once: true });
      return;
    }
    started = true;
    init();
  }
  if (document.readyState === 'complete') {
    start();
  } else {
    document.addEventListener('DOMContentLoaded', start);
    window.addEventListener('load', start);
  }
})();
