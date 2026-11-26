/*
 * YouTube の公式動画の置き場（.yt-facade。_youtube-facade.sbn が出す）。
 *
 * 最初はサムネイルと再生ボタンだけを出しておき、再生ボタンが押されたら、その場で埋め込みのプレイヤー
 * （https://www.youtube.com/embed/<ID>?autoplay=1）の iframe に差し替える。
 * プレイヤーの JS は 1 本で約 1MB あり、動画の多いページ（作品詳細など）では開いただけで数 MB になるため、
 * 押されるまで読まない。
 * ・動画 ID は data-yt-id、iframe の title は data-yt-title から取る。
 * ・差し替えた iframe にフォーカスを移し、キーボードでもそのまま操作できるようにする。
 * ・押した後のプレイヤーは置き場ごと残し、もとのサムネイルには戻さない。
 */
(function () {
  'use strict';

  function play(box) {
    var id = box.getAttribute('data-yt-id');
    if (!id || box.classList.contains('is-playing')) return;
    var iframe = document.createElement('iframe');
    iframe.src = 'https://www.youtube.com/embed/' + encodeURIComponent(id) + '?autoplay=1';
    iframe.title = box.getAttribute('data-yt-title') || '';
    iframe.width = '560';
    iframe.height = '315';
    iframe.setAttribute('frameborder', '0');
    iframe.setAttribute('allow', 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture');
    iframe.setAttribute('allowfullscreen', '');
    iframe.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin');
    var button = box.querySelector('.yt-facade-play');
    if (button) box.removeChild(button);
    box.appendChild(iframe);
    box.classList.add('is-playing');
    iframe.focus();
  }

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('.yt-facade-play');
    if (!button) return;
    var box = button.closest('.yt-facade');
    if (box) play(box);
  });
})();
