# 交接：v0.8.x 测试与微调（2026-10-14，当前 v0.10.0）

新窗口用法：把下面第 1 节的消息贴进新会话，后面附上 Windows 测试结果 / 截图。新会话先读 `CLAUDE.md` → `docs/PROGRESS.md` → 本文件，再按需读 PRD 章节。

## 1. 新会话开场消息（直接复制）
```
You are the lead developer of the "Program Diagram" Rhino 8 plugin (repo branch claude/axon-diagram-workflow-sroy86).
First: git fetch and pull the branch (other sessions have pushed to it before). Read, in this order and only these:
CLAUDE.md, docs/PROGRESS.md, docs/HANDOFF.md, docs/CHANGELOG.md (top rows); PRD sections only as needed
(grep -n '^#' docs/PRD.md, then sed -n). PRD v1.0; S0-S6 done; plugin v0.10.0; all tests green.
Task: test-and-tune round on Windows results pasted below. For each item: find it in HANDOFF.md §3 (where it lives),
prefer changing a style key (§4) over code, reproduce on the Downtown fixture with DumpForReview + tools/svg_shot.mjs (§5),
add or adjust a test, keep goldens updated. Keep: true scale, stable item names, _MANUAL / OVERLAY untouched,
NC vs BG drawn differently, every default in styles/*.json. Ask me only if a request is ambiguous (§7 lists open ones).
Talk to me in Chinese; code/commits in English. At the end: run tests, update PROGRESS.md (<=40 lines), HANDOFF.md
and CHANGELOG.md, bump the patch version, run tools/package.sh, commit, push, send me the zip, reply in Chinese in <=10 lines.
[Paste Windows test results / screenshots here]
```

## 2. 当前状态
- 分支 `claude/axon-diagram-workflow-sroy86`，版本 **0.10.0**（`src/PD.Rhino/PD.Rhino.csproj`、`src/PD.Rhino/yak/manifest.yml`、`illustrator/runtime.jsx` 三处同步改），255 个测试全过（全套约 9 分钟），`dist/ProgramDiagram-0.10.0.zip`（dist 下的 zip 随提交入库）。
- 每轮微调的测试放在 `src/PD.Core.Tests/Rev0xxTests.cs`（v0.8.1 = `Rev081Tests.cs`，v0.8.2 = `Rev082Tests.cs`，v0.8.3 = `Rev083Tests.cs`，v0.8.4 = `Rev084Tests.cs`，v0.8.6 = `Rev086Tests.cs`，v0.8.7 = `Rev087Tests.cs`，v0.8.5 = `Rev085Tests.cs`（`Noisy` = 坐标加噪声模拟 Rhino 原始坐标），其中 `ScreenModel` 是带屋面 screen 的 Downtown，`PD_DUMP_SCREEN=<dir>` 出图）。
- 方案模型（v0.9.2）：`river_tk.json` / `campus.json` + `*_settings.json`，测试里用 `FixtureAnalysis(model, settings)` 加载；成果在 `deliverables/2026-10-19_CAMPUS`（RIVER 方案用户已不需要，TK 只留作回归测试）（PDF、模型、AI 导出包 = ProgramDiagram.jsx，scripts 可重建）。固定画板居中 / 自适应：`ExportContext.PageAnchor / Place / LargerNote`，样式键 `page.centreOn`、`page.fitContentWhenLarger`。
- 测试模型：`tests/fixtures/template.json`（模板模型）和 `downtown.json`（用户 Downtown 模型 2026-10-13 版，毫米，无屋面楼板，B1–L11）。新模型来了用 `python3 -I tools/fixture_from_3dm.py <in.3dm> tests/fixtures/downtown.json` 重做，再修正 `DowntownTests` 里的数字。
- 文档：PRD v1.0（顶部有 v1.0 变化摘要）、`docs/USER_GUIDE.md`、`docs/MODELING_CONVENTION.md`、`docs/SITE_STYLE.md`（§0.1 已注明 Futura PT）。

