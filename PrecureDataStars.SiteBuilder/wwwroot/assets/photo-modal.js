// 写真（figure.photo）をクリックすると、画面いっぱいに拡大して見せる。
// 閉じるのは、もう一度クリック・閉じるボタン・Esc。JS が動かないときは、リンクで元の画像が開く。
(function () {
  var links = document.querySelectorAll("figure.photo a.photo-link");
  if (!links.length || !window.HTMLDialogElement) return;

  var dialog = document.createElement("dialog");
  dialog.className = "photo-modal";
  dialog.setAttribute("aria-label", "写真の拡大表示");
  dialog.innerHTML =
    '<button type="button" class="photo-modal-close" aria-label="閉じる">×</button>' +
    '<img class="photo-modal-img" alt="">' +
    '<p class="photo-modal-caption"></p>';
  document.body.appendChild(dialog);

  var img = dialog.querySelector(".photo-modal-img");
  var caption = dialog.querySelector(".photo-modal-caption");

  function close() { if (dialog.open) dialog.close(); }

  // 写真の上・余白・閉じるボタンのどこを押しても閉じる。
  dialog.addEventListener("click", close);

  for (var i = 0; i < links.length; i++) {
    links[i].addEventListener("click", function (e) {
      // 新しいタブで開く操作（Ctrl・Shift・中クリックなど）は、そのままにする。
      if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
      e.preventDefault();
      var thumb = this.querySelector("img");
      var cap = this.parentNode.querySelector("figcaption");
      img.src = this.getAttribute("href");
      img.alt = thumb ? thumb.alt : "";
      caption.textContent = cap ? cap.textContent : "";
      caption.hidden = !cap;
      dialog.showModal();
    });
  }
})();
