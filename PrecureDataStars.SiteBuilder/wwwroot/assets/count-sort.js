/*
 * 件数バッジでの並べ替えボタン（.count-sort-btn）。
 *
 * ボタンの data-count-sort-for に並べ替え対象のリスト（<ul>）の id を空白区切りで書く。
 * 対象リストの各行（直下の <li>）が持つ件数バッジ（.count-pill-tv / -movie / -song / -cd / -book / -mic / -person / -company）の
 * 数字を読み取り、押すたびに次の順で並べ替える：
 *   バッジの種類ごとに「多い順 ⬇ → 少ない順 ⬆」を、リストに現れる種類の並び順どおりにたどり、
 *   種類が 2 つ以上あるときは最後に全種類の合計（📺🎥 など）で「⬇ → ⬆」、そして最初に戻る。
 *   例：📺⬇ → 📺⬆ → 🎥⬇ → 🎥⬆ → 📺🎥⬇ → 📺🎥⬆ → 📺⬇ …
 * ・既定：最初の種類の多い順（例：📺⬇）で並べ直した状態から始める。
 *   data-count-sort-initial="none"：もともとの並び（役職の表示順など）のまま「⇅」から始め、一周したら元の並びに戻す。
 * ・同じ数の行はもともとの並び順を保つ。種類ごとの並べ替えでは、その種類が 0 の行を向きにかかわらず末尾に置く。
 * ・ボタンは対象リストが画面に出ているときだけ表示する（タブや初参加順／多い順の切り替えに合わせて、クリックのたびに判定し直す）。
 * ・ページ出力は増やさず、並べ替えはこのスクリプトの中だけで行う（外部ライブラリなし）。
 */
