// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    public class Token(TokenType type, string value, int line = 1, int column = 1,
        int? endLine = null, int? endColumn = null)
    {
        public TokenType Type { get; } = type;
        public string Value { get; } = value;
        public int Line { get; } = line;
        public int Column { get; } = column;
        public int EndLine { get; } = endLine ?? line;
        public int EndColumn { get; } = endColumn ?? column + value.Length;

        public override string ToString()
        {
            return $"Token({Type}, {Value})";
        }
    }
}
