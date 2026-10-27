namespace Max;

public class Compiler(NativeFunctionRegistry nativeRegistry)
{
    /// <summary>
    /// The bytecode that contains all the instructions
    /// </summary>
    private readonly Bytecode bytecode = new();

    /// <summary>
    /// A value to represent a free temporary slot in the VM
    /// </summary>
    private int nextTemporarySlot;

    /// <summary>
    /// The depth of a task within the scope
    /// </summary>
    private int taskDepth;

    /// <summary>
    /// Names declared in the global compiler scope
    /// </summary>
    private readonly Dictionary<string, MaxType?> globalVariableNames = [];

    /// <summary>
    /// Compiles all the nodes in a given list
    /// </summary>
    /// <param name="ast">The list to compile</param>
    /// <returns></returns>
    public Bytecode Compile(List<IStatement> ast)
    {
        foreach (IStatement node in ast)
        {
            CompileStatement(node);
        }

        return bytecode;
    }

    /// <summary>
    /// Compiles a statement. This is different from an expression
    /// </summary>
    /// <param name="statement">The statement to compile</param>
    /// <exception cref="MaxSyntaxException"></exception>
    private void CompileStatement(IStatement statement)
    {
        switch (statement)
        {
            case SayStatement say:
                CompileSay(say);
                break;

            case DeclarationStatement declaration:
                CompileDeclaration(declaration);
                break;

            case AssignmentStatement assignment:
                CompileAssignment(assignment);
                break;

            case IfStatement ifStatement:
                CompileIf(ifStatement);
                break;

            case WhileStatement whileStatement:
                CompileWhile(whileStatement);
                break;

            case ForStatement forStatement:
                CompileFor(forStatement);
                break;

            case ForeachStatement foreachStatement:
                CompileForeach(foreachStatement);
                break;

            case TaskDefinition task:
                CompileTaskDefinition(task);
                break;

            case ExpressionStatement expression:
                CompileExpression(expression.Expression);
                bytecode.Instructions.Add(new Instruction(OpCode.Pop));
                break;

            case ReturnStatement returnStatement:
                // So that you can only return within a task
                if (taskDepth <= 0)
                {
                    throw new MaxSyntaxException("Return statements can only be used inside a task.");
                }

                // Simply return nothing
                if (returnStatement.Value is null)
                {
                    bytecode.Instructions.Add(new Instruction(OpCode.Return));
                }
                else
                // Or return the value that is from the return statement
                {
                    CompileExpression(returnStatement.Value);
                    bytecode.Instructions.Add(new Instruction(OpCode.ReturnValue));
                }
                break;

            case YieldStatement yieldStatement:
                if (yieldStatement.Value != null)
                {
                    // Compile the expression if possible
                    CompileExpression(yieldStatement.Value); 
                }
                else
                {
                    // If yield has no value, push Nil
                    CompileConstant(new Nil());
                }

                // Yield will be able to get it's value from popping the stack
                bytecode.Instructions.Add(new Instruction(OpCode.Yield));
                break;

            default:
                throw new MaxSyntaxException(
                    $"Compiler does not yet support statement '{statement.GetType().Name}'."
                );
        }
    }

