# RimTrans 翻译提取工具 - 交接文档（HANDOFF）

> 用途：在新对话中零上下文接手本项目。本文档包含全部关键事实、已完成的调研结论、代码位置和注意事项。
> 配套文档：`PLAN.md` / `PLAN.html`（完整开发计划）。三份文档均在项目根目录 `D:\Visual Studio Code ALL\A Rimwold\Trans\`。
> 交接日期：2026-10-17。项目当前状态：**M1-M5 完成 + M6 审阅窗口完成（待用户真机过一遍 UI）。剩 M7 单文件发布**。软件定名：边缘译者 RimTranslator。
> UI 已重排为五组卡片：①扫描 ②选择Mod ③输出位置 ④翻译AI ⑤进度与日志（GroupBorder/GroupTitle 样式，流程从上到下）。

---

## 0. 一句话项目定义

做一个 Windows 桌面 GUI 工具（C# WPF / .NET 9，单文件 exe），离线复刻创意工坊 Mod「Extract translation」（id=3026452122）的提取功能：不进游戏、图形界面选 Mod → 提取 Keyed + DefInjected 待翻译内容 → 术语库回填官方译名 → AI 翻译剩余 TODO → 人工审阅后写入，目标语言可选（简中/繁中/英/日/韩/俄等）。

## 1. 事实速查（决策已定，不要重新讨论）

| 项 | 决定 |
|---|---|
| 项目路径 | `D:\Visual Studio Code ALL\A Rimwold\Trans\` |
| 技术栈 | C# WPF，net9.0（本机已装 dotnet SDK 9.0.315），双项目：`RimTrans.Core`（逻辑库，无UI依赖）+ `RimTrans.App`（WPF界面），发布单文件 exe |
| 输出位置 | 默认写入**原 Mod 目录** `Languages\<语言>\`；UI 三档可选：①原Mod目录 ②自定义目录（按 Languages 结构写入）③独立汉化包骨架（自动生成 About.xml/LoadFolders.xml） |
| 术语库来源 | **只扫游戏官方翻译**（`Data\<DLC>\Languages\<语言>`），不扫用户自制汉化 |
| AI 供应商 | **Player2 默认置顶**（无需密钥开箱即用），其余：Google/OpenAI/DeepSeek/Grok/GLM/OpenRouter/SiliconFlow/本地模型/自定义（照搬 RimTuber 注册表） |
| 翻译确认 | AI 译完先弹**审阅对照表**（EN原文/术语命中/AI译文/可编辑），用户确认后才写盘 |
| 扫描源 | 创意工坊 + 本地 Mods + 游戏本体 Data + **自定义扫描源按钮**（目录列表持久化，可增删） |
| 版本目录 | 解析 `LoadFolders.xml`，UI 提供 1.6/1.7 等版本选择（对应 `<v1.6>` 等节点） |
| 安全 | 覆盖任何文件前自动备份 `.bak`；写入创意工坊目录时弹"更新会覆盖"警告 |
| PatchOperation | **V1.1 再做**（见第 8 节醒目提醒） |

## 2. 参考模组「Extract translation」反编译结论（已验证，直接照此实现）

对象：`D:\Steam\steamapps\workshop\content\294100\3026452122\1.6\Assemblies\Translate_Library.dll`（1.7 版同目录结构，dll 为 net10 编译）。核心类 `Translate_Library.Window_ExtractTranslation`。

### 2.1 整体流程
点击某 Mod 按钮后依次执行三步：
1. `ExtractTranslate(mod)` —— 生成 DefInjected
2. `ExtractKey(mod)` —— 生成 Keyed
3. `CleanupBackstories(mod)` —— 背景故事旧格式处理（V1.0 可不做）

### 2.2 ExtractTranslate（DefInjected 生成）
- 目标目录：`<Mod根目录>\Languages\<activeLanguage.folderName>\DefInjected\`
- 若存在旧 `DefLinked` 目录且无 `DefInjected`，先 `Directory.Move` 改名（等 1 秒）
- **清空** DefInjected 下所有现存 xml
- 遍历 `GenDefDatabase.AllDefTypesWithDatabases()`，对每个 Def 类型**反射调用**游戏本体私有静态方法 `Verse.TranslationFilesCleaner.CleanupDefInjectionsForDefType(Type defType, string defInjectionsFolderPath, ModMetaData mod)` —— 真正的注入文件生成逻辑在游戏本体里（见第 5 节）
- 对非 RimWorld/Verse 命名空间的 Def 类型：生成的 `<DefTypeName>` 目录改名为 `<全名含命名空间>`（如 `AncotLibrary.DroneWorkModeDef`）
- **关键差异**：它依赖运行时 Def 数据库（继承、Patch 都已合并）。我们离线实现必须自己做 XML 继承合并（Name/ParentName/Abstract），这是本项目最大工作量

### 2.3 ExtractKey（Keyed 生成）
- 收集当前语言 `keyedReplacements`：排除 placeholder、排除英文已有的 key、排除 `fileSourceFullPath` 含 `RimWorld\Data` 的（即只留来自 Mod 的既有翻译）
- 扫描两处：Mod 根 `Languages\` 与**版本子目录**（如 `1.6\Languages\`，它用两层 GetDirectories 找名为 Languages 的目录）——我们改为按 LoadFolders.xml 精确定位
- 对每个 Languages 目录（`ExtractSingleKey`）：
  1. 旧 `CodeLinked` 目录 → 改名 `Keyed`
  2. **前置条件**：`Languages\English\Keyed\*.xml` 必须存在，否则跳过该目录（记日志）
  3. 清空目标语言 `Keyed\` 下现存 xml，把 English Keyed 的 xml（含子目录，保持相对路径）整份复制过去
  4. 对每个复制来的 xml（XDocument, PreserveWhitespace）：
     - 根元素为 `<LanguageData>`
     - 每个子元素：删除其文本节点，在前方插入注释 `<!-- EN: 英文原文 -->`（注释内容经 `SanitizeXComment`：把 `--` 替换为 `- -` 防止非法注释）
     - 回填：若当前语言已有该 key 翻译则填入（`\n` 转义为 `\\n`），否则填 `TODO`
     - 属于该 Mod 但 English Keyed 里不存在的 key：追加在 `<!-- UNUSED -->` 注释段之后
  5. 保存：`<?xml version="1.0" encoding="UTF-8"?>\r\n` + 内容（把 `<!--NEWLINE-->` 移除、`&gt;` 还原为 `>`），UTF-8 写盘

### 2.4 语言文件夹名
`LoadedLanguage.folderName` 即 `Languages\` 下的目录名，如 `ChineseSimplified`、`ChineseTraditional`、`Japanese`、`Korean`、`Russian`、`English`。工具应枚举 `Data\Core\Languages\`（tar 名或已解压目录）+ Mod 自身 Languages 建立可选列表。

## 3. RimTuber 的 Player2 / AI 集成速查（照抄即可）

源码位置：`D:\Visual Studio Code ALL\RimTuber\RimTuber\Source\Content\AIProviderManager.cs` 与同目录 `RimTuberProviderRegistry.cs`。

### 3.1 Player2 本地服务流程（AIProviderManager.cs:1095 CheckPlayer2Local）
1. `GET http://localhost:4315/v1/health`（超时 2-3 秒）失败 → 本地服务未运行
2. `POST http://localhost:4315/v1/login/web/019e12ba-6062-79ae-8c76-de13bea9af7a`，body `{}`，Content-Type: application/json
3. 响应 JSON `{ "p2Key": "..." }` → p2Key 作为后续 API key
4. 正式请求：端点 `http://localhost:4315/v1/chat/completions`，请求头 `player2-game-key: 019e12ba-6062-79ae-8c76-de13bea9af7a`
5. **clientId 固定值：`019e12ba-6062-79ae-8c76-de13bea9af7a`**（与 RimTuber 共用，用户明确指定）
6. 未运行时的错误提示：Player2 桌面程序未启动/未监听 4315 端口 → 引导用户启动桌面端

