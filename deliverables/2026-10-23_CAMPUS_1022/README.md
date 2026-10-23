# CAMPUS — 2026-10-22 模型：按平面图文字命名、清理、河边绿地做成公园（plugin 0.10.0 + 标注开关）

| 文件 | 内容 |
|---|---|
| `CAMPUS_2026_1022_PD.zip` → `CAMPUS_2026_1022_PD.3dm` | 在你们的 CAMPUS_2026_1022 上修改后的模型。没有删除任何物件：被替换 / 清理的都移到隐藏的 `OUTSIDE BUILDING (LEFTOVERS)` 图层（Ignore）。 |
| `CAMPUS_1022_Program_Diagrams.pdf` | 插件输出：总平面（首层 / 屋顶）、L1–L5 和屋顶平面、平面总览、剖面 A–C、场地轴测、叠层轴测、爆炸轴测 A / B、分层轴测、立面 E1 / E2。11x17，1" = 100'-0"（剖面同你们的设置 1" = 100'），放不下的轴测用 Fit content（比例不变）。 |
| `pd_settings_CAMPUS_1022.json` | 这个模型的插件设置（已写进 3dm；第 8 页 Load settings… 也可载入）。 |
| `scripts/` | `build.sh <CAMPUS_2026_1022.3dm> <repo> <工作目录>` 从你们的模型重建：`c22_clean.py`（命名 + 清理 + 设置）→ `c22_park.py`（公园）。`Program.cs` / `harness.csproj` 是 Linux 上的导出程序（`PD_LABEL_NAMED=1` = 本轮的标注规则），`svgs_to_pdf.mjs` + `merge_pdf.py` 合成 PDF。 |

## 1 房间名 = 平面图页面上的文字
模型里 Ground … 6th Floor 六个 layout 页面都是 1" = 100' 的 detail。把每页文字按页面坐标换算回模型（用页面上画的楼层外轮廓对位，误差 < 1 ft），落在该层哪个房间里就给哪个房间命名（物件名 + 插件第 4 页名称表）：

| 层 | 名称 |
|---|---|
| L1 | CORE LABS（北）、QUANTUM HIGH MIP、FITNESS、SECURITY、COFFEE KIOSK、MICRON GALLERY、PROTOTYPING / FABRICATION、AUDITORIUM、COMBINABLE CLASSROOMS、LOADING DOCK、STORAGE、MEP SUPPORT、CORE LABS（南） |
| L2 | RESEARCH、OUTDOOR TERRACE、DINING TERRACE、DINING PREP、CONFERENCE、PARTNER / INCUBATOR |
| L3 | RESEARCH ×2、CONFERENCE、SEMINAR |
| L4 | RESEARCH ×2、EXECUTIVE BRIEFING ×2 |
| L5 | RESEARCH、ROOFTOP TERRACE |
| 6th | TRELLIS（两处棚架物件，页面写的是 "Trelis"） |

- 首层 "Core Labs" 和 "Quantum High MIP" 落在同一个 Lab 物件里：按中间走廊切成两个体块，北 CORE LABS 5,184 sf、南 QUANTUM HIGH MIP 5,940 sf。
- Auditorium（页面注明 Double height）和 Prototyping（二层页面为挑空）设为两层通高。
- 三个露台（Dining / Outdoor / Rooftop Terrace）单独设为 Green Roof / Sky Garden（别名 TERRACE），否则会被本层 circulation 盖住、名字显示不出来；Micron Gallery 设为 Interaction / Collab（原来是 Unassigned）。
- 没有物件对应的文字：首层 "Underground Support Passage"（地下，在中庭下方）、"PARKING" / "ALTERNATIVE PARKING LOCATION"（场地注记）；4th 页面画板外（x < 0）的 "Typical Research Floor" 备选方案没有出图，不处理。
- 有些文字是 "Partner / Incubator (Lab / Ofiice)"，名字用 PARTNER / INCUBATOR（括号说明省略，拼写更正）。

## 2 出图标注（你 10-23 的参考图）
- 不用任何引线；名字放不下先缩小字号（最小 6 pt @ 1"=45'，即 1"=100' 下约 2.7 pt），仍放不下就不写（如 L1 的 SECURITY）。
- 只写有专名的房间（模型物件名，或第 4 页里不同于 program 名称的名字）。只有 program 通用名的（OFFICE、LOBBY / BREAK、CIRCULATION、CORE、STAIR / TRANSPORTATION、OPEN TO BELOW）不写，靠图例。同名房间每块都写（如 L3 / L4 南北两个 RESEARCH）。平面和轴测都按这个规则。
- 插件里对应的是 `styles/style_default.json` → `plan` 的四个键：`labelUniform: false`、`labelLeaders: false`、`labelNamedOnly: true`、`labelMinPt: 6`。插件默认仍是原来的做法（统一字号 + 引线）；如果以后都要这样出图，把这四个值改掉即可（或告诉我，下一版改成默认）。

