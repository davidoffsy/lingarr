# MKV 内置文本字幕翻译实施方案

状态：Phase 0–3 已完成，Phase 4 手工验收待做  
范围：第一期只做 MKV **文本轨**，翻译结果写成 Plex / Emby 可识别的外挂字幕  
非目标：PGS / VobSub / DVB 图像轨、回封装进 MKV、MP4 `mov_text`、WebVTT parser

## 完成进度

- [x] Phase 0 — 运行时（Docker / ffmpeg / 进程封装）
- [x] Phase 1 — 探测 + 抽轨
- [x] Phase 2 — 接到现有翻译
- [x] Phase 3 — 配置 / UI / 种子数据
- [ ] Phase 4 — 测试与验收

---

## 1. 背景

Lingarr 当前只处理视频旁边的 sidecar（`.srt` / `.ssa` / `.ass`）：

```
Radarr / Sonarr 同步
  → 只存目录 Path + 无扩展名 FileName
  → 扫 sidecar，按文件名猜语言
  → Hangfire TranslationJob
  → 解析 → 翻译 → 写出新的 sidecar
```

仓库内没有 FFmpeg / mkvtoolnix / Matroska 实现。Docker 镜像是纯 `aspnet:10.0`。媒体实体不保存完整视频路径。

很多片源（尤其是 Remux）只有 MKV 内嵌字幕、没有外挂。Plex / Emby 播放翻译结果时，认的是**外挂文件名**，不是容器里的轨。因此正确做法是：抽出文本轨 → 走现有翻译管线 → 写成 sidecar，**不改视频文件**。

---

## 2. 产品约定

### 2.1 做与不做

| 做 | 不做 |
|---|---|
| 抽 SRT / ASS / SSA 文本轨 | PGS / VobSub / DVB |
| 写出 sidecar | mux 回 MKV |
| 源轨可落成外挂（建议默认开） | 改视频、改 hash |
| 第一期只认 `.mkv` | MP4 `mov_text`、WebVTT |

### 2.2 Plex / Emby 输出文件名

必须写在视频同目录，stem 与视频一致：

```text
{视频目录}/{视频stem}.{语言}[.caption].srt|ass
```

示例：

- `Dune.2021.zh-cn.srt`
- `Dune.2021.en.forced.srt`
- `Show.S01E01.zh.srt`

对应现有 `SubtitleService.CreateFilePath`。不要把自定义 subtitle tag 设为默认（Plex / Emby 不认）。给播放器使用时，`remove_language_tag` 必须保持关闭。

ASS 保留原格式。用户若开启 Strip formatting，抽出后走现有逻辑即可。

### 2.3 默认开关

老用户行为不变：扫描内嵌字幕默认 **关闭**。需要的媒体库再打开。

---

## 3. 核心策略

不要让 `TranslationJob` 直接读 `.mkv`。抽出后扩展名是 `.srt` / `.ass`，现有解析、翻译、校验、校对、命名全部复用。

```text
定位 {Path}/{FileName}.mkv
  → ffprobe 列字幕轨（只留文本轨）
  → 选源语言轨（sidecar 优先于内嵌）
  → ffmpeg 抽到 {stem}.{srcLang}[.forced|.sdh].srt
  → CreateRequest(抽出的 sidecar 路径)
  → TranslationJob 写出 {stem}.{targetLang}.srt
```

对 Plex / Emby 的附带好处：源语言也变成外挂，客户端可以切换「英 / 中」。

### 3.1 哪些入口可以探测 MKV

列表页和统计 **禁止** ffprobe。现在电影列表是 `GetAllSubtitles(目录)`，剧集是扫整季目录，全库探测会把页面拖死。

| 入口 | 是否探测 MKV |
|---|---|
| 电影 / 剧集列表、统计 Job | 否，只扫 sidecar |
| 自动翻译 / bulk / `GetSubtitles(path, fileName)` | 是，只探这一个视频 |
| 手动处理某一集 / 电影 | 是，按需 |

