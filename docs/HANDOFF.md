# 交接：v0.8.0 测试与微调（2026-10-13）

新窗口用法：把下面第 1 节的消息贴进新会话，后面附上 Windows 测试结果 / 截图。新会话先读 `CLAUDE.md` → `docs/PROGRESS.md` → 本文件，再按需读 PRD 章节。

## 1. 新会话开场消息（直接复制）
```
You are the lead developer of the "Program Diagram" Rhino 8 plugin (repo branch claude/axon-diagram-workflow-sroy86).
First: git fetch and pull the branch (other sessions have pushed to it before). Read, in this order and only these:
CLAUDE.md, docs/PROGRESS.md, docs/HANDOFF.md, docs/CHANGELOG.md (top rows); PRD sections only as needed
(grep -n '^#' docs/PRD.md, then sed -n). PRD v1.0; S0-S6 done; plugin v0.8.0; all tests green.
Task: test-and-tune round on Windows results pasted below. For each item: find it in HANDOFF.md §3 (where it lives),
prefer changing a style key (§4) over code, reproduce on the Downtown fixture with DumpForReview + tools/svg_shot.mjs (§5),
add or adjust a test, keep goldens updated. Keep: true scale, stable item names, _MANUAL / OVERLAY untouched,
NC vs BG drawn differently, every default in styles/*.json. Ask me only if a request is ambiguous (§7 lists open ones).
Talk to me in Chinese; code/commits in English. At the end: run tests, update PROGRESS.md (<=40 lines), HANDOFF.md
and CHANGELOG.md, bump the patch version, run tools/package.sh, commit, push, send me the zip, reply in Chinese in <=10 lines.
[Paste Windows test results / screenshots here]
```

## 2. 当前状态
- 分支 `claude/axon-diagram-workflow-sroy86`，版本 **0.8.0**（`src/PD.Rhino/PD.Rhino.csproj`、`yak/manifest.yml`、`illustrator/runtime.jsx` 三处同步改），168 个测试全过，`dist/ProgramDiagram-0.8.0.zip`（dist 下的 zip 随提交入库）。
- 测试模型：`tests/fixtures/template.json`（模板模型）和 `downtown.json`（用户 Downtown 模型 2026-10-13 版，毫米，无屋面楼板，B1–L11）。新模型来了用 `python3 -I tools/fixture_from_3dm.py <in.3dm> tests/fixtures/downtown.json` 重做，再修正 `DowntownTests` 里的数字。
- 文档：PRD v1.0（顶部有 v1.0 变化摘要）、`docs/USER_GUIDE.md`、`docs/MODELING_CONVENTION.md`、`docs/SITE_STYLE.md`（§0.1 已注明 Futura PT）。

## 3. 用户 rev 0.8 各项在哪里改
| 内容 | 代码 | 样式键 / 规则键 |
|---|---|---|
| 字体 Futura PT 大写（Heavy / Book） | `ExportContext.AddStandardStyles`、`runtime.jsx PD.paraStyle`（找不到字体退回 fallback） | `fonts.*`（postScript / fallbackPostScript / allCaps / sizePt） |
| NC.Interior / Furniture / Massing / Screen | 角色 `Classification/LayerClassifier.cs Roles`；平面 `Plans/PlanLinework.cs`；剖面 `SectionGeometry.MassingCut`；轴测 `AxonDrawing.ScreensAndMassing`；Screen 在视图中 = `ViewKind.Facade`（`ProjectedView.Kind`） | `roles.NC.*`（plan / axon / view 画法）；关键词 `layer_keywords.json rules`（first 规则） |
| 配景（公司人物 39 种、树 19 种） | `Entourage/EntourageLibrary.cs`、`EntourageArt.cs`（Symbol）、`Entourage.cs`（`EntouragePlacer`、`EntourageSymbols`）；库由 `tools/entourage_from_ai.py` 从 `assets/entourage/*.ai` 生成 | `entourage.*`（密度、间距、`treeFills`、`treeHeightFt`） |
| 总平面范围 5 倍 | `PlanDrawing.SiteExtent` | `plan.siteExtentFactor`、`plan.siteMarginFt` |
| 平面铺装 / 道路交界线 | `PlanDrawing.SiteEdges`（图层 `BG_SITE_EDGE`） | `plan.hardscapeEdge`、`plan.roadEdge` |
| 图例符号画板 | `Plans/PlanLegendSheet.cs`（`01_Plans.ai` 最后一个画板） | — |
| LEVEL X 标签 | `PlanDrawing.TitleTag` | `plan.titleTagFill`（空 = 无底色），字号 = `fonts.label` |
| < 0.2 ft 缝视为贴合 | `Cleanup/LevelAssignment.Seal`（面积在封缝前统计） | `rules_default.json cleanup.sealGapFt` |
| 剖面标签居中 / 缩写 | `Sections/SectionBars.cs`（`Fit`、`TextWidth`） | `section.labelSize*`、`labelInsetPt` |
| Penthouse 灰块、无屋面板、T.O. 标高 | `SectionDrawing.Penthouse / DrawPenthouse`、`LevelMarkers.One` | `section.penthousePrograms / penthouseFill / penthouseLabel / penthouseTopLabel` |
| 剖面人物随机 | `EntouragePlacer.AlongLine`（随机 + 成对 + 避开文字） | `entourage.densityByProgram`、`minSpacingFt` |
| 场地轴测：无楼板、层间 2 pt 线、只描外轮廓 | `AxonDrawing.Stacked(site)`、`FullHeight`、`LevelLines`（图层 `AXON_LEVEL_LINE`） | `axon.site.fullHeightBlocks / levelLinePt / levelLineColor`、`axon.stacked.silhouettePt` |
| 屋面核心筒错位 | `AxonFaces.SplitAtNeighbours`（体块在相邻体块顶 / 底处分段后再消隐） | — |
| 爆炸轴测场地在 L1、地下层在下 | `AxonDrawing.ExplodedA`、`GroundAndExcavation`、`Site(..., excavation)` | `axon.exploded.*` |
| 分层轴测地下层虚线场地 + 挖方墙 | `AxonDrawing.Levels`、`Excavation`、`Site(..., outlineOnly)` | `axon.excavationFill / Stroke / Pt`、`axon.hidden*` |
| 树被遮挡 | `AxonDrawing.HideBehind`（树、人带 Depth） | — |
| 立面标高线同剖面、贯穿全图 | `Sections/LevelMarkers.cs`（剖面、立面共用） | `elevation.markerSizePt / levelLine*` |

