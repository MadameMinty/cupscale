using System.Collections.Generic;
using System.Linq;
using Cupscale;
using Cupscale.OS;
using Xunit;

namespace CupscaleTests
{
    public class VideoEncoderTests
    {
        static readonly HashSet<string> essentials = new HashSet<string> { "libx264", "libx265", "libaom-av1", "h264_nvenc", "hevc_nvenc", "av1_nvenc" };
        static readonly HashSet<string> full = new HashSet<string>(essentials) { "libsvtav1" };

        [Theory]
        [InlineData("H.264 (CPU)", "Normal", "-c:v libx264 -preset slow -crf 18")]
        [InlineData("H.264 (CPU)", "High", "-c:v libx264 -preset veryslow -qp 0")]
        [InlineData("H.265 (CPU)", "High", "-c:v libx265 -preset slow -x265-params lossless=1")]
        [InlineData("H.264 (NVENC)", "High", "-c:v h264_nvenc -preset p7 -tune lossless")]
        [InlineData("AV1 (NVENC)", "High", "-c:v av1_nvenc -preset p7 -rc vbr -cq 16 -b:v 0")]
        [InlineData("AV1 (NVENC)", "Low", "-c:v av1_nvenc -preset p7 -rc vbr -cq 34 -b:v 0")]
        public void GetArgs_MapsEncoderAndQuality(string encoder, string quality, string expected)
        {
            Assert.Equal(expected, VideoEncoders.GetArgs(encoder, quality, "", essentials));
        }

        [Fact]
        public void Av1Cpu_PrefersSvtAndFallsBackToAom()
        {
            Assert.Equal("-c:v libsvtav1 -preset 6 -svtav1-params lossless=1", VideoEncoders.GetArgs("AV1 (CPU)", "High", "", full));
            Assert.Equal("-c:v libaom-av1 -cpu-used 6 -row-mt 1 -crf 26 -b:v 0", VideoEncoders.GetArgs("AV1 (CPU)", "Normal", "", essentials));
        }

        [Fact]
        public void Custom_PassesArgsThrough_AndUnknownFallsBackToH264()
        {
            Assert.Equal("-c:v libx264 -crf 12", VideoEncoders.GetArgs(VideoEncoders.Custom, "Normal", "  -c:v libx264 -crf 12 ", essentials));
            Assert.Equal("-c:v libx264 -preset slow -crf 18", VideoEncoders.GetArgs(VideoEncoders.Custom, "Normal", "", essentials));
            Assert.Equal("-c:v libx264 -preset slow -crf 18", VideoEncoders.GetArgs("H.266 (CPU)", "Bogus", "", essentials));
        }

        [Fact]
        public void Available_RequiresGpuForNvenc_AndAdaForAv1Nvenc()
        {
            var none = VideoEncoders.Available(essentials, new NvApi.Architecture[0]).Select(e => e.Name).ToList();
            var turing = VideoEncoders.Available(essentials, new[] { NvApi.Architecture.Turing }).Select(e => e.Name).ToList();
            var ada = VideoEncoders.Available(essentials, new[] { NvApi.Architecture.Ada }).Select(e => e.Name).ToList();

            Assert.Equal(new[] { "H.264 (CPU)", "H.265 (CPU)", "AV1 (CPU)" }, none);
            Assert.Contains("H.265 (NVENC)", turing);
            Assert.DoesNotContain("AV1 (NVENC)", turing);
            Assert.Contains("AV1 (NVENC)", ada);
        }

        [Fact]
        public void ParseEncoderList_ReadsVideoEncodersOnly()
        {
            string output = "Encoders:\n V..... = Video\n ------\n V....D libx264              libx264 H.264\n A....D aac                  AAC\n V....D h264_nvenc           NVIDIA NVENC H.264 encoder\n";
            var names = VideoEncoders.ParseEncoderList(output);

            Assert.Contains("libx264", names);
            Assert.Contains("h264_nvenc", names);
            Assert.DoesNotContain("aac", names);
            Assert.DoesNotContain("=", names);
        }
    }
}
