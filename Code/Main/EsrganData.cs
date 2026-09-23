using Cupscale.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Cupscale
{
	internal class EsrganData
	{
		public static List<string> models = new List<string>();
		public static List<string> modelsFullPath = new List<string>();

		public static readonly string[] modelExtensions = { ".pth", ".safetensors" };

		/// <summary> PyTorch model file (.pth or .safetensors). </summary>
		public static bool IsModelFile (string path)
        {
			return modelExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
		}

		public static void CheckModelDir()
		{
			if (string.IsNullOrWhiteSpace(Config.Get("modelPath")))
			{
				Program.ShowMessage("Please set a model path in the settings.\nPoint it to the folder where you save your .pth/.safetensors model files.", "Notice");
				new SettingsForm().ShowDialog();
			}
			else if (!Directory.Exists(Config.Get("modelPath")))
			{
				Program.ShowMessage($"The model path \"{Config.Get("modelPath")}\" doesn't exist.\nPlease fix it in the settings.", "Notice");
				new SettingsForm().ShowDialog();
			}
		}

		public static bool ModelExists (string modelName)
        {
			IEnumerable<string> files = Directory.GetFiles(Config.Get("modelPath"), "*", SearchOption.AllDirectories).Where(IsModelFile);
			foreach(string modelFile in files)
            {
				if (Path.GetFileNameWithoutExtension(modelFile) == modelName)
					return true;
            }
			return false;
		}

		public static void ReloadModelList()
		{
			string mdlPath = Config.Get("modelPath");
            if (!Directory.Exists(mdlPath))
            {
				Logger.Log($"[EsrganData] Model folder doesn't exist: {mdlPath}");
				return;
			}
			models.Clear();
			modelsFullPath.Clear();
			string[] files = Directory.GetFiles(mdlPath);
			string[] array = files;
			foreach (string path in array)
			{
				if (IsModelFile(path))
				{
					models.Add(Path.GetFileNameWithoutExtension(path));
					modelsFullPath.Add(path);
				}
			}

			Logger.Log($"[EsrganData] Model folder: {mdlPath} ({models.Count} models)");
		}
	}
}
