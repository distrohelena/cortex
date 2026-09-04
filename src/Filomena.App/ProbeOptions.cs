namespace Filomena.App;

public sealed class ProbeOptions
{
    public const string SectionName = "Probe";

    public string Mode { get; set; } = "milestone-a";

    public int WaitTimeoutSeconds { get; set; } = 300;

    public int ReconnectDelaySeconds { get; set; } = 2;

    public string OpenClawCommand { get; set; } = "openclaw.cmd";

    public string[] OpenClawArguments { get; set; } =
        ["mcp", "serve", "--claude-channel-mode", "off"];

    public string[] TargetConversationIds { get; set; } =
    [
        "5511981042361-1589823314@g.us",
        "120363318973251043@g.us",
        "5511944558218@s.whatsapp.net",
    ];

    public static bool IsSupportedMode(string? mode) =>
        mode is "inspect" or "list" or "wait" or "milestone-a" or "phase-b";
}
