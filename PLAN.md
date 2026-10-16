# RimTrans 翻译提取工具 - 项目计划（PLAN）

> 项目路径：`D:\Visual Studio Code ALL\A Rimwold\Trans\`
> 配套文档：`HANDOFF.md`（交接文档，含反编译结论与代码位置速查）、`PLAN.html`（可视化版）
> 版本：V1.0 定稿 2026-10-17

---

## 1. 项目定义

做一个 Windows 桌面 GUI 工具，离线复刻创意工坊 Mod「Extract translation」（id=3026452122）的功能：**不进游戏**，在图形界面中选择 Mod → 提取 Keyed + DefInjected 待翻译内容 → 术语库回填官方译名 → AI 翻译剩余 TODO → 人工审阅 → 确认写入。目标语言可选（简中/繁中/英/日/韩/俄等，从游戏目录动态枚举）。

- 解决的痛点：原模组必须进游戏操作；普通玩家不会用；翻译者拿到提取文件后还要手动丢给 AI
- 目标用户：汉化组作者（用户本人，Maiya Trans 系列）与想给任意 Mod 快速补翻译的普通玩家

## 2. 技术栈与结构

| 项 | 决定 |
|---|---|
| 框架 | C# WPF，net9.0（本机 dotnet SDK 9.0.315） |
| 结构 | `RimTrans.Core`（类库：扫描/解析/提取/术语/AI，无 UI 依赖，可单测）+ `RimTrans.App`（WPF 界面） |
| 发布 | `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true` 单文件 exe |
| 依赖 | 零第三方 NuGet 起步（XML 用 System.Xml.Linq，tar 用 System.Formats.Tar，HTTP 用 HttpClient，JSON 用 System.Text.Json） |

## 3. 功能规格

### 3.1 Mod 扫描器
- 来源（四类，UI 分类标注）：
  1. 创意工坊：`D:\Steam\steamapps\workshop\content\294100\*`
  2. 本地 Mods：`D:\Games\steamapps\common\RimWorld\Mods\*`
  3. 游戏本体 Data（DLC）：`D:\Games\steamapps\common\RimWorld\Data\*`
  4. **自定义扫描源**：UI"添加目录"按钮，任意路径，列表持久化到配置文件，可删除
- 每个 Mod 解析 `About\About.xml`（name/packageId/supportedVersions）与 `LoadFolders.xml`（`<v1.6>` 等版本节点）
- UI：列表 + 搜索框 + 来源筛选 + 版本选择（1.6/1.7 等，决定用根目录还是版本子目录的 Defs/Languages）

### 3.2 提取器（行为与参考模组一致，细节见 HANDOFF.md 第 2 节）
- **Keyed**：以 `Languages\English\Keyed\*.xml` 为源 → 目标语言 `Languages\<语言>\Keyed\`：值清空 + `<!-- EN: 原文 -->` 注释 + 已有翻译回填 + 其余 TODO + UNUSED 键归并；处理旧 `CodeLinked` 目录改名
- **DefInjected**：离线解析 Mod 的 Defs XML —— `Abstract/Name/ParentName` 继承合并 → 按 `Verse.DefInjectionPackage` / `TranslationFilesCleaner.CleanupDefInjectionsForDefType` 的字段遍历规则（string、List\<string\>、嵌套类、列表索引路径）生成 `DefInjected\<DefType>\<defName>.xml`
- **V1.0 不做 PatchOperation**（见第 8 节醒目提醒）

### 3.3 输出目标（三档可选，默认第一档）
1. **原 Mod 目录**（默认）：`<Mod根目录>\Languages\<语言>\`，与参考模组一致，玩家提取完直接可用
2. **自定义目录**：浏览选目录，按 `Languages\<语言>\Keyed|DefInjected\...` 结构写入（对接本地开发副本）
3. **独立汉化包骨架**：自动生成完整 Mod：`About.xml`（packageId=`<原id>.zh`风格 + 依赖声明）、`LoadFolders.xml`、`Languages\<语言>\`——可直接发布的汉化 Mod 雏形

安全规则：覆盖任何文件前自动 `.bak` 备份；写入创意工坊目录弹"更新会覆盖"警告。

### 3.4 术语库（Glossary）
- 来源**仅游戏官方翻译**：`Data\<各DLC>\Languages\<语言>\`（tar 或已解压目录）的 DefInjected + Keyed
- 构建"EN 原文 → 官方译名"对照库（首次运行构建，缓存到本地，语言切换时重建对应语言的库）
- 用途：提取后自动术语匹配 → 命中直接回填（TODO 秒消）；未命中进 AI 队列，且 prompt 附带命中术语作约束（防自创译名，如"Warg=步行兽"）
- V1.0 只做精确/大小写不敏感匹配；模糊匹配 V1.1 评估

### 3.5 AI 翻译层
- **Player2 默认置顶**（无需密钥）：
  - 探测：`GET http://localhost:4315/v1/health`
  - 授权：`POST http://localhost:4315/v1/login/web/019e12ba-6062-79ae-8c76-de13bea9af7a` → 响应 `p2Key`
  - 请求：`http://localhost:4315/v1/chat/completions` + 头 `player2-game-key: 019e12ba-6062-79ae-8c76-de13bea9af7a`
  - 未运行提示启动 Player2 桌面端
