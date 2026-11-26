// 区画ごとの CSS（/assets/css/{区画}.css。SiteBuilder の CssBundler が site.css から作る）が、見た目を変えていないかを確かめる道具。
//
// 使い方（リポジトリ直下で。テストビルドを全体で回した後に）：
//   node scripts/verify-css-bundles.mjs                 出力先は PrecureDataStars.SiteBuilder/App.config の SiteOutputDirTest
//   node scripts/verify-css-bundles.mjs --site <出力>    出力先を指す（本番のドライランの出力など）
//   オプション：--widths 390,960,1280（幅）・--parallel 4（同時に開くタブ）・--visual 20（見た目を突き合わせるページ数）・
//             --only <テンプレート名の一部>（そのテンプレートの見本だけ）・--chrome <chrome.exe のパス>
//
// 確かめること（どれか 1 つでも当たれば終了コード 1）：
//   1. 区画ごと：区画の CSS の規則が、site.css の規則の並びのとおりに、同じ中身で並んでいるか（Chrome の CSSOM で読み比べる）。
//   2. ページごと（控え {出力}.css-bundles.json の見本の URL を、幅ごとに開く）：
//      - ボタン・切り替え・タブ・details・検索などを押して状態を作り、その間に DOM に現れた class・属性名・タグを MutationObserver で集める。
//      - 区画から落とした規則のセレクタを広げた形（疑似クラス・疑似要素を外し、属性の値を外す）で querySelector し、当たる要素が無いか。
//      - 落とした規則が要る語のうち、区画の語の集合に無いものが、操作の間に一度でも現れなかったか（すぐ消えるトーストなども拾う）。
//      - 読み込みの失敗と JS のエラーが無いか。読まなかった JS の入口の印が、そのページに無いか。
//   3. 抜き取りのページで、区画の CSS と全部入りの site.css を同じ DOM のまま差し替え、全要素の計算後の見た目（getComputedStyle）が同じか。
//
// 追加の npm の包みは使わない（Node の標準の http・WebSocket と、手元の Chrome を画面なしで動かす）。

import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { readFileSync, existsSync, mkdtempSync, rmSync, statSync, createReadStream } from 'node:fs';
import { join, extname, dirname, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// ──────────────── 引数 ────────────────
const args = parseArgs(process.argv.slice(2));
const siteDir = resolve(args.site ?? readTestOutputDir());
const widths = (args.widths ?? '390,960,1280').split(',').map(Number);
const parallel = Number(args.parallel ?? 4);
const visualCount = Number(args.visual ?? 20);
const only = args.only ?? '';
const chromePath = args.chrome ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';

function parseArgs(list) {
  const out = {};
  for (let i = 0; i < list.length; i++) {
    if (list[i].startsWith('--')) out[list[i].slice(2)] = list[i + 1]?.startsWith('--') || list[i + 1] === undefined ? 'true' : list[++i];
  }
  return out;
}

// App.config（git に入れていないローカルの設定）の SiteOutputDirTest を読む。
function readTestOutputDir() {
  const config = join(repoRoot, 'PrecureDataStars.SiteBuilder', 'App.config');
  const m = existsSync(config) && readFileSync(config, 'utf8').match(/key="SiteOutputDirTest"\s+value="([^"]+)"/);
  if (!m) throw new Error('出力先が分かりません。--site で出力のディレクトリを指してください。');
  return m[1];
}

const manifestPath = siteDir.replace(/[\\/]+$/, '') + '.css-bundles.json';
if (!existsSync(manifestPath)) throw new Error(`控えがありません（全体のテストビルドを先に回してください）: ${manifestPath}`);
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const fullCss = readFileSync(join(siteDir, 'assets', 'site.css'), 'utf8');
const groups = Object.keys(manifest.words);

