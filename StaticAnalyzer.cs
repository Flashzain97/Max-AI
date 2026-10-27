namespace Max;

/// <summary>
/// Checks errors that are knowable without running user code. Untyped values,
/// mutable untyped lists and optional values outside direct nil guards stay
/// dynamic; runtime validation remains the final authority for those values.
/// Functions are checked in their lexical closure when called. Strict mode
/// also checks dormant bodies and requires explicit declaration annotations.
/// </summary>
public static class StaticAnalyzer
{
    public static void Validate(List<IStatement> statements, bool strict = false) =>
        new Analysis(strict).Run(statements);

    private static readonly HashSet<string> Builtins =
        ["text", "length", "input", "integer", "decimal", "append", "remove"];

    private sealed class Frame(Frame? parent = null)
    {
        public Frame? Parent { get; } = parent;
        public Dictionary<string, Binding> Variables { get; } = [];
        public Dictionary<string, Function> Functions { get; } = [];
        public Dictionary<Binding, Value> Facts { get; } = [];
        public Binding? Variable(string name)
        {
            for (Frame? scope = this; scope != null; scope = scope.Parent)
                if (scope.Variables.TryGetValue(name, out var variable)) return variable;
            return null;
        }
        public Function? Function(string name)
        {
            for (Frame? scope = this; scope != null; scope = scope.Parent)
                if (scope.Functions.TryGetValue(name, out var function)) return function;
            return null;
        }
        public Value? Fact(Binding binding)
        {
            for (Frame? scope = this; scope != null; scope = scope.Parent)
                if (scope.Facts.TryGetValue(binding, out var value)) return value;
            return null;
        }
        public void ClearFacts()
        {
            for (Frame? scope = this; scope != null; scope = scope.Parent) scope.Facts.Clear();
        }
    }

    // Binding identity matters: two optional variables with the same declared
    // type do not share their current value or a fact established by a guard.
    private sealed class Binding(MaxType? type, Value? resort)
    {
        public MaxType? Type { get; } = type;
        public Value? Resort { get; } = resort;
    }
    private sealed record Function(FunctionStatement Declaration, Frame Closure);
    // Literal lists retain their individual elements so nested errors can name
    // the exact item. An annotated float may hold either an int or a float.
    private sealed record Value(MaxType? Type = null, bool Nil = false,
        IReadOnlyList<Value>? Elements = null, bool Exact = false, bool MayBePossible = true)
    {
        public static readonly Value Unknown = new();
        public static readonly Value Nothing = new(Nil: true);
        public static Value Declared(MaxType? type) => Unwrap(type) is NothingType ? Nothing : new(type);
    }

    private enum Truth { Unknown, False, True, Possible }

    private sealed class Analysis(bool strict)
    {
        private readonly HashSet<FunctionStatement> active = [];
        private readonly HashSet<FunctionStatement> queued = [];
        private readonly System.Collections.Generic.List<Function> dormant = [];
        private IAstNode? enclosing;

        public void Run(List<IStatement> statements)
        {
            Block(statements, new Frame(), null);
            // Closures are live scopes. Delay dormant checks until surrounding
            // declarations are available, including ones after the function.
            for (int i = 0; i < dormant.Count; i++) CheckFunction(dormant[i]);
        }

        // True means that execution can reach the next statement. Unreachable
        // statements are still checked in strict mode, without affecting flow.
        public bool Block(List<IStatement> statements, Frame scope, FunctionStatement? function)
        {
            SafeStack(statements.FirstOrDefault() ?? (IAstNode?)function ?? enclosing);
            bool continues = true;
            foreach (var statement in statements)
            {
                if (!continues && !strict) break;
                var saved = enclosing;
                enclosing = statement;
                try
                {
                    bool next = Statement(statement, scope, function);
                    if (continues) continues = next;
                }
                finally { enclosing = saved; }
            }
            return continues;
        }

