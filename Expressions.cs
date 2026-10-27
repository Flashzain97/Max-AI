// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    public class BinaryExpression(IExpression left, string operation, IExpression right) : IExpression
    {
        public IExpression Left { get; } = left;
        public string Operator { get; } = operation;
        public IExpression Right { get; } = right;
    }

    public class ComparisonExpression(IExpression left, string operation, IExpression right) : IExpression
    {
        public IExpression Left { get; } = left;
        public string Operator { get; } = operation;
        public IExpression Right { get; } = right;

        public override string ToString()
        {
            return $"Left hand: {Left}, " +
                   $"Operator: {Operator}, " +
                   $"Right hand: {Right}";
        }
    }

    public class UnaryExpression(UnaryOperator @operator, IExpression operand) : IExpression
    {
        public UnaryOperator Operator { get; } = @operator;
        public IExpression Operand { get; } = operand;
    }

    public class LogicalExpression(IExpression left, LogicalOperator @operator, IExpression right) : IExpression
    {
        public IExpression Left { get; } = left;
        public LogicalOperator Operator { get; } = @operator;
        public IExpression Right { get; } = right;
    }

    public class IndexingExpression(IExpression name, IExpression indexValue) : IExpression
    {
        public IExpression Name { get; } = name;
        public IExpression IndexValue { get; } = indexValue;
    }
}