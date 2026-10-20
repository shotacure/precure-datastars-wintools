/*
 * 人物・ユニットの詳細の、本編クレジット・音楽クレジットの枠（details.credit-card-details）を、はじめから開いておくかどうか決める。
 *
 * 画像や書体を読み終えた後（load）に、次のどちらかに当たる枠を開く。中の枠（枠の中にある details）は対象にしない。
 *   1. 音楽クレジット以外でページが 1 画面に満たないとき、音楽クレジットの枠をすべて開く。
 *      「このページをシェア」より上の本文から、音楽クレジットの節（閉じた状態）の高さを除いた高さが、画面の高さ以下かで決める
 *      （シェア・引用・フッターは数えない）。
 *   2. 開いたときの中身だけの高さが画面の半分に満たない枠は、どこの枠でも開く（単発の出演や数曲だけの枠など）。
 *      全話の出演のように中身が長い枠は閉じたまま。
 */
(function () {
  'use strict';

  // 枠の中にある details（盤の内訳など）を除いた、いちばん外側の枠。
  function topLevelCards() {
    return Array.prototype.filter.call(
      document.querySelectorAll('details.credit-card-details'),
      function (d) { return !d.parentElement.closest('details'); });
  }

  function run() {
    var cards = topLevelCards();
    if (cards.length === 0) return;
    var viewport = window.innerHeight;

    // 1. 音楽クレジット以外でページが 1 画面に満たないとき。
    var music = document.getElementById('music-credits');
    if (music) {
      var share = document.querySelector('.share-buttons');
      var bottom = share ? share.getBoundingClientRect().top + window.scrollY : document.documentElement.scrollHeight;
      if (bottom - music.offsetHeight <= viewport) {
        cards.forEach(function (d) { if (music.contains(d)) d.open = true; });
      }
    }

    // 2. 開いたときの中身だけの高さが画面の半分に満たない枠。一度開いて測り、長ければ閉じ直す。
    cards.forEach(function (d) {
      if (d.open) return;
      var summary = d.querySelector(':scope > summary');
      d.open = true;
      var body = d.offsetHeight - (summary ? summary.offsetHeight : 0);
      if (body >= viewport / 2) d.open = false;
    });
  }

  if (document.readyState === 'complete') {
    run();
  } else {
    window.addEventListener('load', run);
  }
})();
