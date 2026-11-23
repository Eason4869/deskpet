#if !PORTABLE
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinRt = Dafeiyu.WindowsMediaRuntime;

namespace Dafeiyu
{
    internal static class WindowsFileOcr
    {
        const int MaxPixels = 12000000;
        const int MaxTextBytes = 32 * 1024;
        const string Imaging = "Windows.Graphics.Imaging.";
        const string Pdf = "Windows.Data.Pdf.";
        const string Ocr = "Windows.Media.Ocr.";

        internal static string Recognize(string path, int page, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (page < 0) throw new ArgumentOutOfRangeException("page");
            string languageTag = NormalizeLanguage(language);
            // WinRT completion callbacks run through the thread pool, including when invoked from WPF's STA.
            return Task.Run(() => RecognizeCore(path, page, languageTag, token), token).GetAwaiter().GetResult();
        }

        static string RecognizeCore(string path, int page, string languageTag, CancellationToken token)
        {
            try
            {
                var runtime = new WindowsMediaRuntime(token);
                var engineType = WinRt.WinRtType(Ocr + "OcrEngine");
                object engine = CreateEngine(engineType, languageTag);
                if (engine == null) throw new NotSupportedException("The requested Windows OCR language is unavailable.");
                uint maximumDimension = (uint)WinRt.Property(engineType, "MaxImageDimension", null);
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    object source = runtime.Wrap(input), rendered = null, bitmap = null;
                    try
                    {
                        if (page > 0) rendered = RenderPdf(runtime, source, page, maximumDimension);
                        bitmap = DecodeImage(runtime, rendered ?? source, maximumDimension);
                        object result = runtime.Await(engineType, "RecognizeAsync", engine, bitmap);
                        return ReadLines(result, token);
                    }
                    finally
                    {
                        WinRt.Dispose(bitmap);
                        WinRt.Dispose(rendered);
                        WinRt.Dispose(source);
                    }
                }
            }
            catch (TypeLoadException error) { throw Unavailable(error); }
            catch (MissingMemberException error) { throw Unavailable(error); }
            catch (FileLoadException error) { throw Unavailable(error); }
            catch (FileNotFoundException error)
            {
                if (!File.Exists(path)) throw;
                throw Unavailable(error);
            }
            catch (COMException error)
            {
                uint hr = unchecked((uint)error.HResult);
                if (hr == 0x80040154 || hr == 0x80073D54 || hr == 0x80070032 || hr == 0x80004001 ||
                    hr == 0x8007007E || hr == 0x88982F50) throw Unavailable(error);
                throw new InvalidDataException("Windows could not decode the image or PDF for OCR.", error);
            }
        }

        static NotSupportedException Unavailable(Exception error)
        {
            return new NotSupportedException("Windows OCR or the required image decoder is unavailable.", error);
        }

        static string NormalizeLanguage(string language)
        {
            switch ((language ?? "").Trim().ToLowerInvariant())
            {
                case "": case "auto": return null;
                case "en": case "en-us": case "eng": return "en-US";
                case "zh": case "zh-cn": case "zh-hans": case "zh-hans-cn": case "chi_sim": return "zh-Hans-CN";
                default: throw new NotSupportedException("Select English or Simplified Chinese for Windows OCR.");
            }
        }

        static object CreateEngine(Type type, string tag)
        {
            if (tag != null) return CreateLanguageEngine(type, tag);
            return WinRt.Invoke(WinRt.Method(type, "TryCreateFromUserProfileLanguages", 0), null) ??
                CreateLanguageEngine(type, "en-US") ?? CreateLanguageEngine(type, "zh-Hans-CN");
        }

        static object CreateLanguageEngine(Type engineType, string tag)
        {
            var language = Activator.CreateInstance(WinRt.WinRtType("Windows.Globalization.Language"), new object[] { tag });
            return WinRt.Invoke(WinRt.Method(engineType, "TryCreateFromLanguage", 1), null, language);
        }

