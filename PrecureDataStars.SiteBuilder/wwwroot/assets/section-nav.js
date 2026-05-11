/*
 * section-nav.js
 *
 * 長尺ページに「セクション渡り歩き用」の縦タイムライン型ナビを動的に挿入するクライアントスクリプト
 * （v1.3.0 続編で導入、第 3 弾で左サイド配置 + タイムラインギミックに刷新）。
 *
 * 設計方針
 *   - テンプレ側は <section id="..."> を書くだけ。本 JS が DOM を走査して
 *     <nav id="pageSectionNav"> プレースホルダにアイテム一覧を流し込む。
 *   - ナビの見た目は「○ 円アイコン + 縦線 + ラベル」の縦タイムライン形状。
 *     - 通過済みアイテム（current より上）：中抜き円、グレー線
 *     - 現在地アイテム：塗りつぶし円 + プリキュアピンクの光彩
 *     - 未到達アイテム（current より下）：薄色円
 *     - 縦線の左半分はスクロール進捗ピンクで塗られていく（--progress を JS で更新）
 *   - アイテムのラベルは data-section-nav-label 属性 → 子要素 <h2> のテキスト → id の順に解決。
 *     v1.3.0 続編 第 3 弾以降は data-section-nav-label の利用箇所はサイトには無いが、
 *     仕組みは将来のために残してある（テンプレ側で必要になれば付与すれば効く）。
 *   - 検出セクションが 1 個以下のときはナビ自体を非表示（hidden 属性は付けっぱなしのまま）。
 *   - 既存のタブ UI（products-tabs などの .tab-panel.is-active 構造）と共存させるため、
 *     アクティブな .tab-panel が居れば走査スコープをそれに絞る。タブ切替時は
 *     MutationObserver で再構築する。
 *   - スクロール位置に応じて現在地アイテムへ .is-current、それより上のアイテムへ .is-passed を付与。
 *   - モバイル（≤768px）では左サイドナビが見えないので、ハンバーガーオーバーレイ内の
 *     #mobileSectionNav にも同じアイテム列をミラー描画する（ナビ自体は非表示でもデータは流し込む）。
 *   - スムーススクロールは CSS（html { scroll-behavior: smooth }）に任せる。
 */
