/**
 * cite-box.js — 「このページを引用する」ボックス（_cite-box.sbn）のクライアントサイド挙動を担う。
 *
 * 対象 DOM 構造（_cite-box.sbn 側で生成）：
 *   <aside class="cite-box">
 *     <pre class="cite-box-text" id="citeTextPlain">…, (参照 <span data-cite-date="iso">2026-10-02</span>).</pre>
 *     <button class="cite-box-copy" data-cite-copy="citeTextPlain">…</button>
 *     <pre class="cite-box-text" id="citeTextCiteWeb">… |accessdate=<span data-cite-date="iso">2026-10-02</span> …</pre>
 *     <button class="cite-box-copy" data-cite-copy="citeTextCiteWeb">…</button>
 *   </aside>
 *
 * 本ファイルの責務：
 *   - 引用文の参照日（data-cite-date）を、ビルド日の既定値から閲覧者の「今日」に差し替える
 *     （"iso" は「2026-10-02」、"ja" は「2026年10月2日」）。
 *   - コピーボタン（data-cite-copy）クリックで、指した <pre> のテキストをクリップボードへ。
 *   - コピー成功時に share-buttons.js と同じ見た目のトースト（.share-toast）を一定時間表示する。
 *     シェアボタンのトースト（#shareToast）があればそれを借り、無ければ自前で 1 つ作る。
 *   - navigator.clipboard が使えない環境では textarea + document.execCommand('copy') にフォールバック。
 *
 * .cite-box が DOM に無いページ（ShareUrl が空だった等）では何もせず終了する。
 */
(function () {
  'use strict';

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  var toastTimer = null;

  function pad2(n) {
    return n < 10 ? '0' + n : String(n);
  }

  function init() {
    var box = document.querySelector('.cite-box');
    if (!box) return;

    // 閲覧日（閲覧者のローカル時刻）。
    var now = new Date();
    var iso = now.getFullYear() + '-' + pad2(now.getMonth() + 1) + '-' + pad2(now.getDate());
    var ja = now.getFullYear() + '年' + (now.getMonth() + 1) + '月' + now.getDate() + '日';
    var dates = box.querySelectorAll('[data-cite-date]');
    for (var i = 0; i < dates.length; i++) {
      dates[i].textContent = dates[i].getAttribute('data-cite-date') === 'iso' ? iso : ja;
    }

    var buttons = box.querySelectorAll('[data-cite-copy]');
    for (var j = 0; j < buttons.length; j++) {
      buttons[j].addEventListener('click', onCopyClick);
    }
  }

  function onCopyClick(ev) {
    var btn = ev.currentTarget;
    var target = document.getElementById(btn.getAttribute('data-cite-copy'));
    if (!target) return;
    // <pre> 内の改行・連続空白は見た目の都合なので、1 行に畳んでからコピーする。
    var text = (target.textContent || '').replace(/\s+/g, ' ').trim();
    copyText(text, function () {
      showToast('引用文をコピーしました');
    });
  }

  function copyText(text, onDone) {
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(onDone, function () {
        fallbackCopy(text, onDone);
      });
      return;
    }
    fallbackCopy(text, onDone);
  }

  function fallbackCopy(text, onDone) {
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '-1000px';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    var ok = false;
    try {
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    document.body.removeChild(ta);
    if (ok) onDone();
  }

  function showToast(message) {
    var toast = document.getElementById('shareToast');
    if (!toast) {
      toast = document.createElement('div');
      toast.className = 'share-toast';
      toast.id = 'citeToast';
      toast.setAttribute('role', 'status');
      toast.setAttribute('aria-live', 'polite');
      document.body.appendChild(toast);
    }
    toast.textContent = message;
    toast.hidden = false;
    // hidden 解除と同じフレームでクラスを付けるとトランジションが抜けるので、1 フレーム待つ。
    requestAnimationFrame(function () {
      toast.classList.add('is-visible');
    });
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(function () {
      toast.classList.remove('is-visible');
      // フェードアウトを待ってから hidden に戻す。
      setTimeout(function () {
        if (toast.classList.contains('is-visible')) return;
        toast.hidden = true;
        // シェアボタンのトーストを借りたときは、元の文言に戻しておく。
        if (toast.id === 'shareToast') toast.textContent = 'URL をコピーしました';
      }, 250);
    }, 1800);
  }
})();
