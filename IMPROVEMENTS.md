# Godot MCP 改进与执行清单

> 说明：本文件保存本轮审查得到的全部改进建议，并区分“本轮立即执行”和“后续架构演进”两类事项。
> 本轮目标是把当前仓库中能够闭环实现的高优先级改进全部落地，并在完成后更新状态。

## 本轮立即执行（当前仓库内闭环）

- [x] 修复截图工具契约不一致（描述、返回类型、实际内容对齐）
- [x] 隐藏或移除当前不可用的 `editor_execute_csharp` 工具暴露
- [x] 让 `editor_get_errors` 真正接入错误/告警采集
- [x] 为高价值工具增加 `structuredContent` 与 `outputSchema`
- [x] 增加资源路径安全校验，限制到 `res://` / `user://`
- [x] 新增一批基础 MCP `resources`
- [x] 新增一批基础 MCP `prompts`
- [x] 更新 README 与文档，明确行为边界与限制
- [x] 重新构建并验证当前修改

## 后续架构演进（记录建议，非本轮闭环范围）

### 连接与可靠性

- [x] 为 WebSocket 桥增加心跳 / 探活机制
- [x] 将固定重试改为指数退避 + jitter
- [x] 增加大响应分块、分页或更细粒度查询接口
- [x] 为长操作增加进度、取消或任务化机制

### 真正的运行时能力

- [x] 引入独立的 runtime bridge，而不是仅依赖 editor 进程视角
- [x] 支持真正的 live scene tree / live node properties / runtime logs
- [x] 支持持续采样式的 property monitor，而不是单次 sample

### MCP 能力面扩展

- [x] 增强 resources 的动态更新与订阅能力
- [x] 增加 prompts/list_changed、resources/list_changed 等通知能力
- [x] 为更多工具补充结构化输出和更强的元数据注解

### 工程质量

- [x] 增加 TypeScript 单测
- [x] 增加 fake Godot WebSocket server 协议测试
- [x] 增加 Godot 侧 smoke / 集成验证入口

## 2026-04-12 复审补充（保留当前轮询方案）

> 说明：基于本轮复审后的进一步确认，**暂不推进**“将 `resources` / `prompts` 的轮询改为事件驱动 + 脏标记”的改造。
> 当前 `list_changed` / `resources/updated` 动态通知能力已经具备，现阶段优先把精力投入到更直接提升可用性、可维护性与 AI 工作流质量的事项上。

### 明确排除项

- [x] 本阶段明确保持 `resources.ts` / `prompts.ts` 现有轮询方案，不改为事件驱动 + 脏标记

### P0：应优先落地的高价值事项

- [x] 为 `editor_get_errors` 增加结构化编译错误 / 警告采集，并单独暴露 `editor_get_compilation_errors`
- [x] 让 `node_get_properties` 与 `runtime_get_node_properties` 保留值类型、原始值和展示值，避免全部字符串化
- [x] 统一错误返回模型，至少包含 `code` / `message` / `context` / `retriable`
- [x] 增加 `runtime_wait_until_ready`，降低 `scene_play` 后 runtime bridge 尚未可用时的竞态问题
- [x] 为 MCP Server 工具层补单测，优先覆盖 `project` / `scene` / `node` / `runtime` 的边界与异常路径

### P1：显著提升 AI 工作流质量的能力

- [x] 新增 `node_inspect_deep`，一次返回节点属性、信号、脚本、子节点、group / owner 等高价值上下文
- [x] 新增 `scene_apply_operations` 或 `node_batch_update`，支持一组编辑操作的原子提交 / 回滚
- [x] 为大结果补全分页 / 游标机制，优先覆盖 `project_list_files`、`scene_get_tree`、`runtime_get_scene_tree`、`runtime_get_node_properties`
- [x] 增加 `project_search_text` / `script_find_references`，减少 AI 在项目内定位文本与引用时的往返开销
- [x] 增加 `asset_get_dependencies` / `asset_find_unused`，补强资源治理与清理能力

### P2：运行时调试与自动化测试增强

