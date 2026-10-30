# CSV 导入与导出

导入器支持 UTF-8（含 BOM）、RFC 4180 风格双引号、字段内逗号和字段内换行。文件上限为 50 MiB。

自动识别常见列名：

| CipherVault | 常见列名 |
| --- | --- |
| 标题 | `name`, `title` |
| 用户名 | `username`, `login_username` |
| 密码 | `password`, `login_password` |
| URL | `url`, `login_uri` |
| 备注 | `note`, `notes`, `extra` |

导入预览的行数由同一个 CSV 解析器计算，因此带内嵌换行的记录不会被重复计数。密码在写入仓库前立即进行字段级加密。

CSV 导出包含 `Title,Type,Username,Password,URL,Notes,Tags,IsFavorite`，密码为明文。导出采用流式写入和原子替换，避免把所有密码集中在单个大字符串中。CSV 仅用于迁移，不是安全备份格式。