- 供应商注册表照搬 RimTuber（Google/OpenAI/DeepSeek/Grok/GLM/OpenRouter/SiliconFlow/本地模型/自定义，端点与默认模型见 HANDOFF.md 3.3）
- **NormalizeEndpointUrl 端点规范化（强制）**：已含 completions 不动；含版本段（/v1、/v4、/v1beta、/openai、/api/paas…）补 `/chat/completions`；纯域名补 `/v1/chat/completions`；405 单独提示"端点缺少路径，已自动补全，请重试"；测试连接与正式调用同一出口；不回写原值
- 批量翻译：TODO 条目分批（按 token 预算）发送，JSON 批量请求/响应（编号对齐），失败条目重试一次后标记失败
- 翻译 prompt：系统提示词含目标语言、RimWorld 语境、术语约束表；输出仅译文

### 3.6 审阅与写入
- AI 译完弹**审阅对照表**：EN 原文 | 术语命中标记 | AI 译文 | 可编辑单元格 | 条目状态（术语回填/AI/失败/TODO）
- 用户逐条检查/修改 → 点"确认写入"才落盘（写盘同样走 .bak 备份）
- 完成报告：总条数/术语回填数/AI 翻译数/失败数/输出位置

## 4. 界面草图（单窗口三区）

```
+--------------------------------------------------------------+
| [扫描源: 工坊▾ 本地▾ Data▾ 自定义+ ]  [刷新]  版本: [1.6▾]    |
| [搜索___________]  语言: [ChineseSimplified▾]                 |
| +------------------------------------------------+           |
| | Mod 列表（名称/来源/包ID/已有语言目录）          |  [提取]    |
| |  （多选，支持 Ctrl/Shift）                       |  [停止]    |
| +------------------------------------------------+           |
| 输出: (o)原Mod目录  ( )自定义目录[浏览]  ( )汉化包骨架[浏览]   |
| +------------------------------------------------+           |
| | 日志/进度（扫描数、提取条数、术语命中、AI 批次）   |           |
| +------------------------------------------------+           |
| AI设置: 供应商[Player2▾] 端点[____] Key[____] 模型[____] [测试] |
+--------------------------------------------------------------+
```
提取完成 → 审阅窗口（DataGrid 对照表 + 确认写入按钮）。

## 5. 验证策略

每个里程碑的验收都以**游戏内参考模组的提取结果为基准**做目录 diff：
- 推荐基准 Mod：`Maiya Trans01 RimTalk边缘世谭`（结构标准、含 Keyed 与 DefInjected）
- diff 工具：直接目录对比（排除 .bak 与时间戳差异）；语义 diff 用小脚本对比 XML 元素集合
- M5 验收：真实调用 Player2 本地服务（用户环境已有）翻译一批 TODO 并人工抽查 10 条

## 6. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| M1 | 解决方案骨架 + Mod 扫描器（4 来源+自定义+版本选择+搜索） | 正确列出 830 工坊 + 36 本地 Mod，About/LoadFolders 解析无误 |
| M2 | Keyed 提取 + 输出三档 + .bak 备份 | 与游戏内提取 Keyed 结果 diff 一致 |
| M3 | Def 继承合并 + DefInjected 生成 | 挑 2 个 Mod 与游戏内提取 diff 一致 |
| M4 | 术语库构建与回填 | 官方译名命中抽查通过 |
| M5 | AI 层（Player2+注册表+规范化+批量翻译） | 测试连接通过 + 翻译一批成功 |
| M6 | 审阅 UI + 确认写入 + 报告 | 全流程跑通一个 Mod 汉化 |
| M7 | 单文件发布 | exe 独立运行验证 |

## 7. V1.1 展望（不阻塞 V1.0）

- **PatchOperation 支持**（见第 8 节）
- 模糊术语匹配、术语库手动增改界面
- 1.7（Waylith）目录布局适配
- 多 Mod 批量队列

## 8. 醒目提醒：PatchOperation 后置

> **V1.0 不解析 PatchOperation。** 使用补丁动态增改文本的 Mod 会漏提取补丁部分（纯 XML+继承估计覆盖约 90% Mod）。
> V1.1 实现 PatchOperationAdd/Replace/Insert；每次发布说明必须声明此限制。
> 每次开发会话开始前，重读本节防止遗忘。

## 9. 约定

- 工具 UI 简体中文；进入游戏的翻译文本禁 emoji、避 `&`（用 and/和 或 `&amp;`）
- 构建前检查 RimWorld 进程（RimWorldWin64.exe / RimWorld.exe），运行中先征询用户
- 写工坊目录必须走覆盖警告 + .bak 流程，不得静默写入
- 代码风格跟随 C# 常规；XML 输出统一 UTF-8 无 BOM、CRLF
