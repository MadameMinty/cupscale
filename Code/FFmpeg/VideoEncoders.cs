using Cupscale.OS;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cupscale
{
    /// <summary> MP4 encoder presets. Quality levels are matched by PSNR across codecs; Lossless is exact where the encoder supports it. </summary>
    static class VideoEncoders
    {
        public enum Quality { Normal, High, Lossless }

        public const string Custom = "Custom";

        public class Encoder
        {
            public string Name;
            public string[] FfmpegEncoders;      // First one available in the ffmpeg build is used
            public NvApi.Architecture? MinNvidiaArch;
            public Func<string, Quality, string> Args;     // (ffmpeg encoder, quality) => codec args
        }

        static string X264(string e, Quality q) => q switch
        {
            Quality.Normal => "-c:v libx264 -preset slow -crf 22",
            Quality.High => "-c:v libx264 -preset slow -crf 18",
            _ => "-c:v libx264 -preset veryslow -qp 0",
        };

        static string X265(string e, Quality q) => q switch
        {
            Quality.Normal => "-c:v libx265 -preset slow -crf 24",
            Quality.High => "-c:v libx265 -preset slow -crf 20",
            _ => "-c:v libx265 -preset slow -x265-params lossless=1",
        };

        static string Av1Cpu(string e, Quality q)
        {
            string c = e == "libsvtav1" ? "-c:v libsvtav1 -preset 6" : "-c:v libaom-av1 -cpu-used 6 -row-mt 1";
            string lossless = e == "libsvtav1" ? "-svtav1-params lossless=1" : "-aom-params lossless=1";
            string rate = e == "libsvtav1" ? "" : " -b:v 0";
            return q switch
            {
                Quality.Normal => $"{c} -crf 34{rate}",
                Quality.High => $"{c} -crf 26{rate}",
                _ => $"{c} {lossless}",
            };
        }

        static string Nvenc(string e, Quality q, int cqNormal, int cqHigh, bool hasLossless)
        {
            string c = $"-c:v {e} -preset p7";
            if (q == Quality.Lossless)
                return hasLossless ? $"{c} -tune lossless" : $"{c} -rc vbr -cq 16 -b:v 0";
            return $"{c} -rc vbr -cq {(q == Quality.Normal ? cqNormal : cqHigh)} -b:v 0";
        }

        public static readonly List<Encoder> All = new List<Encoder>
        {
            new Encoder { Name = "H.264 (CPU)", FfmpegEncoders = new[] { "libx264" }, Args = X264 },
            new Encoder { Name = "H.265 (CPU)", FfmpegEncoders = new[] { "libx265" }, Args = X265 },
            new Encoder { Name = "AV1 (CPU)", FfmpegEncoders = new[] { "libsvtav1", "libaom-av1" }, Args = Av1Cpu },
            new Encoder { Name = "H.264 (NVENC)", FfmpegEncoders = new[] { "h264_nvenc" }, MinNvidiaArch = NvApi.Architecture.Maxwell, Args = (e, q) => Nvenc(e, q, 25, 21, true) },
            new Encoder { Name = "H.265 (NVENC)", FfmpegEncoders = new[] { "hevc_nvenc" }, MinNvidiaArch = NvApi.Architecture.Maxwell, Args = (e, q) => Nvenc(e, q, 27, 23, true) },
            new Encoder { Name = "AV1 (NVENC)", FfmpegEncoders = new[] { "av1_nvenc" }, MinNvidiaArch = NvApi.Architecture.Ada, Args = (e, q) => Nvenc(e, q, 34, 28, false) },
        };

        public const string DefaultName = "H.264 (CPU)";

        /// <summary> First encoder of e from the ffmpeg build, or null if none is compiled in. </summary>
        static string Pick(Encoder e, ICollection<string> ffmpegEncoders) => e.FfmpegEncoders.FirstOrDefault(ffmpegEncoders.Contains);

        /// <summary> Encoders usable with this ffmpeg build and GPU. </summary>
        public static List<Encoder> Available(ICollection<string> ffmpegEncoders, IEnumerable<NvApi.Architecture> nvidiaArchs)
        {
            var archs = nvidiaArchs.ToList();
            return All.Where(e => Pick(e, ffmpegEncoders) != null
                && (e.MinNvidiaArch == null || archs.Any(a => a >= e.MinNvidiaArch || a == NvApi.Architecture.Undetected))).ToList();
        }

        /// <summary> Codec arguments for the selected encoder/quality. Unknown or unavailable encoders fall back to H.264 (CPU). </summary>
        public static string GetArgs(string encoderName, string quality, string customArgs, ICollection<string> ffmpegEncoders)
        {
            if (encoderName == Custom && !string.IsNullOrWhiteSpace(customArgs))
                return customArgs.Trim();

            Encoder encoder = All.FirstOrDefault(e => e.Name == encoderName && Pick(e, ffmpegEncoders) != null) ?? All.First(e => e.Name == DefaultName);
            Quality q = Enum.TryParse(quality, true, out Quality parsed) ? parsed : Quality.High;
            return encoder.Args(Pick(encoder, ffmpegEncoders) ?? encoder.FfmpegEncoders[0], q);
        }

        /// <summary> Parses "ffmpeg -encoders" output into encoder names. </summary>
        public static HashSet<string> ParseEncoderList(string ffmpegOutput)
        {
            var names = new HashSet<string>();

            foreach (string line in ffmpegOutput.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V' && parts[1] != "=")   // Skip the legend line
                    names.Add(parts[1]);
            }

            return names;
        }

        static HashSet<string> cachedEncoders;
        static string cachedFor;

        /// <summary> Encoders compiled into the configured ffmpeg (cached per ffmpeg path). </summary>
        public static HashSet<string> GetFfmpegEncoders()
        {
            string exe = FFmpeg.GetExePath();

            if (cachedEncoders == null || cachedFor != exe)
            {
                cachedEncoders = ParseEncoderList(FFmpeg.RunAndGetOutput("-encoders"));
                cachedFor = exe;
            }

            return cachedEncoders;
        }
    }
}