### 3.2 端点规范化 NormalizeEndpointUrl（AIProviderManager.cs:770，必须照搬，用户全局规则）
```
输入 trim + 去尾部 /
1. 已以 chat/completions 或 /completions 结尾 → 原样
2. 含版本段（正则 /v\d+[a-z]*(/|$)，或以 /openai 结尾，含 /v1 /v3 /v4 /v1beta /api/paas 等）→ 追加 /chat/completions
3. 纯域名 → 追加 /v1/chat/completions
405 响应 → 单独提示"端点缺少路径，已自动补全，请重试"
测试连接与正式调用必须走同一个规范化出口；不回写用户填的原值
```

### 3.3 供应商注册表（RimTuberProviderRegistry.cs，端点/默认模型可直接复用）
| 供应商 | 端点 | 默认模型 | 要Key |
|---|---|---|---|
| **Player2（置顶默认）** | http://127.0.0.1:4315/v1/chat/completions | Default | 否 |
| Google Gemini | https://generativelanguage.googleapis.com/v1beta/openai/chat/completions | gemini-2.0-flash | 是 |
| OpenAI | https://api.openai.com/v1/chat/completions | gpt-4o-mini | 是 |
| DeepSeek | https://api.deepseek.com/v1/chat/completions | deepseek-chat | 是 |
| Grok | https://api.x.ai/v1/chat/completions | grok-3-mini | 是 |
| GLM | https://api.z.ai/api/paas/v4/chat/completions | glm-4-flash | 是 |
| OpenRouter | https://openrouter.ai/api/v1/chat/completions（额外头 HTTP-Referer、X-Title） | openai/gpt-4o-mini | 是 |
| SiliconFlow | https://api.siliconflow.cn/v1/chat/completions | （空） | 是 |
| 本地模型 | http://localhost:1234/v1/chat/completions（LM Studio；Ollama 11434；vLLM 8000） | （空） | 否 |
| 自定义 | 用户填 | 用户填 | 是 |

