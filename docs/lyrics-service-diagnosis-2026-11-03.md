# 歌词服务不可用诊断（2026-11-03）

## 结论

本次故障的直接原因是 LRCLIB 对部分歌词查询返回 HTTP 503，错误名为 `ServerOverloaded`。Windows 经系统代理、Windows 禁用代理直连，以及 WSL 的独立请求都收到相同错误，因此本次失败不能归因于系统代理。蓝牙与歌曲读取正常。

故障具有暂时性和查询差异：同一时段部分查询成功，部分查询失败，不能据此认定 LRCLIB 全站不可用。客户端无法确认服务端内部负载或具体查询失败的内部原因。

诊断期间程序随后成功加载《Señorita》的 57 行同步歌词；切换到《Smells Like Me》后成功加载 53 行同步歌词。软件保持运行，iPhone 的 Apple Music 连接正常。

## 复现与实测

使用 `python3 desktop/run.py` 启动真实 Windows WPF 软件，日志出现：

```text
AMS: Connected r1 iPhone 已连接，等待播放信息。
AMS: Snapshot r1 正在播放
LYRICS_ERROR: 歌词服务暂时不可用。
```

窗口显示当前歌曲《Señorita》，歌手 `Shawn Mendes & Camila Cabello`，并提示歌词服务不可用及自动重试。设备标识未保存。

连续两次执行现有网络检查：

```sh
python3 desktop/run.py --check-lyrics
```

两次均先成功收到《夜的第七章》的 13 条同步歌词候选，随后在视频标题的查询路径抛出“歌词服务暂时不可用”，退出码为 1。

使用 Windows .NET Framework `HttpClient`、TLS 1.2、`LyricConnector/0.1` User-Agent，对请求逐个验证：

| 查询 | HTTP 状态 | 结果 |
| --- | --- | --- |
| `/api/search`：`Señorita` + `Shawn Mendes & Camila Cabello` | 503 | `ServerOverloaded` |
| `/api/search`：仅 `Señorita` | 200 | 20 条候选，包含 Shawn Mendes / Camila Cabello 的同步歌词 |
| `/api/search`：`夜的第七章` + `Jay Chou` | 200 | 13 条候选 |
| `/api/search`：`止戰之殤` + `周杰倫` | 503 | `ServerOverloaded` |
| `/api/get`：`Señorita` + 双歌手 + `duration=192`，未带专辑 | 503 | `ServerOverloaded` |

最后一项是用于缩小问题的请求，不声称与软件当时发送的完整精确查询完全一致。

503 的响应为：

```json
{"message":"The server is busy, please retry in a moment","name":"ServerOverloaded","statusCode":503}
```

响应还包含 `Retry-After: 1`。Windows 代理与直连的同一“歌名＋歌手”搜索分别约 1.32 秒、1.48 秒返回 503；WSL 同一搜索也返回 503。它们是明确的 HTTP 错误响应，而非本次客户端 20 秒超时。

[LRCLIB 官方错误说明](https://lrclib.net/docs#errors)将 503 / `ServerOverloaded` 定义为服务暂时繁忙，要求按 `Retry-After` 等待后重试；429 则是另列的请求限流错误。本次捕获的是 503，不能将其直接判定为客户端触发 429 限流。

## 客户端为何显示统一的不可用提示

- `desktop/LyricsStore.cs` 的 `GetAsync` 将 429 和所有 5xx 转换为同一个 `HttpRequestException("歌词服务暂时不可用。")`，丢失了状态码、响应错误名及重试头，因此现有日志无法直接区分限流和服务端繁忙。
- `SearchAsync` 仅在“歌名＋歌手”查询成功但结果为空时，继续执行仅歌名查询。首次请求抛出 503 时，整次操作立即中止，即使仅歌名的请求当时可用也不会执行。
- `desktop/Windows.cs` 已提供 10、20、40 秒的三次自动重试；未读取服务端的 `Retry-After`。本次 `Retry-After` 为 1 秒，现有等待时间更长，但不能保证服务在下次请求时已经恢复。

## 验证与范围

`python3 desktop/run.py --test`：85 项检查全部通过。没有运行前端生产构建。本次仅诊断，没有修改业务代码，没有新增自动降级行为。

计划验证界面“清空歌手、仅搜歌名”时，检测到手机已切换歌曲，因此未修改检索框、未提交搜索，也未手动替换歌词；仅歌名路径的可用性已通过独立 HTTP 请求验证。随后读取真实窗口，显示《Smells Like Me》、Charlie Puth、`LRCLIB · 53 行同步歌词`。

后续若改进客户端，优先保留 HTTP 状态、服务端错误类型与 `Retry-After`，让日志能区分服务端繁忙与限流。是否在服务错误后扩展搜索应作为明确的产品行为决定，避免无条件扩大请求量。外部服务的间歇故障无法仅通过本地测试保证永久消除。