        private bool Statement(IStatement statement, Frame scope, FunctionStatement? function)
        {
            switch (statement)
            {
                case DeclarationStatement d:
                    CheckVariableName(d.Name, scope, d);
                    if (strict && d.Type == null)
                        Fail(new MaxTypeException($"Variable '{d.Name}' needs an explicit type in strict mode. Write 'declare {d.Name} as int = ...' (or another type)."), d);
                    ExplicitListElements(d.Type, $"Variable '{d.Name}'", d);
                    var value = Expression(d.Value, scope);
                    var resort = d.Resortable == null ? null : Expression(d.Resortable, scope);
                    if (resort?.Nil == true) Fail(new MaxNilException("A resort cannot be nil."), d.Resortable!);
                    if (resort != null) Require(d.Type, resort, $"Resort for '{d.Name}'", d.Resortable!);
                    Require(d.Type, ResolveResort(value, resort), $"Variable '{d.Name}'", d.Value);
                    scope.Variables.Add(d.Name, new(d.Type, resort));
                    break;
                case AssignmentStatement a:
                    var target = Expression(a.Target, scope);
                    var assigned = Expression(a.Value, scope);
                    if (a.AssignmentType != "=") assigned = Binary(a.AssignmentType[..1], target, assigned, a);
                    if (a.Target is VariableReference reference)
                    {
                        var binding = scope.Variable(reference.Name)!;
                        Require(binding.Type, ResolveResort(assigned, binding.Resort),
                            $"Variable '{reference.Name}'", a.Value);
                    }
                    else if (a.Target is IndexingExpression index)
                    {
                        var collection = Expression(index.Name, scope);
                        var type = Unwrap(collection.Type);
                        if (collection.Nil || (type != null && type is not ListType))
                            Fail(new MaxTypeException("Only list elements can be assigned to."), index.Name);
                        if (type is ListType list) Require(list.ElementType, assigned, "List element", a.Value);
                    }
                    else Fail(new MaxSyntaxException("Expected a variable or list element as assignment target."), a.Target);
                    // Even a conditional write can invalidate an enclosing
                    // guard. Keep subsequent checking conservative after writes.
                    scope.ClearFacts();
                    break;
                case SayStatement s: Expression(s.Value, scope); break;
                case ExpressionStatement e: Expression(e.Expression, scope); break;
                case FunctionStatement f:
                    CheckFunctionName(f.Name, scope, f);
                    var names = new HashSet<string>();
                    foreach (var parameter in f.Parameters)
                    {
                        if (!names.Add(parameter.Name)) Fail(new MaxVariableException($"Duplicate parameter '{parameter.Name}'."), f);
                        if (Builtins.Contains(parameter.Name)) Fail(new MaxVariableException($"'{parameter.Name}' is a built-in function name and cannot be a parameter."), f);
                        if (parameter.Name == f.Name || scope.Function(parameter.Name) != null)
                            Fail(new MaxVariableException($"'{parameter.Name}' is already a visible function name. Choose a different parameter name."), f);
                        if (strict && parameter.Type == null)
                            Fail(new MaxTypeException($"Parameter '{parameter.Name}' in '{f.Name}' needs an explicit type in strict mode."), f);
                        ExplicitListElements(parameter.Type, $"Parameter '{parameter.Name}'", f);
                    }
                    if (strict && f.ReturnType == null)
                        Fail(new MaxTypeException($"Function '{f.Name}' needs 'returns <type>' in strict mode. Use 'returns nothing' for a procedure."), f);
                    ExplicitListElements(f.ReturnType, $"Result of '{f.Name}'", f);
                    var declaredFunction = new Function(f, scope);
                    scope.Functions.Add(f.Name, declaredFunction);
                    if (strict && queued.Add(f)) dormant.Add(declaredFunction);
                    break;
                case ReturnStatement r:
                    if (function == null) Fail(new MaxSyntaxException("'return' can only appear inside a function."), r);
                    Require(function?.ReturnType, Expression(r.Value, scope), $"Return value of '{function?.Name}'", r.Value);
                    return false;
                case IfStatement conditional:
                    return Conditional(conditional, scope, function);
                case WhileStatement loop:
                    var condition = Expression(loop.Check_Statement, scope);
                    Boolean(condition, loop.Check_Statement);
                    var truth = TruthInScope(loop.Check_Statement, scope);
                    if (strict || truth != Truth.False)
                        Block(loop.Consequences, new Frame(scope), function);
                    // A literal true loop either returns or never reaches the
                    // function's end. A guarded variable can change inside the
                    // body, so entry facts cannot establish an infinite loop.
                    return ConstantTruth(loop.Check_Statement) != Truth.True;
                case ForStatement loop:
                    Require(new IntegerType(), Expression(loop.Declaration.Value, scope), "For loop start", loop.Declaration.Value);
                    Require(new IntegerType(), Expression(loop.Expression, scope), "For loop end", loop.Expression);
                    Require(new IntegerType(), Expression(loop.Step, scope), "For loop step", loop.Step);
                    var forScope = new Frame(scope);
                    CheckVariableName(loop.Declaration.Name, forScope, loop);
                    forScope.Variables.Add(loop.Declaration.Name, new(new IntegerType(), null));
                    var forContinues = Block(loop.Consequences, new Frame(forScope), function);
                    bool executes = ConstantInt(loop.Declaration.Value) is int first && ConstantInt(loop.Expression) is int last &&
                        ConstantInt(loop.Step) is int step && step != 0 && (step > 0 ? first <= last : first >= last);
                    return !executes || forContinues;
                case ForeachStatement loop:
                    var iterable = Expression(loop.IterableCollection, scope);
                    if (iterable.Nil || (Unwrap(iterable.Type) is MaxType known && known is not ListType))
                        Fail(new MaxTypeException("'foreach' requires a list."), loop.IterableCollection);
                    var foreachScope = new Frame(scope);
                    CheckVariableName(loop.Declaration.Name, foreachScope, loop);
                    var element = (Unwrap(iterable.Type) as ListType)?.ElementType;
                    foreachScope.Variables.Add(loop.Declaration.Name, new(element, null));
                    bool foreachContinues = true;
                    if (strict || iterable.Elements?.Count != 0)
                        foreachContinues = Block(loop.Consequences, new Frame(foreachScope), function);
                    return iterable.Elements is not { Count: > 0 } || foreachContinues;
                default: Fail(new MaxSyntaxException($"Cannot analyze {statement.GetType().Name}."), statement); break;
            }
            return true;
        }

