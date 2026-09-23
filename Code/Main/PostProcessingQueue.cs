using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
                    if (!outputFileQueue.Contains(file) && !processedFiles.Contains(file) && !outputFiles.Contains(file) && !IoUtils.IsFileLocked(file))
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
            if (IoUtils.GetAmountOfFiles(Paths.imgOutPath, true) > 0)
                return true;

            return false;
        }

        public static string lastOutfile;
        
        public static async Task ProcessQueue ()
        {
            Stopwatch sw = new Stopwatch();

            while (!Program.canceled && (run || AnyFilesLeft()))
            {
                if (outputFileQueue.Count > 0)
                {
                    string file = outputFileQueue.Dequeue();
                    processedFiles.Add(file);
                    Logger.Log("[Queue] Post-Processing " + Path.GetFileName(file));
                    sw.Restart();
                    lastOutfile = null;
                    await PostProcessing.PostprocessingSingle(file, false);

                    for (int retries = 20; retries > 0 && IoUtils.IsFileLocked(lastOutfile); retries--)
                    {
                        Logger.Log($"{lastOutfile} appears to be locked - waiting 500ms...");
                        await Task.Delay(500);
                    }

                    string outFilename = IoUtils.IsFileValid(lastOutfile) ? Upscale.FilenamePostprocess(lastOutfile) : null;

                    if (outFilename == null)
                    {
                        Logger.Log($"[Queue] Error: Post-processing {Path.GetFileName(file)} failed, skipping.");
                        IoUtils.TryDeleteIfExists(file);
                        IoUtils.TryDeleteIfExists(lastOutfile);
                        BatchUpscaleUI.upscaledImages++;
                        continue;
                    }

                    outputFiles.Add(outFilename);
                    Logger.Log("[Queue] Done Post-Processing " + Path.GetFileName(file) + " in " + sw.ElapsedMilliseconds + "ms");

                    try
                    {
                        if (Upscale.overwriteMode == Upscale.Overwrite.Yes)
                        {
                            string suffixToRemove = "-" + Program.lastModelName.Replace(":", ".").Replace(">>", "+");

                            if (copyMode == CopyMode.KeepStructure)
                            {
                                string combinedPath = currentOutPath + outFilename.Replace(Paths.imgOutPath, "");
                                Directory.CreateDirectory(combinedPath.GetParentDir());
                                File.Copy(outFilename, combinedPath.ReplaceInFilename(suffixToRemove, "", true), true);
                            }
                            if (copyMode == CopyMode.CopyToRoot)
                            {
                                File.Copy(outFilename, Path.Combine(currentOutPath, Path.GetFileName(outFilename).Replace(suffixToRemove, "")), true);
                            }

                            File.Delete(outFilename);
                        }
                        else
                        {
                            if (copyMode == CopyMode.KeepStructure)
                            {
                                string combinedPath = currentOutPath + outFilename.Replace(Paths.imgOutPath, "");
                                Directory.CreateDirectory(combinedPath.GetParentDir());
                                File.Copy(outFilename, combinedPath, true);
                            }

                            if (copyMode == CopyMode.CopyToRoot)
                            {
                                File.Copy(outFilename, Path.Combine(currentOutPath, Path.GetFileName(outFilename)), true);
                            }

                            File.Delete(outFilename);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log("Error trying to copy post-processed file back: " + e.Message + "\n" + e.StackTrace);
                    }
                    
                    BatchUpscaleUI.upscaledImages++;
                }

                await Task.Delay(200);
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
