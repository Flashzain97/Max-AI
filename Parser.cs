// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Max;

public class Parser(List<Token> tokens)
{
    private const int MaximumNesting = 256;
    private int position;
    private int functionDepth;
    private int nestingDepth;
    private readonly Dictionary<IExpression, int> expressionDepths = new();
    private Token? Current => position < tokens.Count ? tokens[position] : null;
    private bool Match(TokenType type) => Current?.Type == type;
    private bool Take(TokenType type)
    {
        if (!Match(type)) return false;
        position++; return true;
    }
    private MaxSyntaxException Error(string message)
    {
        if (Current is Token token) return ErrorAt(token, $"{message} Found '{token.Value}'.");
        var end = SourceMap.GetEndOfFile(tokens);
        int line = end?.Line ?? (tokens.Count == 0 ? 1 : tokens[^1].EndLine);
        int column = end?.Column ?? (tokens.Count == 0 ? 1 : tokens[^1].EndColumn);
        return SourceMap.WithLocation(new MaxEndFileException($"Line {line}, column {column}: End of file: {message}"), line, column);
    }
    private static MaxSyntaxException ErrorAt(Token token, string message, bool endOfFile = false)
    {
        MaxSyntaxException error = endOfFile
            ? new MaxEndFileException($"Line {token.Line}, column {token.Column}: End of file: {message}")
            : new MaxSyntaxException($"Line {token.Line}, column {token.Column}: {message}");
        return SourceMap.WithLocation(error, token.Line, token.Column);
    }
    private Token Consume(TokenType type)
    {
        if (!Match(type))
        {
            string expected = Describe(type);
            string? keyword = Keyword(type);
            string suggestion = Current is { Type: TokenType.Identifier } t && keyword != null && CloseSpelling(t.Value, keyword)
                ? $" Did you mean '{keyword}'?" : "";
            throw Error($"Expected {expected}.{suggestion}");
        }
        return tokens[position++];
    }
    private static string? Keyword(TokenType type) => type switch
    {
        TokenType.Declare => "declare", TokenType.Resort => "resort", TokenType.Say => "say",
        TokenType.IfBegin => "if", TokenType.Then => "then", TokenType.Elif => "elif",
        TokenType.Else => "else", TokenType.Maybe => "maybe", TokenType.ConcludeStatement => "conclude",
        TokenType.While => "while", TokenType.For => "for", TokenType.Foreach => "foreach",
        TokenType.Do => "do", TokenType.As => "as", TokenType.To => "to", TokenType.Step => "step",
        TokenType.Of => "of", TokenType.In => "in", TokenType.Function => "function", TokenType.With => "with",
        TokenType.Returns => "returns", TokenType.Return => "return", _ => null
    };
    private static string Describe(TokenType type) => Keyword(type) is string keyword ? $"'{keyword}'" : type switch
    {
        TokenType.Identifier => "a name, such as 'count'",
        TokenType.Equals => "'='", TokenType.LeftParenthesis => "'('", TokenType.RightParenthesis => "')'",
        TokenType.LeftBracket => "'['", TokenType.RightBracket => "']'", TokenType.Greater => "'>'",
        TokenType.Comma => "','", _ => type.ToString()
    };
    private void EnterNesting()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Error("Code is nested too deeply for the available stack. Split it into smaller expressions or functions.");
        if (nestingDepth >= MaximumNesting)
            throw Error($"Code is nested too deeply (limit {MaximumNesting}). Split it into smaller expressions or functions.");
        nestingDepth++;
    }
    private T Located<T>(T node, Token token) where T : IAstNode
    {
        if (node is IExpression expression) CheckExpressionDepth(expression, token);
        SourceMap.Set(node, token.Line, token.Column);
        return node;
    }
    private void CheckExpressionDepth(IExpression expression, Token token)
    {
        int Depth(IExpression child) => expressionDepths.GetValueOrDefault(child, 1);
        int depth = expression switch
        {
            BinaryExpression binary => 1 + Math.Max(Depth(binary.Left), Depth(binary.Right)),
            ComparisonExpression comparison => 1 + Math.Max(Depth(comparison.Left), Depth(comparison.Right)),
            LogicalExpression logical => 1 + Math.Max(Depth(logical.Left), Depth(logical.Right)),
            UnaryExpression unary => 1 + Depth(unary.Operand),
            IndexingExpression index => 1 + Math.Max(Depth(index.Name), Depth(index.IndexValue)),
            ListLiteral list => 1 + (list.Values.Count == 0 ? 0 : list.Values.Max(Depth)),
            CallExpression call => 1 + (call.Arguments.Count == 0 ? 0 : call.Arguments.Max(Depth)),
            _ => 1
        };
        if (depth > MaximumNesting)
            throw ErrorAt(token, $"Expression is nested too deeply (limit {MaximumNesting}). Split it into smaller expressions or functions.");
        expressionDepths[expression] = depth;
    }
    public List<IStatement> Parse()
    {
        List<IStatement> result = [];
        while (Current != null) result.Add(ParseStatement());
        return result;
    }
    private List<IStatement> Block(Token opener, params TokenType[] terminators)
    {
        List<IStatement> result = [];
        while (Current != null && !terminators.Contains(Current.Type)) result.Add(ParseStatement());
        if (Current == null)
            throw ErrorAt(opener, $"The '{opener.Value}' block opened here is missing 'conclude'. Add 'conclude' after its body.", true);
        return result;
    }
    private List<IStatement> ClosedBlock(Token opener)
    {
        var result = Block(opener, TokenType.ConcludeStatement);
        Consume(TokenType.ConcludeStatement);
        return result;
    }
    private IStatement ParseStatement()
    {
        var start = Current!;
        EnterNesting();
        try { return Located(ParseStatementCore(start), start); }
        finally { nestingDepth--; }
    }
    private IStatement ParseStatementCore(Token opener)
    {
        if (Take(TokenType.Declare))
        {
            string name = Consume(TokenType.Identifier).Value;
            MaxType? type = Take(TokenType.As) ? ParseType() : null;
            Consume(TokenType.Equals);
            var value = ParseExpression();
            IExpression? resort = null;
            if (Take(TokenType.Comma))
            {
                Consume(TokenType.Resort);
                var resortStart = Current;
                resort = ParseExpression();
                if (resort is NilLiteral) throw ErrorAt(resortStart!, "A resort cannot be nil. Give it a usable fallback value.");
            }
            return new DeclarationStatement(name, type, value, resort);
        }
        if (Take(TokenType.Say)) return new SayStatement(ParseExpression());
        if (Take(TokenType.Function)) return ParseFunction(opener);
        if (Take(TokenType.Return))
        {
            if (functionDepth == 0) throw ErrorAt(opener, "'return' can only appear inside a function.");
            return new ReturnStatement(ParseExpression());
        }
        if (Take(TokenType.IfBegin)) return ParseIf(opener);
        if (Take(TokenType.While))
        {
            var condition = ParseExpression(); Consume(TokenType.Do);
            return new WhileStatement(condition, ClosedBlock(opener));
        }
        if (Take(TokenType.For))
        {
            var name = Consume(TokenType.Identifier);
            Consume(TokenType.Equals);
            var start = ParseExpression(); Consume(TokenType.To);
            var end = ParseExpression();
            IExpression? step = null;
            if (Take(TokenType.Comma)) { Consume(TokenType.Step); step = ParseExpression(); }
            Consume(TokenType.Do);
            var declaration = Located(new DeclarationStatement(name.Value, new IntegerType(), start, null), name);
            return new ForStatement(declaration, end, ClosedBlock(opener), step);
        }
        if (Take(TokenType.Foreach))
        {
            var name = Consume(TokenType.Identifier); Consume(TokenType.In);
            var collection = ParseExpression(); Consume(TokenType.Do);
            var declaration = Located(new DeclarationStatement(name.Value, null, collection, null), name);
            return new ForeachStatement(declaration, collection, ClosedBlock(opener));
        }
        if (Match(TokenType.Identifier))
        {
            var target = ParsePostfix();
            if (target is CallExpression && (Current == null || !IsAssignment(Current.Type)))
                return new ExpressionStatement(target);
            if (target is not VariableReference && target is not IndexingExpression)
                throw Error("Expected a variable or list element to assign to.");
            if (Current == null || !IsAssignment(Current.Type))
            {
                string? suggestion = new[] { "declare", "say", "if", "while", "for", "foreach", "function", "return", "elif", "maybe", "else", "conclude" }
                    .FirstOrDefault(word => CloseSpelling(opener.Value, word));
                if (suggestion != null)
                    throw ErrorAt(opener, $"Expected an assignment or a function call. Did you mean '{suggestion}'? Keywords use lowercase letters.");
                throw Error("Expected an assignment operator ('=', '+=', '-=', '*=' or '/=') or a function call.");
            }
            string operation = tokens[position++].Value;
            return new AssignmentStatement(target, ParseExpression(), operation);
        }
        if (Match(TokenType.ConcludeStatement))
            throw Error("There is no open block to 'conclude' here.");
        if (Current?.Type is TokenType.Elif or TokenType.Else or TokenType.Maybe)
            throw Error("This branch belongs inside an 'if' block, before its 'conclude'.");
        throw Error("Expected a statement: 'declare', 'say', 'if', a loop, a function, or an assignment.");
    }
    private static bool IsAssignment(TokenType t) => t is TokenType.Equals or TokenType.PlusEqual
        or TokenType.MinusEqual or TokenType.MultiplyEqual or TokenType.DivideEqual;

    private MaxType ParseType()
    {
        EnterNesting();
        try
        {
            var name = Consume(TokenType.Identifier);
            if (!TypeRegistry.Max_TypeNames.TryGetValue(name.Value, out var type))
            {
                string? suggestion = name.Value switch
                { "integer" => "int", "bool" => "boolean", "text" => "string", _ => TypeRegistry.Max_TypeNames.Keys.FirstOrDefault(word => CloseSpelling(name.Value, word)) };
                throw ErrorAt(name, $"Unknown type '{name.Value}'. Use {string.Join(", ", TypeRegistry.Max_TypeNames.Keys.Select(t => $"'{t}'"))}."
                    + (suggestion == null ? "" : $" Did you mean '{suggestion}'?"));
            }
            if (type is ListType)
            {
                if (Take(TokenType.Of)) type = new ListType(ParseType());
                else if (Take(TokenType.Lesser))
                {
                    var element = ParseType(); Consume(TokenType.Greater); type = new ListType(element);
                }
            }
            return Take(TokenType.QuestionMark) ? new OptionalType(type) : type;
        }
        finally { nestingDepth--; }
    }
    private FunctionStatement ParseFunction(Token opener)
    {
        string name = Consume(TokenType.Identifier).Value;
        List<FunctionParameter> parameters = [];
        var parameterStart = Current;
        bool parentheses = Take(TokenType.LeftParenthesis);
        bool words = !parentheses && Take(TokenType.With);
        if (words || (parentheses && !Match(TokenType.RightParenthesis)))
        {
            do
            {
                var parameter = Consume(TokenType.Identifier);
                if (parameters.Any(p => p.Name == parameter.Value))
                    throw ErrorAt(parameter, $"Duplicate parameter '{parameter.Value}'. Each parameter needs a different name.");
                parameters.Add(new(parameter.Value, Take(TokenType.As) ? ParseType() : null));
            } while (Take(TokenType.Comma));
        }
        if (parentheses) ConsumeClosing(TokenType.RightParenthesis, parameterStart!, "function parameters");
        MaxType? returns = Take(TokenType.Returns) ? ParseType() : null;
        Consume(TokenType.Do);
        functionDepth++;
        try { return new FunctionStatement(name, parameters, returns, ClosedBlock(opener)); }
        finally { functionDepth--; }
    }
    private IfStatement ParseIf(Token opener)
    {
        var condition = ParseExpression(); Consume(TokenType.Then);
        TokenType[] ends = [TokenType.Elif, TokenType.Maybe, TokenType.Else, TokenType.ConcludeStatement];
        var first = new ConditionalBranch(condition, Block(opener, ends));
        List<ConditionalBranch> branches = [];
        while (Take(TokenType.Elif))
        {
            condition = ParseExpression(); Consume(TokenType.Then);
            branches.Add(new(condition, Block(opener, ends)));
        }
        List<IStatement> maybe = [], otherwise = [];
        if (Take(TokenType.Maybe)) maybe = Block(opener, ends);
        if (Take(TokenType.Else)) otherwise = Block(opener, ends);
        if (Match(TokenType.Maybe) || Match(TokenType.Else))
            throw Error("'maybe' and 'else' may each appear once, in that order.");
        if (Match(TokenType.Elif)) throw Error("Place every 'elif' before 'maybe' and 'else'.");
        Consume(TokenType.ConcludeStatement);
        return new(first, branches, maybe, otherwise);
    }
    private IExpression ParseExpression()
    {
        EnterNesting();
        try { return ParseOr(); }
        finally { nestingDepth--; }
    }
    private IExpression ParseOr()
    {
        var left = ParseAnd();
        while (Match(TokenType.Or))
        {
            var op = Consume(TokenType.Or);
            left = Located(new LogicalExpression(left, LogicalOperator.Or, ParseAnd()), op);
        }
        return left;
    }
    private IExpression ParseAnd()
    {
        var left = ParseComparison();
        while (Match(TokenType.And))
        {
            var op = Consume(TokenType.And);
            left = Located(new LogicalExpression(left, LogicalOperator.And, ParseComparison()), op);
        }
        return left;
    }
    private static bool IsComparison(TokenType type) => type is TokenType.Comparator or TokenType.NotEqualTo or TokenType.Lesser
        or TokenType.LesserEqual or TokenType.Greater or TokenType.GreaterEqual;
    private IExpression ParseComparison()
    {
        var left = ParseSum();
        if (Current is Token op && IsComparison(op.Type))
        {
            position++;
            left = Located(new ComparisonExpression(left, op.Value, ParseSum()), op);
            if (Current is Token next && IsComparison(next.Type))
                throw Error("Chained comparisons are ambiguous. Join separate comparisons with 'and'.");
        }
        return left;
    }
    private IExpression ParseSum()
    {
        var left = ParseProduct();
        while (Current?.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = tokens[position++];
            left = Located(new BinaryExpression(left, op.Value, ParseProduct()), op);
        }
        return left;
    }
    private IExpression ParseProduct()
    {
        var left = ParseUnary();
        while (Current?.Type is TokenType.Multiply or TokenType.Divide)
        {
            var op = tokens[position++];
            left = Located(new BinaryExpression(left, op.Value, ParseUnary()), op);
        }
        return left;
    }
    private IExpression ParseUnary()
    {
        if (Current?.Type is not (TokenType.Not or TokenType.Minus)) return ParsePostfix();
        var op = tokens[position++];
        EnterNesting();
        try
        {
            if (op.Type == TokenType.Minus && Match(TokenType.Integer) && Current!.Value.TrimStart('0') == "2147483648")
            {
                position++;
                return Located(new IntegerLiteral(int.MinValue), op);
            }
            return Located(new UnaryExpression(op.Type == TokenType.Not ? UnaryOperator.Not : UnaryOperator.Negate, ParseUnary()), op);
        }
        finally { nestingDepth--; }
    }
    private IExpression ParsePostfix()
    {
        var result = ParsePrimary();
        while (true)
        {
            if (Match(TokenType.LeftParenthesis))
            {
                var opener = Consume(TokenType.LeftParenthesis);
                if (result is not VariableReference name) throw ErrorAt(opener, "Only named functions can be called, such as 'length(items)'.");
                var arguments = ExpressionList(TokenType.RightParenthesis, opener, "function arguments");
                var call = new CallExpression(name.Name, arguments);
                var nameLocation = SourceMap.Get(name);
                CheckExpressionDepth(call, opener);
                SourceMap.Set(call, nameLocation?.Line ?? opener.Line, nameLocation?.Column ?? opener.Column);
                result = call;
            }
            else if (Match(TokenType.LeftBracket))
            {
                var opener = Consume(TokenType.LeftBracket);
                if (Match(TokenType.RightBracket)) throw Error("An index needs an expression between '[' and ']'.");
                var index = ParseExpression(); ConsumeClosing(TokenType.RightBracket, opener, "index");
                result = Located(new IndexingExpression(result, index), opener);
            }
            else return result;
        }
    }
    private void ConsumeClosing(TokenType close, Token opener, string context)
    {
        if (Current == null)
            throw ErrorAt(opener, $"Missing {Describe(close)} to close the {context} opened here.", true);
        if (!Match(close))
            throw Error($"Expected {Describe(close)} to close the {context} opened at line {opener.Line}, column {opener.Column}.");
        Consume(close);
    }
    private List<IExpression> ExpressionList(TokenType close, Token opener, string context)
    {
        List<IExpression> items = [];
        if (Current == null) ConsumeClosing(close, opener, context);
        if (!Match(close))
        {
            do
            {
                if (Current == null) ConsumeClosing(close, opener, context);
                items.Add(ParseExpression());
            } while (Take(TokenType.Comma) && !Match(close));
        }
        ConsumeClosing(close, opener, context);
        return items;
    }
    private IExpression ParsePrimary()
    {
        var token = Current ?? throw Error("Expected an expression, such as a value, variable, or function call.");
        position++;
        IExpression result;
        switch (token.Type)
        {
            case TokenType.Integer:
                if (!int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    throw ErrorAt(token, "Integer literal is too large; 2147483648 is only valid after '-' to make the minimum integer.");
                result = new IntegerLiteral(number); break;
            case TokenType.Float: result = new FloatLiteral(double.Parse(token.Value, CultureInfo.InvariantCulture)); break;
            case TokenType.String: result = new StringLiteral(token.Value); break;
            case TokenType.Nil: result = new NilLiteral(); break;
            case TokenType.Boolean: result = token.Value == "possible" ? new PossibleLiteral() : new BooleanLiteral(token.Value == "true"); break;
            case TokenType.Identifier: result = new VariableReference(token.Value); break;
            case TokenType.LeftBracket: result = new ListLiteral(ExpressionList(TokenType.RightBracket, token, "list")); break;
            case TokenType.LeftParenthesis:
                var expression = ParseExpression(); ConsumeClosing(TokenType.RightParenthesis, token, "parenthesized expression"); return expression;
            default: throw ErrorAt(token, $"Expected an expression, found '{token.Value}'. Use a value, variable, or function call here.");
        }
        return Located(result, token);
    }
    private static bool CloseSpelling(string actual, string expected)
    {
        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return actual != expected;
        if (Math.Abs(actual.Length - expected.Length) > 1) return false;
        if (actual.Length == expected.Length)
        {
            var different = Enumerable.Range(0, actual.Length).Where(i => actual[i] != expected[i]).Take(3).ToArray();
            return different.Length == 1 || (different.Length == 2 && different[1] == different[0] + 1
                && actual[different[0]] == expected[different[1]] && actual[different[1]] == expected[different[0]]);
        }
        string shorter = actual.Length < expected.Length ? actual : expected;
        string longer = actual.Length < expected.Length ? expected : actual;
        int a = 0, b = 0, skipped = 0;
        while (a < shorter.Length && b < longer.Length)
        {
            if (shorter[a] == longer[b]) { a++; b++; }
            else { if (++skipped > 1) return false; b++; }
        }
        return true;
    }
}
