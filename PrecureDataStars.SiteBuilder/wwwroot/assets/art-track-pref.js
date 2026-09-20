/**
 * art-track-pref.js — 「YouTube Music Premium 会員限定の配信音源を表示するか」の設定を扱う。
 *
 * 配信音源の一部は権利者の設定で Premium 会員限定になっており、会員でない閲覧者が
 * 再生ボタンを押しても再生できない。押して初めて分かるのは煩わしいので、
 * 「再生できなかったことを検知した時点で一度だけ尋ね、以後は隠す」という流れにしてある。
 *
 * 本ファイルの責務：
 *   - 保存済みの設定を読み、<html> に hide-premium-art-tracks クラスを付け外しする
 *     （実際に隠すのは site.css 側の規則）。
 *   - フッターの常設トグル（#art-track-premium-toggle）と双方向に同期する。
 *   - 再生失敗時の確認ダイアログ（#art-track-premium-dialog）を出す。
 *
 * 再生の検知そのものは art-track-player.js の担当で、失敗を掴んだら
 * window.PCDS.artTrackPremium.notifyBlocked() を呼んでくる。
 * 設定トグルはフッター＝全ページにあるため、本ファイルは全ページで読み込む
 * （再生ボタンのあるページでしか読まない art-track-player.js とは役割を分けている）。
 *
 * 一度「隠す」を選んだあとも、フッターのスイッチでいつでも戻せる。
 */
(function () {
  'use strict';

  var STORAGE_KEY = 'pcds-art-track-premium-pref';
  var HIDE_CLASS = 'hide-premium-art-tracks';

  /** 保存済みの設定を返す（'show' | 'hide' | null＝未設定）。 */
  function getPreference() {
    try {
      var v = window.localStorage.getItem(STORAGE_KEY);
      return (v === 'show' || v === 'hide') ? v : null;
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

  /** 現在の設定をページへ反映する。未設定は「表示する」と同じ扱い。 */
  function apply(pref) {
    var root = document.documentElement;
    if (!root) return;
    if (pref === 'hide') root.classList.add(HIDE_CLASS);
    else root.classList.remove(HIDE_CLASS);
  }

  // 描画のちらつきを避けるため、DOM の準備を待たずにこの時点で当てる。
  apply(getPreference());

  function syncToggle(pref) {
    var input = document.getElementById('art-track-premium-toggle');
    if (input) input.checked = (pref !== 'hide');
  }

  function openDialog() {
    var dialog = document.getElementById('art-track-premium-dialog');
    if (!dialog) return;
    dialog.hidden = false;
    document.body.classList.add('subtitle-embargo-dialog-open');
  }

  function closeDialog() {
    var dialog = document.getElementById('art-track-premium-dialog');
    if (dialog) dialog.hidden = true;
    document.body.classList.remove('subtitle-embargo-dialog-open');
  }

  function choose(pref) {
    setPreference(pref);
    apply(pref);
    syncToggle(pref);
    closeDialog();
  }

  function init() {
    syncToggle(getPreference());

    var input = document.getElementById('art-track-premium-toggle');
    if (input) {
      input.addEventListener('change', function () {
        choose(input.checked ? 'show' : 'hide');
      });
    }

    var dialog = document.getElementById('art-track-premium-dialog');
    if (!dialog) return;

    var btnHide = document.getElementById('art-track-premium-dialog-hide');
    var btnKeep = document.getElementById('art-track-premium-dialog-keep');
    if (btnHide) btnHide.addEventListener('click', function () { choose('hide'); });
    if (btnKeep) btnKeep.addEventListener('click', function () { choose('show'); });

    // 背景クリックと Escape は「未決定のまま閉じる」。次に再生できなかったときまた尋ねる。
    var backdrop = dialog.querySelector('[data-art-track-premium-dismiss]');
    if (backdrop) backdrop.addEventListener('click', closeDialog);
    document.addEventListener('keydown', function (ev) {
      if (ev.key === 'Escape' && !dialog.hidden) closeDialog();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.PCDS = window.PCDS || {};
  window.PCDS.artTrackPremium = {
    /** 現在の設定（'show' | 'hide' | null）。 */
    getPreference: getPreference,

    /**
     * Premium 限定の音源が再生できなかったことを再生側から知らせる。
     * まだ一度も意思表示していないときだけ確認ダイアログを出す
     * （「表示する」を選んだ人に毎回尋ねない）。
     */
    notifyBlocked: function () {
      if (getPreference() !== null) return;
      openDialog();
    }
  };
})();
