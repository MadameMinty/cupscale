using Cupscale.OS;
using Cupscale.Properties;
using Cupscale.UI;
using System;
using System.IO;

namespace Cupscale.IO
{
    /// <summary> Extracts archives with the bundled x64 7za.exe. </summary>
    static class SevenZip
    {
        static readonly object writeLock = new object();

        static string GetExePath()
        {
            string path = Path.Combine(Paths.binPath, "7za.exe");

            lock (writeLock)
            {
                if (!File.Exists(path) || new FileInfo(path).Length != Resources.x64_7za.Length)
                {
                    Directory.CreateDirectory(Paths.binPath);
                    File.WriteAllBytes(path, Resources.x64_7za);
                }
            }

            return path;
        }

        /// <summary> Extracts archivePath into outDir (overwriting). Throws on failure. </summary>
        public static void Extract(string archivePath, string outDir)
        {
            var proc = OsUtils.NewProcess(true, GetExePath());
            proc.StartInfo.Arguments = $"x {archivePath.Wrap()} -o{outDir.Wrap()} -y -bso0 -bsp0";
            string output = OsUtils.RunAndGetOutput(proc, out int exitCode);

            if (exitCode != 0)
                throw new IOException($"7za failed with exit code {exitCode}: {output.Trim()}");
        }
    }
}
