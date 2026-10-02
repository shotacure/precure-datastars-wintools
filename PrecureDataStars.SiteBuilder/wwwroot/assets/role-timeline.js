/* 年表タブの「担当（出演・参加）の移り変わり」（線表。役職詳細・声の出演・歌唱）。
   行にマウスを載せると（タッチでは帯の部分をタップすると）参加期間と作品ごとの参加を出す。
   内訳は行の data-sub（名前の下に添える 1 行。キャラクターの CV など）・data-period（参加期間）・
   data-works（作品ごとの参加。改行区切り）から組み立てる。名前にキーボードのフォーカスが来たときも行の下に出す。 */
(function () {
  document.querySelectorAll('.role-timeline').forEach(function (sec) {
    var tl = sec.querySelector('.rtl');
    if (!tl) return;

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
})();
