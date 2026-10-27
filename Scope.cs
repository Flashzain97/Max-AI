// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max
{
    public class Scope(Scope? parent = null)
    {
        public Dictionary<string, Variable> Variables { get; } = [];
        public Dictionary<string, UserFunction> Functions { get; } = [];

        public Scope? Parent { get; } = parent;
    }
}
