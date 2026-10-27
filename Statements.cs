// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    /// <summary>
    /// A class for both if and elif statements
    /// </summary>
    /// <param name="check_statement">The condition to check</param>
    /// <param name="consequences">The resulting code if true</param>
    public class ConditionalBranch(IExpression check_statement, List<IStatement> consequences)
    {
        public IExpression Check_Statement { get; } = check_statement;
        public List<IStatement> Consequences { get; } = consequences;
    }
    
    public class IfStatement(ConditionalBranch first, List<ConditionalBranch> branches, List<IStatement> maybe, List<IStatement> @else) : IStatement
    {
        public ConditionalBranch First_Consequences { get; } = first;
        public List<ConditionalBranch> Other_Consequences = branches;
        public List<IStatement> Maybe { get; } = maybe;
        public List<IStatement> Else { get; } = @else;
    }

    public class WhileStatement(IExpression check_statement, List<IStatement> consequences) : IStatement
    {
        public IExpression Check_Statement { get; } = check_statement;
        public List<IStatement> Consequences { get; } = consequences;
    }

    /// <summary>
    /// Numeric and countable iteration
    /// </summary>
    /// <param name="declaration">Declaration of the integer to reference</param>
    /// <param name="expression">Expression integer must reach</param>
    /// <param name="step">Integer amount the declaration will step towards the expression. Automatically 1</param>
    public class ForStatement(DeclarationStatement declaration, IExpression expression,  List<IStatement> consequences, IExpression? step) : IStatement
    {
        public DeclarationStatement Declaration { get; } = declaration;
        public IExpression Expression { get; } = expression;
        public IExpression Step { get; } = step ?? new IntegerLiteral(1);

        public List<IStatement> Consequences { get; } = consequences;
    }

    public class ForeachStatement(DeclarationStatement declaration, IExpression iterableCollection, List<IStatement> consequences) : IStatement
    {
        public DeclarationStatement Declaration { get; } = declaration;
        public IExpression IterableCollection { get; } = iterableCollection;
        public List<IStatement> Consequences { get; } = consequences;
    }

    public class DeclarationStatement(string name, MaxType? type, IExpression value, IExpression? resortable) : IStatement
    {
        public string Name { get; } = name;
        public MaxType? Type { get; } = type;
        public IExpression Value { get;} = value;
        public IExpression? Resortable { get; } = resortable;
    }

    public class AssignmentStatement(IExpression target, IExpression value, string assignmentType) : IStatement
    {
        public IExpression Target { get; } = target;
        public IExpression Value { get; } = value;
        public string AssignmentType { get; } = assignmentType;
    }

    public class SayStatement(IExpression value) : IStatement
    {
        public IExpression Value { get; } = value;
    }
}