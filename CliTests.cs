namespace Max;

public static class CliTests
{
    public static void Run(Action<string, Action> test)
    {
        test("CLI version", () =>
        {
            var result = Invoke(["--version"]);
            Assert(result.Code == 0 && result.Output.Contains(Program.Version), "Version was not shown.");
        });
        test("CLI rejects unknown and duplicate options", () =>
        {
            Assert(Invoke(["--unknown"]).Code == 1, "Unknown option accepted.");
            Assert(Invoke(["--check", "--check", "missing.max"]).Code == 1, "Duplicate option accepted.");
            Assert(Invoke(["--help", "missing.max"]).Code == 1, "Extra help argument accepted.");
            Assert(Invoke(["--studio", "--port", "bad"]).Code == 1, "Invalid Studio port accepted.");
            Assert(Invoke(["--studio", "--no-browser", "--no-browser"]).Code == 1, "Duplicate Studio option accepted.");
        });
        test("CLI missing file reports a file error", () =>
        {
            var result = Invoke([Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".max")]);
            Assert(result.Code == 1 && result.Error.Contains("File error"), "Missing file was not reported.");
        });
        test("CLI check never prints or requests input", () => WithFile("say input(\"Do not ask: \")", file =>
        {
            var result = Invoke(["--check", file]);
            Assert(result.Code == 0 && !result.Output.Contains("Do not ask:"), "Check ran the program.");
        }));
        test("CLI strict and check flags in either order", () => WithFile("declare n as int = 2 say n", file =>
        {
            foreach (var args in new[] { new[] { "--strict", "--check", file }, new[] { file, "--check", "--strict" } })
            {
                var result = Invoke(args);
                Assert(result.Code == 0 && result.Output.StartsWith("Strict checks passed"), "Strict check failed.");
            }
        }));
        test("CLI preflight prevents output before known error", () => WithFile("say \"do not print\"\nsay unknown", file =>
        {
            var result = Invoke([file]);
            Assert(result.Code == 1 && result.Output == "" && result.Error.Contains("Name error"), "Preflight did not stop the program.");
            Assert(result.Error.Contains("line 2") && result.Error.Contains("^"), "Missing source context.");
        }));
        test("CLI syntax-only distinguishes grammar from semantic checks", () => WithFile("say unknown", file =>
        {
            var result = Invoke(["--syntax-only", file]);
            Assert(result.Code == 0 && result.Output.Contains("Names and types were not checked"), "Syntax-only failed.");
            Assert(Invoke(["--syntax-only", "--strict", file]).Code == 1, "Conflicting options accepted.");
        }));
        test("CLI runtime error points to operator and call chain", () => WithFile("function divide(n as int) returns int do\n    return 10 / n\nconclude\nsay divide(0)", file =>
        {
            var result = Invoke(["--strict", file]);
            Assert(result.Code == 1 && result.Error.Contains("line 2, column 15") && result.Error.Contains("Call stack: divide"), result.Error);
        }));
        test("CLI strict rejects untyped declaration", () => WithFile("declare score = 4", file =>
        {
            Assert(Invoke(["--strict", file]).Code == 1, "Strict mode accepted an untyped declaration.");
        }));
        test("CLI separator and quoted-path equivalent", () => WithFile("say 42", file =>
        {
            var result = Invoke(["--", file]);
            Assert(result.Code == 0 && result.Output.Trim() == "42", "File path failed.");
        }));
        test("public interpreter reports recursion safely from a small-stack host", () =>
        {
            Exception? failure = null;
            var caller = new System.Threading.Thread(() =>
            {
                try
                {
                    var statements = new Parser(new Lexer("function f() do return f() conclude f()").Tokenize()).Parse();
                    new Interpreter().EvaluateAll(statements);
                }
                catch (Exception error) { failure = error; }
            }, 256 * 1024);
            caller.Start(); caller.Join();
            Assert(failure is MaxInfiniteRecursionException && failure.Data["MaxCallStack"] is List<string>, "Expected a safe recursion error from a small-stack caller.");
        });
        test("CLI recursive call chain stays readable", () => WithFile("function cycle() returns nothing do cycle() conclude cycle()", file =>
        {
            var result = Invoke(["--strict", file]);
            Assert(result.Code == 1 && result.Error.Contains("Recursion error") && result.Error.Contains("calls)") && result.Error.Length < 1000, "Recursion diagnostic was not concise.");
        }));
    }

    private static (int Code, string Output, string Error) Invoke(string[] args)
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        var originalInput = Console.In;
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new StringReader("");
        try
        {
            Console.SetOut(output); Console.SetError(error); Console.SetIn(input);
            int code = Program.Main(args);
            return (code, output.ToString(), error.ToString());
        }
        finally { Console.SetOut(originalOutput); Console.SetError(originalError); Console.SetIn(originalInput); }
    }
    private static void WithFile(string source, Action<string> action)
    {
        string file = Path.Combine(Path.GetTempPath(), $"Max test {Guid.NewGuid()}.max");
        File.WriteAllText(file, source);
        try { action(file); }
        finally { File.Delete(file); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
