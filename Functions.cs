namespace Max;

public record FunctionParameter(string Name, MaxType? Type);

public class FunctionStatement(string name, List<FunctionParameter> parameters,
    MaxType? returnType, List<IStatement> body) : IStatement
{
    public string Name { get; } = name;
    public List<FunctionParameter> Parameters { get; } = parameters;
    public MaxType? ReturnType { get; } = returnType;
    public List<IStatement> Body { get; } = body;
}

public class CallExpression(string name, List<IExpression> arguments) : IExpression
{
    public string Name { get; } = name;
    public List<IExpression> Arguments { get; } = arguments;
}

public class ExpressionStatement(IExpression expression) : IStatement
{
    public IExpression Expression { get; } = expression;
}

public class ReturnStatement(IExpression value) : IStatement
{
    public IExpression Value { get; } = value;
}

public record UserFunction(FunctionStatement Declaration, Scope Closure);

// Internal control flow; caught only by the function that is returning.
internal class ReturnSignal(object value) : Exception
{
    public object Value { get; } = value;
}
