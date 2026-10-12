# MKV 按需提取 UI 与云盘进度实施方案

状态：已实施  
前置：`docs/mkv-embedded-text-subtitle-plan.md` Phase 0–3 已完成（probe / extract 服务已有，未暴露 HTTP）  
范围：列表徽章旁按需列轨、用户确认后再抽轨翻译；云盘大文件抽轨显示进度  
非目标：列表全量 ffprobe、图像轨 OCR、回封装 MKV、MP4 `mov_text`

---

## 1. 背景

第一期只把内嵌文本轨接到**自动翻译 / 批量**。列表徽章仍然只扫 sidecar：

- 电影：`GET /api/media/movies` → `SubtitleService.GetAllSubtitles(movie.Path)`（只认 `.srt/.ssa/.ass`）
- 剧集：展开季时 `POST /api/subtitle/all`
- 点徽章：`POST /api/translate/file`，`TranslationJob` **只读 sidecar**，不能读 `.mkv`

因此没有外挂时，列表是空的，用户无法对某一集「先看里面有什么字幕，再决定翻」。

云盘（rclone / CloudDrive 等 FUSE 挂载）上抽轨几乎要扫完整 MKV，等于把整部 Remux 拉一遍。现有抽轨超时写死 2 分钟，失败后当作「没有内嵌」，用户看不到任何进度。

本方案对应第一期文档 **§9 二期「列表显示内嵌徽章」** 的更稳实现：不在列表加载时探测，只按需探测单个文件。

---

## 2. 产品约定

### 2.1 交互

```
字幕列（语言徽章旁边）
  → 「提取」按钮（每行都显示，不预检是不是 MKV）
  → 弹层：对这一个文件跑 ffprobe，列出可提取文本轨
  → 用户选轨 + 目标语言
  → 立刻出现翻译任务（复用 Translations 页进度条）
  → 后台：抽轨（云盘可能很久）→ 现有翻译管线 → 列表多出语言徽章
```

点按钮**只探测，不抽轨**。抽轨会完整读取视频；用户可能只是看看有哪些轨。

弹层底部固定提示：

> 提取会完整读取视频文件。文件在云盘上时可能很慢。

### 2.2 做与不做

| 做 | 不做 |
|---|---|
| 徽章旁按需按钮 + 轨信息弹层 | 列表 / 统计 / 季目录 collect 里 ffprobe |
| 用现有 `ffprobe`（mkvinfo 同类） | 再装 mkvtoolnix |
| 先建 `TranslationRequest` 再抽轨 | 把 `.mkv` 交给现有 `translate/file` |
| 抽轨进度走现有 SignalR `RequestProgress` | 新造一套 SSE |
| 手动可选 `und` / `unknown` 并指定源语言 | 自动任务翻译未知语言（第一期规则不变） |
| 文本轨 srt / ass / ssa | PGS / VobSub / DVB / WebVTT |

### 2.3 按钮可见性

- 电影 `MoviePage.vue`、剧集 `EpisodeTable.vue` 字幕列，徽章右侧
- **每行都显示**，不在列表加载时 `File.Exists`（云盘元数据未缓存时同样会慢）
- 电影列现在无 sidecar 时手机端 `hidden md:flex`，提取按钮必须单独保证可见
- 点下去找不到 MKV / 没有文本轨：弹层说明原因，不进翻译

---

## 3. 核心策略

不要让 `TranslationJob` 直接读 `.mkv`（第一期约定保留）。抽出后扩展名仍是 `.srt` / `.ass`，解析、翻译、校验、校对、命名全部复用。

```
定位 {Path}/{FileName}.mkv
  → 按需 ffprobe 列文本轨（弹层，可取消）
  → 用户选轨 + 目标语言
  → CreateRequest（Pending，此时还没有 sidecar 路径）
  → Hangfire：ffmpeg 抽轨（报进度）→ 写入 SubtitleToTranslate
  → 现有 TranslationJob 翻译逻辑 → {stem}.{targetLang}.srt
```

