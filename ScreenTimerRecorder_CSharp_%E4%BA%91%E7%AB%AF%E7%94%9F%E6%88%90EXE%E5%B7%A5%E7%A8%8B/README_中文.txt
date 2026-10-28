屏幕录制计时器 / 原生 Windows C# 版本
===================================
功能：点击开始录屏同步计时、计时器只嵌入最终 MP4 右上角、无悬浮窗口、中文 Windows 界面、点击停止保存。
适用：Windows 10/11 64位；默认全桌面、20 FPS、无音频。注意本项目当前未经过 Windows 实机测试。

重要说明
--------
本 ZIP 是完整源代码及云端编译配置，不是已经编译好的 EXE。
可以用 GitHub Actions 的 Windows 云端机器构建 EXE，你的个人电脑无需安装 Python、.NET SDK、Visual Studio 或任何编译器。
不过使用 GitHub Actions 需要 GitHub 账号，上传代码并运行一次 Actions；你最后下载的 artifact 内就是 EXE。
云端构建时会从 gyan.dev 下载 FFmpeg 并嵌入 EXE，生成文件可能较大；运行时会解包到临时目录。

GitHub 云端构建步骤
-----------------
1. 登录 GitHub，在网页上创建一个私人 repository（如 ScreenTimerRecorder）。
2. 上传本文件夹中的 Program.cs、ScreenTimerRecorder.csproj、Assets 文件夹及 .github/workflows/build-windows.yml。
   注意 .github 是隐藏文件夹，一定要保留。推荐通过 GitHub 网页上传整个文件夹的内容（不含外层文件夹）。
3. 进入 repository 的 Actions，选择 Build standalone Windows EXE，点击 Run workflow。
4. 工作流完成后，在该次运行的 Artifacts 下载 ScreenTimerRecorder-Windows-x64。
5. 解压后双击 ScreenTimerRecorder.exe。运行无需安装 Python、.NET SDK 或编译工具。

使用提示
--------
1. 软件启动后点击“开始录制”并选择 MP4 输出位置。
2. 软件自动最小化，录制全部桌面；主窗口中的计时不会以悬浮层出现。
3. 从任务栏恢复窗口后，点击“停止并保存”，待 MP4 完成写入。
4. 此实现没有声音录制；部分受 DRM 保护的内容可能无法录制。
5. 智能屏幕保护/杀毒软件可能对未签名的 EXE 发出提示，应自行核实构建来源，不要关闭安全防护。

技术说明
--------
该程序使用 .NET 8 Windows Forms，FFmpeg gdigrab + libx264，drawtext 读取每秒更新的 UTF-8 计时文本。
只需要本机运行生成的单文件 EXE，不需要本机安装编译工具；第一次运行需将内置 FFmpeg 解压到临时文件夹。
