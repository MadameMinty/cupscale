using System;
using System.IO;
using System.Threading.Tasks;
using Cupscale;
using ImageMagick;
using Xunit;

namespace CupscaleTests
{
    public class JxlTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "cupscale-jxl-" + Guid.NewGuid().ToString("N"));

        public JxlTests()
        {
            Directory.CreateDirectory(dir);
            Config.Init();
        }

        public void Dispose() => Directory.Delete(dir, true);

        static MagickImage Sample() => new MagickImage("gradient:red-blue", 64, 64);    // Deterministic, unlike plasma:

        string Encode(bool lossless, int quality)
        {
            Config.Set("jxlLossless", lossless.ToString());
            Config.Set("jxlQ", quality.ToString());
            string path = Path.Combine(dir, $"out-{lossless}-{quality}.jxl");

            using (var img = Sample())
            {
                ImageProcessing.ApplyJxlSettings(img);
                img.Write(path);
            }

            return path;
        }

        [Fact]
        public void Lossless_RoundTripIsExact()
        {
            string path = Encode(true, 50);

            using (var original = Sample())
            using (var decoded = new MagickImage(path))
                Assert.Equal(0, original.Compare(decoded, ErrorMetric.Absolute));

            Assert.Equal(MagickFormat.Jxl, new MagickImageInfo(path).Format);
        }

        [Fact]
        public void Lossy_IsSmallerThanLossless()
        {
            long lossless = new FileInfo(Encode(true, 90)).Length;
            long lossy = new FileInfo(Encode(false, 60)).Length;

            Assert.True(lossy < lossless, $"lossy {lossy} >= lossless {lossless}");
        }

        [Fact]
        public async Task AiIncompatibleImages_AreRewrittenAsPngInPlace()
        {
            using (var img = new MagickImage(MagickColors.Red, 16, 16))
            {
                img.Write(Path.Combine(dir, "a.jxl"), MagickFormat.Jxl);
                img.Write(Path.Combine(dir, "b.tga"), MagickFormat.Tga);
                img.Write(Path.Combine(dir, "c.png"), MagickFormat.Png);
            }

            byte[] pngBefore = File.ReadAllBytes(Path.Combine(dir, "c.png"));

            await ImageProcessing.ConvertAiIncompatibleImages(dir);

            Assert.Equal(MagickFormat.Png, new MagickImageInfo(Path.Combine(dir, "a.jxl")).Format);
            Assert.Equal(MagickFormat.Png, new MagickImageInfo(Path.Combine(dir, "b.tga")).Format);
            Assert.Equal(pngBefore, File.ReadAllBytes(Path.Combine(dir, "c.png")));
        }
    }
}
