/*
 * mobile-nav.js
 *
 * モバイル幅でのみ表示されるハンバーガーメニューの開閉ロジックと、
 * モバイル時のヘッダ検索ボックスのオーバーレイ移動を担う。
 *
 * 設計方針
 *   - HTML 構造（_layout.sbn）：
 *       <button id="mobileNavToggle" aria-expanded="false" aria-controls="mobileNavOverlay">
 *       <div id="mobileNavOverlay" hidden>
 *         <div class="mobile-nav-overlay-backdrop" data-mobile-nav-close></div>
 *         <nav class="mobile-nav-overlay-panel">
 *           <button class="mobile-nav-overlay-close" data-mobile-nav-close>✕</button>
 *           <ul class="mobile-nav-overlay-list">...</ul>
 *           <div id="mobileSectionNav" class="mobile-nav-overlay-section-nav">...</div>
 *           <div class="mobile-nav-overlay-search"><!-- モバイル時はここに .site-search が JS で移動される --></div>
 *         </nav>
 *       </div>
 *       <header class="site-header">
 *         <div class="container site-header-top">
 *           <a class="site-title">...</a>
 *           <div class="site-search">...</div>  <!-- モバイル時はオーバーレイ内へ移動 -->
 *         </div>
 *         <nav class="site-nav">...</nav>
 *       </header>
 *
 *   - 表示状態の正規ソースは aria-expanded 属性（CSS の :is() 系セレクタが参照）。
 *     overlay 側の hidden 属性は補助的に切替（display 制御）。
 *   - 背面スクロール抑止：body.mobile-nav-open クラスを付けて CSS の overflow:hidden を効かせる。
 *   - 閉じる手段：✕ ボタン、背景（バックドロップ）クリック、Escape キー、画面リサイズで
 *     デスクトップ幅（>768px）に切り替わったとき。
 *
 *   - 検索ボックスの DOM 移動：
 *     モバイル幅（≤768px）では .site-search を .mobile-nav-overlay-search 内に DOM ごと移動する。
 *     これにより：
 *       (a) ヘッダ内の検索 input がビューポート右端まで張り出してページ全体を横スクロールさせる問題を解消
 *       (b) ハンバーガー → 検索の動線を 1 タップに短縮（旧 "🔍 検索する" ボタン → 最上部スクロール → フォーカス
 *           という 3 段階を、オーバーレイ展開と同時に検索ボックスを露出する 1 段階に集約）
 *     デスクトップ幅（≥769px）に戻ったときは元の位置（ヘッダ内 site-header-top 末尾）に戻す。
 *     こうすることで input/results の DOM 同一性が保たれ、既存の site-search.js が
 *     querySelector('#site-search-input') / querySelector('#site-search-results') を毎回引き直さなくても
 *     インスタンス起動時の参照のまま動作する（イベントリスナも維持される）。
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

  // ── セクションナビミラー内のアンカークリック時にオーバーレイを閉じる ──────────
  //
  // section-nav.js が #mobileSectionNav に <a data-target-id="..."> のミラーを動的描画する。
  // モバイル幅でこのリンクをタップすると、section-nav.js 側のスムーススクロールハンドラが
  // preventDefault + window.scrollTo({ behavior: 'smooth' }) でセクション位置へスクロールを開始するが、
  // ハンバーガーオーバーレイは閉じないままだとフルスクリーンで画面を覆っているので
  // ユーザーには「スクロールしていない」ように見えてしまう。
  //
  // ここではミラー要素をルートにイベント委譲し、内側の a[data-target-id] クリックを検知したら
  // 同時に overlay を close() する。section-nav.js のスクロール処理とは別タスクで実行されるため
  // 互いの動作を妨げない。
  // 修飾キー（Ctrl/Cmd/Shift/Alt）+ クリック・中ボタンクリックなどの特殊操作では既定挙動を尊重し、
  // 自前の close() も呼ばない（新規タブ等で開く場合、現ページのオーバーレイ状態は維持する）。
  var mobileSectionNav = document.getElementById('mobileSectionNav');
  if (mobileSectionNav) {
    mobileSectionNav.addEventListener('click', function (ev) {
      if (ev.button !== 0) return;
      if (ev.ctrlKey || ev.metaKey || ev.shiftKey || ev.altKey) return;
      var a = ev.target.closest('a[data-target-id]');
      if (!a || !mobileSectionNav.contains(a)) return;
      close();
    });
  }

  // ── 検索ボックスの DOM 移動 ──────────────────────────────────────────
  //
  // 元位置：<header> 内の <div class="container site-header-top"> の末尾（site-title の次の兄弟）
  // 仮位置：<div class="mobile-nav-overlay-search"> 内
  //
  // 元位置をマーカーノード（コメントノード）で記録し、デスクトップ復帰時に元の位置へ戻す。
  // マーカー方式はインデックスや兄弟関係に依存しないので、レイアウトが後で変わっても破綻しない。
  var siteSearch = document.querySelector('.site-search');
  var overlaySearchHost = overlay.querySelector('.mobile-nav-overlay-search');
  var originalParent = siteSearch ? siteSearch.parentNode : null;
  var originalMarker = null;

  if (siteSearch && originalParent) {
    // 元の位置を覚えておくためのマーカー（空コメント）を兄弟として挿入。
    originalMarker = document.createComment('site-search-original-position');
    originalParent.insertBefore(originalMarker, siteSearch);
  }

  /** 検索ボックスをオーバーレイ内へ移す（モバイル幅で呼ぶ）。 */
  function moveSearchToOverlay() {
    if (!siteSearch || !overlaySearchHost) return;
    if (siteSearch.parentNode === overlaySearchHost) return;
    overlaySearchHost.appendChild(siteSearch);
  }

  /** 検索ボックスをヘッダ内の元の位置に戻す（デスクトップ幅で呼ぶ）。 */
  function moveSearchToHeader() {
    if (!siteSearch || !originalParent || !originalMarker) return;
    if (siteSearch.parentNode === originalParent
        && originalMarker.nextSibling === siteSearch) return;
    originalParent.insertBefore(siteSearch, originalMarker.nextSibling);
  }

  // 画面幅がモバイル幅にあるかどうかを判定するメディアクエリ。
  // CSS 側のブレークポイント（768px）と一致させる。
  var mobileMq = window.matchMedia('(max-width: 768px)');

  /** 現在のメディアクエリ状態に応じて検索ボックスの位置を更新する。 */
  function syncSearchLocation() {
    if (mobileMq.matches) {
      moveSearchToOverlay();
    } else {
      moveSearchToHeader();
    }
  }

  // 初期同期。DOMContentLoaded 後（本スクリプトは body 末尾 or defer 読み込み想定）で実行される。
  syncSearchLocation();

  // メディアクエリの変化を購読。モバイル ⇆ デスクトップ境界をまたぐたびに位置を切り替える。
  function onMobileMqChange(ev) {
    syncSearchLocation();
    // デスクトップ幅に切り替わった時点でオーバーレイが開いていれば強制的に閉じる。
    // メディアクエリ側でも display:none となるが、aria-expanded / body クラスの整合は JS が担う。
    if (!ev.matches && isOpen) close();
  }
  if (mobileMq.addEventListener) {
    mobileMq.addEventListener('change', onMobileMqChange);
  } else if (mobileMq.addListener) {
    // 古い Safari / 古い Edge 互換の addListener フォールバック。
    mobileMq.addListener(onMobileMqChange);
  }
})();
