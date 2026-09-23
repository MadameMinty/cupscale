using Cupscale.Forms;
using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Win32Interop.Structs;

namespace Cupscale.OS
{
    class EmbeddedPython
    {
        public static string GetPyCmd(bool cancelIfFileMissing = false)
        {
            if (IsEnabled())
            {
                if (File.Exists(GetEmbedPyPath()))
                {
                    return GetEmbedPyPath().Wrap();
                }
                else
                {
                    Program.Cancel($"Can't find embedded python executable! Are you sure it's installed?");
                    return "";
                }
            }
                
            return "python";
        }

        public static string GetEmbedPyPath()
        {
            return Path.Combine(Installer.path, "py", "python.exe");
        }

        public static bool IsEnabled()
        {
            return Config.GetInt("esrganPytorchPythonRuntime") == 1;
        }

        public static async Task Init()
        {
            if (!IsEnabled())
                return;

            string shippedPath = Installer.path;

            if (Directory.Exists(Path.Combine(shippedPath, "py")))
            {
                IoUtils.TryDeleteIfExists(Path.Combine(shippedPath, "py", "utils"));
                await IoUtils.CopyDir(Path.Combine(shippedPath, "utils"), Path.Combine(shippedPath, "py", "utils"));
            }
        }

        public static async Task PublicRunCompact()
        {
            extractPath = Path.Combine(Installer.path, "py");
            if (Directory.Exists(extractPath))
            {
                await RunCompact();
                Program.ShowMessage("Compression Complete!", "Compressor");
            }
            else
            {
                Program.ShowMessage("Python Runtime not found!", "Compressor");
            }
        }

        static TextBox logBox;
        static HTAlt.WinForms.HTButton runBtn;
        static string downloadPath;
        static string extractPath;

        public static async Task Download(TextBox logTextbox, HTAlt.WinForms.HTButton runButton)
        {
            logBox = logTextbox;
            runBtn = runButton;
            runBtn.Enabled = false;
            Print("Initializing...");
            downloadPath = Path.Combine(Installer.path, "py.7z");
            extractPath = Path.Combine(Installer.path, "py");
            isExtracting = false;

            await Task.Delay(10);

            Print("Checking disk space before installation...");
            float diskSpaceMb = IoUtils.GetDiskSpace(Paths.GetDataPath());
            Print($"Available disk space on the current drive: {diskSpaceMb} MB.");

            if (diskSpaceMb < 5000)
            {
                Print("Not enough disk space on the current drive!");
                runBtn.Enabled = true;
                return;
            }

            IoUtils.DeleteIfExists(downloadPath);
            await Task.Delay(10);

            string url = GetRuntimeUrl();
            Logger.Log($"Downloading embedded Python from '{url}'");

            Print("Downloading compressed python runtime...");
            var client = new WebClient();
            client.DownloadProgressChanged += DownloadProgressChanged;
            client.DownloadFileCompleted += DoneDownloading;
            client.DownloadFileAsync(new Uri(url), downloadPath);
        }

        /// <summary> Config "pythonRuntimeUrl" (full URL) overrides the default server package. </summary>
        static string GetRuntimeUrl()
        {
            string custom = Config.Get("pythonRuntimeUrl");

            if (!string.IsNullOrWhiteSpace(custom))
                return custom.Trim();

            string srv = Servers.closestServer.GetUrl();
            return Path.Combine(srv, NvApi.HasAmpereOrNewer() ? Paths.pythonAmperePath : Paths.pythonTuringPath).Replace("\\", "/");
        }

        static async void DoneDownloading (object sender, AsyncCompletedEventArgs e)
        {
            ((WebClient)sender).Dispose();

            if (e.Cancelled || e.Error != null)
            {
                Print($"Download failed: {e.Error?.Message ?? "Cancelled"}");
                Logger.Log($"Embedded Python download failed: {e.Error}");
                IoUtils.TryDeleteIfExists(downloadPath);
                runBtn.Enabled = true;
                return;
            }

            try
            {
                await Install();
            }
            catch (Exception ex)
            {
                Print($"Installation failed: {ex.Message}");
                Logger.Log($"Embedded Python installation failed: {ex}");
            }
            finally
            {
                isExtracting = false;
                runBtn.Enabled = true;
            }
        }