## 3 清理（全部移到 LEFTOVERS，未删除）
- 完全重复的物件：礼堂座椅 450 把（每把都有一份重叠副本）、停车线 7 条、立面 Glass Joint 2 个、Grand Stairs 1 块。
- `PROPERTY LINE` 上 +213 ft 高处的一块面；L2 楼板 5a04be12 里一块 1 sf、在 (6531, 4724) 的游离面（楼板重建为 mesh）；L5 研究室内部的一条 Outdoor Circulation 条带（5th 页面上没有）；Micron Gallery 实体下面的第二条轮廓线。
- `PROGRAM` 图层上 7 条其实是 Ground 页面上的箭头（图纸空间）→ 移回 `TEXT`。
- 剖面 / 立面线（原在 `PROGRAM::INTERACTIVE`）→ `DETAIL::CUTLINE`。插件设置里保存的剖切线坐标和曲线现在的位置不一致（E1 原坐标在建筑内部），已按曲线现位置更新，方向不变。
- 单面 / 薄片建模的 Circulation、Break Room（13 块）按所在层拉成整层体块（楼板顶 → 上层楼板底，开洞保留），和 10-19 CAMPUS 的做法一致；否则首层 circulation 不显示。
- 停车场的停车线（`SURROUNDING::PARK`）原来设为 Landscape，出图是绿色线 → 改为硬质线。
- 层高改到楼板顶：L1 1'-6"、L2 21'-6"、L3 41'-6"、L4 57'-6"、L5 73'-6"（原设置 0 / 20 / 40 / 57.5 / 72，比楼板低 1'-6"，插件因此认不出 L2 / L3 / L5 楼板的开洞，L3 中庭被画成了 circulation）。
- 轴测视角：NE → SW（你 10-23 指出轴测是从对角看过来的）。

## 4 绿色部分 → 河边公园（参考 RIVER 1022 模型的滨水公园）
`SURROUNDING::GRASS`（约 153,700 sf）移到 LEFTOVERS，换成和 RIVER 相同图层 / 材质的公园（`PROJECT SITE::RIVERFRONT PARK::*`、`PROJECT SITE::PICNIC::*`）；外圈的 LOT 人行道保留：
- **RIVER WALK**：12' 曲线步道沿河一侧贯通南北，两端接外圈人行道；两条 **RIVER LINK**（12'）穿过河岸绿化接到河边已有步道。
- **GARDEN WALK** ×3：12' S 形步道，从街道一侧的 **PARK ENTRY** 小广场（分别对着建筑南翼、中庭、北翼）通到 RIVER WALK。中间入口广场有 **SPLASH PAD**（一圈水柱）。
- **EVENT LAWN**（带三座 **PLAY HILLS**，画等高线）、三块 **PICNIC LAWN**。
- **RAIN GARDEN**（沿 RIVER WALK）+ **STEPPING STONES**；**RIVER OVERLOOK** 木平台；**RIVER SWINGS**（秋千廊架，4 个秋千）。
- **HAMMOCK GROVE**（树间吊床）、**FLOWERING GROVE**；三个圆形 **PICNIC** 小广场（铺地、C 形木座椅、桌凳、种植环和树，同 RIVER 1022 的野餐区）。
- 其余为 **RIVER MEADOW**；树 103 棵、长凳 20 条、人。草地按高度叠放（草甸在下，草坪 / 树林在上），不切缝。

## 待确认
- `Grand Stairs - Copy` 和 `Grand Stairs - Copy 2` 在首层到二层是两套重叠的楼梯（宽窄不同），不确定哪套是现行方案，都保留了。
- 6th 页面的两个 Shell Space 是开口的面（没有封顶），插件当作体块处理正常，未改。
- 剖面 B / C 的剖切线很长（约 1,780 ft），在 11x17 上建筑偏小；需要的话缩短剖切线或把剖面比例改成 1" = 50'。

## 第二版（REV1）：按 COM_261021_Atrium_Scheme 批注修改
| 文件 | 内容 |
|---|---|
| `CAMPUS_2026_1022_REV1_PD.zip` → `.3dm` | 在上一版模型上按批注修改（`scripts/c23_comments.py`），并加了屋顶、露台、庭院景观（`scripts/c23_landscape.py`）。被替换的物件同样移到 LEFTOVERS。按你的要求本轮不出图。 |
| `pd_settings_CAMPUS_1022_REV1.json` | REV1 的插件设置（已写进 3dm）。 |

批注页就是模型自己的 1" = 100' layout 页面，所以批注坐标直接换算成模型坐标。手绘线落到模型里一律改成直线和正圆弧（你 10-23 的要求）：