## 3. 用户 rev 0.8 各项在哪里改
| 内容 | 代码 | 样式键 / 规则键 |
|---|---|---|
| 场地材质分色；楼梯白底灰线（0.9.5） | `PlanBackground.Mat / PaintFor`（`BackgroundItem.Material`）；`PlanDrawing.Stairs / IsStair`；`AxonDrawing.StairPrisms`（爆炸 A、分层） | `siteMaterials`（key / match / roles / plan / axon）、`stairs.layerKeywords / fill / stroke / strokePt` |
| 斜坡景观 / 露台 / 屋顶花园从上方可见（0.9.6） | `PlanBackground.SiteAbove / SiteRemoved / Climbs / Sorted`；`PlanDrawing.RoofView`（体块和外轮廓让位）、`DrawLevel`（总平面只让位给爬升的场地）、`ExposedItems` | `rules.cleanup.slopedSiteMinRiseFt` |
| Illustrator 提速（0.9.7） | `runtime.jsx` `PD.drawItem`：开放的多段线（hatch、场地边线）画成编组，不用复合路径；`PlanDrawing.DrawLevel(overview)` | `plan.upperFloorHatch / overviewHatch`（默认 false） |
| 打开文件后重读设置（0.9.8） | `PdSession.Reload / Load`；`ProgramDiagramPlugin` 的 `EndOpenDocument` 事件；`Forget` 不再删会话 | —（VERIFY-WIN） |
| 轴测：夹层并入主楼层，雨棚 / 屋面保留（0.9.9） | `AxonDrawing.OnMainLevels / MainLevel / PlateOf / Stacks / Beyond`；`ProgramBlock.FromMezzanine`；`Exposed(plates:)` | `axon.mezzaninesOnMainLevel` |
| 轴测：屋顶花园、坡地斜面、通高空间着色（0.10.0） | `AxonDrawing.Raised / RoofSite / RoofTrees / Climbing / SlopePrisms / Silhouette`；分层轴测 `tallBelow` | `axon.roofSiteMinAboveGroundFt`、`axon.slopeStepFt` |
| 玻璃外皮 → 室内 circulation；薄体块归层 / 室内不当铺装；剖面楼板只在剖到的建筑内、夹层只在其体块下；平面字号统一（0.9.4） | `Analysis/GlassEnvelope.cs`（`Analyzer` 在 LevelAssignment 前加入合成体块，图层 `PD::INDOOR CIRCULATION`）；`Analyzer.Indoors`；`SectionGeometry`（Slab lines）；`PlanDrawing.FitShrinking / Label` | `cleanup.glassEnvelope / envelopeProgram / envelopeGapFt / envelopeMinAreaSf / flatMassMaxThicknessFt`；`axon.exploded.volumesAtModelHeight`；`plan.labelUniform` |
| 轴测风格 0.9.3：爆炸 A = 中性楼板 + program 体块；开放空间（CIRCULATION、INTERACTION）平铺在楼面；标签放大 | `AxonDrawing.SlabAndVolumes / IsOpen / OnFloor`（爆炸 A / B、分层、叠合可选）；标签 `CentredLabels / FrontCornerLabels`（最后一档 `PD Plane Label Min`） | `axon.openPrograms / openLayerFt`、`axon.exploded.volumeHeightFt / plateFill / plateSideDarken`、`axon.stacked.openProgramsFlat`、`fonts.planeLabel* / planeLabelMin` |
| 字体 Futura PT 大写（Heavy / Book） | `ExportContext.AddStandardStyles`、`runtime.jsx PD.paraStyle`（找不到字体退回 fallback） | `fonts.*`（postScript / fallbackPostScript / allCaps / sizePt） |
| NC.Interior / Furniture / Massing / Screen | 角色 `Classification/LayerClassifier.cs Roles`；平面 `Plans/PlanLinework.cs`；剖面 `SectionGeometry.MassingCut`；轴测 `AxonDrawing.ScreensAndMassing`；Screen 在视图中 = `ViewKind.Facade`（`ProjectedView.Kind`） | `roles.NC.*`（plan / axon / view 画法）；关键词 `layer_keywords.json rules`（first 规则） |
| 配景（公司人物 39 种、树 19 种） | `Entourage/EntourageLibrary.cs`、`EntourageArt.cs`（Symbol）、`Entourage.cs`（`EntouragePlacer`、`EntourageSymbols`）；库由 `tools/entourage_from_ai.py` 从 `assets/entourage/*.ai` 生成 | `entourage.*`（密度、间距、`treeFills`、`treeHeightFt`） |
| 总平面范围 5 倍、画板上限 | `PlanDrawing.SiteExtent`（只算建筑 siteMarginFt 内的 NC 场地物体；超出画布时以场地为中心裁小） | `plan.siteExtentFactor`、`plan.siteMarginFt`、`page.maxArtboardPt` |
| 场地曲线（闭合 = 面、开放 = 线、去重） | `PlanBackground.Collect`（平面、总平面、所有轴测共用） | `roles.*.closedCurves`（`"area"` / `"line"`）、`roles.*.plan/axon.line` |
| 绿地随机植树 | `PlanBackground.PlantGreens`（每块绿地整体种植，按 id 定种子） | `entourage.greenTrees.*`（spacingFt、coverage、edgeInsetFt、roles、maxAboveGroundFt），树冠大小 `plan.treeDiameterFt` |
| 绿地 / 水体 hatch | `PlanBackground.Hatch`；平面 `PlanDrawing.DrawLevel`，轴测 `AxonDrawing.Site` | `roles.*.plan/axon.hatch`（stroke、strokePt、spacingFt、angleDeg、dash） |
| 人行道 4.5 ft、行道树距路缘 1.5 ft、> 50 ft 道路中央绿化带（半圆端头） | `Plans/SiteLayout.cs`（每次导出算一次，`PlanBackground.Collect(ctx, …)` 按范围裁切；剖面 `SectionDrawing.Trees`） | `site.*`、`roles.BG.Sidewalk / BG.Median`、`entourage.sectionTreeBandFt` |
| 总平面屋顶俯瞰画板 | `PlanDrawing.RoofView`（`LevelKind.Roof`） | `plan.siteRoofView / siteRoofTitle / roofTierStroke / roofTierPt` |
| 人物只在硬质铺装上 | `PlanBackground.Walkable`；平面 `People`、轴测 `People`、剖面 / 立面 `SectionDrawing.WalkableAlong` | `entourage.sectionBandFt / elevationBandFt` |
| Screen 实心墙 + 白色底板 + 顶部开敞（不加楼层） | `Analysis/ScreenEnclosures.cs`；轴测 `AxonDrawing.ScreenPrisms`；平面 `DrawLevel` / `RoofView`；剖面 `SectionDrawing.ScreenCuts / DrawScreens` | `screen.*`（thicknessFt 0 = 模型楼板厚度） |
| 爆炸轴测挖方墙跟随 B1 | `AxonDrawing.ExplodedA`、`DropBasements(riseFt)` | `axon.exploded.basementClearFt` |
| 分层轴测 = 轴测剖切 | `AxonDrawing.Levels` | `axon.levels.*` |
| 场地轴测铺满画面 | `AxonDrawing.PageFrame / ClipToFrame` | `axon.site.fillPage / pageWidthIn / pageHeightIn` |
| 轴测视角（角点 / V 形视点） | `ExportContext.AxonAzimuth`；Rhino `PlaceAxonViewCommand`、`AxonViewConduit`、第 8 页下拉框 | `Output.AxonView / AxonViewPoint`（设置里） |
| 设置保存 | `PdSettings.FromJson`（逐项容错）、`Newest`；Rhino `PdSession.Save / SidePath`；导出包 `pd_settings.json` | — |
| 03 轴测楼板（本层露天 `Area`，退台楼层的楼板边同 Rhino）、体块在楼板下切开 | `AxonDrawing.Stacked / Levels`、`UnderPlates`、`UnderScreens` | `exposed.*` |
| 玻璃透明 50%、立面玻璃（幕墙）在 03 轴测中显示 | `AxonDrawing.FacadeGlass / PaintGlass`（单独一遍消隐，图层 `FACADE`）；平面 `ExposedItems / BelowItems` | `exposed.glassOpacity`、`facade.stackedAxonOpacity`、`axon.facadeVeil*` |
| Illustrator 崩溃（RPC_E_SERVERFAULT）后续跑：进度文件、跳过崩溃文件、Rhino 端按进展重试 | `runtime.jsx PD.run / PD.progress / PD.readProgress`；`PD.Rhino/Output/IllustratorLauncher.Run`；`OutputTab`（消息框显示最后几步） | — |
| 消隐排序（循环只在真正的循环里取最近面；共面只相接的面不排序） | `AxonFaces.Render`、`CycleBreaker`、`InFront` | — |
| 薄体块（≤ 2 ft，如 B1 地面 circulation 板）切片落空时在半高再切 | `Cleanup/LevelAssignment.Run` | `rules_default.json cleanup.flatMassMaxThicknessFt` |
| 04 爆炸 / 分层轴测的立面玻璃（每层楼板上立本层玻璃 / 画到本层顶）与平面玻璃 50% | `AxonDrawing.ExplodedA / Levels`、`GlassBand`、`PaintGlass(…, layer)` | `facade.explodedAxon`（"storey" / "hidden"）、`facade.stackedAxonOpacity`、`exposed.glassOpacity` |
| 洞口 = 楼板 + 体块的洞（首层楼板开洞到 B1） | `SeenBelow.OpeningsAt`、`GroundSlabs` | — |
| 爆炸轴测图层：场地 › 地下连接带 › 地下层 | `Sheet.UnderSite`、`Page` 图层顺序 | — |
| 坐标吸附 1 mm（Rhino 原始坐标导致布尔运算失败：screen 内部空白、残留面、树穿墙） | `Analysis/Localizer.cs`（`LocalFrame.SnapFt`）；`AxonFaces.Robust`（第三层吸附回退、失败计数 → export_log CHECK） | `rules_default.json cleanup.coordSnapFt` |
| 屋顶判定：本层露天 `Area`（平面、爆炸）/ 无上层体块 `Sky`（03 轴测）；体块拉伸停在露天屋顶板下 | `ExposedSurfaces`（`Flats`、`Above`、`SkyUpTo`）；`AxonDrawing.UnderRoofs` | `exposed.*` |
| 平面下层顶视 + 洞口 OPEN TO BELOW | `Plans/SeenBelow.cs`；`PlanDrawing.BelowItems` | `roles.NC.Roof.plan`、`plan.void*`、`plan.openToBelowLabel` |
| 屋顶俯瞰画板层阶含屋顶板 | `PlanDrawing.RoofView`、`RoofLevels` | `plan.roofTier*` |
| 爆炸轴测地下连接带在场地后 | `AxonDrawing.ExplodedA`（图层 `AXON_CONNECTOR_BELOW`，`Page` 图层顺序） | `axon.connector*` |
| 露天楼板 / 平面玻璃（屋顶 / 露台 / 雨棚） | `Analysis/ExposedSurfaces.cs`（上面没东西的 NC.Slab / 平面 Facade）；平面 `PlanDrawing.ExposedItems`；轴测 `AxonDrawing.Exposed / GlassHatch` | `exposed.*`（minAreaSf、glassFill、glassHatch、line*） |
| NC.PropertyLine（平面正规点划线、04 场地边缘 + 50 ft、剖面虚线） | 角色 `Roles.PropertyLine`；`PlanBackground.PropertyArea / IsLineRole`；`AxonDrawing.PropertySite`、`Site(..., shape)`；`SectionGeometry` | `roles.NC.PropertyLine.*`、`axon.propertySiteMarginFt`；关键字 `layer_keywords.json` |
| 图例符号（入口、服务、视点、车、流线、树、标签、周边建筑名、街名、注释） | `Drawing/AnnotationSymbols.cs`；平面 `PlanLegendSheet`（第 4 列）、总平面 LEGEND & SYMBOLS 画板；轴测 `AxonDrawing.Page` 的 LEGEND 画板 + 图名处比例与指北针 `AxonNorth` | `annotation.*` |
| 树 / 配景颜色（第 ⑦ 页 Colors） | `Style.ApplyColors`（`Exporter.BuildDrawings` 调用）；Rhino `ColorsTab` | 设置 `TreeColor` / `PeopleColor`（空 = 默认） |
| 固定画板时平面 / 剖面 / 立面铺满画板 | `PlanDrawing.FitArtboard`、`SectionPlane.Fit`（剖面 / 立面） | `rules_default.json` 画板尺寸 |
| 建筑范围只算体块（不含曲线） | `Analysis/Localizer.cs` | — |
| 03 轴测层线 0.5 pt、树多裁 1 pt；场地轴测范围 +100%；绿化带两端退 20 ft | `AxonDrawing.HideBehind(…, treeGap)`、`AxonFaces.Hide(gap)`；`SiteLayout` | `axon.site.levelLinePt / siteMarginFt / contextMarginFt`、`axon.treeClearPt`、`site.median.endClearFt` |
| 04 爆炸轴测描边（每层外轮廓、白色内线） | `AxonDrawing.PlateFaces` | `axon.levels.edgePt`、`axon.slabSilhouettePt` |
| 平面铺装 / 道路交界线 | `PlanDrawing.SiteEdges`（图层 `BG_SITE_EDGE`） | `plan.hardscapeEdge`、`plan.roadEdge` |
| 图例符号画板 | `Plans/PlanLegendSheet.cs`（`01_Plans.ai` 最后一个画板） | — |
| LEVEL X 标签 | `PlanDrawing.TitleTag` | `plan.titleTagFill`（空 = 无底色），字号 = `fonts.label` |
| < 0.2 ft 缝视为贴合 | `Cleanup/LevelAssignment.Seal`（面积在封缝前统计） | `rules_default.json cleanup.sealGapFt` |
| 剖面标签居中 / 缩写 | `Sections/SectionBars.cs`（`Fit`、`TextWidth`） | `section.labelSize*`、`labelInsetPt` |
| Penthouse 灰块、无屋面板、T.O. 标高 | `SectionDrawing.Penthouse / DrawPenthouse`、`LevelMarkers.One` | `section.penthousePrograms / penthouseFill / penthouseLabel / penthouseTopLabel` |
| 剖面人物随机 | `EntouragePlacer.AlongLine`（随机 + 成对 + 避开文字） | `entourage.densityByProgram`、`minSpacingFt` |
| 场地轴测 / 叠合轴测：无楼板、层间线（场地 2 pt，叠合 0.5 pt 细线）、只描外轮廓、不画地下层 | `AxonDrawing.Stacked(site)`、`FullHeight`、`LevelLines`（图层 `AXON_LEVEL_LINE`）、`AboveGrade` | `axon.site.*` 与 `axon.stacked.*` 的 `fullHeightBlocks / levelLinePt / levelLineColor / showBasements`、`axon.stacked.silhouettePt` |
| 屋面核心筒错位 | `AxonFaces.SplitAtNeighbours`（体块在相邻体块顶 / 底处分段后再消隐） | — |
| 爆炸轴测场地在 L1、地下层整体在场地板下方（A 画挖方墙；B 同样下移） | `AxonDrawing.ExplodedA / ExplodedB`、`DropBasements`、`GroundAndExcavation`、`Site(..., excavation)`、`Excavation` | `axon.exploded.basementClearFt`、`axon.exploded.*` |
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
3. 截图：`node tools/svg_shot.mjs <in.svg> <out.png> 1400`，局部放大用 `convert out.png -crop WxH+X+Y +repage crop.png`；`PD_ARTBOARD="Fit content"` 让 PD_USER 出图改用 Fit content（看爆炸轴测全图）。
   03 与模型逐像素对比：`tools/axon_check/run.sh <workdir>`（需 numpy / pillow / scipy）——用 z-buffer 渲染 Downtown fixture（= Rhino 看到的），按 program / 楼板分类与我们的 03 比对，列出不一致区域"我们的图元 | 模型物体"；地下室和 1 ft 楼板边是正常噪声。v0.8.7 的 03 楼板问题就是这样定位的。