    /// <summary>
    /// Compiles a expression. This is different from an statement, and is compiled from statements, not individually.
    /// </summary>
    /// <param name="expression">The expression to compile</param>
    /// <exception cref="MaxSyntaxException"></exception>
    private void CompileExpression(IExpression expression)
    {
        switch (expression)
        {
            case IntegerLiteral integer:
                CompileConstant(integer.Value);
                break;

            case FloatLiteral @float:
                CompileConstant(@float.Value);
                break;

            case StringLiteral @string:
                CompileConstant(@string.Value);
                break;

            case BooleanLiteral boolean:
                CompileConstant(boolean.Value);
                break;

            case PossibleLiteral:
                // We do this to give the actual possible value, not the class
                CompileConstant(new Possible().Value);
                break;

            case NilLiteral:
                CompileConstant(new Nil().Value);
                break;

            case ListLiteral list:
                CompileList(list);
                break;

            case DictionaryLiteral dictionary:
                CompileDictionary(dictionary);
                break;

            case VariableReference variable:
                CompileVariable(variable);
                break;

            case BinaryExpression binary:
                CompileBinary(binary);
                break;

            case ComparisonExpression comparison:
                CompileComparison(comparison);
                break;

            case UnaryExpression unary:
                CompileUnary(unary);
                break;

            case IndexingExpression indexing:
                CompileIndexing(indexing);
                break;

            case MemberAccessExpression memberAccess:
                // Compile the target (aka the 'self' to be used for a native function)
                CompileExpression(memberAccess.Target);

                // Now give CallNative instructions with Target set to null
                // The argument count is 1 because the 'self' object counts as the single argument to the property getter
                bytecode.Instructions.Add(new Instruction(
                    OpCode.CallNative,
                    new TaskCallOperand(null, memberAccess.Member, 1, true)
                ));
                break;

            case TaskCall taskCall:
                CompileTaskCall(taskCall);
                break;

            default:
                throw new MaxSyntaxException(
                    $"Compiler does not yet support expression '{expression.GetType().Name}'."
                );
        }
    }

    #region Compiling Expressions

    /// <summary>
    /// Compile any possible value into a constant
    /// </summary>
    /// <param name="value">The value to compile</param>
    private void CompileConstant(object value)
    {
        // Values means we want the value of the literal that comes from the parser, or a runtime class!
        bytecode.Instructions.Add(new Instruction(OpCode.Constant, value));
    }

