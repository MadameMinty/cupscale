using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        public static bool IsInstalled()
        {
            return File.Exists(GetEmbedPyPath());
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
            string pyDir = Path.Combine(Installer.path, "py");
            if (Directory.Exists(pyDir))
            {
                await RunCompact(pyDir);
                Program.ShowMessage("Compression Complete!", "Compressor");
            }
            else
            {
                Program.ShowMessage("Python Runtime not found!", "Compressor");
            }
        }

        static Task<bool> pendingInstall;

        /// <summary>
        /// True if the embedded runtime is disabled (system Python is used) or installed. Otherwise offers to download it.
        /// Concurrent callers share one prompt and download.
        /// </summary>
        public static Task<bool> EnsureAvailable()
        {
            if (!IsEnabled() || IsInstalled())
                return Task.FromResult(true);

            if (pendingInstall == null || pendingInstall.IsCompleted)
                pendingInstall = PromptAndInstall();

            return pendingInstall;
        }

        static async Task<bool> PromptAndInstall()
        {
            DialogResult answer = MessageBox.Show(Program.mainForm, "PyTorch upscaling and model conversion need the Python runtime, which is not installed.\n\n" +
                "Download it now? (1.6 GB download, 3.2 GB installed)\n\nAlternatively, select a system Python in the Settings.", "Python Runtime", MessageBoxButtons.YesNo);

            if (answer != DialogResult.Yes)
                return false;

            try
            {
                await Install();
                return true;
            }
            catch (Exception e)
            {
                Logger.Log($"Python runtime installation failed: {e}");
                Program.mainForm.SetProgress(0, "Python runtime installation failed.");
                Program.ShowMessage($"Installing the Python runtime failed: {e.Message}\n\nYou can also extract py.7z from\n{GetRuntimeUrl()}\ninto {Installer.path} manually.", "Error");
                return false;
            }
        }

        static async Task Install()
        {
            string archive = Path.Combine(Installer.path, "py-download.7z");
            string pyDir = Path.Combine(Installer.path, "py");
            string url = GetRuntimeUrl();

            if (IoUtils.GetDiskSpace(Paths.GetDataPath()) < 5000)
                throw new IOException($"Not enough disk space on {Path.GetPathRoot(Paths.GetDataPath())} (5 GB needed).");

            Logger.Log($"Downloading embedded Python from '{url}'");

            try
            {
                Directory.CreateDirectory(Installer.path);
                await IoUtils.DownloadFileAsync(url, archive, (done, total) => Program.mainForm.SetProgress(total > 0 ? done * 100f / total : -1f,
                    $"Downloading Python runtime... {done / 1024 / 1024} / {(total > 0 ? $"{total / 1024 / 1024}" : "?")} MB"));

                Program.mainForm.SetProgress(100, "Extracting Python runtime...");
                if (Directory.Exists(pyDir))
                    Directory.Delete(pyDir, true);
                await Task.Run(() => SevenZip.Extract(archive, Installer.path));

                if (!IsInstalled())
                    throw new IOException("The downloaded archive does not contain py\\python.exe.");

                await Init();
                Program.mainForm.SetProgress(0, "Installed Python runtime.");
                Logger.Log("Installed embedded Python runtime.");
            }
            finally
            {
                IoUtils.TryDeleteIfExists(archive);
            }
        }

        /// <summary>
        /// Config "pythonRuntimeUrl" (default: this repo's release), or "pythonRuntimeUrlLegacy" if set and there is no Turing or newer GPU
        /// (the default runtime's CUDA needs one; CPU upscaling and NCNN conversion work anywhere).
        /// </summary>
        static string GetRuntimeUrl()
        {
            string legacy = NvApi.HasTuringOrNewer() ? "" : Config.Get("pythonRuntimeUrlLegacy");
            string url = string.IsNullOrWhiteSpace(legacy) ? Config.Get("pythonRuntimeUrl") : legacy;
            return string.IsNullOrWhiteSpace(url) ? Paths.pythonRuntimeReleaseUrl : url.Trim();
        }

        static async Task RunCompact (string pyDir)
        {
            bool stayOpen = Config.GetInt("cmdDebugMode") == 2;
            string opt = stayOpen ? "/K" : "/C";
            Process compact = OsUtils.NewProcess(false);
            compact.StartInfo.Arguments = $"{opt} compact /C /S:{pyDir.Wrap()}";
            OsUtils.StartTracked(compact);

            await Task.Run(() =>
            {
                Thread.Sleep(100);  // <-- ugly hack https://stackoverflow.com/a/1016863/14274419
                SetWindowText(compact.MainWindowHandle, "Compressing Python installation... Do not close this window!");
                compact.WaitForExit();
            });
        }

        [DllImport("user32.dll")]
        static extern int SetWindowText(IntPtr hWnd, string text);
    }
}
