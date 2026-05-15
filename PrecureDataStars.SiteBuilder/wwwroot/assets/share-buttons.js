/**
 * share-buttons.js — SNS シェアボタンのクライアントサイド挙動を担う（v1.3.1 追加）。
 *
 * 対象 DOM 構造（_share-buttons.sbn 側で生成）：
 *   <aside class="share-buttons">
 *     <ul class="share-buttons-list">
 *       <li><a class="share-button share-button-x" ...>...</a></li>
 *       ...
 *       <li><button class="share-button share-button-copy" data-share-url="...">...</button></li>
 *     </ul>
 *     <div class="share-toast" id="shareToast" hidden>URL をコピーしました</div>
 *   </aside>
 *
 * 本ファイルの責務：
 *   - URL コピーボタン（.share-button-copy）クリックで data-share-url の値をクリップボードへ。
 *   - コピー成功時に .share-toast を一定時間表示。
 *   - navigator.clipboard が使えない環境（古いブラウザ、非 secure context）では
 *     textarea + document.execCommand('copy') にフォールバック。
 *
 * .share-buttons が DOM に無いページ（生成時 ShareUrl が空だった等）では何もせず終了する。
 */
(function () {
  'use strict';

  // 古い Safari 等でも安全に動くよう、DOMContentLoaded を起点に初期化する。
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    // defer で読み込まれている場合、解析時点で DOM はほぼ準備済みのため即時走らせる。
    init();
  }

  function init() {
    // .share-buttons はページに 1 個（_layout.sbn から include で挿入）の想定だが、
    // 念のため複数ヒットしてもすべて結線できるよう forEach で回す。
    var copyButtons = document.querySelectorAll('.share-button-copy');
    if (!copyButtons.length) return;

    copyButtons.forEach(function (btn) {
      btn.addEventListener('click', function (ev) {
        ev.preventDefault();
        var url = btn.getAttribute('data-share-url') || '';
        if (!url) return;
        copyToClipboard(url).then(function (ok) {
          if (ok) showToast();
        });
      });
    });
  }

  /**
   * 文字列をクリップボードへコピーする。
   * モダンブラウザでは navigator.clipboard.writeText を使い、
   * 未対応 or 非 secure context では <textarea> + execCommand へフォールバック。
   * 返り値は Promise<boolean>（true=成功, false=失敗）。
   */
  function copyToClipboard(text) {
    // 第 1 候補：navigator.clipboard（HTTPS 配下では基本的にこちらが通る）。
    if (navigator.clipboard && navigator.clipboard.writeText) {
      try {
        return navigator.clipboard.writeText(text).then(function () {
          return true;
        }, function () {
          // writeText が拒否された場合（権限・フォーカス問題）はフォールバックへ。
          return fallbackCopy(text);
        });
      } catch (e) {
        return Promise.resolve(fallbackCopy(text));
      }
    }
    // 第 2 候補：execCommand 経由のレガシーコピー。
    return Promise.resolve(fallbackCopy(text));
  }

  /**
   * <textarea> を使ったレガシーコピー。
   * 一部のブラウザでは execCommand が deprecated 警告を出すが、
   * navigator.clipboard が使えない環境のフォールバックとして引き続き動作する。
   */
  function fallbackCopy(text) {
    var ta = document.createElement('textarea');
    ta.value = text;
    // 画面外に置いてフォーカスやスクロール位置への影響を避ける。
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '-1000px';
    ta.style.left = '0';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, ta.value.length);
    var ok = false;
    try {
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    document.body.removeChild(ta);
    return ok;
  }

  // トーストの表示時間（ミリ秒）。1800ms 表示 + フェードアウト 200ms。
  var TOAST_VISIBLE_MS = 1800;
  var TOAST_FADE_MS = 200;
  // 多重クリック時のタイマー再設定用に保持する。
  var toastTimer = null;

  /**
   * #shareToast を一定時間表示する。
   * hidden 属性を外して .is-visible クラスを付ければ CSS 側で opacity が 1 に上がる。
   */
  function showToast() {
    var toast = document.getElementById('shareToast');
    if (!toast) return;

    // 既存タイマーがあればクリアし、連打時もリセット動作にする。
    if (toastTimer) {
      clearTimeout(toastTimer);
      toastTimer = null;
    }

    toast.hidden = false;
    // 反映を待ってからクラスを付ける（hidden 解除と同フレームでのトランジション抜け対策）。
    requestAnimationFrame(function () {
      toast.classList.add('is-visible');
    });

    toastTimer = setTimeout(function () {
      toast.classList.remove('is-visible');
      // フェードアウト完了後に hidden に戻して、aria-live のスクリーンリーダ再読を抑止する。
      setTimeout(function () {
        if (!toast.classList.contains('is-visible')) {
          toast.hidden = true;
        }
      }, TOAST_FADE_MS);
    }, TOAST_VISIBLE_MS);
  }
})();