第一期手动翻译：片源只有内嵌、列表没有徽章时，打开扫描后跑自动任务或按媒体处理才会抽出；抽出后列表出现语言徽章。二期再给列表加「内嵌」标记。

---

## 4. 分阶段实施

### Phase 0 — 运行时 ✅

工作量：约 0.5 天

- [x] `Lingarr.Server/Dockerfile` 在 runtime 镜像安装 `ffmpeg`（含 `ffprobe`）
- [x] `IExternalProcessRunner` / `ExternalProcessRunner`：`ArgumentList`、超时、缺二进制记日志并返回结果而不是抛错
- [x] `FfmpegExecutable`：可用 `FFMPEG_PATH` / `FFPROBE_PATH` 覆盖
- [x] `Readme.MD`、`Settings.MD`、`Lingarr.Docs` 补充镜像体积与读视频权限说明

Docker 最终镜像安装 `ffmpeg`（自带 `ffprobe`）：

```dockerfile
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg \
 && rm -rf /var/lib/apt/lists/*
```

封装进程调用：

- 使用 `ProcessStartInfo.ArgumentList`，路径不走 shell
- 设置超时
- 找不到二进制时记日志并跳过，不让整个翻译任务失败

文档：`Readme.MD` / `Lingarr.Docs` 写明镜像变大、容器需要能读视频文件。

### Phase 1 — 探测 + 抽轨 ✅

工作量：约 1.5～2 天

- [x] `IMediaFileLocator` / `MediaFileLocator`：定位 `{Path}/{FileName}.mkv`，否则按 `{FileName}.*` 过滤 `.mkv`
- [x] `IEmbeddedSubtitleService` / `EmbeddedSubtitleService` / `EmbeddedSubtitleTrack`：probe + extract
- [x] DI 注册；缺 ffmpeg / 超时跳过而不是抛错
- [x] 单测覆盖文本轨过滤、语言映射、forced/sdh、不覆盖已有 sidecar

**定位视频（第一期不改库）：**

```csharp
Path.Combine(media.Path, media.FileName + ".mkv")
// 不存在再按视频扩展名过滤 FileName.*
```

第一期只认 `.mkv`。

**探测：**

```text
ffprobe -v error -select_streams s
  -show_entries stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced,hearing_impaired
  -of json
```

| `codec_name` | 动作 |
|---|---|
| `subrip` / `srt` | 抽成 `.srt` |
| `ass` / `ssa` | 抽成 `.ass` / `.ssa` |
| `webvtt` | 第一期跳过（现有 parser 没有） |
| `hdmv_pgs_subtitle` / `dvd_subtitle` / `dvb_subtitle` | 跳过 |

语言映射：`eng` → `en`，`chi` / `zho` → `zh`。对不上 `LanguageCodeService` 的标 `unknown`。自动任务跳过 `unknown` / `und`。

Caption：`forced` → `forced`，`hearing_impaired` → `sdh`，与现有文件名规则对齐。

**抽轨：**

```text
ffmpeg -y -i {mkv} -map 0:{index} -c:s copy {stem}.{lang}[.caption].{ext}
```

目标路径已存在则不覆盖。抽完校验文件非空。

### Phase 2 — 接到现有翻译 ✅

工作量：约 1 天

改 `MediaSubtitleProcessor.ProcessMedia`：

