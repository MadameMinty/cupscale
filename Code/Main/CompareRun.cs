using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Cupscale.Main
{
    /// <summary> Multi-model batch comparison: per-model input filtering and the manifest read by compare.html. </summary>
    static class CompareRun
    {
        public const string HtmlName = "compare.html";
        public const string ManifestName = "manifest.js";

        /// <summary> Images named "1x*" only go through "1x*" models. </summary>
        public static bool Is1x(string path)
        {
            return Path.GetFileName(path.TrimEnd('\\', '/')).StartsWith("1x", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary> Output folder name; matches ModelData.model1Name. </summary>
        public static string ModelName(string modelPath)
        {
            return Path.GetFileNameWithoutExtension(modelPath.TrimEnd('\\', '/'));
        }

        /// <summary> "sub\a.jpg" => "sub/a" </summary>
        public static string Key(string relPath)
        {
            return Path.ChangeExtension(relPath, null).Replace('\\', '/');
        }

        /// <summary> Keys of all files already in a model's output folder. </summary>
        public static HashSet<string> OutputKeys(string modelOutDir)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(modelOutDir))
                foreach (string file in Directory.GetFiles(modelOutDir, "*", SearchOption.AllDirectories))
                    keys.Add(Key(Path.GetRelativePath(modelOutDir, file)));

            return keys;
        }

        /// <summary> Inputs (relative paths) this model should process: 1x rule, minus those already done. </summary>
        public static List<string> InputsFor(string modelPath, IEnumerable<string> relInputs, ISet<string> doneKeys)
        {
            bool model1x = Is1x(modelPath);
            return relInputs.Where(r => (model1x || !Is1x(r)) && !doneKeys.Contains(Key(r))).ToList();
        }

        /// <summary> window.MANIFEST for compare.html: originals (by URL) and every model folder's outputs in outDir. </summary>
        public static string BuildManifest(string outDir, string inDir, IEnumerable<string> inputFiles)
        {
            var fixtures = inputFiles
                .Select(f => Path.GetRelativePath(inDir, f))
                .OrderBy(r => Key(r), StringComparer.OrdinalIgnoreCase)
                .GroupBy(r => Key(r), StringComparer.OrdinalIgnoreCase).Select(g => g.First())     // Same stem, different ext: first wins
                .Select(r => new { key = Key(r), file = r.Replace('\\', '/'), url = UrlOf(Path.Combine(inDir, r), outDir) })
                .ToList();

            var models = new List<object>();

            foreach (string dir in Directory.GetDirectories(outDir).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var outputs = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    outputs.TryAdd(Key(Path.GetRelativePath(dir, file)), UrlOf(file, outDir));

                if (outputs.Count > 0)
                    models.Add(new { name = Path.GetFileName(dir), outputs });
            }

            string json = JsonConvert.SerializeObject(new { fixtures, models }, Formatting.Indented);
            return $"window.MANIFEST = {json};\n";
        }

        /// <summary> Writes compare.html and its manifest into outDir. Returns the HTML path. </summary>
        public static string WriteViewer(string outDir, string inDir, IEnumerable<string> inputFiles)
        {
            File.WriteAllText(Path.Combine(outDir, ManifestName), BuildManifest(outDir, inDir, inputFiles));
            string htmlPath = Path.Combine(outDir, HtmlName);

            using (Stream res = typeof(CompareRun).Assembly.GetManifestResourceStream(HtmlName))
            using (FileStream file = File.Create(htmlPath))
                res.CopyTo(file);

            return htmlPath;
        }

        /// <summary> URL of path as seen from a page in pageDir: relative if possible, else file:///. </summary>
        public static string UrlOf(string path, string pageDir)
        {
            string rel = Path.GetRelativePath(pageDir, path);

            if (!Path.IsPathRooted(rel))
                return Escape(rel);

            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return "file:///" + root.Replace('\\', '/') + Escape(full.Substring(root.Length));
        }

        static string Escape(string path)
        {
            return string.Join("/", path.Split('\\', '/').Select(Uri.EscapeDataString));
        }
    }
}
