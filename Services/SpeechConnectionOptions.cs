namespace Sonja.ReadAloud.Services;

public sealed class SpeechConnectionOptions
{
    private static readonly HashSet<string> OpenAiVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer"
    };

    public required string Key { get; init; }

    public string? Region { get; init; }

    public Uri? Endpoint { get; init; }

    public string AzureOpenAiKey { get; init; } = string.Empty;

    public Uri? AzureOpenAiEndpoint { get; init; }

    public string AzureOpenAiDeployment { get; init; } = string.Empty;

    public string AzureOpenAiApiVersion { get; init; } = "2025-03-01-preview";

    public string ElevenLabsKey { get; init; } = string.Empty;

    public bool IsAzureSpeechConfigured =>
        !string.IsNullOrWhiteSpace(Key) &&
        (!string.IsNullOrWhiteSpace(Region) || Endpoint is not null);

    public bool IsAzureOpenAiConfigured =>
        !string.IsNullOrWhiteSpace(AzureOpenAiKey) &&
        AzureOpenAiEndpoint is not null &&
        !string.IsNullOrWhiteSpace(AzureOpenAiDeployment);

    public bool IsElevenLabsConfigured => !string.IsNullOrWhiteSpace(ElevenLabsKey);

    public bool IsConfigured => IsAzureSpeechConfigured || IsElevenLabsConfigured || IsAzureOpenAiConfigured;

    public string ConnectionSummary => IsAzureSpeechConfigured
        ? $"Azure Speech · {(!string.IsNullOrWhiteSpace(Region) ? Region : Endpoint!.Host)}"
        : IsElevenLabsConfigured
            ? "Cloud voice · ElevenLabs"
            : IsAzureOpenAiConfigured
                ? $"Foundry TTS · {AzureOpenAiEndpoint!.Host}"
                : "Speech credentials not found";

    public string GetCompatibleVoice(string voiceName)
    {
        if (IsAzureSpeechConfigured)
        {
            return string.IsNullOrWhiteSpace(voiceName) ? Models.AppSettings.DefaultVoiceName : voiceName;
        }

        if (IsElevenLabsConfigured)
        {
            return voiceName;
        }

        return OpenAiVoices.Contains(voiceName) ? voiceName : "alloy";
    }

    public static SpeechConnectionOptions FromEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        environment.TryGetValue("SPEECH_KEY", out var key);
        if (string.IsNullOrWhiteSpace(key))
        {
            environment.TryGetValue("AZURE_SPEECH_KEY", out key);
        }

        environment.TryGetValue("SPEECH_REGION", out var region);
        if (string.IsNullOrWhiteSpace(region))
        {
            environment.TryGetValue("AZURE_SPEECH_REGION", out region);
        }

        environment.TryGetValue("SPEECH_ENDPOINT", out var endpointValue);
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(endpointValue))
        {
            Uri.TryCreate(endpointValue, UriKind.Absolute, out endpoint);
        }

        environment.TryGetValue("AZURE_OPENAI_TTS_KEY", out var openAiKey);
        environment.TryGetValue("AZURE_OPENAI_TTS_ENDPOINT", out var openAiEndpointValue);
        environment.TryGetValue("AZURE_OPENAI_TTS_DEPLOYMENT", out var openAiDeployment);
        environment.TryGetValue("AZURE_OPENAI_TTS_API_VERSION", out var openAiApiVersion);
        Uri? openAiEndpoint = null;
        if (!string.IsNullOrWhiteSpace(openAiEndpointValue))
        {
            Uri.TryCreate(openAiEndpointValue, UriKind.Absolute, out openAiEndpoint);
        }

        environment.TryGetValue("ELEVENLABS_API_KEY", out var elevenLabsKey);

        return new SpeechConnectionOptions
        {
            Key = key?.Trim() ?? string.Empty,
            Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim(),
            Endpoint = endpoint,
            AzureOpenAiKey = openAiKey?.Trim() ?? string.Empty,
            AzureOpenAiEndpoint = openAiEndpoint,
            AzureOpenAiDeployment = openAiDeployment?.Trim() ?? string.Empty,
            AzureOpenAiApiVersion = string.IsNullOrWhiteSpace(openAiApiVersion)
                ? "2025-03-01-preview"
                : openAiApiVersion.Trim(),
            ElevenLabsKey = elevenLabsKey?.Trim() ?? string.Empty
        };
    }
}