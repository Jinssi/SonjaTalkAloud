namespace Sonja.ReadAloud.Models;

public sealed record VoiceOption(string ShortName, string DisplayName, string Locale);

public sealed record VoiceListResult(bool Success, string Message, IReadOnlyList<VoiceOption> Voices)
{
    public static VoiceListResult Failed(string message) => new(false, message, Array.Empty<VoiceOption>());
}