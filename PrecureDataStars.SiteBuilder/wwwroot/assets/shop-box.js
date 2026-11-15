/**
 * shop-box.js — 作品詳細・各話の詳細・トップページの「販売中の商品」の枠を描く。
 *
 * 対象 DOM 構造（テンプレート側は空の置き場だけを出す）：
 *   <aside class="shop-box" data-shop-series="69" data-shop-heading="この作品の Blu-ray・CD" hidden></aside>
 *   data-shop-series … 作品の ID。トップページは "home"。
 *
 * 枠の中身は /shop/{data-shop-series の値}.json から読む。このファイルは、Amazon で売られている商品だけを
 * 最大 4 件載せていて、売り切れ・絶版になった商品はすぐ外れ、また売られ始めるとすぐ戻る。
 * 出す商品が無ければ枠ごと出さない。
 * 端末には何も保存しない（ブラウザの通常のキャッシュだけを使う）。
 *
 * フッターの Amazon についての注記（data-shop-footnote）は、枠を出したときだけ見せる。
 * 置き場の無いページでは何もせず終了する。
 */
(function () {
  'use strict';

  var FILE_DIR = '/shop/';

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  function init() {
    var boxes = document.querySelectorAll('.shop-box[data-shop-series]');
    if (boxes.length === 0) return;

    for (var i = 0; i < boxes.length; i++) load(boxes[i]);
  }

  /** 置き場 1 つのファイルを読んで描く。読めなかったときは、枠を出さないままにする。 */
  function load(box) {
    var name = box.getAttribute('data-shop-series');
    if (!/^[0-9a-z]+$/.test(name)) return;
    fetch(FILE_DIR + name + '.json')
      .then(function (res) { return res.ok ? res.json() : Promise.reject(new Error('HTTP ' + res.status)); })
      .then(function (data) {
        render(box, (data && data.items) || []);
        showFootnotes();
      })
      .catch(function () {});
  }

  /** フッターの Amazon についての注記を、枠が 1 つでも出ているときだけ見せる。 */
  function showFootnotes() {
    var anyShown = document.querySelector('.shop-box[data-shop-series]:not([hidden])') !== null;
    var notes = document.querySelectorAll('[data-shop-footnote]');
    for (var j = 0; j < notes.length; j++) notes[j].hidden = !anyShown;
  }

  /** 置き場 1 つを描く。出す商品があれば true。 */
  function render(box, mine) {
    box.textContent = '';
    if (mine.length === 0) {
      box.hidden = true;
      return false;
    }

    var heading = document.createElement('p');
    heading.className = 'shop-box-heading';
    heading.textContent = box.getAttribute('data-shop-heading') || '';
    box.appendChild(heading);

    var list = document.createElement('ul');
    list.className = 'shop-box-list';
    for (var k = 0; k < mine.length; k++) list.appendChild(buildItem(mine[k]));
    box.appendChild(list);

    // 購入のリンクは Amazon アソシエイトの広告なので、枠の中で広告である旨を示す。
    var note = document.createElement('p');
    note.className = 'muted product-links-note shop-box-note';
    note.textContent = box.getAttribute('data-shop-note') || '';
    box.appendChild(note);

    box.hidden = false;
    return true;
  }

  /**
   * 商品 1 つのカード。どの商品も「画像の場所・札と状態・商品名・下端のボタンの行」の同じ骨組みにして、高さと位置をそろえる。
   * カード全体を Amazon へのリンクにしつつ、中に商品詳細へのリンクも置くので、リンクは入れ子にせず、
   * 透明なリンクをカード全体に重ねる。
   */
  function buildItem(item) {
    var li = document.createElement('li');
    li.className = 'shop-box-item';

    var link = document.createElement('a');
    link.className = 'shop-box-link';
    link.href = item.url;
    link.target = '_blank';
    link.rel = 'nofollow sponsored noopener';
    link.setAttribute('aria-label', item.title + '（Amazon で見る）');
    li.appendChild(link);

    // 画像の場所は、画像がまだ無い商品でも同じ大きさで取る。
    var thumb = document.createElement('span');
    thumb.className = 'shop-box-thumb';
    if (item.image) {
      var img = document.createElement('img');
      img.src = item.image;
      img.alt = '';
      img.loading = 'lazy';
      img.decoding = 'async';
      thumb.appendChild(img);
    }
    li.appendChild(thumb);

    var body = document.createElement('div');
    body.className = 'shop-box-body';

    var meta = document.createElement('div');
    meta.className = 'shop-box-meta';
    if (item.media) meta.appendChild(span('shop-box-media', item.media));
    var status = statusText(item);
    if (status) meta.appendChild(span('shop-box-status', status));
    body.appendChild(meta);

    body.appendChild(span('shop-box-title', item.title));

    var foot = document.createElement('div');
    foot.className = 'shop-box-foot';
    foot.appendChild(span('shop-box-cta', 'Amazon で見る'));
    // 音楽商品は、収録内容の分かる商品詳細へのリンクを添える。
    if (item.detail) {
      var detail = document.createElement('a');
      detail.className = 'shop-box-detail';
      detail.href = item.detail;
      detail.textContent = '収録内容を見る';
      foot.appendChild(detail);
    }
    body.appendChild(foot);

    li.appendChild(body);
    return li;
  }

  /** 予約受付中なら「予約受付中・2027年1月27日発売」、在庫ありなら「発売中」。 */
  function statusText(item) {
    if (item.status === 'PREORDER') {
      var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(item.release || '');
      if (!m) return '予約受付中';
      return '予約受付中・' + Number(m[1]) + '年' + Number(m[2]) + '月' + Number(m[3]) + '日発売';
    }
    if (item.status === 'IN_STOCK' || item.status === 'IN_STOCK_SCARCE') return '発売中';
    return '';
  }

  function span(className, text) {
    var el = document.createElement('span');
    el.className = className;
    el.textContent = text;
    return el;
  }
})();
