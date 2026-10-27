using Dafeiyu;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskPet.Platform.Mac;

// Process transport is isolated from the UI/native player to verify real child lifetime.
internal sealed class SayProcess(Func<ProcessStartInfo>? factory = null)
{
    internal static ProcessStartInfo StartInfo()
    {
        var info = new ProcessStartInfo("/usr/bin/say")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--"); return info;
    }
    internal static IEnumerable<string> SplitPhrases(string text)
    {
        foreach (Match match in Regex.Matches(text, @"[^。！？.!?\r\n]+[。！？.!?]?|[。！？.!?]"))
        {
            var phrase = match.Value.Trim();
            for (var offset = 0; offset < phrase.Length;)
            {
                var length = Math.Min(180, phrase.Length - offset);
                if (offset + length < phrase.Length && char.IsHighSurrogate(phrase[offset + length - 1])) length--;
                yield return phrase.Substring(offset, length); offset += length;
            }
        }
    }
    internal async Task SpeakAsync(string text, int volume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = factory?.Invoke() ?? StartInfo() };
        if (!process.Start()) throw new InvalidOperationException(TextResources.Get("sayProcess.speakAsync.text"));
        using var kill = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { /* Cleanup retries and reports an actual kill failure. */ }
        });
        // Drain concurrently so a failing speech engine cannot fill its error pipe and deadlock.
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            var prefix = "[[volm " + (Math.Clamp(volume, 0, 100) / 100.0).ToString(CultureInfo.InvariantCulture) + "]]";
            await process.StandardInput.WriteAsync((prefix + text.Replace("[[", "［［")).AsMemory(), token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            await errors;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException(TextResources.Format("sayProcess.speakAsync.template",process.ExitCode));
        }
        finally
        {
            // Covers cancellation, broken stdin, and output-reader exceptions, not only the happy path.
            if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } }
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            try { await errors; } catch (IOException) { }
        }
    }
}