(function () {
  'use strict';

  // バッジの種類。並び順はテンプレートのバッジの並び（📺 → 🎥 → 🎵 → 💿 …）に合わせる。
  var PERSON_SVG = '<svg class="count-pill-icon" viewBox="0 0 16 16" width="16" height="16" fill="currentColor" aria-hidden="true"><circle cx="8" cy="5" r="3"/><path d="M2 14.5a6 6 0 0 1 12 0z"/></svg>';
  var COMPANY_SVG = '<svg class="count-pill-icon" viewBox="0 0 16 16" width="16" height="16" fill="currentColor" fill-rule="evenodd" aria-hidden="true"><path d="M2.5 14.5V2.5h11v12zM5 5v1.6h2V5zm4 0v1.6h2V5zM5 8.4V10h2V8.4zm4 0V10h2V8.4z"/></svg>';
  var KINDS = [
    { key: 'tv', icon: '📺', label: 'TV' },
    { key: 'movie', icon: '🎥', label: '映画' },
    { key: 'song', icon: '🎵', label: '曲' },
    { key: 'cd', icon: '💿', label: '音盤' },
    { key: 'book', icon: '📚', label: '書籍' },
    { key: 'mic', icon: '🎤', label: '歌' },
    { key: 'person', icon: PERSON_SVG, label: '人物' },
    { key: 'company', icon: COMPANY_SVG, label: '団体' }
  ];

  // 1 行の件数を { tv: 46, movie: 3, … } の形で読む。
  function readCounts(li) {
    var counts = {};
    KINDS.forEach(function (k) {
      var pill = li.querySelector('.count-pill-' + k.key);
      if (!pill) return;
      var n = parseInt((pill.textContent || '').replace(/[^0-9]/g, ''), 10);
      if (!isNaN(n)) counts[k.key] = n;
    });
    return counts;
  }

  function isShown(el) {
    return el.getClientRects().length > 0;
  }

  function setup(btn) {
    var ids = (btn.getAttribute('data-count-sort-for') || '').split(/\s+/).filter(Boolean);
    var lists = ids.map(function (id) { return document.getElementById(id); }).filter(Boolean);
    if (lists.length === 0) return null;

    // 行ごとの件数ともとの並び順を控える。
    var present = {};
    var rowsByList = lists.map(function (list) {
      return Array.prototype.filter.call(list.children, function (c) { return c.tagName === 'LI'; })
        .map(function (li, i) {
          var counts = readCounts(li);
          Object.keys(counts).forEach(function (k) { present[k] = true; });
          return { el: li, index: i, counts: counts };
        });
    });
    var kinds = KINDS.filter(function (k) { return present[k.key]; });
    if (kinds.length === 0) return null;

    // 並べ替えの段階を組み立てる（種類ごとの ⬇⬆、2 種類以上なら合計の ⬇⬆）。
    var states = [];
    kinds.forEach(function (k) {
      states.push({ keys: [k.key], desc: true });
      states.push({ keys: [k.key], desc: false });
    });
    if (kinds.length > 1) {
      var all = kinds.map(function (k) { return k.key; });
      states.push({ keys: all, desc: true });
      states.push({ keys: all, desc: false });
    }
    var initialNone = btn.getAttribute('data-count-sort-initial') === 'none';
    if (initialNone) states.unshift({ keys: null, desc: true });
    // 既定は最初の種類の多い順（例：📺⬇）から始める。「⇅」から始めるリストはもとの並びのまま。
    var pos = 0;

    function valueOf(row, keys) {
      var sum = 0, has = false;
      keys.forEach(function (k) {
        if (row.counts[k] !== undefined) { sum += row.counts[k]; has = true; }
      });
      return has ? sum : null;
    }

    function apply() {
      var st = states[pos];
      rowsByList.forEach(function (rows, li) {
        var sorted = rows.slice();
        if (st.keys) {
          sorted.sort(function (a, b) {
            var va = valueOf(a, st.keys), vb = valueOf(b, st.keys);
            // その種類を持たない行（0 件）は向きにかかわらず末尾へ。
            if (va === null && vb === null) return a.index - b.index;
            if (va === null) return 1;
            if (vb === null) return -1;
            if (va !== vb) return st.desc ? vb - va : va - vb;
            return a.index - b.index;
          });
        }
        var list = lists[li];
        sorted.forEach(function (r) { list.appendChild(r.el); });
      });
      render();
    }

    function render() {
      var st = states[pos];
      var html, title;
      if (!st.keys) {
        html = '<span class="count-pill count-sort-pill count-sort-pill-none">⇅</span>';
        title = 'もとの並び';
      } else {
        var arrow = st.desc ? '⬇' : '⬆';
        var names = st.keys.map(function (key) {
          return KINDS.filter(function (k) { return k.key === key; })[0];
        });
        var icons = names.map(function (k) { return k.icon; }).join('');
        var cls = st.keys.length === 1 ? 'count-pill-' + st.keys[0] : 'count-sort-pill-total';
        cls = 'count-pill count-sort-pill ' + cls;
        html = '<span class="' + cls + '">' + icons + '<span class="count-sort-arrow">' + arrow + '</span></span>';
        title = names.map(function (k) { return k.label; }).join('・') + (st.keys.length > 1 ? 'の合計' : '')
          + (st.desc ? 'が多い順' : 'が少ない順');
      }
      btn.innerHTML = html;
      btn.setAttribute('title', title + '（押すと次の並べ替え）');
      btn.setAttribute('aria-label', '並べ替え：' + title);
    }

    function refreshVisibility() {
      btn.hidden = !lists.some(isShown);
    }

    btn.addEventListener('click', function () {
      pos = (pos + 1) % states.length;
      apply();
    });
    if (initialNone) { render(); } else { apply(); }
    return refreshVisibility;
  }

  function init() {
    var refreshers = [];
    document.querySelectorAll('.count-sort-btn').forEach(function (btn) {
      var r = setup(btn);
      if (r) { refreshers.push(r); } else { btn.hidden = true; }
    });
    if (refreshers.length === 0) return;
    var refreshAll = function () { refreshers.forEach(function (r) { r(); }); };
    refreshAll();
    // タブ・並べ替え切り替えの処理が済んだ後に表示を判定し直す。
    document.addEventListener('click', function () { setTimeout(refreshAll, 0); });
    window.addEventListener('resize', refreshAll);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
