-- v1.4.2 データ修正：Amazon の「画像はありません（No Image Available）」プレースホルダ画像を
--   カバー画像として保存してしまっていた商品について、未取得状態（NULL）へ戻す。
--   プレースホルダ画像 ID は 01MKUOLsA5L（例: https://m.media-amazon.com/images/I/01MKUOLsA5L._SL500_.gif）。
--   PaApiClient 側でプレースホルダを取り込まないフィルタを入れたため、本修正で既存の取り込み済みを除去すれば、
--   次回の AmazonSync 取得時に実ジャケットが取れれば差し替わり、取れなければ NULL のまま（=未取得）になる。
--   対象は cover_image_url がプレースホルダ ID を含む行のみ。SELECT のみの health-check ではなく更新を行うため、
--   実行は root 権限で明示的に行う。

UPDATE `products`
   SET `cover_image_url`        = NULL,
       `cover_image_source`     = NULL,
       `cover_image_fetched_at` = NULL
 WHERE `cover_image_url` LIKE '%01MKUOLsA5L%';
