# 隐私说明 / Privacy

QQ Chat Local Reader 的聊天读取、解析、索引、搜索和导出均在当前 Windows 设备本地完成。项目不包含遥测、广告、使用统计或自动崩溃上传，也不会上传 QQ 数据库、聊天内容、账号、索引密钥、附件路径或设备标识。

默认联网行为仅为向固定的 GitHub Releases API 查询本项目最新版本。请求使用固定产品名称和版本作为 User-Agent，不生成唯一安装 ID。用户可在“设置”中关闭更新检查；关闭后程序不会主动联网。

本地索引使用独立 SQLCipher 密钥加密，密钥及 MCP 信任配置由 Windows 当前用户的 DPAPI 保护并限制目录访问。用户主动导出的 Markdown、JSON 和 CSV 是普通明文文件，不再受索引加密保护。

原始 QQ 数据库保持只读。同步时程序按需创建短生命周期一致性快照，完成或失败后清理；只有受限快照助手可能请求管理员权限，主界面、CLI 和 MCP 保持普通权限。

这些保护不能抵御已经控制当前 Windows 用户、管理员权限或读取器进程的恶意软件，也不承诺 SSD、文件系统或备份介质上的物理不可恢复删除。

---

QQ Chat Local Reader processes chat databases, indexes, searches, and exports locally. It includes no telemetry or automatic crash upload. Its only default network action is a non-identifying check of this project's GitHub Releases metadata, which can be disabled in Settings. User-created exports are plaintext and are no longer protected by the encrypted local index.
