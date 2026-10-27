using System.Runtime.CompilerServices;

namespace Max;

public sealed record SourcePosition(int Line, int Column);

public static class SourceMap
{
    private static readonly ConditionalWeakTable<IAstNode, SourcePosition> Locations = new();
    private static readonly ConditionalWeakTable<List<Token>, SourcePosition> EndOfFileLocations = new();

    internal static void SetEndOfFile(List<Token> tokens, int line, int column) =>
        EndOfFileLocations.Add(tokens, new SourcePosition(line, column));

    internal static SourcePosition? GetEndOfFile(List<Token> tokens) =>
        EndOfFileLocations.TryGetValue(tokens, out var location) ? location : null;

    public static void Set(IAstNode node, int line, int column)
    {
        lock (Locations)
        {
            Locations.Remove(node);
            Locations.Add(node, new SourcePosition(line, column));
        }
    }

    public static SourcePosition? Get(IAstNode node) =>
        Locations.TryGetValue(node, out var location) ? location : null;

    public static TException WithLocation<TException>(TException error, int line, int column)
        where TException : Exception
    {
        error.Data["MaxLocation"] = new SourcePosition(line, column);
        return error;
    }
}
