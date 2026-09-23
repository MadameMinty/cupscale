using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Collections.Concurrent;
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
        static readonly HashSet<string> queuedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static List<string> outputFiles = new List<string>();

        public static bool run;
        public static string currentOutPath;

        public enum CopyMode { KeepStructure, CopyToRoot }
        public static CopyMode copyMode;

        static int generation;  // Bumped per run so workers/loops of a cancelled run can't touch the next one
        static int activeWorkers;
        static readonly ConcurrentDictionary<string, SemaphoreSlim> stemLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        static readonly object moveLock = new object();

        /// <summary> Settings captured when a run starts. </summary>
        class RunSettings
        {
            public int Generation;
            public string Format;
            public string OutPath;
            public CopyMode CopyMode;
            public Upscale.Overwrite Overwrite;
        }

        public static void Start (string outpath)
        {
            Logger.Log("[Queue] Start()");
            Interlocked.Increment(ref generation);
            currentOutPath = outpath;
            outputFileQueue.Clear();
            queuedFiles.Clear();
            processedFiles.Clear();
            lock (outputFiles)
                outputFiles.Clear();
            IoUtils.ClearDir(Paths.imgOutPath);
            run = true;
        }

        public static void Stop ()
        {
            Logger.Log("[Queue] Stop()");
            run = false;
        }

        static bool IsActive (int gen)
        {
            return !Program.canceled && gen == generation;
        }

        public static async Task Update ()
        {
            int gen = generation;

            while (IsActive(gen) && (run || AnyFilesLeft()))
            {
                CheckNcnnOutput();
                string[] outFiles = Directory.GetFiles(Paths.imgOutPath, "*.tmp", SearchOption.AllDirectories);

                foreach (string file in outFiles)
                {
                    if (!queuedFiles.Contains(file) && !processedFiles.Contains(file) && !IoUtils.IsFileLocked(file))
                    {
                        outputFileQueue.Enqueue(file);
                        queuedFiles.Add(file);
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

        public static async Task ProcessQueue ()
        {
            var settings = new RunSettings      // Read UI/static state once, on the UI thread
            {
                Generation = generation,
                Format = PreviewUi.outputFormat.Text,
                OutPath = currentOutPath,
                CopyMode = copyMode,
                Overwrite = Upscale.overwriteMode,
            };

            int maxWorkers = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
            List<Task> workers = new List<Task>();

            while (IsActive(settings.Generation) && (run || AnyFilesLeft()))
            {
                workers.RemoveAll(t => t.IsCompleted);

                while (outputFileQueue.Count > 0 && workers.Count < maxWorkers)
                {
                    string file = outputFileQueue.Dequeue();
                    queuedFiles.Remove(file);
                    processedFiles.Add(file);
                    Interlocked.Increment(ref activeWorkers);
                    workers.Add(Task.Run(async () =>
                    {
                        try { await ProcessFile(file, settings); }
                        finally { Interlocked.Decrement(ref activeWorkers); }
                    }));
                }

                await Task.Delay(100);
            }

            await Task.WhenAll(workers);
        }

        static async Task ProcessFile (string file, RunSettings settings)
        {
            // "dir\a.jpg.tmp" and "dir\a.png.tmp" both produce "dir\a.*" intermediates: process them one at a time
            string stemKey = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)));
            SemaphoreSlim stemLock = stemLocks.GetOrAdd(stemKey, _ => new SemaphoreSlim(1, 1));
            await stemLock.WaitAsync();

            Logger.Log("[Queue] Post-Processing " + Path.GetFileName(file));
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                string processed = await PostProcessing.PostprocessingSingle(file, false, 20, true, settings.Format);

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

                if (!IsActive(settings.Generation))     // Cancelled or superseded by a newer run
                {
                    IoUtils.TryDeleteIfExists(outFilename);
                    return;
                }

                lock (outputFiles)
                    outputFiles.Add(outFilename);

                Logger.Log("[Queue] Done Post-Processing " + Path.GetFileName(file) + " in " + sw.ElapsedMilliseconds + "ms");
                MoveToOutput(outFilename, settings);
            }
            catch (Exception e)
            {
                Logger.Log($"[Queue] Error post-processing {Path.GetFileName(file)}: {e.Message}\n{e.StackTrace}");
                IoUtils.TryDeleteIfExists(file);
            }
            finally
            {
                stemLock.Release();

                if (settings.Generation == generation)
                    Interlocked.Increment(ref BatchUpscaleUI.upscaledImages);
            }
        }

        static void MoveToOutput (string outFilename, RunSettings settings)
        {
            try
            {
                string targetPath;

                if (settings.CopyMode == CopyMode.KeepStructure)
                    targetPath = settings.OutPath + outFilename.Replace(Paths.imgOutPath, "");
                else
                    targetPath = Path.Combine(settings.OutPath, Path.GetFileName(outFilename));

                if (settings.Overwrite == Upscale.Overwrite.Yes)
                    targetPath = Path.Combine(Path.GetDirectoryName(targetPath), Path.GetFileNameWithoutExtension(targetPath).Replace("-" + Upscale.GetLastModelName(), "") + Path.GetExtension(targetPath));

                lock (moveLock)     // CopyToRoot can map several files to one target
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    IoUtils.DeleteIfExists(targetPath);
                    File.Move(outFilename, targetPath);
                }
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
                try
                {
                    string movePath = GetTmpPath(file, Paths.imgOutPath, Paths.imgInPath, File.Exists);

                    if (movePath == null)   // Not an AI output (e.g. a file being post-processed) - don't touch it
                        continue;

                    if (IoUtils.IsFileLocked(file))     // Still being written
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