(function () {
  'use strict';

  // ナビが描画されるホスト要素。_layout.sbn が必ず 1 つだけ用意している前提。
  var navHost = document.getElementById('pageSectionNav');
  if (!navHost) return;

  // モバイルオーバーレイ内のセクションナビミラー先（無い場合もあり得るので null チェック）。
  var mobileNavMirror = document.getElementById('mobileSectionNav');

  // ナビ走査の起点。タブ UI があるページではアクティブなタブパネル、なければ <main> 全体。
  // products-tabs のように 1 ページに複数 tab-panel がある場合、現状アクティブな
  // .tab-panel.is-active を優先する。タブが見つからないときは main を走査する。
  function getScopeRoot() {
    var activeTabPanel = document.querySelector('.tab-panel.is-active');
    if (activeTabPanel) return activeTabPanel;
    return document.querySelector('main') || document.body;
  }

  // 現在の走査スコープから「id 持ちの section」を順序通りに集める。
  // 例外として「display:none で隠されているセクション」は除外する。
  // ホームの「今日の記念日」など、JS が後付けで非表示にするケースに対応するため。
  //
  // v1.3.0 公開直前のデザイン整理 第 N+1 弾：
  //   - data-section-nav-year（4 桁西暦）と data-section-nav-count（件数）の 2 属性を追加で拾い、
  //     アイテム左側に「年4桁 + 件数バッジ」を出せるようにする。
  //   - 「該当ナビ内で year / count が 1 個でも登場するなら、その列を全アイテムで予約」して
  //     縦線・○ の位置をシフトする（年あり/なしが混在しても縦軸を揃えるため）。
  //     col 予約は collectSections の戻り値とあわせて呼び出し側に伝える。
  function collectSections(scope) {
    var items = [];
    var hasAnyYear = false;
    var hasAnyCount = false;
    var nodes = scope.querySelectorAll('section[id]');
    for (var i = 0; i < nodes.length; i++) {
      var sec = nodes[i];
      var id = sec.getAttribute('id') || '';
      if (!id) continue;

      // 非表示要素は除外。offsetParent が null なら自身か祖先のいずれかが display:none。
      if (sec.offsetParent === null) continue;

      // ラベルの解決：data-section-nav-label > 直下の h2 テキスト > id 文字列
      // テンプレ側の h2 が複数行にわたるケース（録音バージョン見出しなど）に備え、
      // textContent 内の連続空白を 1 個に潰してから trim する。
      var label = sec.getAttribute('data-section-nav-label') || '';
      if (!label) {
        var h2 = sec.querySelector(':scope > h2');
        if (h2) label = (h2.textContent || '').replace(/\s+/g, ' ').trim();
      }
      if (!label) label = id;

      // 任意の補助情報：4 桁西暦 + 件数バッジ。
      // 何も検出できないアイテムは year/count を null で持つ（テンプレ用に空文字を返す）。
      var year = sec.getAttribute('data-section-nav-year') || '';
      var count = sec.getAttribute('data-section-nav-count') || '';
      if (year) hasAnyYear = true;
      if (count) hasAnyCount = true;

      items.push({ id: id, label: label, year: year, count: count, element: sec });
    }
    return { items: items, hasAnyYear: hasAnyYear, hasAnyCount: hasAnyCount };
  }

  // 1 アイテム（タイムラインの 1 行）を生成する。
  // 構造： <li><a><span class="year">…</span><span class="count">…</span><span class="dot"></span><span class="label">…</span></a></li>
  //   - year / count スパンはアイテムごとに有無が異なるが、CSS 側で固定幅（visibility 制御）で
  //     必ず場所を確保するため、縦軸（○の位置）は揃う。
  //   - year / count を 1 つも持たないナビ（hasAnyYear / hasAnyCount が共に false）では、
  //     呼び出し側がコンテナに専用クラスを付けて該当エリアを完全に削除する。
  // 円アイコンは CSS で描画（::before に頼らず実 DOM の span にすることで、
  // クラス切替アニメーションを安定して効かせる）。
  function buildTimelineItem(item) {
    var li = document.createElement('li');
    li.className = 'page-section-nav-item';

    var a = document.createElement('a');
    a.href = '#' + item.id;
    a.setAttribute('data-target-id', item.id);
    // ラベルが長いと CSS の text-overflow で末尾省略されるため、フル文字列を title 属性に保持。
    // ホバー時にツールチップで全部が見えるようにする。
    a.setAttribute('title', item.label);

    // 年エリア（4 桁西暦、等幅）。値が空でも要素を出してレイアウトを揃える。
    var yearSpan = document.createElement('span');
    yearSpan.className = 'page-section-nav-year';
    if (item.year) {
      yearSpan.textContent = item.year;
    } else {
      yearSpan.classList.add('is-empty');
    }

    // 件数バッジ。値が空でも要素を出してレイアウトを揃える。
    var countSpan = document.createElement('span');
    countSpan.className = 'page-section-nav-count';
    if (item.count) {
      countSpan.textContent = item.count;
    } else {
      countSpan.classList.add('is-empty');
    }

    var dot = document.createElement('span');
    dot.className = 'page-section-nav-dot';
    dot.setAttribute('aria-hidden', 'true');

    var label = document.createElement('span');
    label.className = 'page-section-nav-label';
    label.textContent = item.label;

    // ○ の左側に年・バッジを並べる。順序：[年4桁] [バッジ] ○ [ラベル]
    a.appendChild(yearSpan);
    a.appendChild(countSpan);
    a.appendChild(dot);
    a.appendChild(label);
    li.appendChild(a);
    return li;
  }

  // 同じデータをモバイルオーバーレイ側に流し込むための、よりシンプルな <li><a>…</a></li>。
  // 縦タイムライン装飾は付けず、グローバルナビと同列のリスト項目として描画する。
  function buildMobileMirrorItem(item) {
    var li = document.createElement('li');
    var a = document.createElement('a');
    a.href = '#' + item.id;
    a.setAttribute('data-target-id', item.id);
    a.textContent = item.label;
    li.appendChild(a);
    return li;
  }

  // 現在地ハイライトを切り替える。引数 id に該当するアイテムを .is-current にし、
  // それより上のアイテムを .is-passed（通過済み、中抜き円表示）、下のアイテムを素の状態にする。
  function setCurrent(items, id) {
    var found = false;
    for (var i = 0; i < items.length; i++) {
      var li = items[i].itemEl;
      if (!li) continue;
      if (items[i].id === id) {
        li.classList.add('is-current');
        li.classList.remove('is-passed');
        li.querySelector('a').setAttribute('aria-current', 'true');
        found = true;
      } else if (!found) {
        // current より前 = 既に通過したアイテム
        li.classList.remove('is-current');
        li.classList.add('is-passed');
        li.querySelector('a').removeAttribute('aria-current');
      } else {
        // current より後 = 未到達
        li.classList.remove('is-current');
        li.classList.remove('is-passed');
        li.querySelector('a').removeAttribute('aria-current');
      }
    }
  }

  // 描画済みのアイテムに対する IntersectionObserver / scroll listener の解除用ハンドル。
  var currentObserver = null;
  var scrollProgressHandler = null;

  // ナビ本体を再構築する。タブ切替時にも呼び出される。
  function rebuild() {
    var scope = getScopeRoot();
    var collected = collectSections(scope);
    var items = collected.items;

    // セクションが 1 個以下のページではナビを完全に隠す。hidden 属性を立てて
    // CSS の display:none を効かせる（aria 的にもナビ自体が存在しない扱い）。
    if (items.length < 2) {
      navHost.setAttribute('hidden', '');
      navHost.innerHTML = '';
      if (mobileNavMirror) {
        mobileNavMirror.setAttribute('hidden', '');
        var ul = mobileNavMirror.querySelector('ul');
        if (ul) ul.innerHTML = '';
      }
      if (currentObserver) {
        currentObserver.disconnect();
        currentObserver = null;
      }
      if (scrollProgressHandler) {
        window.removeEventListener('scroll', scrollProgressHandler);
        scrollProgressHandler = null;
      }
      return;
    }

    // ナビ枠を組み立て。アクセシビリティのため aria-label 付与。
    navHost.removeAttribute('hidden');
    navHost.setAttribute('aria-label', 'ページ内セクションナビ');
    navHost.setAttribute('role', 'navigation');
    // 年エリア・件数バッジエリアの予約は「ナビ内に 1 個でも該当属性がある」ときのみ。
    // CSS は .has-year / .has-count クラスを見て該当列の場所を確保する（visibility:hidden で
    // 値が無いアイテムでも幅は維持され、縦軸＝○の位置が揃う）。
    navHost.classList.toggle('has-year', collected.hasAnyYear);
    navHost.classList.toggle('has-count', collected.hasAnyCount);

    // 内側の見出し（スクリーンリーダーには見出しを与えつつ、視覚的には目立たせない）。
    // 同時に縦タイムラインを格納する ol。
    var inner = document.createElement('div');
    inner.className = 'page-section-nav-inner';

    var heading = document.createElement('div');
    heading.className = 'page-section-nav-heading';
    heading.textContent = 'このページ';
    inner.appendChild(heading);

    // タイムライン本体。ol で順序を意味付け。
    var list = document.createElement('ol');
    list.className = 'page-section-nav-list';

    for (var i = 0; i < items.length; i++) {
      var li = buildTimelineItem(items[i]);
      items[i].itemEl = li;
      list.appendChild(li);
    }
    inner.appendChild(list);

    navHost.innerHTML = '';
    navHost.appendChild(inner);

    // モバイルミラー：オーバーレイ内のリストに同じアイテム列を流し込む。
    if (mobileNavMirror) {
      var mobileUl = mobileNavMirror.querySelector('ul');
      if (mobileUl) {
        mobileUl.innerHTML = '';
        for (var mi = 0; mi < items.length; mi++) {
          mobileUl.appendChild(buildMobileMirrorItem(items[mi]));
        }
        mobileNavMirror.removeAttribute('hidden');
      }
    }

    // スクロールスパイ：IntersectionObserver で「視界に入っているセクションのうち
    // ドキュメント順で最上位のもの」を current として採用する。
    // v1.3.0 続編：モバイル（≤768px）では site-header が非固定でスクロール時に画面外へ
    // 消えるため、上端オフセットはセクションナビ自身の高さ分だけで足りる。
    // デスクトップでは固定ヘッダ（≈ 56px）+ 余白を見越して 80px。
    if (currentObserver) currentObserver.disconnect();

    var isMobile = window.matchMedia('(max-width: 768px)').matches;
    var topOffsetPx = isMobile ? 30 : 80;

    // 視界に居るセクション id の集合。閉包で IO コールバックから参照する。
    var lastVisibleSet = {};

    currentObserver = new IntersectionObserver(function (entries) {
      for (var j = 0; j < entries.length; j++) {
        var e = entries[j];
        var id = e.target.getAttribute('id') || '';
        if (!id) continue;
        if (e.isIntersecting) {
          lastVisibleSet[id] = true;
        } else {
          delete lastVisibleSet[id];
        }
      }

      // ドキュメント順で最初に見える id を選ぶ。
      var pickedId = null;
      for (var k = 0; k < items.length; k++) {
        if (lastVisibleSet[items[k].id]) {
          pickedId = items[k].id;
          break;
        }
      }

      // どれも視界に無いときは最後のハイライトを維持する。
      if (pickedId) setCurrent(items, pickedId);
    }, {
      // 上端は固定ヘッダぶんを差し引く。下端は画面上半分以内のセクションを優先するため -55%。
      rootMargin: '-' + topOffsetPx + 'px 0px -55% 0px',
      threshold: 0
    });

    for (var m = 0; m < items.length; m++) {
      currentObserver.observe(items[m].element);
    }

    // 初期ハイライト：URL に #fragment があればそれを、無ければ最初のセクションを current にする。
    var initialId = (location.hash || '').replace(/^#/, '');
    if (!initialId || !items.some(function (it) { return it.id === initialId; })) {
      initialId = items[0].id;
    }
    setCurrent(items, initialId);

    // スクロール進捗ゲージ：ナビの縦線左半分を、現在のスクロール位置に応じて
    // プリキュアピンクで塗っていく。リスト内 CSS 変数 --progress（0〜100%）を JS で更新し、
    // CSS の linear-gradient がそれに応じて伸縮する。
    // 進捗の定義：最初のセクションの top を 0%、最後のセクションの top + height を 100% とし、
    // スクロール位置（viewport top + ヘッダオフセット）の比率を求める。
    if (scrollProgressHandler) {
      window.removeEventListener('scroll', scrollProgressHandler);
    }
    var rafScheduled = false;
    scrollProgressHandler = function () {
      if (rafScheduled) return;
      rafScheduled = true;
      window.requestAnimationFrame(function () {
        rafScheduled = false;
        var firstSec = items[0].element;
        var lastSec  = items[items.length - 1].element;
        var firstTop = firstSec.getBoundingClientRect().top + window.scrollY;
        var lastRect = lastSec.getBoundingClientRect();
        var lastBottom = lastRect.top + window.scrollY + lastRect.height;
        var range = Math.max(1, lastBottom - firstTop);
        var pos   = (window.scrollY + topOffsetPx) - firstTop;
        var pct   = Math.max(0, Math.min(100, (pos / range) * 100));
        list.style.setProperty('--progress', pct.toFixed(2) + '%');
      });
    };
    window.addEventListener('scroll', scrollProgressHandler, { passive: true });
    // 初期値も計算しておく。
    scrollProgressHandler();
  }

  // 起動：DOM 構築済みで即座に走らせる。defer 読み込み前提なので DOMContentLoaded は
  // 既に発火しているのが普通。
  rebuild();

  // タブ切り替えの検知：.tab-panel.is-active クラスの変動を観測する。
  // products-tabs 等は 1 ページに複数の .tab-panel を持ち、JS が is-active を付け替える。
  // 子孫の class 属性変化を全体監視するのは負荷が気になるので、tab-panel 群に限定する。
  var tabPanels = document.querySelectorAll('.tab-panel');
  if (tabPanels.length > 0) {
    var tabObserver = new MutationObserver(function () {
      rebuild();
    });
    for (var n = 0; n < tabPanels.length; n++) {
      tabObserver.observe(tabPanels[n], {
        attributes: true,
        attributeFilter: ['class', 'hidden']
      });
    }
  }

  // モバイル / デスクトップの境界（768px）を跨いだら IntersectionObserver の rootMargin を
  // 適切な値に組み直す必要があるため、ブレークポイント遷移をフックして rebuild する。
  // resize 連打を避けるため matchMedia.change のみを使う（resize イベント自体は無視）。
  var breakpointMq = window.matchMedia('(max-width: 768px)');
  function onBreakpointChange() {
    rebuild();
  }
  if (breakpointMq.addEventListener) {
    breakpointMq.addEventListener('change', onBreakpointChange);
  } else if (breakpointMq.addListener) {
    breakpointMq.addListener(onBreakpointChange);
  }
})();
