namespace Max;

public static class SyntaxTests
{
    public static void Run(Action<string, Action> test)
    {
        test("syntax: expressions carry precise operator and operand locations", () =>
        {
            var expression = (BinaryExpression)((SayStatement)Parse("say missing + 1 * 2")[0]).Value;
            Position(expression, 1, 13);
            Position(expression.Left, 1, 5);
            Position(expression.Right, 1, 17);
            Position(((BinaryExpression)expression.Right).Left, 1, 15);
        });
        test("syntax: logical and unary locations", () =>
        {
            var expression = (LogicalExpression)((SayStatement)Parse("say not false or true and false")[0]).Value;
            Position(expression, 1, 15);
            Position(expression.Left, 1, 5);
            Position(expression.Right, 1, 23);
        });
        test("syntax: comparison location", () => Position(Value("say 1 < 2"), 1, 7));
        test("syntax: call and argument locations", () =>
        {
            var call = (CallExpression)Value("say length([1])");
            Position(call, 1, 5);
            Position(call.Arguments[0], 1, 12);
        });
        test("syntax: indexing and list locations", () =>
        {
            var index = (IndexingExpression)Value("say [1, 2][9]");
            Position(index, 1, 11);
            Position(index.Name, 1, 5);
            Position(index.IndexValue, 1, 12);
        });
        test("syntax: grouping preserves inner location", () => Position(Value("say (missing)"), 1, 6));
        test("syntax: negative minimum integer location", () => Position(Value("say -2147483648"), 1, 5));
        test("syntax: source mapping can be updated safely", () =>
        {
            var expression = Value("say 1");
            SourceMap.Set(expression, 3, 8);
            Position(expression, 3, 8);
        });
        test("syntax: same-line statements remain supported", () =>
        {
            if (Parse("declare count = 1 count += 1 say count say 3").Count != 4)
                throw new Exception("Same-line statements changed.");
        });
        test("syntax: both list type forms and optional types remain supported", () =>
        {
            Parse("declare a as list<list<int?>>? = nil declare b as list of list of int = [[1]]");
        });
        test("syntax: explicit nothing procedure signature parses", () => Parse("function greet() returns nothing do say \"Hello\" conclude"));
        test("syntax: list and call trailing commas remain supported", () => Parse("say length([1, 2,],)"));
        test("syntax: minimum and maximum literals parse", () => Parse("say -2147483648 say 2147483647 say 1. say 1.5"));
        test("syntax: leading zeros preserve the minimum integer", () =>
        {
            if (Value("say -0002147483648") is not IntegerLiteral { Value: int.MinValue })
                throw new Exception("Leading zeros changed the minimum integer.");
            Error("say 0002147483648", "only valid after '-'");
        });
        test("syntax: Unicode names include supplementary letters and digits", () =>
        {
            var tokens = new Lexer("declare 𐐀𝟙 as int = 3 say 𐐀𝟙").Tokenize();
            if (tokens[1].Value != "𐐀𝟙" || tokens[1].Column != 9 || tokens[1].EndColumn != 13)
                throw new Exception("A Unicode name or its UTF-16 source span was lost.");
            if (new Parser(tokens).Parse().Count != 2)
                throw new Exception("Unicode names did not parse.");
            Error("say 𐐀x+", "Expected an expression", 1, 9);
        });
        test("syntax: non-ASCII numeric digits have actionable errors", () =>
        {
            Error("say ١", "digits 0 to 9", 1, 5);
            Error("say 1٢", "digits 0 to 9", 1, 6);
            Error("say 𝟙", "digits 0 to 9", 1, 5);
        });
        test("syntax: unsupported supplementary symbols stay intact in diagnostics", () =>
            Error("say 😀", "Unknown character '😀'", 1, 5));
        test("syntax: comment-only and empty programs parse", () =>
        {
            if (Parse("").Count != 0 || Parse("# comment\r// comment\n").Count != 0)
                throw new Exception("Comments produced statements.");
        });
        test("syntax: CR, CRLF, and LF preserve line numbers and comments", () =>
        {
            var tokens = new Lexer("# first\rsay 1\r\n// second\nsay 2").Tokenize();
            if (tokens[0].Line != 2 || tokens[0].Column != 1 || tokens[2].Line != 4 || tokens[2].Column != 1)
                throw new Exception("Line endings did not advance correctly.");
        });
        test("syntax: escaped strings retain their source end", () =>
        {
            var token = new Lexer("\"a\\n\"").Tokenize()[0];
            if (token.Value != "a\n" || token.EndLine != 1 || token.EndColumn != 6)
                throw new Exception("The token end used decoded string length.");
        });
        test("syntax: statement typo suggests the keyword", () => Error("decalre value = 1", "Did you mean 'declare'", 1, 1));
        test("syntax: statement keyword case is explained", () => Error("Say 1", "Keywords use lowercase", 1, 1));
        test("syntax: conditional and branch keyword typos get suggestions", () =>
        {
            Error("If true then conclude", "Did you mean 'if'", 1, 1);
            Error("iff true then conclude", "Did you mean 'if'", 1, 1);
            Error("if true then esle say 1 conclude", "Did you mean 'else'", 1, 14);
        });
        test("syntax: header typo suggests then", () => Error("if true thne say 1 conclude", "Did you mean 'then'", 1, 9));
        test("syntax: unknown types point to the type name", () => Error("declare x as integer = 1", "Did you mean 'int'", 1, 14));
        test("syntax: duplicate parameters point to the duplicate", () => Error("function f(a, a) do conclude", "Duplicate parameter", 1, 15));
        test("syntax: invalid return points to return", () => Error("\n  return 1", "inside a function", 2, 3));
        test("syntax: unfinished block points to its opener", () => Error("say 1\nwhile false do say 2", "'while' block opened here is missing 'conclude'", 2, 1));
        test("syntax: unfinished nested block identifies inner opener", () => Error("if true then\n while false do\n say 1", "'while' block opened here", 2, 2));
        test("syntax: missing delimiters identify opening location", () =>
        {
            Error("say [1, 2", "Missing ']'", 1, 5);
            Error("say length(1", "Missing ')'", 1, 11);
            Error("say (1 + 2", "Missing ')'", 1, 5);
        });
        test("syntax: mismatched delimiter explains expected closing symbol", () => Error("say [1)", "Expected ']'", 1, 7));
        test("syntax: unexpected conclude explains block structure", () => Error("conclude", "no open block", 1, 1));
        test("syntax: elif after else explains branch order", () => Error("if true then else elif false then conclude", "Place every 'elif'", 1, 19));
        test("syntax: incomplete declaration carries an end location", () => Error("declare", "Expected a name", 1, 8));
        test("syntax: incomplete source points after trailing whitespace and comments", () =>
        {
            Error("declare\n\n  # unfinished declaration\n", "Expected a name", 4, 1);
            Error("say 1 + \r\n // unfinished expression", "Expected an expression", 2, 26);
            Error("function f() returns\r  ", "Expected a name", 2, 3);
        });
        test("syntax: empty index gives an actionable error", () => Error("say [1][]", "An index needs an expression", 1, 9));
        test("syntax: malformed numeric and string input carries locations", () =>
        {
            Error("say 99999999999999999999", "outside the supported range", 1, 5);
            Error("say 1.2.3", "two decimal points", 1, 8);
            Error("say 1foo", "Separate a number", 1, 6);
            Error("say \"hello\\", "Unterminated string after", 1, 5);
            Error("say \"hello\nworld\"", "cannot span lines", 1, 5);
            Error("say \"hello\\\nworld\"", "cannot span lines", 1, 5);
            Error("say \"\\q\"", "Unknown string escape", 1, 7);
            Error("say 1 @", "Unknown character", 1, 7);
        });
        test("syntax: conflicting list type forms are rejected", () => Error("declare x as list of int<string> = []", "Expected '='"));
        test("syntax: deeply nested grouping fails safely", () => Error("say " + new string('(', 300) + "1" + new string(')', 300), "nested too deeply"));
        test("syntax: deeply nested unary operators fail safely", () => Error("say " + new string('-', 300) + "1", "nested too deeply"));
        test("syntax: deeply nested list types fail safely", () => Error("declare x as " + string.Concat(Enumerable.Repeat("list<", 300)) + "int" + new string('>', 300) + " = []", "nested too deeply"));
        test("syntax: deeply nested blocks fail safely", () => Error(string.Concat(Enumerable.Repeat("if true then ", 300)) + "say 1 " + string.Concat(Enumerable.Repeat("conclude ", 300)), "nested too deeply"));
        test("syntax: long arithmetic chains fail safely", () => Error("say " + string.Join(" + ", Enumerable.Repeat("1", 300)), "nested too deeply"));
        test("syntax: long indexing chains fail safely", () => Error("say value" + string.Concat(Enumerable.Repeat("[0]", 300)), "nested too deeply"));
        test("syntax: long logical chains fail safely", () => Error("say " + string.Join(" and ", Enumerable.Repeat("true", 300)), "nested too deeply"));
    }

    private static List<IStatement> Parse(string source) => new Parser(new Lexer(source).Tokenize()).Parse();
    private static IExpression Value(string source) => ((SayStatement)Parse(source)[0]).Value;
    private static void Position(IAstNode node, int line, int column)
    {
        if (SourceMap.Get(node) != new SourcePosition(line, column))
            throw new Exception($"Expected line {line}, column {column}; got {SourceMap.Get(node)}.");
    }
    private static void Error(string source, string message, int? line = null, int? column = null)
    {
        try { Parse(source); }
        catch (MaxSyntaxException error)
        {
            if (!error.Message.Contains(message, StringComparison.Ordinal))
                throw new Exception($"Expected a message containing '{message}'; got '{error.Message}'.");
            if (error.Data["MaxLocation"] is not SourcePosition location)
                throw new Exception("The syntax error has no source location.");
            if (line != null && (location.Line != line || location.Column != column))
                throw new Exception($"Expected line {line}, column {column}; got {location}.");
            return;
        }
        throw new Exception("Expected MaxSyntaxException.");
    }
}