- [x] 先 `GetSubtitles` 拿 sidecar
- [x] 设置开启时，probe 该 MKV 文本轨
- [x] 合并列表：**同语言 + 同 caption 的 sidecar 优先**，丢掉对应内嵌轨，避免翻两次
- [x] `SelectSourceSubtitle` 仍按现有 source / target / ignoreCaptions
- [x] 源是内嵌 → 先抽成 sidecar → `CreateRequest(抽出路径)`
- [x] Hash 把轨 `index + codec + lang` 算进去，否则抽完前后 hash 对不上会空转或重复
- [x] `ValidateSubtitle` 按扩展名选 parser（抽出的 ASS 不能被 `SrtParser` 判无效）
- [x] `GetAllSubtitles(目录)` / 统计 Job **不加** 内嵌扫描
- [x] `TranslationJob` / `ProofreadJob` 尽量不改；抽出文件必须是真实 sidecar 路径
- [x] bulk / `TranslationRequestService.ProcessMediaSubtitles` 同样 probe + 抽源

### Phase 3 — 配置 / UI / 种子数据 ✅

工作量：约 0.5～1 天

- [x] 迁移 `M0022` 种子数据（默认关闭扫描）
- [x] `SettingKeys`（`scan_embedded_subtitles` / `extract_embedded_source`，未种子前默认关闭扫描）
- [x] 前端 `SETTINGS` / `ISettings`
- [x] `SubtitleSettings.vue` 两个开关
- [x] `Subtitles` / 前端 `ISubtitle`：`IsEmbedded` / `TrackIndex`

新设置（迁移 `M0022`，默认关闭）：

| key | 默认 | 含义 |
|---|---|---|
| `scan_embedded_subtitles` | `false` | 自动 / 批量时扫描 MKV 文本轨 |
| `extract_embedded_source` | `true` | 把源轨写成外挂，Plex / Emby 也能切源语言 |

改动：

- `Lingarr.Core/Configuration/SettingKeys.cs`
- 前端 `SETTINGS` / `ISettings`
- `SubtitleSettings.vue`：两个开关，说明结果写在视频旁边、给 Plex / Emby 使用
- `Subtitles` / `ISubtitle` 已加 `isEmbedded`（第一期 UI 抽出后仍当普通徽章；列表「内嵌」标记放二期）

说明：关闭 `extract_embedded_source` 时后端仍会抽出源 sidecar。`TranslationJob` 只读 sidecar，目标路径也从源路径推导，本期不改 Job。

### Phase 4 — 测试与验收

工作量：约 1 天

单测（mock ffmpeg）：

- [x] 只收 srt / ass，丢掉 pgs
- [x] `eng` / `chi` 语言映射
- [x] sidecar 与内嵌同语言时不重复入队
- [x] `und` 不自动翻译
- [x] 目标语言 sidecar 已存在则跳过
- [x] `ignore_captions` 跳过 forced / sdh 轨
- [x] ffmpeg 不存在或超时 → 跳过内嵌，sidecar 流程不受影响

手工（Plex / Emby）：

- [ ] 准备只有内嵌英文字幕的 MKV，无 sidecar
- [ ] 打开扫描，跑自动翻译到中文
- [ ] 目录出现 `Movie.en.srt` 和 `Movie.zh.srt`（或 `zh-cn`）
- [ ] 刷新资料库后播放器能选中 / 英，视频文件本身未变

---

## 5. 文件清单

| 完成 | 文件 | 动作 |
| :---: |---|---|
| [x] | `Lingarr.Server/Dockerfile` | 安装 ffmpeg |
| [x] | `Lingarr.Server/Services/Media/*` | 新建 locator + probe / extract + merger |
| [x] | `Lingarr.Server/Extensions/ServiceCollectionExtensions.cs` | 注册新服务 |
| [x] | `Lingarr.Server/Services/MediaSubtitleProcessor.cs` | 合并轨、抽源、再入队 |
| [x] | `Lingarr.Server/Services/SubtitleService.cs` | `ValidateSubtitle` 按扩展名选 parser |
| [x] | `Lingarr.Server/Models/FileSystem/Subtitles.cs` | 可选 `IsEmbedded` / `TrackIndex` |
| [x] | `Lingarr.Core/Configuration/SettingKeys.cs` | 新配置键 |
| [x] | `Lingarr.Migrations/Migrations/M0022_SeedEmbeddedSubtitleSettings.cs` | 种子数据 |
| [x] | `Lingarr.Client/src/components/features/settings/SubtitleSettings.vue` | 开关 |
| [x] | `Lingarr.Client/src/ts/setting.ts` | 前端设置键 |
| [x] | `Readme.MD` / `Lingarr.Docs` | 镜像依赖、Plex / Emby 命名、默认关闭 |
| [x] | `Lingarr.Server.Tests` | mock 探测 / 去重 / 语言（手工 Plex 验收仍待做） |