探测用现有 `IEmbeddedSubtitleService.ProbeAsync`。  
抽轨用现有 `ExtractAsync`，但必须：可取消、可配置超时、能报进度。

---

## 4. API

均挂在现有 `SubtitleController`（`/api/subtitle`）。请求用 `mediaId + mediaType`，**不要让前端传文件系统路径**。

### 4.1 按需探测

`POST /api/subtitle/probe`

```json
{ "mediaId": 12, "mediaType": "Movie" }
```

`mediaType`：`Movie` | `Episode`（与现有 `MediaType` 一致）。

成功：

```json
{
  "found": true,
  "fileName": "Dune.2021.mkv",
  "tracks": [
    {
      "index": 3,
      "language": "en",
      "codecName": "subrip",
      "extension": ".srt",
      "title": "English",
      "caption": "",
      "isDefault": true,
      "isForced": false,
      "isHearingImpaired": false,
      "canAutoTranslate": true,
      "sidecarExists": false,
      "extractable": true
    }
  ],
  "skipped": [
    { "index": 4, "codecName": "hdmv_pgs_subtitle", "reason": "image" }
  ]
}
```

失败仍返回 200，用字段区分，避免前端当异常：

| 情况 | `found` | 其它 |
|---|---|---|
| 没有 MKV | `false` | `reason: "no_mkv"` |
| 缺 ffprobe | `false` | `reason: "no_ffprobe"` |
| 超时 | `false` | `reason: "timeout"` |
| 无可提取文本轨 | `true` | `tracks: []` |

HTTP 超时建议 60s（比 probe 默认 30s 略宽）。前端弹层可取消 → 取消 token。

**禁止**把 probe 并进 `GetMovies` / `GetAllSubtitles` / `StatisticsJob`。

### 4.2 提取并翻译

`POST /api/subtitle/extract-and-translate`

```json
{
  "mediaId": 12,
  "mediaType": "Movie",
  "trackIndex": 3,
  "sourceLanguage": "en",
  "targetLanguage": "zh"
}
```

`sourceLanguage` 在轨语言为 `und` / `unknown` 时由用户指定，否则用轨上的语言。

行为：

1. 再 `FindMkv` + 校验该 `trackIndex` 仍是可提取文本轨（防 TOCTOU）
2. 同语言 + 同 caption 已有非空 sidecar → 直接 `CreateRequest(sidecar 路径)`，不再抽
3. 否则 `CreateRequest` 入队（`SubtitleToTranslate` 可暂空或写占位），**立即返回 `{ jobId }`**
4. Hangfire 里抽轨，成功后再走现有翻译

不要在 HTTP 线程里 `ExtractAsync`。批量 `POST /api/translate/bulk` 现在同步抽轨，云盘会像页面卡死；本期手动入口先改对，bulk 顺手挪进任务后再说（见 §8.3）。

取消：复用 `POST /api/TranslationRequest/cancel`，必须能杀掉正在跑的 ffmpeg。

---

## 5. 进度（云盘）

翻译页已有 `TranslationProgress.vue` + SignalR `RequestProgress`。缺口是抽轨发生在 **CreateRequest 之前**，进度条无处可挂。

### 5.1 阶段

| 阶段 | 进度来源 | UI |
|---|---|---|
| 弹层探测 | 无百分比 | 转圈 +「正在读取字幕轨」 |
| 抽轨 | ffmpeg `out_time / duration` → 0–100，映射到请求进度的 **0–20** | Translations 页进度条；可选文案 `Extracting` |
| 翻译 | 现有按行百分比，映射到 **20–100** | 现有行为 |

没有 `duration` 时抽轨用不确定进度（条在动、百分比可空）。前端 `TranslationPage` 现在是 `status===INPROGRESS && item.progress`，**0 不显示**；抽轨开始就要 `Emit(..., 1)` 或放宽条件让 0 也出条。

### 5.2 进程封装

