using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
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
        /// 首选系统首选语言。首选语言不匹配时使用已安装的识别语言。
        /// </summary>
        internal static OcrEngine? CreateEngine()
        {
            OcrEngine? preferred = OcrEngine.TryCreateFromUserProfileLanguages();
            if (preferred is not null)
            {
                return preferred;
            }

            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
                if (engine is not null)
                {
                    return engine;
                }
            }

            return null;
        }

        /// <summary>
        /// 识别截图中的文字。
        /// 超过引擎尺寸上限时缩小；小字未识别时放大重试。词坐标换算回原图像素。
        /// </summary>
        internal static Task<OcrCaptureResult> RecognizeAsync(
            OcrEngine engine, ScreenCaptureResult capture, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                uint max = OcrEngine.MaxImageDimension;
                double maxScale = (double)max / Math.Max(capture.Width, capture.Height);
                OcrCaptureResult result = await RecognizeAtScaleAsync(engine, capture, Math.Min(1, maxScale), cancellationToken);

                return result.Words.Count == 0 && maxScale > 1
                    ? await RecognizeAtScaleAsync(engine, capture, Math.Min(2, maxScale), cancellationToken)
                    : result;
            }, cancellationToken);
        }

        private static async Task<OcrCaptureResult> RecognizeAtScaleAsync(
            OcrEngine engine, ScreenCaptureResult capture, double scale, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                using var bitmapStream = stream.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(bitmapStream).AsTask(cancellationToken);
                bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    new BitmapTransform
                    {
                        ScaledWidth = Math.Max(1, (uint)(capture.Width * scale)),
                        ScaledHeight = Math.Max(1, (uint)(capture.Height * scale)),
                        InterpolationMode = scale > 1 ? BitmapInterpolationMode.Cubic : BitmapInterpolationMode.Fant
                    },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
            }

            using (bitmap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double scaleX = (double)bitmap.PixelWidth / capture.Width;
                double scaleY = (double)bitmap.PixelHeight / capture.Height;
                OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
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

                return new OcrCaptureResult { Text = string.Join("\n", result.Lines.Select(line => line.Text)), Words = words };
            }
        }
    }
}
