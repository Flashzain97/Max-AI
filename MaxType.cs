// Under license to the reader by the author of the Max Language®
// Relaying these files and/or distributing or publishing them without the author's consent may incur a fine

namespace Max {
    public abstract class MaxType
    {
    }

    public class NumberType : MaxType
    {
    }

    public class IntegerType : NumberType
    {
    }

    public class FloatType : NumberType
    {
    }

    public class StringType : MaxType
    {
    }

    public class NothingType : MaxType
    {
    }

    public class BooleanType : MaxType
    {
    }

    public class ListType(MaxType? elementType = null) : MaxType
    {
        public MaxType? ElementType { get; } = elementType;
    }

    public class OptionalType(MaxType innerType) : MaxType
    {
        public MaxType InnerType { get; } = innerType;
    }

    public class PossibleType : BooleanType
    {
    }

    public static class TypeRegistry
    {
        public static readonly Dictionary<string, MaxType> Max_TypeNames = new()
        {
            { "int", new IntegerType() },
            { "float", new FloatType() },
            { "string" , new StringType() },
            { "nothing", new NothingType() },
            { "boolean", new BooleanType() },
            { "list", new ListType() }
        };
    }
}
