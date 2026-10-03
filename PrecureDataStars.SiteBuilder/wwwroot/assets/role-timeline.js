/* 年表タブの「担当（出演・参加）の移り変わり」（線表。役職詳細・声の出演・歌唱）。
   行にマウスを載せると（タッチでは帯の部分をタップすると）参加期間と作品ごとの参加を出す。
   内訳は行の data-sub（名前の下に添える 1 行。キャラクターの CV など）・data-period（参加期間）・
   data-works（作品ごとの参加。改行区切り）から組み立てる。名前にキーボードのフォーカスが来たときも行の下に出す。
   「主な方のみ」のスイッチ（.rtl-filter）は、切ると主な方でない行（.rtl-row-extra）も見せ、横の数を
   「主な方 / 全員」で出す（個人・団体の絞り込みで隠れている行は数えない）。? はマウスを載せる・タップする・
   フォーカスすると主な方の条件の吹き出しを出す。 */
(function () {
  document.querySelectorAll('.role-timeline').forEach(function (sec) {
    var tl = sec.querySelector('.rtl');
    if (!tl) return;
    setupFilter(sec);

    var tip = document.createElement('div');
    tip.className = 'rtl-tip';
    tip.hidden = true;
    tip.setAttribute('role', 'tooltip');
    document.body.appendChild(tip);
    var cur = null;

    function fill(row) {
      if (cur === row) return;
      if (cur) cur.classList.remove('is-tip');
      cur = row;
      row.classList.add('is-tip');
      tip.textContent = '';
      var nameEl = row.querySelector('.rtl-name-text');
      var name = document.createElement('div');
      name.className = 'rtl-tip-name';
      name.textContent = nameEl ? nameEl.textContent : '';
      tip.appendChild(name);
      var subText = row.getAttribute('data-sub');
      if (subText) {
        var sub = document.createElement('div');
        sub.className = 'rtl-tip-sub';
        sub.textContent = subText;
        tip.appendChild(sub);
      }
      var period = document.createElement('div');
      period.className = 'rtl-tip-period';
      period.textContent = row.getAttribute('data-period') || '';
      tip.appendChild(period);
      var ul = document.createElement('ul');
      ul.className = 'rtl-tip-works';
      (row.getAttribute('data-works') || '').split('\n').forEach(function (w) {
        if (!w) return;
        var li = document.createElement('li');
        li.textContent = w;
        ul.appendChild(li);
      });
      tip.appendChild(ul);
      tip.hidden = false;
    }
    function place(x, y) {
      var r = tip.getBoundingClientRect();
      var left = x + 16, top = y + 16;
      if (left + r.width > window.innerWidth - 8) left = Math.max(8, x - r.width - 12);
      if (top + r.height > window.innerHeight - 8) top = Math.max(8, y - r.height - 12);
      tip.style.left = left + 'px';
      tip.style.top = top + 'px';
    }
    function hide() {
      if (cur) cur.classList.remove('is-tip');
      cur = null;
      tip.hidden = true;
    }

    tl.addEventListener('pointermove', function (ev) {
      if (ev.pointerType === 'touch') return;
      var row = ev.target.closest('.rtl-row');
      if (!row) { hide(); return; }
      fill(row);
      place(ev.clientX, ev.clientY);
    });
    tl.addEventListener('pointerleave', function (ev) {
      if (ev.pointerType !== 'touch') hide();
    });
    // タッチでは帯の部分のタップで内訳を出し、同じ行をもう一度タップすると閉じる（名前のタップはリンク遷移）。
    tl.addEventListener('click', function (ev) {
      var track = ev.target.closest('.rtl-track');
      if (!track) return;
      var row = track.closest('.rtl-row');
      if (cur === row && !tip.hidden) { hide(); return; }
      fill(row);
      place(ev.clientX, ev.clientY);
    });
    document.addEventListener('click', function (ev) {
      if (!tl.contains(ev.target)) hide();
    });
    window.addEventListener('scroll', function () {
      if (cur && !cur.matches(':hover')) hide();
    }, { passive: true });
    tl.addEventListener('focusin', function (ev) {
      var row = ev.target.closest('.rtl-row');
      if (!row) return;
      fill(row);
      var r = row.getBoundingClientRect();
      place(r.left + 40, r.bottom - 10);
    });
    tl.addEventListener('focusout', function () { hide(); });
  });

  function setupFilter(sec) {
    var box = sec.querySelector('.rtl-filter');
    if (!box) return;
    var input = box.querySelector('.rtl-filter-input');
    var help = box.querySelector('.rtl-filter-help');
    var tip = box.querySelector('.rtl-filter-tip');
    var count = box.querySelector('.rtl-filter-count');
    var rows = sec.querySelectorAll('.rtl-row');

    // 個人・団体の絞り込み（ページ側で親要素の data-entity-filter を切り替える）で隠れていない行を数える。
    function updateCount() {
      var root = sec.closest('[data-entity-filter]');
      var filter = root ? root.getAttribute('data-entity-filter') : '';
      var main = 0, total = 0;
      rows.forEach(function (r) {
        var t = r.getAttribute('data-entity-type');
        if (filter === 'person' && t === 'company') return;
        if (filter === 'company' && t === 'person') return;
        total++;
        if (!r.classList.contains('rtl-row-extra')) main++;
      });
      count.textContent = (input.checked ? main : total) + ' / ' + total + '名';
    }
    input.addEventListener('change', function () {
      sec.classList.toggle('is-main-only', input.checked);
      updateCount();
    });
    var root = sec.closest('.creators-root');
    if (root && window.MutationObserver) {
      new MutationObserver(updateCount).observe(root, { attributes: true, attributeFilter: ['data-entity-filter'] });
    }
    updateCount();

    // 吹き出し：マウスは ? に載せている間、タップ・クリックは開閉、キーボードはフォーカスの間。
    var pinned = false;
    function show() { tip.hidden = false; help.setAttribute('aria-expanded', 'true'); }
    function hide() { tip.hidden = true; help.setAttribute('aria-expanded', 'false'); }
    help.addEventListener('pointerenter', function (ev) { if (ev.pointerType !== 'touch') show(); });
    help.addEventListener('pointerleave', function (ev) { if (ev.pointerType !== 'touch' && !pinned) hide(); });
    help.addEventListener('focus', show);
    help.addEventListener('blur', function () { pinned = false; hide(); });
    help.addEventListener('click', function () {
      pinned = !pinned;
      if (pinned) show(); else hide();
    });
    document.addEventListener('click', function (ev) {
      if (!box.contains(ev.target)) { pinned = false; hide(); }
    });
    document.addEventListener('keydown', function (ev) {
      if (ev.key === 'Escape') { pinned = false; hide(); }
    });
  }
})();