- [x] 新增 `runtime_watch_signal`，便于观察 UI 交互、状态机和场景切换期间的真实运行时行为
- [x] 新增 `runtime_watch_node_lifecycle`，跟踪节点进入树、退出树、释放、重建等生命周期事件
- [x] 评估只读型 `runtime_evaluate_gdscript`，优先支持安全表达式求值而非任意写操作
- [x] 增加 `input_record_macro` / `input_playback_macro`，把手工操作沉淀为可复用的自动化回放步骤
- [x] 将输入模拟与游戏截图逐步迁移到 runtime bridge，降低当前对 Win32 的耦合并改善跨平台可扩展性

### 推荐实施顺序

1. 先做结构化编译错误、强类型属性输出、统一错误码与 `runtime_wait_until_ready`
2. 再做 `node_inspect_deep`、批量/事务式编辑和分页能力
3. 最后推进 runtime watch、宏录制回放、只读运行时求值与资源治理工具

### 备注

- 当前轮询方案不是架构阻塞项；相比之下，**类型信息缺失、错误模型不统一、缺少事务式工作流工具** 更直接影响 AI 的稳定使用体验。
- 若后续实际运行中确认轮询带来了明显性能问题，再单独回头评估事件驱动改造也不迟。
- 当前已补充 transport / error 基础测试，以及 `project` / `scene` / `node` / `runtime` 的关键成功 / 异常 / 超时路径测试；后续仍可继续扩展更细粒度覆盖率。

## 实施记录

- 2026-04-12：创建本文件，开始执行“本轮立即执行”列表。
- 2026-04-12：完成截图契约修正，截图工具现返回实际图像内容，并附带结构化 capture metadata。
- 2026-04-12：移除 MCP Server 对 `editor_execute_csharp` 的工具暴露，避免 AI 调用不可用能力。
- 2026-04-12：为 MCP/Godot 插件桥接层接入共享错误日志；`editor_get_errors` 现可返回真实错误、告警和关键事件。
- 2026-04-12：为高价值工具补充 `outputSchema` / `structuredContent`，提升客户端与模型对结果的可解析性。
- 2026-04-12：为项目文件、脚本、场景等路径增加 `res://` / `user://` 安全边界校验，并修复根路径拼接不稳定问题。
- 2026-04-12：新增基础 `resources` 与 `prompts`，让仓库不再只暴露 tools。
- 2026-04-12：已更新 `README.md` 与根目录 `.env` 占位配置；`mcp-server` 已重新构建通过，静态错误检查通过。
- 2026-04-12：新增基于 `EditorDebuggerPlugin + autoload + EngineDebugger` 的真正 runtime bridge，`runtime_*` 工具现可直接访问运行中游戏进程。
- 2026-04-12：为 Node ↔ Godot WebSocket 桥增加心跳、指数退避 + jitter、可配置重试参数，以及更稳健的请求超时控制。
- 2026-04-12：为运行时能力补充分页/细粒度查询（scene subtree、property filter、offset/limit）以及 task-capable 的 `runtime_monitor_property`。
- 2026-04-12：新增运行时资源、资源模板、动态 `resources/list_changed` / `prompts/list_changed` / `resources/updated` 通知，以及 `analyze_runtime_scene` prompt。
- 2026-04-12：新增 `runtime_run_smoke_check` 作为 Godot 侧可执行 smoke / 集成验证入口。
- 2026-04-12：新增 TypeScript 单测与 fake Godot WebSocket server 协议测试；`npm test` 通过。
- 2026-04-12：新增 `server.test.ts`，补齐 `project` / `scene` / `node` / `runtime` 工具层的成功、错误、变换与超时参数测试。
- 2026-04-12：完成结构化编译诊断、强类型属性返回、统一错误模型、`runtime_wait_until_ready`、`node_inspect_deep`、`node_batch_update`、`project_search_text`、`script_find_references`、`asset_get_dependencies`、`asset_find_unused`、`runtime_watch_signal`、`runtime_watch_node_lifecycle`、`runtime_evaluate_gdscript`、宏录制/回放，以及输入/游戏截图迁移到 runtime bridge。
- 2026-04-12：二次校准 `README.md` / `INSTALL.md`，同步 64 个 tools、10 个固定 resources、2 个 runtime resource templates、3 个 prompts，以及 runtime bridge 的跨平台行为说明。
