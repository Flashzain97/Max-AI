using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Max;

public static class Studio
{
    // Set by the source runner; published builds embed the page instead.
    public static string? SourceDirectory { get; set; }
    private const int MaximumText = 65536;
    private const int MaximumRequest = 1024 * 1024;
    private static readonly SemaphoreSlim Runs = new(2);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Process> Children = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public static int Run(string[] args)
    {
        int port = 7337;
        bool browser = true;
        var options = new HashSet<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!options.Add(args[i])) throw new ArgumentException($"Option '{args[i]}' was supplied twice.");
            if (args[i] == "--no-browser") browser = false;
            else if (args[i] == "--port" && ++i < args.Length && int.TryParse(args[i], out port) && port is >= 0 and <= 65535) { }
            else throw new ArgumentException("Studio usage: max --studio [--no-browser] [--port 0..65535].");
        }
        byte[] page = ReadPage();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(8); }
        catch (SocketException) when (port != 0 && ExistingStudio(port))
        {
            string existingUrl = $"http://127.0.0.1:{port}/";
            Console.WriteLine($"Max Studio is already running.\n{existingUrl}");
            if (browser) OpenBrowser(existingUrl);
            return 0;
        }
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var programs = new StudioProcessGroup();
        string url = $"http://127.0.0.1:{port}/";
        Console.WriteLine($"Max Studio\n{url}\nKeep this window open. Press Ctrl+C to stop the editor.");
        using var shutdown = new CancellationTokenSource();
        int stopping = 0;
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; Interlocked.Exchange(ref stopping, 1); shutdown.Cancel(); listener.Stop(); };
        Console.CancelKeyPress += stop;
        if (browser)
        {
            OpenBrowser(url);
        }
        // Handlers may be completing when the listener shuts down.
        var connections = new SemaphoreSlim(8);
        var handlers = new System.Collections.Concurrent.ConcurrentDictionary<long, Task>();
        long nextHandler = 0;
        try
        {
            while (Volatile.Read(ref stopping) == 0)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception error) when (Volatile.Read(ref stopping) != 0 && error is SocketException or InvalidOperationException) { break; }
                if (!connections.Wait(1000)) { client.Dispose(); continue; }
                if (Volatile.Read(ref stopping) != 0) { client.Dispose(); connections.Release(); break; }
                long id = Interlocked.Increment(ref nextHandler);
                var handler = Task.Run(async () =>
                {
                    try { await Serve(client, port, page, programs, shutdown.Token); }
                    finally { client.Dispose(); connections.Release(); }
                });
                handlers[id] = handler;
                _ = handler.ContinueWith(completed => { handlers.TryRemove(id, out _); }, TaskScheduler.Default);
            }
        }
        finally
        {
            Console.CancelKeyPress -= stop;
            shutdown.Cancel();
            foreach (var child in Children.Values) Kill(child);
            try { Task.WhenAll(handlers.Values).Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
        }
        return 0;
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Console.WriteLine("Open the address above in your browser."); }
    }
    private static bool ExistingStudio(int port)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            string response = http.GetStringAsync($"http://127.0.0.1:{port}/api/info").GetAwaiter().GetResult();
            using var info = JsonDocument.Parse(response);
            return info.RootElement.ValueKind == JsonValueKind.Object && info.RootElement.TryGetProperty("app", out var app) &&
                app.ValueKind == JsonValueKind.String && app.GetString() == "Max Studio";
        }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException or JsonException) { return false; }
    }

    private static byte[] ReadPage()
    {
        using var resource = typeof(Studio).Assembly.GetManifestResourceStream("Max.Studio.html");
        if (resource != null)
        {
            using var bytes = new MemoryStream(); resource.CopyTo(bytes); return bytes.ToArray();
        }
        if (SourceDirectory != null) return File.ReadAllBytes(Path.Combine(SourceDirectory, "Studio.html"));
        throw new IOException("Studio.html is missing. Rebuild Max or use Run-Max.ps1 from the source folder.");
    }

    private static async Task Serve(TcpClient client, int port, byte[] page, StudioProcessGroup programs, CancellationToken shutdown)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stream = client.GetStream();
        try
        {
            var request = await ReadRequest(stream, timeout.Token);
            if (request.Headers.GetValueOrDefault("Host") is not string host ||
                (host != $"127.0.0.1:{port}" && host != $"localhost:{port}"))
            { await Reply(stream, 403, "text/plain", "Use the local Studio address."); return; }
            if (request.Headers.TryGetValue("Origin", out var origin) &&
                origin != $"http://127.0.0.1:{port}" && origin != $"http://localhost:{port}")
            { await Reply(stream, 403, "text/plain", "This request must come from Max Studio."); return; }
            if (request.Method == "GET" && request.Path == "/")
                await Reply(stream, 200, "text/html; charset=utf-8", page);
            else if (request.Method == "GET" && request.Path == "/api/info")
                await JsonReply(stream, 200, new { app = "Max Studio", version = Program.Version });
            else if (request.Method == "POST" && request.Path is "/api/run" or "/api/check")
            {
                if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                { await Reply(stream, 400, "text/plain", "Send a JSON request."); return; }
                var program = JsonSerializer.Deserialize<StudioProgram>(request.Body, JsonOptions);
                if (program == null || program.Code == null || program.Code.Length > MaximumText || (program.Input?.Length ?? 0) > MaximumText)
                { await JsonReply(stream, 400, new { error = "Code and input must each be at most 64 KB of text." }); return; }
                await Runs.WaitAsync(timeout.Token);
                try { await JsonReply(stream, 200, await Execute(program, request.Path == "/api/check", programs, timeout.Token)); }
                finally { Runs.Release(); }
            }
            else await Reply(stream, 404, "text/plain", "Page not found.");
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or JsonException or ArgumentException)
        {
            try { await JsonReply(stream, 400, new { error = "The request was invalid or the connection expired." }); }
            catch (Exception retry) when (retry is IOException or SocketException) { }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try { await JsonReply(stream, 500, new { error = "Max could not start. Rebuild Max and try again." }); }
            catch (Exception retry) when (retry is IOException or SocketException) { }
        }
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Headers, byte[] Body);
    private static async Task<Request> ReadRequest(NetworkStream stream, CancellationToken cancellation)
    {
        // This local server supports one bounded HTTP/1 request per connection.
        // Reject ambiguous framing instead of guessing how to interpret it.
        using var headerBytes = new MemoryStream();
        byte[] one = new byte[1];
        int terminator = 0;
        while (terminator != 4)
        {
            if (headerBytes.Length >= 16384 || await stream.ReadAsync(one, cancellation) == 0) throw new IOException("Invalid headers.");
            headerBytes.WriteByte(one[0]);
            byte expected = terminator is 0 or 2 ? (byte)'\r' : (byte)'\n';
            terminator = one[0] == expected ? terminator + 1 : one[0] == '\r' ? 1 : 0;
        }
        string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
        string[] start = lines[0].Split(' ');
        if (start.Length != 3 || start[2] is not ("HTTP/1.1" or "HTTP/1.0")) throw new IOException("Invalid request line.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1).Where(line => line.Length != 0))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || !headers.TryAdd(line[..separator], line[(separator + 1)..].Trim())) throw new IOException("Invalid or duplicate header.");
        }
        if (headers.ContainsKey("Transfer-Encoding")) throw new IOException("Transfer encoding is not supported.");
        int length = 0;
        if (headers.TryGetValue("Content-Length", out var value) &&
            (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length) || length > MaximumRequest))
            throw new IOException("Invalid content length.");
        if (start[0] == "POST" && !headers.ContainsKey("Content-Length")) throw new IOException("Missing content length.");
        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int count = await stream.ReadAsync(body.AsMemory(read), cancellation);
            if (count == 0) throw new IOException("Incomplete request.");
            read += count;
        }
        return new(start[0], start[1], headers, body);
    }

    public sealed record StudioProgram(string Code, string? Input = null, bool Strict = true);
    public sealed record StudioResult(int ExitCode, string Output, string Error, long ElapsedMs, int? Line = null, int? Column = null);
    internal static async Task<StudioResult> Execute(StudioProgram program, bool check, StudioProcessGroup? programs = null, CancellationToken cancellation = default)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Max-Studio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "program.max");
        var timer = Stopwatch.StartNew();
        using var process = new Process();
        int? childId = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(file, program.Code);
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Could not locate the Max runner."))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
              StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
            if (SourceDirectory != null)
            {
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File");
                start.ArgumentList.Add(Path.Combine(SourceDirectory, "Run-Max.ps1"));
            }
            else if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            if (check) start.ArgumentList.Add("--check");
            if (program.Strict) start.ArgumentList.Add("--strict");
            start.ArgumentList.Add(file);
            process.StartInfo = start;
            process.Start();
            programs?.AssignProcess(process);
            childId = process.Id;
            Children[childId.Value] = process;
            if (cancellation.IsCancellationRequested) Kill(process);
            int truncated = 0;
            async Task<string> ReadOutput(StreamReader reader)
            {
                var text = new StringBuilder();
                char[] buffer = new char[4096];
                int count;
                while ((count = await reader.ReadAsync(buffer)) != 0)
                {
                    if (text.Length + count > MaximumText)
                    {
                        text.Append(buffer, 0, MaximumText - text.Length);
                        Interlocked.Exchange(ref truncated, 1); Kill(process); break;
                    }
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
            Task<string> outputTask = ReadOutput(process.StandardOutput), errorTask = ReadOutput(process.StandardError);
            bool expired = false;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            var inputTask = Task.Run(async () =>
            {
                try
                {
                    await process.StandardInput.WriteAsync((program.Input ?? "").AsMemory(), limit.Token);
                    await process.StandardInput.FlushAsync(limit.Token);
                }
                catch (IOException) { /* The child may reject source before reading input. */ }
                finally
                {
                    try { process.StandardInput.Close(); }
                    catch (IOException) { }
                }
            });
            try
            {
                await Task.WhenAll(inputTask, process.WaitForExitAsync()).WaitAsync(limit.Token);
            }
            catch (OperationCanceledException) { expired = true; Kill(process); }
            catch (IOException) { /* A quick syntax failure may close input before it is written. */ }
            finally
            {
                if (!process.HasExited) Kill(process);
                await process.WaitForExitAsync();
                try { await inputTask; }
                catch (OperationCanceledException) { }
            }
            string output = await outputTask;
            string error = (await errorTask).Replace(file, "program.max", StringComparison.Ordinal);
            if (expired) error = cancellation.IsCancellationRequested ? "The editor stopped this program." : "Program stopped after 10 seconds. Check for a loop that never ends.";
            else if (truncated != 0) error = "Program stopped after printing too much text (64 KB). Reduce its output and try again.";
            Match location = Regex.Match(error, @", line (\d+), column (\d+):");
            return new(expired || truncated != 0 ? 1 : process.ExitCode, output, error, timer.ElapsedMilliseconds,
                location.Success ? int.Parse(location.Groups[1].Value) : null,
                location.Success ? int.Parse(location.Groups[2].Value) : null);
        }
        finally
        {
            if (childId is int id) Children.TryRemove(id, out _);
            Kill(process);
            Directory.Delete(directory, true);
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    private static Task JsonReply(NetworkStream stream, int status, object value) =>
        Reply(stream, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static Task Reply(NetworkStream stream, int status, string type, string text) => Reply(stream, status, type, Encoding.UTF8.GetBytes(text));
    private static async Task Reply(NetworkStream stream, int status, string type, byte[] body)
    {
        string reason = status switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 500 => "Internal Server Error", _ => "Bad Request" };
        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; frame-ancestors 'none'\r\n\r\n");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stream.WriteAsync(headers, timeout.Token); await stream.WriteAsync(body, timeout.Token);
    }
}
