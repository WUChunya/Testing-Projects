# RIVER 2026-10-16 — 场地更新与出图（plugin v0.8.9）

| 文件 | 内容 |
|---|---|
| `RIVER_Program_Diagrams.pdf` | 30 页，按插件编号排序：00 总平面（含屋顶视图、图例）→ 01 各层平面 → 02 剖面 A/B → 03 场地轴测、叠合轴测 → 04 爆炸轴测 A/B、分层轴测 → 05 Program 分类图 → 06 饼图 → 07 立面 E1/E2。左侧书签可跳页。 |
| `RIVER_2026_1016_FINAL_SITE.3dm` | 原模型 + `SITE CONTEXT` 图层组（见下），插件设置已写入。 |
| `scripts/` | 生成过程（可复现）：`extract_ai.py`（读 .ai 矢量）→ `site_to_3dm.py`（配准并写入 3dm）→ `update_settings.py`（图层角色、轴测视角）→ `export_previews.cs`（PD.Core 出 SVG）→ `svgs_to_pdf.mjs`（逐页转 PDF）。 |

## 场地（来自 `20261016_Site_Plan_River_1.100.ai`）
按地块红线 7 个角点与 `Plats::MX3` 配准：比例 1" = 100'，旋转 0°，角点误差 0.06 ft。.ai 的叠放顺序用布尔差集整理成互不重叠的区域，所以插件的绘制顺序不会把路径压在绿地下面。

| 图层 `SITE CONTEXT::` | 角色（关键词自动识别） | 数量 |
|---|---|---|
| `CITY BLOCKS` | BG.Hardscape | 5 |
| `STREET` | BG.Road | 1 |
| `SIDEWALK` | BG.Hardscape | 12 |
| `WALKWAY`（滨河步道） | BG.Hardscape | 2 |
| `BANK LANDSCAPE`（河岸绿带） | BG.Landscape | 4 |
| `RIVER` | BG.Water | 1 |
| `CONTEXT BUILDINGS`（挤出 40'，.ai 无高度，可在 Rhino 中改） | BG.Building | 14 |
| `TREES`（圆，半径按树冠面积，6'–18'） | BG.Tree | 298 |

斑马线未导入（插件不读这一类）。

## 轴测视角
按截图反推：观察者在东北偏北（方位角约 67°），俯角约 49°。
- 插件设置：Axon view = **Viewport**，并存了这个方向。
- 3dm 里新建了命名视图 **`PD AXON (site plan view)`**（平行投影）。在 Rhino 里恢复这个视图并保持为当前视口后再 Export，轴测就和截图一致。

## 说明
PDF 是插件在 Linux 上生成的 SVG 预览（与 `_preview` 文件夹内容相同），不是 Illustrator 生成的 .ai。在 Windows 上用 v0.8.9 打开这个 3dm 导出，得到的是同样的图。
