// precure.tv の CloudFront Function（viewer-request。ランタイムは cloudfront-js-1.0 / 2.0 のどちらでも動く書き方）。
//
// 1. 内部ファイルの非公開化
//    /_edge/ 配下（Lambda@Edge が S3 から読む旧 URL の転送表 _edge/legacy-redirects.json など）は
//    閲覧者に見せないため 404 を返す。
//
// 2. 人物の区分名の付け替え（/persons/ → /people/）
//    /persons/{名前}/ で公開した期間のある URL を、同じ名前の /people/{名前}/ へ 301 で転送する。
//    数字だけの旧 ID URL（/persons/123/）はここでは触らず、Lambda@Edge の転送表に任せる。
//
// 3. サブディレクトリの index 解決
//    S3 REST オリジンはサブパスの index を解決しないため、"/…/" と拡張子なしパスを "…/index.html" に書き換える。
//
// 旧 ID URL（/persons/123/ 等）の 301 転送は、この関数ではなく origin-request の Lambda@Edge
// （scripts/lambda-edge/legacy-redirect/index.mjs）が行う。転送表が Function のコード上限（10KB）に収まらないため。
//
// 関数の更新・公開は AWS コンソールで行う（1 ビヘイビアの viewer-request に付けられる関数は 1 つだけなので、
// 処理はこの 1 本に統合する）。
function handler(event) {
    var request = event.request;
    var uri = request.uri;

    if (uri.indexOf('/_edge/') === 0) {
        return { statusCode: 404, statusDescription: 'Not Found' };
    }

    if (uri.indexOf('/persons/') === 0) {
        var rest = uri.substring('/persons/'.length);
        if (!/^\d+(\/(index\.html)?)?$/.test(rest)) {
            var location = '/people/' + rest;
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

    if (uri.endsWith('/')) {
        request.uri = uri + 'index.html';
        return request;
    }
    var last = uri.substring(uri.lastIndexOf('/') + 1);
    if (last.indexOf('.') === -1) {
        request.uri = uri + '/index.html';
    }
    return request;
}
