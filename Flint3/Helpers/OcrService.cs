using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Flint3.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Flint3.Helpers
{
    /// <summary>
    /// 一次 OCR 的结果：整段文本 + 词级矩形（坐标相对送入的截图，物理像素）
    /// </summary>
    public sealed class OcrCaptureResult
    {
        public required string Text { get; init; }

        public required IReadOnlyList<CapturedWord> Words { get; init; }
    }

    /// <summary>
    /// 系统自带 Windows.Media.Ocr 封装：免打包可用、零新增依赖、CsWinRT AOT 兼容
    /// </summary>
    public static class OcrService
    {
        /// <summary>
        /// 系统是否装有可用的 OCR 语言包
        /// </summary>
        public static bool IsLanguageAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

        /// <summary>
        /// 识别截图中的文字；无语言包或识别失败返回 null。
        /// 超过引擎尺寸上限时缩小；小字未识别时放大重试。词坐标换算回原图像素。
        /// </summary>
        public static async Task<OcrCaptureResult?> RecognizeAsync(ScreenCaptureResult capture)
        {
            ArgumentNullException.ThrowIfNull(capture);

            try
            {
                OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine is null)
                {
                    return null;
                }

                uint max = OcrEngine.MaxImageDimension;
                double maxScale = (double)max / Math.Max(capture.Width, capture.Height);
                OcrCaptureResult result = await RecognizeAtScaleAsync(engine, capture, Math.Min(1, maxScale));

                return result.Words.Count == 0 && maxScale > 1
                    ? await RecognizeAtScaleAsync(engine, capture, Math.Min(2, maxScale))
                    : result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                return null;
            }
        }

        private static async Task<OcrCaptureResult> RecognizeAtScaleAsync(
            OcrEngine engine, ScreenCaptureResult capture, double scale)
        {
            SoftwareBitmap bitmap;
            if (scale == 1)
            {
                bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                    capture.BgraPixels.AsBuffer(), BitmapPixelFormat.Bgra8,
                    capture.Width, capture.Height, BitmapAlphaMode.Ignore);
            }
            else
            {
                using var stream = new MemoryStream(ScreenCaptureService.EncodeBmp(capture));
                var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
                bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    new BitmapTransform
                    {
                        ScaledWidth = Math.Max(1, (uint)(capture.Width * scale)),
                        ScaledHeight = Math.Max(1, (uint)(capture.Height * scale)),
                        InterpolationMode = scale > 1 ? BitmapInterpolationMode.Cubic : BitmapInterpolationMode.Fant
                    },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            }

            using (bitmap)
            {
                double scaleX = (double)bitmap.PixelWidth / capture.Width;
                double scaleY = (double)bitmap.PixelHeight / capture.Height;
                OcrResult result = await engine.RecognizeAsync(bitmap);
                var words = new List<CapturedWord>();
                foreach (OcrLine line in result.Lines)
                {
                    foreach (OcrWord word in line.Words)
                    {
                        words.Add(new CapturedWord(
                            word.Text,
                            word.BoundingRect.X / scaleX, word.BoundingRect.Y / scaleY,
                            word.BoundingRect.Width / scaleX, word.BoundingRect.Height / scaleY));
                    }
                }

                return new OcrCaptureResult { Text = result.Text, Words = words };
            }
        }
    }
}
