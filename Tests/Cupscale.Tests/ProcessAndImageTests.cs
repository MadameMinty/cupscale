using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cupscale.ImageUtils;
using Cupscale.OS;
using ImageMagick;
using Xunit;

namespace CupscaleTests
{
    public class ProcessAndImageTests
    {
        [Fact]
        public void RunAndGetOutput_DoesNotDeadlockOnLargeStderr()
        {
            Process proc = OsUtils.NewProcess(true);
            proc.StartInfo.Arguments = "/C for /L %i in (1,1,6000) do @echo stderr line %i 1>&2";

            var run = Task.Run(() => OsUtils.RunAndGetOutput(proc));

            Assert.True(run.Wait(TimeSpan.FromSeconds(60)), "Process output read deadlocked");
            Assert.Contains("stderr line 6000", run.Result);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

        [Fact]
        public void StartTracked_AssignsProcessToJob()
        {
            Process proc = OsUtils.NewProcess(true);
            proc.StartInfo.Arguments = "/C ping -n 3 127.0.0.1 >nul";
            OsUtils.StartTracked(proc);

            try
            {
                Assert.True(IsProcessInJob(proc.Handle, IntPtr.Zero, out bool inJob));
                Assert.True(inJob);
            }
            finally
            {
                if (!proc.HasExited) proc.Kill();
            }
        }

        [Fact]
        public void SevenZip_ExtractsArchive()
        {
            Cupscale.IO.Paths.Init();
            string dir = Path.Combine(Path.GetTempPath(), $"cupscale-7z-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            try
            {
                string src = Path.Combine(dir, "zażółć.txt");
                File.WriteAllText(src, "hello");
                string archive = Path.Combine(dir, "a.7z");
                string exe = Path.Combine(Cupscale.IO.Paths.binPath, "7za.exe");
                Directory.CreateDirectory(Cupscale.IO.Paths.binPath);
                File.WriteAllBytes(exe, Cupscale.Properties.Resources.x64_7za);
                Process.Start(new ProcessStartInfo(exe, $"a \"{archive}\" \"{src}\"") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit();

                Cupscale.IO.SevenZip.Extract(archive, Path.Combine(dir, "out"));

                Assert.Equal("hello", File.ReadAllText(Path.Combine(dir, "out", "zażółć.txt")));
                Assert.Throws<IOException>(() => Cupscale.IO.SevenZip.Extract(Path.Combine(dir, "missing.7z"), dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MozJpeg_KeepsChannelOrder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"cupscale-moz-{Guid.NewGuid():N}.jpg");

            try
            {
                using (var red = new MagickImage(MagickColors.Red, 32, 32))
                    MozJpeg.Encode(red, path, 95);

                using (var result = new MagickImage(path))
                {
                    var pixel = result.GetPixels().GetPixel(16, 16).ToColor();
                    Assert.True(pixel.R > 60000, $"R={pixel.R}");
                    Assert.True(pixel.B < 5000, $"B={pixel.B}");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IsLowColorDepth_FlagsGrayscaleNotTrueColor()
        {
            using (var color = new MagickImage("plasma:", 64, 64))
            {
                Assert.False(ImgUtils.IsLowColorDepth(color));

                color.ColorType = ColorType.Grayscale;
                Assert.True(ImgUtils.IsLowColorDepth(color));
            }
        }
    }
}
