/*
 * credit-align.js
 *
 * エピソード詳細クレジットセクション内で、各テーブルの「役職名カラム」「キャラ名カラム」の
 * 幅をセクション横断で統一するクライアントスクリプト
 * （v1.3.0 公開直前のデザイン整理 第 N+2 弾で導入）。
 *
 * 背景
 *   クレジット階層は「カード > ティア > グループ > ロール > ブロック > エントリ」と入れ子になり、
 *   各ロールが独立した <table class="fallback-table"> または <table class="fallback-vc-table"> を
 *   生成する。それぞれの table のカラムは内容に応じて shrink-wrap されるため、
 *   隣接ロール間で「役職名カラム」「キャラ名カラム」の右端位置がガタついて見える。
 *
 * 解決方針
 *   .credit-section 配下のすべての <td class="role-name"> を走査して最大の自然幅を測定し、
 *   その値を CSS 変数 --credit-role-name-w に設定する。table 側の CSS は
 *   .role-name { width: var(--credit-role-name-w, ...); } で参照する。
 *   .character-cell も同様に --credit-character-cell-w で揃える。
 *
 * 計測の落とし穴対策
 *   - getBoundingClientRect は border-collapse な table セルでも実寸 px を返してくれる。
 *   - cell の min-width / padding 込みの「中身が必要とする幅」を取りたいので、
 *     scrollWidth ではなく一時的に幅制約を外して clientWidth を測る方法を採る。
 *     ただし今回は最初に「自然幅で並んだ最初の描画」が完了したタイミング（DOMContentLoaded
 *     + フォント読み込み完了）に走るため、getBoundingClientRect().width で十分。
 *   - フォント読み込みで幅が変わる可能性があるため、document.fonts.ready を待ってから走らせる。
 *   - ResizeObserver で .credit-section 自体の幅変化（ウィンドウサイズ変更等）を検知し、
 *     必要なら再計測する。再計測時には変数を一旦クリアして自然幅に戻してから測り直す。
 *
 * 適用範囲
 *   このスクリプトは .credit-section が DOM に存在するページのみで動く（無いページでは何もしない）。
 *   エピソード詳細以外でクレジット階層が出るページ（持っていないが将来的に出る場合）にも、
 *   .credit-section クラスがあれば自動的に効く。
 */
(function () {
  'use strict';

  // 1 つの .credit-section に対して、配下の .role-name / .character-cell の最大幅を測定し、
  // CSS 変数として section 自身の style 属性に書き込む。
  function alignOne(section) {
    // まず変数を空に戻して「自然幅」を取り直す（再計測時に古い値で固定されるのを防ぐ）。
    section.style.removeProperty('--credit-role-name-w');
    section.style.removeProperty('--credit-character-cell-w');

    // 役職名カラム：両 fallback テーブルで .role-name を使うので 1 セレクタで拾える。
    // 空セル（声の出演 2 行目以降の role-name 抑止行）は 0 幅扱いで構わない。
    var roleNameMax = 0;
    var roleNameCells = section.querySelectorAll('td.role-name');
    for (var i = 0; i < roleNameCells.length; i++) {
      var w = roleNameCells[i].getBoundingClientRect().width;
      if (w > roleNameMax) roleNameMax = w;
    }

    // キャラ名カラム：fallback-vc-table 配下のみ。協力行も .character-cell を持つので含まれる。
    // colspan="2" になっている leading-company 行も拾うが、その分の幅は無視したいので
    // colspan を持つセルは計測から除外する。
    var charCellMax = 0;
    var charCells = section.querySelectorAll('td.character-cell');
    for (var j = 0; j < charCells.length; j++) {
      if (charCells[j].hasAttribute('colspan')) continue;
      var w2 = charCells[j].getBoundingClientRect().width;
      if (w2 > charCellMax) charCellMax = w2;
    }

    // 端数を 1px 切り上げて確実にテキストが収まるようにする。
    if (roleNameMax > 0) {
      section.style.setProperty('--credit-role-name-w', Math.ceil(roleNameMax) + 'px');
    }
    if (charCellMax > 0) {
      section.style.setProperty('--credit-character-cell-w', Math.ceil(charCellMax) + 'px');
    }
  }

  function alignAll() {
    var sections = document.querySelectorAll('.credit-section');
    for (var i = 0; i < sections.length; i++) {
      alignOne(sections[i]);
    }
  }

  // フォント読み込み待ち（Kiwi Maru が遅れて適用されるとセル幅が変わるため）。
  // document.fonts が無い古い環境は無視（その場合は DOMContentLoaded 直後の値で固定）。
  function init() {
    if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === 'function') {
      document.fonts.ready.then(function () {
        alignAll();
        setupResizeObserver();
      });
    } else {
      alignAll();
      setupResizeObserver();
    }
  }

  // ウィンドウサイズ変更や、内部のセクションナビ／タブ切替などで .credit-section が再レイアウトされる
  // ケースに備えて ResizeObserver を仕掛ける。サポート外の古い環境では window.resize で代替。
  // 連続リサイズ中に過剰に走らないよう、簡易デバウンス（rAF + flag）で抑制する。
  var rafScheduled = false;
  function scheduleAlign() {
    if (rafScheduled) return;
    rafScheduled = true;
    window.requestAnimationFrame(function () {
      rafScheduled = false;
      alignAll();
    });
  }

  function setupResizeObserver() {
    var sections = document.querySelectorAll('.credit-section');
    if (sections.length === 0) return;
    if (typeof ResizeObserver === 'function') {
      var ro = new ResizeObserver(scheduleAlign);
      for (var i = 0; i < sections.length; i++) ro.observe(sections[i]);
    } else {
      window.addEventListener('resize', scheduleAlign, { passive: true });
    }
  }

  // defer 読み込み前提だが、念のため readyState もハンドリング。
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
  } else {
    init();
  }
})();