| 页 | 批注 | 修改 |
|---|---|---|
| 各层 | if the core moves one bay, floor plate more efficient（红框） | 西侧 8 个核心筒一起西移一跨（24'，现 x 3240–3258）。紧邻的办公（L3 / L4 / L5，蓝框）和 L1 COMBINABLE CLASSROOMS（紫框）向西扩一跨。所有房间都让开核心筒。 |
| L1 | cafe is open | COFFEE KIOSK 改为开敞（Interaction / Collab，不围合）。 |
| L1 | open（贴入的楼梯图） | 大台阶首层梯段东移 28'（仍在二层开洞内），Micron Gallery 一侧打开。 |
| L1 | something cool | 南翼东端一跨（x 3456–3480，约 2,450 sf）设为 INNOVATION SHOWCASE（Interaction / Collab）。名字是我暂定的，请按你们的想法改。 |
| L2 | flip | 二层起步的弧形大台阶左右镜像（以开洞中线 x 3225.5 为轴）。南侧螺旋楼梯不动。 |
| L2 | 东侧两个红框 / 你 10-23：东侧两部楼梯起 core 作用 | 东北、东南两部楼梯（原来是 FLOOR PLATE (2) 上每层一个楼梯块）改为 CORE 核心筒：东北 L2–L5，东南 L2–L4，范围同原楼梯块。 |
| L3 | wider | 中庭开洞加宽：西侧直边 x 3213（下半段）、东侧直边 x 3259（上半段），两段 R 46' 四分之一圆弧连接。原开洞完全在内，面积 3,302 → 5,071 sf。 |
| L3 | deeper | 中庭边两个窄房间（Seminar Room 条、Break Room 条）从 12' 加深到一跨 24'（x 3269–3293）。 |
| L4 | consider different shape for this floor | 中庭开洞改为 D 形：西侧直边 x 3195，东侧一段 R 68.7' 圆弧，面积 5,674 sf。 |
| L4 | 东侧涂成 circulation 色的条带 | 庭院东侧（x 3463–3480）两翼之间加一条室内连廊（circulation）。 |
| L3 | additional column line ? | 是问题，没有改结构。 |

楼板开洞在 Rhino 里是真正的直线加圆弧（PolyCurve：LineCurve + ArcCurve），楼板重建为一个带洞的 Extrusion。改开洞时，本层房间先把原开洞补平再按新开洞切，所以新开洞旁边不会留下旧开洞的凹凸。两部螺旋楼梯原来骑在新开洞的边线上，已挪进开洞内：北侧（L3→L4）西移 5'、南移 19'，同时在 L3、L4 开洞内；南侧（L2→L3）西移 7'、北移 19.4'，同时在 L2、L3 开洞内。平面里开洞中那两段粗弧线就是这两部楼梯。

另外（你 10-23）：**南翼屋顶的 Shell（圆角体量）向西延长 40'**（x 3243 → 3203，两端仍是 R 36' 半圆，Shell 和它的屋面板一起改），西移后伸出来的南侧核心筒现在完全在 Shell 内。北侧 Shell 本来就包着北侧核心筒，未改。延长后会碰到 ROOFTOP TERRACE 上方高位棚架的南端，棚架南端退到 Shell 北边外 1'（y 2198 → 2215）。

## 屋顶 / 露台 / 庭院景观（参考 RIVER 方案的花园做法）
图层同 RIVER：`PROJECT SITE::GARDENS::*`（DECK 木平台、PAVING、PATH、GRAVEL、LAWN、PLANTING、PLANTER 抬高种植池、GREEN ROOF、TREES、FURNITURE、STRUCTURE 廊架、PEOPLE）。所有边线都是直线或正圆弧（直角矩形、圆角矩形、圆、圆弧带），在 Rhino 里就是 LineCurve / ArcCurve 组成的曲线或由它们挤出的体块，没有布尔裁切出来的折线。露台和屋顶上的构件是体块（木平台 0.5'、种植池 1.5'、绿屋面 0.5'），庭院在地面上是闭合曲线。
- **庭院**（两翼之间的露天部分，首层地面）：碎石铺底；沿北翼一条 12' 木平台；沿南翼种植带配一排树；从西侧主廊直通东端圆形小广场（R 15'，上方廊架，两条长凳）的花园步道；步道两侧各一块圆角草坡（画两圈等高线），西端入口两个圆角种植池配树；东侧两个角上各一个圆形野餐小广场（C 形木座椅、桌凳、种植环、树）；步道边长凳、人。
- **L2 OUTDOOR TERRACE / DINING TERRACE、L4 露台**：沿建筑一侧木平台（约 12'），临空一侧抬高圆头种植池配一排树，中间桌凳，平台上长凳。
- **L5**：ROOFTOP TERRACE 上一块抬高的圆角草坡（带等高线）和一块木平台配桌子（在高位棚架下）；西侧百叶棚架下整块木平台配长凳，旁边圆头种植池配一排树；L4 露台上方屋面做绿屋面；南翼屋顶 Shell 西侧做屋顶花园：木平台中间一个圆形草坡（R 22'，等高线），四角种树，两条长凳；Shell 南侧、东侧做绿屋面，北侧一条 10' 屋面步道。
- **北翼顶层屋面（+94'-2"）**：Shell 四周四块圆角绿屋面。