// テンプレート名 → 区画（SiteBuilder の CssBundlePlan と同じ当て方。ページの <link> の指し先からも引けるので、見本はそちらで決める）。
const problems = [];
const report = (kind, where, detail) => {
  problems.push({ kind, where, detail });
  console.log(`  ✗ [${kind}] ${where}: ${detail}`);
};

// ──────────────── 手元の配り口 ────────────────
const TYPES = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.webp': 'image/webp',
  '.ico': 'image/x-icon', '.txt': 'text/plain; charset=utf-8', '.xml': 'application/xml; charset=utf-8', '.woff2': 'font/woff2' };

const server = createServer((req, res) => {
  try {
    let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (path === '/__blank') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end('<!doctype html><title>blank</title>'); return; }
    let file = join(siteDir, path);
    if (path.endsWith('/')) file = join(file, 'index.html');
    else if (!extname(path) && existsSync(join(file, 'index.html'))) file = join(file, 'index.html');
    if (!file.startsWith(siteDir) || !existsSync(file) || statSync(file).isDirectory()) {
      res.writeHead(404, { 'content-type': 'text/plain' }); res.end('not found'); return;
    }
    res.writeHead(200, { 'content-type': TYPES[extname(file).toLowerCase()] ?? 'application/octet-stream', 'cache-control': 'no-store' });
    createReadStream(file).pipe(res);
  } catch (e) {
    res.writeHead(500); res.end(String(e));
  }
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const origin = `http://127.0.0.1:${server.address().port}`;

// ──────────────── Chrome（画面なし）と DevTools Protocol ────────────────
const profile = mkdtempSync(join(tmpdir(), 'verify-css-'));
const chrome = spawn(chromePath, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
  '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--hide-scrollbars', '--mute-audio', 'about:blank'],
  { stdio: 'ignore' });

async function devtoolsUrl() {
  const file = join(profile, 'DevToolsActivePort');
  for (let i = 0; i < 100; i++) {
    if (existsSync(file)) {
      const [port, path] = readFileSync(file, 'utf8').trim().split(/\r?\n/);
      if (port && path) return `ws://127.0.0.1:${port}${path}`;
    }
    await sleep(100);
  }
  throw new Error('Chrome の DevTools に接続できませんでした');
}

class Cdp {
  constructor(url) { this.url = url; this.id = 0; this.pending = new Map(); this.listeners = new Set(); }
  async open() {
    this.ws = new WebSocket(this.url);
    await new Promise((res, rej) => { this.ws.onopen = res; this.ws.onerror = rej; });
    this.ws.onmessage = ev => {
      const msg = JSON.parse(ev.data);
      if (msg.id && this.pending.has(msg.id)) {
        const { resolve: ok, reject: ng, method } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        msg.error ? ng(new Error(`${method}: ${msg.error.message}`)) : ok(msg.result);
      } else for (const l of this.listeners) l(msg);
    };
  }
  send(method, params = {}, sessionId) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params, sessionId }));
    return new Promise((ok, ng) => this.pending.set(id, { resolve: ok, reject: ng, method }));
  }
  on(fn) { this.listeners.add(fn); return () => this.listeners.delete(fn); }
}

const sleep = ms => new Promise(r => setTimeout(r, ms));
const cdp = new Cdp(await devtoolsUrl());
await cdp.open();
await cdp.send('Browser.grantPermissions', { origin, permissions: ['clipboardReadWrite', 'clipboardSanitizedWrite'] }).catch(() => {});

