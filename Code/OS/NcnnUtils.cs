using Cupscale.Forms;
using Cupscale.ImageUtils;
using Cupscale.Implementations;
using Cupscale.IO;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cupscale.OS
{
    class NcnnUtils
    {
		public static string currentNcnnModel;
        public static string lastNcnnOutput;
		public static string lastScaleCheckOutput;
		static Process currentProcess;
		static string ncnnDir = "";
		private static string ConverterDir { get => Path.Combine(Paths.binPath, "pth2ncnn"); }

		public static async Task ConvertNcnnModel(string modelPath, string filenamePattern)
        {
			Logger.Log($"ConvertNcnnModel: {modelPath}");
			currentNcnnModel = null;
			DialogForm dialog = null;

            try
            {
                if (IsDirNcnnModel(modelPath))
                {
					ApplyFilenamePattern(modelPath, filenamePattern);
					currentNcnnModel = modelPath;
					return;
				}

                string modelName = Path.GetFileName(modelPath);
                ncnnDir = Path.Combine(Config.Get("modelPath"), ".ncnn-models");
                Directory.CreateDirectory(ncnnDir);
                string outPath = Path.Combine(ncnnDir, Path.ChangeExtension(modelName, null));
                Logger.Log("Checking for NCNN model: " + outPath);

                if (IoUtils.GetAmountOfFiles(outPath, false) < 2)
                {
                    Logger.Log("Running model converter...");
                    if (!await EmbeddedPython.EnsureAvailable())
                        throw new Exception("Converting PyTorch models to NCNN needs the Python runtime.");
                    dialog = new DialogForm("Converting ESRGAN model to NCNN format...");
                    await RunConverter(modelPath, outPath);

                    if (lastNcnnOutput.Contains("Error:"))
                        throw new Exception(lastNcnnOutput.SplitIntoLines().Where(x => x.Contains("Error:")).First());
                }
                else
                {
                    Logger.Log("NCNN Model is cached - Skipping conversion.");
                }

				ApplyFilenamePattern(outPath, filenamePattern);
                currentNcnnModel = outPath;
            }
            catch (Exception e)
            {
				Logger.ErrorMessage("Failed to convert Pytorch model to NCNN format! It might be incompatible.", e);
            }
			finally
			{
				dialog?.Close();
			}
        }

		static void ApplyFilenamePattern(string path, string pattern)
        {
			foreach (FileInfo file in IoUtils.GetFileInfosSorted(path).Where(f => f.Extension == ".bin" || f.Extension == ".param"))
				IoUtils.RenameFile(file.FullName, pattern.Replace("*", $"{file.Name.GetInt()}"));
		}

		static async Task RunConverter(string modelPath, string directory)
        {
            lastNcnnOutput = "";
			bool showWindow = Config.GetInt("cmdDebugMode") > 0;
			bool stayOpen = Config.GetInt("cmdDebugMode") == 2;

			modelPath = modelPath.Wrap();
			directory = directory.Wrap();

			string opt = "/C";
			if (stayOpen) opt = "/K";

			string args = $"{opt} cd /D {ConverterDir.Wrap()} & {EmbeddedPython.GetPyCmd()} pth2ncnn.py {modelPath} --outpath {directory}";

			Logger.Log("[CMD] " + args);
			Process converterProc = OsUtils.NewProcess(!showWindow);
			converterProc.StartInfo.Arguments = args;

			if (!showWindow)
			{
				converterProc.OutputDataReceived += ConverterOutputHandler;
				converterProc.ErrorDataReceived += ConverterOutputHandler;
			}

			currentProcess = converterProc;
			OsUtils.StartTracked(converterProc);

			if (!showWindow)
			{
				converterProc.BeginOutputReadLine();
				converterProc.BeginErrorReadLine();
			}

			while (!converterProc.HasExited)
				await Task.Delay(100);
		}

		private static void ConverterOutputHandler(object sendingProcess, DataReceivedEventArgs output)
		{
			if (output == null || output.Data == null)
				return;

			string data = output.Data;
			Logger.Log("[NcnnUtils] Model Converter Output: " + data);
            lastNcnnOutput += $"{data}\n";
        }

		public static bool IsDirNcnnModel (string path, bool requireSuffix = true, bool noMoreThanTwoFiles = false)
        {
            try
            {
				if (!IoUtils.IsPathDirectory(path))
					return false;

				DirectoryInfo dir = new DirectoryInfo(path);
				bool suffixValid = dir.Name.EndsWith(".ncnn");
				bool filesValid = dir.GetFiles("*.bin").Length == 1 && dir.GetFiles("*.param").Length == 1;

				if (noMoreThanTwoFiles && dir.GetFiles("*").Length > 2)
					filesValid = false;

				if (requireSuffix && !suffixValid)
					return false;

				return filesValid;
			}
			catch(Exception e)
            {
				Logger.Log($"IsDirNcnnModel Exception: {e.Message}. Defaulting to false.");
				return false;
            }
		}

		static async Task RunScaleCheck(string bin, string param)
		{
			lastScaleCheckOutput = "";

			bin = bin.Wrap();
			param = param.Wrap();

			string opt = "/C";

			string args = $"{opt} cd /D {ConverterDir.Wrap()} & {EmbeddedPython.GetPyCmd()} get_scale.py {bin} {param}";

			Logger.Log("[CMD] " + args);
			Process converterProc = OsUtils.NewProcess(true);
			converterProc.StartInfo.Arguments = args;


			converterProc.OutputDataReceived += ScaleCheckOutputHandler;
			converterProc.ErrorDataReceived += ScaleCheckOutputHandler;

			currentProcess = converterProc;
			OsUtils.StartTracked(converterProc);


			converterProc.BeginOutputReadLine();
			converterProc.BeginErrorReadLine();

			while (!converterProc.HasExited)
				await Task.Delay(100);
		}

		private static void ScaleCheckOutputHandler(object sendingProcess, DataReceivedEventArgs output)
		{
			if (output == null || output.Data == null)
				return;

			string data = output.Data;
			Logger.Log("[NcnnUtils] Scale Check Output: " + data);
			lastScaleCheckOutput += $"{data}\n";
		}

		static readonly Dictionary<string, int> scaleCache = new Dictionary<string, int>();

		public static async Task<int> GetNcnnModelScale(string modelDir)
		{
            try
            {
				string bin_file = Directory.GetFiles(modelDir, "*.bin")[0];
				string param_file = Directory.GetFiles(modelDir, "*.param")[0];
				string cacheKey = $"{bin_file}|{File.GetLastWriteTimeUtc(bin_file).Ticks}";

				if (scaleCache.TryGetValue(cacheKey, out int cached))
					return cached;

				if (!await EmbeddedPython.EnsureAvailable())
				{
					Logger.Log("No Python runtime for the NCNN scale check - assuming 4x.");
					return 4;
				}

				await RunScaleCheck(bin_file, param_file);
				var match = System.Text.RegularExpressions.Regex.Match(lastScaleCheckOutput, @"Scale:\s*(\d+)");

				if (!match.Success)
				{
					Logger.Log($"Failed to parse NCNN model scale: {lastScaleCheckOutput.Trim()}");
					return 4;
				}

				int scale = int.Parse(match.Groups[1].Value);
				scaleCache[cacheKey] = scale;
				return scale;
			}
			catch (Exception e)
            {
				Logger.Log($"Failed to get NCNN model scale for dir '{modelDir}': {e.Message}");
				return 4;
            }
		}

		/// <summary>
		/// (input, output) folder pairs for every folder under inRoot that contains files, mirroring the tree under outRoot.
		/// NCNN executables don't recurse into subfolders, so each folder is run separately.
		/// </summary>
		internal static List<(string inDir, string outDir)> GetFolderPairs(string inRoot, string outRoot)
		{
			var pairs = new List<(string, string)>();
			string root = inRoot.TrimEnd('\\', '/');

			if (!Directory.Exists(root))
				return pairs;

			foreach (string dir in new[] { root }.Concat(Directory.GetDirectories(root, "*", SearchOption.AllDirectories)))
			{
				if (!Directory.EnumerateFiles(dir).Any())
					continue;

				string rel = dir.Substring(root.Length).TrimStart('\\', '/');
				pairs.Add((dir, rel.Length == 0 ? outRoot : Path.Combine(outRoot, rel)));
			}

			return pairs;
		}

		/// <summary> Runs an NCNN executable once per input folder. buildArgs(inDir, outDir) returns the cmd.exe arguments. </summary>
		public static async Task RunPerFolder(string inRoot, string outRoot, Func<string, string, string> buildArgs, Action<string, bool> outputHandler)
		{
			bool showWindow = Config.GetInt("cmdDebugMode") > 0;

			foreach (var (inDir, outDir) in GetFolderPairs(inRoot, outRoot))
			{
				if (Program.canceled)
					return;

				Directory.CreateDirectory(outDir);
				string cmd = buildArgs(inDir, outDir);
				Logger.Log("[CMD] " + cmd);

				Process proc = OsUtils.NewProcess(!showWindow);
				proc.StartInfo.Arguments = cmd;

				if (!showWindow)
				{
					proc.OutputDataReceived += (sender, outLine) => { outputHandler(outLine.Data, false); };
					proc.ErrorDataReceived += (sender, outLine) => { outputHandler(outLine.Data, true); };
				}

				Program.lastImpProcess = proc;
				OsUtils.StartTracked(proc);

				if (!showWindow)
				{
					proc.BeginOutputReadLine();
					proc.BeginErrorReadLine();
				}

				while (!proc.HasExited)
					await Task.Delay(50);
			}
		}

		/// <summary> Parses NCNN progress lines like "12.50%", or "12,50%" (NCNN prints using the system locale). </summary>
		public static bool TryParsePercent(string line, out float percent)
		{
			percent = 0f;
			string s = line?.Trim();

			if (string.IsNullOrEmpty(s) || !s.EndsWith("%"))
				return false;

			return float.TryParse(s.TrimEnd('%').Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
		}
	}
}