        private bool Conditional(IfStatement conditional, Frame scope, FunctionStatement? function)
        {
            bool canReachNextBranch = true, continues = false;
            var remainingScope = new Frame(scope);
            foreach (var branch in new[] { conditional.First_Consequences }.Concat(conditional.Other_Consequences))
            {
                if (!canReachNextBranch && !strict) break;
                var value = Expression(branch.Check_Statement, remainingScope);
                Boolean(value, branch.Check_Statement);
                var truth = TruthInScope(branch.Check_Statement, remainingScope);
                bool bodyContinues = true;
                if (strict || truth is not (Truth.False or Truth.Possible))
                {
                    var bodyScope = new Frame(remainingScope);
                    NarrowNilGuard(branch.Check_Statement, bodyScope, whenTrue: true);
                    bodyContinues = Block(branch.Consequences, bodyScope, function);
                }
                if (canReachNextBranch)
                {
                    if (truth is Truth.True or Truth.Unknown) continues |= bodyContinues;
                    if (truth is Truth.Possible || (truth is Truth.Unknown && value.MayBePossible))
                        continues |= Block(conditional.Maybe, new Frame(remainingScope), function);
                    if (truth is Truth.True or Truth.Possible) canReachNextBranch = false;
                }
                NarrowNilGuard(branch.Check_Statement, remainingScope, whenTrue: false);
            }
            if (canReachNextBranch) continues |= Block(conditional.Else, new Frame(remainingScope), function);
            else if (strict) Block(conditional.Else, new Frame(remainingScope), function);
            if (strict) Block(conditional.Maybe, new Frame(scope), function);
            return continues;
        }

        private void NarrowNilGuard(IExpression expression, Frame scope, bool whenTrue)
        {
            SafeStack(expression);
            if (expression is UnaryExpression { Operator: UnaryOperator.Not } negation)
            {
                NarrowNilGuard(negation.Operand, scope, !whenTrue);
                return;
            }
            if (expression is not ComparisonExpression { Operator: "==" or "!=" } comparison) return;
            var reference = comparison.Left is VariableReference left && comparison.Right is NilLiteral ? left
                : comparison.Right is VariableReference right && comparison.Left is NilLiteral ? right : null;
            var binding = reference == null ? null : scope.Variable(reference.Name);
            if (binding?.Type is not OptionalType optional) return;
            bool isNil = whenTrue == (comparison.Operator == "==");
            scope.Facts[binding] = isNil ? Value.Nothing : Value.Declared(optional.InnerType);
        }

