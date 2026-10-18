/**
 * fonts-viewer.js — フォントのページの見本を押したとき、大きな画像を重ねて出す（<dialog class="fonts-viewer">）。
 *
 * [data-fonts-viewer] の付いたリンクを押すと、リンク先の画像を dialog に入れて showModal で開く。
 * 閉じるボタン・背景の部分を押す・Esc で閉じる（Esc は dialog の標準の動き）。
 * dialog が使えないブラウザでは何もせず、リンクのとおり画像を開く。
 */
(function () {
  'use strict';

  function init() {
    var dialog = document.querySelector('dialog.fonts-viewer');
    if (!dialog || typeof dialog.showModal !== 'function') return;
    var img = dialog.querySelector('img');
    var close = dialog.querySelector('.fonts-viewer-close');

    document.addEventListener('click', function (e) {
      var link = e.target.closest && e.target.closest('a[data-fonts-viewer]');
      if (!link) return;
      e.preventDefault();
      var thumb = link.querySelector('img');
      img.src = link.getAttribute('href');
      img.alt = thumb ? thumb.alt : '';
      dialog.showModal();
    });

    close.addEventListener('click', function () { dialog.close(); });
    // 画像の外（背景の部分）を押したら閉じる
    dialog.addEventListener('click', function (e) {
      if (e.target === dialog) dialog.close();
    });
    dialog.addEventListener('close', function () { img.removeAttribute('src'); });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
