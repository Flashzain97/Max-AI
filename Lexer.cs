// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine
using System.Globalization;
using System.Text;

namespace Max;

public class Lexer(string code)
{
    private int position;
    private int line = 1;
    private int column = 1;
    private char Current => position < code.Length ? code[position] : '\0';
    private char Peek => position + 1 < code.Length ? code[position + 1] : '\0';
    private bool IsLetter => char.IsLetter(Current) ||
        (Rune.TryCreate(Current, Peek, out Rune rune) && Rune.IsLetter(rune));
    private bool IsDigit => char.IsDigit(Current) ||
        (Rune.TryCreate(Current, Peek, out Rune rune) && Rune.IsDigit(rune));
    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
    private void AdvanceNameCharacter()
    {
        bool surrogatePair = char.IsHighSurrogate(Current) && char.IsLowSurrogate(Peek);
        Advance();
        if (surrogatePair) Advance();
    }
    private void Advance()
    {
        if (Current == '\r' || (Current == '\n' && (position == 0 || code[position - 1] != '\r')))
        { line++; column = 1; }
        else if (Current != '\n') column++;
        position++;
    }

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["declare"] = TokenType.Declare, ["resort"] = TokenType.Resort,
        ["say"] = TokenType.Say, ["true"] = TokenType.Boolean,
        ["false"] = TokenType.Boolean, ["possible"] = TokenType.Boolean,
        ["if"] = TokenType.IfBegin, ["then"] = TokenType.Then,
        ["elif"] = TokenType.Elif, ["else"] = TokenType.Else,
        ["maybe"] = TokenType.Maybe, ["conclude"] = TokenType.ConcludeStatement,
        ["while"] = TokenType.While, ["for"] = TokenType.For,
        ["foreach"] = TokenType.Foreach, ["do"] = TokenType.Do,
        ["nil"] = TokenType.Nil, ["as"] = TokenType.As,
        ["not"] = TokenType.Not, ["and"] = TokenType.And, ["or"] = TokenType.Or,
        ["to"] = TokenType.To, ["step"] = TokenType.Step,
        ["of"] = TokenType.Of, ["in"] = TokenType.In,
        ["function"] = TokenType.Function, ["with"] = TokenType.With,
        ["returns"] = TokenType.Returns, ["return"] = TokenType.Return
    };

    public List<Token> Tokenize()
    {
        List<Token> tokens = [];
        while (position < code.Length)
        {
            if (char.IsWhiteSpace(Current)) { Advance(); continue; }
            if (Current == '#' || (Current == '/' && Peek == '/'))
            {
                while (position < code.Length && Current is not ('\r' or '\n')) Advance();
                continue;
            }
            int start = position, startLine = line, startColumn = column;
            TokenType type;
            string value;
            if (IsLetter || Current == '_')
            {
                while (IsLetter || IsDigit || Current == '_') AdvanceNameCharacter();
                value = code[start..position];
                type = Keywords.GetValueOrDefault(value, TokenType.Identifier);
            }
            else if (IsAsciiDigit(Current))
            {
                bool decimalPoint = false;
                while (IsAsciiDigit(Current) || Current == '.')
                {
                    if (Current == '.')
                    {
                        if (decimalPoint) throw Error("A number cannot contain two decimal points.");
                        decimalPoint = true;
                    }
                    Advance();
                }
                value = code[start..position];
                type = decimalPoint ? TokenType.Float : TokenType.Integer;
                if (IsDigit)
                    throw Error("Number literals use the digits 0 to 9. Replace the non-ASCII digit.");
                if ((!decimalPoint && (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long integer) || integer > 2147483648L)) ||
                    (decimalPoint && (!double.TryParse(value, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))))
                    throw ErrorAt(startLine, startColumn, decimalPoint
                        ? "This decimal number is outside the supported finite range."
                        : "This integer is outside the supported range (-2147483648 to 2147483647).");
                if (IsLetter || Current == '_')
                    throw Error("Separate a number from a name with a space or an operator.");
            }
            else if (IsDigit)
                throw Error("Number literals use the digits 0 to 9. Replace the non-ASCII digit.");
            else if (Current == '"')
            {
                Advance();
                StringBuilder text = new();
                while (position < code.Length && Current != '"')
                {
                    if (Current is '\r' or '\n')
                        throw ErrorAt(startLine, startColumn, "A string cannot span lines. Use \\n for a newline.");
                    if (Current == '\\')
                    {
                        Advance();
                        if (position >= code.Length)
                            throw ErrorAt(startLine, startColumn, "Unterminated string after '\\'. Finish the escape and close the string with '\"'.");
                        if (Current is '\r' or '\n')
                            throw ErrorAt(startLine, startColumn, "A string cannot span lines. Use \\n for a newline.");
                        text.Append(Current switch
                        {
                            'n' => '\n', 'r' => '\r', 't' => '\t',
                            '"' => '"', '\\' => '\\',
                            _ => throw Error("Unknown string escape. Use \\n, \\r, \\t, \\\" or \\\\.")
                        });
                    }
                    else text.Append(Current);
                    Advance();
                }
                if (position >= code.Length) throw ErrorAt(startLine, startColumn, "Unterminated string. Add a closing '\"'.");
                Advance();
                value = text.ToString(); type = TokenType.String;
            }
            else
            {
                char symbol = Current;
                Advance();
                if (char.IsHighSurrogate(symbol) && char.IsLowSurrogate(Current)) Advance();
                bool pair = Current == '=' && "=+-*/<>!".Contains(symbol);
                if (pair) Advance();
                value = code[start..position];
                type = value switch
                {
                    "=" => TokenType.Equals, "==" => TokenType.Comparator,
                    "+" => TokenType.Plus, "-" => TokenType.Minus,
                    "*" => TokenType.Multiply, "/" => TokenType.Divide,
                    "+=" => TokenType.PlusEqual, "-=" => TokenType.MinusEqual,
                    "*=" => TokenType.MultiplyEqual, "/=" => TokenType.DivideEqual,
                    "<" => TokenType.Lesser, "<=" => TokenType.LesserEqual,
                    ">" => TokenType.Greater, ">=" => TokenType.GreaterEqual,
                    "!" => TokenType.Not, "!=" => TokenType.NotEqualTo,
                    "(" => TokenType.LeftParenthesis, ")" => TokenType.RightParenthesis,
                    "[" => TokenType.LeftBracket, "]" => TokenType.RightBracket,
                    "?" => TokenType.QuestionMark,
                    "," => TokenType.Comma,
                    _ => throw ErrorAt(startLine, startColumn, $"Unknown character '{value}'.")
                };
            }
            tokens.Add(new Token(type, value, startLine, startColumn, line, column));
        }
        SourceMap.SetEndOfFile(tokens, line, column);
        return tokens;
    }
    private MaxSyntaxException Error(string message) => ErrorAt(line, column, message);
    private static MaxSyntaxException ErrorAt(int line, int column, string message) =>
        SourceMap.WithLocation(new MaxSyntaxException($"Line {line}, column {column}: {message}"), line, column);
}
