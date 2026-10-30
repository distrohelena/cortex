namespace Cortex.Inference.Tests.LocalModels;

/// <summary>The OpenJev demo cases, shared by every local-model provider test so all providers answer the same texts.</summary>
internal static class LocalModelDemoCases
{
    internal const string AccountState = "A customer says a password reset succeeded, but every login attempt still returns ‘account locked’. Two unlock emails were requested and neither arrived.";
    internal const string EmailState = "An email claims to be from the payroll team and says the recipient’s salary payment will be suspended today. It comes from payroll-review@outlook.com and links to a non-company sign-in page asking for a password and verification code.";
    internal const string EmailQuestion = "How should this email be classified?";
    internal static readonly string[] EmailOptions = ["Legitimate", "Spam", "Phishing"];

    internal static readonly LocalDecisionCase[] Cases =
    [
        new("account", "en", AccountState, "Which queue should handle this request?", "queue", false,
            ["Account access support", "Billing support", "Close as resolved"], null, "Account access support"),
        new("email", "en", EmailState, EmailQuestion, "classification", false, EmailOptions, null, "Phishing"),
        new("boolean", "en", EmailState, "Classify this email.", "asks_for_credentials", true, [],
            "The message asks the recipient to enter a password or verification code.", "true"),
    ];
}
