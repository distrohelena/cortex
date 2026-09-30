using Filomena.Core.Identity;

namespace Filomena.App.FlowRuntime;

public sealed class DmFlowLiveOptions
{
    public string Channel { get; set; } = "whatsapp";
    public string AccountId { get; set; } = "default";
    public string AllowedDmNativeId { get; set; } = "";

    public void Validate()
    {
        if (Channel != "whatsapp" || string.IsNullOrWhiteSpace(AccountId) || AccountId != AccountId.Trim()
            || string.IsNullOrWhiteSpace(AllowedDmNativeId)
            || !(AllowedDmNativeId.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) || AllowedDmNativeId.EndsWith("@lid", StringComparison.Ordinal))
            || FilomenaIdentity.NormalizeWhatsAppActorNativeId(AllowedDmNativeId) != AllowedDmNativeId)
            throw new ArgumentException("Configure FlowLive:AllowedDmNativeId com o ID canônico de uma única DM WhatsApp e a conta correta.");
    }
}
