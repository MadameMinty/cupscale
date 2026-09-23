using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using Cupscale.ImageUtils;
using Cupscale.IO;
using Cupscale.Main;
using Cupscale.UI;
using ImageMagick;
using Paths = Cupscale.IO.Paths;

namespace Cupscale
{
    internal class PreviewMerger
    {
        public static float offsetX;
        public static float offsetY;
        public static string inputCutoutPath;
        public static string outputCutoutPath;

        public static bool showingOriginal = false;

        public static void Merge()
        {
            PreviewUi.sw.Stop();
            Program.mainForm.SetProgress(100f);
            inputCutoutPath = Path.Combine(Paths.previewPath, "preview.png.png");
            outputCutoutPath = Directory.GetFiles(Paths.previewOutPath, "preview.*", SearchOption.AllDirectories)[0];

            var sourceInfo = new MagickImageInfo(Paths.tempImgPath);    // Header only
            float scale = GetScale();

            if (sourceInfo.Width * scale > 16000 || sourceInfo.Height * scale > 16000)
            {
                MergeOnlyCutout(scale);
                Program.ShowMessage("The scaled output image is very large (>16000px), so only the cutout will be shown.", "Warning");
                return;
            }

            MergeScrollable(scale);
        }

        static void MergeScrollable(float scale)
        {

            if (offsetX < 0f) offsetX *= -1f;
            if (offsetY < 0f) offsetY *= -1f;
            offsetX *= scale;
            offsetY *= scale;
            Logger.Log("[Merger] Merging " + Path.GetFileName(outputCutoutPath) + " onto original using offset " + offsetX + "x" + offsetY);
            Image image = MergeInMemory(scale);
            PreviewUi.currentOriginal = ImgUtils.GetImage(Paths.tempImgPath);
            PreviewUi.currentOutput = image;
            PreviewUi.currentScale = scale;
            UiHelpers.ReplaceImageAtSameScale(PreviewUi.previewImg, image);
            Program.mainForm.SetProgress(0f, "Done.");
        }


        public static Image MergeInMemory(float scale)
        {
            Image scaledSourceImg;
            int oldWidth;
            int newWidth;

            if (!(ImageProcessing.preScaleMode == Upscale.ScaleMode.Percent && ImageProcessing.preScaleValue == 100))
            {
                string tempScaledSourceImagePath = Path.Combine(Paths.tempImgPath.GetParentDir(), "scaled-source.png");

                using (MagickImage scaledSourceMagickImg = new MagickImage(Paths.tempImgPath))
                {
                    oldWidth = scaledSourceMagickImg.Width;
                    ImageProcessing.ResizeImagePre(scaledSourceMagickImg);
                    newWidth = scaledSourceMagickImg.Width;
                    scaledSourceMagickImg.Quality = 0;  // Temp file: no compression
                    scaledSourceMagickImg.Write(tempScaledSourceImagePath);
                }

                scaledSourceImg = ImgUtils.GetImage(tempScaledSourceImagePath);
            }
            else
            {
                scaledSourceImg = ImgUtils.GetImage(Paths.tempImgPath);
                oldWidth = scaledSourceImg.Width;
                newWidth = scaledSourceImg.Width;
            }

            float preScale = (float)oldWidth / (float)newWidth;
            Image cutout = ImgUtils.GetImage(outputCutoutPath);

            int scaledWidth = (scaledSourceImg.Width * scale).RoundToInt();
            int scaledHeight = (scaledSourceImg.Height * scale).RoundToInt();

            if (scaledWidth == cutout.Width && scaledHeight == cutout.Height)
            {
                Logger.Log("[Merger] Cutout is the entire image - skipping merge");
                scaledSourceImg.Dispose();
                return cutout;
            }

            var destImage = new Bitmap(scaledWidth, scaledHeight);

            using (scaledSourceImg)
            using (cutout)
            using (var gfx = Graphics.FromImage(destImage))
            {
                gfx.CompositingMode = CompositingMode.SourceCopy;
                gfx.CompositingQuality = CompositingQuality.HighQuality;
                gfx.InterpolationMode = (Program.currentFilter == FilterType.Point) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                gfx.SmoothingMode = SmoothingMode.HighQuality;
                gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gfx.DrawImage(scaledSourceImg, 0, 0, destImage.Width, destImage.Height);       // Scale up
                gfx.DrawImage(cutout, (offsetX / preScale).RoundToInt(), (offsetY / preScale).RoundToInt());     // Overlay cutout
            }

            return destImage;
        }

        static void MergeOnlyCutout(float scale)
        {
            string scaledCutoutPath = Path.Combine(Paths.previewOutPath, "preview-input-scaled.png");

            using (MagickImage originalCutout = ImgUtils.GetMagickImage(inputCutoutPath))
            {
                originalCutout.FilterType = Program.currentFilter;
                originalCutout.Resize(new Percentage(scale * 100));
                originalCutout.Format = MagickFormat.Png;
                originalCutout.Quality = 0;  // Save preview as uncompressed PNG for max speed
                originalCutout.Write(scaledCutoutPath);
            }

            PreviewUi.currentOriginal = ImgUtils.GetImage(scaledCutoutPath);
            PreviewUi.currentOutput = ImgUtils.GetImage(outputCutoutPath);

            PreviewUi.previewImg.Image = PreviewUi.currentOutput;
            PreviewUi.previewImg.ZoomToFit();
            PreviewUi.previewImg.Zoom = (int)Math.Round(PreviewUi.previewImg.Zoom * 1.01f);
            Program.mainForm.resetImageOnMove = true;
            Program.mainForm.SetProgress(0f, "Done.");
        }

        private static float GetScale()
        {
            var input = new MagickImageInfo(inputCutoutPath);     // Header only
            var output = new MagickImageInfo(outputCutoutPath);
            return (float)output.Width / (float)input.Width;
        }

        public static void ShowOutput()
        {
            if (PreviewUi.currentOutput != null)
            {
                showingOriginal = false;
                UiHelpers.ReplaceImageAtSameScale(PreviewUi.previewImg, PreviewUi.currentOutput);
            }
        }

        public static void ShowOriginal()
        {
            if (PreviewUi.currentOriginal != null)
            {
                showingOriginal = true;
                UiHelpers.ReplaceImageAtSameScale(PreviewUi.previewImg, PreviewUi.currentOriginal);
            }
        }
    }
}
