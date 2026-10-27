namespace Max;

public static class StaticAnalyzerTests
{
    public static void Run(Action<string, Action> test)
    {
        void Valid(string name, string source, bool strict = false) =>
            test("static: " + name, () => RunSource(source, strict));
        void Error<T>(string name, string source, bool strict = false, string? message = null) where T : Exception =>
            test("static: " + name, () =>
            {
                try { RunSource(source, strict); }
                catch (T error)
                {
                    if (message != null && !error.Message.Contains(message))
                        throw new Exception($"Expected message containing [{message}], got [{error.Message}]");
                    return;
                }
                throw new Exception($"Expected {typeof(T).Name}");
            });

        Error<MaxVariableException>("undefined variable", "say missing");
        Error<MaxVariableException>("undefined assignment", "missing = 1");
        Error<MaxVariableException>("call before definition", "say later() function later() do return 1 conclude");
        Error<MaxVariableException>("duplicate variable", "declare n = 1 declare n = 2");
        Error<MaxVariableException>("duplicate function", "function f() do conclude function f() do conclude");
        Error<MaxSyntaxException>("duplicate parameter", "function f(x, x) do conclude");
        Error<MaxVariableException>("builtin variable reserved", "declare length = 2");
        Error<MaxVariableException>("builtin function reserved", "function input() do conclude");
        Error<MaxVariableException>("builtin parameter reserved", "function f(append) do conclude");
        Error<MaxVariableException>("builtin loop reserved", "for text = 1 to 2 do conclude");
        Error<MaxVariableException>("function shadows variable", "declare f = 1 if true then function f() do conclude conclude");
        Error<MaxVariableException>("variable shadows function", "function f() do conclude if true then declare f = 1 conclude");
        Error<MaxVariableException>("parameter shadows function", "function f() do conclude function g(f) do return f conclude g(1)");

        Error<MaxTypeException>("wrong declaration type", "declare n as int = \"wrong\"");
        Error<MaxTypeException>("float literal in int", "declare n as int = 1.5");
        Error<MaxTypeException>("wrong assignment type", "declare n as int = 1 n = \"wrong\"");
        Error<MaxTypeException>("nil declaration", "declare n as int = nil");
        Error<MaxTypeException>("nil assignment", "declare n as int = 1 n = nil");
        Error<MaxTypeException>("wrong resort type", "declare n as int = 1, resort \"wrong\"");
        Error<MaxTypeException>("nested list member", "declare xs as list<list<int>> = [[1], [\"wrong\"]]", message: "[1][0]");
        Error<MaxTypeException>("typed list assignment", "declare xs as list<int> = [1] xs[0] = \"wrong\"");
        Error<MaxTypeException>("string element assignment", "declare s as string = \"a\" s[0] = \"b\"");
        Error<MaxTypeException>("noninteger index", "declare xs as list<int> = [1] say xs[true]");
        Error<MaxTypeException>("nonboolean if", "if 1 then say 1 conclude");
        Error<MaxTypeException>("nonboolean while", "while \"yes\" do conclude");
        Error<MaxTypeException>("for end type", "for n = 1 to \"three\" do conclude");
        Error<MaxTypeException>("foreach input type", "foreach n in \"hello\" do conclude");
        Error<MaxArithmeticException>("arithmetic type", "say \"n=\" + 1");
        Error<MaxComparisonException>("comparison type", "say 1 == \"1\"");
        Error<MaxValueException>("user function arity", "function f(x) do return x conclude f()");
        Error<MaxTypeException>("argument type", "function f(x as int) returns int do return x conclude f(\"wrong\")");
        Error<MaxTypeException>("return type", "function f() returns int do return \"wrong\" conclude f()");
        Error<MaxTypeException>("strict missing return", "function f() returns int do say 1 conclude f()", true);
        Error<MaxTypeException>("strict missing branch return", "function f(n as int) returns int do if n > 0 then return n conclude conclude f(1)", true);
        Error<MaxTypeException>("strict possible needs maybe return", "function f(flag as boolean) returns int do if flag then return 1 else return 0 conclude conclude f(possible)", true);
        Error<MaxVariableException>("closure call too early", "function f() do return later conclude f() declare later = 1");
        Error<MaxVariableException>("callee before declaration inside closure", "function f() do return g() conclude f() function g() do return 1 conclude");

        Valid("original resort", "declare n as int = nil, resort 9 n = nil say n");
        Valid("dynamic variable remains dynamic", "declare n = 1 n = \"text\" say n + \"!\"");
        Valid("dynamic return remains dynamic", "function read(x) do return x conclude declare n as int = read(1)");
        Valid("optional nil narrowing", "declare n as int? = 1 if n != nil then declare result as int = n say result + 1 conclude");
        Valid("optional parameter narrowing", "function f(n as int?) returns int do if n == nil then return 0 conclude return n + 1 conclude f(2)");
        Valid("late closure variable", "function f() do return later conclude declare later = 1 say f()");
        Valid("late closure function", "function f() do return g() conclude function g() do return 1 conclude say f()");
        Valid("lexical closure ignores caller", "declare n = 1 function read() do return n conclude function caller(n) do return read() conclude caller(2)");
        Valid("recursion", "function f(n as int) returns int do if n <= 1 then return 1 conclude return n * f(n - 1) conclude f(4)");
        Valid("nested function", "function outer(x) do function inner(y) do return x + y conclude return inner(1) conclude outer(2)");
        Valid("same kind variable shadow", "declare n as int = 1 if true then declare n as string = \"ok\" say n conclude");
        Valid("same kind function shadow", "function f() do return 1 conclude if true then function f() do return 2 conclude f() conclude");
        Valid("constant short circuit", "say false and missing() say true or missing()");
        Valid("constant dead branch", "if false then say missing else say 1 conclude while false do say missing conclude");
        Valid("constant comparison dead branch", "if 1 < 2 then say 1 else say missing conclude");
        Valid("literal boolean comparisons cannot be possible", "function f() returns int do if true == true then return 1 else return 2 conclude conclude say f()");
        Valid("comparison loop return", "function f() returns int do while 1 == 1 do return 1 conclude conclude say f()");
        Valid("logical loop return", "function f() returns int do while true and not false do return 1 conclude conclude say f()");
        Valid("possible comparison maybe", "function f() returns int do if possible == true then return 1 maybe return 2 conclude conclude say f()");
        Valid("empty foreach", "foreach item in [] do say missing conclude");
        Valid("return unreachable body", "function f() returns int do return 1 say missing conclude f()");
        Valid("optional implicit return", "function f() returns int? do conclude f()");
        Valid("complete comparison branches", "function f(n) returns int do if n > 0 then return 1 else return 2 conclude conclude f(1)");
        Valid("complete three valued branches", "function f(flag as boolean) returns int do if flag then return 1 maybe return 2 else return 0 conclude conclude f(possible)");
        Valid("while true has no implicit nil", "function f() returns int do while true do conclude conclude f()");
        Valid("nonempty for return", "function f() returns int do for i = 1 to 2 do return i conclude conclude f()");
        Valid("nonempty foreach return", "function f() returns int do foreach item in [1] do return item conclude conclude f()");
        Valid("ordinary return completeness stays dynamic", "function f() returns int do declare xs as list<int> = [1] foreach item in xs do return item conclude conclude say f()");
        Valid("ordinary missing return is runtime checked", "function f() returns int do conclude f()");
        Valid("float accepts int", "declare n as float = 1 declare xs as list<float> = [1, 2.5]");
        Valid("float annotation can carry int", "function f(n as float) returns int do return n conclude f(2)");
        Valid("nil narrowing between optional types", "declare n as int? = nil if n == nil then declare s as string? = n conclude");
        Valid("optional nil narrowing to nothing", "function f(n as int?) returns nothing do if n == nil then return n conclude return nil conclude f(nil)");
        Error<MaxArithmeticException>("nil guard rejects nil arithmetic", "declare n as int? = nil if n == nil then say n + 1 conclude");
        Error<MaxTypeException>("nil guard rejects nil length", "declare xs as list<int>? = nil if xs == nil then length(xs) conclude");
        Error<MaxTypeException>("nonnull guard checks optional inner type", "declare n as int? = 1 if n != nil then declare s as string? = n conclude");
        Error<MaxTypeException>("else guard checks optional inner type", "declare n as int? = 1 if n == nil then say 0 else declare s as string? = n conclude");
        Error<MaxTypeException>("negated guard checks optional inner type", "declare n as int? = 1 if not (nil == n) then declare s as string? = n conclude");
        Valid("nil guard allows cross optional nil", "declare n as int? = nil if n == nil then declare s as string? = n conclude");
        Valid("nil guard facts do not leak branches", "declare n as int? = 1 if n == nil then say text(n) else say n + 1 conclude");
        Valid("nil guard facts discarded by assignment", "declare n as int? = nil if n == nil then n = 1 say n + 1 conclude");
        Valid("nil guard facts discarded by user call", "declare n as int? = nil function fill() returns nothing do n = 1 conclude if n == nil then fill() say n + 1 conclude");
        Valid("nil guard scalar aliases have distinct bindings", "declare a as int? = nil declare b as int? = 1 if a == nil then say b + 1 conclude");
        Valid("ordinary contradictory nil guard branch is skipped", "declare n as int? = 1 if n != nil then if n == nil then say n + 1 conclude conclude");
        Valid("ordinary known nil guard branch is skipped", "declare n as int? = nil if n == nil then if n != nil then say missing conclude conclude");
        Valid("ordinary contextual nil short circuit", "declare n as int? = 1 if n != nil then say n == nil and missing() conclude");
        Error<MaxVariableException>("strict contradictory guard still checks body", "declare n as int? = 1 if n != nil then if n == nil then say missing conclude conclude", true);
        Error<MaxTypeException>("guard true loop can exit after mutation", "function f(n as int?) returns int do if n == nil then while n == nil do n = 1 conclude else return 1 conclude conclude", true, "can finish");
        Valid("guard true loop with final return", "function f(n as int?) returns int do if n == nil then while n == nil do n = 1 conclude else return 1 conclude return 2 conclude", true);
        Valid("optional nil resort declaration", "declare source as string? = nil declare result as int = source, resort 3", true);
        Valid("optional nil resort assignment", "declare source as string? = nil declare result as int = 0, resort 3 result = source", true);
        Valid("optional nil resort avoids list constraint conflict", "declare source as list<float>? = nil declare result as list<int> = source, resort [1]", true);
        Error<MaxTypeException>("nonnull guard cannot be rescued by resort", "declare source as string? = \"wrong\" if source != nil then declare result as int = source, resort 3 conclude", true);

        Error<MaxTypeException>("typed list alias cannot widen", "declare xs as list<int> = [1] declare ys as list<float> = xs", message: "shared list");
        Error<MaxTypeException>("typed list alias cannot narrow", "declare xs as list<float> = [1] declare ys as list<int> = xs", message: "shared list");
        Error<MaxTypeException>("typed list alias cannot add optional elements", "declare xs as list<int> = [] declare ys as list<int?> = xs", message: "shared list");
        Error<MaxTypeException>("typed list argument cannot widen", "function f(xs as list<float>) returns nothing do conclude declare xs as list<int> = [1] f(xs)", message: "shared list");
        Error<MaxTypeException>("typed list return cannot widen", "function f() returns list<float> do declare xs as list<int> = [1] return xs conclude f()", message: "shared list");
        Error<MaxTypeException>("nested append cannot retag shared list", "declare xs as list<int> = [1] declare nested as list<list<float>> = [] append(nested, xs)", message: "shared list");
        Valid("typed list alias same constraint", "declare xs as list<float> = [1] declare ys as list<float> = xs");
        Valid("untyped literal list remains element checked", "declare xs as list<float> = [1, 2.5] declare ys as list<list<float>> = [[1], [2.5]]");

        Error<MaxValueException>("builtin arity", "text()");
        Error<MaxTypeException>("length input", "length(1)");
        Error<MaxTypeException>("input prompt", "input(1)");
        Error<MaxTypeException>("integer input", "integer(true)");
        Error<MaxTypeException>("decimal input", "decimal([])");
        Error<MaxTypeException>("append input", "append(1, 2)");
        Error<MaxTypeException>("append typed element", "declare xs as list<int> = [] append(xs, \"wrong\")");
        Error<MaxTypeException>("remove index", "remove([], \"first\")");
        Valid("new builtin signatures", "declare prompt as string = input(\"Name: \") declare n as int = integer(\"3\") declare d as float = decimal(n) declare xs as list<int> = [n] append(xs, 4) declare result as int = remove(xs, 0) say text(length(prompt))");
        Valid("nothing procedure", "function greet() returns nothing do say \"Hi\" conclude greet()", true);
        Valid("explicit nothing return", "function f() returns nothing do return nil conclude f()", true);
        Error<MaxTypeException>("nothing rejects value", "function f() returns nothing do return 1 conclude f()", true);

        Error<MaxTypeException>("strict declaration annotation", "declare n = 1", true);
        Error<MaxTypeException>("strict parameter annotation", "function f(n) returns int do return n conclude", true);
        Error<MaxTypeException>("strict result annotation", "function f() do conclude", true);
        Error<MaxTypeException>("strict list annotation", "declare xs as list = []", true);
        Error<MaxTypeException>("strict nested list annotation", "declare xs as list<list> = []", true);
        Error<MaxTypeException>("strict optional list annotation", "declare xs as list? = nil", true);
        Error<MaxTypeException>("strict parameter list annotation", "function f(xs as list) returns nothing do conclude", true);
        Error<MaxTypeException>("strict return list annotation", "function f() returns list do return [] conclude", true);
        Error<MaxVariableException>("strict dormant undefined variable", "function f() returns nothing do say missing conclude", true);
        Error<MaxVariableException>("strict dead branch", "if false then say missing conclude", true);
        Error<MaxVariableException>("strict unreachable statement", "function f() returns int do return 1 say missing conclude", true);
        Valid("ordinary dormant body is deferred", "function f() do say missing conclude");
        Valid("strict late lexical closure", "function f() returns int do return later conclude declare later as int = 1", true);
        Valid("strict nested late lexical closure", "function f() returns int do function g() returns int do return later conclude declare later as int = 1 return g() conclude", true);
        Valid("strict loop bindings inferred", "for n = 1 to 2 do say n conclude foreach item in [1,2] do say item conclude", true);
        Valid("strict typed complete program", "function add(a as int, b as int) returns int do return a + b conclude declare result as int = add(2, 3) declare values as list<int> = [result] append(values, 4) foreach item in values do say text(item) conclude", true);

        test("static: errors carry source location", () =>
        {
            try { RunSource("say 1\nsay missing"); }
            catch (MaxVariableException error) when (error.Data["MaxLocation"] is SourcePosition { Line: 2 }) { return; }
            throw new Exception("Expected a source location on the static diagnostic");
        });
    }

    private static void RunSource(string source, bool strict = false) =>
        StaticAnalyzer.Validate(new Parser(new Lexer(source).Tokenize()).Parse(), strict);
}
