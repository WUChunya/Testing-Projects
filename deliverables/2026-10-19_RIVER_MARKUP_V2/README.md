# RIVER — CW markup V2 修订（2026-10-19，plugin v0.9.1）

| 文件 | 内容 |
|---|---|
| `RIVER_Program_Diagrams_REV2.pdf` | 30 页，按插件编号排序，真实比例。新增剖面 C、D。 |
| `RIVER_2026_1019_MARKUP_V2.3dm` | 修订后的模型（插件设置已写入）。 |
| `scripts/` | `build_model.sh <原 3dm> <总平面 .ai> <repo> <工作目录>` 从原始文件重建整个模型（V1 + V2 修改都在 `revise_model.py` / `waterfront.py` 中，V2 部分有注释标出）。 |

## 逐条对应 V2 markup

| 批注 | 处理 |
|---|---|
| This is also circulation inside building boundary（L1 餐厅旁、东北角缺口） | 一层 circulation 扩到建筑边界：在玻璃线、各房间和餐厅外缘之间闭合 30 ft 以内的缝隙。 |
| 这一块一层是架空的，显示 dashed line，2 层开始才有 | 一层 circulation 不再延伸到西侧长条南边；插件新增“上层悬挑虚线”：平面中上一层伸出本层的部分画虚线轮廓（`plan.overhead*`）。 |
| 删除这些灰色的线条 | `GRID` 图层改为 Ignore（这些是轴网线，压在公园上）。 |
| 三个 block 都显示 block 图层，dash line，示意 grid | 插件新增：同名相邻的房间（3 个 PROTOTYPING、CORE LAB）之间画灰色虚线，示意柱网（`plan.grid*`）。 |
| 这些也都是 circulation（L2、L3） | L2：两条长条之间的楼板（GYM 和 LOBBY 之外）计为 circulation；L3：西南玻璃内（OFFICE 三角形周围）计为 circulation。 |
| GYM / EXECUTIVE OFFICE / OFFICE / PENTHOUSE / main entrance | L2 conference → GYM；L3 东北长条 → EXECUTIVE OFFICE，西长条和三角形 → OFFICE（program 改为 OFFICE）；三个机房 → PENTHOUSE；L2 坡道顶端的入口大厅单独成块，命名 MAIN ENTRANCE。原来场地上叫 MAIN ENTRANCE 的平台改名为 ENTRANCE PLAZA。 |
| 再添加两个剖面，展示坡道下可以停车、main entrance 在二楼 | 新增剖面 C（平行于斜向长条，沿坡道从街面升到 +18' 入口平台再到建筑）和剖面 D（南北向，穿过停车楼）。坡道改为 1.5' 厚的斜板实体，插件新增“场地剖切”：高出地面的项目铺装 / 绿地 / 车道实体按实际剖切形状画出（`section.siteCut*`）。坡下停车是贴着坡道底面的楔形体量，剖面中白底虚线并标注 PARKING。 |
| 再检查 program，没有中空可以透树木的（剖面 B） | 二层两条长条之间补成 circulation 后，剖面 B 二层的空隙已填满。一层靠近礼堂的一段是玻璃线外的室外（上面是 L2 平台），保留。 |
| 场地外的 program 用灰色表示 | RIVER PAVILION 改为背景建筑（BG.Building），不计面积。剖面中放不下名称的远处背景建筑不再标名。 |
| 这是什么线，应该不需要请删除（场地轴测） | 场地轴测不再画 NC.Massing 虚线（`axon.site.massing = false`，其它轴测不变）。 |
| auditorium 不是 core，两层通高 lab 也不是 core，不需要 core 的引线 | 爆炸轴测 A 不再为通高空间画半透明连接体，上层楼板在通高处自然留空（`axon.exploded.tallConnectors = false`）。 |
| 查看平面的标注，这一块应该是室内 circulation（爆炸轴测） | 同第一条，一层 circulation 已包住餐厅一侧。 |
| L1 看不见这个玻璃，这是 L2 的立面 | 插件：分层/爆炸轴测里，每层只画本层的立面玻璃（上一层玻璃向下伸进本层不到 4'，或不到其高度的 30%，就不算本层）。 |
| auditorium 是一层 + 10ft 高的体量，二层也能进去 | 礼堂改为 0–30'（L1 20' + 10'），设为两层通高（KeepTall）：L2 平面中显示为挑空，剖面中高出 L2 楼板。 |

## 说明
- 本轮新增 / 修改的样式键都在 `styles/style_default.json`，可在样式里关掉：`plan.gridPt`、`plan.overheadPt`、`section.siteCutRoles`、`axon.site.massing`、`axon.exploded.tallConnectors`。
- 礼堂在 L2 是“挑空 + 可进入”。若希望 L2 显示为礼堂上层（而不是 OPEN TO BELOW），需要把礼堂拆成上下两块，请告诉我。
