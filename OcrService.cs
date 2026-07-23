using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TesseractOCR;
using TesseractOCR.Enums;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace KeyMapper
{
    public sealed record OcrRecognitionResult(
        string Text,
        string EngineName,
        string LanguageSummary,
        float Confidence,
        bool Success,
        string ErrorMessage);

    public class OcrService
    {
        private const string RecognitionLanguages = "fas+deu+eng";
        private sealed record OcrCandidate(
            string Text,
            float Confidence,
            string PassName,
            bool PersianOnly);

        public static async Task<string> RecognizeTextAsync(Bitmap bitmap)
        {
            OcrRecognitionResult result = await RecognizeDetailedAsync(bitmap);
            return result.Success ? result.Text : result.ErrorMessage;
        }

        public static async Task<OcrRecognitionResult> RecognizeDetailedAsync(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return Failed("No image was captured.");
            }

            // Clone before leaving the UI callback. The screen snipper owns its
            // original bitmap and can dispose it as soon as this method returns.
            using Bitmap source = new Bitmap(bitmap);
            try
            {
                return await Task.Run(() => RecognizeWithTesseract(source));
            }
            catch (Exception tesseractError)
            {
                try
                {
                    OcrRecognitionResult fallback = await RecognizeWithWindowsAsync(source);
                    if (fallback.Success) return fallback;
                }
                catch
                {
                    // Report the more useful Tesseract error below.
                }

                return Failed($"OCR could not read this region. {tesseractError.Message}");
            }
        }

        private static OcrRecognitionResult RecognizeWithTesseract(Bitmap source)
        {
            string dataPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "OCR",
                "tessdata");

            string[] requiredModels = ["eng.traineddata", "deu.traineddata", "fas.traineddata"];
            string? missingModel = requiredModels.FirstOrDefault(
                model => !File.Exists(Path.Combine(dataPath, model)));
            if (missingModel != null)
            {
                throw new FileNotFoundException($"Missing OCR language model: {missingModel}");
            }

            using Bitmap prepared = PrepareForRecognition(source);
            using Bitmap thresholded = ApplyOtsuThreshold(prepared);
            var candidates = new List<OcrCandidate>();

            using (var multilingual = new Engine(
                dataPath,
                RecognitionLanguages,
                EngineMode.LstmOnly))
            {
                candidates.Add(RecognizePass(
                    multilingual,
                    prepared,
                    PageSegMode.Auto,
                    "balanced colour/contrast",
                    false));
                candidates.Add(RecognizePass(
                    multilingual,
                    thresholded,
                    PageSegMode.Auto,
                    "adaptive black and white",
                    false));

                // Sparse screen text and compact UI labels often score badly when
                // Tesseract assumes a document-shaped block.
                candidates.Add(RecognizePass(
                    multilingual,
                    prepared,
                    source.Width > source.Height * 2.5
                        ? PageSegMode.SingleLine
                        : PageSegMode.SparseText,
                    "screen text layout",
                    false));
            }

            // A dedicated Persian pass prevents the Latin models from winning merely
            // because they produced confident-looking gibberish from connected script.
            using (var persian = new Engine(dataPath, "fas", EngineMode.LstmOnly))
            {
                candidates.Add(RecognizePass(
                    persian,
                    prepared,
                    PageSegMode.Auto,
                    "Persian high-accuracy model",
                    true));
                candidates.Add(RecognizePass(
                    persian,
                    thresholded,
                    source.Width > source.Height * 2.5
                        ? PageSegMode.SingleLine
                        : PageSegMode.SparseText,
                    "Persian thresholded model",
                    true));
            }

            OcrCandidate? best = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
                .OrderByDescending(ScoreCandidate)
                .FirstOrDefault();
            if (best == null)
            {
                return Failed("No readable text was found in that region.");
            }

            return new OcrRecognitionResult(
                best.Text,
                $"Tesseract 5.5 multi-pass ({best.PassName})",
                DescribeLanguages(best.Text),
                Math.Clamp(best.Confidence, 0f, 1f),
                true,
                string.Empty);
        }

        private static OcrCandidate RecognizePass(
            Engine engine,
            Bitmap bitmap,
            PageSegMode segmentation,
            string passName,
            bool persianOnly)
        {
            using MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            using TesseractOCR.Pix.Image image =
                TesseractOCR.Pix.Image.LoadFromMemory(stream.ToArray());
            using Page page = engine.Process(image, segmentation);
            return new OcrCandidate(
                NormalizeRecognizedText(page.Text),
                Math.Clamp(page.MeanConfidence, 0f, 1f),
                passName,
                persianOnly);
        }

        private static double ScoreCandidate(OcrCandidate candidate)
        {
            int letters = candidate.Text.Count(char.IsLetterOrDigit);
            int persianLetters = candidate.Text.Count(
                character => character is >= '\u0600' and <= '\u06FF');
            double usefulRatio = letters / (double)Math.Max(1, candidate.Text.Length);
            double persianRatio = persianLetters / (double)Math.Max(1, letters);
            double score = candidate.Confidence * 100.0;

            score += Math.Min(8, Math.Log10(Math.Max(1, letters)) * 4);
            score += usefulRatio * 6;
            if (candidate.PersianOnly)
            {
                score += persianRatio >= 0.45 ? 8 : -12;
            }
            else if (persianRatio >= 0.45)
            {
                score += 3;
            }

            return score;
        }

        private static Bitmap PrepareForRecognition(Bitmap source)
        {
            const int maximumWidth = 3600;
            const int maximumHeight = 2400;
            const int borderPadding = 28;
            double desiredScale = source.Height switch
            {
                <= 70 => 7.0,
                <= 160 => 4.0,
                _ when source.Width < 1400 => 2.0,
                _ => 1.0
            };
            double scale = Math.Min(
                desiredScale,
                Math.Min(
                    maximumWidth / (double)Math.Max(1, source.Width),
                    maximumHeight / (double)Math.Max(1, source.Height)));
            scale = Math.Max(1.0, scale);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var prepared = new Bitmap(
                width + (borderPadding * 2),
                height + (borderPadding * 2),
                PixelFormat.Format24bppRgb);
            prepared.SetResolution(300, 300);

            using Graphics graphics = Graphics.FromImage(prepared);
            graphics.Clear(Color.White);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Grayscale plus a modest contrast lift improves low-resolution UI
            // text while preserving Persian dots and German umlauts.
            var colorMatrix = new ColorMatrix(
            [
                [0.36f, 0.36f, 0.36f, 0, 0],
                [0.50f, 0.50f, 0.50f, 0, 0],
                [0.14f, 0.14f, 0.14f, 0, 0],
                [0, 0, 0, 1, 0],
                [-0.04f, -0.04f, -0.04f, 0, 1.08f]
            ]);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);
            graphics.DrawImage(
                source,
                new Rectangle(
                    borderPadding,
                    borderPadding,
                    width,
                    height),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel,
                attributes);
            return prepared;
        }

        private static Bitmap ApplyOtsuThreshold(Bitmap grayscale)
        {
            var result = new Bitmap(
                grayscale.Width,
                grayscale.Height,
                PixelFormat.Format24bppRgb);
            Rectangle rectangle = new Rectangle(0, 0, grayscale.Width, grayscale.Height);
            BitmapData sourceData = grayscale.LockBits(
                rectangle,
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            BitmapData resultData = result.LockBits(
                rectangle,
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                int byteCount = Math.Abs(sourceData.Stride) * sourceData.Height;
                byte[] sourceBytes = new byte[byteCount];
                byte[] resultBytes = new byte[byteCount];
                Marshal.Copy(sourceData.Scan0, sourceBytes, 0, byteCount);

                var histogram = new long[256];
                for (int y = 0; y < grayscale.Height; y++)
                {
                    int row = y * sourceData.Stride;
                    for (int x = 0; x < grayscale.Width; x++)
                    {
                        histogram[sourceBytes[row + (x * 3)]]++;
                    }
                }

                int threshold = CalculateOtsuThreshold(
                    histogram,
                    grayscale.Width * grayscale.Height);
                for (int y = 0; y < grayscale.Height; y++)
                {
                    int sourceRow = y * sourceData.Stride;
                    int resultRow = y * resultData.Stride;
                    for (int x = 0; x < grayscale.Width; x++)
                    {
                        byte value = sourceBytes[sourceRow + (x * 3)] >= threshold
                            ? (byte)255
                            : (byte)0;
                        int index = resultRow + (x * 3);
                        resultBytes[index] = value;
                        resultBytes[index + 1] = value;
                        resultBytes[index + 2] = value;
                    }
                }

                Marshal.Copy(resultBytes, 0, resultData.Scan0, resultBytes.Length);
            }
            finally
            {
                grayscale.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }

            result.SetResolution(300, 300);
            return result;
        }

        private static int CalculateOtsuThreshold(long[] histogram, int pixelCount)
        {
            long weightedTotal = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                weightedTotal += value * histogram[value];
            }

            long backgroundWeight = 0;
            long backgroundTotal = 0;
            double maximumVariance = -1;
            int threshold = 127;
            for (int value = 0; value < histogram.Length; value++)
            {
                backgroundWeight += histogram[value];
                if (backgroundWeight == 0) continue;

                long foregroundWeight = pixelCount - backgroundWeight;
                if (foregroundWeight == 0) break;

                backgroundTotal += value * histogram[value];
                double backgroundMean = backgroundTotal / (double)backgroundWeight;
                double foregroundMean =
                    (weightedTotal - backgroundTotal) / (double)foregroundWeight;
                double difference = backgroundMean - foregroundMean;
                double variance =
                    backgroundWeight * (double)foregroundWeight * difference * difference;
                if (variance > maximumVariance)
                {
                    maximumVariance = variance;
                    threshold = value;
                }
            }

            return threshold;
        }

        private static async Task<OcrRecognitionResult> RecognizeWithWindowsAsync(Bitmap source)
        {
            using MemoryStream stream = new MemoryStream();
            source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                return Failed("No Windows OCR engine is available.");
            }

            OcrResult result = await engine.RecognizeAsync(softwareBitmap);
            string text = NormalizeRecognizedText(result.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Failed("No readable text was found in that region.");
            }

            return new OcrRecognitionResult(
                text,
                "Windows OCR fallback",
                DescribeLanguages(text),
                0f,
                true,
                string.Empty);
        }

        private static string NormalizeRecognizedText(string text)
        {
            return text
                .Replace('\u064A', '\u06CC') // Arabic yeh -> Persian yeh
                .Replace('\u0649', '\u06CC')
                .Replace('\u0643', '\u06A9') // Arabic kaf -> Persian kaf
                .Replace("\r\n", "\n")
                .Trim();
        }

        private static string DescribeLanguages(string text)
        {
            var languages = new List<string>();
            if (text.Any(character => character is >= '\u0600' and <= '\u06FF'))
                languages.Add("Persian");
            if (text.IndexOfAny(['ä', 'ö', 'ü', 'ß', 'Ä', 'Ö', 'Ü']) >= 0)
                languages.Add("German");
            if (text.Any(character => character is >= 'A' and <= 'z'))
                languages.Add("English / German");
            return languages.Count == 0
                ? "Automatic language detection"
                : string.Join(" + ", languages.Distinct());
        }

        private static OcrRecognitionResult Failed(string message) =>
            new(string.Empty, "OCR", "Persian + German + English", 0f, false, message);
    }
}
