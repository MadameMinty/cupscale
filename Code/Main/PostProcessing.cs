using Cupscale.Cupscale;
using Cupscale.IO;
using Cupscale.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace Cupscale.Main
{
    class PostProcessing
    {
        /// <summary> Post-processes an AI output file. Returns the resulting file path, or null on failure. Pass format when calling off the UI thread. </summary>
        public static async Task<string> PostprocessingSingle(string path, bool dontResize = false, int retryCount = 20, bool trimPngExt = true, string format = null)
        {
            if (!IoUtils.IsFileValid(path))
                return null;

            format = format ?? PreviewUi.outputFormat.Text;

            if (trimPngExt)
            {
                string newPath = path.Substring(0, path.Length - 4);
                Logger.Log($"PostProc: Trimmed filename from '{Path.GetFileName(path)}' to '{Path.GetFileName(newPath)}'");

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(path, newPath);
                        break;
                    }
                    catch (Exception e)     // An I/O error can appear if the file is still locked by python (?)
                    {
                        Logger.Log($"Failed to move/rename! ('{path}' => '{newPath}') {e.Message}");

                        if (attempt >= retryCount)
                        {
                            Logger.ErrorMessage($"Failed to move/rename '{Path.GetFileName(path)}' and ran out of retries!", e);
                            return null;
                        }

                        await Task.Delay(500);
                    }
                }

                path = newPath;
            }

            if (Program.lastUpscaleIsVideo)     // Temp frames for ffmpeg: re-encode only if resizing
            {
                bool resize = !dontResize && !(ImageProcessing.postScaleMode == Upscale.ScaleMode.Percent && ImageProcessing.postScaleValue == 100);

                if (resize || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    return await ImageProcessing.PostProcessImage(path, ImageProcessing.Format.PngFast, dontResize);

                return path;
            }

            if (format == Upscale.ImgExportMode.SameAsSource.ToStringTitleCase())
                return await ImageProcessing.ConvertImageToOriginalFormat(path, true, dontResize);

            if (format == Upscale.ImgExportMode.DDS.ToStringTitleCase())
                return await ImageProcessing.PostProcessDDS(path);

            return await ImageProcessing.PostProcessImage(path, GetFormat(format), dontResize);
        }

        static ImageProcessing.Format GetFormat(string format)
        {
            if (format == Upscale.ImgExportMode.JPEG.ToStringTitleCase()) return ImageProcessing.Format.Jpeg;
            if (format == Upscale.ImgExportMode.WEBP.ToStringTitleCase()) return ImageProcessing.Format.Weppy;
            if (format == Upscale.ImgExportMode.JXL.ToStringTitleCase()) return ImageProcessing.Format.Jxl;
            if (format == Upscale.ImgExportMode.BMP.ToStringTitleCase()) return ImageProcessing.Format.BMP;
            if (format == Upscale.ImgExportMode.TGA.ToStringTitleCase()) return ImageProcessing.Format.TGA;
            if (format == Upscale.ImgExportMode.GIF.ToStringTitleCase()) return ImageProcessing.Format.GIF;
            return ImageProcessing.Format.Png50;
        }
    }
}