## 4. 微调优先改样式（不用改代码）
- `styles/style_default.json`：`fonts`、`plan`、`section`、`axon`（含 `site`、`exploded`、`stacked`）、`roles`、`entourage`、`charts`、`view`、`elevation`。用 `python3 -I -c "import json;print(json.load(open('styles/style_default.json'))['section'])"` 按键读，不要整文件读。
- `styles/rules_default.json`：楼层、清洗、输出（比例、默认图纸列表）。
- `styles/layer_keywords.json`：图层名 → 角色的自动预填。
- 改完 JSON 后用 Python `json.dump(..., indent=2, ensure_ascii=False)` 写回，保持原格式。

## 5. 复现与检查流程
1. 新容器先装环境：`apt-get install -y dotnet-sdk-8.0`；`pip install rhino3dm pypdf`（只在重做 fixture / 配景库时需要）；node 22 和 Playwright 已预装。
2. 生成全部图的 SVG：`PD_DUMP=<dir> PD_ONLY=Plan,Section dotnet test src/PD.Core.Tests --filter DumpForReview`（`PD_ONLY` 可省略，可选值见 `rules_default.json output.drawings`）。同时写出 `roles.txt`（图层角色）和 `blocks.txt`（每层体块的 z 范围）。
3. 截图：`node tools/svg_shot.mjs <in.svg> <out.png> 1400`，局部放大用 `convert out.png -crop WxH+X+Y +repage crop.png`。
4. 金样：`DowntownTests.ExportBuildsEveryDrawing` 等测试会重写 `tests/golden/*.svg`，提交前看一眼变化。
5. 测试：`dotnet test PD.sln -v q --nologo 2>&1 | tail -3`（约 1 分钟；`S6AcceptanceTests.T6` 跑完整导出最慢）。

## 6. 请用户在 Windows 上回报的内容
- `illustrator_log.txt` 中所有 WARN / ERROR 行；`export_log.txt` 中的 NOTE。
- 每个问题：哪张图（文件名 + 画板）、截图、期望效果；如可能附上模型或图层名。
- 重点验证（Linux 上测不到）：AI Symbol 是否生成（Symbols 面板有 `PD_PERSON_*` / `PD_TREE_*`，否则会退回普通编组）、Futura PT 是否生效、剖面标签是否真正居中（AI 按字形居中）、`_MANUAL` 图层重新导出后是否保留。

## 7. 待确认 / 已知问题
- **"site context 永远跟着 B1 走"**：目前理解为场地放在地面层（L1），B1 在挖开的场地下方。若用户本意是场地贴着 B1，改 `GroundAndExcavation` 返回的层即可。
- Penthouse 的判定：顶层只含 SHELL / CORE / STAIR / BUILDING_SUPPORT（`section.penthousePrograms`）；模板模型的 L11 设备层也被当作 penthouse。
- 平面顶视人物是内置简图（公司库只有立面人物）；立面图不画树（剖面画）。
- 文字宽度是估算值（剖面缩写、图例列宽）；字体换了要校 `SectionBars.TextWidth`。
- 尚未做：街道名 / 箭头、流线 / 视线 / 服务图标（画在 `ANNOTATION`）、key plan、Unassigned 斜线。
- 分支上曾有其他会话同时推送：开工前先 `git fetch` + `git pull`，冲突时以远端为准再合并自己的改动。
