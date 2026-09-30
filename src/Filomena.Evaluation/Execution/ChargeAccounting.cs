namespace Filomena.Evaluation.Execution;

public sealed record ChargeAccountingEntry(Guid AttemptId, Guid? DecisionId, decimal Reservation,
    string Status, string? Currency, decimal? Charge);
public sealed record ChargeAccountingReport(string? Currency, decimal? DeclaredTotal, decimal KnownDeclaredSubtotal,
    int MissingDeclarations, int UnavailableEvidence, decimal RetainedExposureLowerBound)
{
    public string Origin => "operator_billing_attestation";
    public bool AuthorizesRefundOrRetry => false;
}

public static class ChargeAccounting
{
    public static ChargeAccountingReport Project(IReadOnlyList<ChargeAccountingEntry> entries, string? approvedCurrency,
        decimal parentReservation)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (parentReservation < 0 || approvedCurrency is not null &&
            (approvedCurrency.Length != 3 || approvedCurrency.Any(c => c is < 'A' or > 'Z')))
            throw new InvalidDataException("Invalid monetary projection context.");
        var attempts = new HashSet<Guid>();
        var decisions = new HashSet<Guid>();
        decimal subtotal = 0, retained = 0;
        int missing = 0, unavailable = 0;
        try
        {
            foreach (var entry in entries)
            {
                if (entry is null || entry.AttemptId == Guid.Empty || !attempts.Add(entry.AttemptId) || entry.Reservation < 0 ||
                    entry.DecisionId is Guid id && (id == Guid.Empty || !decisions.Add(id)))
                    throw new InvalidDataException("Invalid or duplicated monetary evidence.");
                switch (entry.Status)
                {
                    case "declared" when entry.DecisionId is not null && entry.Charge is >= 0 &&
                        approvedCurrency is not null && entry.Currency == approvedCurrency:
                        subtotal = checked(subtotal + entry.Charge.Value);
                        break;
                    case "missing" when entry.DecisionId is null && entry.Currency is null && entry.Charge is null:
                        missing++;
                        break;
                    case "evidence_unavailable" when entry.DecisionId is not null && entry.Currency is null && entry.Charge is null:
                        unavailable++;
                        break;
                    default:
                        throw new InvalidDataException("Invalid declaration status, currency or charge.");
                }
                retained = checked(retained + Math.Max(entry.Reservation, entry.Charge ?? 0));
            }
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Monetary projection overflow; no partial total.", error);
        }
        return new(approvedCurrency, entries.Count > 0 && missing == 0 && unavailable == 0 ? subtotal : null,
            subtotal, missing, unavailable, Math.Max(parentReservation, retained));
    }
}
