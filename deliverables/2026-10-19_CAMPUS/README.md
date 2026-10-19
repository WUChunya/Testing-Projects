# CAMPUS 方案（2026-10-19，plugin v0.9.2）

输出与 RIVER TK 完全一致：同样的图纸清单、编号、图层 / 色板 / 字体、轴测视角，11x17 横向按用地红线居中，平面 / 轴测 / 立面 1" = 100'-0"，剖面 1" = 50'-0"，爆炸轴测放不下时 Fit content（比例不变）。

| 文件 | 内容 |
|---|---|
| `CAMPUS_Program_Diagrams.pdf` | 32 页，按插件编号排序（00 总平面 → 07 立面）。 |
| `CAMPUS_AI_Export.zip` | AI 原图：解压后在 Illustrator 里运行 `ProgramDiagram.jsx`，在同一文件夹生成分层的 .ai。附面积表 CSV、export_log、插件设置。 |
| `CAMPUS_2026_1019_PD.3dm` | 加了场地和插件设置的模型（原物件都在，只改了图层，见下）。 |
| `scripts/` | `build_campus.sh <CAMPUS_2026_1019.3dm> <20261016_Site_Plan_Town_1.100.ai> <repo> <工作目录>` 从原始文件重建模型：`town_fit.py`（对位）→ `town_to_3dm.py`（场地图层）→ `campus_settings.py`（图层角色、剖面、输出）→ `campus_solids.py`（开放式 program 拉成整层）。 |

## 模型处理
- **总平面对位**：Town 总平面（1"=100'）与模型 SURROUNDING::LOT 的地块轮廓对齐，1 pt = 100/72 ft、无旋转，本地块误差 < 0.3 ft。新增与 RIVER 相同的图层：`SITE CONTEXT::*`（街区、道路、人行道、河、河岸绿地、背景建筑 40'、行道树）、`PROPERTY LINE`、`SETBACK`、`PROJECT SITE::PLAZA`（地块内地面）、`DETAIL::CUTLINE`（剖面 A/B、立面 E1/E2）。其它开发地块按街区灰色画。原 `SURROUNDING::*` 不画。
- **图层角色**（同 RIVER）：PROGRAM::* = program；CORE = core；FLOOR PLATE = 楼板；Columns = 结构；`Facade::Glass` = 立面玻璃（紧贴 program 的幕墙）；Mullion / Glass Joint / Wood Verticals 不画；ENTOURAGE::PEOPLE 画人、TREES 画树，CARS 和礼堂座椅不画；Outdoor Circulation = 室外铺装（平台）。
- **开放式 program**：1 ft 高或单面的 Circulation / Break Room（没有外墙分割的开放区域）按所在楼层拉成整层高实体（楼板顶 → 上层楼板底），重复的只保留一个。原面片移到 `OUTSIDE BUILDING (LEFTOVERS)` 图层。
- **移到 `OUTSIDE BUILDING (LEFTOVERS)`（不画、未删除）**：建筑南侧场地外的 83 根柱子；北侧 200 ft 外悬空的一块 Shell Space 副本；与南侧 Shell Space 重合的 Brep 副本；绿屋面下两块 Shell Space 平面。`PROGRAM::Glass` 在现在的体量东侧、对不上任何 program，像旧方案的玻璃，设为不画。
- **两层通高**：Seminar Room（礼堂，L1–L2）和东侧 lab 设为通高，L2 显示为挑空、不计面积。两个 Shell Space 的开口挤出体已封顶；北侧（L5 起 25' 高）只算 L5。

## 面积（SF，插件统计）
| 层 | 标高 | 合计 |
|---|---|---|
| L1 | 1'-6" | 89,232 |
| L2 | 21'-10" | 78,141 |
| L3 | 41'-6" | 91,284 |
| L4 | 57'-6" | 96,103 |
| L5 | 73'-6" | 59,910（含绿屋面 15,913） |
| L6 | 94'-2" | 16,493（Shell / 机房 + core） |

## 需要确认
- `PROGRAM::Glass` 是否确实是旧方案残留（是的话可以删掉）；`PROGRAM::Support` 里 +65' 的单面和 L2 的闭合曲线没有当作房间。
- L2 中间那块（PROGRAM::Support 曲线所在、circulation 面片留空处）画成了 OPEN TO BELOW；如果它是房间请告诉我。
- 剖面用 1"=50'（1"=100' 时房间名放不下）；要统一 1"=100' 改插件 Output 的 Section scale 即可。