4. 金样：`DowntownTests.ExportBuildsEveryDrawing` 等测试会重写 `tests/golden/*.svg`，提交前看一眼变化。
5. 测试：`dotnet test PD.sln -v q --nologo 2>&1 | tail -3`（本容器约 7 分钟；`S6AcceptanceTests.T6` 跑完整导出最慢）。

## 6. 请用户在 Windows 上回报的内容
- `illustrator_log.txt` 中所有 WARN / ERROR 行；`export_log.txt` 中的 NOTE。
- 每个问题：哪张图（文件名 + 画板）、截图、期望效果；如可能附上模型或图层名。
- 重点验证（Linux 上测不到）：AI Symbol 是否生成（Symbols 面板有 `PD_PERSON_*` / `PD_TREE_*`，否则会退回普通编组）、Futura PT 是否生效、剖面标签是否真正居中（AI 按字形居中）、`_MANUAL` 图层重新导出后是否保留。

## 7. 待确认 / 已知问题
- 已澄清（v0.8.1）：场地跟着 L1 走；B1 要表现为地下层，不能压在场地上。03 不画地下层；爆炸轴测地下层落在整块场地板下方。若用户希望地下层离场地更近，调 `axon.exploded.basementClearFt`（可为负，但会重新压到场地上）。
- 总平面在 Windows 上消失的原因是画板超过 Illustrator 画布；若再出现，看 `illustrator_log.txt` 的 "larger than the Illustrator canvas"，再查哪个 NC 场地物体过大 / 过远。
- Penthouse 的判定：顶层只含 SHELL / CORE / STAIR / BUILDING_SUPPORT（`section.penthousePrograms`）；模板模型的 L11 设备层也被当作 penthouse。
- 平面顶视人物是内置简图（公司库只有立面人物）；立面图不画树（剖面画）。
- 文字宽度是估算值（剖面缩写、图例列宽）；字体换了要校 `SectionBars.TextWidth`。
- 场地丰富度取决于模型：街区来自 LOT FILL 等 BG.Hardscape / 公园 BG.Landscape / 闭合的道路曲线（ROAD LINES），没有街区就没有人行道和行道树。用户 0.8.2 的 preview 里没有 LOT FILL 与公园填色，应检查第①页这些图层的角色。
- 屋面 screen 在 Linux 上只用合成模型验证过（`ScreenModel`）；真实模型的 screen 若是多段曲面或有缺口，`screen.closeGapFt` 控制视为闭合的缝宽。
- 轴测 V 形视点、"Load settings…"、模型旁 `.programdiagram.json` 都是 Rhino 端（VERIFY-WIN）。
- v0.9.1：CW markup V2。新样式键 `plan.grid*`、`plan.overhead*`、`section.siteCut*`、`axon.site.massing`、`axon.exploded.tallConnectors`；生成脚本 `deliverables/2026-10-19_RIVER_MARKUP_V2/scripts`。
- v0.9.0：CW markup（RIVER）。新测试模型 `tests/fixtures/river.json`（+ `river_settings.json`，`RiverAnalysis`）；生成脚本在 `deliverables/2026-10-18_RIVER_MARKUP/scripts`。新样式键 `plan.siteNameRoles` / `plan.siteNamePt`、`greenTrees.openNameKeywords`。
- v0.8.9：RIVER 模型导出报 "found non-noded intersection"（斜墙共用产生的细缝）。`MeshSlicer.Factory` 改为 OverlayNG，所有布尔运算走 OverlayNGRobust；注意 NTS 不允许与其他工厂（`new GeometryFactory()`）的几何混算，测试里也要用 `MeshSlicer.Factory`。
- v0.8.8：v0.8.7 在 Windows 上（11x17 画板）Illustrator 两次 RPC_E_SERVERFAULT、无日志。原因未定位（JSX 在 Linux 检查无异常；之前 Fit content 的 20 MB JSX 成功过）。已让脚本可续跑并记录崩溃位置——下一轮请用户发 `illustrator_log.txt`（含 "crashed while drawing it (last step: …)"）或 `illustrator_progress.txt`，据此定位到文件 / 图层 / 200 个图元内。可疑方向：11x17 时 01_Plans 11.7 MB / 1.2 万图元（总览画板把各层再画一遍）、PNG 300% 导出。
- v0.8.7：用户把 v0.8.6 的四项原文再次发来（附 v0.8.6 预览，无截图）。03 的根因（消隐循环处理）和 B1 circulation 丢失已修；04 图层顺序 v0.8.6 已是 场地 › 地下连接带 › B1（若用户指的是别的"circulation layer"，请其截图说明）；00_Site_Plan 首层 24×48 ft 楼板洞在 v0.8.6 预览中已画出（虚线 + X + OPEN TO BELOW，1/64" 下很小）——若用户说的"镂空"是首层架空 / 中庭（塔楼与 LAB SUPPORT 之间、上有楼板的露天地面），需确认是否要在总平面画上层楼板投影虚线；已在回复中询问。04 爆炸 B（平面斜轴测）未加立面玻璃。
- v0.8.6：坐标吸附后 Windows 端 screen 正常、export_log 无 overlay 失败。玻璃（平面玻璃、立面幕墙）不参与遮挡，单独一遍消隐后以 50% 画在 `FACADE` 层；04 爆炸 / 分层轴测的立面玻璃未加（玻璃跨多层）。
- v0.8.5：用户 Windows 预览里 03 / 分层轴测的 screen 内部空白、核心筒残留面、树穿墙在 Linux 上无法用 fixture 复现（fixture 坐标是整毫米）；加噪声后连分析都会失败 → 坐标在 `Localizer` 吸附到 1 mm。若 export_log 仍有 "CHECK axons: … overlay(s) failed"，记下其中的 NTS 版本（Rhino 里可能加载了别的 NetTopologySuite）。
- 测试用用户设置：`tests/fixtures/downtown_settings.json`（用户 pd_settings.json 副本；v0.8.5 起视角 NE、`property line` → NC.PropertyLine；用户本轮实际用 "Fit content"，看爆炸轴测全图时临时改），`DowntownTests.UserContext`，`PD_USER=1` 出图（剖切线 / 视点 ft → mm）。
- v0.8.4：用户 PROGRAM::Break Room 图层上有剖切线，曾把建筑范围撑到 ~1600 ft（`Localizer` 已改为只算体块）。周边建筑与截图逐块核对一致，平面样式统一为轴测白色 + 灰描边；若仍有不对，请用户指出具体哪栋。
- NC.PropertyLine 只在合成模型上验证（`Rev084Tests.WithProperty`）；用户需在第 ① 页把红线图层设为 NC.PropertyLine。
- 04 爆炸轴测在 1/64" 时仍超出 11x17（楼层叠高所致）；平面 / 剖面 / 立面已铺满固定画板。
- 轴测预览名多了 `_LEGEND` 画板，原名带视图后缀（如 `03_Site_Axon_SITE_AXON`）。
- 尚未做：流线 / 视线图标的自动放置（目前只有图例符号，图元画在 `ANNOTATION`）、key plan、Unassigned 斜线。
- 分支上曾有其他会话同时推送：开工前先 `git fetch` + `git pull`，冲突时以远端为准再合并自己的改动。
