namespace Cortex.Inference.Tests.LocalModels;

/// <summary>The OpenJev demo cases, shared by every local-model provider test so all providers answer the same texts.</summary>
internal static class LocalModelDemoCases
{
    internal const string AccountState = "A customer says a password reset succeeded, but every login attempt still returns ‘account locked’. Two unlock emails were requested and neither arrived.";
    internal const string EmailState = "An email claims to be from the payroll team and says the recipient’s salary payment will be suspended today. It comes from payroll-review@outlook.com and links to a non-company sign-in page asking for a password and verification code.";
    internal const string EmailQuestion = "How should this email be classified?";
    internal static readonly string[] EmailOptions = ["Legitimate", "Spam", "Phishing"];

    internal sealed record DemoCase(string Name, string State, string Question, string Field, string Schema, string[] Options, string Expected);

    internal static readonly DemoCase[] Cases =
    [
        new("account", AccountState, "Which queue should handle this request?", "queue",
            """{"type":"object","properties":{"queue":{"type":"string","enum":["Account access support","Billing support","Close as resolved"]}},"required":["queue"],"additionalProperties":false}""",
            ["Account access support", "Billing support", "Close as resolved"], "Account access support"),
        new("email", EmailState, EmailQuestion, "classification",
            """{"type":"object","properties":{"classification":{"type":"string","enum":["Legitimate","Spam","Phishing"]}},"required":["classification"],"additionalProperties":false}""",
            EmailOptions, "Phishing"),
        new("boolean", EmailState, "Classify this email.", "asks_for_credentials",
            """{"type":"object","properties":{"asks_for_credentials":{"type":"boolean","description":"The message asks the recipient to enter a password or verification code."}},"required":["asks_for_credentials"],"additionalProperties":false}""",
            ["Yes", "No"], "true"),
    ];
}
