// precure.tv の CloudFront Function（viewer-request。ランタイムは cloudfront-js-2.0。KeyValueStore を 1 つ関連付ける）。
//
// 1. 内部ファイルの非公開化
//    /_edge/ 配下（SiteBuilder が書き出す旧 URL の転送表 _edge/legacy-redirects.json など）は
//    閲覧者に見せないため 404 を返す。
//
// 2. 人物の区分名の付け替え（/persons/ → /people/）
//    /persons/{名前}/ で公開した期間のある URL を、同じ名前の /people/{名前}/ へ 301 で転送する。
//    数字だけの旧 ID URL（/persons/123/）は 3. の転送表で引く。
//
// 3. 旧 URL の転送（KeyValueStore の転送表）
//    先頭の区分が persons / people / characters / companies / units / books / creators / bgms / stats の URL は、
//    関連付けた KeyValueStore に転送先があれば 301 で転送する。無ければそのまま通す（いまのページはそのまま返る）。
//    転送表に載るのは次のような URL。
//      - 区分 + 数字だけの旧 ID URL（/persons/123/ /characters/123/ /companies/123/ /books/123/）
//      - 名前・コードの旧 URL（/people/{旧名義}/ /characters/{旧キャラ名}/ /companies/{旧正式名}/ /units/{旧名義}/ /books/{旧コード}/）
//      - 廃止・統合したページ（/creators/roles/{code}/、/creators/otona/… 、/stats/records/… ）と、
//        劇伴の入っていない作品の劇伴の URL（/bgms/{作品}/）
//    転送表は SiteBuilder が本番デプロイのたびに KeyValueStore へ同期する（Deploy/KeyValueStoreSyncService.cs）。
//    キーは「区分までのパス + 最後の区切りより後ろをパーセントエンコードしたもの」（英数字と - . _ ~ 以外は %XX の大文字）。
//    同期する側（KeyValueStoreSyncService.ToStoreKey）と同じ形になるよう、ここでも作り直してから引く。
//    名前に濁点・半濁点などがある行は、転送表の側が合成済み（NFC）と合成前（NFD）の両方のキーを持つ。
//
// 4. サブディレクトリの index 解決
//    S3 REST オリジンはサブパスの index を解決しないため、"/…/" と拡張子なしパスを "…/index.html" に書き換える。
//
// 1 ビヘイビアの viewer-request に付けられる関数は 1 つだけなので、処理はこの 1 本に統合する。
import cf from 'cloudfront';

const kvs = cf.kvs();

// 転送表に載りうる URL の先頭の区分。ここに当たるリクエストだけ KeyValueStore を引く（画像や CSS では引かない）。
const TABLE_ROOT = /^\/(persons|people|characters|companies|units|books|creators|bgms|stats)\//;

/**
 * パスの 1 区切りぶんを、転送表のキーの形（デコード → パーセントエンコード）にする。不正な % は null。
 * このランタイムでは文字の正規化ができないので、濁点・半濁点が分かれた形（NFD）で届いた名前はそのままの形で引く
 * （転送表の側が NFC と NFD の両方のキーを持つ）。
 */
function encodeSegment(segment) {
    let decoded;
    try {
        decoded = decodeURIComponent(segment);
    } catch (e) {
        return null;
    }
    // encodeURIComponent が残す ! ' ( ) * も %XX にして、同期する側のエンコードと合わせる。
    return encodeURIComponent(decoded).replace(/[!'()*]/g, function (c) {
        return '%' + c.charCodeAt(0).toString(16).toUpperCase();
    });
}

/**
 * リクエスト URI から転送表のキーを作る。対象外の URI は null。
 * 末尾の "/" と "/index.html" を落とし、最後の区切りより後ろだけをエンコードし直す（途中の区切りは英数字だけ）。
 * 区分の一覧ページ（/people/ など）は転送表に無いので引かない。
 */
function storeKeyOf(uri) {
    if (!TABLE_ROOT.test(uri)) return null;
    let path = uri;
    if (path.endsWith('/index.html')) path = path.substring(0, path.length - '/index.html'.length);
    else if (path.endsWith('/')) path = path.substring(0, path.length - 1);
    const slash = path.lastIndexOf('/');
    if (slash <= 0) return null;
    const encoded = encodeSegment(path.substring(slash + 1));
    return encoded === null ? null : path.substring(0, slash + 1) + encoded;
}

async function handler(event) {
    const request = event.request;
    const uri = request.uri;

    if (uri.indexOf('/_edge/') === 0) {
        return { statusCode: 404, statusDescription: 'Not Found' };
    }

    if (uri.indexOf('/persons/') === 0) {
        const rest = uri.substring('/persons/'.length);
        if (!/^\d+(\/(index\.html)?)?$/.test(rest)) {
            let location = '/people/' + rest;
            // uri がデコード済みで届いた場合に備え、非 ASCII を含むときだけエンコードし直す。
            if (/[^\x00-\x7F]/.test(location)) location = encodeURI(location);
            return {
                statusCode: 301,
                statusDescription: 'Moved Permanently',
                headers: {
                    location: { value: location },
                    'cache-control': { value: 'public, max-age=86400' }
                }
            };
        }
    }

    const key = storeKeyOf(uri);
    if (key !== null) {
        let location = null;
        try {
            // 転送表に無いキーは例外になる（いまのページの URL はここを通ってそのままオリジンへ進む）。
            location = await kvs.get(key);
        } catch (e) {
            location = null;
        }
        if (location) {
            return {
                statusCode: 301,
                statusDescription: 'Moved Permanently',
                headers: {
                    location: { value: location },
                    'cache-control': { value: 'public, max-age=3600' }
                }
            };
        }
    }

    if (uri.endsWith('/')) {
        request.uri = uri + 'index.html';
        return request;
    }
    const last = uri.substring(uri.lastIndexOf('/') + 1);
    if (last.indexOf('.') === -1) {
        request.uri = uri + '/index.html';
    }
    return request;
}
