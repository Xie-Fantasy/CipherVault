# `.cvault` 文件格式 v1

所有整数均为小端序。写入实现位于 `EncryptedVaultFileManager`。

| 顺序 | 大小 | 内容 |
| --- | ---: | --- |
| 1 | 8 | Magic：ASCII `CVAULT` + `01 00` |
| 2 | 4 | 文件版本，当前为 1 |
| 3 | 32 | Argon2id salt |
| 4 | 4 | Argon2 内存 KiB |
| 5 | 4 | Argon2 迭代次数 |
| 6 | 4 | Argon2 并行度 |
| 7 | 4 | 失败次数 |
| 8 | 4 | 锁定截止 Unix 秒，0 表示未锁定 |
| 9 | 4 | 头校验长度，固定 32 |
| 10 | 32 | 对前 64 字节计算的 SHA-256 |
| 11 | 4 | 恢复块长度，v1 为 0 或 60 |
| 12 | 变长 | 恢复块：nonce(12) + encrypted vault key(32) + tag(16) |
| 13 | 8 | 加密 payload 总长度 |
| 14 | 变长 | payload：nonce(12) + ciphertext + tag(16) |

payload 明文是完整 SQLite 数据库。读取器拒绝错误 magic/版本、异常 KDF 参数、错误固定长度、超过 512 MiB 的 payload、截断内容和尾随数据。GCM tag 验证 payload 与恢复块的真实性；头 SHA-256 只用于损坏检测，不是带密钥的认证。

保存先在目标目录创建随机临时文件，`WriteThrough` 写入并 `Flush(true)`，然后使用文件替换提交，从而避免进程中断留下半写目标文件。