/** タブを 1 つ開いて、操作用の小さな口を返す。 */
async function openTab() {
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const send = (m, p) => cdp.send(m, p, sessionId);
  const events = [];
  const off = cdp.on(msg => { if (msg.sessionId === sessionId) events.push(msg); });
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  await send('DOM.enable');
  await send('CSS.enable');
  // 外の書体・動画・画像・計測は読まない（手元の確かめに要らず、偽の「違い」と待ちのもとになる）。
  await send('Network.setBlockedURLs', { urls: ['*fonts.googleapis.com*', '*fonts.gstatic.com*', '*youtube.com*', '*ytimg.com*',
    '*media-amazon.com*', '*googletagmanager.com*', '*google-analytics.com*', '*amazon.co.jp*'] });
  await send('Emulation.setTimezoneOverride', { timezoneId: 'Asia/Tokyo' });
  await send('Page.addScriptToEvaluateOnNewDocument', { source: OBSERVER_SOURCE });
  const evaluate = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
    return r.result.value;
  };
  const navigate = async (url) => {
    events.length = 0;
    await send('Page.navigate', { url });
    for (let i = 0; i < 300; i++) {
      if (events.some(e => e.method === 'Page.loadEventFired')) return;
      await sleep(50);
    }
    throw new Error('読み込みが 15 秒で終わりませんでした');
  };
  const close = async () => { off(); await cdp.send('Target.closeTarget', { targetId }).catch(() => {}); };
  return { send, evaluate, navigate, close, events };
}

// 読み込みの始めから DOM の変化を見張り、現れた class・属性名・タグを集める（すぐ消える要素も拾う）。
const OBSERVER_SOURCE = `(() => {
  const seen = { cls: new Set(), attr: new Set(), tag: new Set() };
  window.__pcdsSeen = seen;
  const note = (el) => {
    if (!el || el.nodeType !== 1) return;
    seen.tag.add(el.localName);
    for (const a of el.attributes) seen.attr.add(a.name);
    for (const c of el.classList) seen.cls.add(c);
  };
  const walk = (root) => { note(root); if (root.querySelectorAll) for (const el of root.querySelectorAll('*')) note(el); };
  new MutationObserver(list => {
    for (const m of list) {
      if (m.type === 'attributes') note(m.target);
      for (const n of m.addedNodes) walk(n);
    }
  }).observe(document, { subtree: true, childList: true, attributes: true });
})();`;

// ──────────────── 1. 区画ごとの点検（CSSOM の並び） ────────────────
console.log(`出力: ${siteDir}`);
console.log(`版: ${manifest.version}・区画 ${groups.length}・幅 ${widths.join('/')}px`);
console.log('');
console.log('[1/3] 区画の CSS が site.css の規則の並びどおりか');

const RULES_FN = `(cssText) => {
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(cssText);
  const out = [];
  const walk = (rules, ctx) => {
    for (const r of rules) {
      if (r.cssRules && !(r instanceof CSSStyleRule)) walk(r.cssRules, ctx + (r.conditionText ?? r.cssText.split('{')[0]) + '|');
      else out.push({ ctx, text: r.cssText, selector: r.selectorText ?? '' });
    }
  };
  walk(sheet.cssRules, '');
  return out;
}`;

const bundleInfo = {};
{
  const tab = await openTab();
  await tab.navigate(`${origin}/__blank`);
  const fullRules = await tab.evaluate(`(${RULES_FN})(${JSON.stringify(fullCss)})`);
  for (const group of groups) {
    const css = readFileSync(join(siteDir, 'assets', 'css', `${group}.css`), 'utf8');
    const rules = await tab.evaluate(`(${RULES_FN})(${JSON.stringify(css)})`);
    // 区画の規則が、site.css の規則の並びの中に、順番どおりに全部あるか（部分列か）。残らなかった規則が「落とした規則」。
    const dropped = [];
    let j = 0;
    for (const full of fullRules) {
      if (j < rules.length && rules[j].ctx === full.ctx && rules[j].text === full.text) j++;
      else if (full.selector) dropped.push(full);
    }
    if (j !== rules.length) report('並び', `${group}.css`, `区画の ${j + 1} 本目の規則が site.css の並びに見つかりません: ${rules[j].text.slice(0, 120)}`);
    const words = new Set(manifest.words[group]);
    const lower = new Set([...words].map(w => w.toLowerCase()));
    for (const w of ['html', 'head', 'body', 'tbody', 'thead', 'colgroup', 'open']) lower.add(w);
    const prefixes = [...words].filter(w => w.length >= 2 && w.endsWith('-'));
    // 落とした規則ごとに、広げたセレクタと「区画の語の集合に無い、要る語」を控える。
    const droppedChecks = dropped.map(r => {
      const selectors = splitSelectorList(r.selector).map(broaden);
      const missing = new Set();
      for (const s of splitSelectorList(r.selector)) for (const t of requiredTokens(s)) {
        const present = t.kind === 'tag' ? lower.has(t.name)
          : t.kind === 'attr' ? (lower.has(t.name) || prefixes.some(p => t.name.startsWith(p.toLowerCase())))
          : (words.has(t.name) || prefixes.some(p => t.name.startsWith(p)));
        if (!present) missing.add(`${t.kind}:${t.name}`);
      }
      return { selector: r.selector, broadened: selectors, missing: [...missing] };
    });
    bundleInfo[group] = { kept: rules.length, dropped: droppedChecks };
    console.log(`  ${group}.css：残した規則 ${rules.length}・落とした規則 ${dropped.length}`);
  }
  await tab.close();
}

