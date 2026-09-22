/**
 * art-track-player.js — 配信音源（YouTube アートトラック）の再生を担う。
 *
 * アートトラックは、レーベルが正規に配信した音源へ YouTube がジャケット画像を付けて
 * 自動生成する公式動画。本ファイルは IFrame Player API でそれを再生する。
 *
 * 対象 DOM 構造（products-detail.sbn / bgms-detail.sbn 側で生成）：
 *   <ul data-art-track-list data-art-track-mode="continuous">   ← アルバムのトラックリスト
 *     <li>
 *       <button class="art-track-play"
 *               data-art-track-id="HREZTwGMies"
 *               data-art-track-title="ハートにヒント! 名探偵プリキュア!"
 *               data-art-track-sub="名探偵プリキュア！ ボーカルアルバム">1</button>
 *
 * 会員限定の音源には、誰でも再生できる同一音源が別の盤にあれば
 * data-art-track-alt-id / data-art-track-alt-sub が併記される。
 * 「会員限定を表示しない」設定の閲覧者にはそちらを鳴らす（下記 resolveSource）。
 *   <ul data-art-track-list data-art-track-mode="single">       ← 劇伴の cue リスト
 *
 * mode の意味：
 *   continuous  リスト内の全トラックを並び順にプレイリスト化し、押した位置から連続再生する。
 *               アルバムの収録順そのものなので、通して流れるのが本来の聴き方。
 *   single      押した 1 件だけを再生し、終わったら停止する。劇伴 cue は音源が違えば別 cue として
 *               登録されており、連番の派生（~Harp Only~ 等）が隣接して並ぶため、連続再生すると
 *               同じ曲が続けて鳴ってしまう。
 *
 * プレイヤーは画面下部の sticky バーに 1 個だけ置く。YouTube API Services Developer Policies
 * III.I.7 が音声と映像の分離を禁じており、ビューポートの最小サイズも 200x200px と定められて
 * いるため、プレイヤーを画面外に隠して音だけ鳴らす実装は採らない。アートトラックの映像は
 * ジャケット画像の静止画なので、小さく表示してもそのまま音楽プレイヤーのアートワークになる。
 *
 * パフォーマンス：最初の再生クリックまで iframe_api を読み込まない（ファサード方式）。
 * 再生ボタンのあるページでも、押されなければ外部リクエストは 1 本も発生しない。
 *
 * 再生ボタンが 1 つも無いページでは何もせず終了する。
 */