`ExternalProcessRunner.RunAsync` 目前 `ReadToEndAsync` 整段读完，无法边跑边报。

抽轨专用路径（可新方法，不必改所有调用方）：

```text
ffmpeg -y -i {mkv} -map 0:{index} -c:s copy
  -progress pipe:1 -nostats
  {sidecar}
```

流式读 `pipe:1` 的 `out_time_us=` / `progress=end`。  
`duration` 在 probe 时顺带取 `format=duration`（只读头，可接受），或抽轨前一次短 ffprobe。

取消：`cancellationToken` → `Kill(entireProcessTree)`，删未完成 sidecar（`ExtractAsync` 已有 `TryDeleteIncomplete`）。

### 5.3 超时

写死在 `EmbeddedSubtitleService`：

```csharp
ProbeTimeout   = 30s
ExtractTimeout = 2min
```

本期：

| 项 | 建议 |
|---|---|
| probe | 保持 30s，或设置项默认 30s、上限 120s |
| extract | 可配置，默认 **60min**；云盘 Remux 仍可能不够，取消优先于再加长 |
| Hangfire | `JOB_TIMEOUT_MINUTES` 默认 30，抽轨必须低于它，或抽轨期间滑动续命 |
| `WebhookJob` `[DisableConcurrentExecution(2*60)]` | **不要**让 webhook 同步抽云盘文件；手动入口走 translation 队列 |

超时 / 失败：请求标 `Failed`，写清楚 `ErrorMessage`（超时 / 缺 ffmpeg / 空文件），不要再静默当成「没有内嵌」。手动任务失败应可见；自动任务仍可跳过以免堵住队列。

### 5.4 SignalR 载荷

现有 `Emit` 匿名对象：`Id, JobId, CompletedAt, ErrorMessage, StackTrace, Status, Progress`。

可选加 `phase: "extract" | "translate"`（前端不认识就忽略）。不强制改库：`TranslationRequest` 仍然没有 Progress 字段，刷新后进行中任务百分比仍靠下一次 SignalR（与现在翻译进度相同）。

---

## 6. 前端

### 6.1 列表按钮

- `Lingarr.Client/src/pages/MoviePage.vue` 字幕列 `flex-wrap` 末尾
- `Lingarr.Client/src/components/features/show/EpisodeTable.vue` 徽章旁
- 样式靠近 `BadgeComponent`，文案 `MKV` 或图标按钮，tooltip「提取内嵌字幕」
- 无 sidecar 时电影列也要显示（修 `hidden md:flex`）

### 6.2 弹层

仓库几乎没有通用 modal（设置里的目录选择除外），新建例如：

`Lingarr.Client/src/components/features/subtitle/ExtractEmbeddedModal.vue`

状态：

1. loading：取消按钮
2. 空 / 失败：原因文案
3. 列表：每轨一行（语言、codec、title、forced/sdh、已有外挂）
4. 图像轨放在「无法提取」分组，灰色
5. 选中轨后：目标语言列表复用 `target_languages`（与 `ContextMenu` 相同）
6. `und` / `unknown`：额外源语言选择
7. 主按钮「翻译」→ `extract-and-translate` → 关弹层；`NAVIGATE_TO_DETAILS_ON_REQUEST` 时跳详情（与点徽章一致）

抽出完成后刷新该行徽章：电影重拉 `/api/media/movies` 对应项或整页；剧集重拉 `/api/subtitle/all`。不等抽完也可以靠 Translations 页看进度。

### 6.3 服务

`Lingarr.Client/src/services/subtitleService.ts` 增加 `probe` / `extractAndTranslate`。  
类型放 `Lingarr.Client/src/ts/subtitle.ts`（已有 `isEmbedded?`、`trackIndex?`，列表仍未用）。

---

## 7. 后端改动要点

### 7.1 已有、直接用

