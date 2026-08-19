# OGP カード描画用フォント

SiteBuilder が OGP カード画像（1200×630 PNG）をビルド時にラスタライズする際に使う日本語フォント。
ブラウザ側は同じ書体を Google Fonts の CDN から読むが、サーバサイドのラスタライズには実ファイルが
必要なため、ここに同梱して exe と一緒に配布する。

サブタイトルには任意の漢字が現れるためサブセット化はできず、いずれもフルセットを置いている。

| ファイル | 用途 | 出自 | ライセンス |
|---|---|---|---|
| `KiwiMaru-Medium.ttf` | ブランド書体。カードの識別子・見出し・サイト名 | [Kiwi Maru](https://fonts.google.com/specimen/Kiwi+Maru) | SIL Open Font License 1.1（`OFL-KiwiMaru.txt`） |
| `NotoSansJP.ttf` | 本文書体。前置き・バッジ・帯グラフのラベル・ファクト行 | [Noto Sans JP](https://fonts.google.com/noto/specimen/Noto+Sans+JP)（可変フォント、既定インスタンスを使用） | SIL Open Font License 1.1（`OFL-NotoSansJP.txt`） |

いずれも SIL OFL 1.1 のため再配布可。同ライセンスは著作権表示とライセンス本文の同梱を求めるので、
`OFL-*.txt` はフォント本体とセットで維持すること。

書体を差し替える場合は `Rendering/OgCardRenderer.cs` のコンストラクタが読むファイル名も合わせて直す。
差し替え後は、ブランド書体に無い文字がビルドログに警告として出ないか確認する
（未収録文字があるとカード上で豆腐になる）。