        private Truth TruthInScope(IExpression expression, Frame scope)
        {
            SafeStack(expression);
            if (expression is ComparisonExpression { Operator: "==" or "!=" } comparison)
            {
                var reference = comparison.Left is VariableReference left && comparison.Right is NilLiteral ? left
                    : comparison.Right is VariableReference right && comparison.Left is NilLiteral ? right : null;
                var binding = reference == null ? null : scope.Variable(reference.Name);
                if (binding != null)
                {
                    var value = scope.Fact(binding) ?? Value.Declared(binding.Type);
                    if (value.Nil) return BooleanTruth(comparison.Operator == "==");
                    if (value.Type != null && value.Type is not OptionalType)
                        return BooleanTruth(comparison.Operator == "!=");
                }
            }
            if (expression is UnaryExpression { Operator: UnaryOperator.Not } negation)
                return TruthInScope(negation.Operand, scope) switch
                { Truth.True => Truth.False, Truth.False => Truth.True, Truth.Possible => Truth.Possible, _ => Truth.Unknown };
            if (expression is LogicalExpression logical)
            {
                var left = TruthInScope(logical.Left, scope);
                if (logical.Operator == LogicalOperator.And && left == Truth.False) return Truth.False;
                if (logical.Operator == LogicalOperator.Or && left == Truth.True) return Truth.True;
                var right = TruthInScope(logical.Right, scope);
                if (logical.Operator == LogicalOperator.And && right == Truth.False) return Truth.False;
                if (logical.Operator == LogicalOperator.Or && right == Truth.True) return Truth.True;
                if (left == Truth.Unknown || right == Truth.Unknown) return Truth.Unknown;
                if (left == Truth.Possible || right == Truth.Possible) return Truth.Possible;
                return logical.Operator == LogicalOperator.And ? Truth.True : Truth.False;
            }
            return ConstantTruth(expression);
        }

        private void CheckFunction(Function function)
        {
            var declaration = function.Declaration;
            SafeStack(declaration);
            if (!active.Add(declaration)) return; // Recursion is valid; its signature is already checked.
            var saved = enclosing;
            enclosing = declaration;
            try
            {
                var scope = new Frame(function.Closure);
                foreach (var parameter in declaration.Parameters)
                {
                    CheckVariableName(parameter.Name, scope, declaration);
                    scope.Variables.Add(parameter.Name, new(parameter.Type, null));
                }
                bool reachesEnd = Block(declaration.Body, scope, declaration);
                if (strict && reachesEnd && declaration.ReturnType != null &&
                    declaration.ReturnType is not (OptionalType or NothingType))
                    Fail(new MaxTypeException($"Function '{declaration.Name}' can finish without returning {TypeName(declaration.ReturnType)}. Return a value on every path, or use an optional result type."), declaration);
            }
            finally { enclosing = saved; active.Remove(declaration); }
        }

