/**
 * share-buttons.js — SNS シェアボタンのクライアントサイド挙動を担う。
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
 *   - 今のアドレスに # の後ろ（並べ替え・絞り込み・タブの状態や節へのアンカー。view-state.js が書く）があれば、
 *     共有ボタンの行き先と「URL をコピー」の URL に付ける。ボタンの行き先は、押す直前（押し始め・フォーカス・
 *     右クリック・クリック）に、テンプレートが埋めたページの URL を「ページの URL＋今の #」に差し替える
 *     （埋めたときの符号化の形〔RFC 3986〕に合わせる。はてなブックマークは URL をパスに埋めるので # を %23 にする）。
 *     # が無いときはテンプレートが埋めたままの行き先に戻す。共有先のカード（OGP）は変わらない。
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
        copyToClipboard(withHash(url)).then(function (ok) {
          if (ok) showToast();
        });
      });
    });

    // 共有ボタンの行き先に今の # を付ける（押す直前に作り直す）。
    document.querySelectorAll('.share-buttons').forEach(function (aside) {
      var copy = aside.querySelector('.share-button-copy');
      var base = copy ? copy.getAttribute('data-share-url') || '' : '';
      if (!base) return;
      var links = Array.prototype.slice.call(aside.querySelectorAll('a.share-button[href]'));
      links.forEach(function (a) { a.setAttribute('data-share-href', a.getAttribute('href')); });
      function refresh() {
        var url = withHash(base);
        links.forEach(function (a) {
          var orig = a.getAttribute('data-share-href');
          if (url === base) { a.setAttribute('href', orig); return; }
          var next = orig.split(rfc3986(base)).join(rfc3986(url));
          var bare = base.replace(/^https?:\/\//, '');
          if (next === orig && orig.indexOf('/entry/' + bare) >= 0) {
            // はてなブックマーク（URL をパスに埋める）。# は %23 にして、後ろは符号化する。
            next = orig.split('/entry/' + bare).join('/entry/' + bare + '%23' + rfc3986(url.slice(url.indexOf('#') + 1)));
          }
          a.setAttribute('href', next);
        });
      }
      ['pointerdown', 'focusin', 'contextmenu', 'click'].forEach(function (type) {
        aside.addEventListener(type, refresh, true);
      });
    });
  }

  /** 今のアドレスの # の後ろ（あれば）を URL に付ける。 */
  function withHash(url) {
    var h = location.hash || '';
    return h.length > 1 ? url.split('#')[0] + h : url;
  }

  /** encodeURIComponent に、RFC 3986 で予約された !'()* の符号化を足したもの（テンプレートの html.url_encode と同じ形）。 */
  function rfc3986(s) {
    return encodeURIComponent(s).replace(/[!'()*]/g, function (c) {
      return '%' + c.charCodeAt(0).toString(16).toUpperCase();
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