        static async Task Install ()
        {
            Print("Done downloading!");
            Print("Extracting...");

            if (Directory.Exists(extractPath))
                IoUtils.ClearDir(extractPath);

            List<Task> tasks = new List<Task>();
            isExtracting = true;
            tasks.Add(CheckDownloadedFileSizeAsync());
            tasks.Add(ExtractAsync());
            await Task.WhenAll(tasks);

            if(Directory.Exists(Path.Combine(Installer.path, "FlowframesData")))
            {
                DirectoryInfo parentDir = new DirectoryInfo(Path.Combine(Installer.path, "FlowframesData", "pkgs"));
                DirectoryInfo dir = parentDir.GetDirectories().First();
                dir.MoveTo(extractPath);
            }

            Print("Done extracting files.");
            MsgBox msg = Program.ShowMessage("The Python files will now be compressed to reduce the amount of storage space needed " +
                "by about 40%.\nThis can take a few minutes.", "Message");
            while (DialogQueue.IsOpen(msg)) await Task.Delay(50);
            Print("Compressing files...");
            await RunCompact();
            Print("Done!");
            Config.Set("esrganPytorchPythonRuntime", "1");
            await Init();
            MsgBox msg2 = Program.ShowMessage("Installed embedded Python runtime and enabled it!\nIf you want to disable it, you can do so in the settings.", "Message");
            while (DialogQueue.IsOpen(msg2)) await Task.Delay(50);
        }

        static async Task RunCompact ()
        {
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;
            string opt = stayOpen ? "/K" : "/C";
            Process compact = OsUtils.NewProcess(false);
            compact.StartInfo.Arguments = $"{opt} compact /C /S:{extractPath.Wrap()}";
            OsUtils.StartTracked(compact);

            await Task.Run(() =>
            {
                Thread.Sleep(100);  // <-- ugly hack https://stackoverflow.com/a/1016863/14274419
                SetWindowText(compact.MainWindowHandle, "Compressing Python installation... Do not close this window!");
                compact.WaitForExit();
            });
        }

        static int lastPercentage = 0;
        static void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            int diff = Math.Abs(lastPercentage - e.ProgressPercentage);
            if(diff >= 1)
            {
                lastPercentage = e.ProgressPercentage;
                Print("Downloading compressed python runtime - " + e.ProgressPercentage + "%", true);
            }
        }

        static bool isExtracting;
        static async Task ExtractAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string path7za = Path.Combine(Installer.path, "7za.exe");

                    if (!File.Exists(path7za))     // Not shipped in bin; bundled as resource
                        File.WriteAllBytes(path7za, Properties.Resources.x64_7za);

                    SevenZipNET.SevenZipExtractor.Path7za = path7za;
                    SevenZipNET.SevenZipExtractor extractor = new SevenZipNET.SevenZipExtractor(downloadPath);
                    extractor.ExtractAll(Installer.path, true, true);
                    File.Delete(downloadPath);
                }
                finally
                {
                    isExtracting = false;
                }
            });
        }

        static async Task CheckDownloadedFileSizeAsync()
        {
            await Task.Run(async () =>
            {
                while (isExtracting)
                {
                    try
                    {
                        if (Directory.Exists(extractPath))
                        {
                            long currentBytes = IoUtils.GetDirSize(extractPath, true);
                            int mb = (int)(currentBytes / 1024f / 1000f);
                            Print("Installed " + mb + " MB of 2500", true);
                        }
                    }
                    catch (Exception e)
                    {
                        Print("Error: " + e.Message);
                    }
                    await Task.Delay(3000);
                }
            });
        }

        [DllImport("user32.dll")]
        static extern int SetWindowText(IntPtr hWnd, string text);

        static void Print(string s, bool replaceLastLine = false)
        {
            if (logBox.InvokeRequired)
            {
                logBox.BeginInvoke((Action)(() => Print(s, replaceLastLine)));
                return;
            }

            if (replaceLastLine)
            {
                logBox.Text = logBox.Text.Remove(logBox.Text.LastIndexOf(Environment.NewLine));
            }
            if(string.IsNullOrWhiteSpace(logBox.Text))
                logBox.Text += s;
            else
                logBox.Text += Environment.NewLine + s;
            logBox.SelectionStart = logBox.Text.Length;
            logBox.ScrollToCaret();
        }
    }
}
