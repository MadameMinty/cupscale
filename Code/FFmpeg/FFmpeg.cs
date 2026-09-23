using Cupscale.IO;
using Cupscale.OS;
using Cupscale.UI;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Cupscale
{
    class FFmpeg
    {
        public static string lastOutputFfmpeg;

        /// <summary> Setting "ffmpegPath" if set, else bundled bin\ffmpeg.exe, else ffmpeg from PATH. </summary>
        public static string GetExePath()
        {
            string custom = Config.Get("ffmpegPath")?.Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            string bundled = System.IO.Path.Combine(Paths.binPath, "ffmpeg.exe");
            return System.IO.File.Exists(bundled) ? bundled : "ffmpeg";
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