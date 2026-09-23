using Cupscale.Cupscale;
using Cupscale.Data;
using Cupscale.IO;
using Cupscale.Main;
using Cupscale.OS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cupscale.UI
{
    class BatchUpscaleUI
    {

        static TextBox outDir;
        static TextBox fileList;
        static Label titleLabel;

        static string currentInDir;
        static string currentParentDir;
        static string[] currentInFiles;

        static bool multiImgMode = false;

        public static Stopwatch sw = new Stopwatch();

        // For resetting
        static string defaultOutStr;
        static string defaultTitleText;

        public static void Init (TextBox outDirBox, TextBox fileListBox, Label mainLabel)
        {
            outDir = outDirBox;
            fileList = fileListBox;
            titleLabel = mainLabel;

            defaultOutStr = outDir.Text;
            defaultTitleText = titleLabel.Text;
        }

        public static void LoadDir (string path, bool noGui = false)
        {
            multiImgMode = false;
            currentInDir = path.Trim();
            currentParentDir = path.Trim();
            currentInFiles = null;
            Program.lastDirPath = currentInDir;
            if (noGui) return;
            outDir.Text = path;
            string[] files = Directory.GetFiles(currentInDir, "*", SearchOption.AllDirectories).Where(file => IoUtils.compatibleExtensions.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase))).ToArray();
            FillFileList(files, true);
            TabSelected();
        }

        public static void LoadImages(string[] imgs)
        {
            multiImgMode = true;
            outDir.Text = imgs[0].GetParentDir();
            currentInDir = null;
            currentParentDir = imgs[0].GetParentDir();
            currentInFiles = imgs;
            Program.lastDirPath = outDir.Text;
            FillFileList(imgs, false);
            TabSelected();
        }

        public static void Reset ()
        {
            multiImgMode = false;
            outDir.Text = defaultOutStr;
            titleLabel.Text = defaultTitleText;
            currentInDir = null;
            currentParentDir = null;
            currentInFiles = null;
        }

        public static void TabSelected ()
        {
            if (!outDir.Visible)
                return;
            if (string.IsNullOrWhiteSpace(currentInDir))
            {
                Program.mainForm.SetButtonText("Upscale Images");
                return;
            }
            int compatFilesAmount;

            if (multiImgMode)
            {
                compatFilesAmount = IoUtils.GetAmountOfCompatibleFiles(currentInFiles);
                titleLabel.Text = "Loaded " + compatFilesAmount + " compatible files.";
            }
            else
            {
                compatFilesAmount = IoUtils.GetAmountOfCompatibleFiles(currentInDir, true);
                titleLabel.Text = "Loaded " + currentInDir.Wrap() + " - Found " + compatFilesAmount + " compatible files.";
            }

            string models = compareEnabled ? $" × {compareModels.Count} Models" : "";
            Program.mainForm.SetButtonText($"Upscale {compatFilesAmount} Images{models}");
        }

        public static bool compareEnabled;
        public static List<string> compareModels = new List<string>();

        public static void LoadCompareModels ()
        {
            compareModels = Config.Get("compareModels").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static void SetCompareModels (List<string> models)
        {
            compareModels = models;
            Config.Set("compareModels", string.Join("|", models));
            TabSelected();
        }

        public static async Task CopyImages(string[] imgs, int targetAmount = 0)
        {
            IoUtils.ClearDir(Paths.imgInPath);

            int i = 0;
            foreach (string img in imgs)
            {
                if(IoUtils.compatibleExtensions.Contains(Path.GetExtension(img).ToLower()) && File.Exists(img))
                {
                    File.Copy(img, IoUtils.GetUniquePath(Path.Combine(Paths.imgInPath, Path.GetFileName(img))));
                    i++;
                    float prog = -1f;
                    if (targetAmount > 0)
                        prog = ((float)i / targetAmount) * 100f;
                    if (i % 20 == 0) Program.mainForm.SetProgress(prog, $"Copied {i} images...");
                }
                await Task.Delay(1);
            }
        }

        static void FillFileList (string[] files, bool relativePath)
        {
            fileList.Clear();
            string text = "";

            foreach (string file in files)
            {
                if (relativePath)
                {
                    string relPath = file.Replace(@"\", "/").Replace(currentParentDir.Replace(@"\", "/"), "");
                    text = text + "Root" + relPath + Environment.NewLine;
                }
                else
                {
                    text = text + file + Environment.NewLine;
                }
            }

            fileList.AppendText(text);
        }

        public static async Task Run (bool preprocess, bool postProcess = true, bool cacheSplitDepth = false, string overrideOutDir = "")
        {
            int cudaFallback = Config.Get("cudaFallback").GetInt();
            bool useNcnn = (cudaFallback == 2 || cudaFallback == 3);
            bool useCpu = (cudaFallback == 1);

            string imgOutDir = outDir.Text.Trim();
            if (!string.IsNullOrWhiteSpace(overrideOutDir)) imgOutDir = overrideOutDir;

            if (!PreviewUi.HasValidModelSelection())
                return;

            if (string.IsNullOrWhiteSpace(currentInDir) && (currentInFiles == null || currentInFiles.Length < 1))
            {
                Program.ShowMessage("No directory or files loaded.", "Error");
                return;
            }

            long inputBytes = multiImgMode ? currentInFiles.Where(File.Exists).Sum(f => new FileInfo(f).Length) : IoUtils.GetDirSize(currentInDir);

            if (!IoUtils.HasEnoughDiskSpace((int)(inputBytes / 1024 / 1024), Paths.GetDataPath(), 2.0f))
            {
                Program.ShowMessage($"Not enough disk space on {Path.GetPathRoot(Paths.GetDataPath())} to store temporary files!", "Error");
                return;
            }

            Upscale.currentMode = Upscale.UpscaleMode.Batch;
            Program.mainForm.SetBusy(true);
            Program.mainForm.SetProgress(2f, "Loading images...");
            await Task.Delay(20);
            Directory.CreateDirectory(imgOutDir);
            await StageInputs(preprocess);

            ModelData mdl = Upscale.GetModelData();
            GetProgress(Paths.imgOutPath, IoUtils.GetAmountOfFiles(Paths.imgInPath, true));

            if(postProcess)
                PostProcessingQueue.Start(imgOutDir);

            List<Task> tasks = new List<Task>();
            tasks.Add(Upscale.Run(Paths.imgInPath, Paths.imgOutPath, mdl, cacheSplitDepth, bool.Parse(Config.Get("alpha")), PreviewUi.PreviewMode.None, false));
            
            if (postProcess)
            {
                tasks.Add(PostProcessingQueue.Update());
                tasks.Add(PostProcessingQueue.ProcessQueue());
            }

            sw.Restart();
            await Task.WhenAll(tasks);

            if(!Program.canceled)
                Program.mainForm.SetProgress(0, $"Done - Upscaling took {(sw.ElapsedMilliseconds / 1000f).ToString("0")}s");

            Program.mainForm.SetBusy(false);
        }

        /// <summary> Copies inputs to imgInPath and makes them AI-readable ("{orig}.png"). </summary>
        static async Task StageInputs (bool preprocess)
        {
            await CopyCompatibleImagesToTemp();
            Program.mainForm.SetProgress(3f, "Pre-Processing...");

            if (preprocess)
                await ImageProcessing.PreProcessImages(Paths.imgInPath, !bool.Parse(Config.Get("alpha")));
            else
            {
                await ImageProcessing.ConvertAiIncompatibleImages(Paths.imgInPath);
                IoUtils.AppendToFilenames(Paths.imgInPath, ".png");
            }
        }

        /// <summary> Upscales the loaded images with each model into outDir\{model}, then writes compare.html. </summary>
        public static async Task RunCompare (List<string> models, bool preprocess, bool cacheSplitDepth)
        {
            string outRoot = outDir.Text.Trim();
            string inRoot = multiImgMode ? currentParentDir : currentInDir;

            if (string.IsNullOrWhiteSpace(inRoot))
            {
                Program.ShowMessage("No directory or files loaded.", "Error");
                return;
            }

            if (!Upscale.currentAi.supportsModels)
            {
                Program.ShowMessage("This implementation does not support custom models.", "Error");
                return;
            }

            bool pytorch = Upscale.currentAi == Implementations.Imps.esrganPytorch;
            models = models.Where(m => pytorch ? File.Exists(m) : File.Exists(m) || NcnnUtils.IsDirNcnnModel(m)).ToList();     // PyTorch can't run NCNN models

            if (models.Count < 1)
            {
                Program.ShowMessage("No usable models selected for comparison.", "Error");
                return;
            }

            string outFull = Path.GetFullPath(outRoot).TrimEnd('\\') + "\\";
            if (outFull.StartsWith(Path.GetFullPath(inRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                Program.ShowMessage("Choose an output directory outside of the input directory.", "Error");
                return;
            }

            string[] inputFiles = (multiImgMode ? currentInFiles.Where(File.Exists) : Directory.GetFiles(currentInDir, "*", SearchOption.AllDirectories))
                .Where(f => IoUtils.compatibleExtensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase))).ToArray();
            long inputBytes = inputFiles.Sum(f => new FileInfo(f).Length);

            if (!IoUtils.HasEnoughDiskSpace((int)(inputBytes / 1024 / 1024), Paths.GetDataPath(), 3.0f))
            {
                Program.ShowMessage($"Not enough disk space on {Path.GetPathRoot(Paths.GetDataPath())} to store temporary files!", "Error");
                return;
            }

            Program.canceled = false;
            Upscale.currentMode = Upscale.UpscaleMode.Batch;
            Program.mainForm.SetBusy(true);
            string srcDir = Path.Combine(Paths.GetDataPath(), "compare-src");     // Staged once, copied to imgInPath per model

            try
            {
                await RunCompareStaged(models, preprocess, cacheSplitDepth, outRoot, inRoot, inputFiles, srcDir);
            }
            catch (Exception e)
            {
                Logger.ErrorMessage("Model comparison failed: ", e);
            }
            finally
            {
                try { if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true); } catch { }
                IoUtils.ClearDir(Paths.imgInPath);
                Program.mainForm.SetBusy(false);
            }
        }

        static async Task RunCompareStaged (List<string> models, bool preprocess, bool cacheSplitDepth, string outRoot, string inRoot, string[] inputFiles, string srcDir)
        {
            Program.mainForm.SetProgress(2f, "Loading images...");
            Directory.CreateDirectory(outRoot);
            await StageInputs(preprocess);

            if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
            Directory.Move(Paths.imgInPath, srcDir);
            Directory.CreateDirectory(Paths.imgInPath);
            List<string> staged = Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(srcDir, f)).Select(r => r.Substring(0, r.Length - 4)).ToList();   // Strip ".png"

            bool alpha = bool.Parse(Config.Get("alpha"));
            List<string> incomplete = new List<string>();
            sw.Restart();

            for (int i = 0; i < models.Count && !Program.canceled; i++)
            {
                string name = CompareRun.ModelName(models[i]);
                string modelOut = Path.Combine(outRoot, name);
                List<string> todo = CompareRun.InputsFor(models[i], staged, CompareRun.OutputKeys(modelOut));

                if (todo.Count < 1)
                    continue;

                IoUtils.ClearDir(Paths.imgInPath);

                foreach (string rel in todo)
                {
                    string target = Path.Combine(Paths.imgInPath, rel + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(srcDir, rel + ".png"), target);
                }

                Program.mainForm.SetProgress(Program.GetPercentage(i, models.Count), $"Model {i + 1}/{models.Count}: {name} ({todo.Count} images)");
                PostProcessingQueue.Start(modelOut, PostProcessingQueue.CopyMode.KeepStructure, Upscale.Overwrite.Yes);
                ModelData mdl = new ModelData(models[i], null, ModelData.ModelMode.Single);

                await Task.WhenAll(
                    Upscale.Run(Paths.imgInPath, Paths.imgOutPath, mdl, cacheSplitDepth, alpha, PreviewUi.PreviewMode.None, false),
                    PostProcessingQueue.Update(),
                    PostProcessingQueue.ProcessQueue());

                HashSet<string> done = CompareRun.OutputKeys(modelOut);
                if (!Program.canceled && todo.Any(r => !done.Contains(CompareRun.Key(r))))
                    incomplete.Add(name);
            }

            string html = CompareRun.WriteViewer(outRoot, inRoot, inputFiles);
            Program.lastOutputDir = outRoot;
            Program.mainForm.AfterFirstUpscale();

            if (Program.canceled)
                return;

            Program.mainForm.SetProgress(0, $"Done - Compared {models.Count} models in {(sw.ElapsedMilliseconds / 1000f):0}s");
            OsUtils.OpenUrl(html);

            if (incomplete.Count > 0)
                Program.ShowMessage("Some images failed with these models:\n\n" + string.Join("\n", incomplete), "Warning");
        }

        public static int upscaledImages = 0;

        public static async void GetProgress (string outdir, int target)
        {
            upscaledImages = 0;
            while (Program.busy)
            {
                if (Directory.Exists(outdir))
                {
                    float percentage = (float)upscaledImages / target;
                    percentage = percentage * 100f;
                    if (percentage >= 100f)
                        break;
                    if(upscaledImages > 0)
                        Program.mainForm.SetProgress((int)Math.Round(percentage), "Upscaled " + upscaledImages + "/" + target + " images");
                }
                await Task.Delay(500);
            }
            Program.mainForm.SetProgress(0);
        }

        static async Task CopyCompatibleImagesToTemp(bool move = false)
        {
            IoUtils.ClearDir(Paths.imgOutPath);
            Logger.Log("currentInDir: " + currentInDir + ", imgInPath: " + Paths.imgInPath);

            if (currentInDir == Paths.imgInPath)    // Skip if we are directly upscaling the img-in folder
                return;

            Logger.Log($"Clearing '{Paths.imgInPath}'");
            IoUtils.ClearDir(Paths.imgInPath);

            if (multiImgMode)
                await CopyImages(currentInFiles);
            else
                await IoUtils.CopyDir(currentInDir, Paths.imgInPath, "*", false, true);
        }
    }
}