// セレクタの並びを、括弧と引用符の外の , で切る。
function splitSelectorList(list) {
  const out = []; let depth = 0, quote = '', start = 0;
  for (let i = 0; i < list.length; i++) {
    const c = list[i];
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = ''; continue; }
    if (c === '"' || c === "'") quote = c;
    else if (c === '(' || c === '[') depth++;
    else if (c === ')' || c === ']') depth--;
    else if (c === ',' && depth === 0) { out.push(list.slice(start, i).trim()); start = i + 1; }
  }
  out.push(list.slice(start).trim());
  return out.filter(Boolean);
}

// セレクタを広げる：疑似クラス・疑似要素（引数ごと）を外し、属性の値を外す。当たる範囲は元より広くなる（見落とさない向き）。
function broaden(sel) {
  let s = sel.replace(/\[\s*([\w-]+)[^\]]*\]/g, '[$1]');
  let prev;
  do { prev = s; s = s.replace(/::?(?!root\b)[a-zA-Z-]+\((?:[^()]|\([^()]*\))*\)/g, ''); } while (s !== prev);
  s = s.replace(/::?(?!root\b)[a-zA-Z-]+/g, '');
  s = s.replace(/([>+~])\s*(?=[>+~]|$)/g, '$1 *').trim();
  if (/^[>+~]/.test(s)) s = '* ' + s;
  return s || '*';
}

