using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cupscale.Main;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CupscaleTests
{
    public class CompareRunTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cupscale-compare-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static readonly string[] inputs = { "1x_noisy.png", "photo.jpg", @"sub\art.webp" };
        static readonly HashSet<string> none = new HashSet<string>();

        [Fact]
        public void InputsFor_1xImagesOnlyGoTo1xModels()
        {
            Assert.Equal(inputs, CompareRun.InputsFor(@"C:\m\1x_Denoise.pth", inputs, none));
            Assert.Equal(new[] { "photo.jpg", @"sub\art.webp" }, CompareRun.InputsFor(@"C:\m\4x_Foo.pth", inputs, none));
            Assert.Equal(new[] { "photo.jpg", @"sub\art.webp" }, CompareRun.InputsFor(@"C:\m\4x_Bar.ncnn\", inputs, none));
        }

        [Fact]
        public void InputsFor_SkipsExistingOutputs()
        {
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PHOTO", "sub/art" };
            Assert.Empty(CompareRun.InputsFor(@"C:\m\4x_Foo.pth", inputs, done));
        }

        [Fact]
        public void KeyAndModelName()
        {
            Assert.Equal("sub/art", CompareRun.Key(@"sub\art.webp"));
            Assert.Equal("my.photo", CompareRun.Key("my.photo.jpg"));
            Assert.Equal("4x_Bar", CompareRun.ModelName(@"C:\m\4x_Bar.ncnn\"));
            Assert.Equal("4x_Foo", CompareRun.ModelName(@"C:\m\4x_Foo.pth"));
        }

        [Fact]
        public void UrlOf_RelativeOrFileUri()
        {
            Assert.Equal("../in/a%20b/c%23.png", CompareRun.UrlOf(@"C:\x\in\a b\c#.png", @"C:\x\out"));
            Assert.Equal("file:///D:/in/a%20b.png", CompareRun.UrlOf(@"D:\in\a b.png", @"C:\x\out"));
        }

        [Fact]
        public void BuildManifest_ListsFixturesAndModelOutputs()
        {
            string inDir = Path.Combine(root, "in"), outDir = Path.Combine(root, "out");
            string[] files = { Touch(inDir, "b.png"), Touch(inDir, "a.jpg"), Touch(inDir, @"sub\c.webp") };
            Touch(outDir, @"4x_Foo\a.png");
            Touch(outDir, @"4x_Foo\sub\c.jxl");
            Touch(outDir, @"1x_Bar\b.png");
            Directory.CreateDirectory(Path.Combine(outDir, "empty"));

            string js = CompareRun.BuildManifest(outDir, inDir, files);
            Assert.StartsWith("window.MANIFEST = ", js);
            JObject m = JObject.Parse(js.Substring("window.MANIFEST = ".Length).TrimEnd(';', '\n'));

            Assert.Equal(new[] { "a", "b", "sub/c" }, m["fixtures"].Select(f => (string)f["key"]));
            Assert.Equal("../in/sub/c.webp", (string)m["fixtures"][2]["url"]);
            Assert.Equal(new[] { "1x_Bar", "4x_Foo" }, m["models"].Select(x => (string)x["name"]));
            Assert.Equal("4x_Foo/sub/c.jxl", (string)m["models"][1]["outputs"]["sub/c"]);
            Assert.Null(m["models"][0]["outputs"]["a"]);
        }

        [Fact]
        public void OutputKeys_ReadsNestedFiles()
        {
            string dir = Path.Combine(root, "model");
            Touch(dir, @"sub\x.png");
            Touch(dir, "y.jpg");
            Assert.Equal(new[] { "sub/x", "y" }, CompareRun.OutputKeys(dir).OrderBy(k => k));
            Assert.Empty(CompareRun.OutputKeys(Path.Combine(root, "missing")));
        }

        static string Touch(string dir, string rel)
        {
            string path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "");
            return path;
        }
    }
}