(function () {
  'use strict';

  var API_SRC = 'https://www.youtube.com/iframe_api';
  // 再生は YouTube の通常ホストで行う。Cookie を使わないプライバシー強化ホスト
  // （youtube-nocookie.com）は視聴者のログイン状態がプレイヤーに伝わらないため、
  // YouTube Music Premium 会員限定の音源が会員でも再生できず、広告も非パーソナライズに限られ、
  // Premium 会員の再生も会員として計上されない。どちらも権利者の収益を減らす方向に働くので、
  // 権利者に還元される再生を優先して通常ホストで統一する。
  var PLAYER_HOST = 'https://www.youtube.com';
  var PLAYER_ELEMENT_ID = 'artTrackPlayerFrame';

  // API のロード状態。'idle' → 'loading' → 'ready'。
  var apiState = 'idle';
  // API ロード完了後に実行する処理の待ち行列（ロード中に複数回クリックされた場合に備える）。
  var pendingCallbacks = [];

  var player = null;
  var playerReady = false;
  // Premium 限定の音源を再生しようとしたときの見張り。一定時間 PLAYING にならなければ
  // 再生できなかったと判断する（Premium の壁はエラーではなく案内画面として出るため、
  // onError だけに頼れない）。
  var premiumWatchdog = null;

  // プレイヤー生成直後はまだ再生要求を受け付けられないため、onReady まで保留する処理を入れておく。
  var pendingPlayRequest = null;

  var barEl = null;
  var titleEl = null;
  var subEl = null;

  // 現在再生中のボタン（ハイライト解除に使う）。
  var activeButton = null;
  // continuous モードで再生中のリスト（動画 ID → ボタンの対応付けに使う）。
  var activeButtons = [];

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  function init() {
    var buttons = document.querySelectorAll('.art-track-play');
    if (!buttons.length) return;

    buttons.forEach(function (btn) {
      btn.addEventListener('click', function (ev) {
        ev.preventDefault();
        // トラック行・cue カードはカード全体がリンクになっている（オーバーレイリンクパターン）。
        // 再生ボタンのクリックが遷移に化けないよう伝播を止める。
        ev.stopPropagation();
        onPlayClick(btn);
      });
    });
  }

  /**
   * 再生ボタンが押されたときの入口。
   * 既に同じボタンが再生中なら一時停止／再開のトグルとして振る舞う。
   */
  function onPlayClick(btn) {
    var videoId = resolveSource(btn).id;
    if (!videoId) return;

    if (activeButton === btn && playerReady && player) {
      var state = player.getPlayerState();
      if (state === window.YT.PlayerState.PLAYING) {
        player.pauseVideo();
      } else {
        player.playVideo();
      }
      return;
    }

    var list = btn.closest('[data-art-track-list]');
    var mode = list ? list.getAttribute('data-art-track-mode') : 'single';

    var request;
    if (mode === 'continuous' && list) {
      // リスト内の再生可能なボタンを DOM 順に集め、押された位置を開始インデックスにする。
      var siblings = Array.prototype.slice.call(list.querySelectorAll('.art-track-play'))
        .filter(function (b) { return resolveSource(b).id; });
      var index = siblings.indexOf(btn);
      request = {
        mode: 'continuous',
        buttons: siblings,
        ids: siblings.map(function (b) { return resolveSource(b).id; }),
        index: index < 0 ? 0 : index,
        button: btn
      };
    } else {
      request = { mode: 'single', buttons: [btn], ids: [videoId], index: 0, button: btn };
    }

    ensurePlayer(function () { startPlayback(request); });
  }

  /**
   * ボタンから「実際に鳴らす音源」を決める。
   * 既定は data-art-track-id（アルバム詳細ならその盤、楽曲・劇伴詳細なら初出）。
   * ただし「会員限定を表示しない」設定の閲覧者に対しては、その音源が会員限定で、
   * かつ誰でも再生できる同一音源が別の盤にあるときに限り、そちらへ振り替える。
   * 会員はどちらも鳴らせるので、設定を変えていない閲覧者には既定のまま返す。
   */
  function resolveSource(btn) {
    var id = btn.getAttribute('data-art-track-id') || '';
    var sub = btn.getAttribute('data-art-track-sub') || '';
    if (btn.getAttribute('data-art-track-premium') !== '1') return { id: id, sub: sub };

    var pref = (window.PCDS && window.PCDS.artTrackPremium)
      ? window.PCDS.artTrackPremium.getPreference() : null;
    if (pref !== 'hide') return { id: id, sub: sub };

    var altId = btn.getAttribute('data-art-track-alt-id') || '';
    if (!altId) return { id: id, sub: sub };

    return { id: altId, sub: btn.getAttribute('data-art-track-alt-sub') || sub };
  }

  /**
   * プレイヤー（と前段の iframe_api）を必要になった時点で初めて用意する。
   * 用意が済んでいれば callback を即時実行する。
   */
  function ensurePlayer(callback) {
    if (playerReady && player) { callback(); return; }

    // プレイヤー生成待ちの間に来た要求は最後のものだけを保持する（連打時に最後の意図を優先）。
    pendingPlayRequest = callback;

    if (player) return;          // 生成済みで onReady 待ち
    if (apiState === 'loading') return;

    buildBar();

    if (apiState === 'ready') { createPlayer(); return; }

    apiState = 'loading';
    pendingCallbacks.push(function () { createPlayer(); });

    // iframe_api は読み込み完了後にグローバルの onYouTubeIframeAPIReady を呼ぶ。
    // 他スクリプトが同名を定義している可能性を考慮し、既存があれば連鎖させる。
    var previous = window.onYouTubeIframeAPIReady;
    window.onYouTubeIframeAPIReady = function () {
      if (typeof previous === 'function') { try { previous(); } catch (e) { /* 既存側の失敗は無視 */ } }
      apiState = 'ready';
      var queued = pendingCallbacks;
      pendingCallbacks = [];
      queued.forEach(function (fn) { fn(); });
    };

    var script = document.createElement('script');
    script.src = API_SRC;
    script.async = true;
    document.head.appendChild(script);
  }

  function createPlayer() {
    var vars = { playsinline: 1, rel: 0 };
    // origin を明示すると postMessage の宛先が固定され、埋め込み側の取り違えを防げる。
    // file:// 等で origin が "null" になる環境では指定しない。
    if (window.location.origin && window.location.origin !== 'null') {
      vars.origin = window.location.origin;
    }

    player = new window.YT.Player(PLAYER_ELEMENT_ID, {
      host: PLAYER_HOST,
      playerVars: vars,
      events: {
        onReady: function () {
          playerReady = true;
          var req = pendingPlayRequest;
          pendingPlayRequest = null;
          if (req) req();
        },
        onStateChange: onPlayerStateChange
      }
    });
  }

  function startPlayback(request) {
    armPremiumWatchdog(request.button);
    activeButtons = request.buttons;
    setActiveButton(request.button);
    updateMeta(request.button);
    showBar();

    if (request.mode === 'continuous') {
      // プレイリスト ID ではなく動画 ID の配列を渡す。実行時に YouTube 側の並び順へ依存しないため、
      // 複数枚組のフラット化の有無や将来の曲追加があっても壊れない。
      // 連続再生は 1 つのホストで通す。Premium 限定の曲がリストに混ざっていると
      // そこだけ再生できない画面になるが、その曲のボタンを直接押せば適切なホストで開き直る。
      player.loadPlaylist({ playlist: request.ids, index: request.index });
    } else {
      player.loadVideoById(request.ids[0]);
    }
  }

  function onPlayerStateChange(ev) {
    var YTState = window.YT.PlayerState;

    if (ev.data === YTState.ENDED) {
      // continuous モードでは次のトラックへ自動で移るため、ここで止めるのは single のときだけ。
      // 最終トラックが終わった場合も YouTube 側が次を持たないので同じ扱いになる。
      if (player && typeof player.getPlaylist === 'function') {
        var playlist = player.getPlaylist();
        if (playlist && playlist.length && player.getPlaylistIndex() < playlist.length - 1) return;
      }
      setActiveButton(null);
      return;
    }

    if (ev.data === YTState.PLAYING) {
      // 鳴り出したので再生できなかった疑いは晴れた。
      clearPremiumWatchdog();
      // 連続再生で次トラックへ移ったときに、ハイライトと曲名表示を追従させる。
      syncActiveFromPlayer();
    }
  }

  /** 実際に再生中の動画 ID から、対応するボタンを探してハイライト・曲名表示を合わせる。 */
  function syncActiveFromPlayer() {
    if (!player || typeof player.getVideoData !== 'function') return;
    var data = player.getVideoData();
    if (!data || !data.video_id) return;

    for (var i = 0; i < activeButtons.length; i++) {
      if (activeButtons[i].getAttribute('data-art-track-id') === data.video_id) {
        setActiveButton(activeButtons[i]);
        updateMeta(activeButtons[i]);
        return;
      }
    }
  }

  function setActiveButton(btn) {
    if (activeButton && activeButton !== btn) {
      activeButton.classList.remove('is-playing');
      var prevRow = activeButton.closest('[data-art-track-row]');
      if (prevRow) prevRow.classList.remove('is-playing');
    }
    activeButton = btn;
    if (!btn) return;

    btn.classList.add('is-playing');
    var row = btn.closest('[data-art-track-row]');
    if (row) row.classList.add('is-playing');
  }

  function updateMeta(btn) {
    if (!titleEl) return;
    titleEl.textContent = btn.getAttribute('data-art-track-title') || '';
    // 実際に鳴らす音源の盤を出す。会員限定のまま鳴らすときだけ、その旨を添える
    // （会員でない環境では再生できず、プレイヤーにその旨の画面が出る）。
    var source = resolveSource(btn);
    var sub = source.sub;
    if (btn.getAttribute('data-art-track-premium') === '1'
        && source.id === btn.getAttribute('data-art-track-id')) {
      sub = sub ? sub + '（YouTube Music Premium 会員限定）' : 'YouTube Music Premium 会員限定';
    }
    subEl.textContent = sub;
  }

  /**
   * 画面下部の sticky プレイヤーバーを組み立てる（初回のみ）。
   * 全ページの HTML に空のバーを出力すると無駄なので、再生が要求された時点で JS から生成する。
   */
  function buildBar() {
    if (barEl) return;

    barEl = document.createElement('div');
    barEl.className = 'art-track-bar';
    barEl.setAttribute('hidden', '');

    var inner = document.createElement('div');
    inner.className = 'art-track-bar-inner';

    var frameWrap = document.createElement('div');
    frameWrap.className = 'art-track-frame';
    var frame = document.createElement('div');
    frame.id = PLAYER_ELEMENT_ID;
    frameWrap.appendChild(frame);

    var meta = document.createElement('div');
    meta.className = 'art-track-meta';

    titleEl = document.createElement('div');
    titleEl.className = 'art-track-meta-title';
    subEl = document.createElement('div');
    subEl.className = 'art-track-meta-sub muted';

    // 再生できてしまうページには、その場で根拠が読める短い注記を必ず添える。
    // 「何を鳴らしているのか」が曖昧だと出所不明の音源に見えるため、
    // 権利者が配信した音源をもとに YouTube 側が自動生成した公式動画であることを明示する。
    var note = document.createElement('p');
    note.className = 'art-track-note muted';
    note.textContent = 'レコード会社が配信した音源をもとに YouTube が自動生成した公式動画（アートトラック）を、'
      + 'YouTube の公式プレイヤーで再生しています。当サイトは音源を保持しておらず、'
      + '再生数・広告収益は権利者に帰属します。';

    // 詳しい説明への導線は本文と段落を分け、read more として独立させる。
    // 再生中に同じタブで遷移すると音が止まってしまうため、別タブで開く。
    // 内部リンクなので nofollow は付けない（クロールさせたいページのため）。
    var noteLink = document.createElement('p');
    noteLink.className = 'art-track-note art-track-note-link muted';
    var link = document.createElement('a');
    link.href = '/music/playback/';
    link.target = '_blank';
    link.rel = 'noopener';
    link.textContent = '配信音源の掲載について';
    noteLink.appendChild(link);

    meta.appendChild(titleEl);
    meta.appendChild(subEl);
    meta.appendChild(note);
    meta.appendChild(noteLink);

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'art-track-close';
    close.setAttribute('aria-label', 'プレイヤーを閉じる');
    close.textContent = '×';
    close.addEventListener('click', closeBar);

    inner.appendChild(frameWrap);
    inner.appendChild(meta);
    inner.appendChild(close);
    barEl.appendChild(inner);
    document.body.appendChild(barEl);
  }

  function showBar() {
    if (!barEl) return;
    barEl.removeAttribute('hidden');
    document.body.classList.add('has-art-track-bar');
  }

  /**
   * Premium 限定の音源に対して「再生できなかったら知らせる」見張りを仕掛ける。
   * 限定でない音源では何もしない。
   */
  function armPremiumWatchdog(btn) {
    clearPremiumWatchdog();
    if (!btn || btn.getAttribute('data-art-track-premium') !== '1') return;

    premiumWatchdog = window.setTimeout(function () {
      premiumWatchdog = null;
      // この時点でまだ再生が始まっていなければ、会員限定の壁に当たったと判断する。
      var state = (player && typeof player.getPlayerState === 'function') ? player.getPlayerState() : -1;
      if (state === window.YT.PlayerState.PLAYING) return;

      if (window.PCDS && window.PCDS.artTrackPremium) {
        window.PCDS.artTrackPremium.notifyBlocked();
      }
    }, 5000);
  }

  function clearPremiumWatchdog() {
    if (premiumWatchdog === null) return;
    window.clearTimeout(premiumWatchdog);
    premiumWatchdog = null;
  }

  function closeBar() {
    clearPremiumWatchdog();
    if (player && typeof player.stopVideo === 'function') player.stopVideo();
    setActiveButton(null);
    if (barEl) barEl.setAttribute('hidden', '');
    document.body.classList.remove('has-art-track-bar');
  }
})();
