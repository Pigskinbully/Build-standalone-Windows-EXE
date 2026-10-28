using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ScreenTimerRecorder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new RecorderForm());
    }
}

internal sealed class RecorderForm : Form
{
    private readonly Label statusLabel = new() { Text = "等待开始录制", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label timeLabel = new() { Text = "00:00:00", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 27, FontStyle.Bold) };
    private readonly Button startButton = new() { Text = "开始录制", Width = 142, Height = 44 };
    private readonly Button stopButton = new() { Text = "停止并保存", Width = 142, Height = 44, Enabled = false };
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 200 };
    private readonly Stopwatch stopwatch = new();
    private Process? recordingProcess;
    private string? workspace;
    private string? outputPath;
    private string? ffmpegPath;
    private string errorLog = "";
    private bool stopping;
    private bool recording;
    private int lastSecond = -1;

    internal RecorderForm()
    {
        Text = "屏幕录制计时器";
        ClientSize = new Size(380, 245);
        MinimumSize = new Size(400, 280);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "主显示器全屏录制 · 时间仅写入视频", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        layout.Controls.Add(timeLabel, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(startButton);
        buttons.Controls.Add(stopButton);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(statusLabel, 0, 3);
        Controls.Add(layout);
        startButton.Click += async (_, _) => await StartRecordingAsync();
        stopButton.Click += async (_, _) => await StopRecordingAsync();
        uiTimer.Tick += (_, _) => UpdateTime();
        FormClosing += OnFormClosing;
    }

    private static string FormatTime(TimeSpan elapsed) => $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    private static string PrepareFfmpeg()
    {
        var resourceName = "ScreenTimerRecorder.ffmpeg.exe";
        var assembly = Assembly.GetExecutingAssembly();
        using var input = assembly.GetManifestResourceStream(resourceName);
        if (input is null)
            throw new InvalidOperationException("此文件没有内置 FFmpeg。请使用云端构建生成的正式 EXE，而不是直接编译缺少资源的源码。");
        var dir = Path.Combine(Path.GetTempPath(), "ScreenTimerRecorder", "engine");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "ffmpeg.exe");
        // Write a fully extracted executable. Each app launch can refresh an older cached copy.
        var temporary = dest + ".new";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
        try { File.Move(temporary, dest, overwrite: true); }
        catch (IOException) { File.Delete(temporary); if (!File.Exists(dest)) throw; }
        return dest;
    }

    private async Task StartRecordingAsync()
    {
        if (recording || stopping) return;
        using var save = new SaveFileDialog
        {
            Filter = "MP4 视频 (*.mp4)|*.mp4", DefaultExt = "mp4", AddExtension = true,
            FileName = $"录屏_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        outputPath = save.FileName;
        startButton.Enabled = false;
        statusLabel.Text = "准备录制引擎...";
        try
        {
            ffmpegPath ??= PrepareFfmpeg();
            workspace = Path.Combine(Path.GetTempPath(), "ScreenTimerRecorder", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
            if (!File.Exists(font)) throw new FileNotFoundException("无法找到 Windows Arial 字体文件。", font);
            File.Copy(font, Path.Combine(workspace, "font.ttf"));
            File.WriteAllText(Path.Combine(workspace, "timer.txt"), "00:00:00", new UTF8Encoding(false));
            WindowState = FormWindowState.Minimized;
            await Task.Delay(500); // Hide the controls before recording begins.
            var filter = "drawtext=fontfile=font.ttf:textfile=timer.txt:reload=1:fontcolor=white:fontsize=36:box=1:boxcolor=black@0.6:boxborderw=12:x=w-tw-30:y=30";
            var info = new ProcessStartInfo(ffmpegPath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardError = true, WorkingDirectory = workspace,
                StandardInputEncoding = new UTF8Encoding(false)
            };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "gdigrab", "-framerate", "20", "-i", "desktop", "-vf", filter,
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23", "-pix_fmt", "yuv420p", "-movflags", "+faststart", outputPath! })
                info.ArgumentList.Add(arg);
            recordingProcess = new Process { StartInfo = info, EnableRaisingEvents = true };
            errorLog = "";
            recordingProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) errorLog = (errorLog + "\n" + e.Data); };
            recordingProcess.Exited += (_, _) => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(async () => { if (recording && !stopping) await StopRecordingAsync(true); })); };
            if (!recordingProcess.Start()) throw new InvalidOperationException("无法启动录制引擎。");
            recordingProcess.BeginErrorReadLine();
            stopwatch.Restart();
            lastSecond = -1;
            recording = true;
            stopButton.Enabled = true;
            statusLabel.Text = "正在录制（窗口已最小化）";
            uiTimer.Start();
        }
        catch (Exception ex)
        {
            recording = false;
            uiTimer.Stop();
            stopwatch.Reset();
            WindowState = FormWindowState.Normal;
            startButton.Enabled = true;
            stopButton.Enabled = false;
            statusLabel.Text = "启动失败";
            MessageBox.Show(this, ex.Message, "录制失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Cleanup();
        }
    }

    private void UpdateTime()
    {
        if (!recording || workspace is null) return;
        var sec = (int)stopwatch.Elapsed.TotalSeconds;
        timeLabel.Text = FormatTime(stopwatch.Elapsed);
        if (sec == lastSecond) return;
        lastSecond = sec;
        try
        {
            // Rename on the same volume to avoid FFmpeg reading a half-written timer.
            var path = Path.Combine(workspace, "timer.txt");
            var next = Path.Combine(workspace, "timer-next.txt");
            File.WriteAllText(next, timeLabel.Text, new UTF8Encoding(false));
            File.Move(next, path, true);
        }
        catch (IOException) { /* Retry at the next tick if the file was temporarily held. */ lastSecond = -1; }
    }

    private async Task StopRecordingAsync(bool unexpectedlyExited = false)
    {
        if (!recording || stopping) return;
        stopping = true;
        uiTimer.Stop();
        stopwatch.Stop();
        stopButton.Enabled = false;
        statusLabel.Text = "正在保存 MP4...";
        WindowState = FormWindowState.Normal;
        try
        {
            if (recordingProcess is not null && !recordingProcess.HasExited)
            {
                await recordingProcess.StandardInput.WriteLineAsync("q");
                await recordingProcess.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await recordingProcess.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { recordingProcess.Kill(entireProcessTree: true); await recordingProcess.WaitForExitAsync(); }
            }
            if (recordingProcess is not null) await recordingProcess.WaitForExitAsync();
            var saved = outputPath is not null && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            var ok = saved && !unexpectedlyExited && recordingProcess?.ExitCode == 0;
            statusLabel.Text = ok ? "保存完成" : "录制结束，可能未正常保存";
            if (ok) MessageBox.Show(this, $"视频已保存：\n{outputPath}", "录制完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else MessageBox.Show(this, $"请检查录制文件与以下错误信息：\n{errorLog.Trim().Take(1800).Aggregate(new StringBuilder(), (b, c) => b.Append(c))}", "录制异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "保存失败";
            MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            recording = false;
            stopping = false;
            startButton.Enabled = true;
            Cleanup();
        }
    }

    private void Cleanup()
    {
        recordingProcess?.Dispose();
        recordingProcess = null;
        if (workspace is not null)
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            workspace = null;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!recording || stopping) { if (stopping) e.Cancel = true; return; }
        e.Cancel = true;
        _ = StopRecordingAsync();
    }
}
