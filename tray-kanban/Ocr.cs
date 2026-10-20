using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace TrayKanban
{
    // Windows' built-in OCR engine (the same one Snipping Tool uses): offline, no keys.
    public static class Ocr
    {
        public static async Task<string> ReadAsync(string imagePath)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (engine == null) return "";

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var max = OcrEngine.MaxImageDimension;
                var transform = new BitmapTransform();
                var w = decoder.PixelWidth; var h = decoder.PixelHeight;
                double scale = 1;
                if (w > max || h > max) scale = Math.Min((double)max / w, (double)max / h);
                else if (Math.Max(w, h) < 900) scale = Math.Min(2.0, (double)max / Math.Max(w, h)); // small snips read better upscaled
                if (scale != 1)
                {
                    transform.ScaledWidth = (uint)(w * scale);
                    transform.ScaledHeight = (uint)(h * scale);
                    transform.InterpolationMode = BitmapInterpolationMode.Fant;
                }
                using (var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage))
                {
                    var result = await engine.RecognizeAsync(bmp);
                    return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
                }
            }
        }
    }
}
