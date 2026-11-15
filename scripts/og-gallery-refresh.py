# -*- coding: utf-8 -*-
"""OGP カードの見本のページ（artifact の HTML）の各カードを、出力フォルダの OGP カード画像に差し替える。

見本のページは、カードごとに <a href="https://precure.tv/{パス}"><img src="{画像のフォルダ}/{番号}.png"> を並べた形。
リンク先のパスから出力フォルダの og/{パス}.png（トップは og/home.png）を引き、新しい画像のフォルダへ同じ番号で写す。
HTML の画像の参照も新しいフォルダに書き換え、出力先に index.html として書く（配信時に付く骨組みは外す）。
出力先の index.html と画像のフォルダを、artifact へそのまま publish する。

使い方:
  python -I scripts/og-gallery-refresh.py <見本の HTML> <サイトの出力フォルダ> <出力先> [--from img5] [--to img6] [--lead "説明文"]
    --from : 今の HTML が参照している画像のフォルダ（既定 img5）
    --to   : 新しく写す画像のフォルダ（既定は --from と同じ＝同じ名前で上書き）
    --lead : 見本の冒頭の説明文（<p class="lead">）を差し替える文
"""
import argparse
import io
import os
import re
import shutil
import sys
import urllib.parse

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

ap = argparse.ArgumentParser()
ap.add_argument("html")
ap.add_argument("site_dir")
ap.add_argument("out_dir")
ap.add_argument("--from", dest="src_folder", default="img5")
ap.add_argument("--to", dest="dst_folder", default="")
ap.add_argument("--lead", default="")
a = ap.parse_args()
dst_folder = a.dst_folder or a.src_folder

s = open(a.html, encoding="utf-8").read()
os.makedirs(os.path.join(a.out_dir, dst_folder), exist_ok=True)

missing, count = [], 0
pattern = r'<a href="https://precure\.tv(/[^"]*)" target="_blank" rel="noopener"><img src="' + re.escape(a.src_folder) + r'/(\d+)\.png"'
for m in re.finditer(pattern, s):
    path, num = urllib.parse.unquote(m.group(1)), m.group(2)
    folded = "home" if path == "/" else path.strip("/")
    src = os.path.join(a.site_dir, "og", *folded.split("/")) + ".png"
    if not os.path.exists(src):
        missing.append((num, path, src))
        continue
    shutil.copyfile(src, os.path.join(a.out_dir, dst_folder, num + ".png"))
    count += 1

s = s.replace(f'src="{a.src_folder}/', f'src="{dst_folder}/')
if a.lead:
    s = re.sub(r'<p class="lead">.*?</p>', lambda _: f'<p class="lead">{a.lead}</p>', s, count=1, flags=re.S)
# 保存した HTML の先頭にある配信時の骨組みは外して、<title> からの中身だけを書く
i = s.find("<title>")
body = s[i:] if i >= 0 else s
body = re.sub(r"</body></html>\s*$", "", body)
with open(os.path.join(a.out_dir, "index.html"), "w", encoding="utf-8") as f:
    f.write('<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' + body)

print("copied", count)
for x in missing:
    print("MISSING", *x)
