using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cupscale.Cupscale
{
    class PostProcessingQueue
    {
        public static Queue<string> outputFileQueue = new Queue<string>();
        public static List<string> processedFiles = new List<string>();
        public static List<string> outputFiles = new List<string>();

        public static bool run;
        public static string currentOutPath;

        //public static bool ncnn;

        public enum CopyMode { KeepStructure, CopyToRoot }
        public static CopyMode copyMode;

        public static void Start (string outpath)
        {
            Logger.Log("[Queue] Start()");
            currentOutPath = outpath;
            outputFileQueue.Clear();
            processedFiles.Clear();
            outputFiles.Clear();
            IoUtils.ClearDir(Paths.imgOutPath);
            run = true;
        }

        public static void Stop ()
        {
            Logger.Log("[Queue] Stop()");
            run = false;
        }

        public static async Task Update ()
        {
            while (!Program.canceled && (run || AnyFilesLeft()))
            {
                CheckNcnnOutput();
                string[] outFiles = Directory.GetFiles(Paths.imgOutPath, "*.tmp", SearchOption.AllDirectories);

                foreach (string file in outFiles)
                {
                    if (!outputFileQueue.Contains(file) && !processedFiles.Contains(file) && !IoUtils.IsFileLocked(file))
                    {
                        //processedFiles.Add(file);
                        outputFileQueue.Enqueue(file);
                        Logger.Log("[Queue] Enqueued " + Path.GetFileName(file));
                    }
                }

                await Task.Delay(1000);
            }
        }

        static bool AnyFilesLeft ()
        {
            if (outputFileQueue.Count > 0 || activeWorkers > 0)
                return true;

            try
            {
                if (Directory.GetFiles(Paths.imgOutPath, "*.tmp", SearchOption.AllDirectories).Any(f => !processedFiles.Contains(f)))
                    return true;

                return Directory.GetFiles(Paths.imgOutPath, "*.*.png", SearchOption.AllDirectories)
                    .Any(f => GetTmpPath(f, Paths.imgOutPath, Paths.imgInPath, File.Exists) != null);
            }
            catch
            {
                return false;
            }
        }

        static int activeWorkers;

        public static async Task ProcessQueue ()
        {
            string format = PreviewUi.outputFormat.Text;    // Read UI state once, on the UI thread
            int maxWorkers = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
            List<Task> workers = new List<Task>();

            while (!Program.canceled && (run || AnyFilesLeft()))
            {
                workers.RemoveAll(t => t.IsCompleted);

                while (outputFileQueue.Count > 0 && workers.Count < maxWorkers)
                {
                    string file = outputFileQueue.Dequeue();
                    processedFiles.Add(file);
                    Interlocked.Increment(ref activeWorkers);
                    workers.Add(Task.Run(async () =>
                    {
                        try { await ProcessFile(file, format); }
                        finally { Interlocked.Decrement(ref activeWorkers); }
                    }));
                }

                await Task.Delay(100);
            }

            await Task.WhenAll(workers);
        }

        static async Task ProcessFile (string file, string format)
        {
            Logger.Log("[Queue] Post-Processing " + Path.GetFileName(file));
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                string processed = await PostProcessing.PostprocessingSingle(file, false, 20, true, format);

                for (int retries = 20; retries > 0 && IoUtils.IsFileLocked(processed); retries--)
                {
                    Logger.Log($"{processed} appears to be locked - waiting 500ms...");
                    await Task.Delay(500);
                }

                string outFilename = IoUtils.IsFileValid(processed) ? Upscale.FilenamePostprocess(processed) : null;

                if (outFilename == null)
                {
                    Logger.Log($"[Queue] Error: Post-processing {Path.GetFileName(file)} failed, skipping.");
                    IoUtils.TryDeleteIfExists(file);
                    IoUtils.TryDeleteIfExists(processed);
                    return;
                }

                lock (outputFiles)
                    outputFiles.Add(outFilename);

                Logger.Log("[Queue] Done Post-Processing " + Path.GetFileName(file) + " in " + sw.ElapsedMilliseconds + "ms");
                MoveToOutput(outFilename);
            }
            catch (Exception e)
            {
                Logger.Log($"[Queue] Error post-processing {Path.GetFileName(file)}: {e.Message}\n{e.StackTrace}");
                IoUtils.TryDeleteIfExists(file);
            }
            finally
            {
                Interlocked.Increment(ref BatchUpscaleUI.upscaledImages);
            }
        }

        static void MoveToOutput (string outFilename)
        {
            try
            {
                string targetPath;

                if (copyMode == CopyMode.KeepStructure)
                    targetPath = currentOutPath + outFilename.Replace(Paths.imgOutPath, "");
                else
                    targetPath = Path.Combine(currentOutPath, Path.GetFileName(outFilename));

                if (Upscale.overwriteMode == Upscale.Overwrite.Yes)
                {
                    string suffixToRemove = "-" + Upscale.GetLastModelName();
                    targetPath = Path.Combine(Path.GetDirectoryName(targetPath), Path.GetFileNameWithoutExtension(targetPath).Replace(suffixToRemove, "") + Path.GetExtension(targetPath));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                IoUtils.DeleteIfExists(targetPath);
                File.Move(outFilename, targetPath);
            }
            catch (Exception e)
            {
                Logger.Log("Error trying to copy post-processed file back: " + e.Message + "\n" + e.StackTrace);
            }
        }

        static void CheckNcnnOutput()
        {
            foreach (string file in Directory.GetFiles(Paths.imgOutPath, "*.*.png", SearchOption.AllDirectories))   // Rename to tmp
            {
                if (IoUtils.IsFileLocked(file))
                    continue;

                try
                {
                    string movePath = GetTmpPath(file, Paths.imgOutPath, Paths.imgInPath, File.Exists);

                    if (movePath == null)   // Not an AI output (e.g. a file being post-processed)
                        continue;

                    Logger.Log("[Queue] Renaming " + file + " => " + movePath);
                    IoUtils.DeleteIfExists(movePath);
                    File.Move(file, movePath);
                }
                catch (Exception e)
                {
                    Logger.Log($"[Queue] Failed to rename {file}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Maps AI output "{orig}.png" (or legacy "{orig}.png.png") to "{orig}.tmp", keeping subfolders. Inputs are always "{orig}.png".
        /// Returns null if outFile has no matching input.
        /// </summary>
        internal static string GetTmpPath(string outFile, string outRoot, string inRoot, Func<string, bool> fileExists)
        {
            string rel = outFile.Substring(outRoot.TrimEnd('\\', '/').Length).TrimStart('\\', '/');

            if (fileExists(Path.Combine(inRoot, rel)))
                return Path.Combine(outRoot, rel.Substring(0, rel.Length - 4) + ".tmp");

            if (rel.EndsWith(".png.png", StringComparison.OrdinalIgnoreCase) && fileExists(Path.Combine(inRoot, rel.Substring(0, rel.Length - 4))))
                return Path.Combine(outRoot, rel.Substring(0, rel.Length - 8) + ".tmp");

            return null;
        }
    }
}
