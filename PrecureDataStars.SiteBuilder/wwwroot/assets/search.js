/* =============================================================================
 * precure-datastars サイト内検索（v1.3.0 後半追加）
 *
 * 静的サイトでクライアント側全文検索を成立させる JS。
 * /search-index.json を初回入力時に fetch し、メモリ上でフィルタする。
 *
 * 仕様:
 *   - ヘッダの <input id="site-search-input"> から起動
 *   - 結果は <div id="site-search-results"> に最大 20 件
 *   - インクリメンタル：50ms デバウンスで再検索
 *   - マッチ：title (t) と reading (x) の両方を部分一致、スペース区切りは AND
 *   - クエリは正規化（全角カナ→ひらがな、英大文字→小文字、空白除去）
 *   - キーボード操作：↓↑ で候補移動、Enter で選択、Esc で閉じる
 *
 * 検索対象は SearchIndexGenerator が出力する JSON 配列。各アイテムは
 *   { u: URL, t: title, k: kind, s: subtext, x: reading }
 * の 5 キー。
 * ========================================================================== */
(function () {
  'use strict';

  // 結果表示の最大件数（多すぎると DOM 描画が重くなるので適当な上限で打ち切る）。
  var MAX_RESULTS = 20;
  // 入力デバウンス（ミリ秒）。連続入力中にフィルタが走り過ぎないように。
  var DEBOUNCE_MS = 50;

  // インデックス本体（fetch 後にここに保持）。null = 未ロード、Array = ロード済み。
  var indexData = null;
  // fetch の重複防止フラグ。
  var indexLoading = false;

  // 種別コード → 表示用ラベルの変換テーブル（結果ドロップダウンで小さなバッジに使う）。
  var KIND_LABELS = {
    'series':    'シリーズ',
    'episode':   'エピソード',
    'precure':   'プリキュア',
    'character': 'キャラ',
    'person':    '人物',
    'company':   '企業',
    'song':      '楽曲',
    'product':   '商品'
  };

  // 結果並び替え用：種別の表示優先順位。同点（マッチスコア）の場合の順序を決める。
  var KIND_ORDER = {
    'series':    1,
    'episode':   2,
    'precure':   3,
    'character': 4,
    'person':    5,
    'company':   6,
    'song':      7,
    'product':   8
  };

  /** クエリ文字列を正規化する。SearchIndexGenerator.NormalizeForSearch と対応する処理。 */
  function normalizeQuery(s) {
    if (!s) return '';
    var out = '';
    for (var i = 0; i < s.length; i++) {
      var ch = s.charCodeAt(i);
      // 全角カタカナ → ひらがな（U+30A1〜U+30F6 を U+3041〜U+3096 にシフト）
      if (ch >= 0x30A1 && ch <= 0x30F6) {
        out += String.fromCharCode(ch - 0x60);
        continue;
      }
      // 英大文字 → 小文字
      if (ch >= 0x41 && ch <= 0x5A) {
        out += String.fromCharCode(ch + 32);
        continue;
      }
      // 空白除去
      if (s.charAt(i) === ' ' || s.charAt(i) === '\t' || s.charAt(i) === '\u3000') {
        continue;
      }
      out += s.charAt(i);
    }
    return out;
  }

  /** インデックスを fetch する（初回入力時のみ）。 */
  function loadIndex(callback) {
    if (indexData !== null) { callback(); return; }
    if (indexLoading) {
      // すでに別呼び出しでロード中なら待つ（リトライ）。
      setTimeout(function () { loadIndex(callback); }, 30);
      return;
    }
    indexLoading = true;
    fetch('/search-index.json')
      .then(function (r) {
        if (!r.ok) throw new Error('search index fetch failed: ' + r.status);
        return r.json();
      })
      .then(function (data) {
        indexData = data;
        indexLoading = false;
        callback();
      })
      .catch(function (err) {
        console.error('Search index load failed:', err);
        indexLoading = false;
        // エラー時は空インデックスとして扱い、それ以降の検索は単に 0 件になる。
        indexData = [];
        callback();
      });
  }

  /**
   * 検索の本体。クエリを空白で分割して全語が title / reading のいずれかに含まれているかを AND 判定。
   * マッチした結果は (種別優先順位, タイトル長) の 2 段ソートで並べて、上位 MAX_RESULTS 件を返す。
   */
  function performSearch(rawQuery) {
    if (!indexData || indexData.length === 0) return [];

    // クエリ語ごとに正規化して語リストを作る。スペース区切りで AND。
    var rawTerms = rawQuery.trim().split(/[\s\u3000]+/);
    var terms = [];
    for (var i = 0; i < rawTerms.length; i++) {
      var nt = normalizeQuery(rawTerms[i]);
      if (nt.length > 0) terms.push({ raw: rawTerms[i].toLowerCase(), norm: nt });
    }
    if (terms.length === 0) return [];

    var results = [];
    for (var j = 0; j < indexData.length; j++) {
      var item = indexData[j];
      var titleLower = (item.t || '').toLowerCase();
      var reading = item.x || '';
      var allMatch = true;
      // 全クエリ語について「正規化済み reading」または「lowercase title」のいずれかに含まれるかをチェック。
      for (var k = 0; k < terms.length; k++) {
        var t = terms[k];
        if (reading.indexOf(t.norm) === -1 && titleLower.indexOf(t.raw) === -1) {
          allMatch = false;
          break;
        }
      }
      if (allMatch) results.push(item);
    }

    // ソート：種別優先順位 → タイトル長昇順（短いものほど一般的なマッチに近い）→ タイトル昇順。
    results.sort(function (a, b) {
      var ka = KIND_ORDER[a.k] || 99;
      var kb = KIND_ORDER[b.k] || 99;
      if (ka !== kb) return ka - kb;
      var la = (a.t || '').length;
      var lb = (b.t || '').length;
      if (la !== lb) return la - lb;
      return (a.t || '').localeCompare(b.t || '', 'ja');
    });

    return results.slice(0, MAX_RESULTS);
  }

  /** 1 件の結果を <a> 要素として描画。 */
  function renderResult(item) {
    var a = document.createElement('a');
    a.className = 'site-search-result-item';
    a.href = item.u;
    a.setAttribute('role', 'option');

    var kindBadge = document.createElement('span');
    kindBadge.className = 'site-search-result-kind site-search-result-kind-' + item.k;
    kindBadge.textContent = KIND_LABELS[item.k] || item.k;
    a.appendChild(kindBadge);

    var title = document.createElement('span');
    title.className = 'site-search-result-title';
    title.textContent = item.t;
    a.appendChild(title);

    if (item.s) {
      var sub = document.createElement('span');
      sub.className = 'site-search-result-sub';
      sub.textContent = item.s;
      a.appendChild(sub);
    }
    return a;
  }

  /** 結果をドロップダウンに描画。空なら結果ボックスを非表示。 */
  function renderResults(container, results, query) {
    container.innerHTML = '';
    if (results.length === 0) {
      if (query.trim().length > 0) {
        var noHit = document.createElement('div');
        noHit.className = 'site-search-no-results';
        noHit.textContent = '「' + query + '」に一致する項目は見つかりませんでした。';
        container.appendChild(noHit);
        container.classList.add('open');
      } else {
        container.classList.remove('open');
      }
      return;
    }
    for (var i = 0; i < results.length; i++) {
      container.appendChild(renderResult(results[i]));
    }
    container.classList.add('open');
  }

  /** 現在ハイライトされている候補のインデックスを返す（無ければ -1）。 */
  function getSelectedIndex(container) {
    var items = container.querySelectorAll('.site-search-result-item');
    for (var i = 0; i < items.length; i++) {
      if (items[i].classList.contains('selected')) return i;
    }
    return -1;
  }

  /** 候補のハイライトを移動する。 */
  function moveSelection(container, delta) {
    var items = container.querySelectorAll('.site-search-result-item');
    if (items.length === 0) return;
    var current = getSelectedIndex(container);
    var next;
    if (current === -1) {
      next = (delta > 0) ? 0 : items.length - 1;
    } else {
      items[current].classList.remove('selected');
      next = (current + delta + items.length) % items.length;
    }
    items[next].classList.add('selected');
    items[next].scrollIntoView({ block: 'nearest' });
  }

  /** ハイライトされている候補があればそれをクリック相当で開く。 */
  function activateSelection(container) {
    var items = container.querySelectorAll('.site-search-result-item');
    for (var i = 0; i < items.length; i++) {
      if (items[i].classList.contains('selected')) {
        window.location.href = items[i].href;
        return true;
      }
    }
    return false;
  }

  // ── 起動 ──
  document.addEventListener('DOMContentLoaded', function () {
    var input = document.getElementById('site-search-input');
    var results = document.getElementById('site-search-results');
    if (!input || !results) return;

    var debounceTimer = null;

    function doSearch() {
      var q = input.value;
      if (q.trim().length === 0) {
        results.innerHTML = '';
        results.classList.remove('open');
        return;
      }
      loadIndex(function () {
        var hits = performSearch(q);
        renderResults(results, hits, q);
      });
    }

    input.addEventListener('input', function () {
      if (debounceTimer) clearTimeout(debounceTimer);
      debounceTimer = setTimeout(doSearch, DEBOUNCE_MS);
    });

    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        moveSelection(results, +1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        moveSelection(results, -1);
      } else if (e.key === 'Enter') {
        if (activateSelection(results)) {
          e.preventDefault();
        }
        // 選択が無ければ通常の Enter として扱う（検索ページ無いので何もしない）
      } else if (e.key === 'Escape') {
        results.innerHTML = '';
        results.classList.remove('open');
        input.blur();
      }
    });

    // フォーカス時に既に入力があれば再表示。
    input.addEventListener('focus', function () {
      if (input.value.trim().length > 0) doSearch();
    });

    // 結果ボックス外クリックで閉じる。
    document.addEventListener('click', function (e) {
      if (e.target === input) return;
      if (results.contains(e.target)) return;
      results.classList.remove('open');
    });
  });
})();