        private Value Expression(IExpression expression, Frame scope)
        {
            SafeStack(expression);
            switch (expression)
            {
                case IntegerLiteral: return new(new IntegerType(), Exact: true, MayBePossible: false);
                case FloatLiteral: return new(new FloatType(), Exact: true, MayBePossible: false);
                case StringLiteral: return new(new StringType(), Exact: true, MayBePossible: false);
                case BooleanLiteral: return new(new BooleanType(), Exact: true, MayBePossible: false);
                case PossibleLiteral: return new(new PossibleType(), Exact: true);
                case NilLiteral: return Value.Nothing;
                case VariableReference v:
                    var variable = scope.Variable(v.Name);
                    if (variable == null) Fail(new MaxVariableException($"Variable '{v.Name}' does not exist. Declare it before using it."), v);
                    return variable == null ? Value.Unknown : scope.Fact(variable) ?? Value.Declared(variable.Type);
                case ListLiteral l:
                    return new(new ListType(), Elements: l.Values.Select(item => Expression(item, scope)).ToArray(), Exact: true, MayBePossible: false);
                case BinaryExpression b:
                    return Binary(b.Operator, Expression(b.Left, scope), Expression(b.Right, scope), b);
                case UnaryExpression u:
                    var operand = Expression(u.Operand, scope);
                    if (u.Operator == UnaryOperator.Not)
                    {
                        Boolean(operand, u.Operand);
                        return new(new BooleanType(), MayBePossible: operand.MayBePossible);
                    }
                    Numeric(operand, u.Operand);
                    return new(Unwrap(operand.Type), Exact: operand.Exact, MayBePossible: false);
                case ComparisonExpression c:
                    var left = Expression(c.Left, scope);
                    var right = Expression(c.Right, scope);
                    Comparison(c.Operator, left, right, c);
                    bool uncertain = c.Operator is "==" or "!=" && !left.Nil && !right.Nil &&
                        ((!IsDefinitelyNonBoolean(left) && left.MayBePossible) ||
                         (!IsDefinitelyNonBoolean(right) && right.MayBePossible));
                    return new(new BooleanType(), MayBePossible: uncertain);
                case LogicalExpression l:
                    var first = Expression(l.Left, scope);
                    Boolean(first, l.Left);
                    var truth = TruthInScope(l.Left, scope);
                    bool skips = (l.Operator == LogicalOperator.And && truth == Truth.False) ||
                        (l.Operator == LogicalOperator.Or && truth == Truth.True);
                    var second = strict || !skips ? Expression(l.Right, scope) : new Value(new BooleanType(), MayBePossible: false);
                    Boolean(second, l.Right);
                    return new(new BooleanType(), MayBePossible: !skips && (first.MayBePossible || second.MayBePossible));
                case IndexingExpression i:
                    var collection = Expression(i.Name, scope);
                    var index = Expression(i.IndexValue, scope);
                    Require(new IntegerType(), index, "Index", i.IndexValue);
                    var collectionType = Unwrap(collection.Type);
                    if (collection.Nil || (collectionType != null && collectionType is not (ListType or StringType)))
                        Fail(new MaxTypeException("Only lists and strings can be indexed."), i.Name);
                    if (collectionType is StringType) return new(new StringType(), MayBePossible: false);
                    // Mutable collections can change, so literal element values
                    // are never treated as stable facts after indexing.
                    return Value.Declared((collectionType as ListType)?.ElementType);
                case CallExpression call:
                    return Call(call, scope);
                default: Fail(new MaxSyntaxException($"Cannot analyze {expression.GetType().Name}."), expression); return Value.Unknown;
            }
        }

        private Value Call(CallExpression call, Frame scope)
        {
            if (Builtins.Contains(call.Name))
            {
                int expected = call.Name is "append" or "remove" ? 2 : 1;
                Arity(call, expected);
                var args = call.Arguments.Select(arg => Expression(arg, scope)).ToArray();
                switch (call.Name)
                {
                    case "text": return new(new StringType(), MayBePossible: false);
                    case "length":
                        Collection(args[0], allowString: true, call.Arguments[0]);
                        return new(new IntegerType(), MayBePossible: false);
                    case "input":
                        Require(new StringType(), args[0], "Input prompt", call.Arguments[0]);
                        return new(new StringType(), MayBePossible: false);
                    case "integer":
                    case "decimal":
                        var inputType = Unwrap(args[0].Type);
                        if (args[0].Nil || (inputType != null && inputType is not (StringType or NumberType)))
                            Fail(new MaxTypeException($"{call.Name} requires a string or number."), call.Arguments[0]);
                        return new(call.Name == "integer" ? new IntegerType() : new FloatType(), Exact: true, MayBePossible: false);
                    case "append":
                        Collection(args[0], allowString: false, call.Arguments[0]);
                        Require((Unwrap(args[0].Type) as ListType)?.ElementType, args[1], "Appended list element", call.Arguments[1]);
                        return Value.Nothing;
                    case "remove":
                        Collection(args[0], allowString: false, call.Arguments[0]);
                        Require(new IntegerType(), args[1], "Remove index", call.Arguments[1]);
                        return Value.Declared((Unwrap(args[0].Type) as ListType)?.ElementType);
                }
            }
            var function = scope.Function(call.Name);
            if (function == null) Fail(new MaxVariableException($"Function '{call.Name}' does not exist. Define it before calling it."), call);
            var declaration = function!.Declaration;
            Arity(call, declaration.Parameters.Count);
            // Arguments are checked in caller scope, before body/parameter scope.
            var values = call.Arguments.Select(arg => Expression(arg, scope)).ToArray();
            for (int i = 0; i < values.Length; i++)
                Require(declaration.Parameters[i].Type, values[i], $"Parameter '{declaration.Parameters[i].Name}' of '{call.Name}'", call.Arguments[i]);
            // A function can update captured variables. The checker does not
            // execute it to predict which guards remain true afterward.
            scope.ClearFacts();
            CheckFunction(function);
            return Value.Declared(declaration.ReturnType);
        }

