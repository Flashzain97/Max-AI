// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    public class IntegerLiteral(int value) : IExpression
    {
        public int Value { get; } = value;
    }

    public class FloatLiteral(double value) : IExpression
    {
        public double Value { get; } = value;
    }

    public class StringLiteral(string value) : IExpression
    {
        public string Value { get; } = value;
    }

    public class BooleanLiteral(bool value) : IExpression
    {
        public bool Value { get; } = value;
    }

    public class ListLiteral(List<IExpression> values) : IExpression
    {
        public List<IExpression> Values { get; } = values;
    }

    public class PossibleLiteral : IExpression
    {
    }

    public class NilLiteral : IExpression
    {
    }

    public class VariableReference(string name) : IExpression
    {
        public string Name { get; } = name;
    }
}