// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    public interface IAstNode
    {
        
    }

    public interface IExpression : IAstNode
    {
    }

    public interface IStatement : IAstNode
    {
    }

    public enum TokenType
    {
        Integer,
        Float,
        String,
        Identifier,
        Nil,
        As,
        To,
        Step,
        Of,
        In,

        Declare,
        Resort,
        Say,
        Boolean,

        IfBegin,
        Then,
        Elif,
        Else,
        Maybe,
        ConcludeStatement,

        While,
        Do,
        For,
        Foreach,
        Function,
        With,
        Returns,
        Return,

        Equals,
        Comparator,

        Plus,
        Minus,
        Multiply,
        Divide,

        PlusEqual,
        MinusEqual,
        MultiplyEqual,
        DivideEqual,

        Comma,
        Not,
        And,
        Or,
        NotEqualTo,

        LeftParenthesis,
        RightParenthesis,
        LeftBracket,
        RightBracket,
        QuestionMark,

        Lesser,
        LesserEqual,
        Greater,
        GreaterEqual
    }

    public enum UnaryOperator
    {
        Not,
        Negate
    }

    public enum LogicalOperator
    {
        And,
        Or
    }
}
