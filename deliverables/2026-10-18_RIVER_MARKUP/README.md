# RIVER — CW markup 修订（2026-10-18，plugin v0.9.0）

| 文件 | 内容 |
|---|---|
| `RIVER_Program_Diagrams_REV1.pdf` | 28 页，按插件编号排序（00 总平面 → 07 立面），左侧书签跳页。每页按真实比例（1 单位 = 1 pt），上一版 PDF 只占页面 75% 的问题已修正。 |
| `RIVER_2026_1018_MARKUP.3dm` | 修订后的模型，插件设置已写入（图层角色、名称、跨层、楼层、轴测视角）。 |
| `scripts/` | 可复现：`build_model.sh <原 3dm> <总平面 .ai> <repo> <工作目录>` 依次运行 `extract_ai.py` → `site_to_3dm.py` → `update_settings.py` → `revise_model.py`（markup 修改）→ `waterfront.py`（滨水公园）；`export_previews.cs` 出 SVG，`svgs_to_pdf.mjs` + `merge_pdf.py` 合成 PDF。 |

## 逐条对应 markup

| 批注 | 处理 |
|---|---|
| 一楼玻璃内的 circulation 用浅黄色 program | 新建 `PROGRAM::Circulation` 体块：L1 0–18'（玻璃线内）、L2 20–34'、L3 36–50'（玻璃与 program 包围的可走楼板）。program 体量更小、按“小体量优先”规则自动扣除，circulation 就是剩下的可走部分。 |
| 只有标注的两块是绿化 / 这个是道路 / 这些部分是广场铺装 | 坡道区域重新划分：`PROJECT SITE::GREEN SPACE`（两条绿化带，NC.Green）、`PROJECT SITE::ROAD`（上坡车道和回车圈，BG.Road）、`PROJECT SITE::PLAZA`（其余铺装，NC.Hardscape）。曲线按坡度贴合（街面 0' → 入口广场 +18'）。原 `SITE LANDSCAPE` 斜板保留为 3D 参考（角色 Ignore），`SITE BASE` 改为 BG.Base。 |
| 这一整块板子是斜坡，下面停车 | `PROJECT SITE::PARKING BELOW`：坡道下净高够 10' 的部分（25,076 sf，0–9'），NC.Massing，各图画虚线、不计面积。 |
| 这一栋是停车楼 | 原 BUILDING SUPPORT 长条移到 `PROGRAM::Parking`，新增自定义 program PARKING（#C3CBD3），标签 PARKING GARAGE。 |
| CORE LAB / PROTOTYPING / GALLERY / SERVICE CORRIDOR / GENERAL SUPPORT / EVENT SUPPORT / LOADING | 按图上位置写入插件的名称表（tab 4）。LOADING 处原来只有轮廓线，补了 0–18' 体块。 |
| MAIN ENTRANCE / LOADING AND BOH SERVICE | 入口广场（+18'）命名 MAIN ENTRANCE；西南角新建服务场地 LOADING AND BOH SERVICE。插件新增“场地命名”标签（见下）。 |
| 这三个 core lab 两层通高 | 斜向长条的三个 core lab（0–34'）跨层设为 KeepTall：L2 显示 OPEN TO BELOW，剖面为通高。 |
| 楼梯不给名字、不算 program、不需要楼板、不计面积 | `DETAIL::TREADS`、`PROGRAM::Stair` 改为 NC.Structure。 |
| 剖面不需要两层楼板 / 为什么这一层 program 丢失了（爆炸轴测、分层 L3） | 同一个原因：楼层标高设为 16'/32'/48'，而楼板顶面在 20'/36'/52'，结果每层画了两道楼板，36–50' 的 program 也被分到错误的楼层。楼层改为楼板顶面 L2 20'、L3 36'、L4 52'、L5 77'-1"。 |
| 顶层 mechanical house 紧贴 core，消除 gap | 三个机房和屋面板原来在 core 位置开了比 core 大一圈的洞（36.5' vs 24'×36'）；改为实心，core 按规则扣除，机房内壁紧贴 core。 |
| 配景重叠了（爆炸轴测 B） | 插件修复：场地上的树和人被上方抬起的楼板遮挡时裁掉；场地板以外的树不再画出（之前按外包矩形筛选，树浮在空中）。 |
| 室外景观参考平面图修改 + 参考 Sasaki 滨水公园 | 见下。 |

## 滨水公园（`PROJECT SITE::RIVERFRONT PARK`）
沿南岸水边线约 970 ft，从建筑沿河一侧一直到水面，替换原有的近岸绿带和步道。元素和活化方式参照 Sasaki 案例：
- **多层丝带步道**：蜿蜒的 RIVER PROMENADE（18'）、沿建筑的 UPPER RIBBON（10'）、三条 S 形 GARDEN WALK 把建筑和水边连起来，水边还有一条 BOARDWALK。
- **草坪与林地**：PICNIC LAWN ×2、EVENT LAWN、开敞的 RIVER MEADOW，加上成团的 FLOWERING GROVE 和沿主步道的开花乔木（NC.Tree）。
- **亲水与活动**：三处 RIVER STEPS（阶梯入水）、KAYAK LAUNCH 码头、SCULPTURE PLAZA、RAIN GARDEN 水带、RIVER PAVILION（滨水亭/咖啡，约 1,130 sf，program INTERACTION）。
- **人物活动**：117 个人物点（`PEOPLE`，Entourage 角色）：草坪上的野餐小组、步道上的行人、台阶上的人群、码头和广场。

## 插件 v0.9.0 的相关改动
- 爆炸轴测 B：树和人按楼板遮挡裁剪；只画场地范围内的树。
- 剖面：屋面层标高已标出时，不再重复画 T.O. PENTHOUSE（原来 "L5 ROOF" 和 "T.O. PENTHOUSE" 叠在一起）。
- 平面：给命名的 NC.Hardscape / NC.Green / NC.Water 区域写标签（`plan.siteNameRoles`、`plan.siteNamePt`），互相重叠的标签自动略过。
- 绿地自动种树：名称含 LAWN / MEADOW 的绿地视为开敞草坪，不种树（`greenTrees.openNameKeywords`）。
- 模型里的人物（Entourage 图层）现在会画在平面和轴测里，并替换附近自动生成的人物。

## 需要确认
- **滨水亭**：RIVER PAVILION 计入面积（INTERACTION 约 1,130 sf）；不需要的话删除 `PROGRAM::Pavilion` 即可。
- **河边三块绿地**：`greens` 图层上的三块椭圆绿地保留为绿化（NC.Green）。批注中“只有这两块是绿化”按坡道区域理解。
- **停车楼面积**：同一楼层内两层错层车库重叠的部分只计一次，所以 PARKING 合计 111,574 sf，不是 5 层的总和。
