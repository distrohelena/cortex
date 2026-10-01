namespace Cortex.Core.Flows;

internal static class TypedBranch
{
    internal static void Validate(FlowType left, FlowType right, string? operation)
    {
        if (left is not (BooleanType or NumberType or StringType or EnumType) ||
            left.GetType() != right.GetType() || !left.IsCompatibleWith(right) || !right.IsCompatibleWith(left))
            throw new InvalidDataException("Branch exige escalares do mesmo tipo; sem conversão implícita ou opcionais.");
        if (operation is "eq" or "ne") return;
        if (left is NumberType && operation is "gt" or "gte" or "lt" or "lte") return;
        throw new InvalidDataException("Comparação incompatível com o tipo do Branch.");
    }

    internal static bool Compare(TypedValue left, TypedValue right, string operation)
    {
        Validate(left.Type, right.Type, operation);
        FlowDomain.Check(left.Value, left.Type); FlowDomain.Check(right.Value, right.Type);
        var comparison = left.Type switch
        {
            NumberType => left.Value.GetDouble().CompareTo(right.Value.GetDouble()),
            BooleanType => left.Value.GetBoolean().CompareTo(right.Value.GetBoolean()),
            _ => string.Compare(left.Value.GetString(), right.Value.GetString(), StringComparison.Ordinal)
        };
        return operation switch
        {
            "eq" => comparison == 0,
            "ne" => comparison != 0,
            "gt" => comparison > 0,
            "gte" => comparison >= 0,
            "lt" => comparison < 0,
            "lte" => comparison <= 0,
            _ => throw new InvalidDataException("Comparação inválida.")
        };
    }
}
