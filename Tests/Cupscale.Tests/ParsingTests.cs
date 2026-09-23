using System.Globalization;
using System.Threading;
using Cupscale;
using Cupscale.Main;
using Cupscale.OS;
using Xunit;

namespace CupscaleTests
{
    public class ParsingTests
    {
        [Theory]
        [InlineData("12.50%", 12.5f)]
        [InlineData("91,67%", 91.67f)]
        [InlineData("  100.00%  ", 100f)]
        [InlineData("0%", 0f)]
        public void TryParsePercent_ParsesDotDecimals_UnderCommaLocale(string line, float expected)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            try
            {
                Assert.True(NcnnUtils.TryParsePercent(line, out float percent));
                Assert.Equal(expected, percent, 3);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[0 NVIDIA GeForce]  queue-c=2")]
        [InlineData("abc%")]
        public void TryParsePercent_RejectsNonProgressLines(string line)
        {
            Assert.False(NcnnUtils.TryParsePercent(line, out _));
        }

        [Theory]
        [InlineData(23.98f, "24000/1001")]
        [InlineData(29.97f, "30000/1001")]
        [InlineData(59.94f, "60000/1001")]
        [InlineData(25f, "25")]
        [InlineData(24f, "24")]
        [InlineData(12.5f, "12.5")]
        public void FpsToArg_UsesExactNtscFractions(float fps, string expected)
        {
            Assert.Equal(expected, FFmpegCommands.FpsToArg(fps));
        }

        [Theory]
        [InlineData("GA102", "Ampere")]
        [InlineData("AD103-A", "Ada")]
        [InlineData("GB202", "Blackwell")]
        [InlineData("TU116", "Turing")]
        [InlineData("G", "Undetected")]
        [InlineData(null, "Undetected")]
        public void GetArch_MapsChipCodes(string code, string expected)     // Enum is internal
        {
            Assert.Equal(expected, NvApi.GetArch(code).ToString());
        }

        [Fact]
        public void AdvancedModelArg_UsesAtAndPipeSeparators()
        {
            AdvancedModelSelection.e1m1 = @"C:\models\a.pth";
            AdvancedModelSelection.e1m1i = 25;
            AdvancedModelSelection.e1m2 = @"C:\models\b.pth";
            AdvancedModelSelection.e1m2i = 75;
            AdvancedModelSelection.e2m1 = @"C:\models\c.pth";
            AdvancedModelSelection.e2m2 = null;
            AdvancedModelSelection.e3m1 = null;

            string arg = AdvancedModelSelection.GetArg();

            Assert.Equal(" \"C:\\models\\a.pth@25|C:\\models\\b.pth@75>C:\\models\\c.pth\"", arg);
        }
    }
}
