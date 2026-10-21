# RIVER — 2026-10-21 VP 批注：模型修改 + 总平面 / 平面（plugin v0.9.2，插件未改）

| 文件 | 内容 |
|---|---|
| `RIVER_Site_Plan_and_Plans_1021.pdf` | 10 页：00 总平面（SITE PLAN、屋顶视图、图例）+ 01 平面（L1、L1M、L2–L4、总览、图例）。11x17 横向，按用地红线（MX3）居中，1" = 100'-0"。 |
| `RIVER_1021_AI_Export.zip` | AI 原图：解压后在 Illustrator 中运行 `ProgramDiagram.jsx`，生成 00_Site_Plan.ai、01_Plans.ai（分层、全局色板）。 |
| `RIVER_1021_Illustrator_Editable.zip` | 不跑脚本也能编辑：每张图一个 SVG（按 AI 图层分组）+ 矢量 PDF。 |
| `RIVER_2026_1021_PD.3dm` | 修改后的模型 + 插件设置（11x17 / 1"=100'）。被替换的物件都没删，移到 `OUTSIDE BUILDING (LEFTOVERS)`。 |
| `scripts/` | `build_r21.sh <RIVER_2026_1020_checkin.3dm> <repo> <工作目录>` 从 10-20 的原始模型重建（`r21_edits.py` 建筑 + `r21_site.py` 场地 + `r21_more.py` 露台/MEP/名称 + `waterfront3.py` 滨水公园）。 |

注意：10-20 你们在 Illustrator 里改过的总览（PROTOTYPING、MAIN ENTRY 等手加标注）没有套用到这一版——西侧体量移动了，底图不能只换背景，需要在新导出上重新加。

## 批注逐条处理
| 批注 | 处理 |
|---|---|
| ACCESS TO RIVER WALK（西侧） | **按你们的要求不改 program**：西侧体量从 L1 到屋顶（两个核心筒、玻璃、楼板、屋顶机房一起）整体向东平移 24'，西边空出 24' 宽、上下通高的通道（`PROJECT SITE::WALKS`），从南侧落客区直通北面滨水步道。平移后和斜向体量重叠的部分让给西侧体量（斜向体量 6 块被切：L1 支持用房、L2 COLLAB/实验支持、L3 COLLAB/实验支持）；L2/L3 西侧体量东南角因此向外伸出约 24'。玻璃外皮按新外轮廓重画，转角倒圆（也回应 L3 "TRY ROUNDING THE CORNERS"）。通道太窄放不下名称，没有标字。 |
| SECONDARY DROP OFF | 南侧街边 108'×12' 港湾式落客车道 + 沿车道的落客步道（标 SECONDARY DROP OFF）；原 "LOADING AND BOH SERVICE" 铺装去掉名称。 |
| LARGER LOADING | 健身房和坡地之间的铺装扩成装卸场（约 17,800 sf，标 LOADING），西侧体量南边 4 个卸货车位。 |
| SHOW A SERVICE ROAD, GAP BETWEEN BUILDING AND PARKING / WIDE ENOUGH FOR A SERVICE ROAD | 24' 服务道路（`PROJECT SITE::SERVICE ROAD`）：街道 → x=349' 向北（装卸场与坡地花园之间）→ 斜向穿过裙房停车 → 建筑与停车楼之间（y≈268'）→ 停车楼东侧 24' 带 → 回到街道。停车楼 L1/L1M 退到 y=256'，与建筑之间留出道路和种植带；裙房停车、L1M 停车和楼板按道路切开。 |
| ROUND CORNERS, MORE OF AN OBJECT（停车楼） | 停车楼所有楼层 15' 圆角。 |
| MARK RAMP ABOVE | L1 上用虚线（NC.Massing）标出裙房停车上方的坡道。 |
| LARGER DROP OFF, MORE TREES, LANDSCAPE | +20' 的回车转盘从 18' 岛扩到外径 44' / 岛 20'（种植岛），周边和坡地花园加树。 |
| "GARDEN" / ANOTHER "GARDEN" | 停车楼与建筑之间、停车楼北侧东边条带做成 GARDEN（种树）；南侧坡地景观命名 GARDEN 并加树。 |
| SIMPLER STAIR | 大台阶的 3D 踏步不再画（DETAIL::TREADS → Ignore），L1/L2 用简单楼梯符号（轮廓 + 踏步线 + 方向箭头）。 |
| STAIR（L2/L3 COLLAB） | COLLAB 里加一部连接 L2–L3 的楼梯符号。 |
| SHOW SEATING | 礼堂 L1/L2 画阶梯座位弧线和舞台。 |
| OUTDOOR AREAS, MARK WALKS, A PAVILION OR TWO / RIVER WALK | 滨水步道改名 RIVER WALK；在批注位置（建筑北侧）加第二个 GARDEN PAVILION；其他步道已有名称。 |
| ADD TERRACE / TERRACE / SHOW TERRACE | 新图层 `PROGRAM::Terrace`（GREEN_ROOF，1' 厚）：L2 西北（1,700 sf）、L3 西北转角（5,900 sf）、L3 礼堂屋顶周边（6,600 sf）、L4 行政办公周边（7,400 sf）。 |
| GREEN ROOF ON TOP | 停车楼屋顶绿屋面（31,600 sf，随 L4 出图）。 |
| TEST ONE MEP LARGER | 斜向屋顶机房向西南加长（13,000 → 16,500 sf），机房屋面板跟着改。 |
| DELETE SQUARE LINES | L4 两块方形屋面板（西侧机房下、斜向机房下）移走。 |
| 名称 | L1：斜向 LAB → CORE LAB，支持带 → SERVICE，北侧通高 lab → PROTOTYPING / CORE LAB；L2：MAKER SPACE 位置 → CORE LABS，INCUBATOR，西侧 OPEN OFFICE → OFFICE，西侧 LAB SUPPORT → RESEARCH，COLLAB；L3：西侧 → OFFICE / RESEARCH，北侧办公 → COMPUTATIONAL，COLLAB。L1 斜向几块 lab 名称已写入模型，但块太窄放不下 CORE LAB，插件没标字，需要在 AI 里补。 |
| MAIN ENTRY AND SECURITY | 入口内侧加 24'×16' SECURITY 房间（L2）。 |

## 没有改的（需要你们确认）
- L2 楼梯开洞 "ROUND"：开洞是楼板的形状，这次没动。
- L3 两个红框（北侧办公中的蓝色条、东端粉色条）、L3 "OPEN?" 北侧虚线：意思不确定，没改。
- L2 主入口凸出部分仍在停车楼上方（y≈226'，原模型如此）。
- 转盘种植岛在楼上平面里很淡（楼上的场地底图都是淡化的）。