// セレクタが当たるのに要る語（SiteBuilder の CssBundler と同じ数え方）。
function requiredTokens(sel) {
  const s = sel.replace(/::?[a-zA-Z-]+(\((?:[^()]|\([^()]*\))*\))?/g, ' ');
  const out = [];
  for (const m of s.matchAll(/\.([\w-]+)/g)) out.push({ kind: 'class', name: m[1] });
  for (const m of s.matchAll(/#([\w-]+)/g)) out.push({ kind: 'id', name: m[1] });
  for (const m of s.matchAll(/\[\s*([\w-]+)/g)) out.push({ kind: 'attr', name: m[1].toLowerCase() });
  for (const m of s.replace(/\[[^\]]*\]/g, ' ').matchAll(/(?:^|[\s>+~])([a-zA-Z][\w-]*)/g)) out.push({ kind: 'tag', name: m[1].toLowerCase() });
  return out;
}

// ──────────────── 2. ページごとの点検 ────────────────
console.log('');
console.log('[2/3] 見本のページで、操作の後も落とした規則が当たらないか');

// 検索で打つ語：索引から種類ごとに 1 件ずつ。
let searchWords = [];
try {
  const index = JSON.parse(readFileSync(join(siteDir, 'search-index.json'), 'utf8'));
  const items = Array.isArray(index) ? index : (Object.values(index).find(Array.isArray) ?? []);
  const byKind = new Map();
  for (const it of items) if (it && it.k && it.t && !byKind.has(it.k)) byKind.set(it.k, String(it.t).slice(0, 4));
  searchWords = [...byKind.values()];
} catch { /* 索引が無ければ検索は押さない */ }

const pages = [];
for (const [template, urls] of Object.entries(manifest.samples)) {
  if (only && !template.includes(only)) continue;
  for (const url of urls) pages.push({ template, url });
}

const PAGE_CHECK_FN = `async (droppedByGroup, searchWords) => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const link = document.querySelector('link[rel=stylesheet][href*="/assets/css/"], link[rel=stylesheet][href*="/assets/site.css"]');
  const group = link && (link.getAttribute('href').match(/\\/assets\\/css\\/([\\w-]+)\\.css/) || [])[1];
  const result = { group: group || '', matches: [], seenMissing: [], omittedJs: [], invalid: 0 };
  // 遅れて読む画像を読ませる。
  window.scrollTo(0, document.body.scrollHeight); await sleep(150); window.scrollTo(0, 0);
  // 別のページへ移るリンクは押しても移らないようにする（ページの中の JS の処理は走る）。
  document.addEventListener('click', e => {
    const a = e.target.closest && e.target.closest('a[href]');
    if (a && !a.getAttribute('href').startsWith('#')) e.preventDefault();
  }, true);
  window.open = () => null;
  for (const d of document.querySelectorAll('details')) d.open = true;
  const targets = [...document.querySelectorAll('button, input[type=checkbox], input[type=radio], summary, [role=tab], [aria-controls], [aria-expanded], [aria-pressed], [popovertarget], a[data-fonts-viewer], [data-photo-modal], .yt-facade-play')].slice(0, 150);
  for (const el of targets) {
    const times = el.matches('.count-sort-btn, .sort-toggle-btn') ? 4 : 1;
    for (let i = 0; i < times; i++) { try { el.click(); } catch {} await sleep(15); }
  }
  for (const dlg of document.querySelectorAll('dialog[open]')) { try { dlg.close(); } catch {} }
  const input = document.getElementById('site-search-input');
  if (input) {
    for (const w of searchWords) {
      input.focus(); input.value = w; input.dispatchEvent(new Event('input', { bubbles: true }));
      await sleep(400);
    }
  }
  await sleep(300);
  // 終わりの DOM も見張りの集合に入れる。
  const seen = window.__pcdsSeen || { cls: new Set(), attr: new Set(), tag: new Set() };
  for (const el of document.querySelectorAll('*')) {
    seen.tag.add(el.localName); for (const a of el.attributes) seen.attr.add(a.name); for (const c of el.classList) seen.cls.add(c);
  }
  const dropped = droppedByGroup[group] || [];
  for (const d of dropped) {
    for (const s of d.broadened) {
      let el = null;
      try { el = document.querySelector(s); } catch { result.invalid++; continue; }
      if (el) { result.matches.push(d.selector + '  ⇒  ' + el.outerHTML.slice(0, 120)); break; }
    }
    for (const m of d.missing) {
      const [kind, name] = [m.slice(0, m.indexOf(':')), m.slice(m.indexOf(':') + 1)];
      const hit = kind === 'class' ? seen.cls.has(name) : kind === 'attr' ? seen.attr.has(name) : kind === 'tag' ? seen.tag.has(name) : !!document.getElementById(name);
      if (hit) { result.seenMissing.push(m + '（' + d.selector + '）'); }
    }
  }
  // 読まなかった JS の入口の印が、ページに無いか。
  const loaded = new Set([...document.scripts].map(s => (s.getAttribute('src') || '').replace(/\\?.*$/, '')));
  const entry = { '/assets/count-sort.js': () => !!document.querySelector('.count-sort-btn'),
    '/assets/credit-align.js': () => !!document.querySelector('.credit-section'),
    '/assets/release-countdown.js': () => !!document.querySelector('[data-release-date]'),
    '/assets/section-nav.js': () => document.querySelectorAll('section[id]').length >= 2 };
  for (const [src, needed] of Object.entries(entry)) if (!loaded.has(src) && needed()) result.omittedJs.push(src);
  result.seenMissing = [...new Set(result.seenMissing)];
  return result;
}`;

const droppedByGroup = Object.fromEntries(Object.entries(bundleInfo).map(([g, info]) => [g, info.dropped]));
let checked = 0;
async function checkPage(tab, page, width) {
  await tab.send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: width < 600 });
  await tab.navigate(origin + page.url);
  await sleep(250);
  const r = await tab.evaluate(`(${PAGE_CHECK_FN})(${JSON.stringify(droppedByGroup)}, ${JSON.stringify(searchWords)})`);
  const where = `${page.template} ${decodeURI(page.url)} @${width}`;
  if (!r.group) report('CSS', where, 'ページが区画の CSS を読んでいません');
  for (const m of r.matches) report('当たる', where, m);
  for (const m of r.seenMissing.slice(0, 5)) report('語の漏れ', where, m);
  for (const s of r.omittedJs) report('JS', where, `読んでいない ${s} の入口の印がある`);
  for (const e of tab.events) {
    if (e.method === 'Runtime.exceptionThrown') report('JS エラー', where, e.params.exceptionDetails.exception?.description?.split('\n')[0] ?? e.params.exceptionDetails.text);
    if (e.method === 'Network.loadingFailed' && !e.params.blockedReason && !e.params.canceled) report('読み込み', where, e.params.errorText);
    if (e.method === 'Network.responseReceived' && e.params.response.url.startsWith(origin) && e.params.response.status >= 400
      && !e.params.response.url.includes('/favicon')) report('読み込み', where, `${e.params.response.status} ${e.params.response.url.slice(origin.length)}`);
  }
  checked++;
  if (checked % 50 === 0) console.log(`  … ${checked} / ${pages.length * widths.length}（${Math.round((Date.now() - started) / 1000)} 秒）`);
}