- `IMediaFileLocator` / `MediaFileLocator`
- `IEmbeddedSubtitleService.ProbeAsync` / `ExtractAsync`
- `EmbeddedSubtitleMerger`（同语言+caption sidecar 优先）
- Hangfire `TranslationJob`、`IProgressService`、`TranslationRequestsHub`

### 7.2 要改

| 文件 | 动作 |
|---|---|
| `SubtitleController.cs` | `probe`、`extract-and-translate` |
| 新 API 模型 | `ProbeEmbeddedRequest` 等 |
| `EmbeddedSubtitleService` | probe 带 duration；extract 超时可配置；传入 progress callback |
| `IExternalProcessRunner` | 流式进度重载（仅 ffmpeg 抽轨用） |
| `TranslationRequestService` | 允许「先入队、抽完再填 SubtitleToTranslate」 |
| `TranslationJob` | 开头若路径空/是 MKV 占位：先 extract，再 `ReadSubtitles` |
| `ProgressService.Emit` | 可选 `phase` |
| 设置 | `extract_timeout_minutes`（可选）；`extract_embedded_source` 后端仍未接线，本期可不碰 |

`TranslationJob` 改动要小：只在 Execute 开头补「确保 sidecar」，后面解析/翻译不动。不要把 MKV 交给 `SrtParser`。

占位方案二选一（实现时定一个）：

**A（推荐）** 请求里 `SubtitleToTranslate` 先写 `embedded:{mkvPath}:{trackIndex}`，Job 识别此前缀再抽，抽完改成真实 sidecar 路径并 Save。  
**B** 新字段 `EmbeddedTrackIndex`（要迁移），Job 看字段决定是否先抽。

A 无迁移，第一期 hash 已用 `embedded:index:codec:lang:` 形式，风格一致。

### 7.3 手动 vs 自动

| | 自动 / bulk | 本期手动 |
|---|---|---|
| 开关 `scan_embedded_subtitles` | 关则不探 | 按钮不看这个开关 |
| `und` / `unknown` | 跳过 | 用户指定源语言 |
| 抽轨失败 | 记日志，当没内嵌 | 请求 `Failed`，界面可见 |
| 超时 | 2min 写死（可后续统一） | 可配置长超时 + 进度 |

---

## 8. 分阶段

### Phase A — 按需探测 UI（约 1.5 天）

不抽轨、不改 Job。验证「点按钮能看到轨」。

- [ ] `POST /api/subtitle/probe`（mediaId + mediaType → locator → ProbeAsync）
- [ ] 前端服务 + 类型
- [ ] 电影 / 剧集字幕列按钮（无 sidecar 也可见）
- [ ] `ExtractEmbeddedModal`：loading / 空 / 轨列表
- [ ] 单测：无 MKV、超时、过滤 PGS、语言映射
- [ ] **不**在 `GetMovies` / `GetAllSubtitles` 调 probe

### Phase B — 抽出后翻译（约 1 天）

本地磁盘即可验收；云盘进度下一阶段。

- [ ] `POST /api/subtitle/extract-and-translate` 立即返回 jobId
- [ ] CreateRequest 占位 → Job 开头 extract → 填 sidecar → 现有翻译
- [ ] 已有 sidecar 则跳过 extract
- [ ] `und` 允许用户指定源语言
- [ ] 取消能杀掉 ffmpeg
- [ ] 抽出后刷新该行徽章
- [ ] 单测：占位解析、不覆盖 sidecar、取消删半成品

本阶段 extract 仍可暂时 2min 超时（本地够用），但必须走 Hangfire，不能在 HTTP 里抽。

### Phase C — 云盘进度与超时（约 2 天）

- [ ] runner 流式读 `-progress pipe:1`
- [ ] 抽轨 0–20% + 翻译 20–100%，开始即 `Emit` 让条出现
- [ ] extract 超时可配置，默认 60min；对齐 `JOB_TIMEOUT_MINUTES`
- [ ] 失败信息写到请求，不再静默
- [ ] 弹层探测可取消；抽轨取消复用翻译取消
- [ ] 文档：云盘会读完整视频、建议 `JOB_TIMEOUT_MINUTES` ≥ extract 超时

