using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Filomena.Core.Boundaries;

public readonly record struct BoundarySourceSpan(int Start, int Length)
{
    public string Extract(string source) => source.Substring(Start, Length);
}

public sealed record BoundaryProposal(string Status, string? Kind, string? TopicEvidence, string? ConstraintEvidence);
public sealed record BoundaryAdmissionResult(string Status, string? DiagnosticCode,
    BoundarySourceSpan? NormativeClause = null, BoundarySourceSpan? Constraint = null, BoundarySourceSpan? Topic = null);

/// <summary>A closed admission language, not a general Portuguese parser. Unknown syntax abstains.</summary>
public static partial class BoundaryAdmission
{
    public const string Kind = "no_teasing_about_topic";

    // No open-ended topic tail: conjunctions, exceptions, quotations and injected clauses cannot be consumed.
    private const string TopicLanguage = @"(?:(?:meu|minha|seu|sua|o|a) )?(?:trabalho|emprego|carreira|salário|família|aparência|saúde|projeto|vida pessoal|vida profissional|episódio profissional)(?: de ontem| de hoje| atual| recente)?";
    private const string OperationLanguage = @"não (?:brinque|faça piada|provoque) sobre";
    private const string ReportedOperationLanguage = @"não (?:brincar nem provocar|brincar|fazer piada|provocar) sobre";

    // The reported form is exactly the production GateMemory wrapper. Names here never establish identity.
    [GeneratedRegex(@"\A(?:(?<clause>(?<constraint>" + OperationLanguage + @") (?<topic>" + TopicLanguage + @"))|helena pediu para filomena (?<clause>(?<constraint>" + ReportedOperationLanguage + @") (?<topic>" + TopicLanguage + @")))[.!]?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AdmittedClause();

    [GeneratedRegex(@"\A(?:não (?:brinque|faça piada|provoque) sobre (?:isso|aquilo|esse assunto|este assunto)|melhor não entrar nesse assunto)[.!]?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AmbiguousReference();

    public static BoundaryAdmissionResult Analyze(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 4096) return Failure("unsupported", "source-outside-admission-language");
        var normalized = Normalize(source);
        if (AmbiguousReference().IsMatch(normalized.Text)) return Failure("ambiguous", "unresolved-topic-reference");
        var match = AdmittedClause().Match(normalized.Text);
        if (!match.Success) return Failure("unsupported", "whole-clause-not-admitted");
        BoundarySourceSpan Span(string group)
        {
            var capture = match.Groups[group];
            var start = normalized.Starts[capture.Index];
            var end = normalized.Ends[capture.Index + capture.Length - 1];
            return new(start, end - start);
        }
        return new("compiled", null, Span("clause"), Span("constraint"), Span("topic"));
    }

    public static BoundaryAdmissionResult Admit(string source, BoundaryProposal proposal)
    {
        if (proposal.Status is "ambiguous" or "unsupported")
            return proposal.Kind is null && proposal.TopicEvidence is null && proposal.ConstraintEvidence is null
                ? Failure(proposal.Status, "interpreter-abstained") : Failure("unsupported", "inconsistent-proposal");
        var admitted = Analyze(source);
        if (admitted.Status != "compiled") return admitted;
        if (proposal.Status != "compiled" || proposal.Kind != Kind) return Failure("unsupported", "unknown-status-or-kind");
        var topic = UniqueSpan(source, proposal.TopicEvidence);
        var constraint = UniqueSpan(source, proposal.ConstraintEvidence);
        if (topic is null || constraint is null) return Failure("unsupported", "evidence-not-unique-exact-span");
        if (topic != admitted.Topic || constraint != admitted.Constraint) return Failure("unsupported", "normative-clause-not-fully-consumed");
        return admitted;
    }

    public static BoundarySourceSpan? UniqueSpan(string source, string? evidence)
    {
        if (string.IsNullOrEmpty(evidence)) return null;
        var start = source.IndexOf(evidence, StringComparison.Ordinal);
        if (start < 0 || source.IndexOf(evidence, start + 1, StringComparison.Ordinal) >= 0) return null;
        return new(start, evidence.Length);
    }

    private static BoundaryAdmissionResult Failure(string status, string code) => new(status, code);

    private sealed record NormalizedSource(string Text, List<int> Starts, List<int> Ends);

    private static NormalizedSource Normalize(string source)
    {
        var text = new StringBuilder();
        var starts = new List<int>();
        var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(source);
        while (elements.MoveNext())
        {
            var original = elements.GetTextElement();
            var start = elements.ElementIndex;
            var end = start + original.Length;
            if (original.All(char.IsWhiteSpace))
            {
                if (text.Length == 0) continue;
                if (text[^1] == ' ') { ends[^1] = end; continue; }
                text.Append(' '); starts.Add(start); ends.Add(end);
            }
            else
            {
                var normalized = original.Normalize(NormalizationForm.FormC).ToLowerInvariant();
                foreach (var character in normalized) { text.Append(character); starts.Add(start); ends.Add(end); }
            }
        }
        if (text.Length > 0 && text[^1] == ' ') { text.Length--; starts.RemoveAt(starts.Count - 1); ends.RemoveAt(ends.Count - 1); }
        return new(text.ToString(), starts, ends);
    }
}
