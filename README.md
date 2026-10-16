# 边缘译者 RimTranslator

> RimWorld 离线翻译提取工具——不进游戏，图形界面选 Mod，一键提取 → 术语回填 → AI 翻译 → 人工审阅 → 写入。
> 完全免费 · 开源 · 本地运行

## 这是什么

复刻创意工坊 Mod「[Extract translation](https://steamcommunity.com/sharedfiles/filedetails/?id=3026452122)」核心功能的 **Windows 桌面工具**，并在此基础上扩展了术语库与 AI 翻译流水线：

- **离线提取**：不启动游戏，扫描创意工坊 / 本地 / 自定义目录的 Mod，提取 Keyed 与 DefInjected 两类待翻译文本（含 XML 继承合并）
- **术语库回填**：自动读取游戏官方翻译（本体 + 全部 DLC）构建 EN→译文 库，命中的条目直接回填官方译名，不消耗 AI 额度
- **自定义词库**：玩家可增删改自己的词条；与官方译名不一致时，添加同名词条即可覆盖（翻译时自定义优先）
- **AI 翻译**：Player2 默认置顶（免密钥），另支持 Gemini / OpenAI / DeepSeek / Grok / GLM / OpenRouter / SiliconFlow / 本地模型 / 自定义端点
- **人工审阅**：AI 译完弹出对照表（原文 / 术语命中 / 译文可编辑），确认后才写盘
- **安全写盘**：覆盖任何文件前自动生成 `.bak` 备份；写创意工坊目录前弹覆盖警告
- **三档输出**：原 Mod 目录 / 自定义目录 / 生成独立汉化 Mod（自动生成 About.xml、LoadFolders.xml，可直接发布）

## 界面语言与隐私

- 工具界面为简体中文（多语言界面在计划中）
- **你的 API 密钥只保存在你自己电脑的 `%APPDATA%\RimTrans\settings.json`，工具不内置、不上传、不回传任何遥测——开发者也无法读取**。AI 请求直接从本机发往你自己填写的供应商端点

## 致谢

- **[Extract translation](https://steamcommunity.com/sharedfiles/filedetails/?id=3026452122)（作者 HaiLuan）**——本工具的提取行为（ExtractKey / DefInjected 生成 / 注释格式 / UNUSED 归并等）以其模组为蓝本对齐实现，感谢原作者的开拓
- RimWorld 本体源码中的 `Verse.TranslationFilesCleaner` / `XmlInheritance` 等逻辑作为字段遍历与继承合并的规则依据
- Player2 本地服务集成参考 RimTuber 的实现

## 已知限制（V1.0）

- 不解析 PatchOperation 补丁动态产生的文本（约覆盖 90% 的 Mod，V1.1 计划支持 Add/Replace/Insert）
- 跨 Mod 继承（ParentName 指向其他 Mod 的抽象节点）的 Def 暂无法解析（V1.1 计划支持依赖链解析）
- 代码动态生成的 Def 离线抓不到（离线方案固有边界）

## 支持

- GitHub：https://github.com/maiya026/rimtranslator（开源仓库，Issue 反馈）
- QQ 群：**1568455**（使用帮助、汉化交流、最新消息）
- 网盘下载：（发布时公布）
- 爱发电：https://afdian.com/a/maiya0126 —— 如果工具帮到了你，欢迎请作者喝杯奶茶

## 许可

开源（许可证随仓库发布确定），基于 Extract translation 原作者的友好授权共识。