请求体：OpenAI 兼容 `{model, messages:[{role:system},{role:user}], max_tokens, temperature}`；WPF 用 HttpClient + System.Text.Json 即可（不需要 UnityWebRequest）。

## 4. 本机关键路径（都已实地验证存在）

| 内容 | 路径 |
|---|---|
| 创意工坊 Mod 库（约 830 个） | `D:\Steam\steamapps\workshop\content\294100\` |
| 本地 Mods（36 个，含 MaiyaMod/Maiya Trans 系列） | `D:\Games\steamapps\common\RimWorld\Mods\` |
| 游戏本体 Data（Core/Royalty/Ideology/Biotech/Anomaly/Odyssey） | `D:\Games\steamapps\common\RimWorld\Data\` |
| 游戏当前版本 | 1.6.4871 rev590（Version.txt；Steam 分支 public；用户也装了 1.7 测试版会用到 LoadFolders 版本目录） |
| 官方语言 tar 示例 | `Data\Core\Languages\ChineseSimplified (简体中文).tar`（各 DLC 同名目录下各一份；解压后即 Languages\<folderName>\ 结构） |
| 参考模组（提取器本体） | `D:\Steam\steamapps\workshop\content\294100\3026452122\` |
| RimTuber 源码 | `D:\Visual Studio Code ALL\RimTuber\RimTuber\Source\Content\` |
| dotnet SDK | 9.0.315（`dotnet --list-sdks` 已验证） |

注意：`tar` 文件读取——.NET 9 有 `System.Formats.Tar`，可直接读取 tar 内文件而不解压；或检测同名已解压目录优先使用。

## 5. 需要阅读的游戏源码（实现 DefInjected 遍历规则的依据）

用 rimworld-source MCP（或 ILSpy 反编译 `D:\Games\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`）阅读：
- `Verse/TranslationFilesCleaner.cs` —— `CleanupDefInjectionsForDefType`：逐 Def 逐字段生成注入路径的完整规则（含 translatedFieldPaths、列表索引、嵌套类、possibleDefInjection 判定）
- `Verse/DefInjectionPackage.cs` —— 注入路径语法（`fieldName`、`fieldName.index`、`fieldName.nestedField`）与回填规则
- XML 继承规则参考 `Verse/DirectXmlToObject` / `XmlInheritance`：`Abstract="True"` + `Name` 定义基节点，子节点 `ParentName` 继承，字段级覆盖合并
- 生成的文件格式：`DefInjected\<DefType类名>\<defName>.xml`，内容 `<LanguageData><label>译文</label>...<!-- EN: 原文 -->...</LanguageData>`（EN 注释紧邻对应元素之前）

## 6. 开工顺序（里程碑，详见 PLAN.md 第 6 节）

1. **M1** ~~解决方案骨架 + Mod 扫描器~~ ✅ 完成（验收 828 工坊+32 本地+6 Data，About/LoadFolders/语言目录解析无误；ModInfo 必须用属性而非字段供 DataGrid 绑定；Data 来源默认不勾选——官方翻译已完备，仅术语库用）
2. **M2** ~~Keyed 提取~~ ✅ 完成（`Extraction/KeyedExtractor` 等对齐参考模组逐节点行为；临时目录测试通过）
3. **M3** ~~Def 继承合并 + DefInjected 生成~~ ✅ 完成（`Defs/XmlInheritanceMerger`+`DefInjectionWalker`+`DefInjectedGenerator`+`ExistingDefInjectionLoader`；真实 Mod RimTalk(3551203752) 与官方提取产物语义 diff 一致，除跨 Mod 继承 ResourceBase 3 条——已知 V1.0 边界。⚠️ 教训：XDocument 加载 Defs 一律不用 PreserveWhitespace，否则缩进空白被当文本节点导致继承合并整体替换成空白）
4. **M4** 术语库：官方构建（`OfficialGlossaryBuilder`：扫 Data tar/目录 EN↔译文对）+ 自定义词库（`Glossary`：`%APPDATA%\RimTrans\glossary-<语言>.json` + `GlossaryWindow` 增删改/导入合并）✅ 基础完成；**待做：提取后回填接线 + AI prompt 术语约束**
5. **M5** AI 层（Player2 探测/授权/头部 + 供应商注册表 + NormalizeEndpointUrl + 批量翻译 + prompt 附术语约束）——UI 已有供应商注册表与设置持久化（AppSettings.Ai*），测试连接与调用待实装
6. **M6** 审阅对照表 UI + 确认写入 + 完成报告
7. **M7** 单文件发布验证（`dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true`）

UI 已完成：右键菜单（提取/翻译/翻译并生成独立汉化Mod/打开文件夹——后两项 M5 实装）、进度条+状态文本（提取循环逐 Mod 更新）、Data 复选框默认不勾、词库管理窗口。
进度条注意：AI 翻译批次也要驱动 SetProgress（M5 接线时别忘了）。

附加决定（2026-10-17 用户确认）：
- 软件名：**边缘译者 RimTranslator**（生成物 About 描述署名 ExtractionOptions.GeneratorName）
- "汉化包骨架"UI 名称改为"生成独立汉化Mod"
- UI 多语言：V1.1 再做（V1.0 简中）
- 教程：内置教程窗口（HelpWindow.xaml）已做；在线文档链接后续可加
- Tar：System.Formats.Tar 在 net9 共享框架内，无需 NuGet 包

每步验证方法：同一 Mod 分别用游戏内参考模组提取和本工具提取，目录 diff（推荐小 Mod 如 `Maiya Trans01 RimTalk边缘世谭`，结构标准）。

## 7. 注意事项（用户全局规则相关，务必遵守）

1. **RimWorld 游戏运行期间禁止构建/同步**：进程名 1.6 为 `RimWorldWin64.exe`、1.7 为 `RimWorld.exe`，构建本工具前先检查（两者任一在跑就要先问用户）。本项目是独立 WPF 程序，不受 RimWorld 模组构建规则约束，但该检查规则照用。
2. **创意工坊目录写入**：用户全局规则默认工坊目录只读，但**本工具的"写入原 Mod 目录"功能是用户明确要求的设计**（与参考模组行为一致）。工具执行写入时必须：弹覆盖警告 + 自动 .bak 备份。Agent 自己测试时也不得静默写工坊目录，要走工具流程或先问。
3. **游戏内显示文字禁用 emoji 和 `&`**：生成/修改任何进入游戏的 XML 文本时，禁 emoji（用文字标签如 `[礼物]`），避免 `&`（用 "and"/"和" 或转义 `&amp;`）。本工具产出的翻译 XML 同样受限。
4. **API 端点 405 修复**：任何 AI 端点调用必须带 NormalizeEndpointUrl（第 3.2 节），405 单独提示。
5. 界面语言：工具 UI 用简体中文；代码注释可中可英；项目内文档不使用 emoji。
6. 提取行为细节以第 2 节反编译结论为准，不要凭印象实现。

## 8. 醒目提醒：PatchOperation 后置到 V1.1

> **V1.0 不实现 PatchOperation（PatchOperationAdd/Replace/Remove/Insert/FindMod）解析。**
> 后果：使用补丁动态新增/修改文本的 Mod，离线提取会**漏掉补丁产生的那部分内容**（纯 XML + 继承合并估计覆盖约 90% 的 Mod）。
> 计划：V1.1 实现 Add/Replace/Insert 三个高频操作。实现时参考 `Verse/PatchOperation*` 系列源码的 xpath 语义（支持 LibsHarmony 式第三方 Patch 类不做承诺）。
> **每个版本发布前，在 README/更新说明里都要向用户声明此限制。**

## 9. 已知风险与边界

- 运行时由 C# 代码动态注册的 Def（代码生成 Def）离线抓不到——游戏内提取器能抓到。属离线方案固有边界，文档中声明。
- 1.7（Waylith 引擎）的游戏本体目录布局可能不同（用户 1.7 经 RimModManager 启动，Data 路径待确认）——V1.0 以 1.6 目录为准，1.7 兼容放 V1.1。
- Mod 内非标准结构（Languages 藏在版本目录、DefLinked/CodeLinked 旧目录）第 2 节已有处理方案。
- 术语匹配只做精确/大小写不敏感匹配起步；模糊/包含匹配 V1.1 再评估（误报反而制造坏翻译）。

---

接手后第一件事：读 `PLAN.md` 了解全貌，然后按第 6 节 M1 开工。遇到与本文档冲突的新事实，以实际代码/文件为准并回写本文档。
