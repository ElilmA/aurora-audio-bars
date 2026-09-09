# 会话记录

## 2026-09-09 (会话6) - 提交声道分离 + 发布 v1.1.0

### 任务
声道分离代码发布到远程，并发布 v1.1.0 Release。

### 完成内容
- commit 66244c0「feat: stereo channel split - left/right bar per channel with tray toggle」含源码+README+.ai，已推 main
- dotnet publish win-x64 自包含单文件 v1.1.0（exe 71852188 B + zip 66265523 B），产物 bin/Release/publish-v1.1.0/
- tag v1.1.0 推送；gh release draft + 分条 upload（zip 首次超时、重试成功）+ 转正式

### 验证
- 发布链接：https://github.com/ElilmA/aurora-audio-bars/releases/tag/v1.1.0（两资产 uploaded，sha256 正常）
- git push 成功（6e50ceb..66244c0）

### 后续注意事项
- 未提交 .ai 记忆更新（memory/sessions/conversations），下轮可并入下一次提交
- 全屏独占游戏无法覆盖已答复用户（系统限制），不加菜单项

## 2026-09-09 (会话5) - 左右声道分离

### 任务
左条=左声道、右条=右声道；托盘菜单可开关声道选项。

### 完成内容
- AudioCapture：混音单 ring → L/R 双 ring（帧内通道按索引奇偶拆，立体声=原生L/R、单声道右镜像左、多声道近似下混）；`ReadLR` 分声道读、`Read` 保留为左右均值
- AudioEngine：3 组滚动窗口+SpectrumEngine（mono 复合/L/R 各自独立 FFT/AGC/平滑）；`ChannelSplit` volatile 托盘实时切换；`Left/Right` 属性取代 `Current`
- EdgeOverlay.Render：左条用 Left 帧、右条用 Right 帧
- AppHost/TrayIcon：新增「声道分离 (左=左声道/右=右声道)」勾选项，默认关
- README 特性与托盘说明更新；构建 0 错误；冒烟测试运行 4s 无崩溃

### 修改文件
- src/MouseAudioVisualizer/Audio/AudioCapture.cs（重写：L/R 双 ring 解交织）
- src/MouseAudioVisualizer/Audio/AudioEngine.cs（重写：双声道管道 + 模式切换）
- src/MouseAudioVisualizer/Visual/EdgeOverlay.cs（分声道渲染）
- src/MouseAudioVisualizer/Program.cs（AppHost.ChannelSplit）
- src/MouseAudioVisualizer/Shell/TrayIcon.cs（菜单项）
- README.md
- .ai/memory.md

### 验证
- dotnet build -c Release 0 错误；运行 4 秒进程存活后停止
- 未做左右声道真实差异的听感验证（需立体声素材+观察两侧差异）

### 后续注意事项
- 声道分离开启时 AGC 左右独立 → 单边特别小声的声道增益可能放大噪声，待实测
- 多声道设备(>2ch)左右为奇偶下混近似，非严格原声道
- 旧进程(6024)曾锁定 exe 导致无法覆盖，重建前先结束运行中的程序

## 2026-09-09 (会话4) - GitHub Release v1.0.0 发布

### 任务
在 GitHub 发布 Release 供下载。

### 完成内容
- `dotnet publish` win-x64 自包含单文件 exe(≈72MB),zip(≈66MB,exe+使用说明)
- git tag v1.0.0 已推送;gh release create 因资产大超时中断 → 改 draft + 分条 upload 完成
- release 已转正式(非 draft): https://github.com/ElilmA/aurora-audio-bars/releases/tag/v1.0.0

### 修改文件
- 无源码改动(git tree 保持干净);发布产物在 bin/Release/publish-v1.0.0/(gitignore)
- .ai/memory.md(发布状态与经验)

### 验证
- gh release view:isDraft=false,两个资产 uploaded,sha256 摘要正常

### 后续注意事项
- 自包含 exe 未经纯净机实测;关注杀软/Defender 误报与 Win10 兼容
- 下次发布直接用:先 `gh release create <tag> --draft` 再逐资产 `gh release upload`

## 2026-09-08 (会话3) - Aurora 视觉升级

### 任务
视觉升级为全色谱 Aurora Neon 能量流 + 修复下降残影。

### 完成内容

- EdgeRenderer v5:Aurora 全色谱(360°/完整 hue 环,底部紫/青分别起相,中间青绿黄橙红,顶部粉紫回环),慢速 hue drift(12s/圈),顶部液态圆头(bloom+光晕尾 16px),x 向中心亮边缘淡,饱和度/亮度微随音量
- 左右相位差:左 hueStart=270(紫系)、右 hueStart=180(青系)
- 残影修复:每帧 Draw 前整图 BGRA 清零(此前只跳过高位行,旧像素残留)
- EdgeOverlay BarWindow 增加 hueStart 参数,ResizeBar 保留
- 保留:底部向上、连续、attack/release、高度=音量、60FPS、宽度菜单、点击穿透

### 修改文件

- src/MouseAudioVisualizer/Visual/EdgeRenderer.cs(重写 v5)
- src/MouseAudioVisualizer/Visual/EdgeOverlay.cs(BarWindow hueStart)
- .ai/memory.md

### 验证

- PrintWindow:左条底部到顶 绿→黄→橙→红→品红→紫→蓝紫(67% 处透明,无残影)
- 右条:橙红→红→品红→紫→蓝→天蓝→青(同色谱、不同相位)
- hue drift:t+8s 颜色变化(12s/圈流动生效)
- CPU 0%

### 后续注意事项

- 顶部圆头视觉效果(bloom+光晕尾)代码实现,未逐帧截图细验,用户观感可微调 glowTail/bloomPx 常量
- 色相流动速度=12s/圈,用户要求 8-20s 范围内
- 测试时注意其他声源会干扰高度观察(浏览器/播放器在放音)