        static object RenderPdf(WindowsMediaRuntime runtime, object input, int pageNumber, uint maximumDimension)
        {
            var documentType = WinRt.WinRtType(Pdf + "PdfDocument");
            object document = runtime.Await(documentType, "LoadFromStreamAsync", null, input);
            if ((uint)WinRt.Property(documentType, "PageCount", document) < (uint)pageNumber)
                throw new ArgumentOutOfRangeException("pageNumber");
            object page = WinRt.Invoke(WinRt.Method(documentType, "GetPage", 1), document, (uint)(pageNumber - 1));
            object output = null;
            try
            {
                var pageType = WinRt.WinRtType(Pdf + "PdfPage");
                object size = WinRt.Property(pageType, "Size", page);
                double width = Convert.ToDouble(WinRt.Property(size.GetType(), "Width", size));
                double height = Convert.ToDouble(WinRt.Property(size.GetType(), "Height", size));
                if (!(width > 0) || !(height > 0) || Double.IsInfinity(width) || Double.IsInfinity(height))
                    throw new InvalidDataException("The PDF page has invalid dimensions.");
                double factor = Math.Min(2.0, Math.Min(maximumDimension / Math.Max(width, height), Math.Sqrt(MaxPixels / (width * height))));
                var optionsType = WinRt.WinRtType(Pdf + "PdfPageRenderOptions");
                object options = Activator.CreateInstance(optionsType);
                optionsType.GetProperty("DestinationWidth").SetValue(options, (uint)Math.Max(1, Math.Floor(width * factor)), null);
                optionsType.GetProperty("DestinationHeight").SetValue(options, (uint)Math.Max(1, Math.Floor(height * factor)), null);
                optionsType.GetProperty("IsIgnoringHighContrast").SetValue(options, true, null);
                var streamType = WinRt.WinRtType("Windows.Storage.Streams.InMemoryRandomAccessStream");
                output = Activator.CreateInstance(streamType);
                runtime.Await(pageType, "RenderToStreamAsync", page, output, options);
                WinRt.Invoke(WinRt.Method(streamType, "Seek", 1), output, 0UL);
                return output;
            }
            catch
            {
                WinRt.Dispose(output);
                throw;
            }
            finally { WinRt.Dispose(page); }
        }

        static object DecodeImage(WindowsMediaRuntime runtime, object source, uint maximumDimension)
        {
            var decoderType = WinRt.WinRtType(Imaging + "BitmapDecoder");
            object decoder = runtime.Await(decoderType, "CreateAsync", null, source);
            uint width = (uint)WinRt.Property(decoderType, "OrientedPixelWidth", decoder);
            uint height = (uint)WinRt.Property(decoderType, "OrientedPixelHeight", decoder);
            if (width == 0 || height == 0 || width > maximumDimension || height > maximumDimension || (ulong)width * height > MaxPixels)
                throw new InvalidDataException("The OCR image exceeds the supported pixel dimensions.");
            var transform = Activator.CreateInstance(WinRt.WinRtType(Imaging + "BitmapTransform"));
            var bitmap = runtime.Await(decoderType, "GetSoftwareBitmapAsync", decoder,
                WinRt.EnumValue(Imaging + "BitmapPixelFormat", "Bgra8"), WinRt.EnumValue(Imaging + "BitmapAlphaMode", "Premultiplied"), transform,
                WinRt.EnumValue(Imaging + "ExifOrientationMode", "RespectExifOrientation"), WinRt.EnumValue(Imaging + "ColorManagementMode", "DoNotColorManage"));
            try
            {
                if (!Equals(WinRt.Property(decoderType, "BitmapAlphaMode", decoder), WinRt.EnumValue(Imaging + "BitmapAlphaMode", "Ignore")))
                    PaintWhiteBackground(runtime, bitmap, width, height);
                return bitmap;
            }
            catch { WinRt.Dispose(bitmap); throw; }
        }

