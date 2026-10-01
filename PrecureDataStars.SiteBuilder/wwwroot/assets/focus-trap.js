/**
 * focus-trap.js — モーダル（aria-modal="true"）のダイアログ・スマホ用メニューのキーボード操作を整える共通ヘルパー。
 *
 *   var trap = window.PCDS.focusTrap.activate(container);   // 開いた直後に呼ぶ
 *   trap.release();                                         // 閉じるときに呼ぶ
 *
 * activate は、container 内の最初の操作できる要素（または第 2 引数の要素）へフォーカスを移し、
 * Tab / Shift+Tab のフォーカス移動を container の中で循環させる（背後のページへ出さない）。
 * release は循環を解き、開く前にフォーカスがあった要素へフォーカスを戻す（release(false) なら戻さない）。
 * subtitle-embargo.js・art-track-pref.js・mobile-nav.js が使うため、それらより先に defer で読み込む。
 */
(function () {
  'use strict';

  var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), '
    + 'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

  function isVisible(el) {
    return !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length);
  }

  function focusablesIn(container) {
    return Array.prototype.filter.call(container.querySelectorAll(FOCUSABLE), isVisible);
  }

  function activate(container, initial) {
    var previous = document.activeElement;

    function onKeydown(e) {
      if (e.key !== 'Tab') return;
      var els = focusablesIn(container);
      if (els.length === 0) { e.preventDefault(); return; }
      var first = els[0];
      var last = els[els.length - 1];
      var active = document.activeElement;
      if (e.shiftKey) {
        if (active === first || !container.contains(active)) { e.preventDefault(); last.focus(); }
      } else {
        if (active === last || !container.contains(active)) { e.preventDefault(); first.focus(); }
      }
    }
    document.addEventListener('keydown', onKeydown, true);

    var target = initial || focusablesIn(container)[0];
    if (target) target.focus();

    return {
      release: function (restoreFocus) {
        document.removeEventListener('keydown', onKeydown, true);
        if (restoreFocus !== false && previous && previous.focus && document.contains(previous)) previous.focus();
      }
    };
  }

  window.PCDS = window.PCDS || {};
  window.PCDS.focusTrap = { activate: activate };
})();
