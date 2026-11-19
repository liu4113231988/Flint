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
        /// 超过引擎尺寸上限时自动缩放，词坐标换算回原图像素
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

                SoftwareBitmap bitmap;
                double scale = 1;
                uint max = OcrEngine.MaxImageDimension;
                if (capture.Width <= max && capture.Height <= max)
                {
                    bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                        capture.BgraPixels.AsBuffer(), BitmapPixelFormat.Bgra8,
                        capture.Width, capture.Height, BitmapAlphaMode.Ignore);
                }
                else
                {
                    scale = Math.Min((double)max / capture.Width, (double)max / capture.Height);
                    uint scaledWidth = Math.Max(1, (uint)(capture.Width * scale));
                    uint scaledHeight = Math.Max(1, (uint)(capture.Height * scale));
                    byte[] bmp = ScreenCaptureService.EncodeBmp(capture);
                    using var ms = new MemoryStream(bmp);
                    var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                        new BitmapTransform { ScaledWidth = scaledWidth, ScaledHeight = scaledHeight, InterpolationMode = BitmapInterpolationMode.Fant },
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                }

                using (bitmap)
                {
                    OcrResult result = await engine.RecognizeAsync(bitmap);
                    var words = new List<CapturedWord>();
                    foreach (OcrLine line in result.Lines)
                    {
                        foreach (OcrWord word in line.Words)
                        {
                            words.Add(new CapturedWord(
                                word.Text,
                                word.BoundingRect.X / scale, word.BoundingRect.Y / scale,
                                word.BoundingRect.Width / scale, word.BoundingRect.Height / scale));
                        }
                    }

                    return new OcrCaptureResult { Text = result.Text, Words = words };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                return null;
            }
        }
    }
}