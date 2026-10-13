# Program Diagram 使用说明（v0.8.0）

把 Rhino 8 体块模型一键输出成 Illustrator 的 program 图：总平面、各层平面、剖面、立面、场地 / 叠合 / 爆炸 / 分层轴测、Program 分类图和饼图。界面为英文，下面括号里是面板上的名字。

## 1. 安装
1. 解压 `ProgramDiagram-0.8.0.zip`，整个 `ProgramDiagram` 文件夹放在自己的用户目录下（不需要管理员权限）。升级时整个文件夹覆盖。
2. 把 `ProgramDiagram.rhp` 拖进 Rhino 8，重启 Rhino。
3. 命令 `ProgramDiagram` 打开面板。字体需要 **Futura PT**（Book / Heavy）；没有时 AI 中退回 Arial。

## 2. 九步流程（面板 1–9 页）
| 页 | 做什么 |
|---|---|
| **1 Layers** | 每个图层一个角色（自动按图层名预填，见 `MODELING_CONVENTION.md`）。可多选行批量改（Ctrl / Shift）。确认后勾 Confirm。 |
| **2 Cleanup** | 非封闭体块、单张曲面、跨层体块、重叠、游离对象：按提示选择处理方式。 |
| **3 Levels** | 检查楼层标高；可增删、改名、设夹层。`ProgramDiagramLevels` 在视图中显示楼层面。 |
| **4 Names** | 每个体块的标签和稳定编号（`L03_LAB_01`），可单独改 program。 |
| **5 Colors** | 配色方案（公司图例）；可加自定义 program。顺序即图例和饼图顺序。 |
| **6 Sections** | 画剖切线（`ProgramDiagramPickSection`），设看向哪一侧、看深。 |
| **7 Elevations** | 画立面线（`ProgramDiagramPickElevation`），默认自动朝向本楼。 |
| **8 Output** | 保存位置、比例（平面 / 轴测默认 1" = 45'-0"，剖面 1" = 16'-0"，可自由输入）、要出的图、Program Excel、配景随机（Re-randomize entourage）。点 **Export**。 |
| **9 Check** | 剩余不确定项；可标记为已知并忽略。 |

## 3. 输出
每次导出在模型文件夹下新建 `YYYY MMDD_Conceptual Program Diagram_HHMMSS`：`.ai` 文件（Illustrator 在后台生成后自动关闭）、`_preview/*.svg` 预览、`program_areas.csv`、`program_table.csv`、`export_log.txt`、`illustrator_log.txt`、`ProgramDiagram.jsx`（没有 Illustrator 时可之后手动运行：File › Scripts › Other Script…）。

## 4. 在 Illustrator 里继续画
- 手画的内容放在 **`_MANUAL`** 或 **`OVERLAY`** 图层：重新导出永远不动。场地轴测另有 `ANNOTATION` 图层（流线、视线、图标）。
- 插件生成的对象都有名字，重新导出时按名字原位替换；你在生成图层里画的**未命名**对象会保留。
- 颜色是全局色板（如 `PRG_LAB`）：在第 5 页改色后重新导出，已有文件原位变色。
- 人物和树是 Symbol（`PD_PERSON_*`、`PD_TREE_*`）：在 AI 里改了 Symbol 的造型，重新导出会保留；想恢复默认就删掉该 Symbol。
- `01_Plans.ai` 最后一个画板 **LEGEND & SYMBOLS** 收集了全部图例、线型、指北针、比例尺和配景符号，可直接复制使用。

## 5. 常见问题
- **体块颜色不对 / 显示 Unassigned**：第 1 页该图层的 Program 没选，或第 4 页单独改过。
- **少了某层**：第 3 页检查楼层；楼板（`NC.Slab`）是最可靠的楼层来源。
- **剖面没东西**：剖切线要穿过体块；第 6 页 Look left 方向。
- **Excel 读不到**：导出时 Excel 文件不能在 Excel 里打开着。
- **Illustrator 报错**：把 `illustrator_log.txt` 的 WARN / ERROR 行发给开发。
