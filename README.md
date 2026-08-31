# QQ Chat Local Reader

QQ Chat Local Reader 是一个面向 Windows QQ NT 的本地聊天记录读取、检索和导出工具，并计划提供图形界面、命令行和本地 STDIO MCP 接口。

[English summary](#english-summary) · [隐私说明](PRIVACY.md) · [安全策略](SECURITY.md)

项目仍处于早期开发阶段，当前版本尚不适合保存唯一副本或处理无法恢复的数据。

## 使用方式

发布包是 Windows x64 自包含程序，不需要另行安装 .NET。安装版会按当前用户安装；便携版解压后直接运行 `qq-chat-local-reader.exe`。首次使用时：

1. 保持受支持版本的 Windows QQ 已登录并运行。
2. 选择账号，点击“读取会话”，明确勾选需要处理的群聊或私聊。
3. 选择最近 7 天、最近 30 天或自定义日期，然后同步；未选择时默认最近 7 个自然日。
4. 同步完成后可搜索、查看前后文，或导出 Markdown、JSON、CSV。

读取 QQ 一致性快照时 Windows 可能弹出管理员确认，但主程序本身保持普通权限。超过 31 天的范围会在同一个任务中自动分批；已完成批次会立即写入本机加密索引。

安装结束时可选择注册 Codex MCP。其他 MCP 客户端可在图形界面点击“复制 MCP 配置”；复制配置本身不会授予聊天访问权，AI 发起同步时仍由本机确认窗口决定拒绝、仅允许一次或信任该注册。EXE、CLI 和 MCP 是同一个程序，不需要另外安装 MCP 服务。

设置中可以关闭启动更新检查、逐项撤销 MCP 信任，也可以清除本地加密索引；清除索引和卸载都不会修改 QQ 原始聊天记录。

## 设计原则

- 数据默认只在本机处理，不上传聊天记录，不包含遥测。
- 只读取 QQ 数据源，不修改原始聊天数据库。
- 本地索引加密保存，密钥由当前 Windows 用户保护。
- AI/MCP 只能访问用户明确授权的账号和会话范围。
- 未选择时间范围时默认读取最近七个自然日。

完整的产品边界、隐私模型和验收标准见 [DESIGN.md](DESIGN.md)。

## 开发状态

仓库目前已完成 QQ 9.9.33 严格适配器、同一次 VSS 快照中的消息与群资料读取、文本/表情/引用/媒体元数据解析、DPAPI + SQLCipher 加密索引、分页搜索与上下文、Markdown/JSON/CSV 安全导出、持久化后台同步任务，以及共用这些能力的简体中文 WPF、中文 CLI 和本地 STDIO MCP。MCP 提供 11 个有界工具并已通过真实协议握手；同步支持 120 秒本地确认，以及按独立 DPAPI 配置选择“拒绝 / 仅本次允许 / 信任此注册并允许”。

`scripts/build-release.ps1` 可生成无需预装 .NET 的 win-x64 便携 ZIP；Inno Setup 脚本提供按当前用户安装、可选官方 Codex MCP 注册和安全卸载。标签发布流水线从固定提交构建 SQLCipher/OpenSSL，生成 ZIP、安装器、SHA-256 清单、第三方许可、原生构建来源说明和 GitHub 构建来源证明。当前仍属于预发布阶段；每个公开版本都必须通过自动化测试、真实受支持 QQ 会话验收、便携版/安装版冒烟测试和隐私扫描。测试版暂不提供 Authenticode 签名，Windows 可能显示未知发布者或 SmartScreen 提示。

## 许可证

Copyright (c) 2026 leaf0329

本项目以 [PolyForm Noncommercial License 1.0.0](LICENSE) 提供源代码，仅允许许可证定义的非商业用途。它是 source-available（源代码可用）软件，不是 OSI 定义的开源软件。

商业使用不在公开许可证授权范围内，需要另行取得版权所有者的商业授权。第三方组件继续适用各自的许可证；详见 [上游许可证调研](docs/research/upstream-licenses.md)。

## English summary

QQ Chat Local Reader is a Windows-only, local-first reader, encrypted index, search, and export tool for the currently signed-in QQ NT client. The same self-contained executable provides a Simplified Chinese WPF interface, CLI commands, and a local STDIO MCP server. Chat data is not uploaded; optional update checks query only this repository's GitHub Release metadata and can be disabled.

The public repository is source-available under PolyForm Noncommercial 1.0.0. Commercial use requires separate permission. This is pre-release software with a strict adapter for the QQ version documented in the repository; unsupported versions fail closed.
