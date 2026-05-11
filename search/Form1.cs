using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace search;

public sealed class Form1 : Form
{
    private readonly WebView2 webView = new();
    private readonly Queue<string> pendingScripts = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private string searchFolderPath = string.Empty;
    private string saveFolderPath = string.Empty;
    private DateTime searchStartTime;
    private CancellationTokenSource? searchCts;
    private bool webReady;
    private bool isRunningAsAdmin;
    private string? lastReportPath;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    public Form1()
    {
        Text = "Cinematic Search";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1320, 820);
        BackColor = Color.FromArgb(12, 16, 25);
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        Icon = File.Exists(Path.Combine(AppContext.BaseDirectory, "gi46euav4r9i_64.ico"))
            ? new Icon(Path.Combine(AppContext.BaseDirectory, "gi46euav4r9i_64.ico"))
            : Icon;

        isRunningAsAdmin = CheckAdminStatus();
        saveFolderPath = Path.Combine(AppContext.BaseDirectory, "Результаты_поиска");
        Directory.CreateDirectory(saveFolderPath);

        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(webView);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await InitializeWebViewAsync();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)
            {
                var p = PointToClient(new Point(m.LParam.ToInt32()));
                const int grip = 8;

                bool left = p.X <= grip;
                bool right = p.X >= ClientSize.Width - grip;
                bool top = p.Y <= grip;
                bool bottom = p.Y >= ClientSize.Height - grip;

                if (left && top) m.Result = HTTOPLEFT;
                else if (right && top) m.Result = HTTOPRIGHT;
                else if (left && bottom) m.Result = HTBOTTOMLEFT;
                else if (right && bottom) m.Result = HTBOTTOMRIGHT;
                else if (left) m.Result = HTLEFT;
                else if (right) m.Result = HTRIGHT;
                else if (top) m.Result = HTTOP;
                else if (bottom) m.Result = HTBOTTOM;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private async Task InitializeWebViewAsync()
    {
        var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinematicSearch", "WebView2");
        var fixedRuntime = Path.Combine(AppContext.BaseDirectory, "Runtime", "WebView2");

        CoreWebView2Environment env;
        if (Directory.Exists(fixedRuntime) && File.Exists(Path.Combine(fixedRuntime, "msedgewebview2.exe")))
            env = await CoreWebView2Environment.CreateAsync(fixedRuntime, userDataFolder);
        else
            env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

        await webView.EnsureCoreWebView2Async(env);
        webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
        webReady = true;

        var indexPath = Path.Combine(AppContext.BaseDirectory, "Web", "index.html");
        if (!File.Exists(indexPath))
        {
            MessageBox.Show("Не найден файл интерфейса: " + indexPath, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        webView.Source = new Uri(indexPath);
        await FlushScriptsAsync();
    }

    private async void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? string.Empty;
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            switch (type)
            {
                case "app.ready":
                    await SendAsync("app.state", new
                    {
                        searchFolderPath,
                        saveFolderPath,
                        isRunningAsAdmin,
                        lastReportPath,
                        appVersion = "2.0 Liquid Glass"
                    });
                    await LogAsync("Application started");
                    break;

                case "window.drag":
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    break;

                case "window.minimize":
                    WindowState = FormWindowState.Minimized;
                    break;

                case "window.maximize":
                    ToggleMaximizeTaskbarSafe();
                    break;

                case "window.close":
                    Close();
                    break;

                case "folder.search.choose":
                    ChooseSearchFolder();
                    break;

                case "folder.save.choose":
                    ChooseSaveFolder();
                    break;

                case "admin.restart":
                    RestartAsAdmin();
                    break;

                case "search.start":
                    var words = payload.GetProperty("words").GetString() ?? string.Empty;
                    await StartSearchAsync(words);
                    break;

                case "search.cancel":
                    searchCts?.Cancel();
                    await LogAsync("Cancellation requested");
                    break;

                case "report.open":
                    OpenReport();
                    break;

                case "report.folder":
                    OpenFolder(saveFolderPath);
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendAsync("app.error", new { message = ex.Message });
        }
    }

    private void ToggleMaximizeTaskbarSafe()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }

        var area = Screen.FromHandle(Handle).WorkingArea;
        WindowState = FormWindowState.Normal;
        Bounds = area;
    }

    private void ChooseSearchFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = isRunningAsAdmin
                ? "Выберите папку для поиска файлов"
                : "Выберите папку для поиска файлов. Без прав администратора доступ может быть ограничен.",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        searchFolderPath = dialog.SelectedPath;
        _ = SendAsync("folder.search.selected", new { path = searchFolderPath, accessOk = CheckFolderAccess(searchFolderPath) });
        _ = LogAsync("Search folder selected: " + searchFolderPath);
    }

    private void ChooseSaveFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите папку для сохранения результатов",
            SelectedPath = Directory.Exists(saveFolderPath) ? saveFolderPath : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        saveFolderPath = dialog.SelectedPath;
        _ = SendAsync("folder.save.selected", new { path = saveFolderPath });
        _ = LogAsync("Save folder selected: " + saveFolderPath);
    }

    private async Task StartSearchAsync(string rawWords)
    {
        if (string.IsNullOrWhiteSpace(searchFolderPath) || !Directory.Exists(searchFolderPath))
        {
            await SendAsync("app.error", new { message = "Сначала выберите существующую папку для поиска." });
            return;
        }

        var words = ParseWords(rawWords);
        if (words.Count == 0)
        {
            await SendAsync("app.error", new { message = "Введите слова для поиска. Минимальная длина слова — 2 символа." });
            return;
        }

        Directory.CreateDirectory(saveFolderPath);
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        searchStartTime = DateTime.Now;
        lastReportPath = null;

        await SendAsync("search.started", new
        {
            words,
            searchFolderPath,
            saveFolderPath,
            startedAt = searchStartTime.ToString("dd.MM.yyyy HH:mm:ss"),
            isRunningAsAdmin
        });
        await LogAsync("Search started: " + string.Join(", ", words));

        try
        {
            var result = await Task.Run(() => RunSearch(searchFolderPath, saveFolderPath, words, searchCts.Token));
            lastReportPath = result.ReportPath;
            await SendAsync("search.completed", result);
            await LogAsync($"Search completed. Found: {result.FoundCount}, processed: {result.TotalItems}, skipped: {result.AccessDeniedCount}");
        }
        catch (OperationCanceledException)
        {
            await SendAsync("search.cancelled", new { message = "Поиск отменён пользователем." });
            await LogAsync("Search cancelled");
        }
        catch (Exception ex)
        {
            await SendAsync("app.error", new { message = ex.Message });
            await LogAsync("Error: " + ex.Message);
        }
    }

    private SearchSummary RunSearch(string searchPath, string savePath, List<string> words, CancellationToken token)
    {
        var found = new List<FoundItem>();
        var log = new List<string>();
        int filesSkipped = 0;
        int dirsSkipped = 0;
        int accessDenied = 0;
        int processed = 0;

        var allFiles = new List<string>();
        var allDirs = new List<string>();

        void ReportLog(string text)
        {
            log.Add(text);
            _ = SendAsync("search.log", new { message = text });
        }

        void GetAllFilesSafe(string directory)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var f in Directory.GetFiles(directory)) allFiles.Add(f);
                foreach (var d in Directory.GetDirectories(directory))
                {
                    try { GetAllFilesSafe(d); }
                    catch (UnauthorizedAccessException) { dirsSkipped++; accessDenied++; ReportLog("Нет доступа к папке: " + d); }
                    catch (SecurityException) { dirsSkipped++; accessDenied++; ReportLog("Ошибка безопасности: " + d); }
                    catch (Exception ex) { dirsSkipped++; ReportLog($"Пропуск папки {d}: {ex.Message}"); }
                }
            }
            catch (UnauthorizedAccessException) { accessDenied++; ReportLog("Нет доступа к папке: " + directory); }
            catch (SecurityException) { accessDenied++; ReportLog("Ошибка безопасности: " + directory); }
            catch (Exception ex) { ReportLog($"Ошибка в папке {directory}: {ex.Message}"); }
        }

        void GetAllDirsSafe(string directory)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var d in Directory.GetDirectories(directory))
                {
                    allDirs.Add(d);
                    try { GetAllDirsSafe(d); }
                    catch { }
                }
            }
            catch { }
        }

        ReportLog("Сканирование структуры папок...");
        GetAllFilesSafe(searchPath);
        GetAllDirsSafe(searchPath);
        int total = allFiles.Count + allDirs.Count;
        ReportLog($"Доступно элементов: {total:n0}");

        foreach (var file in allFiles)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var fileName = Path.GetFileName(file);
                var dir = Path.GetDirectoryName(file) ?? string.Empty;
                foreach (var word in words)
                {
                    if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(new FoundItem("Файл", fileName, dir, file, word));
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException) { filesSkipped++; accessDenied++; }
            catch (SecurityException) { filesSkipped++; accessDenied++; }
            catch { filesSkipped++; }

            processed++;
            ReportProgress(processed, total, found.Count);
        }

        foreach (var dir in allDirs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var name = Path.GetFileName(dir);
                var parent = Path.GetDirectoryName(dir) ?? string.Empty;
                foreach (var word in words)
                {
                    if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(new FoundItem("Папка", name, parent, dir, word));
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException) { dirsSkipped++; accessDenied++; }
            catch { dirsSkipped++; }

            processed++;
            ReportProgress(processed, total, found.Count);
        }

        var duration = DateTime.Now - searchStartTime;
        var reportPath = SaveReport(savePath, searchPath, words, found, total, filesSkipped, dirsSkipped, accessDenied, duration);

        return new SearchSummary
        {
            Found = found.Take(250).ToList(),
            FoundCount = found.Count,
            TotalItems = total,
            FilesSkipped = filesSkipped,
            DirsSkipped = dirsSkipped,
            AccessDeniedCount = accessDenied,
            DurationSeconds = Math.Round(duration.TotalSeconds, 1),
            ReportPath = reportPath,
            ReportFileName = Path.GetFileName(reportPath),
            SaveFolderPath = savePath,
            SearchFolderPath = searchPath,
            Admin = isRunningAsAdmin
        };
    }

    private void ReportProgress(int processed, int total, int found)
    {
        var percent = total <= 0 ? 0 : (int)Math.Round(processed * 100d / total);
        _ = SendAsync("search.progress", new { processed, total, found, percent });
    }

    private string SaveReport(string savePath, string searchPath, List<string> words, List<FoundItem> found, int total, int filesSkipped, int dirsSkipped, int accessDenied, TimeSpan duration)
    {
        Directory.CreateDirectory(savePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeWords = string.Join("_", words.Take(3).Select(w => new string(w.Take(12).Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray())));
        if (string.IsNullOrWhiteSpace(safeWords)) safeWords = "search";
        var fileName = found.Count > 0 ? $"{timestamp}_{safeWords}.txt" : $"{timestamp}_{safeWords}_NO_RESULTS.txt";
        var path = Path.Combine(savePath, fileName);

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("================================================");
        writer.WriteLine("        ОТЧЕТ О ПОИСКЕ ФАЙЛОВ И ПАПОК");
        writer.WriteLine("================================================");
        writer.WriteLine($"Дата поиска: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        writer.WriteLine($"Время выполнения: {duration.TotalSeconds:F1} сек.");
        writer.WriteLine($"Папка поиска: {searchPath}");
        writer.WriteLine($"Папка сохранения: {savePath}");
        writer.WriteLine($"Ключевые слова: {string.Join(", ", words)}");
        writer.WriteLine($"Запуск от администратора: {(isRunningAsAdmin ? "ДА" : "НЕТ")}");
        writer.WriteLine();
        writer.WriteLine("----------- СТАТИСТИКА -----------");
        writer.WriteLine($"Всего обработано: {total:n0}");
        writer.WriteLine($"Найдено совпадений: {found.Count:n0}");
        writer.WriteLine($"Нет доступа / ограничения: {accessDenied:n0}");
        writer.WriteLine($"Файлов пропущено: {filesSkipped:n0}");
        writer.WriteLine($"Папок пропущено: {dirsSkipped:n0}");
        writer.WriteLine();

        if (found.Count == 0)
        {
            writer.WriteLine("Совпадений не найдено.");
        }
        else
        {
            writer.WriteLine("----------- НАЙДЕННЫЕ ЭЛЕМЕНТЫ -----------");
            int counter = 1;
            foreach (var item in found)
            {
                writer.WriteLine($"--- ЭЛЕМЕНТ #{counter++} ---");
                writer.WriteLine($"ТИП: {item.Type}");
                writer.WriteLine($"ИМЯ: {item.Name}");
                writer.WriteLine($"ПАПКА: {item.Directory}");
                writer.WriteLine($"ПОЛНЫЙ ПУТЬ: {item.FullPath}");
                writer.WriteLine($"НАЙДЕНО ПО СЛОВУ: {item.Keyword}");
                writer.WriteLine(new string('=', 50));
                writer.WriteLine();
            }
        }

        return path;
    }

    private List<string> ParseWords(string text)
    {
        char[] separators = { ' ', ',', '.', ';', ':', '\n', '\r', '\t' };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool CheckFolderAccess(string path)
    {
        try
        {
            _ = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
            _ = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
            return true;
        }
        catch { return false; }
    }

    private static bool CheckAdminStatus()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdmin()
    {
        try
        {
            var procInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = Application.ExecutablePath,
                WorkingDirectory = Environment.CurrentDirectory,
                Verb = "runas"
            };
            Process.Start(procInfo);
            Application.Exit();
        }
        catch (Exception ex)
        {
            _ = SendAsync("app.error", new { message = "Не удалось перезапустить с правами администратора: " + ex.Message });
        }
    }

    private void OpenReport()
    {
        if (string.IsNullOrWhiteSpace(lastReportPath) || !File.Exists(lastReportPath))
        {
            _ = SendAsync("app.error", new { message = "Файл отчёта пока не создан." });
            return;
        }
        Process.Start(new ProcessStartInfo(lastReportPath) { UseShellExecute = true });
    }

    private void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private async Task LogAsync(string message)
    {
        await SendAsync("app.log", new { message, time = DateTime.Now.ToString("HH:mm:ss") });
    }

    private async Task SendAsync(string type, object data)
    {
        var payload = JsonSerializer.Serialize(new { type, data }, json);
        var script = $"window.desktopResponse && window.desktopResponse({payload});";

        if (!webReady || webView.CoreWebView2 == null)
        {
            pendingScripts.Enqueue(script);
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => _ = webView.ExecuteScriptAsync(script)));
                return;
            }

            await webView.ExecuteScriptAsync(script);
        }
        catch (InvalidOperationException)
        {
            pendingScripts.Enqueue(script);
        }
    }

    private async Task FlushScriptsAsync()
    {
        while (pendingScripts.Count > 0 && webReady && webView.CoreWebView2 != null)
        {
            var script = pendingScripts.Dequeue();
            await webView.ExecuteScriptAsync(script);
        }
    }

    private sealed record FoundItem(string Type, string Name, string Directory, string FullPath, string Keyword);

    private sealed class SearchSummary
    {
        public List<FoundItem> Found { get; set; } = new();
        public int FoundCount { get; set; }
        public int TotalItems { get; set; }
        public int FilesSkipped { get; set; }
        public int DirsSkipped { get; set; }
        public int AccessDeniedCount { get; set; }
        public double DurationSeconds { get; set; }
        public string ReportPath { get; set; } = string.Empty;
        public string ReportFileName { get; set; } = string.Empty;
        public string SaveFolderPath { get; set; } = string.Empty;
        public string SearchFolderPath { get; set; } = string.Empty;
        public bool Admin { get; set; }
    }
}
