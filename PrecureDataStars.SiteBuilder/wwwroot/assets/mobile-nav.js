/*
 * mobile-nav.js
 *
 * モバイル幅でのみ表示されるハンバーガーメニューの開閉ロジック（v1.3.0 続編で導入）。
 *
 * 設計方針
 *   - HTML 構造（_layout.sbn）：
 *       <button id="mobileNavToggle" aria-expanded="false" aria-controls="mobileNavOverlay">
 *       <div id="mobileNavOverlay" hidden>
 *         <div class="mobile-nav-overlay-backdrop" data-mobile-nav-close></div>
 *         <nav class="mobile-nav-overlay-panel">
 *           <button class="mobile-nav-overlay-close" data-mobile-nav-close>✕</button>
 *           <ul class="mobile-nav-overlay-list">...</ul>
 *           <div class="mobile-nav-overlay-search">
 *             <button id="mobileNavSearchJump">🔍 検索する</button>
 *           </div>
 *         </nav>
 *       </div>
 *   - 表示状態の正規ソースは aria-expanded 属性（CSS の :is() 系セレクタが参照）。
 *     overlay 側の hidden 属性は補助的に切替（display 制御）。
 *   - 背面スクロール抑止：body.mobile-nav-open クラスを付けて CSS の overflow:hidden を効かせる。
 *   - 閉じる手段：✕ ボタン、背景（バックドロップ）クリック、Escape キー、画面リサイズで
 *     デスクトップ幅（>768px）に切り替わったとき。
 *   - 「🔍 検索する」ボタン：オーバーレイを閉じ、ページ最上部までスムーススクロールして
 *     ヘッダ内の #site-search-input にフォーカスする。
 */
(function () {
  'use strict';

  var toggle  = document.getElementById('mobileNavToggle');
  var overlay = document.getElementById('mobileNavOverlay');
  if (!toggle || !overlay) return;

  // 開状態を保持するフラグ（getAttribute('hidden') を毎回見るより軽い）。
  var isOpen = false;

  function open() {
    if (isOpen) return;
    isOpen = true;
    overlay.removeAttribute('hidden');
    toggle.setAttribute('aria-expanded', 'true');
    toggle.setAttribute('aria-label', 'メニューを閉じる');
    document.body.classList.add('mobile-nav-open');
  }

  function close() {
    if (!isOpen) return;
    isOpen = false;
    overlay.setAttribute('hidden', '');
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-label', 'メニューを開く');
    document.body.classList.remove('mobile-nav-open');
  }

  // ハンバーガーボタン本体のクリックでトグル。
  toggle.addEventListener('click', function () {
    if (isOpen) close(); else open();
  });

  // 「閉じる」アクションを持つ要素群（✕ ボタン + バックドロップ）。
  // data-mobile-nav-close 属性を付けたものを一括バインドする。
  var closers = overlay.querySelectorAll('[data-mobile-nav-close]');
  for (var i = 0; i < closers.length; i++) {
    closers[i].addEventListener('click', close);
  }

  // Escape キーで閉じる。開状態のときだけ反応。
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && isOpen) {
      close();
      // フォーカスをハンバーガーボタンに戻して、キーボード操作の文脈を維持。
      toggle.focus();
    }
  });

  // 画面幅がデスクトップに切り替わったらオーバーレイを強制的に閉じる。
  // メディアクエリ側でも display:none となるが、aria-expanded / body クラスの整合は JS が担う。
  var mediaQuery = window.matchMedia('(min-width: 769px)');
  function onMediaChange(ev) {
    if (ev.matches && isOpen) close();
  }
  // 古い Safari / 古い Edge 互換の addListener フォールバック。
  if (mediaQuery.addEventListener) {
    mediaQuery.addEventListener('change', onMediaChange);
  } else if (mediaQuery.addListener) {
    mediaQuery.addListener(onMediaChange);
  }

  // 「🔍 検索する」ボタン：オーバーレイを閉じ、ページ最上部までスムーススクロール、
  // しばらく待ってからヘッダ内の検索ボックスにフォーカスする。
  // スムーススクロール完了前にフォーカスすると着地点がずれるため、setTimeout で
  // スクロール完了を待つ（厳密にはイベントは無いので体感タイミングで 400ms）。
  var jumpBtn = document.getElementById('mobileNavSearchJump');
  if (jumpBtn) {
    jumpBtn.addEventListener('click', function () {
      close();
      try {
        window.scrollTo({ top: 0, behavior: 'smooth' });
      } catch (_) {
        // 古いブラウザ用フォールバック：オプション無視で即座にトップへ。
        window.scrollTo(0, 0);
      }
      setTimeout(function () {
        var input = document.getElementById('site-search-input');
        if (input) input.focus();
      }, 400);
    });
  }
})();
