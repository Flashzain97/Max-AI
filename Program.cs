namespace Max;

public static class Program
{
    public const string Version = "0.4.0";
    public static int Main(string[] args)
    {
        // A native Windows runner can otherwise lose Unicode on redirected
        // pipes. Embedded callers keep ownership of their host's text streams.
        if (System.Reflection.Assembly.GetEntryAssembly() == typeof(Program).Assembly)
        {
            if (Console.OutputEncoding.CodePage != 65001) Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            if (Console.InputEncoding.CodePage != 65001) Console.InputEncoding = new System.Text.UTF8Encoding(false);
        }
        return Execution.Run(() => Run(args));
    }

    private static int Run(string[] args)
    {
        string? source = null, file = null;
        try
        {
            if (args.Length > 0 && args[0] == "--studio") return Studio.Run(args[1..]);
            if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
            {
                PrintHelp();
                return 0;
            }
            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine($"Max {Version}");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTests.Run();

            bool check = false, strict = false, syntaxOnly = false, options = true;
            var seen = new HashSet<string>();
            foreach (string arg in args)
            {
                if (options && arg == "--") { options = false; continue; }
                if (options && arg.StartsWith("-", StringComparison.Ordinal))
                {
                    if (!seen.Add(arg)) throw new ArgumentException($"Option '{arg}' was supplied twice.");
                    switch (arg)
                    {
                        case "--check": check = true; break;
                        case "--strict": strict = true; break;
                        case "--syntax-only": syntaxOnly = true; break;
                        default: throw new ArgumentException($"Unknown option '{arg}'. Use --help for usage.");
                    }
                }
                else if (file == null) file = arg;
                else throw new ArgumentException("Expected one .max file. Use --help for usage.");
            }
            if (file == null) throw new ArgumentException("Supply a .max file. Use --help for usage.");
            if (syntaxOnly && (strict || check))
                throw new ArgumentException("Use --syntax-only by itself with a file; --check already includes syntax checks.");

            source = File.ReadAllText(file);
            var statements = new Parser(new Lexer(source).Tokenize()).Parse();
            if (syntaxOnly) Console.WriteLine("Syntax OK. Names and types were not checked.");
            else
            {
                StaticAnalyzer.Validate(statements, strict);
                if (check) Console.WriteLine(strict ? "Strict checks passed. The program was not run." : "Checks passed. The program was not run.");
                else new Interpreter().EvaluateAll(statements);
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Net.Sockets.SocketException or System.ComponentModel.Win32Exception || error.GetType().Namespace == "Max")
        {
            ReportError(error, source, file);
            return 1;
        }
    }

    private static void PrintHelp() => Console.WriteLine($"""
        Max {Version}
          max <file.max>                   Check and run a program
          max --strict <file.max>          Require explicit types, then run
          max --check [--strict] <file.max> Check without running
          max --syntax-only <file.max>     Check grammar only
          max --self-test                  Run regression tests
          max --version                    Show version
          max --studio                     Open the local editor

        Type checks also run while executing. Dynamic values may need runtime checks.
        Windows: use .\max.exe from this folder. File paths with spaces need quotes.
        """);

    private static void ReportError(Exception error, string? source, string? file)
    {
        string category = error switch
        {
            MaxSyntaxException => "Syntax error",
            MaxTypeException => "Type error",
            MaxVariableException => "Name error",
            MaxArithmeticException => "Arithmetic error",
            MaxComparisonException => "Comparison error",
            MaxIndexingException => "Index error",
            MaxInfiniteRecursionException => "Recursion error",
            IOException or UnauthorizedAccessException => "File error",
            ArgumentException => "Command error",
            System.Net.Sockets.SocketException or System.ComponentModel.Win32Exception => "Studio error",
            _ => "Value error"
        };
        if (error.Data["MaxLocation"] is SourcePosition location)
        {
            string message = error.Message;
            string repeatedLocation = $"Line {location.Line}, column {location.Column}: ";
            if (message.StartsWith(repeatedLocation, StringComparison.Ordinal)) message = message[repeatedLocation.Length..];
            Console.Error.WriteLine($"{file ?? "Source"}, line {location.Line}, column {location.Column}: {category}: {message}");
            if (source != null)
            {
                string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                if (location.Line > 0 && location.Line <= lines.Length)
                {
                    string line = lines[location.Line - 1];
                    int offset = Math.Clamp(location.Column - 1, 0, line.Length);
                    string prefix = line[..offset].Replace("\t", "    ");
                    Console.Error.WriteLine($"  {line.Replace("\t", "    ")}");
                    Console.Error.WriteLine($"  {new string(' ', prefix.Length)}^");
                }
            }
        }
        else Console.Error.WriteLine($"{category}: {error.Message}");
        if (error.Data["MaxCallStack"] is List<string> calls && calls.Count > 0)
            Console.Error.WriteLine($"Call stack: {FormatCalls(calls)}");
    }

    private static string FormatCalls(List<string> calls)
    {
        var ordered = calls.AsEnumerable().Reverse().ToArray();
        var groups = new List<string>();
        for (int i = 0; i < ordered.Length;)
        {
            int end = i + 1;
            while (end < ordered.Length && ordered[end] == ordered[i]) end++;
            groups.Add(end - i == 1 ? ordered[i] : $"{ordered[i]} ({end - i} calls)");
            i = end;
        }
        if (groups.Count > 16)
            groups = groups.Take(8).Concat([$"... {groups.Count - 16} call groups omitted ..."]).Concat(groups.TakeLast(8)).ToList();
        return string.Join(" -> ", groups);
    }
}
