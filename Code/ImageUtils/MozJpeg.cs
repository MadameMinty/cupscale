using System;
using System.Drawing.Imaging;
using System.IO;
using MozJpegSharp;
using ImageMagick;

namespace Cupscale.ImageUtils
{
    class MozJpeg
    {
        public static void Encode(MagickImage image, string outPath, int q, bool chromaSubSample = true)
        {
            int width = (int)image.Width;
            int height = (int)image.Height;
            byte[] bgr;

            using (var pixels = image.GetPixelsUnsafe())    // Raw 8-bit BGR, no intermediate PNG/Bitmap
                bgr = pixels.ToByteArray(PixelMapping.BGR);

            Encode(bgr, width, height, outPath, q, chromaSubSample);
        }

        public static void Encode(byte[] bgr, int width, int height, string outPath, int q, bool chromaSubSample = true)
        {
            try
            {
                TJSubsamplingOption subSample = chromaSubSample ? TJSubsamplingOption.Chrominance420 : TJSubsamplingOption.Chrominance444;

                using (var compressor = new TJCompressor())
                {
                    byte[] compressed = compressor.Compress(bgr, width * 3, width, height, PixelFormat.Format24bppRgb, subSample, q, TJFlags.None);   // GDI+ 24bppRgb = BGR byte order
                    File.WriteAllBytes(outPath, compressed);
                }

                Logger.Log("[MozJpeg] Written image to " + outPath);
            }
            catch (TypeInitializationException e)
            {
                Logger.ErrorMessage($"MozJpeg Initialization Error: {e.InnerException.Message}\n", e);
            }
            catch (Exception e)
            {
                Logger.ErrorMessage("MozJpeg Error: ", e);
            }
        }
    }
}