        private void Arity(CallExpression call, int expected)
        {
            if (call.Arguments.Count != expected)
                Fail(new MaxValueException($"Function '{call.Name}' expects {expected} argument(s), received {call.Arguments.Count}."), call);
        }

        private void CheckVariableName(string name, Frame scope, IAstNode node)
        {
            if (Builtins.Contains(name)) Fail(new MaxVariableException($"'{name}' is a built-in function name and cannot be declared."), node);
            if (scope.Variables.ContainsKey(name)) Fail(new MaxVariableException($"Variable '{name}' is already declared in this scope."), node);
            if (scope.Function(name) != null) Fail(new MaxVariableException($"'{name}' is already a visible function name. Choose a different variable name."), node);
        }

        private void ExplicitListElements(MaxType? type, string context, IAstNode node)
        {
            SafeStack(node);
            if (!strict || type == null) return;
            if (type is OptionalType optional) ExplicitListElements(optional.InnerType, context, node);
            if (type is ListType list)
            {
                if (list.ElementType == null)
                    Fail(new MaxTypeException($"{context} needs an explicit list element type in strict mode. Write 'list<int>' (or another element type)."), node);
                ExplicitListElements(list.ElementType, context, node);
            }
        }

        private void CheckFunctionName(string name, Frame scope, IAstNode node)
        {
            if (Builtins.Contains(name)) Fail(new MaxVariableException($"'{name}' is a built-in function name and cannot be defined."), node);
            if (scope.Functions.ContainsKey(name)) Fail(new MaxVariableException($"Function '{name}' is already defined in this scope."), node);
            if (scope.Variable(name) != null) Fail(new MaxVariableException($"'{name}' is already a visible variable name. Choose a different function name."), node);
        }

        private void Require(MaxType? expected, Value actual, string context, IAstNode node)
        {
            SafeStack(node);
            if (expected == null) return;
            if (expected is OptionalType optional)
            {
                // A differently typed optional expression can still be nil
                // after a guard; nil is valid for any optional expected type.
                if (actual.Type is OptionalType) return;
                if (!actual.Nil) Require(optional.InnerType, actual with { Type = Unwrap(actual.Type) }, context, node);
                return;
            }
            if (actual.Nil)
            {
                if (expected is not NothingType)
                    Fail(new MaxTypeException($"{context} requires {TypeName(expected)}, but received nil. Add '?' to that type to allow nil."), node);
                return;
            }
            var type = Unwrap(actual.Type);
            if (type == null) return;
            if (expected is NothingType && actual.Type is OptionalType) return;
            if (expected is ListType expectedList && type is ListType actualList)
            {
                if (expectedList.ElementType == null) return;
                // A typed mutable list already carries an invariant constraint.
                // Widening int elements to float (or adding '?') would retag the
                // same object and permit writes its existing aliases reject.
                // Fresh literals have no constraint yet and remain checked item
                // by item, including their nested lists.
                if (actualList.ElementType != null &&
                    TypeName(expectedList.ElementType) != TypeName(actualList.ElementType))
                    Fail(new MaxTypeException($"{context}: this shared list already requires {TypeName(actualList.ElementType)} elements and cannot also require {TypeName(expectedList.ElementType)} elements."), node);
                if (actual.Elements != null)
                    for (int i = 0; i < actual.Elements.Count; i++)
                        Require(expectedList.ElementType, actual.Elements[i], $"{context}[{i}]", node);
                else if (actualList.ElementType != null)
                    Require(expectedList.ElementType, Value.Declared(actualList.ElementType), $"{context} element", node);
                return;
            }
            bool accepts = expected switch
            {
                IntegerType => type is IntegerType || (type is FloatType && !actual.Exact),
                FloatType => type is NumberType,
                StringType => type is StringType,
                PossibleType => type is PossibleType || (type is BooleanType && !actual.Exact),
                BooleanType => type is BooleanType,
                NothingType => false,
                _ => expected.GetType() == type.GetType()
            };
            if (!accepts)
                Fail(new MaxTypeException($"{context} requires {TypeName(expected)}, but received {TypeName(type)}."), node);
        }

