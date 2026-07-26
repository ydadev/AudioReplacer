using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioReplacerPortable
{
    internal sealed class FfmpegSetupDialog : Form
    {
        public FfmpegSetupDialog()
        {
            Text = "Необходим FFmpeg";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(590, 315);
            Font = new Font("Segoe UI", 9F);

            var title = new Label
            {
                Text = "В папке программы не найдены ffmpeg.exe и ffprobe.exe",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = true, Left = 18, Top = 18
            };
            var description = new Label
            {
                Text = "Оба файла необходимы для обработки и проверки видео.\r\n" +
                       "Перед загрузкой ознакомьтесь с лицензией и юридической информацией FFmpeg.\r\n" +
                       "Автоматическая загрузка использует публичную Windows-сборку BtbN с GitHub.",
                AutoSize = true, Left = 18, Top = 57
            };
            var licenseLink = new LinkLabel
            {
                Text = "Открыть лицензию FFmpeg (ffmpeg.org/legal.html)",
                AutoSize = true, Left = 18, Top = 120
            };
            licenseLink.LinkClicked += delegate
            {
                try { Process.Start("https://ffmpeg.org/legal.html"); } catch { }
            };
            var downloadLink = new LinkLabel
            {
                Text = "Открыть страницу ручной загрузки BtbN",
                AutoSize = true, Left = 18, Top = 148
            };
            downloadLink.LinkClicked += delegate
            {
                try { Process.Start("https://github.com/BtbN/FFmpeg-Builds/releases"); } catch { }
            };
            var note = new Label
            {
                Text = "При автоматической загрузке ffmpeg.exe и ffprobe.exe будут сохранены\r\n" +
                       "непосредственно рядом с AudioReplacer.exe.",
                AutoSize = true, Left = 18, Top = 181, ForeColor = Color.DimGray
            };

            var autoButton = new Button
            {
                Text = "Скачать автоматически",
                DialogResult = DialogResult.Yes,
                Left = 18, Top = 245, Width = 175, Height = 34
            };
            var manualButton = new Button
            {
                Text = "Скачать самостоятельно",
                DialogResult = DialogResult.No,
                Left = 203, Top = 245, Width = 175, Height = 34
            };
            var cancelButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Left = 388, Top = 245, Width = 175, Height = 34
            };
            AcceptButton = autoButton;
            CancelButton = cancelButton;
            Controls.AddRange(new Control[] {
                title, description, licenseLink, downloadLink, note,
                autoButton, manualButton, cancelButton
            });
        }
    }

    internal sealed class MediaPair
    {
        public string Video;
        public string Audio;
        public string Output;
        public string MatchType;
        public override string ToString()
        {
            return Path.GetFileName(Video) + "  <=  " + Path.GetFileName(Audio) +
                   "  [" + MatchType + "]";
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(
            new[] { ".mkv", ".mp4", ".mov", ".m4v", ".avi", ".webm", ".ts", ".mts",
                    ".m2ts", ".wmv", ".flv", ".mpg", ".mpeg", ".vob" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(
            new[] { ".mka", ".aac", ".ac3", ".eac3", ".mp3", ".flac", ".wav", ".m4a",
                    ".ogg", ".opus", ".wma", ".aiff", ".ape", ".dts" },
            StringComparer.OrdinalIgnoreCase);

        private readonly TextBox folderBox = new TextBox();
        private readonly Button browseButton = new Button();
        private readonly CheckBox sameFolderBox = new CheckBox();
        private readonly TextBox outputFolderBox = new TextBox();
        private readonly Button outputBrowseButton = new Button();
        private readonly Button scanButton = new Button();
        private readonly Button startButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly ListBox pairList = new ListBox();
        private readonly TextBox logBox = new TextBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label statusLabel = new Label();
        private readonly CheckBox overwriteBox = new CheckBox();
        private readonly PictureBox duckPicture = new PictureBox();
        private readonly List<MediaPair> pairs = new List<MediaPair>();
        private Process currentProcess;
        private bool cancelRequested;
        private bool checkingTools;
        private string resolvedFfmpegPath;
        private string resolvedFfprobePath;

        private string AppDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        private bool PortableFolderMode
        {
            get { return File.Exists(Path.Combine(AppDirectory, "portable-full.mode")); }
        }
        private string FfmpegPath { get { return resolvedFfmpegPath ?? Path.Combine(AppDirectory, "ffmpeg.exe"); } }
        private string FfprobePath { get { return resolvedFfprobePath ?? Path.Combine(AppDirectory, "ffprobe.exe"); } }
        private const string DownloadUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        private const string RepositoryUrl = "https://github.com/BtbN/FFmpeg-Builds/releases";

        public MainForm()
        {
            Text = "Замена аудиодорожек";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 590);
            Size = new Size(900, 680);
            Font = new Font("Segoe UI", 9F);
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var folderLabel = new Label { Text = "Рабочая папка:", AutoSize = true, Left = 12, Top = 18 };
            folderBox.Left = 115;
            folderBox.Top = 14;
            folderBox.Width = 590;
            folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            browseButton.Text = "Выбрать…";
            browseButton.Left = 715;
            browseButton.Top = 12;
            browseButton.Width = 90;
            browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseButton.Click += BrowseClicked;

            sameFolderBox.Text = "Записывать в ту же папку";
            sameFolderBox.AutoSize = true;
            sameFolderBox.Left = 12;
            sameFolderBox.Top = 53;
            sameFolderBox.Checked = true;
            sameFolderBox.CheckedChanged += delegate
            {
                outputFolderBox.Enabled = !sameFolderBox.Checked;
                outputBrowseButton.Enabled = !sameFolderBox.Checked;
                if (sameFolderBox.Checked) outputFolderBox.Text = folderBox.Text.Trim();
            };

            var outputLabel = new Label { Text = "Папка результата:", AutoSize = true, Left = 190, Top = 54 };
            outputFolderBox.Left = 310;
            outputFolderBox.Top = 49;
            outputFolderBox.Width = 395;
            outputFolderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            outputFolderBox.Enabled = false;

            outputBrowseButton.Text = "Выбрать…";
            outputBrowseButton.Left = 715;
            outputBrowseButton.Top = 47;
            outputBrowseButton.Width = 90;
            outputBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            outputBrowseButton.Enabled = false;
            outputBrowseButton.Click += OutputBrowseClicked;

            duckPicture.Left = 815;
            duckPicture.Top = 7;
            duckPicture.Width = 58;
            duckPicture.Height = 58;
            duckPicture.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            duckPicture.SizeMode = PictureBoxSizeMode.Zoom;
            duckPicture.BackColor = Color.Transparent;
            try
            {
                Stream duckStream = Assembly.GetExecutingAssembly()
                                            .GetManifestResourceStream("DuckPng");
                if (duckStream != null) duckPicture.Image = Image.FromStream(duckStream);
            }
            catch { }

            scanButton.Text = "Найти пары";
            scanButton.Left = 12;
            scanButton.Top = 82;
            scanButton.Width = 110;
            scanButton.Click += async delegate
            {
                if (await EnsureToolsAvailable(true)) ScanFolder();
            };

            overwriteBox.Text = "Перезаписывать существующие файлы с _";
            overwriteBox.AutoSize = true;
            overwriteBox.Left = 140;
            overwriteBox.Top = 87;

            var hint = new Label
            {
                Text = "Видео и внешняя аудиодорожка сопоставляются по имени файла.",
                AutoSize = true,
                Left = 12,
                Top = 120,
                ForeColor = Color.DimGray
            };

            pairList.Left = 12;
            pairList.Top = 142;
            pairList.Width = 860;
            pairList.Height = 160;
            pairList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            startButton.Text = "Заменить звук";
            startButton.Left = 12;
            startButton.Top = 313;
            startButton.Width = 130;
            startButton.Enabled = false;
            startButton.Click += StartClicked;

            cancelButton.Text = "Отмена";
            cancelButton.Left = 152;
            cancelButton.Top = 313;
            cancelButton.Width = 90;
            cancelButton.Enabled = false;
            cancelButton.Click += CancelClicked;

            progress.Left = 255;
            progress.Top = 317;
            progress.Width = 617;
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            statusLabel.Text = "Выберите папку.";
            statusLabel.Left = 12;
            statusLabel.Top = 353;
            statusLabel.Width = 860;
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            logBox.Left = 12;
            logBox.Top = 379;
            logBox.Width = 860;
            logBox.Height = 251;
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Multiline = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.ReadOnly = true;
            logBox.WordWrap = false;
            logBox.Font = new Font("Consolas", 8.5F);

            Controls.AddRange(new Control[] {
                folderLabel, folderBox, browseButton, sameFolderBox, outputLabel, outputFolderBox,
                outputBrowseButton, duckPicture, scanButton, overwriteBox, hint, pairList,
                startButton, cancelButton, progress, statusLabel, logBox
            });

            DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object sender, DragEventArgs e)
            {
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths.Length > 0 && Directory.Exists(paths[0]))
                {
                    folderBox.Text = paths[0];
                    ScanFolder();
                }
            };
            FormClosing += delegate
            {
                cancelRequested = true;
                TryKillCurrentProcess();
            };
            Shown += async delegate { await EnsureToolsAvailable(true); };
        }

        private async void BrowseClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с видео и аудиодорожками";
                if (Directory.Exists(folderBox.Text)) dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                    if (sameFolderBox.Checked) outputFolderBox.Text = dialog.SelectedPath;
                    if (await EnsureToolsAvailable(true)) ScanFolder();
                }
            }
        }

        private async void OutputBrowseClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для готовых файлов";
                if (Directory.Exists(outputFolderBox.Text)) dialog.SelectedPath = outputFolderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputFolderBox.Text = dialog.SelectedPath;
                    if (await EnsureToolsAvailable(true)) ScanFolder();
                }
            }
        }

        private void ScanFolder()
        {
            pairs.Clear();
            pairList.Items.Clear();
            logBox.Clear();
            startButton.Enabled = false;

            var folder = folderBox.Text.Trim();
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "Указанная папка не существует.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string outputFolder = sameFolderBox.Checked ? folder : outputFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Выберите папку для готовых файлов.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку результата:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            outputFolderBox.Text = outputFolder;
            if (!File.Exists(FfmpegPath) || !File.Exists(FfprobePath))
            {
                MessageBox.Show(this, "FFmpeg и FFprobe не найдены. Перезапустите программу " +
                    "и разрешите автоматическую загрузку либо установите их самостоятельно.",
                    "Не хватает FFmpeg", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
            var videos = files.Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                              .Where(f => !Path.GetFileName(f).StartsWith("_"))
                              .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
            var audios = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f))).ToList();
            var usedVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedAudios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var video in videos)
            {
                string videoStem = Path.GetFileNameWithoutExtension(video);
                var exact = audios.Where(a => string.Equals(Path.GetFileNameWithoutExtension(a), videoStem,
                                             StringComparison.OrdinalIgnoreCase)).ToList();
                List<string> candidates = exact;
                if (candidates.Count == 0)
                {
                    candidates = audios.Where(a => IsConservativeNameMatch(videoStem,
                        Path.GetFileNameWithoutExtension(a))).ToList();
                }
                if (candidates.Count == 1)
                {
                    var pair = new MediaPair
                    {
                        Video = video,
                        Audio = candidates[0],
                        Output = Path.Combine(outputFolder, "_" + Path.GetFileName(video)),
                        MatchType = "по имени"
                    };
                    pairs.Add(pair);
                    pairList.Items.Add(pair);
                    usedVideos.Add(video);
                    usedAudios.Add(candidates[0]);
                }
                else if (candidates.Count > 1)
                {
                    AppendLog("ПРОПУСК: несколько аудиофайлов подходят к " + Path.GetFileName(video));
                }
            }

            int numberedCount = AddNumberedSeriesPairs(
                videos.Where(v => !usedVideos.Contains(v)).ToList(),
                audios.Where(a => !usedAudios.Contains(a)).ToList(),
                outputFolder, usedVideos, usedAudios);

            statusLabel.Text = "Найдено пар: " + pairs.Count + ". Видео без однозначной пары не обрабатываются.";
            startButton.Enabled = pairs.Count > 0;
            AppendLog("Сканирование: видео — " + videos.Count + ", аудио — " + audios.Count +
                      ", однозначных пар — " + pairs.Count + ".");
            if (numberedCount > 0)
                AppendLog("По изменяющемуся номеру серии сопоставлено пар: " + numberedCount + ".");
        }

        private static bool IsConservativeNameMatch(string videoStem, string audioStem)
        {
            if (!audioStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)) return false;
            if (audioStem.Length == videoStem.Length) return true;
            char separator = audioStem[videoStem.Length];
            return separator == '.' || separator == '_' || separator == '-' || separator == ' ';
        }

        private int AddNumberedSeriesPairs(List<string> unmatchedVideos, List<string> unmatchedAudios,
                                           string folder, HashSet<string> usedVideos,
                                  …1150 tokens truncated…            string selectedOutputFolder = sameFolderBox.Checked
                ? folderBox.Text.Trim()
                : outputFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(selectedOutputFolder))
            {
                MessageBox.Show(this, "Выберите папку для готовых файлов.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                if (!Directory.Exists(selectedOutputFolder))
                    Directory.CreateDirectory(selectedOutputFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку результата:\r\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Путь результата мог быть изменён после сканирования. Всегда используем
            // текущее значение интерфейса непосредственно перед обработкой.
            foreach (var pair in pairs)
                pair.Output = Path.Combine(selectedOutputFolder, "_" + Path.GetFileName(pair.Video));

            SetBusy(true);
            cancelRequested = false;
            progress.Minimum = 0;
            progress.Maximum = pairs.Count;
            progress.Value = 0;
            int success = 0;
            int skipped = 0;
            int failed = 0;

            AppendLog("=== Начало обработки " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            AppendLog("Папка результата: " + selectedOutputFolder);
            for (int i = 0; i < pairs.Count; i++)
            {
                if (cancelRequested) break;
                var pair = pairs[i];
                pairList.SelectedIndex = i;
                statusLabel.Text = "Обработка " + (i + 1) + " из " + pairs.Count + ": " +
                                   Path.GetFileName(pair.Video);

                if (File.Exists(pair.Output) && !overwriteBox.Checked)
                {
                    AppendLog("ПРОПУСК: уже существует " + Path.GetFileName(pair.Output));
                    skipped++;
                    progress.Value = i + 1;
                    continue;
                }

                bool ok = await ProcessPair(pair);
                if (ok) success++; else if (!cancelRequested) failed++;
                progress.Value = i + 1;
            }

            AppendLog("=== Готово. Успешно: " + success + "; пропущено: " + skipped +
                      "; ошибок: " + failed + " ===");
            SaveLog();
            SetBusy(false);
            statusLabel.Text = cancelRequested ? "Операция отменена. Лог сохранён." :
                "Готово. Успешно: " + success + ", пропущено: " + skipped + ", ошибок: " + failed + ".";
            MessageBox.Show(this, statusLabel.Text, "Замена аудиодорожек",
                MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async Task<bool> EnsureToolsAvailable(bool offerDownload)
        {
            if (checkingTools) return false;
            checkingTools = true;
            try
            {
                string[] tools = FindInstalledTools();
                if (tools != null)
                {
                    resolvedFfmpegPath = tools[0];
                    resolvedFfprobePath = tools[1];
                    statusLabel.Text = "FFmpeg найден: " + Path.GetDirectoryName(resolvedFfmpegPath);
                    return true;
                }
                if (!offerDownload) return false;

                DialogResult answer;
                using (var setupDialog = new FfmpegSetupDialog())
                    answer = setupDialog.ShowDialog(this);

                if (answer == DialogResult.No)
                {
                    try { Process.Start(RepositoryUrl); } catch { }
                    MessageBox.Show(this,
                        PortableFolderMode
                            ? "После загрузки положите ffmpeg.exe и ffprobe.exe рядом с " +
                              "AudioReplacer.exe, затем перезапустите программу."
                            : "После загрузки положите ffmpeg.exe и ffprobe.exe рядом с " +
                              "AudioReplacer.exe либо добавьте папку с ними в PATH, затем перезапустите программу.",
                        "Самостоятельная установка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                if (answer != DialogResult.Yes) return false;

                SetToolDownloadState(true);
                statusLabel.Text = "Загрузка FFmpeg с GitHub…";
                AppendLog("FFmpeg не найден. Начата автоматическая загрузка: " + DownloadUrl);

                string[] installed = await Task.Factory.StartNew<string[]>(DownloadAndInstallTools);
                resolvedFfmpegPath = installed[0];
                resolvedFfprobePath = installed[1];
                AppendLog("FFmpeg установлен: " + Path.GetDirectoryName(resolvedFfmpegPath));
                SaveLog();
                statusLabel.Text = "FFmpeg установлен и готов к работе.";
                MessageBox.Show(this, "FFmpeg и FFprobe успешно загружены и настроены.",
                    "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА установки FFmpeg: " + ex.Message);
                SaveLog();
                MessageBox.Show(this,
                    "Не удалось автоматически загрузить или распаковать FFmpeg:\r\n\r\n" +
                    ex.Message + "\r\n\r\nМожно установить его самостоятельно со страницы:\r\n" +
                    RepositoryUrl,
                    "Ошибка установки FFmpeg", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                SetToolDownloadState(false);
                checkingTools = false;
            }
        }

        private string[] FindInstalledTools()
        {
            var directories = new List<string>();
            directories.Add(AppDirectory);
            if (PortableFolderMode)
            {
                string localFfmpeg = Path.Combine(AppDirectory, "ffmpeg.exe");
                string localFfprobe = Path.Combine(AppDirectory, "ffprobe.exe");
                return File.Exists(localFfmpeg) && File.Exists(localFfprobe)
                    ? new[] { localFfmpeg, localFfprobe }
                    : null;
            }
            directories.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AudioReplacer", "ffmpeg"));
            directories.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Links"));
            directories.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ffmpeg", "bin"));
            directories.Add(@"C:\ffmpeg\bin");

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            directories.AddRange(pathValue.Split(new[] { Path.PathSeparator },
                StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Trim('"')));

            foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string ffmpeg = Path.Combine(directory, "ffmpeg.exe");
                    string ffprobe = Path.Combine(directory, "ffprobe.exe");
                    if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                        return new[] { ffmpeg, ffprobe };
                }
                catch { }
            }

            // Winget часто хранит пакет в каталоге с версией, не добавляя его в PATH.
            string wingetPackages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            try
            {
                if (Directory.Exists(wingetPackages))
                {
                    foreach (string ffmpeg in Directory.GetFiles(
                        wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories))
                    {
                        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe");
                        if (File.Exists(ffprobe)) return new[] { ffmpeg, ffprobe };
                    }
                }
            }
            catch { }
            return null;
        }

        private string[] DownloadAndInstallTools()
        {
            string installDirectory = PortableFolderMode
                ? AppDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                               "AudioReplacer", "ffmpeg");
            Directory.CreateDirectory(installDirectory);

            string archive = Path.Combine(Path.GetTempPath(), "AudioReplacer-ffmpeg.zip");
            if (File.Exists(archive)) File.Delete(archive);

            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "AudioReplacer");
                client.DownloadFile(DownloadUrl, archive);
            }

            string installedFfmpeg = Path.Combine(installDirectory, "ffmpeg.exe");
            string installedFfprobe = Path.Combine(installDirectory, "ffprobe.exe");
            bool gotFfmpeg = false;
            bool gotFfprobe = false;
            using (var archiveStream = File.OpenRead(archive))
            using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string normalized = entry.FullName.Replace('\\', '/');
                    string destination = null;
                    if (normalized.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        destination = installedFfmpeg;
                        gotFfmpeg = true;
                    }
                    else if (normalized.EndsWith("/bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        destination = installedFfprobe;
                        gotFfprobe = true;
                    }
                    if (destination != null)
                    {
                        using (Stream input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.Create,
                                                          FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                    }
                }
            }
            try { File.Delete(archive); } catch { }

            if (!gotFfmpeg || !gotFfprobe ||
                !File.Exists(installedFfmpeg) || !File.Exists(installedFfprobe))
                throw new InvalidDataException("В загруженном архиве не найдены ffmpeg.exe и ffprobe.exe.");

            return new[] { installedFfmpeg, installedFfprobe };
        }

        private void SetToolDownloadState(bool busy)
        {
            browseButton.Enabled = !busy;
            outputBrowseButton.Enabled = !busy && !sameFolderBox.Checked;
            scanButton.Enabled = !busy;
            startButton.Enabled = !busy && pairs.Count > 0;
            cancelButton.Enabled = false;
        }

        private async Task<bool> ProcessPair(MediaPair pair)
        {
            string temporary = pair.Output + ".processing";
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                AppendLog("ВИДЕО: " + Path.GetFileName(pair.Video));
                AppendLog("АУДИО: " + Path.GetFileName(pair.Audio));

                string extension = Path.GetExtension(pair.Output).ToLowerInvariant();
                string audioOptions;
                if (extension == ".webm")
                    audioOptions = "-c:a libopus -b:a 192k";
                else if (extension == ".mp4" || extension == ".mov" || extension == ".m4v")
                    audioOptions = "-c:a aac -b:a 256k -ar 48000";
                else
                    audioOptions = "-c:a ac3 -b:a 640k -ar 48000";

                string formatOption = FormatOptionFor(extension);
                string extra = (extension == ".mp4" || extension == ".mov" || extension == ".m4v")
                    ? " -movflags +faststart" : "";
                string arguments = "-hide_banner -y -fflags +genpts -i " + Q(pair.Video) +
                    " -i " + Q(pair.Audio) +
                    " -map 0:v:0 -map 1:a:0 -map_metadata 0 -map_chapters 0" +
                    " -c:v copy " + audioOptions +
                    " -metadata:s:a:0 language=rus -metadata:s:a:0 title=\"External audio\"" +
                    " -disposition:a:0 default -avoid_negative_ts make_zero -max_interleave_delta 0" +
                    extra + formatOption + " " + Q(temporary);

                int exitCode = await RunProcess(FfmpegPath, arguments, true);
                if (exitCode != 0 || cancelRequested)
                {
                    AppendLog(cancelRequested ? "ОТМЕНЕНО." : "ОШИБКА FFmpeg, код " + exitCode + ".");
                    SafeDelete(temporary);
                    return false;
                }

                string probeArguments = "-v error -select_streams v:0 -show_entries stream=codec_type " +
                                        "-of default=nw=1:nk=1 " + Q(temporary);
                int probeCode = await RunProcess(FfprobePath, probeArguments, false);
                if (probeCode != 0)
                {
                    AppendLog("ОШИБКА: итоговый файл не прошёл проверку ffprobe.");
                    SafeDelete(temporary);
                    return false;
                }

                if (File.Exists(pair.Output)) File.Delete(pair.Output);
                File.Move(temporary, pair.Output);
                AppendLog("ГОТОВО: " + Path.GetFileName(pair.Output));
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА: " + ex.Message);
                SafeDelete(temporary);
                return false;
            }
            finally
            {
                SaveLog();
            }
        }

        private static string FormatOptionFor(string extension)
        {
            switch (extension)
            {
                case ".mkv": return " -f matroska";
                case ".mp4":
                case ".m4v": return " -f mp4";
                case ".mov": return " -f mov";
                case ".webm": return " -f webm";
                case ".avi": return " -f avi";
                case ".ts":
                case ".mts":
                case ".m2ts": return " -f mpegts";
                case ".flv": return " -f flv";
                case ".mpg":
                case ".mpeg":
                case ".vob": return " -f mpeg";
                default: return "";
            }
        }

        private Task<int> RunProcess(string fileName, string arguments, bool captureErrors)
        {
            var completion = new TaskCompletionSource<int>();
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = captureErrors,
                RedirectStandardOutput = false,
                WorkingDirectory = AppDirectory
            };
            process.EnableRaisingEvents = true;
            if (captureErrors)
            {
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrWhiteSpace(e.Data) &&
                        (e.Data.Contains("Error") || e.Data.Contains("Invalid") ||
                         e.Data.Contains("failed") || e.Data.Contains("Unable")))
                        BeginInvoke(new Action(delegate { AppendLog("FFmpeg: " + e.Data); }));
                };
            }
            process.Exited += delegate
            {
                int code = process.ExitCode;
                if (ReferenceEquals(currentProcess, process)) currentProcess = null;
                process.Dispose();
                completion.TrySetResult(code);
            };
            currentProcess = process;
            process.Start();
            if (captureErrors) process.BeginErrorReadLine();
            return completion.Task;
        }

        private void CancelClicked(object sender, EventArgs e)
        {
            cancelRequested = true;
            cancelButton.Enabled = false;
            statusLabel.Text = "Отмена…";
            TryKillCurrentProcess();
        }

        private void TryKillCurrentProcess()
        {
            try
            {
                var process = currentProcess;
                if (process != null && !process.HasExited) process.Kill();
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            browseButton.Enabled = !busy;
            scanButton.Enabled = !busy;
            folderBox.Enabled = !busy;
            sameFolderBox.Enabled = !busy;
            outputFolderBox.Enabled = !busy && !sameFolderBox.Checked;
            outputBrowseButton.Enabled = !busy && !sameFolderBox.Checked;
            overwriteBox.Enabled = !busy;
            startButton.Enabled = !busy && pairs.Count > 0;
            cancelButton.Enabled = busy;
        }

        private void AppendLog(string text)
        {
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text +
                              Environment.NewLine);
        }

        private void SaveLog()
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDirectory, "AudioReplacer.log.txt"),
                                  logBox.Text, new UTF8Encoding(true));
            }
            catch { }
        }

        private static string Q(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
