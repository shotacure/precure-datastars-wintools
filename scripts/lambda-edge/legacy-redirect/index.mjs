// precure.tv の旧 ID URL 転送（Lambda@Edge、origin-request、ランタイム Node.js 24.x（x86_64）、リージョン us-east-1）。
//
// /persons/123/ /characters/123/ /companies/123/ /books/123/ のような「区分 + 数字だけ」の旧 URL に来た
// リクエストを、名前（書籍はコード）ベースの新 URL へ 301 で転送する。
// あわせて、人物の旧名 URL（/people/{旧名義}/。最新名義が変わって URL が変わった人物の、本番で公開済みの URL）と
// キャラの旧名 URL（/characters/{旧キャラ名}/。キャラ名を変えて URL が変わったキャラの、本番で公開済みの URL）も
// いまの URL へ 301 で転送する。表に無い名前の URL はそのままオリジンへ通す（いまのページはそのまま返る）。
// 廃止した役職詳細（/creators/roles/{code}/。歌唱系は /creators/singers/ に集約）も表にあれば転送する。
// viewer-request の CloudFront Function が先に "…/index.html" へ書き換えるので、その形も受ける。
//
// 転送表は SiteBuilder がビルドのたびにサイト出力の _edge/legacy-redirects.json に書き出し、
// 通常のデプロイで S3（非公開バケット）へ上がる。本関数はそれを S3 から読み、数分間メモリに保持する
// （表の更新はデプロイだけで反映され、関数の作り直しは要らない）。
// 形式：{"/persons/123": "/people/%E9%AB%98…/", "/people/旧名": "/people/%E6%96%B0…/", "/characters/旧名": "/characters/%E6%96%B0…/", …}
// （キーは末尾スラッシュ無しの旧パス。名前のキーはデコード済みの素の文字列なので、リクエスト URI もデコードして引く）。
//
// 旧 URL 以外のリクエストは何もせずそのままオリジンへ通す。表を読めないときも通す（旧ページが無ければ 404）。
// Lambda@Edge は環境変数を使えないため、バケット名等は定数で持つ。
// 実行ロールには s3:GetObject（arn:aws:s3:::{バケット名}/_edge/*）と CloudWatch Logs への書き込み権限が要る。
// BUCKET / BUCKET_REGION はプレースホルダ。コンソールへ貼るときに実値へ置き換える（実値はリポジトリに入れない）。
import { S3Client, GetObjectCommand } from '@aws-sdk/client-s3';

const BUCKET = 'your-bucket-name';
const BUCKET_REGION = 'your-bucket-region';
const MAP_KEY = '_edge/legacy-redirects.json';
const MAP_TTL_MS = 5 * 60 * 1000;
const LEGACY_ID_PATH = /^\/(persons|characters|companies|books)\/(\d+)(?:\/(?:index\.html)?)?$/;
// 名前 URL は 1 階層だけ（/characters/guests/{slug}/ のような 2 階層のパスは対象外）。
const NAME_PATH = /^\/(people|characters)\/([^\/]+)(?:\/(?:index\.html)?)?$/;
// 役職詳細 URL（表に載っている廃止ページだけが転送される）。
const ROLE_PATH = /^\/creators\/roles\/([^\/]+)(?:\/(?:index\.html)?)?$/;

/** リクエスト URI から転送表のキーを作る。対象外の URI は null。 */
function mapKeyOf(uri) {
    const id = uri.match(LEGACY_ID_PATH);
    if (id) return '/' + id[1] + '/' + id[2];
    const name = uri.match(NAME_PATH);
    if (name) {
        try {
            return '/' + name[1] + '/' + decodeURIComponent(name[2]).normalize('NFC');
        } catch (e) {
            return null; // 不正なパーセントエンコードは転送しない
        }
    }
    const role = uri.match(ROLE_PATH);
    if (role) return '/creators/roles/' + role[1];
    return null;
}

const s3 = new S3Client({ region: BUCKET_REGION });
let cachedMap = null;
let cachedAt = 0;

async function loadMap() {
    if (cachedMap && Date.now() - cachedAt < MAP_TTL_MS) return cachedMap;
    const res = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: MAP_KEY }));
    cachedMap = JSON.parse(await res.Body.transformToString());
    cachedAt = Date.now();
    return cachedMap;
}

export const handler = async (event) => {
    const request = event.Records[0].cf.request;
    const key = mapKeyOf(request.uri);
    if (!key) return request;

    let map;
    try {
        map = await loadMap();
    } catch (e) {
        console.error('legacy redirect map load failed', e);
        return request;
    }

    const location = map[key];
    if (!location) return request;

    return {
        status: '301',
        statusDescription: 'Moved Permanently',
        headers: {
            location: [{ key: 'Location', value: location }],
            'cache-control': [{ key: 'Cache-Control', value: 'public, max-age=3600' }]
        }
    };
};
