namespace Max;

public static class SelfTests
{
    public static int Run()
    {
        int passed = 0, failed = 0;
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
        }
        void Output(string name, string source, params string[] expected) => Test(name, () =>
        {
            string actual = Capture(source);
            string wanted = string.Join("\n", expected);
            if (actual != wanted) throw new Exception($"Expected [{wanted}], got [{actual}]");
        });
        void Error<T>(string name, string source) where T : Exception => Test(name, () =>
        {
            try { Capture(source); }
            catch (T) { return; }
            throw new Exception($"Expected {typeof(T).Name}");
        });
        Output("original resort", "declare number as int = 10, resort 50 number = nil say number", "50");
        Output("original if/elif", "declare example = -5 if example > 0 then say 1 elif example < 0 then say 2 else say 3 conclude", "2");
        Output("original while", "declare x as int = 0 while x <= 5 do say x x += 1 conclude", "0", "1", "2", "3", "4", "5");
        Output("original for", "for i = 5 to 15, step 2 do say i conclude", "5", "7", "9", "11", "13", "15");
        Output("original foreach", "declare items as list<string> = [\"Bananas\", \"Fruits\", \"Pineapples\"] foreach item in items do say item conclude", "Bananas", "Fruits", "Pineapples");
        Output("word-style function", "function greet with name as string do say \"Hello \" + name conclude greet(\"Flashzain\")", "Hello Flashzain");
        Output("typed function expression", "function add(a as int, b as int) returns int do return a + b conclude say add(2, 3) * 4", "20");
        Output("recursion", "function factorial with n as int returns int do if n <= 1 then return 1 conclude return n * factorial(n - 1) conclude say factorial(6)", "720");
        Output("return from while", "function first() do while true do return 9 conclude conclude say first() declare ok = 1 say ok", "9", "1");
        Output("return from for", "function first() do for i = 1 to 3 do return i conclude conclude say first()", "1");
        Output("return from foreach", "function first() do foreach item in [4,5] do return item conclude conclude say first()", "4");
        Output("return from else", "function f() do if false then return 0 else return 7 conclude conclude say f()", "7");
        Output("return from maybe", "function f() do if possible then return 0 maybe return 8 conclude conclude say f()", "8");
        Output("implicit nil return", "function empty do conclude say empty()", "nil");
        Output("explicit nil return", "function empty do return nil conclude say empty()", "nil");
        Output("lexical scope", "declare x = 10 function read() do return x conclude function caller(x) do return read() conclude say caller(99)", "10");
        Output("nested function", "function outer(x) do function inner(y) do return x + y conclude return inner(3) conclude say outer(4)", "7");
        Output("local shadowing", "declare n = 99 function identity(n) do return n conclude say identity(5) say n", "5", "99");
        Output("short circuit and", "say false and missing()", "false");
        Output("short circuit or", "say true or missing()", "true");
        Output("possible and false", "say possible and false say true and false", "false", "false");
        Output("possible logic", "say possible and true say possible or false say possible or true say not possible", "possible", "possible", "true", "possible");
        Output("false equality", "say false == false say false != true say possible == true", "true", "true", "possible");
        Output("numeric/nil equality", "say 1 == 1.0 say nil == nil say nil != 1", "true", "true", "true");
        Output("logical grouping", "say (1 < 2) and (3 >= 3) say not false and false say not (false and false)", "true", "false", "true");
        Output("arithmetic precedence", "say 2 + 3 * 4 say (2 + 3) * 4 say -2 * -3 say 7 / 2 say 7 / 2.0", "14", "20", "6", "3", "3.5");
        Output("nested list types", "declare xs as list<list<int>> = [[1], [2]] xs[0][-1] += 3 say xs", "[[4], [2]]");
        Output("list of type syntax", "declare xs as list of list of int = [[3]] say xs[0][0]", "3");
        Output("string indexing returns string", "declare letter as string = \"hello\"[-1] say letter + \"!\"", "o!");
        Output("explicit string conversion", "say text(42) + \"!\" say \"n=\" + text(42)", "42!", "n=42");
        Output("length of list and Unicode text", "say length([1, 2]) say length(\"A😀\") say \"A😀\"[-1]", "2", "2", "😀");
        Output("optional typed value", "declare n as int? = nil say n n = 3 say n n = nil say n", "nil", "3", "nil");
        Output("optional return", "function missing() returns int? do return nil conclude say missing()", "nil");
        Output("optional list elements", "declare xs as list<int?> = [1, nil] xs[0] = nil say xs", "[nil, nil]");
        Output("optional list itself", "declare xs as list<int>? = nil xs = [1,2] say xs", "[1, 2]");
        Output("alphabetical string order", "say \"apple\" < \"banana\" say \"z\" > \"aa\"", "true", "true");
        Output("list identity equality", "declare a = [1] declare b = a say a == b say a == [1]", "true", "false");
        Output("comments and underscores", "# comment\ndeclare my_value = 4 // comment\nsay my_value", "4");
        Output("string escapes", "say \"a\\t\\\"b\\\"\\\\\"", "a\t\"b\"\\");
        Output("descending for", "for i = 3 to 1, step -1 do say i conclude", "3", "2", "1");
        Output("for overflow boundary", "for i = 2147483647 to 2147483647 do say i conclude", "2147483647");
        Output("minimum integer", "say -2147483648", "-2147483648");
        Output("fresh loop body scope", "for i = 1 to 2 do declare local = i say local conclude declare local = 9 say local", "1", "2", "9");
        Output("for scope restored", "for i = 1 to 1 do conclude declare i = 2 say i", "2");
        Output("foreach scope restored", "foreach item in [1] do conclude declare item = 2 say item", "2");
        Output("else block scope", "if false then else declare local = 1 conclude declare local = 2 say local", "2");
        Output("typed list expression evaluated once", "declare count = 0 function next() do count += 1 return count conclude declare xs as list<int> = [next()] say count say xs", "1", "[1]");
        Output("typed list returned from call", "function xs() returns list<int> do return [1,2] conclude declare result = xs() say result[1]", "2");
        Output("resort captured at declaration", "declare n = 5 declare x = nil, resort n n = 8 x = nil say x", "5");
        Error<MaxSyntaxException>("unknown character", "say 1 @ 2");
        Error<MaxSyntaxException>("multiline string", "say \"hello\nworld\"");
        Error<MaxSyntaxException>("chained comparison", "say 1 < 2 < 3");
        Error<MaxSyntaxException>("else before maybe", "if false then else say 1 maybe say 2 conclude");
        Error<MaxSyntaxException>("duplicate maybe", "if false then maybe say 1 maybe say 2 conclude");
        Error<MaxSyntaxException>("unterminated string", "say \"hello");
        Error<MaxSyntaxException>("missing while conclude", "while false do say 1");
        Error<MaxSyntaxException>("missing for conclude", "for i = 1 to 2 do say i");
        Error<MaxSyntaxException>("missing foreach conclude", "foreach i in [] do say i");
        Error<MaxSyntaxException>("missing function conclude", "function f() do return 1");
        Error<MaxSyntaxException>("return outside function", "return 5");
        Error<MaxSyntaxException>("duplicate parameter", "function f(a,a) do conclude");
        Error<MaxSyntaxException>("missing elif then", "if false then elif true say 1 conclude");
        Error<MaxSyntaxException>("incomplete declaration", "declare");
        Error<MaxSyntaxException>("incomplete list", "say [1,");
        Error<MaxSyntaxException>("number range", "say 99999999999999999999");
        Error<MaxSyntaxException>("integer literal too large", "say 2147483648");
        Error<MaxValueException>("wrong argument count", "function f(a) do conclude f()");
        Error<MaxTypeException>("wrong parameter type", "function f(a as int) do conclude f(\"x\")");
        Error<MaxTypeException>("wrong return type", "function f() returns int do return \"x\" conclude say f()");
        Error<MaxTypeException>("typed declaration rejects nil", "declare n as int = nil");
        Error<MaxTypeException>("typed assignment rejects nil", "declare n as int = 1 n = nil");
        Error<MaxTypeException>("typed return rejects nil", "function f() returns int do return nil conclude say f()");
        Error<MaxTypeException>("typed return requires value", "function f() returns int do conclude say f()");
        Error<MaxTypeException>("typed list rejects nil element", "declare xs as list<int> = [nil]");
        Error<MaxTypeException>("typed list rejects nil write", "declare xs as list<int> = [1] xs[0] = nil");
        Error<MaxTypeException>("length requires collection", "say length(4)");
        Error<MaxValueException>("text arity", "say text()");
        Error<MaxVariableException>("built-in name reserved", "function text(x) do return x conclude");
        Error<MaxComparisonException>("mixed equality rejected", "say 1 == \"1\"");
        Error<MaxComparisonException>("mixed ordering rejected", "say 1 < \"2\"");
        Error<MaxArithmeticException>("implicit text conversion rejected", "say \"age \" + 9");
        Error<MaxInfiniteRecursionException>("recursion limit", "function f() do return f() conclude f()");
        Error<MaxTypeException>("list element assignment type", "declare xs as list<int> = [1] xs[0] = \"x\"");
        Error<MaxTypeException>("list whole assignment type", "declare xs as list<int> = [1] xs = [\"x\"]");
        Error<MaxTypeException>("nested list assignment type", "declare xs as list<list<int>> = [[1]] xs[0][0] = \"x\"");
        Error<MaxTypeException>("list alias typing", "declare a = [1] declare b as list<int> = a a[0] = \"x\"");
        Error<MaxTypeException>("list incompatible alias", "declare a as list<int> = [1] declare b as list<float> = a");
        Error<MaxTypeException>("invalid resort", "declare x as int = 1, resort \"oops\"");
        Error<MaxNilException>("nil resort expression", "declare empty = nil declare x = 1, resort empty");
        Error<MaxIndexingException>("empty last index", "say [][-1]");
        Error<MaxIndexingException>("out of range index", "say [1][1]");
        Error<MaxIndexingException>("invalid negative index", "declare xs = [1] xs[-2] = 5");
        Error<MaxArithmeticException>("integer zero division", "say 1 / 0");
        Error<MaxArithmeticException>("float zero division", "say 1.0 / 0.0");
        Error<MaxArithmeticException>("overflow", "say 2147483647 + 1");
        Error<MaxArithmeticException>("string subtraction rejected", "say \"hello\" - 1");
        Error<MaxValueException>("zero step", "for i = 1 to 3, step 0 do conclude");
        Error<MaxTypeException>("condition must be boolean", "if 1 then conclude");
        Error<MaxVariableException>("duplicate declaration", "declare x = 1 declare x = 2");
        Error<MaxValueException>("direct list cycle", "declare xs = [nil] xs[0] = xs");
        Error<MaxValueException>("indirect list cycle", "declare a = [nil] declare b = [a] a[0] = b");
        Test("all three-valued truth table entries", () =>
        {
            string[] inputs = ["true", "false", "possible"];
            string[,] and = { { "true", "false", "possible" }, { "false", "false", "false" }, { "possible", "false", "possible" } };
            string[,] or = { { "true", "true", "true" }, { "true", "false", "possible" }, { "true", "possible", "possible" } };
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    if (Capture($"say {inputs[a]} and {inputs[b]}") != and[a,b] ||
                        Capture($"say {inputs[a]} or {inputs[b]}") != or[a,b])
                        throw new Exception($"Wrong truth table entry {a}, {b}");
                }
        });
        Test("scope restored after runtime error", () =>
        {
            var interpreter = new Interpreter();
            try { RunSource("function fail() do declare hidden = 1 return 1 / 0 conclude fail()", interpreter); }
            catch (MaxArithmeticException) { }
            try { RunSource("say hidden", interpreter); }
            catch (MaxVariableException) { return; }
            throw new Exception("Function scope leaked");
        });
        Test("source location", () =>
        {
            try { RunSource("say 1\nsay @", new()); }
            catch (MaxSyntaxException e) when (e.Message.Contains("Line 2, column 5")) { return; }
            throw new Exception("Expected line and column");
        });
        Test("runtime location and call stack", () =>
        {
            try { RunSource("function fail() do\n say 1 / 0\nconclude\nfail()", new()); }
            catch (MaxArithmeticException error) when
                (error.Data["MaxLocation"] is SourcePosition { Line: 2, Column: 8 } &&
                 error.Data["MaxCallStack"] is List<string> { Count: 1 } calls && calls[0] == "fail") { return; }
            throw new Exception("Expected inner source location and function name");
        });
        Test("nested type error names element", () =>
        {
            try { RunSource("declare xs as list<list<int>> = [[1], [\"wrong\"]]", new()); }
            catch (MaxTypeException error) when (error.Message.Contains("Variable 'xs'[1][0] requires int")) { return; }
            throw new Exception("Expected a precise list element error");
        });
        SyntaxTests.Run(Test);
        RuntimeTests.Run(Test);
        StaticAnalyzerTests.Run(Test);
        CliTests.Run(Test);
        Console.WriteLine($"{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static void RunSource(string source, Interpreter interpreter) =>
        interpreter.EvaluateAll(new Parser(new Lexer(source).Tokenize()).Parse());
    private static string Capture(string source)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); RunSource(source, new()); }
        finally { Console.SetOut(original); }
        return output.ToString().Replace("\r\n", "\n").TrimEnd('\n', '\r');
    }
}

