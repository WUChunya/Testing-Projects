# CAMPUS — 261019_Campus_Layouts 模型（plugin v0.9.2）

| 文件 | 内容 |
|---|---|
| `CAMPUS_Program_Diagrams_261019.pdf` | 33 页，按插件编号排序。11x17 横向，按用地红线居中；平面 / 轴测 / 立面 1" = 100'-0"，剖面 1" = 50'-0"；爆炸轴测和分层轴测放不下，按 Fit content 出图（比例不变）。 |
| `CAMPUS_261019_AI_Export.zip` | AI 原图：解压后在 Illustrator 中运行 `ProgramDiagram.jsx`，在同一文件夹生成分层 .ai。附面积表 CSV、export_log、插件设置。 |
| `CAMPUS_261019_PD.3dm` | 加了场地、停车楼和插件设置的模型（原物件都在，只移了图层）。 |
| `scripts/` | `build_campus2.sh <261019_Campus_Layouts.3dm> <Town .ai> <repo> <工作目录> scripts/campus2_base_settings.json` 从原始文件重建。 |

## 处理
- **轴测角度**：按截图，从西南方向看（方位 234°，俯角 41°）。
- **用地**：Town 总平面的 4 块实线地块 = MX3 用地红线（建筑、停车楼、河边、南侧小块），虚线 = 退线；地块内画项目场地，周边街道、河、背景建筑、行道树同前。
- **停车楼**：模型里只有平面线（FLOR-OTLN），按其外轮廓建 5 层 × 10'-6" 的体量（PROGRAM::Parking，program PARKING，约 50,400 sf/层，5 层合计约 252,000 sf）。面积表按建筑楼层统计，只落在 L1–L3。
- **房间名**按 layouts：MEP SUPPORT、STORAGE、LOADING DOCK、CORE LABS、COMBINABLE CLASSROOMS、AUDITORIUM、FITNESS、QUANTUM HIGH MIP、SECURITY、COFFEE KIOSK、PROTOTYPING / FABRICATION、PARTNER / INCUBATOR、CONFERENCE、DINING PREP、RESEARCH、SEMINAR、EXECUTIVE BRIEFING。礼堂和 Prototyping 两层通高。
- 1 ft 高 / 单面的开放式 program 拉成整层；场地外的柱子和远处的幕墙 block 副本、重复的 Shell Space 移到 `OUTSIDE BUILDING (LEFTOVERS)`。

## 已知问题（下一版修）
- L3 中庭开洞（圆角洞口）被整层 circulation 填满；L2、L4 的开洞正常。
- 剖面中的停车楼按一个 5 层体量画，没有分层。