        static void PaintWhiteBackground(WindowsMediaRuntime runtime, object bitmap, uint width, uint height)
        {
            var type = WinRt.WinRtType(Imaging + "SoftwareBitmap");
            var pixels = new byte[(int)(width * height * 4)];
            object buffer = runtime.WrapBuffer(pixels);
            WinRt.Invoke(WinRt.Method(type, "CopyToBuffer", 1), bitmap, buffer);
            // The decoder has already premultiplied the color channels, so blending onto white adds the inverse alpha.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if ((i & 65535) == 0) runtime.CheckCancellation();
                int inverseAlpha = 255 - pixels[i + 3];
                if (inverseAlpha == 0) continue;
                pixels[i] = (byte)Math.Min(255, pixels[i] + inverseAlpha);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + inverseAlpha);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + inverseAlpha);
                pixels[i + 3] = 255;
            }
            WinRt.Invoke(WinRt.Method(type, "CopyFromBuffer", 1), bitmap, buffer);
        }

        static string ReadLines(object result, CancellationToken token)
        {
            var lines = (IEnumerable)WinRt.Property(WinRt.WinRtType(Ocr + "OcrResult"), "Lines", result);
            var lineType = WinRt.WinRtType(Ocr + "OcrLine");
            var output = new StringBuilder();
            const string marker = "\n[OCR text truncated]";
            int remaining = MaxTextBytes - marker.Length;
            foreach (object line in lines)
            {
                token.ThrowIfCancellationRequested();
                string text = (string)WinRt.Property(lineType, "Text", line);
                int bytes = Encoding.UTF8.GetByteCount(text) + 1;
                if (bytes > remaining)
                {
                    int kept = 0;
                    while (kept < text.Length)
                    {
                        char ch = text[kept];
                        int count = Char.IsHighSurrogate(ch) && kept + 1 < text.Length && Char.IsLowSurrogate(text[kept + 1]) ? 2 : 1;
                        int cost = count == 2 ? 4 : ch <= 0x7f ? 1 : ch <= 0x7ff ? 2 : 3;
                        if (cost > remaining) break;
                        remaining -= cost;
                        kept += count;
                    }
                    output.Append(text, 0, kept).Append(marker);
                    break;
                }
                output.Append(text).Append('\n');
                remaining -= bytes;
            }
            return output.ToString().TrimEnd('\n');
        }

    }

    internal sealed class WindowsMediaRuntime
    {
        internal static Type WinRtType(string name) { return Type.GetType(name + ", Windows, ContentType=WindowsRuntime", true); }
        internal static object EnumValue(string type, string value) { return Enum.Parse(WinRtType(type), value); }
        internal static void Dispose(object value) { var owned = value as IDisposable; if (owned != null) owned.Dispose(); }
        internal static object Property(Type type, string name, object instance)
        {
            var property = type.GetProperty(name);
            if (property == null) throw new MissingMemberException(type.FullName, name);
            return Invoke(property.GetGetMethod(), instance);
        }

        internal static MethodInfo Method(Type type, string name, int arity)
        {
            var method = type.GetMethods().SingleOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arity);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        internal static object Invoke(MethodInfo method, object owner, params object[] parameters)
        {
            try { return method.Invoke(owner, parameters); }
            catch (TargetInvocationException error)
            {
                if (error.InnerException != null) ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        const string RuntimeAssembly = "System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        readonly CancellationToken token;
        readonly MethodInfo bridgeAction, bridgeResult, wrapStream, wrapBuffer, readStream;

        internal WindowsMediaRuntime(CancellationToken token)
        {
            this.token = token;
            var assembly = Assembly.Load(RuntimeAssembly);
            var extensions = assembly.GetType("System.WindowsRuntimeSystemExtensions", true);
            var cancellable = extensions.GetMethods().Where(method => method.Name == "AsTask" && method.GetParameters().Length == 2 &&
                method.GetParameters()[1].ParameterType == typeof(CancellationToken)).ToList();
            bridgeAction = cancellable.Single(method => !method.IsGenericMethod);
            bridgeResult = cancellable.Single(method => method.IsGenericMethod && method.ReturnType.IsGenericType && method.GetGenericArguments().Length == 1);
            var streams = assembly.GetType("System.IO.WindowsRuntimeStreamExtensions", true);
            wrapStream = Method(streams, "AsRandomAccessStream", 1);
            readStream = Method(streams, "AsStreamForRead", 1);
            wrapBuffer = Method(assembly.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions", true), "AsBuffer", 1);
        }

        internal object Wrap(Stream stream) { return Invoke(wrapStream, null, stream); }
        internal object WrapBuffer(byte[] bytes) { return Invoke(wrapBuffer, null, bytes); }
        internal Stream OpenForRead(object stream) { return (Stream)Invoke(readStream, null, stream); }
        internal void CheckCancellation() { token.ThrowIfCancellationRequested(); }

        internal object Await(Type type, string name, object owner, params object[] parameters)
        {
            token.ThrowIfCancellationRequested();
            var method = Method(type, name, parameters.Length);
            var operation = Invoke(method, owner, parameters);
            var resultType = method.ReturnType.IsGenericType ? method.ReturnType.GetGenericArguments()[0] : null;
            var bridge = resultType == null ? bridgeAction : bridgeResult.MakeGenericMethod(resultType);
            var task = (Task)Invoke(bridge, null, operation, token);
            task.GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            return resultType == null ? null : task.GetType().GetProperty("Result").GetValue(task, null);
        }
    }
}
#endif
