/**
 * release-countdown.js — ホームの商品・書籍カードの「発売まで N 日」と状態バッジを、閲覧日基準で更新する。
 *
 * 対象 DOM 構造（home.sbn 側で生成）：
 *   <article class="home-product-card" data-release-date="2026-10-21">
 *     … <span class="home-product-card-badge home-product-card-badge-upcoming">予約受付中</span> …
 *     … <span class="home-product-card-countdown">発売まであと 19 日</span> …
 *   </article>
 *
 * サーバ側（HomeGenerator）はビルド日基準の文言を初期表示として埋めている。ビルドの間隔が空くと
 * 日数が止まり、発売日を過ぎても「予約受付中」のままになるので、閲覧時にここで計算し直す。
 * 決まりはサーバ側と同じ：
 *   - 発売日が未来 … バッジ「予約受付中」（upcoming の配色）、翌日なら「明日発売」、それ以外は「発売まであと N 日」
 *   - 発売日当日 … バッジ「本日発売」（released の配色）、カウントダウンなし
 *   - 発売から 7 日以内 … バッジ「発売中」（released の配色）、カウントダウンなし
 *   - それより前 … バッジもカウントダウンも出さない
 * バッジとカウントダウンの要素は中身が空なら CSS（:empty）で消える。
 *
 * data-release-date を持つ要素が無いページでは何もせず終了する。
 */
(function () {
  'use strict';

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  function init() {
    var cards = document.querySelectorAll('[data-release-date]');
    if (cards.length === 0) return;

    var now = new Date();
    var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

    for (var i = 0; i < cards.length; i++) {
      update(cards[i], today);
    }
  }

  function update(card, today) {
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(card.getAttribute('data-release-date') || '');
    if (!m) return;
    var release = new Date(parseInt(m[1], 10), parseInt(m[2], 10) - 1, parseInt(m[3], 10));
    // 日付同士の差（ローカル時刻の深夜 0 時基準）。夏時間の無い日本では 24 時間割りで足りるが、念のため四捨五入する。
    var diffDays = Math.round((release.getTime() - today.getTime()) / 86400000);

    var badgeText = '';
    var badgeUpcoming = false;
    var countdownText = '';
    if (diffDays > 0) {
      badgeText = '予約受付中';
      badgeUpcoming = true;
      countdownText = diffDays === 1 ? '明日発売' : '発売まであと ' + diffDays + ' 日';
    } else if (diffDays >= -7) {
      badgeText = diffDays === 0 ? '本日発売' : '発売中';
    }

    var badge = card.querySelector('.home-product-card-badge');
    if (badge) {
      badge.textContent = badgeText;
      badge.classList.toggle('home-product-card-badge-upcoming', badgeText !== '' && badgeUpcoming);
      badge.classList.toggle('home-product-card-badge-released', badgeText !== '' && !badgeUpcoming);
    }
    var countdown = card.querySelector('.home-product-card-countdown');
    if (countdown) {
      countdown.textContent = countdownText;
    }
  }
})();
