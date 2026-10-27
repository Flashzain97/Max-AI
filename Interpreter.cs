// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Max;

public class Interpreter
{
    private Scope currentScope = new();
    private int callDepth;
    private int evaluationDepth;
    public int MaximumCallDepth { get; init; } = 128;
    private const int MaximumEvaluationDepth = 512;
    private const int MaximumValueNesting = 256;
    private static readonly HashSet<string> BuiltinNames = new(StringComparer.Ordinal)
        { "text", "length", "input", "integer", "decimal", "append", "remove" };

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void EvaluateAll(List<IStatement> statements)
    {
        if (!Execution.HasDedicatedStack)
        {
            Execution.Run(() => EvaluateAll(statements));
            return;
        }
        foreach (var statement in statements) Evaluate(statement);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public object Evaluate(IAstNode node)
    {
        if (!Execution.HasDedicatedStack) return Execution.Run(() => Evaluate(node));
        try
        {
            evaluationDepth++;
            if (evaluationDepth > MaximumEvaluationDepth)
                throw new MaxInfiniteRecursionException($"Maximum combined expression and function nesting ({MaximumEvaluationDepth}) exceeded. Simplify nested expressions or reduce recursion.");
            if (!RuntimeStackGuard.HasRoom())
                throw new MaxInfiniteRecursionException("The program is nested too deeply for the available execution stack. Reduce function recursion or expression nesting.");
            return EvaluateCore(node);
        }
        catch (Exception error)
        {
            if (error is not ReturnSignal && error.GetType().Namespace == "Max" &&
                error.Data["MaxLocation"] == null && SourceMap.Get(node) is SourcePosition location)
                error.Data["MaxLocation"] = location;
            throw;
        }
        finally { evaluationDepth--; }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object EvaluateCore(IAstNode node)
    {
        switch (node)
        {
            case IntegerLiteral n: return n.Value;
            case FloatLiteral n: return n.Value;
            case StringLiteral s: return s.Value;
            case BooleanLiteral b: return new Bool(b.Value);
            case PossibleLiteral: return new Possible();
            case NilLiteral: return new Nil();
            case VariableReference v: return GetVariable(v.Name).Value;
            case ListLiteral l: return MakeList(l);
            case BinaryExpression b: return Binary(b.Operator, Evaluate(b.Left), Evaluate(b.Right));
            case ComparisonExpression c: return Compare(c.Operator, Evaluate(c.Left), Evaluate(c.Right));
            case LogicalExpression l: return Logical(l);
            case UnaryExpression u: return Unary(u);
            case IndexingExpression i: return Index(i);
            case CallExpression c: return Call(c);
            case ExpressionStatement e: return Evaluate(e.Expression);
            case ReturnStatement r:
                if (callDepth == 0) throw new MaxSyntaxException("'return' can only run inside a function.");
                throw new ReturnSignal(Evaluate(r.Value));
            case FunctionStatement f: return DefineFunction(f);
            case SayStatement s: return Say(s);
            case DeclarationStatement d: return Declaration(d);
            case AssignmentStatement a: return Assign(a);
            case IfStatement i: return If(i);
            case WhileStatement w: return While(w);
            case ForStatement f: return For(f);
            case ForeachStatement f: return Foreach(f);
            default: throw new MaxSyntaxException($"Cannot evaluate {node.GetType().Name}.");
        }
    }

    private List MakeList(ListLiteral literal)
    {
        var list = new List(null);
        foreach (var item in literal.Values) list.Values.Add(Evaluate(item));
        return list;
    }
    private object Unary(UnaryExpression expression)
    {
        var operand = Evaluate(expression.Operand);
        return (expression.Operator, operand) switch
        {
            (UnaryOperator.Not, Bool b) => new Bool(!b.Value),
            (UnaryOperator.Not, Possible) => new Possible(),
            (UnaryOperator.Negate, int n) => Negate(n),
            (UnaryOperator.Negate, double n) => -n,
            _ => throw new MaxTypeException("'not' requires a boolean; unary '-' requires a number.")
        };
    }
    private object Index(IndexingExpression expression)
    {
        var target = Evaluate(expression.Name);
        int index = ResolveIndex(Evaluate(expression.IndexValue), Length(target));
        return target is List items ? items.Values[index] : TextAt((string)target, index);
    }
    private Nil DefineFunction(FunctionStatement function)
    {
        EnsureFunctionName(function.Name);
        var parameterScope = new Scope(currentScope);
        foreach (var parameter in function.Parameters)
        {
            EnsureVariableName(parameter.Name, parameterScope);
            if (parameter.Name == function.Name)
                throw new MaxVariableException($"Parameter '{parameter.Name}' would conflict with its function's name.");
            parameterScope.Variables.Add(parameter.Name, new Variable(parameter.Name, new Nil(), parameter.Type, null));
        }
        currentScope.Functions.Add(function.Name, new(function, currentScope));
        return new Nil();
    }
    private object Say(SayStatement statement)
    {
        var output = Evaluate(statement.Value);
        Console.WriteLine(Format(output));
        return output;
    }
    private object Declaration(DeclarationStatement statement)
    {
        var value = Evaluate(statement.Value);
        var resort = statement.Resortable == null ? null : Evaluate(statement.Resortable);
        if (resort is Nil) throw new MaxNilException("A resort cannot be nil.");
        Declare(statement.Name, value, statement.Type, resort);
        return GetVariable(statement.Name).Value;
    }
    private Nil If(IfStatement statement)
    {
        foreach (var branch in new[] { statement.First_Consequences }.Concat(statement.Other_Consequences))
        {
            var check = Condition(Evaluate(branch.Check_Statement));
            if (check is Possible) { ExecuteBlock(statement.Maybe); return new Nil(); }
            if (check is Bool { Value: true }) { ExecuteBlock(branch.Consequences); return new Nil(); }
        }
        ExecuteBlock(statement.Else);
        return new Nil();
    }
    private Nil While(WhileStatement statement)
    {
        while (Condition(Evaluate(statement.Check_Statement)) is Bool { Value: true }) ExecuteBlock(statement.Consequences);
        return new Nil();
    }
    private Nil Foreach(ForeachStatement statement)
    {
        if (Evaluate(statement.IterableCollection) is not List collection) throw new MaxTypeException("'foreach' requires a list.");
        var saved = currentScope;
        EnsureVariableName(statement.Declaration.Name, new Scope(saved));
        var elementType = collection.ElementType;
        // Each item has a fresh binding, including when the list is empty.
        foreach (var item in collection.Values.ToArray())
        {
            currentScope = new Scope(saved);
            try { Declare(statement.Declaration.Name, item, elementType, null); ExecuteBlock(statement.Consequences); }
            finally { currentScope = saved; }
        }
        return new Nil();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ExecuteBlock(List<IStatement> body)
    {
        var saved = currentScope;
        currentScope = new Scope(saved);
        try { EvaluateAll(body); }
        finally { currentScope = saved; }
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object Call(CallExpression call)
    {
        try { return CallCore(call); }
        catch (Exception error)
        {
            if (error is ReturnSignal || error.GetType().Namespace != "Max") throw;
            if (error.Data["MaxCallStack"] is not List<string> calls)
            {
                calls = [];
                error.Data["MaxCallStack"] = calls;
            }
            calls.Add(call.Name);
            throw;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object CallCore(CallExpression call)
    {
        if (!RuntimeStackGuard.HasRoom())
            throw new MaxInfiniteRecursionException("The program is nested too deeply for the available execution stack. Reduce function recursion or expression nesting.");
        if (BuiltinNames.Contains(call.Name)) return CallBuiltin(call);
        UserFunction? function = null;
        for (Scope? scope = currentScope; scope != null; scope = scope.Parent)
            if (scope.Functions.TryGetValue(call.Name, out function)) break;
        if (function == null) throw new MaxVariableException($"Function '{call.Name}' does not exist. Define it before calling it.");
        var declaration = function.Declaration;
        if (call.Arguments.Count != declaration.Parameters.Count)
            throw new MaxValueException($"Function '{call.Name}' expects {declaration.Parameters.Count} argument(s), received {call.Arguments.Count}.");
        if (callDepth >= MaximumCallDepth) throw new MaxInfiniteRecursionException($"Maximum function depth ({MaximumCallDepth}) exceeded in '{call.Name}'.");
        // Evaluate arguments exactly once in the caller's scope, before binding parameters.
        var values = call.Arguments.Select(EvaluateExpression).ToArray();
        var saved = currentScope;
        currentScope = new Scope(function.Closure);
        callDepth++;
        try
        {
            // Prepare all parameter constraints before mutating any caller-owned list.
            var plan = new Dictionary<List, MaxType>();
            for (int i = 0; i < values.Length; i++)
            {
                var parameter = declaration.Parameters[i];
                EnsureVariableName(parameter.Name, currentScope);
                ValidateAndPlan(parameter.Type, values[i], $"Parameter '{parameter.Name}'", plan);
                currentScope.Variables.Add(parameter.Name, new Variable(parameter.Name, values[i], parameter.Type, null));
            }
            ApplyListTypePlan(plan);
            object result = new Nil();
            try { EvaluateAll(declaration.Body); }
            catch (ReturnSignal signal) { result = signal.Value; }
            ValidateAndApply(declaration.ReturnType, result, $"Return value of '{call.Name}'");
            return result;
        }
        finally { callDepth--; currentScope = saved; }
    }
    private object CallBuiltin(CallExpression call)
    {
        int expected = call.Name is "append" or "remove" ? 2 : 1;
        if (call.Arguments.Count != expected)
            throw new MaxValueException($"{call.Name} expects {expected} argument(s), received {call.Arguments.Count}.");
        var arguments = call.Arguments.Select(EvaluateExpression).ToArray();
        return call.Name switch
        {
            "text" => Format(arguments[0]),
            "length" => Length(arguments[0]),
            "input" => Input(arguments[0]),
            "integer" => Integer(arguments[0]),
            "decimal" => Decimal(arguments[0]),
            "append" => Append(arguments[0], arguments[1]),
            "remove" => Remove(arguments[0], arguments[1]),
            _ => throw new MaxVariableException($"Unknown built-in function '{call.Name}'.")
        };
    }
    private static string Input(object prompt)
    {
        if (prompt is not string text) throw new MaxTypeException("input requires a string prompt.");
        Console.Write(text);
        return Console.ReadLine() ?? throw new MaxValueException("input reached the end of input before a line was entered.");
    }
    private static int Integer(object value)
    {
        if (value is int integer) return integer;
        if (value is string text)
        {
            if (!text.Contains('\0') && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            throw new MaxValueException("integer requires a signed whole-number string within the 32-bit integer range.");
        }
        if (value is double number)
        {
            if (double.IsFinite(number) && number >= int.MinValue && number <= int.MaxValue && Math.Truncate(number) == number)
                return (int)number;
            throw new MaxValueException("integer requires a finite whole number between -2147483648 and 2147483647.");
        }
        throw new MaxTypeException("integer requires a number or a signed whole-number string.");
    }
    private static double Decimal(object value)
    {
        if (value is int integer) return integer;
        if (value is double number)
        {
            if (double.IsFinite(number)) return number;
            throw new MaxValueException("decimal requires a finite number.");
        }
        if (value is string text)
        {
            const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
            if (!text.Contains('\0') && double.TryParse(text, style, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)) return parsed;
            throw new MaxValueException("decimal requires a finite numeric string using '.' as the decimal point.");
        }
        throw new MaxTypeException("decimal requires a number or a numeric string.");
    }
    private static Nil Append(object target, object value)
    {
        if (target is not List list) throw new MaxTypeException("append requires a list as its first argument.");
        if (ContainsList(value, list, new HashSet<List>()))
            throw new MaxValueException("A list cannot contain itself, directly or indirectly.");
        ValidateAndApply(list.ElementType, value, $"Appended list element [{list.Values.Count}]");
        list.Values.Add(value);
        return new Nil();
    }
    private static object Remove(object target, object indexValue)
    {
        if (target is not List list) throw new MaxTypeException("remove requires a list as its first argument.");
        int index = ResolveIndex(indexValue, list.Values.Count);
        var removed = list.Values[index];
        list.Values.RemoveAt(index);
        return removed;
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private object EvaluateExpression(IExpression expression) => Evaluate(expression);
    private object For(ForStatement loop)
    {
        var start = Evaluate(loop.Declaration.Value);
        var end = Evaluate(loop.Expression);
        var step = Evaluate(loop.Step);
        if (start is not int first || end is not int last || step is not int increment)
            throw new MaxTypeException("A for loop's start, end and step must be integers.");
        if (increment == 0) throw new MaxValueException("A for loop's step cannot be zero.");
        if ((increment > 0 && first > last) || (increment < 0 && first < last))
            throw new MaxValueException("The step moves away from the loop's end. Reverse its sign.");
        var saved = currentScope;
        currentScope = new Scope(saved);
        try
        {
            Declare(loop.Declaration.Name, first, new IntegerType(), null);
            // A wider counter prevents wrapping around at the ends of the int range.
            for (long counter = first; increment > 0 ? counter <= last : counter >= last; counter += increment)
            {
                Set(GetVariable(loop.Declaration.Name), (int)counter);
                ExecuteBlock(loop.Consequences);
            }
        }
        finally { currentScope = saved; }
        return new Nil();
    }
    private object Assign(AssignmentStatement assignment)
    {
        if (assignment.Target is VariableReference reference)
        {
            var variable = GetVariable(reference.Name);
            var previous = variable.Value;
            var value = Evaluate(assignment.Value);
            if (assignment.AssignmentType != "=") value = Binary(assignment.AssignmentType[..1], previous, value);
            Set(variable, value); return variable.Value;
        }
        if (assignment.Target is IndexingExpression indexing)
        {
            if (Evaluate(indexing.Name) is not List list) throw new MaxTypeException("Only list elements can be assigned to.");
            int index = ResolveIndex(Evaluate(indexing.IndexValue), list.Values.Count);
            var previous = list.Values[index];
            var value = Evaluate(assignment.Value);
            if (index >= list.Values.Count)
                throw new MaxIndexingException($"Index {index} is outside a collection with {list.Values.Count} item(s) after evaluating the assigned value.");
            if (assignment.AssignmentType != "=") value = Binary(assignment.AssignmentType[..1], previous, value);
            if (ContainsList(value, list, new HashSet<List>())) throw new MaxValueException("A list cannot contain itself, directly or indirectly.");
            ValidateAndApply(list.ElementType, value, $"List element [{index}]");
            list.Values[index] = value; return value;
        }
        throw new MaxSyntaxException("Expected a variable or list element as assignment target.");
    }
    private static bool ContainsList(object value, List target, HashSet<List> visited, int nesting = 0)
    {
        EnsureGraphStack(nesting);
        if (value is not List list) return false;
        if (ReferenceEquals(list, target)) return true;
        return visited.Add(list) && list.Values.Any(item => ContainsList(item, target, visited, nesting + 1));
    }
    private void Declare(string name, object value, MaxType? type, object? resort)
    {
        EnsureVariableName(name, currentScope);
        var plan = new Dictionary<List, MaxType>();
        if (resort != null) ValidateAndPlan(type, resort, $"Resort for '{name}'", plan);
        var actual = value is Nil && resort != null ? resort : value;
        ValidateAndPlan(type, actual, $"Variable '{name}'", plan);
        ApplyListTypePlan(plan);
        var variable = new Variable(name, actual, type, resort);
        currentScope.Variables.Add(name, variable);
    }
    private static void EnsureNotBuiltin(string name)
    {
        if (BuiltinNames.Contains(name)) throw new MaxVariableException($"'{name}' is a built-in function name and cannot be used as a binding.");
    }
    private static void EnsureVariableName(string name, Scope scope)
    {
        EnsureNotBuiltin(name);
        if (scope.Variables.ContainsKey(name)) throw new MaxVariableException($"Variable '{name}' is already declared in this scope.");
        for (Scope? visible = scope; visible != null; visible = visible.Parent)
            if (visible.Functions.ContainsKey(name))
                throw new MaxVariableException($"Variable '{name}' would conflict with a visible function of the same name.");
    }
    private void EnsureFunctionName(string name)
    {
        EnsureNotBuiltin(name);
        if (currentScope.Functions.ContainsKey(name)) throw new MaxVariableException($"Function '{name}' is already defined in this scope.");
        for (Scope? visible = currentScope; visible != null; visible = visible.Parent)
            if (visible.Variables.ContainsKey(name))
                throw new MaxVariableException($"Function '{name}' would conflict with a visible variable of the same name.");
    }
    private Variable GetVariable(string name)
    {
        for (Scope? scope = currentScope; scope != null; scope = scope.Parent)
            if (scope.Variables.TryGetValue(name, out var variable)) return variable;
        throw new MaxVariableException($"Variable '{name}' does not exist.");
    }
    private static void Set(Variable variable, object value)
    {
        if (value is Nil && variable.ResortValue != null) value = variable.ResortValue;
        ValidateAndApply(variable.Type, value, $"Variable '{variable.Name}'");
        variable.SetValue(value);
    }
    private static string? TypeMismatch(MaxType type, object value, string context) =>
        TypeMismatch(type, value, context, new HashSet<(List, MaxType)>(), 0);
    private static string? TypeMismatch(MaxType type, object value, string context,
        HashSet<(List, MaxType)> validated, int nesting)
    {
        EnsureGraphStack(nesting);
        if (type is OptionalType optional)
            return value is Nil ? null : TypeMismatch(optional.InnerType, value, context, validated, nesting + 1);
        if (type is NothingType)
            return value is Nil ? null : $"{context} requires nothing (nil), but received {ValueTypeName(value)} ({Format(value)}).";
        if (value is Nil)
            return $"{context} requires {TypeName(type)}, but received nil. Add '?' to that type to allow nil.";
        if (type is ListType listType)
        {
            if (value is not List list)
                return $"{context} requires {TypeName(type)}, but received {ValueTypeName(value)} ({Format(value)}).";
            if (listType.ElementType == null) return null;
            // Shared sublists form a graph. Rechecking each alias path would take
            // exponential time even when only a few distinct lists exist.
            if (validated.Contains((list, type))) return null;
            for (int i = 0; i < list.Values.Count; i++)
            {
                var mismatch = TypeMismatch(listType.ElementType, list.Values[i], $"{context}[{i}]", validated, nesting + 1);
                if (mismatch != null) return mismatch;
            }
            validated.Add((list, type));
            return null;
        }
        bool accepts = type switch
        {
            IntegerType => value is int,
            FloatType => value is int or double,
            StringType => value is string,
            PossibleType => value is Possible,
            BooleanType => value is BoolLike,
            _ => false
        };
        return accepts ? null : $"{context} requires {TypeName(type)}, but received {ValueTypeName(value)} ({Format(value)}).";
    }
    private static void ValidateAndApply(MaxType? type, object value, string context)
    {
        var plan = new Dictionary<List, MaxType>();
        ValidateAndPlan(type, value, context, plan);
        ApplyListTypePlan(plan);
    }
    private static void ValidateAndPlan(MaxType? type, object value, string context, Dictionary<List, MaxType> plan)
    {
        EnsureAcyclic(value, new HashSet<List>(), new HashSet<List>());
        if (type != null && TypeMismatch(type, value, context) is string mismatch)
            throw new MaxTypeException(mismatch);
        PlanListType(type, value, plan);
    }
    private static void EnsureAcyclic(object value, HashSet<List> visiting, HashSet<List> visited)
    {
        EnsureGraphStack(visiting.Count);
        if (value is not List list || visited.Contains(list)) return;
        if (!visiting.Add(list)) throw new MaxValueException("A list cannot contain itself, directly or indirectly.");
        foreach (var item in list.Values) EnsureAcyclic(item, visiting, visited);
        visiting.Remove(list);
        visited.Add(list);
    }
    private static string TypeName(MaxType type, int nesting = 0)
    {
        EnsureGraphStack(nesting);
        return type switch
        {
            OptionalType o => $"{TypeName(o.InnerType, nesting + 1)}?",
            IntegerType => "int", FloatType => "float", StringType => "string", PossibleType => "possible", BooleanType => "boolean",
            NothingType => "nothing",
            ListType l => l.ElementType == null ? "list" : $"list<{TypeName(l.ElementType, nesting + 1)}>", _ => type.GetType().Name
        };
    }
    private static string ValueTypeName(object value) => value switch
    {
        int => "int", double => "float", string => "string", BoolLike => "boolean", Nil => "nil",
        List => "list", _ => value.GetType().Name
    };
    private static bool SameType(MaxType a, MaxType b) => TypeName(a) == TypeName(b);
    private static void PlanListType(MaxType? type, object value, Dictionary<List, MaxType> plan, int nesting = 0)
    {
        EnsureGraphStack(nesting);
        if (type is OptionalType optional) { PlanListType(optional.InnerType, value, plan, nesting + 1); return; }
        if (type is not ListType { ElementType: not null } typed || value is not List list) return;
        if (list.ElementType != null && !SameType(list.ElementType, typed.ElementType))
            throw new MaxTypeException($"This shared list already requires {TypeName(list.ElementType)} elements and cannot also require {TypeName(typed.ElementType)} elements.");
        if (plan.TryGetValue(list, out var requested))
        {
            if (!SameType(requested, typed.ElementType))
                throw new MaxTypeException($"This shared list would require both {TypeName(requested)} and {TypeName(typed.ElementType)} elements.");
            return;
        }
        plan.Add(list, typed.ElementType);
        foreach (var item in list.Values) PlanListType(typed.ElementType, item, plan, nesting + 1);
    }
    private static void ApplyListTypePlan(Dictionary<List, MaxType> plan)
    {
        foreach (var item in plan) item.Key.ElementType = item.Value;
    }
    private static BoolLike Condition(object value) => value as BoolLike
        ?? throw new MaxTypeException("A condition must be true, false or possible.");
    private object Logical(LogicalExpression expression)
    {
        var left = Condition(Evaluate(expression.Left));
        if (expression.Operator == LogicalOperator.And && left is Bool { Value: false }) return new Bool(false);
        if (expression.Operator == LogicalOperator.Or && left is Bool { Value: true }) return new Bool(true);
        var right = Condition(Evaluate(expression.Right));
        if (expression.Operator == LogicalOperator.And && right is Bool { Value: false }) return new Bool(false);
        if (expression.Operator == LogicalOperator.Or && right is Bool { Value: true }) return new Bool(true);
        if (left is Possible || right is Possible) return new Possible();
        return new Bool(expression.Operator == LogicalOperator.And);
    }
    private static int Negate(int value)
    {
        try { return checked(-value); }
        catch (OverflowException) { throw new MaxArithmeticException("Integer overflow."); }
    }
    private static object Binary(string op, object left, object right)
    {
        if (op == "+" && left is string firstText && right is string secondText) return firstText + secondText;
        if (left is string || right is string)
            throw new MaxArithmeticException(op == "+"
                ? "'+' requires two numbers or two strings. Use text(value) to convert a value to string."
                : $"'{op}' requires two numbers.");
        if (left is not (int or double) || right is not (int or double))
            throw new MaxArithmeticException($"'{op}' requires numbers.");
        if (op == "/" && Convert.ToDouble(right, CultureInfo.InvariantCulture) == 0) throw new MaxArithmeticException("Cannot divide by zero.");
        if (left is int a && right is int b)
        {
            try
            {
                return op switch
                {
                    "+" => checked(a + b), "-" => checked(a - b), "*" => checked(a * b), "/" => a / b,
                    _ => throw new MaxArithmeticException($"Unknown operator '{op}'.")
                };
            }
            catch (OverflowException) { throw new MaxArithmeticException("Integer overflow."); }
        }
        double x = Convert.ToDouble(left, CultureInfo.InvariantCulture), y = Convert.ToDouble(right, CultureInfo.InvariantCulture);
        double result = op switch
        {
            "+" => x + y, "-" => x - y, "*" => x * y, "/" => x / y,
            _ => throw new MaxArithmeticException($"Unknown operator '{op}'.")
        };
        if (!double.IsFinite(result)) throw new MaxArithmeticException("Floating-point overflow.");
        return result;
    }
    private static BoolLike Compare(string op, object left, object right)
    {
        if (op is "==" or "!=")
        {
            bool equal;
            if (left is Nil || right is Nil) equal = left is Nil && right is Nil;
            else if (left is BoolLike && right is BoolLike)
            {
                if (left is Possible || right is Possible) return new Possible();
                equal = ((Bool)left).Value == ((Bool)right).Value;
            }
            else if (left is int or double && right is int or double)
                equal = Convert.ToDouble(left, CultureInfo.InvariantCulture) == Convert.ToDouble(right, CultureInfo.InvariantCulture);
            else if (left is string a && right is string b) equal = a == b;
            else if (left is List && right is List) equal = ReferenceEquals(left, right);
            else throw new MaxComparisonException($"Cannot compare {ValueTypeName(left)} and {ValueTypeName(right)} with '{op}'.");
            return new Bool(op == "==" ? equal : !equal);
        }
        int order;
        if (left is string leftText && right is string rightText)
            order = string.CompareOrdinal(leftText, rightText);
        else if (left is int or double && right is int or double)
            order = Convert.ToDouble(left, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
        else throw new MaxComparisonException($"'{op}' requires two numbers or two strings, got {ValueTypeName(left)} and {ValueTypeName(right)}.");
        return new Bool(op switch
        {
            "<" => order < 0, "<=" => order <= 0, ">" => order > 0, ">=" => order >= 0,
            _ => throw new MaxComparisonException($"Unknown comparator '{op}'.")
        });
    }
    private static int Length(object value) => value switch
    {
        List list => list.Values.Count, string text => TextLength(text),
        _ => throw new MaxTypeException("Only lists and strings have a length or can be indexed.")
    };
    private static int TextLength(string text)
    {
        int count = 0;
        foreach (var _ in text.EnumerateRunes()) count++;
        return count;
    }
    private static string TextAt(string text, int index)
    {
        int current = 0;
        foreach (var rune in text.EnumerateRunes())
            if (current++ == index) return rune.ToString();
        throw new MaxIndexingException($"Index {index} is outside the string.");
    }
    private static int ResolveIndex(object value, int count)
    {
        if (value is not int index) throw new MaxIndexingException("An index must be an integer.");
        if (index == -1) index = count - 1;
        if (index < 0 || index >= count) throw new MaxIndexingException($"Index {value} is outside a collection with {count} item(s).");
        return index;
    }
    private static void EnsureGraphStack(int nesting)
    {
        if (nesting > MaximumValueNesting)
            throw new MaxValueException($"Lists and types can be nested at most {MaximumValueNesting} levels. Reduce their nesting.");
        if (!RuntimeStackGuard.HasRoom())
            throw new MaxValueException("The list or type is nested too deeply for the available execution stack. Reduce its nesting.");
    }
    public static string Format(object value) => Format(value, 0);
    private static string Format(object value, int nesting)
    {
        EnsureGraphStack(nesting);
        return value switch
        {
            Bool b => b.Value ? "true" : "false",
            Possible => "possible",
            double n => n.ToString(CultureInfo.InvariantCulture),
            List list => $"[{string.Join(", ", list.Values.Select(item => Format(item, nesting + 1)))}]",
            _ => value.ToString() ?? "nil"
        };
    }
}

public class Possible : BoolLike { public override string ToString() => "possible"; }
public class Bool(bool value) : BoolLike
{
    public bool Value { get; } = value;
    public override string ToString() => Value ? "true" : "false";
}
public class BoolLike { }
public class Nil { public override string ToString() => "nil"; }
public class Variable(string name, object value, MaxType? type, object? resortValue)
{
    public string Name { get; } = name;
    public object Value { get; private set; } = value;
    public MaxType? Type { get; } = type;
    public object? ResortValue { get; private set; } = resortValue;
    public void SetValue(object value) => Value = value;
    public void SetResort(object resortValue)
    {
        if (resortValue is Nil) throw new MaxVariableException("A resort cannot be nil.");
        ResortValue ??= resortValue;
    }
}
public interface IIterable { }
public class List(MaxType? elementType) : IIterable
{
    public List<object> Values { get; } = [];
    public MaxType? ElementType { get; internal set; } = elementType;
    public override string ToString() => Interpreter.Format(this);
}

