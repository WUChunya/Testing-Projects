# QQ空间说说导出

把某个 QQ 号的说说按时间顺序（从早到晚）导出为 Markdown 和 JSON，可选下载图片。

脚本使用**你自己登录后的 Cookie**，所以只能导出你的账号本来就有权限看到的内容（对方设置了仅自己可见/指定好友可见的说说，导不出来）。

## 使用步骤

1. 安装依赖：`pip install requests`
2. 用浏览器登录 https://user.qzone.qq.com/350904191/311
3. 按 F12 → Network（网络）→ 刷新页面 → 点任意一个 `qzone.qq.com` 的请求 → 在 Request Headers 里复制 `Cookie:` 后面的整串内容，保存为 `cookie.txt`（需包含 `p_skey` 或 `skey`）
4. 运行：

   ```bash
   python qzone_shuoshuo_export.py --target 350904191 --cookie-file cookie.txt --images
   ```

5. 结果在 `qzone_350904191/` 目录：
   - `shuoshuo.md` — 按年份分组、按时间排序的可读版本（含图片、转发、地点、评论）
   - `shuoshuo.json` — 结构化数据
   - `images/` — 图片（加 `--images` 时）

## 注意

- `cookie.txt` 等同于你的登录凭证，不要分享或提交到仓库。
- 默认每页间隔 1.5 秒，可用 `--delay` 调整；请求太快可能被临时限制。
- 如果提示 `code=-3000` 之类的错误，通常是 Cookie 过期，重新登录复制即可。
