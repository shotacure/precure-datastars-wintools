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
 *   media=<data-media-set>         年表の「TVのみ・映画のみ」（.rtl-media-btn）
 *   main=all                       年表の「主な方のみ」を切った（.rtl-filter-input）
 *   hide=movie,spring              /series/ の年度順で OFF にした区分（input[data-kind-toggle]）
 *   例：#tab=timeline&filter=person&main=all
 *
 * # が「=」を含まない（セクションナビ・ページ内リンクの #セクションid）ときは、その要素を含むタブを開いてそこへ移る。
 * 書き換えは history.replaceState で行い、ブラウザの履歴は増やさない。
 * 件数バッジでの並べ替え（count-sort.js）は押すたびに順に切り替わる一時的な並べ替えなので残さない。
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

  function init() {
    var tabButtons = qsa('.tab-btn[data-tab]');
    var filterButtons = qsa('.entity-filter-btn[data-entity-filter-set]');
    var mediaButtons = qsa('.rtl-media-btn[data-media-set]');
    var mainInputs = qsa('.rtl-filter-input');
    var kindInputs = qsa('input[data-kind-toggle]');
    var sortToggles = qsa('.sort-toggle');
    if (!tabButtons.length && !filterButtons.length && !mediaButtons.length && !mainInputs.length && !kindInputs.length && !sortToggles.length) return;

    // 初期状態（テンプレが is-active・checked を付けた状態）を覚えておき、それと違う項目だけを # に書く。
    var defaultTab = activeKey(tabButtons, 'data-tab');
    var defaultFilter = activeKey(filterButtons, 'data-entity-filter-set');
    var defaultMedia = activeKey(mediaButtons, 'data-media-set');
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
      if (params.media) clickKey(mediaButtons, 'data-media-set', params.media);
      if (params.main === 'all') mainInputs.forEach(function (input) { setChecked(input, false); });
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
      var media = activeKey(mediaButtons, 'data-media-set');
      if (media && media !== defaultMedia) parts.push('media=' + encodeURIComponent(media));
      var si = visibleSortToggle();
      if (si >= 0) {
        var sort = activeKey(qsa('.sort-toggle-btn', sortToggles[si]), 'data-sort');
        if (sort && sort !== defaultSorts[si]) parts.push('sort=' + encodeURIComponent(sort));
      }
      if (mainInputs.some(function (input) { return !input.checked; })) parts.push('main=all');
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
      if (ev.target.closest('.tab-btn, .entity-filter-btn, .rtl-media-btn, .sort-toggle-btn')) write();
    });
    document.addEventListener('change', function (ev) {
      if (ev.target.matches('.rtl-filter-input, input[data-kind-toggle]')) write();
    });
  }

  // 各ページのタブ処理（本文のインライン JS と、本文で読み込む defer のスクリプト）が揃ってから動かす。
  // defer のスクリプトはすべて DOMContentLoaded より前に実行される。
  var started = false;
  function start() {
    if (started) return;
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
