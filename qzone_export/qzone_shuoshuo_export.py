#!/usr/bin/env python3
"""Export a QQ Zone user's 说说 (shuoshuo) in chronological order.

Uses your own logged-in browser cookie, so it can only fetch what your
account is already allowed to see on that user's QQ Zone.

Usage:
    pip install requests
    python qzone_shuoshuo_export.py --target 350904191 --cookie-file cookie.txt [--images]

Outputs (in ./qzone_<target>/):
    shuoshuo.json   raw-ish structured data, oldest first
    shuoshuo.md     readable Markdown, oldest first
    images/         post images (only with --images)
"""
import argparse
import json
import os
import re
import sys
import time
from datetime import datetime

import requests

LIST_URL = (
    "https://user.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6"
)
PAGE_SIZE = 20


def g_tk(skey: str) -> int:
    h = 5381
    for c in skey:
        h += (h << 5) + ord(c)
    return h & 0x7FFFFFFF


def parse_cookie(raw: str) -> dict:
    cookies = {}
    for part in raw.strip().split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            cookies[k.strip()] = v.strip()
    return cookies


def fetch_page(session, target, pos, gtk):
    params = {
        "uin": target,
        "ftype": 0,
        "sort": 0,
        "pos": pos,
        "num": PAGE_SIZE,
        "replynum": 100,
        "g_tk": gtk,
        "callback": "_preloadCallback",
        "code_version": 1,
        "format": "jsonp",
        "need_private_comment": 1,
    }
    r = session.get(LIST_URL, params=params, timeout=20)
    r.raise_for_status()
    m = re.search(r"_preloadCallback\((.*)\)\s*;?\s*$", r.text, re.S)
    data = json.loads(m.group(1) if m else r.text)
    if data.get("code") != 0:
        raise RuntimeError(f"QZone returned code={data.get('code')}: {data.get('message')}")
    return data


def pic_urls(msg):
    urls = []
    for p in msg.get("pic") or []:
        u = p.get("url3") or p.get("url2") or p.get("url1") or p.get("smallurl")
        if u:
            urls.append(u)
    return urls


def normalize(msg):
    rt = msg.get("rt_con") or {}
    return {
        "tid": msg.get("tid"),
        "timestamp": msg.get("created_time"),
        "time": datetime.fromtimestamp(msg.get("created_time", 0)).strftime("%Y-%m-%d %H:%M:%S"),
        "content": msg.get("content", ""),
        "forwarded": rt.get("content") or None,
        "location": (msg.get("lbs") or {}).get("name") or (msg.get("lbs") or {}).get("idname") or None,
        "source": msg.get("source_name") or None,
        "images": pic_urls(msg),
        "comment_count": msg.get("cmtnum", 0),
        "comments": [
            {
                "name": c.get("name"),
                "time": c.get("createTime2") or c.get("createTime"),
                "content": c.get("content"),
            }
            for c in (msg.get("commentlist") or [])
        ],
    }


def download_images(session, posts, outdir):
    imgdir = os.path.join(outdir, "images")
    os.makedirs(imgdir, exist_ok=True)
    for post in posts:
        local = []
        for i, url in enumerate(post["images"], 1):
            name = f"{post['time'][:10]}_{post['tid']}_{i}.jpg"
            path = os.path.join(imgdir, name)
            if not os.path.exists(path):
                try:
                    r = session.get(url, timeout=30)
                    r.raise_for_status()
                    with open(path, "wb") as f:
                        f.write(r.content)
                    time.sleep(0.3)
                except requests.RequestException as e:
                    print(f"  image failed: {url} ({e})", file=sys.stderr)
                    continue
            local.append(f"images/{name}")
        post["local_images"] = local


def write_markdown(posts, target, path):
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"# QQ空间说说 · {target}\n\n共 {len(posts)} 条，按时间从早到晚排列。\n\n")
        current_year = None
        for p in posts:
            year = p["time"][:4]
            if year != current_year:
                f.write(f"\n## {year}\n\n")
                current_year = year
            f.write(f"### {p['time']}\n\n")
            if p["content"]:
                f.write(p["content"].strip() + "\n\n")
            if p["forwarded"]:
                f.write(f"> 转发：{p['forwarded'].strip()}\n\n")
            for img in p.get("local_images") or p["images"]:
                f.write(f"![]({img})\n\n")
            meta = [x for x in (p["location"] and f"📍{p['location']}", p["source"]) if x]
            if meta:
                f.write(f"*{' · '.join(meta)}*\n\n")
            if p["comments"]:
                f.write(f"评论（{p['comment_count']}）：\n\n")
                for c in p["comments"]:
                    f.write(f"- **{c['name']}**（{c['time']}）：{c['content']}\n")
                f.write("\n")
            f.write("---\n\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--target", required=True, help="QQ number whose 说说 to export")
    ap.add_argument("--cookie-file", required=True, help="file containing your QZone Cookie header value")
    ap.add_argument("--images", action="store_true", help="also download post images")
    ap.add_argument("--delay", type=float, default=1.5, help="seconds between page requests")
    args = ap.parse_args()

    with open(args.cookie_file, encoding="utf-8") as f:
        cookies = parse_cookie(f.read())
    skey = cookies.get("p_skey") or cookies.get("skey")
    if not skey:
        sys.exit("Cookie is missing p_skey/skey — copy the full Cookie header after logging in to user.qzone.qq.com")
    gtk = g_tk(skey)

    session = requests.Session()
    session.cookies.update(cookies)
    session.headers.update({
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                      "(KHTML, like Gecko) Chrome/124.0 Safari/537.36",
        "Referer": f"https://user.qzone.qq.com/{args.target}/311",
    })

    posts, seen, pos, total = [], set(), 0, None
    while True:
        data = fetch_page(session, args.target, pos, gtk)
        if total is None:
            total = data.get("total", 0)
            print(f"Total 说说 reported: {total}")
        msgs = data.get("msglist") or []
        if not msgs:
            break
        for m in msgs:
            if m.get("tid") not in seen:
                seen.add(m.get("tid"))
                posts.append(normalize(m))
        pos += PAGE_SIZE
        print(f"  fetched {len(posts)}/{total}")
        if pos >= total:
            break
        time.sleep(args.delay)

    posts.sort(key=lambda p: p["timestamp"] or 0)

    outdir = f"qzone_{args.target}"
    os.makedirs(outdir, exist_ok=True)
    if args.images:
        download_images(session, posts, outdir)
    with open(os.path.join(outdir, "shuoshuo.json"), "w", encoding="utf-8") as f:
        json.dump(posts, f, ensure_ascii=False, indent=2)
    write_markdown(posts, args.target, os.path.join(outdir, "shuoshuo.md"))
    print(f"Done: {len(posts)} posts -> {outdir}/shuoshuo.md, shuoshuo.json")


if __name__ == "__main__":
    main()