    private void CompileList(ListLiteral expression)
    {
        foreach (IExpression item in expression.Values)
        {
            CompileExpression(item);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.MakeList, expression.Values.Count));
    }

    private void CompileDictionary(DictionaryLiteral expression)
    {
        foreach ((IExpression key, IExpression value) in expression.Entries)
        {
            // Compile in the order: key -> value
            CompileExpression(key);
            CompileExpression(value);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.MakeDictionary, expression.Entries.Count));
    }

    /// <summary>
    /// Compiles a basic binary expression
    /// </summary>
    /// <param name="expression">The expression to compile</param>
    /// <exception cref="MaxSyntaxException"></exception>
    private void CompileBinary(BinaryExpression expression)
    {
        // Compile sides
        CompileExpression(expression.Left);
        CompileExpression(expression.Right);

        switch (expression.Operator)
        {
            case "+":
                bytecode.Instructions.Add(new Instruction(OpCode.Add));
                break;
                
            case "-":
                bytecode.Instructions.Add(new Instruction(OpCode.Subtract));
                break;
                
            case "*":
                bytecode.Instructions.Add(new Instruction(OpCode.Multiply));
                break;
                
            case "/":
                bytecode.Instructions.Add(new Instruction(OpCode.Divide));
                break;

            default:
                throw new MaxSyntaxException(
                    $"Unsupported binary operator '{expression.Operator}'."
                );
        }
    }

    private void CompileComparison(ComparisonExpression expression)
    {
        CompileExpression(expression.Left);
        CompileExpression(expression.Right);

        bytecode.Instructions.Add(new Instruction(OpCode.Compare, expression.Operator));
    }

    private void CompileUnary(UnaryExpression expression)
    {
        CompileExpression(expression.Operand);

        switch (expression.Operator)
        {
            case UnaryOperator.Not:
            {
                bytecode.Instructions.Add(new Instruction(OpCode.Not));
                break;
            }

            case UnaryOperator.Negate:
            {
                bytecode.Instructions.Add(new Instruction(OpCode.Negate));
                break;
            }

            default:
                throw new MaxLogicException($"Unsupported Unary Operator: {expression.Operator}");
        }
    }

    private void CompileIndexing(IndexingExpression expression)
    {
        CompileExpression(expression.Name);
        CompileExpression(expression.IndexValue);
        bytecode.Instructions.Add(new Instruction(OpCode.Index));
    }

    /// <summary>
    /// Compiles a variable by giving it the store instruction
    /// </summary>
    /// <param name="variable">The variable to store</param>
    private void CompileVariable(VariableReference variable)
    {
        bytecode.Instructions.Add(new Instruction(OpCode.Load, variable.Name));
    }

    #endregion

    #region Compiling Statements

    #region Compiling Say

    /// <summary>
    /// Compiles a say statement
    /// </summary>
    /// <param name="statement">The statement to compile</param>
    private void CompileSay(SayStatement statement)
    {
        CompileExpression(statement.Value);

        bytecode.Instructions.Add(new Instruction(OpCode.Say));
    }

    #endregion

    #region Compiling Declaration

    private void CompileDeclaration(DeclarationStatement statement)
    {
        if (taskDepth == 0)
        {
            // Check for variables and tasks of the same name
            if (HasDuplicateNaming(statement.Name))
            {
                throw new MaxVariableException(
                    $"A task or variable of the same name has already been defined: {statement.Name}"
                );
            }
            
            globalVariableNames.Add(statement.Name, statement.Type);
        }

        // Compile value
        CompileExpression(statement.Value);

        // And compile name too as storage
        bytecode.Instructions.Add(new Instruction(OpCode.Store, new StoreOperand(
            statement.Name, statement.Type, statement.Resortable
        )));
    }

    #endregion

    #region Compiling Assignment

    private void CompileAssignment(AssignmentStatement statement)
    {
        if (statement.Target is not VariableReference variable)
        {
            // Also check for indexing!
            if (statement.Target is not IndexingExpression indexing)
            {
                throw new MaxSyntaxException("This expression cannot be assigned to");
            }

            // Compile the name of the index, it's value, and the value to update with. All in one!
            CompileExpression(indexing.Name);
            CompileExpression(indexing.IndexValue);
            CompileExpression(statement.Value);

            // Add instructions to update the index value
            bytecode.Instructions.Add(new Instruction(
                OpCode.UpdateIndex,
                // Create a new Update Index Operand, using either the variable name as a target, or null
                new UpdateIndexOperand(
                    statement.AssignmentType,
                    indexing.Name is VariableReference target ? target.Name : null
                )
            ));
            return;
        }

        // Compile the value
        CompileExpression(statement.Value);

        // Follow up with the Update Operand
        bytecode.Instructions.Add(new Instruction(
            OpCode.Update,
            new UpdateOperand(variable.Name, statement.AssignmentType)
        ));
    }

    #endregion

    #region Compiling If

    private void CompileIf(IfStatement statement)
    {
        /* List of jumps that could happen
            ---
            For the individual branches
            Prevents already selected branches (chosen i.e. already true) from falling into else
        */
        List<int> endJumps = [];
        List<int> possibleJumps = [];

        // The branches
        List<ConditionalBranch> branches = [statement.First_Consequences];
        branches.AddRange(statement.Other_Consequences);

        // Through each conditional branch
        foreach (ConditionalBranch branch in branches)
        {
            // In each branch, Compile the CheckStatement
            CompileExpression(branch.Check_Statement);

            // Simply add placeholder Jump Instructions until the actual consequences are evaluated
            int possibleJump = EmitJump(OpCode.JumpIfPossible);
            // Oh, and this list is for the possible evaluations, not the actual maybe block itself. Don't get confused!
            possibleJumps.Add(possibleJump);

            // Same for the false jump instructions
            int falseJump = EmitJump(OpCode.JumpIfFalse);

            // Enters the scope when evaluating
            bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

            // Compile the basic consequences
            foreach (IStatement consequence in branch.Consequences)
            {
                CompileStatement(consequence);
            }

            // Exits afterwards. This process is repeated for the other parts
            bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

            // Add the new Jump for the first consequence
            endJumps.Add(EmitJump(OpCode.Jump));

            // Update the false jump now, since we know what the next block is
            PatchJump(falseJump, bytecode.Instructions.Count);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        // Now for the Else
        foreach (IStatement consequence in statement.Else)
        {
            CompileStatement(consequence);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

        // Get the maybe block
        int maybeJump = EmitJump(OpCode.Jump);
        int maybeStart = bytecode.Instructions.Count;

        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        foreach (IStatement consequence in statement.Maybe)
        {
            CompileStatement(consequence);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

        // End of the maybe block after compiling all above
        int end = bytecode.Instructions.Count;
        // Now Patch (i.e. Update)
        PatchJump(maybeJump, end);

        // We now go through each conditional branch to Update
        foreach (int endJump in endJumps)
        {
            PatchJump(endJump, end);
        }

        // With possible too
        foreach (int possibleJump in possibleJumps)
        {
            PatchJump(possibleJump, maybeStart);
        }
    }

    /// <summary>
    /// Emits a jump instruction, and also returns the jump instructions index
    /// </summary>
    /// <param name="opCode">The Jump to add into the Instruction</param>
    /// <returns></returns>
    private int EmitJump(OpCode opCode)
    {
        int instructionIndex = bytecode.Instructions.Count;
        bytecode.Instructions.Add(new Instruction(opCode, -1));
        return instructionIndex;
    }

    /// <summary>
    /// Updates a Jump Instruction with the correct target
    /// </summary>
    /// <param name="instructionIndex">The instruction to target</param>
    /// <param name="target">The target value to input inside the jump instruction</param>
    private void PatchJump(int instructionIndex, int target)
    {
        // Replaces rather than modifies since Instruction is immutable
        bytecode.Instructions[instructionIndex] = new Instruction(
            bytecode.Instructions[instructionIndex].OpCode,
            target
        );
    }

    #endregion

    #region Compiling While

    private void CompileWhile(WhileStatement statement)
    {
        // Re-evaluate the condition at the start of every iteration.
        int conditionStart = bytecode.Instructions.Count;

        CompileExpression(statement.Check_Statement);
        // Get that exit jump based on the above check
        int exitJump = EmitJump(OpCode.JumpIfFalse);

        // Scopes apply in while as well
        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        foreach (IStatement line in statement.Consequences)
        {
            CompileStatement(line);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

        // When true, just jump to the start again
        int jumpBack = EmitJump(OpCode.Jump);
        PatchJump(jumpBack, conditionStart);

        // When it's false, it exits the body
        PatchJump(exitJump, bytecode.Instructions.Count);
    }

    #endregion

    #region Compiling For
    
    private void CompileFor(ForStatement statement)
    {
        if (statement.Declaration is not DeclarationStatement declaration)
        {
            throw new MaxSyntaxException(
                "Expected a declaration-like statement at beginning of for loop"
            );
        }

        // Enter scope. This applies to the temporary variable declared inside the for loop too!
        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        // Compile the declared variable
        CompileDeclaration(declaration);

        int stepSlot = nextTemporarySlot++;
        CompileExpression(statement.Step);
        bytecode.Instructions.Add(new Instruction(OpCode.TemporaryStore, stepSlot));

        // A zero step cannot make progress, so reject the loop.
        CompileTemporaryComparison(stepSlot, 0, "!=");
        int zeroStepExit = EmitJump(OpCode.JumpIfFalse);
        
        // Create a jump that also allows jumping over the error when needed
        int pastZeroCheck = EmitJump(OpCode.Jump);

        int zeroStepScopeExit = bytecode.Instructions.Count;
        // Exit scope when having to err
        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));
        bytecode.Instructions.Add(new Instruction(OpCode.Throw,
            new MaxLogicException("A for loops step amount cannot be 0")
        ));

        // Bind this to the error
        PatchJump(zeroStepExit, zeroStepScopeExit);

        int conditionStart = bytecode.Instructions.Count;
        // Also now jump past zero check when needed
        PatchJump(pastZeroCheck, conditionStart);

        // The deciding factor as to if the step is negative or positive
        CompileTemporaryComparison(stepSlot, 0, ">");
        int negativeBranch = EmitJump(OpCode.JumpIfFalse);

        // When the declaration is lower the expression with a negative step amount
        CompileForBoundCheck(declaration.Name, statement.Expression, "<=");
        int positiveExit = EmitJump(OpCode.JumpIfFalse);

        // The jump back to the body beginning
        int bodyJump = EmitJump(OpCode.Jump);

        PatchJump(negativeBranch, bytecode.Instructions.Count);

        // When the declaration is higher than the expression with a positive step amount
        CompileForBoundCheck(declaration.Name, statement.Expression, ">=");
        int negativeExit = EmitJump(OpCode.JumpIfFalse);

        int bodyStart = bytecode.Instructions.Count;
        PatchJump(bodyJump, bodyStart);

        // Compile the body
        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        foreach (IStatement line in statement.Consequences)
        {
            CompileStatement(line);
        }

        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

        // Adds the declared variable with the step amount
        CompileExpression(new VariableReference(declaration.Name));
        bytecode.Instructions.Add(new Instruction(OpCode.TemporaryLoad, stepSlot));
        bytecode.Instructions.Add(new Instruction(OpCode.Add));
        // And now reassign
        bytecode.Instructions.Add(new Instruction(OpCode.Update, new UpdateOperand(declaration.Name, "=")));

        int jumpBack = EmitJump(OpCode.Jump);
        PatchJump(jumpBack, conditionStart);

        int exit = bytecode.Instructions.Count;

        // Exit scope
        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

        // All these lead to exitting the body when needed
        PatchJump(positiveExit, exit);
        PatchJump(negativeExit, exit);
    }

    /// <summary>
    /// A helper for step logic. Compares the step amount to the value, as decided by the operation
    /// </summary>
    /// <param name="stepSlot"></param>
    /// <param name="value"></param>
    /// <param name="operation"></param>
    private void CompileTemporaryComparison(int stepSlot, object value, string operation)
    {
        bytecode.Instructions.Add(new Instruction(OpCode.TemporaryLoad, stepSlot));
        CompileConstant(value);
        bytecode.Instructions.Add(new Instruction(OpCode.Compare, operation));
    }

    /// <summary>
    /// Compiles instructions that tell the VM to check if the declaration is out of bounds of the expression.
    /// Dependent on the operation.
    /// </summary>
    /// <param name="declarationName">The declaration name that will be referenced as a variable</param>
    /// <param name="expression">The expression to check against</param>
    /// <param name="operation">The operation decider</param>
    private void CompileForBoundCheck(string declarationName, IExpression expression, string operation)
    {
        CompileExpression(new VariableReference(declarationName));
        CompileExpression(expression);
        bytecode.Instructions.Add(new Instruction(OpCode.Compare, operation));
    }

    #endregion

    #region Compiling Foreach

    private void CompileForeach(ForeachStatement statement)
    {
        int collectionSlot = nextTemporarySlot++;
        CompileExpression(statement.IterableCollection);
        // Temporarily store the collection
        bytecode.Instructions.Add(new Instruction(OpCode.TemporaryStore, collectionSlot));

        // Enter scope like any other loop...
        bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

        // Create a placeholder constant that will hold the declarations value
        CompileConstant(new Nil());
        // Stored with a StoreOperand of all the parts of the declaration too! Although mostly nil still
        bytecode.Instructions.Add(new Instruction(OpCode.Store, new StoreOperand(
            statement.Declaration.Name, statement.Declaration.Type, statement.Declaration.Resortable
        )));

        // Hold a temporary spot for the index value of the declaration in the list
        int indexSlot = nextTemporarySlot++;
        CompileConstant(0);
        // This is temporary of course because we are simply indexing the item. We don't want it to appear to the user
        bytecode.Instructions.Add(new Instruction(OpCode.TemporaryStore, indexSlot));

        // Now we get where the loop starts
        int loopStart = bytecode.Instructions.Count;
        bytecode.Instructions.Add(new Instruction(
            OpCode.ForeachNext,
            // Generates a ForeachOperand, which can be supplied through the Instruction
            new ForeachOperand(
                collectionSlot,
                indexSlot,
                statement.Declaration.Name,
                statement.Declaration.Type
            )
        ));

        // The jump to exit
        int exitJump = EmitJump(OpCode.JumpIfFalse);

        // Compile the body...
        foreach (IStatement line in statement.Consequences)
        {
            CompileStatement(line);
        }

        int loopBack = EmitJump(OpCode.Jump);
        PatchJump(loopBack, loopStart);

        // Patch the exit jump, and enable exitting the scope. Skidoosh!
        int exit = bytecode.Instructions.Count;
        bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));
        PatchJump(exitJump, exit);
    }

    #endregion

    #region Compiling Tasks

    private void CompileTaskDefinition(TaskDefinition task)
    {
        switch (task.TaskType)
        {
            case TaskType.Function:
            {
                // Ensure no duplicate tasks or variables
                if (HasDuplicateNaming(task.Name))
                {
                    throw new MaxTaskException($"A task or variable of the same name has already been defined: {task.Name}");
                }

                // A jump for when to skip the body
                int skipBody = EmitJump(OpCode.Jump);

                // Enter scope
                bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));

                // A jump for when to execute the body
                int entryPoint = bytecode.Instructions.Count;

                // Add a Task Info, telling the tasks list the new Tasks information, as necessary to execute
                bytecode.Tasks.Add(task.Name, new TaskInfo(entryPoint, task.Parameters, task.ReturnType));

                // Increase the depth of the task
                // Task depth is specific to tasks
                taskDepth++;
                try
                {
                    // Track parameter types in the compiler so member accesses inside the body know their types!
                    foreach (Parameter param in task.Parameters)
                    {
                        globalVariableNames[param.Name] = param.Type;
                    }

                    // Safe compiling
                    foreach (IStatement statement in task.Body)
                    {
                        CompileStatement(statement);
                    }
                }
                finally
                {
                    // Clean up parameters after compiling the body so they don't 'leak' 🤔 outside the task
                    foreach (Parameter param in task.Parameters)
                    {
                        globalVariableNames.Remove(param.Name);
                    }

                    // Task depth is lowered at the end always
                    taskDepth--;
                }

                // A return instruction, which tells when and what to return
                bytecode.Instructions.Add(new Instruction(OpCode.Return));

                bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

                // Now patch the jump for when to skip
                PatchJump(skipBody, bytecode.Instructions.Count);

                break;
            }

            case TaskType.Coroutine:
            {
                // Similar logic to functions here
                if (HasDuplicateNaming(task.Name))
                {
                    throw new MaxTaskException($"A task or variable of the same name has already been defined: {task.Name}");
                }

                int skipBody = EmitJump(OpCode.Jump);

                bytecode.Instructions.Add(new Instruction(OpCode.EnterScope));
                
                int entryPoint = bytecode.Instructions.Count;

                // Supply info to coroutine dictionary
                bytecode.Coroutines.Add(task.Name, new CoroutineInfo(entryPoint, task.ReturnType));

                taskDepth++;
                try
                {
                    // Parameters (being nothing) are ignored

                    // Safe compiling
                    foreach (IStatement statement in task.Body)
                    {
                        CompileStatement(statement);
                    }
                }
                finally
                {
                    taskDepth--;
                }

                bytecode.Instructions.Add(new Instruction(OpCode.ExitScope));

                // We add this to allow the coroutine to return nil
                CompileConstant(new Nil());

                // Simply return with no value left
                bytecode.Instructions.Add(new Instruction(OpCode.ReturnValue));

                PatchJump(skipBody, bytecode.Instructions.Count);

                // Instructions for VM to create a Coroutine Instance of this
                bytecode.Instructions.Add(new Instruction(OpCode.CreateCoroutine, new CoroutineInfo(entryPoint, task.ReturnType)));

                /*
                    Store in a variable the coroutine instance
                    REMEMBER!!!!
                    We storing it a variable because it is a coroutine object, so it can be member accessed to be called!
                */
                bytecode.Instructions.Add(new Instruction(OpCode.Store, new StoreOperand(task.Name, task.ReturnType, null)));

                break;
            }

            default: throw new Exception($"Unresolved Tasktype given: {task.TaskType}");
        }
    }

    /// <summary>
    /// A gate compiler that also takes into account builtin and native functions
    /// </summary>
    /// <param name="taskCall">The task call to compile, native or not</param>
    private void CompileTaskCall(TaskCall taskCall)
    {
        if (taskCall.Name is MemberAccessExpression member)
        {
            // First, we check if the target is a static native module
            if (member.Target is VariableReference targetVar)
            {
                // Incase there is a variable of the same name!
                bool isShadowed = globalVariableNames.ContainsKey(targetVar.Name);

                // When plausible, check if the name is a static module
                if (!isShadowed && nativeRegistry.IsStaticModule(targetVar.Name)) {
                    // Compile arguments WITHOUT pushing target variable as 'self'!
                    foreach (var arg in taskCall.Arguments)
                    {
                        CompileExpression(arg);
                    }

                    // Now call the function, with the module name, and isMemberAccessed as false (it technically is not, it is module-accessed)
                    bytecode.Instructions.Add(new Instruction(
                        OpCode.CallNative,
                        new TaskCallOperand(targetVar.Name, member.Member, taskCall.Arguments.Count, false)
                    ));
                    return;
                }
            }

            // Push arguments to the stack:
            // First, compile the variable itself
            CompileExpression(member.Target);

            // Now, we compile all explicit arguments passed to the method
            foreach (var arg in taskCall.Arguments)
            {
                // I'd argue not to but... This is beyond my Parameter! 😂
                CompileExpression(arg);
            }

            // Give Instructions with CallNative with argument count + 1 (including 'self')
            // It is infact also member accessed too!
            bytecode.Instructions.Add(new Instruction(
                OpCode.CallNative,
                new TaskCallOperand(null, member.Member, taskCall.Arguments.Count + 1, true)
            ));
            return;
        }
        
        CompileStandardTaskCall(taskCall);
    }

    /// <summary>
    /// The standard task call compiler
    /// </summary>
    /// <param name="taskCall">The defined task call to compile</param>
    private void CompileStandardTaskCall(TaskCall taskCall)
    {
        foreach (IExpression argument in taskCall.Arguments)
        {
            CompileExpression(argument);
        }

        TaskReference taskReference = ResolveTaskName(taskCall.Name);

        bytecode.Instructions.Add(new Instruction(
            OpCode.Call,
            new TaskCallOperand(
                taskReference.Target,
                taskReference.Name,
                taskCall.Arguments.Count
            )
        ));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks a name in the global variables, tasks and native tasks for if it already exists
    /// </summary>
    /// <param name="name">The name to check</param>
    /// <returns></returns>
    private bool HasDuplicateNaming(string name)
    {
        if (bytecode.Tasks.ContainsKey(name) ||
            bytecode.Coroutines.ContainsKey(name) ||
            globalVariableNames.TryGetValue(name, out _) ||
            nativeRegistry.TryGetTask(null, name, out _))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve a Task Name from an expression
    /// </summary>
    /// <param name="expression">The expression to resolve.
    /// Either a Variable Reference or Member Access Expression</param>
    /// <returns>The name if possible, else an error</returns>
    /// <exception cref="MaxTaskException"></exception>
    private static TaskReference ResolveTaskName(IExpression expression)
    {
        return expression switch
        {
            VariableReference variable => new TaskReference(null, variable.Name),
            // Using ResolveTaskName here allows recursive expressions
            MemberAccessExpression member => ResolveMemberReference(member),

            _ => throw new MaxTaskException("Only named tasks can be called."),
        };
    }

    private static TaskReference ResolveMemberReference(MemberAccessExpression member)
    {
        TaskReference parent = ResolveTaskName(member.Target);
        string target = parent.Target is null
            ? parent.Name
            : $"{parent.Target}.{parent.Name}";

        return new TaskReference(target, member.Member);
    }

    #endregion

    #endregion
}