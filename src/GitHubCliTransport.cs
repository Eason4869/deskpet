using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal sealed class GitHubCliTransport
    {
        const int MaximumBodyBytes = 2 * 1024 * 1024, MaximumHeaderBytes = 16 * 1024;
        const string Hostname = "github.com";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly Regex RepositoryName = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant);
        static readonly Regex IssueNumber = new Regex(@"\A[0-9]+\z", RegexOptions.CultureInvariant);
        static readonly Regex StatusLine = new Regex(@"\AHTTP/[0-9.]+ ([0-9]{3})(?: |\r?\n|\z)", RegexOptions.CultureInvariant);
        static readonly string[] EnvironmentNames = {
            "SystemRoot", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE",
            "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "HOMEDRIVE", "HOMEPATH", "XDG_CONFIG_HOME"
        };

        internal string ExecutablePath { get; private set; }

        GitHubCliTransport(string executablePath) { ExecutablePath = executablePath; }

        internal static GitHubCliTransport Find()
        {
            bool windows = WindowsCommandRunner.IsWindowsHost;
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).AsEnumerable();
            if (windows)
            {
                var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (programs.Length > 0) directories = new[] { Path.Combine(programs, "GitHub CLI") }.Concat(directories);
            }
            foreach (string entry in directories)
            {
                string directory = entry.Trim().Trim('"');
                if (windows ? !Regex.IsMatch(directory, @"\A[A-Za-z]:[\\/]") : !Path.IsPathRooted(directory)) continue;
                try
                {
                    string executable = Path.GetFullPath(Path.Combine(directory, windows ? "gh.exe" : "gh"));
                    if (File.Exists(executable)) return new GitHubCliTransport(executable);
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return null;
        }

        internal async Task<string> SendAsync(HttpMethod method, string path, string jsonBody, CancellationToken cancellationToken,
            string proxyAddress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AllowsRequest(method, path) || (method == HttpMethod.Post) != (jsonBody != null))
                throw new ArgumentException(TextResources.Get("gitHubClient.executeCoreAsync.text"));
            byte[] body = jsonBody == null ? null : Utf8.GetBytes(jsonBody);
            if (body != null && body.Length > MaximumBodyBytes) throw OutputLimit();

            var arguments = new List<string> { "api", "--hostname", Hostname, "--method", method.Method, "--include",
                "--header", "Accept: application/vnd.github+json", "--header", "X-GitHub-Api-Version: 2022-11-28" };
            if (body != null) arguments.AddRange(new[] { "--input", "-", "--header", "Content-Type: application/json" });
            arguments.Add("--");
            // gh accepts absolute endpoints without consulting a configured api_host.
            arguments.Add(GitHubClient.PublicApi.TrimEnd('/') + path);

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var process = new Process())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    process.StartInfo = ApiStartInfo(arguments, proxyAddress);
                    if (!process.Start()) throw RequestFailed();
                    using (timeout.Token.Register(() => Stop(process)))
                    {
                        var output = ReadOutputAsync(process, timeout.Token);
                        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                        var input = WriteInputAsync(process, body, timeout.Token);
                        var exit = Task.Run(() => process.WaitForExit());
                        await Task.WhenAll(output, input, error, exit).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        return Response(output.Result, process.ExitCode);
                    }
                }
                catch (Exception error)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (error is ApiException) throw;
                    if (!(error is System.ComponentModel.Win32Exception || error is IOException || error is InvalidOperationException ||
                        error is System.Security.SecurityException || error is ArgumentException)) throw;
                    throw RequestFailed();
                }
            }
        }

        static bool AllowsRequest(HttpMethod method, string path)
        {
            if ((method != HttpMethod.Get && method != HttpMethod.Post) || String.IsNullOrEmpty(path) || path.Length > 16000 ||
                path[0] != '/' || path.Any(Char.IsWhiteSpace) || path.Any(Char.IsControl) ||
                path.IndexOfAny(new[] { '#', ':', '"', '\\', '{', '}' }) >= 0) return false;
            int query = path.IndexOf('?');
            string endpoint = query < 0 ? path : path.Substring(0, query);
            if (method == HttpMethod.Post && query >= 0) return false;
            if (endpoint == "/user") return method == HttpMethod.Get && query < 0;
            if (endpoint == "/user/repos") return true;
            if (endpoint == "/search/code") return method == HttpMethod.Get && query >= 0;

            string[] segments = endpoint.Split('/');
            if (segments.Length < 4 || segments[0].Length != 0 || segments[1] != "repos" ||
                !RepositoryName.IsMatch(segments[2]) || !RepositoryName.IsMatch(segments[3])) return false;
            if (segments.Length == 4) return method == HttpMethod.Get;
            if (segments[4] == "pulls") return segments.Length == 5;
            if (segments[4] == "issues")
            {
                if (segments.Length == 5) return method == HttpMethod.Post;
                if (!IssueNumber.IsMatch(segments[5])) return false;
                return method == HttpMethod.Get ? segments.Length == 6 : segments.Length == 7 && segments[6] == "comments";
            }
            if (segments[4] != "contents" || method != HttpMethod.Get) return false;
            for (int index = 5; index < segments.Length; index++)
            {
                string segment = Uri.UnescapeDataString(segments[index]);
                if (segment.Length == 0 && index == segments.Length - 1) return true;
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.Any(Char.IsControl) ||
                    segment.IndexOfAny(new[] { '/', '\\' }) >= 0) return false;
            }
            return true;
        }

        ProcessStartInfo ApiStartInfo(List<string> arguments, string proxyAddress)
        {
            var start = new ProcessStartInfo { FileName = ExecutablePath, WorkingDirectory = WorkingDirectory(), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
#if PORTABLE
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
#else
            start.Arguments = String.Join(" ", arguments.Select(WindowsCommandRunner.Quote));
#endif
            start.EnvironmentVariables.Clear();
            foreach (var item in ProcessEnvironment(false, proxyAddress)) start.EnvironmentVariables[item.Key] = item.Value;
            return start;
        }

        static string WorkingDirectory()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return system.Length > 0 ? system : AppDomain.CurrentDomain.BaseDirectory;
        }

        static SortedDictionary<string, string> ProcessEnvironment(bool interactive, string proxyAddress)
        {
            var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in EnvironmentNames)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (!String.IsNullOrEmpty(value)) result[name] = value;
            }
            result["GH_TELEMETRY"] = "0";
            result["GH_NO_UPDATE_NOTIFIER"] = "1";
            if (!String.IsNullOrEmpty(proxyAddress)) result["HTTPS_PROXY"] = ProxyConfiguration.Endpoint(proxyAddress).AbsoluteUri;
            if (!interactive)
            {
                result["GH_PROMPT_DISABLED"] = "1";
                result["GH_PAGER"] = "cat";
                result["NO_COLOR"] = "1";
            }
            return result;
        }

        static async Task WriteInputAsync(Process process, byte[] body, CancellationToken token)
        {
            using (var input = process.StandardInput.BaseStream)
                if (body != null) await input.WriteAsync(body, 0, body.Length, token).ConfigureAwait(false);
        }

        static async Task<string> ReadOutputAsync(Process process, CancellationToken token)
        {
            try
            {
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    var stream = process.StandardOutput.BaseStream;
                    int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > MaximumBodyBytes + MaximumHeaderBytes) throw OutputLimit();
                        output.Write(buffer, 0, count);
                    }
                    return Utf8.GetString(output.GetBuffer(), 0, (int)output.Length);
                }
            }
            catch { Stop(process); throw; }
        }

        static string Response(string output, int exitCode)
        {
            var statusLine = StatusLine.Match(output);
            if (!statusLine.Success) throw RequestFailed();
            int status = Int32.Parse(statusLine.Groups[1].Value, CultureInfo.InvariantCulture);
            if (status < 200 || status >= 300) throw new ApiException(TextResources.Format("gitHubClient.sendJsonAsync.template", status));
            if (exitCode != 0) throw RequestFailed();
            int lineStart = 0;
            while (lineStart < output.Length && lineStart <= MaximumHeaderBytes)
            {
                int newline = output.IndexOf('\n', lineStart);
                if (newline < 0 || newline > MaximumHeaderBytes) break;
                if (newline == lineStart || (newline == lineStart + 1 && output[lineStart] == '\r'))
                {
                    string body = output.Substring(newline + 1);
                    if (Utf8.GetByteCount(body) > MaximumBodyBytes) throw OutputLimit();
                    return body;
                }
                lineStart = newline + 1;
            }
            throw RequestFailed();
        }

        static void Stop(Process process)
        {
            try
            {
#if PORTABLE
                process.Kill(true);
#else
                process.Kill();
#endif
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        static ApiException RequestFailed() { return new ApiException(TextResources.Get("gitHubClient.executeAsync.text.alternativeA")); }
        static ApiException OutputLimit() { return new ApiException(TextResources.Get("gitHubClient.sendJsonAsync.text")); }

        internal void OpenLogin(string proxyAddress = null)
        {
            if (!WindowsCommandRunner.IsWindowsHost) throw new PlatformNotSupportedException();
            const uint NewConsole = 0x10, UnicodeEnvironment = 0x400;
            var command = new StringBuilder(WindowsCommandRunner.Quote(ExecutablePath) + " auth login --web --hostname github.com --skip-ssh-key");
            var environment = new StringBuilder();
            foreach (var item in ProcessEnvironment(true, proxyAddress)) environment.Append(item.Key).Append('=').Append(item.Value).Append('\0');
            environment.Append('\0');
            IntPtr block = Marshal.StringToHGlobalUni(environment.ToString());
            try
            {
                var start = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)) };
                ProcessInformation info;
                if (!CreateProcess(ExecutablePath, command, IntPtr.Zero, IntPtr.Zero, false, NewConsole | UnicodeEnvironment, block,
                    WorkingDirectory(), ref start, out info)) throw RequestFailed();
                CloseHandle(info.Thread);
                CloseHandle(info.Process);
            }
            finally { Marshal.FreeHGlobal(block); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            internal int Size;
            internal string Reserved, Desktop, Title;
            internal int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
            internal short ShowWindow, ReservedSize;
            internal IntPtr ReservedData, Input, Output, Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation { internal IntPtr Process, Thread; internal int ProcessId, ThreadId; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
    }
}
