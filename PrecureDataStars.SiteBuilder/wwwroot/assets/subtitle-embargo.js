/**
 * subtitle-embargo.js — 未放送話（前話の予告がまだ放送されていない話数）のサブタイトルを
 * 全ページで一律にぼかし、閲覧者の同意設定に応じて解禁するクライアント側ロジック。
 *
 * サーバー側（SiteBuilder）は各所で実サブタイトルを
 *   <span class="ep-subtitle-guard" data-reveal-at="2026-08-02T08:58:40+09:00">…</span>
 * の形でそのまま埋め込む（解禁時刻を過ぎている大多数のエピソードにはこのラップ自体が付かない）。
 * 本スクリプトが DOMContentLoaded 後に data-reveal-at を持つ要素を走査し、
 * 「現在時刻が解禁時刻を過ぎている」または「閲覧者が早期表示に同意済み」なら
 * is-revealed クラスを付けてぼかしを解除する。
 *
 * 同意状態は localStorage に保存し、次回以降は確認ダイアログを出さず記憶した設定に従う。
 * フッターの運営情報リンク列に常設のトグルスイッチ（#subtitle-embargo-toggle、checkbox）を置き、
 * いつでも切り替えられる。
 * home.sbn の calendar.js や search.js など、埋め込み JSON からクライアント側で
 * DOM を組み立てる他スクリプトも window.PCDS.subtitleEmbargo を通じて同じ判定・設定を共有する
 * （このため本スクリプトは _layout.sbn の <head> で最優先 defer 読み込みし、他の defer スクリプトより
 * 必ず先に実行されるようにしてある）。
 */
(function () {
  'use strict';

  var STORAGE_KEY = 'pcds-subtitle-embargo-pref';
  var GUARD_SELECTOR = '.ep-subtitle-guard[data-reveal-at]';

  /** 保存済みの閲覧者設定を返す（'reveal' | 'hide' | null＝未設定）。 */
  function getPreference() {
    try {
      var v = window.localStorage.getItem(STORAGE_KEY);
      return (v === 'reveal' || v === 'hide') ? v : null;
    } catch (e) {
      // プライベートブラウジング等で localStorage が使えないときは常に未設定扱い。
      return null;
    }
  }

  function setPreference(pref) {
    try {
      window.localStorage.setItem(STORAGE_KEY, pref);
    } catch (e) {
      // 保存できなくても致命的ではない（次回もダイアログが出るだけ）。
    }
  }

  function isPastReveal(revealAtIso) {
    if (!revealAtIso) return true;
    var t = Date.parse(revealAtIso);
    if (isNaN(t)) return true;
    return Date.now() >= t;
  }

  /** 解禁済みとして表示してよいか（時刻を過ぎている、または早期表示に同意済み）。 */
  function isRevealed(revealAtIso) {
    if (isPastReveal(revealAtIso)) return true;
    return getPreference() === 'reveal';
  }

  /** ページ内の全ガード要素へ現在の判定結果を反映する。現在アクティブな embargo が 1 件でもあれば true。 */
  function applyGuards() {
    var els = document.querySelectorAll(GUARD_SELECTOR);
    var hasActiveEmbargo = false;
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      var revealed = isRevealed(el.getAttribute('data-reveal-at'));
      el.classList.toggle('is-revealed', revealed);
      if (!revealed) hasActiveEmbargo = true;
    }
    updateToggleUi();
    return hasActiveEmbargo;
  }

  // ── フッターの常設トグルスイッチ（checkbox） ──
  function updateToggleUi() {
    var input = document.getElementById('subtitle-embargo-toggle');
    if (!input) return;
    input.checked = getPreference() === 'reveal';
  }

  // ── 初回確認ダイアログ ──
  function showDialogIfNeeded(hasActiveEmbargo) {
    if (!hasActiveEmbargo) return;
    if (getPreference() !== null) return;
    var dialog = document.getElementById('subtitle-embargo-dialog');
    if (!dialog) return;
    dialog.hidden = false;
    document.body.classList.add('subtitle-embargo-dialog-open');
  }

  function closeDialog() {
    var dialog = document.getElementById('subtitle-embargo-dialog');
    if (dialog) dialog.hidden = true;
    document.body.classList.remove('subtitle-embargo-dialog-open');
  }

  function wireDialog() {
    var dialog = document.getElementById('subtitle-embargo-dialog');
    if (!dialog) return;
    var revealBtn = document.getElementById('subtitle-embargo-dialog-reveal');
    var hideBtn = document.getElementById('subtitle-embargo-dialog-hide');
    var backdrop = dialog.querySelector('[data-subtitle-embargo-dismiss]');
    if (revealBtn) {
      revealBtn.addEventListener('click', function () {
        setPreference('reveal');
        applyGuards();
        closeDialog();
      });
    }
    if (hideBtn) {
      hideBtn.addEventListener('click', function () {
        setPreference('hide');
        applyGuards();
        closeDialog();
      });
    }
    // 背景クリック・Escape での閉じ方は「未決定のまま閉じる」扱い（設定は保存しない＝次回また聞く）。
    if (backdrop) backdrop.addEventListener('click', closeDialog);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !dialog.hidden) closeDialog();
    });
  }

  function wireToggleSwitch() {
    var input = document.getElementById('subtitle-embargo-toggle');
    if (!input) return;
    input.addEventListener('change', function () {
      setPreference(input.checked ? 'reveal' : 'hide');
      applyGuards();
    });
  }

  function init() {
    wireDialog();
    wireToggleSwitch();
    var hasActiveEmbargo = applyGuards();
    showDialogIfNeeded(hasActiveEmbargo);
  }

  // calendar.js / search.js など、埋め込み JSON から動的に DOM を組み立てる他スクリプトが
  // 同じ判定ロジックと同意設定を再利用できるよう公開する。
  window.PCDS = window.PCDS || {};
  window.PCDS.subtitleEmbargo = {
    isRevealed: isRevealed,
    getPreference: getPreference,
    setPreference: setPreference,
    refresh: applyGuards
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