        private static Value ResolveResort(Value actual, Value? resort)
        {
            if (resort == null) return actual;
            if (actual.Nil) return resort;
            // An optional expression may be nil even when its non-nil type
            // differs from the declaration. That nil follows the valid resort
            // path, so only runtime can decide an unguarded expression's value.
            return actual.Type is OptionalType ? Value.Unknown : actual;
        }

        private void Boolean(Value value, IAstNode node)
        {
            if (value.Nil || (Unwrap(value.Type) is MaxType type && type is not BooleanType))
                Fail(new MaxTypeException("A condition must be true, false or possible."), node);
        }

        private void Numeric(Value value, IAstNode node)
        {
            if (value.Nil || (Unwrap(value.Type) is MaxType type && type is not NumberType))
                Fail(new MaxArithmeticException("Arithmetic requires numbers. Use text(value) for explicit string conversion."), node);
        }

        private Value Binary(string operation, Value left, Value right, IAstNode node)
        {
            var a = Unwrap(left.Type); var b = Unwrap(right.Type);
            if (operation == "+" && a is StringType && b is StringType)
                return new(new StringType(), MayBePossible: false);
            // Unknown operands can still be strings in a valid concatenation.
            if (operation == "+" && !left.Nil && !right.Nil &&
                ((a is StringType && b == null) || (b is StringType && a == null))) return Value.Unknown;
            Numeric(left, node); Numeric(right, node);
            if (a == null || b == null) return Value.Unknown;
            return new(a is IntegerType && b is IntegerType ? new IntegerType() : new FloatType(),
                Exact: left.Exact && right.Exact, MayBePossible: false);
        }

        private void Comparison(string operation, Value left, Value right, IAstNode node)
        {
            var a = Unwrap(left.Type); var b = Unwrap(right.Type);
            if (operation is "==" or "!=")
            {
                if (left.Nil || right.Nil || a == null || b == null || left.Type is OptionalType || right.Type is OptionalType) return;
                if ((a is NumberType && b is NumberType) || (a is BooleanType && b is BooleanType) ||
                    (a is StringType && b is StringType) || (a is ListType && b is ListType)) return;
                Fail(new MaxComparisonException($"Cannot compare {TypeName(a)} and {TypeName(b)} with '{operation}'."), node);
            }
            else if (left.Nil || right.Nil || (a != null && b != null &&
                !((a is NumberType && b is NumberType) || (a is StringType && b is StringType))))
                Fail(new MaxComparisonException($"'{operation}' requires two numbers or two strings."), node);
        }

        private void Collection(Value value, bool allowString, IAstNode node)
        {
            if (value.Nil || (Unwrap(value.Type) is MaxType type && type is not ListType && !(allowString && type is StringType)))
                Fail(new MaxTypeException(allowString ? "Only lists and strings have a length." : "This function requires a list."), node);
        }

        private void Fail(Exception error, IAstNode node)
        {
            var location = SourceMap.Get(node) ?? (enclosing == null ? null : SourceMap.Get(enclosing));
            if (location != null) error.Data["MaxLocation"] = location;
            throw error;
        }

        private void SafeStack(IAstNode? node)
        {
            if (RuntimeStackGuard.HasRoom()) return;
            var error = new MaxSyntaxException("Program is too deeply nested to check safely. Split complex expressions or long function-call chains into smaller parts.");
            if (node != null) Fail(error, node);
            throw error;
        }
    }

