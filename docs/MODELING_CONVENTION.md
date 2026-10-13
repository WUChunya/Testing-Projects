# 建模约定（一页）

插件按**图层**读取模型。按下面的约定建模，第 1 页基本不用手改。单位任意（内部换算为英尺）。

## 图层与角色（按图层名自动预填，子图层单独判断，不继承父图层）
| 放什么 | 图层名示例 | 角色 | 出图 |
|---|---|---|---|
| program 体块（封闭实体，一个功能一层） | `PROGRAM::Lab`、`PROGRAM::Office` | Program | 平面色块、剖面色条、轴测体块，计面积 |
| 核心筒 / 楼梯电梯 | `CORE`、`STAIR`、`ELEVATOR` | NC.Core / NC.Stair | 同 program；爆炸轴测中连接上下层 |
| 楼板 | `FLOOR PLATE`、`SLAB` | NC.Slab | 判定楼层（最可靠）、剖面楼板 |
| 立面玻璃 / 竖梃 | `GLASS`、`CW-*`、`MULLION` | NC.Facade | 平面玻璃线、立面、剖面 |
| 围墙 / 装饰墙 / 格栅 | `SCREEN`、`FENCE`、`LOUVER` | NC.Screen | 只画这一层面，不挤出体块 |
| 室内隔墙 | `NC::INTERIOR`、`PARTITIONS` | NC.Interior | 平面中灰线 |
| 家具 | `NC FURNITURE`、`CASEWORK` | NC.Furniture | 平面浅灰线 |
| 建筑外轮廓体量（含无 program 空间） | `NC::MASSING`、`ENVELOPE` | NC.Massing | 平面 / 剖面 / 轴测虚线，不计面积 |
| 柱、结构墙 | `COLUMNS`、`STRUCTURE` | NC.Structure | 平面深灰实心 |
| 本项目场地 | `NC::PLAZA`、`NC::LAWN`、`NC::TREES` | NC.Hardscape / NC.Green / NC.Tree | 场地、轴测场地 |
| 周边建筑 | `CONTEXT`、`CADMAPPER BUILDINGS` | BG.Building | 白色体块 / 剖面剖到画粗线 |
| 道路 / 街区 / 绿地 / 树 / 水 | `ROAD`、`LOT FILL`、`PARK`、`TREES`、`WATER` | BG.* | 场地底图 |
| 人、车 | `PEOPLE / CARS` | Entourage | 用模型中的位置放配景 |

路径中含 `NC::`、`NEW::`、`PROJECT::`、`PROPOSED::` 的场地图层自动变为 NC（本项目）；`NC::MASSING` 这类在 NC 路径下的建筑体量为 `NC.Massing`。`Make2D` / `Visible` / `ClippingPlaneIntersections` 等 2D 输出图层自动忽略。

## 体块
1. **一个功能空间一个封闭实体**，放在对应 program 图层；不同 program 不要重叠（重叠时体积小的优先）。
2. 体块**从楼板顶面建到上层楼板底**；跨两层以内且不高于 1.6 倍层高的保留为通高空间，否则按层切分。
3. 同层相邻体块贴合建模；**相距 < 0.2 ft 视为贴合**（平面 / 轴测中用白色细线分隔）。
4. 屋面上的设备间、楼梯间画成 `Shell` / `Core` / `Support` 体块即可：剖面中作为灰色 PENTHOUSE，自动标顶面标高；**屋面上不要再画楼板**。
5. 地下室照常建在地坪以下：平面画土色底，轴测中放在挖开的场地下方。
6. 屋顶花园、广场等薄板（≤ 2 ft）：架空的计入下方楼层的 program，贴地的按场地铺装 / 种植画，不计面积。

## 场地与配景
- 首层所在的地坪默认为 0'-0"（`rules_default.json › levels.groundElevationFt`）。
- 树：每棵树一个物体（任意形状），插件取中心和高度，平面画圆、剖面画公司树形（TREES.ai）、轴测画棒棒糖树。
- 人：不建模时自动按 program 密度随机放置（随机种子随项目保存）；建模的人优先。
- 总平面范围 = 本项目场地四周各 5 倍自身尺寸，周边模型要覆盖到。