const jobs = [];
for (const page of pages) for (const width of widths) jobs.push({ page, width });
const started = Date.now();
await Promise.all(Array.from({ length: parallel }, async () => {
  const tab = await openTab();
  while (jobs.length) {
    const { page, width } = jobs.shift();
    try { await checkPage(tab, page, width); }
    catch (e) { report('道具', `${page.template} ${decodeURI(page.url)} @${width}`, e.message); }
  }
  await tab.close();
}));
console.log(`  ${pages.length} ページ × ${widths.length} 幅 = ${checked} 回（${Math.round((Date.now() - started) / 1000)} 秒）`);

// ──────────────── 3. 見た目の抜き取り ────────────────
console.log('');
console.log(`[3/3] 抜き取り ${visualCount} ページで、区画の CSS と site.css の見た目を突き合わせる`);

const SNAPSHOT_FN = `() => {
  for (const a of document.getAnimations()) { try { a.finish(); } catch {} }
  const out = [];
  const els = document.querySelectorAll('*');
  for (let i = 0; i < els.length; i++) {
    for (const pseudo of [null, '::before', '::after', '::marker']) {
      const cs = getComputedStyle(els[i], pseudo);
      let s = '';
      for (let k = 0; k < cs.length; k++) { const p = cs[k]; s += p + ':' + cs.getPropertyValue(p) + ';'; }
      for (const v of ['--credit-role-name-w', '--credit-character-cell-w']) s += v + ':' + cs.getPropertyValue(v) + ';';
      out.push(s);
    }
  }
  window.__pcdsSnapshot = out;
  return out.length;
}`;

