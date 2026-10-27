using System.Globalization;

namespace Max;

public static class RuntimeTests
{
    public static void Run(Action<string, Action> test)
    {
        void Output(string name, string source, params string[] expected) => test(name, () =>
            Equal(string.Join("\n", expected), Capture(source)));
        void Error<T>(string name, string source) where T : Exception => test(name, () =>
            Expect<T>(() => RunSource(source, new())));

        Output("explicit integer conversions", "say integer(7) say integer(7.0) say integer(\"+7\") say integer(\"-2147483648\") say integer(\"2147483647\")", "7", "7", "7", "-2147483648", "2147483647");
        Output("explicit decimal conversions", "say decimal(7) / 2 say decimal(\"-3.25\") say decimal(\"1e2\") say decimal(4.5)", "3.5", "-3.25", "100", "4.5");
        Output("nothing procedure returns nil", "function show() returns nothing do say \"ok\" conclude say show()", "ok", "nil");
        Output("nothing value and explicit return", "declare empty as nothing = nil function empty_value() returns nothing do return nil conclude say empty say empty_value()", "nil", "nil");
        Error<MaxTypeException>("nothing rejects non-nil", "declare empty as nothing = 1");
        Error<MaxTypeException>("nothing procedure rejects value", "function bad() returns nothing do return 1 conclude bad()");
        Error<MaxValueException>("integer rejects fractional value", "integer(3.5)");
        Error<MaxValueException>("integer rejects range overflow", "integer(2147483648.0)");
        Error<MaxValueException>("integer rejects decimal text", "integer(\"3.0\")");
        Error<MaxValueException>("integer rejects exponent text", "integer(\"3e0\")");
        Error<MaxValueException>("integer rejects whitespace text", "integer(\" 3\")");
        Error<MaxValueException>("integer rejects malformed text", "integer(\"three\")");
        Error<MaxValueException>("integer rejects embedded NUL text", "integer(\"3\0\")");
        Error<MaxTypeException>("integer rejects boolean", "integer(true)");
        Error<MaxTypeException>("integer rejects nil", "integer(nil)");
        Error<MaxValueException>("decimal rejects nonfinite text", "decimal(\"NaN\")");
        Error<MaxValueException>("decimal rejects overflow", "decimal(\"1e999\")");
        Error<MaxValueException>("decimal rejects thousands separators", "decimal(\"1,000\")");
        Error<MaxValueException>("decimal rejects whitespace text", "decimal(\"1.5 \")");
        Error<MaxValueException>("decimal rejects embedded NUL text", "decimal(\"1.5\0\")");
        Error<MaxTypeException>("decimal rejects boolean", "decimal(false)");
        Error<MaxTypeException>("decimal rejects nil", "decimal(nil)");
        Error<MaxValueException>("input arity", "input()");
        Error<MaxTypeException>("input prompt type", "input(1)");
        test("input prompts and returns line", () =>
        {
            var original = Console.In;
            using var input = new StringReader("Ada\n");
            try { Console.SetIn(input); Equal("Name: Ada!", Capture("say input(\"Name: \") + \"!\"")); }
            finally { Console.SetIn(original); }
        });
        test("input EOF has a clear error", () =>
        {
            var original = Console.In;
            using var input = new StringReader("");
            try
            {
                Console.SetIn(input);
                var error = Expect<MaxValueException>(() => Capture("input(\"\")"));
                if (!error.Message.Contains("end of input", StringComparison.Ordinal)) throw new Exception("Expected a readable EOF error.");
            }
            finally { Console.SetIn(original); }
        });
        test("conversions use invariant culture", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Equal("2.5\n-4", Capture("say decimal(\"2.5\") say integer(\"-4\")"));
                Expect<MaxValueException>(() => Capture("decimal(\"2,5\")"));
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
        Output("append and remove update aliases", "declare xs = [1] declare alias = xs say append(xs, 2) say alias say remove(alias, -1) say xs", "nil", "[1, 2]", "2", "[1]");
        Output("append evaluates arguments once", "declare calls = 0 function next() do calls += 1 return calls conclude declare xs = [] append(xs, next()) say xs say calls", "[1]", "1");
        Output("compound variable captures value before RHS mutation", "declare x = 1 function mutate() do x = 100 return 4 conclude x += mutate() say x", "5");
        Output("compound indexed value captured before RHS removal", "declare xs = [1, 2] xs[0] += remove(xs, 0) say xs", "[2]");
        Output("compound indexed target captured before RHS rebinding", "declare xs = [1] declare alias = xs function rebind() do xs = [100] return 4 conclude xs[0] += rebind() say xs say alias", "[100]", "[5]");
        Output("compound indexed target index and RHS run once in order", "declare order = \"\" declare xs = [1] function target() do order += \"target \" return xs conclude function index() do order += \"index \" return 0 conclude function value() do order += \"value\" return 4 conclude target()[index()] += value() say xs say order", "[5]", "target index value");
        Output("foreach captures element rule with its snapshot", "declare xs = [1, \"two\"] foreach item in xs do if length(xs) == 2 then remove(xs, 1) declare typed as list<int> = xs conclude say item conclude", "1", "two");
        Output("remove returns nested list identity", "declare child = [1] declare xs = [child] declare removed = remove(xs, 0) say removed == child say length(xs)", "true", "0");
        Error<MaxTypeException>("append requires list", "append(\"text\", 1)");
        Error<MaxValueException>("append arity", "append([])");
        Error<MaxTypeException>("remove requires list", "remove(\"text\", 0)");
        Error<MaxValueException>("remove arity", "remove([])");
        Error<MaxIndexingException>("remove requires integer index", "remove([1], 0.0)");
        Error<MaxIndexingException>("remove rejects empty list", "remove([], -1)");
        Error<MaxIndexingException>("remove rejects negative index", "remove([1], -2)");
        Error<MaxIndexingException>("list assignment rechecks index after RHS removal", "declare xs = [1] xs[0] = remove(xs, 0)");
        Error<MaxIndexingException>("list compound assignment rechecks index after RHS removal", "declare xs = [1] xs[0] += remove(xs, 0)");
        Error<MaxTypeException>("typed append enforces element type", "declare xs as list<int> = [] append(xs, \"wrong\")");
        Error<MaxTypeException>("empty typed list retains type after removal", "declare xs as list<int> = [1] remove(xs, 0) append(xs, \"wrong\")");
        Error<MaxTypeException>("typed nested append attaches constraints", "declare outer as list<list<int>> = [] declare child = [] append(outer, child) append(child, \"wrong\")");
        Error<MaxTypeException>("returned list attaches element constraints", "function values() returns list<int> do return [1] conclude declare xs = values() append(xs, \"wrong\")");
        Error<MaxTypeException>("returned nested list constrains original alias", "declare child = [] function values() returns list<list<int>> do return [child] conclude values() append(child, \"wrong\")");
        Error<MaxTypeException>("resort constrains captured list immediately", "declare fallback = [1] declare xs as list<int> = [2], resort fallback append(fallback, \"wrong\")");
        Output("resort alias stays shared", "declare fallback = [1] declare xs as list<int> = [2], resort fallback append(fallback, 3) xs = nil say xs", "[1, 3]");
        test("failed graph type attachment is atomic", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare child = [] declare parent = [child]", interpreter);
            Expect<MaxTypeException>(() => RunSource("declare bad as list<list<list<int>>> = [child, parent]", interpreter));
            Equal("[free]\n[[free], free]", Capture("append(child, \"free\") append(parent, \"free\") say child say parent", interpreter));
        });
        test("failed assignment type attachment is atomic", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare child = [] declare parent = [child] declare xs as list<list<list<list<int>>>> = []", interpreter);
            Expect<MaxTypeException>(() => RunSource("append(xs, [child, parent])", interpreter));
            Equal("[free]\n[]", Capture("append(child, \"free\") say child say xs", interpreter));
        });
        test("failed resort does not type valid initial list", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare initial = [] declare fallback = [\"wrong\"]", interpreter);
            Expect<MaxTypeException>(() => RunSource("declare bad as list<int> = initial, resort fallback", interpreter));
            Equal("[free]", Capture("append(initial, \"free\") say initial", interpreter));
        });
        test("failed initial value does not type valid resort", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare fallback = []", interpreter);
            Expect<MaxTypeException>(() => RunSource("declare bad as list<int> = [\"wrong\"], resort fallback", interpreter));
            Equal("[free]", Capture("append(fallback, \"free\") say fallback", interpreter));
        });
        test("all parameter constraints attach atomically", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare xs = [] function incompatible(a as list<int>, b as list<string>) do conclude", interpreter);
            Expect<MaxTypeException>(() => RunSource("incompatible(xs, xs)", interpreter));
            Equal("[free]", Capture("append(xs, \"free\") say xs", interpreter));
        });
        test("append rejects direct alias cycle without mutation", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare xs = [] declare alias = xs", interpreter);
            Expect<MaxValueException>(() => RunSource("append(xs, alias)", interpreter));
            Equal("[]", Capture("say xs", interpreter));
        });
        test("append rejects indirect cycle without mutation", () =>
        {
            var interpreter = new Interpreter();
            RunSource("declare xs = [] declare parent = [xs]", interpreter);
            Expect<MaxValueException>(() => RunSource("append(xs, parent)", interpreter));
            Equal("[]\n[[]]", Capture("say xs say parent", interpreter));
        });
        foreach (string name in new[] { "text", "length", "input", "integer", "decimal", "append", "remove" })
        {
            Error<MaxVariableException>($"built-in variable name reserved: {name}", $"declare {name} = 1");
            Error<MaxVariableException>($"built-in function name reserved: {name}", $"function {name}() do conclude");
            Error<MaxVariableException>($"built-in parameter name reserved: {name}", $"function example({name}) do conclude");
            Error<MaxVariableException>($"built-in empty foreach name reserved: {name}", $"foreach {name} in [] do conclude");
            Error<MaxVariableException>($"built-in for name reserved: {name}", $"for {name} = 1 to 1 do conclude");
        }
        Error<MaxVariableException>("variable then function collision", "declare same = 1 function same() do conclude");
        Error<MaxVariableException>("function then variable collision", "function same() do conclude declare same = 1");
        Error<MaxVariableException>("nested function cannot shadow variable", "declare same = 1 if true then function same() do conclude conclude");
        Error<MaxVariableException>("nested variable cannot shadow function", "function same() do conclude if true then declare same = 1 conclude");
        Error<MaxVariableException>("parameter cannot shadow visible function", "function same() do conclude function use(same) do conclude use(1)");
        Error<MaxVariableException>("parameter cannot share its function name", "function same(same) do conclude");
        Output("same-kind function shadowing remains lexical", "function value() do return 1 conclude function outer() do function value() do return 2 conclude return value() conclude say outer() say value()", "2", "1");
        test("runtime error identifies innermost expression", () =>
        {
            var error = Expect<MaxVariableException>(() => RunSource("say 1 + absent", new()));
            if (error.Data["MaxLocation"] is not SourcePosition { Line: 1, Column: 9 })
                throw new Exception("Expected the missing name's line and column.");
        });
        test("iteratively built deep list fails safely", () =>
        {
            var error = Expect<MaxValueException>(() => RunSource("declare node = [nil] for i = 1 to 1000 do node = [node] conclude say node", new()));
            if (!error.Message.Contains("nested", StringComparison.Ordinal)) throw new Exception("Expected a readable nesting error.");
        });
        test("deep host list formatting fails safely", () =>
        {
            object value = new Nil();
            for (int i = 0; i < 1000; i++)
            {
                var parent = new List(null);
                parent.Values.Add(value);
                value = parent;
            }
            Expect<MaxValueException>(() => Interpreter.Format(value));
        });
        test("combined function and expression nesting is bounded", () =>
        {
            string signs = string.Concat(Enumerable.Repeat("- ", 30));
            var interpreter = new Interpreter();
            Expect<MaxInfiniteRecursionException>(() => RunSource($"function recurse() do return {signs}recurse() conclude recurse()", interpreter));
            Equal("1", Capture("say 1", interpreter));
        });
        test("shared list graph type checking visits distinct values", () =>
        {
            // Forty list objects represent over a trillion alias paths. Type
            // checking must follow the graph, rather than expand those paths.
            string type = "int";
            for (int i = 0; i < 40; i++) type = $"list<{type}>";
            Equal("2", Capture($"declare node = 1 for i = 1 to 40 do node = [node, node] conclude declare typed as {type} = node say length(typed)"));
        });
    }

    private static void RunSource(string source, Interpreter interpreter) =>
        interpreter.EvaluateAll(new Parser(new Lexer(source).Tokenize()).Parse());
    private static string Capture(string source, Interpreter? interpreter = null)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); RunSource(source, interpreter ?? new()); }
        finally { Console.SetOut(original); }
        return output.ToString().Replace("\r\n", "\n").TrimEnd('\n', '\r');
    }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static void Equal(string expected, string actual)
    {
        if (expected != actual) throw new Exception($"Expected [{expected}], got [{actual}].");
    }
}
