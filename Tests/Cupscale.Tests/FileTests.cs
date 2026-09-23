using System;
using System.Collections.Generic;
using System.IO;
using Cupscale;
using Cupscale.Cupscale;
using Xunit;

namespace CupscaleTests
{
    public class FileTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "cupscale-tests-" + Guid.NewGuid().ToString("N"));

        public FileTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, true);

        const string outRoot = @"C:\data\img-out";
        const string inRoot = @"C:\data\img-in";

        static Func<string, bool> Exists(params string[] files) => new HashSet<string>(files, StringComparer.OrdinalIgnoreCase).Contains;

        [Theory]
        [InlineData(@"a.jpg.png", @"a.jpg.tmp")]
        [InlineData(@"x.png.png", @"x.png.tmp")]
        [InlineData(@"r2image.200.png.png", @"r2image.200.png.tmp")]
        [InlineData(@"sub\dir\a.webp.png", @"sub\dir\a.webp.tmp")]
        public void GetTmpPath_KeepsOriginalNameAndFolders(string outRel, string expectedRel)
        {
            string result = PostProcessingQueue.GetTmpPath(Path.Combine(outRoot, outRel), outRoot, inRoot, Exists(Path.Combine(inRoot, outRel)));
            Assert.Equal(Path.Combine(outRoot, expectedRel), result);
        }

        [Theory]
        [InlineData(@"C:\m\4x_Foo.pth", true)]
        [InlineData(@"C:\m\4x_Foo.PTH", true)]
        [InlineData(@"C:\m\1x_Bar.safetensors", true)]
        [InlineData(@"C:\m\1x_Bar.onnx", false)]
        [InlineData(@"C:\m\4x_Baz.ncnn", false)]
        public void IsModelFile_AcceptsPthAndSafetensors(string path, bool expected)
        {
            Assert.Equal(expected, EsrganData.IsModelFile(path));
        }

        [Fact]
        public void GetTmpPath_HandlesLegacyDoubleExtension()
        {
            string result = PostProcessingQueue.GetTmpPath(Path.Combine(outRoot, "a.jpg.png.png"), outRoot, inRoot, Exists(Path.Combine(inRoot, "a.jpg.png")));
            Assert.Equal(Path.Combine(outRoot, "a.jpg.tmp"), result);
        }

        [Fact]
        public void GetTmpPath_IgnoresFilesWithoutMatchingInput()
        {
            Assert.Null(PostProcessingQueue.GetTmpPath(Path.Combine(outRoot, "r2image.200.png"), outRoot, inRoot, Exists(Path.Combine(inRoot, "r2image.200.png.png"))));
            Assert.Null(PostProcessingQueue.GetTmpPath(Path.Combine(outRoot, "a-model.png"), outRoot, inRoot, Exists()));
        }

        [Theory]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\Temp1_Cupscale.zip\Cupscale", true)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\Rar$EXa1234.5678\Cupscale", true)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\7zO4A1B2C3D", true)]
        [InlineData(@"C:\Users\x\AppData\Local\Temp\claude\scratchpad\publish", false)]
        [InlineData(@"D:\Tools\Cupscale", false)]
        public void IsInsideArchiveTempDir_DetectsArchiveExtraction(string dir, bool expected)
        {
            Assert.Equal(expected, IoUtils.IsInsideArchiveTempDir(dir));
        }

        [Fact]
        public void NcnnFolderPairs_MirrorSubfoldersWithFiles()
        {
            string inRoot = Path.Combine(dir, "in");
            string outRoot = Path.Combine(dir, "out");
            Directory.CreateDirectory(Path.Combine(inRoot, "a", "deep"));
            Directory.CreateDirectory(Path.Combine(inRoot, "empty"));
            File.WriteAllText(Path.Combine(inRoot, "top.png.png"), "");
            File.WriteAllText(Path.Combine(inRoot, "a", "deep", "x.png.png"), "");

            var pairs = Cupscale.OS.NcnnUtils.GetFolderPairs(inRoot, outRoot);

            Assert.Equal(2, pairs.Count);
            Assert.Contains((inRoot, outRoot), pairs);
            Assert.Contains((Path.Combine(inRoot, "a", "deep"), Path.Combine(outRoot, "a", "deep")), pairs);
        }

        [Fact]
        public void GetUniquePath_AppendsCounter()
        {
            string path = Path.Combine(dir, "img.png");
            Assert.Equal(path, IoUtils.GetUniquePath(path));

            File.WriteAllText(path, "");
            File.WriteAllText(Path.Combine(dir, "img (2).png"), "");

            Assert.Equal(Path.Combine(dir, "img (3).png"), IoUtils.GetUniquePath(path));
        }

        [Fact]
        public void IsFileLocked_DetectsExclusiveHandle()
        {
            string path = Path.Combine(dir, "locked.bin");
            File.WriteAllText(path, "x");

            Assert.False(IoUtils.IsFileLocked(path));

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.True(IoUtils.IsFileLocked(path));

            Assert.False(IoUtils.IsFileLocked(Path.Combine(dir, "missing.bin")));
            Assert.False(IoUtils.IsFileLocked(null));
        }

        [Fact]
        public void IsFileLocked_ReadOnlyFileIsNotLocked()
        {
            string path = Path.Combine(dir, "ro.bin");
            File.WriteAllText(path, "x");
            File.SetAttributes(path, FileAttributes.ReadOnly);

            try
            {
                Assert.False(IoUtils.IsFileLocked(path));
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
    }
}