const COMPARE_FN = `() => {
  for (const a of document.getAnimations()) { try { a.finish(); } catch {} }
  const before = window.__pcdsSnapshot;
  const els = document.querySelectorAll('*');
  const diffs = [];
  let n = 0;
  for (let i = 0; i < els.length; i++) {
    for (const pseudo of [null, '::before', '::after', '::marker']) {
      const cs = getComputedStyle(els[i], pseudo);
      let s = '';
      for (let k = 0; k < cs.length; k++) { const p = cs[k]; s += p + ':' + cs.getPropertyValue(p) + ';'; }
      for (const v of ['--credit-role-name-w', '--credit-character-cell-w']) s += v + ':' + cs.getPropertyValue(v) + ';';
      if (s !== before[n] && diffs.length < 5) {
        const a = before[n].split(';'), b = s.split(';');
        const changed = b.filter(x => !a.includes(x)).slice(0, 3).join(' / ');
        diffs.push((els[i].className ? els[i].localName + '.' + String(els[i].className).split(' ')[0] : els[i].localName) + (pseudo || '') + ' : ' + changed);
      }
      n++;
    }
  }
  if (n !== before.length) diffs.push('要素の数が変わりました（' + before.length + ' → ' + n + '）');
  return diffs;
}`;

{
  // 区画ごとにまんべんなく選ぶ。
  const byGroup = new Map();
  for (const p of pages) {
    const g = (readFileSync(htmlPathOf(p.url), 'utf8').match(/\/assets\/css\/([\w-]+)\.css/) || [])[1] ?? '';
    if (!byGroup.has(g)) byGroup.set(g, []);
    byGroup.get(g).push(p);
  }
  const picked = [];
  for (let round = 0; picked.length < visualCount && round < 50; round++) {
    for (const list of byGroup.values()) if (list[round] && picked.length < visualCount) picked.push(list[round]);
  }
  const tab = await openTab();
  const sheets = new Map();
  cdp.on(msg => { if (msg.method === 'CSS.styleSheetAdded') sheets.set(msg.params.header.sourceURL, msg.params.header.styleSheetId); });
  for (const page of picked) {
    for (const width of [390, 1280]) {
      const where = `${page.template} ${decodeURI(page.url)} @${width}`;
      try {
        await tab.send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: width < 600 });
        sheets.clear();
        await tab.navigate(origin + page.url);
        await sleep(400);
        const sheetUrl = [...sheets.keys()].find(u => u.includes('/assets/css/'));
        if (!sheetUrl) { report('見た目', where, '区画の CSS が見つかりません'); continue; }
        await tab.evaluate(`(${SNAPSHOT_FN})()`);
        // 同じ DOM のまま、区画の CSS の中身を全部入りの site.css に 1 回で置き換える（外して足すと、CSS の無い一瞬に transition が走る）。
        await tab.send('CSS.setStyleSheetText', { styleSheetId: sheets.get(sheetUrl), text: fullCss });
        await sleep(200);
        const diffs = await tab.evaluate(`(${COMPARE_FN})()`);
        for (const d of diffs) report('見た目', where, d);
      } catch (e) {
        report('道具', where, e.message);
      }
    }
  }
  await tab.close();
  console.log(`  ${picked.length} ページ × 2 幅`);
}

function htmlPathOf(url) {
  const p = decodeURIComponent(url);
  return p.endsWith('/') ? join(siteDir, p, 'index.html') : join(siteDir, p);
}

// ──────────────── 片づけと結果 ────────────────
await cdp.send('Browser.close').catch(() => {});
chrome.kill();
server.close();
await sleep(300);
try { rmSync(profile, { recursive: true, force: true }); } catch { /* Chrome が掴んでいれば残る（一時フォルダなので害は無い） */ }

console.log('');
if (problems.length === 0) {
  console.log('✓ 区画の CSS で見た目が変わる所は見つかりませんでした。');
  process.exit(0);
} else {
  const counts = problems.reduce((m, p) => (m[p.kind] = (m[p.kind] ?? 0) + 1, m), {});
  console.log(`✗ ${problems.length} 件：` + Object.entries(counts).map(([k, n]) => `${k} ${n}`).join('・'));
  process.exit(1);
}