翻译插件、Hangfire 队列、实体 `Movie.Path` 第一期不动。

---

## 6. 现有代码锚点

发现与选择：

- `Lingarr.Server/Services/SubtitleService.cs` — `SupportedExtensions = [".srt", ".ssa", ".ass"]`，`GetAllSubtitles` / `GetSubtitles` / `CreateFilePath` / `ValidateSubtitle`
- `Lingarr.Server/Services/MediaSubtitleProcessor.cs` — 自动化入队
- `Lingarr.Server/Services/Sync/MovieSync.cs`、`EpisodeSync.cs` — 丢掉扩展名，只存目录 + stem

翻译与写回：

- `Lingarr.Server/Jobs/TranslationJob.cs` — 读 sidecar、翻译、`CreateFilePath` 写回
- `Lingarr.Server/Jobs/ProofreadJob.cs` — 覆盖已译 sidecar
- Parser / Writer：`Services/Subtitle/SrtParser.cs`、`SsaParser.cs`、`SrtWriter.cs`、`SsaWriter.cs`

配置与前端：

- `Lingarr.Core/Configuration/SettingKeys.cs`
- `Lingarr.Client/src/ts/setting.ts`
- `Lingarr.Client/src/components/features/settings/SubtitleSettings.vue`
- 最新迁移编号：`M0021_SeedAiContextUseTranslated`（下一号 `M0022`）

列表与统计（禁止在这里 probe）：

- `Lingarr.Server/Services/MediaService.cs` — 电影列表 `GetAllSubtitles(movie.Path)`
- `Lingarr.Client/src/components/features/show/SeasonTable.vue` — 整季目录 collect
- `Lingarr.Server/Jobs/StatisticsJob.cs`

---

## 7. 风险与对策

1. **列表性能**：探测只挂在 `GetSubtitles(path, fileName)`，禁止进入 `GetAllSubtitles`。
2. **路径空格 / 特殊字符**：禁止拼接 shell 命令，使用 `ArgumentList`。
3. **抽轨失败**：缺 ffmpeg、权限不足、坏轨 → 记日志，当作没有内嵌字幕，不影响已有 sidecar。
4. **语言 `und`**：自动跳过，避免乱抽第一条轨。
5. **多文本轨**：同语言时优先 `default`，再非 forced；forced / sdh 当作 caption。
6. **Plex 不马上出现**：本机或可监控目录一般会自己扫到；网盘库可能要手动扫描，文档写明即可。
7. **镜像体积**：ffmpeg 大约几十 MB，可接受。

---

## 8. 排期

大约 4～5 个工作日可合并一版可用 MVP。

顺序：

1. Docker / ffmpeg
2. probe / extract
3. Processor 接入
4. 校验修复
5. 设置 / UI
6. 测试 / 文档

---

## 9. 二期（不在本次范围）

列表按需提取与云盘进度已单独成文，见 [`mkv-on-demand-extract-ui-plan.md`](./mkv-on-demand-extract-ui-plan.md)（用徽章旁按钮代替列表全量「内嵌」徽章，避免列表 ffprobe）。

仍未纳入：

- 同步时预探测并缓存轨信息
- 完整视频路径入库
- MP4 `mov_text`
- WebVTT parser
- 图像轨 OCR（PGS / VobSub）
- 回封装进 MKV（默认不建议，会改 hash，Radarr / Sonarr 可能重新导入）