### Phase D — 验收

- [ ] 本地：无 sidecar 的英文字幕 MKV → 按钮 → 列轨 → 译中 → 出现 `Movie.en.srt` + `Movie.zh.srt`，视频未改
- [ ] 已有 `en.srt` 时弹层标「已有外挂」，翻译不再抽
- [ ] 只有 PGS：可列出但不可提取
- [ ] 云盘（可选）：进度条在抽轨阶段会动，取消后 ffmpeg 退出、无半成品 sidecar

---

## 9. 文件清单

| 文件 | 动作 |
|---|---|
| `Lingarr.Server/Controllers/SubtitleController.cs` | probe / extract-and-translate |
| `Lingarr.Server/Models/Api/` | 请求 / 响应 DTO |
| `Lingarr.Server/Services/Media/EmbeddedSubtitleService.cs` | duration、超时、progress |
| `Lingarr.Server/Services/ExternalProcessRunner.cs` | 流式进度 |
| `Lingarr.Server/Jobs/TranslationJob.cs` | 开头确保 sidecar |
| `Lingarr.Server/Services/TranslationRequestService.cs` | 占位入队 |
| `Lingarr.Server/Services/ProgressService.cs` | 可选 phase |
| `Lingarr.Client/src/pages/MoviePage.vue` | 按钮 |
| `Lingarr.Client/src/components/features/show/EpisodeTable.vue` | 按钮 |
| `Lingarr.Client/src/components/features/subtitle/ExtractEmbeddedModal.vue` | 新建弹层 |
| `Lingarr.Client/src/services/subtitleService.ts` | 两个新方法 |
| `Lingarr.Client/src/ts/subtitle.ts` | 轨类型 |
| `Lingarr.Client/src/pages/TranslationPage.vue` | 抽轨阶段也显示进度条 |
| `Lingarr.Server.Tests` | probe API、Job 先抽再译、取消 |
| `Lingarr.Docs` / `Readme.MD` | 手动提取、云盘耗时 |

翻译插件、Radarr/Sonarr 同步、实体 `Movie.Path` 仍不改为完整视频路径。

---

## 10. 风险与对策

1. **列表性能**：探测只挂按需 API。按钮一律显示，用失败文案代替预检。
2. **Matroska 索引在文件尾**：列轨通常只读 Tracks（文件头），多数云盘可接受；极端 seek 差会顶满 30s，弹层 loading 即可。
3. **抽轨等于读完整文件**：进度解决「别当死机」，解决不了「拉几十 GB」。弹层必须写明。
4. **Hangfire 不可见超时**：SQLite `JOB_TIMEOUT_MINUTES` 默认 30，worker 被当成死掉会重领。抽轨要续命或调高，且 `AutomaticRetry(Attempts = 0)` 避免抽一半重来（半成品已删）。
5. **Webhook 2min 锁**：手动入口不要走 `WebhookJob`。
6. **路径空格**：继续 `ArgumentList`，禁止拼 shell。
7. **语言 `und`**：手动让用户选；自动仍跳过。
8. **Plex 不马上出现外挂**：本机一般会扫到；网盘库可能要手动扫描资料库（第一期文档已写）。

---

## 11. 明确不在本期

- 同步时预探测并缓存轨
- 完整视频路径入库
- 列表每行自动「内嵌」徽章（本方案用按钮代替，避免列表 I/O）
- MP4 `mov_text`、WebVTT、图像轨 OCR
- 回封装进 MKV
- 改造 `POST /api/translate/bulk` 的同步抽轨（建议紧随其后，否则批量在云盘上仍会卡 HTTP）

---

## 12. 排期

大约 **4.5～5.5 个工作日**。

1. probe API + 弹层（先能看见轨）
2. 占位请求 + Job 先抽再译
3. ffmpeg 进度 + 超时/取消
4. 电影 / 剧集按钮与刷新
5. 测试 / 文档
