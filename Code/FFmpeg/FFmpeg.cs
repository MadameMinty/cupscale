using Cupscale.IO;
using Cupscale.OS;
using Cupscale.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cupscale
{
    class FFmpeg
    {
        public static string lastOutputFfmpeg;

        const string downloadVersion = "9.0.2";    // gyan.dev essentials build, via its GitHub mirror
        static readonly string downloadUrl = $"https://github.com/GyanD/codexffmpeg/releases/download/{downloadVersion}/ffmpeg-{downloadVersion}-essentials_build.7z";

        static string BundledPath => Path.Combine(Paths.binPath, "ffmpeg.exe");

        /// <summary> Setting "ffmpegPath" if set, else bundled bin\ffmpeg.exe, else ffmpeg from PATH. </summary>
        public static string GetExePath()
        {
            return FindExe() ?? "ffmpeg";
        }

        /// <summary> Like GetExePath, but null if no ffmpeg exists. </summary>
        public static string FindExe()
        {
            string custom = Config.Get("ffmpegPath")?.Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(custom))
                return File.Exists(custom) ? custom : FindOnPath(custom);

            return File.Exists(BundledPath) ? BundledPath : FindOnPath("ffmpeg.exe");
        }

        internal static string FindOnPath(string name, string pathVar = null)
        {
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";

            return (pathVar ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => { try { return Path.Combine(dir.Trim().Trim('"'), name); } catch { return null; } })
                .FirstOrDefault(File.Exists);
        }

        /// <summary> Returns true if ffmpeg exists, otherwise offers to download it into bin. </summary>
        public static async Task<bool> EnsureAvailable()
        {
            if (FindExe() != null)
                return true;

            DialogResult answer = MessageBox.Show("This needs FFmpeg, which is not installed.\n\nDownload it now? (about 35 MB, gyan.dev build)\n\n" +
                "Alternatively, set a custom FFmpeg path in the Settings.", "FFmpeg", MessageBoxButtons.YesNo);

            if (answer != DialogResult.Yes)
                return false;

            try
            {
                await Download();
                return true;
            }
            catch (Exception e)
            {
                Logger.Log($"FFmpeg download failed: {e}");
                Program.mainForm.SetProgress(0, "FFmpeg download failed.");
                Program.ShowMessage($"Downloading FFmpeg failed: {e.Message}\n\nYou can set a custom FFmpeg path in the Settings instead.", "Error");
                return false;
            }
        }

        static async Task Download()
        {
            string archive = Path.Combine(Paths.binPath, "ffmpeg-download.7z");
            string tmpDir = Path.Combine(Paths.binPath, "ffmpeg-download");
            Logger.Log($"Downloading FFmpeg from {downloadUrl}");

            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? -1;

                    using (Stream src = await response.Content.ReadAsStreamAsync())
                    using (FileStream dst = File.Create(archive))
                    {
                        byte[] buffer = new byte[1 << 16];
                        long done = 0;
                        int read;

                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await dst.WriteAsync(buffer, 0, read);
                            done += read;
                            float percent = total > 0 ? done * 100f / total : -1f;
                            Program.mainForm.SetProgress(percent, $"Downloading FFmpeg... {done / 1024 / 1024} MB");
                        }
                    }
                }

                Program.mainForm.SetProgress(100, "Extracting FFmpeg...");
                await Task.Run(() => SevenZip.ExtractFile(archive, "ffmpeg.exe", tmpDir));
                File.Move(Path.Combine(tmpDir, "ffmpeg.exe"), BundledPath, true);
                Program.mainForm.SetProgress(0, "Installed FFmpeg.");
            }
            finally
            {
                IoUtils.TryDeleteIfExists(archive);
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            }
        }

        public static async Task Run(string args)
        {
            lastOutputFfmpeg = "";
            Process ffmpeg = OsUtils.NewProcess(true);
            ffmpeg.StartInfo.Arguments = $"/C cd /D {Paths.binPath.Wrap()} & {GetExePath().Wrap()} -hide_banner -loglevel warning -y -stats {args}";
            Logger.Log("Running ffmpeg...");
            Logger.Log("cmd.exe " + ffmpeg.StartInfo.Arguments);
            ffmpeg.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            ffmpeg.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            OsUtils.StartTracked(ffmpeg);
            ffmpeg.BeginOutputReadLine();
            ffmpeg.BeginErrorReadLine();

            while (!ffmpeg.HasExited)
                await Task.Delay(100);

            Logger.Log("Done running ffmpeg.");
        }

        static void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            string line = outLine.Data;
            if (outLine == null || line == null) return;
            lastOutputFfmpeg = lastOutputFfmpeg + line + "\n";
            Logger.Log("[FFmpeg] " + line);

            if (line.ToLower().Contains("error"))
                Program.ShowMessage("FFmpeg Error:\n\n" + line);
        }

        public static async Task RunGifski (string args)
        {
            Process ffmpeg = OsUtils.NewProcess(true);
            ffmpeg.StartInfo.Arguments = $"/C cd /D {Paths.binPath.Wrap()} & gifski.exe {args}";
            Logger.Log("Running gifski...");
            Logger.Log("cmd.exe " + ffmpeg.StartInfo.Arguments);
            ffmpeg.OutputDataReceived += new DataReceivedEventHandler(OutputHandlerGifski);
            ffmpeg.ErrorDataReceived += new DataReceivedEventHandler(OutputHandlerGifski);
            OsUtils.StartTracked(ffmpeg);
            ffmpeg.BeginOutputReadLine();
            ffmpeg.BeginErrorReadLine();

            while (!ffmpeg.HasExited)
                await Task.Delay(100);

            Logger.Log("Done running gifski.");
        }

        static void OutputHandlerGifski (object sendingProcess, DataReceivedEventArgs outLine)
        {
            string line = outLine.Data;
            if (outLine == null || line == null) return;
            Logger.Log("[gifski] " + line);

            if (line.ToLower().Contains("error"))
                Program.ShowMessage("Gifski Error:\n\n" + line);
        }

        public static string RunAndGetOutput (string args)
        {
            Process ffmpeg = OsUtils.NewProcess(true);
            ffmpeg.StartInfo.Arguments = $"/C cd /D {Paths.binPath.Wrap()} & {GetExePath().Wrap()} -hide_banner -y -stats {args}";
            return OsUtils.RunAndGetOutput(ffmpeg);
        }
    }
}