    private static MaxType? Unwrap(MaxType? type)
    {
        while (type is OptionalType optional) type = optional.InnerType;
        return type;
    }
    private static bool IsDefinitelyNonBoolean(Value value) => value.Nil ||
        (Unwrap(value.Type) is MaxType type && type is not BooleanType);
    private static int? ConstantInt(IExpression expression) => expression switch
    {
        IntegerLiteral n => n.Value,
        UnaryExpression { Operator: UnaryOperator.Negate, Operand: IntegerLiteral n } when n.Value != int.MinValue => -n.Value,
        _ => null
    };
    private static Truth ConstantTruth(IExpression expression)
    {
        if (!RuntimeStackGuard.HasRoom()) return Truth.Unknown;
        switch (expression)
        {
            case BooleanLiteral literal: return literal.Value ? Truth.True : Truth.False;
            case PossibleLiteral: return Truth.Possible;
            case UnaryExpression { Operator: UnaryOperator.Not } u:
                return ConstantTruth(u.Operand) switch
                { Truth.True => Truth.False, Truth.False => Truth.True, Truth.Possible => Truth.Possible, _ => Truth.Unknown };
            case LogicalExpression l:
                var left = ConstantTruth(l.Left);
                if (l.Operator == LogicalOperator.And && left == Truth.False) return Truth.False;
                if (l.Operator == LogicalOperator.Or && left == Truth.True) return Truth.True;
                var right = ConstantTruth(l.Right);
                if (l.Operator == LogicalOperator.And && right == Truth.False) return Truth.False;
                if (l.Operator == LogicalOperator.Or && right == Truth.True) return Truth.True;
                if (left == Truth.Unknown || right == Truth.Unknown) return Truth.Unknown;
                if (left == Truth.Possible || right == Truth.Possible) return Truth.Possible;
                return l.Operator == LogicalOperator.And ? Truth.True : Truth.False;
            case ComparisonExpression c:
                if (!ConstantValue(c.Left, out var a) || !ConstantValue(c.Right, out var b)) return Truth.Unknown;
                if (c.Operator is "==" or "!=")
                {
                    if (a == NilConstant || b == NilConstant)
                        return BooleanTruth(c.Operator == "==" ? a == b : a != b);
                    if (a == PossibleConstant || b == PossibleConstant)
                        return (a is bool || a == PossibleConstant) && (b is bool || b == PossibleConstant) ? Truth.Possible : Truth.Unknown;
                }
                int order;
                if (a is int or double && b is int or double)
                    order = Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
                else if (a is string sa && b is string sb) order = string.CompareOrdinal(sa, sb);
                else if (a is bool ba && b is bool bb && c.Operator is "==" or "!=") order = ba.CompareTo(bb);
                else return Truth.Unknown;
                return c.Operator switch
                {
                    "==" => BooleanTruth(order == 0), "!=" => BooleanTruth(order != 0),
                    "<" => BooleanTruth(order < 0), "<=" => BooleanTruth(order <= 0),
                    ">" => BooleanTruth(order > 0), ">=" => BooleanTruth(order >= 0), _ => Truth.Unknown
                };
            default: return Truth.Unknown;
        }
    }

    private static readonly object NilConstant = new();
    private static readonly object PossibleConstant = new();
    private static Truth BooleanTruth(bool value) => value ? Truth.True : Truth.False;
    private static bool ConstantValue(IExpression expression, out object value)
    {
        value = NilConstant;
        if (!RuntimeStackGuard.HasRoom()) return false;
        switch (expression)
        {
            case IntegerLiteral n: value = n.Value; return true;
            case FloatLiteral n: value = n.Value; return true;
            case StringLiteral s: value = s.Value; return true;
            case BooleanLiteral b: value = b.Value; return true;
            case NilLiteral: return true;
            case PossibleLiteral: value = PossibleConstant; return true;
            case UnaryExpression { Operator: UnaryOperator.Negate } u:
                if (!ConstantValue(u.Operand, out var operand)) return false;
                if (operand is int i && i != int.MinValue) { value = -i; return true; }
                if (operand is double d) { value = -d; return true; }
                return false;
            case ComparisonExpression or LogicalExpression or UnaryExpression { Operator: UnaryOperator.Not }:
                var truth = ConstantTruth(expression);
                if (truth == Truth.Unknown) return false;
                value = truth == Truth.Possible ? PossibleConstant : truth == Truth.True;
                return true;
            default: return false;
        }
    }
    private static string TypeName(MaxType type) => type switch
    {
        OptionalType o => $"{TypeName(o.InnerType)}?",
        IntegerType => "int", FloatType => "float", StringType => "string", PossibleType => "possible",
        BooleanType => "boolean", NothingType => "nothing",
        ListType l => l.ElementType == null ? "list" : $"list<{TypeName(l.ElementType)}>",
        _ => type.GetType().Name
    };
}
