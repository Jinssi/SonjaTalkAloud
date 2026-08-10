using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Sonja.ReadAloud.Models;

namespace Sonja.ReadAloud.Services;

public sealed class SpeechService : IDisposable
{
    private const int AzureSpeechMaximumChunkLength = 8_000;
    private const int OpenAiMaximumChunkLength = 3_500;

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly VoiceOption[] OpenAiVoiceOptions =
    {
        new("alloy", "Alloy · balanced and neutral", "Multilingual"),
        new("ash", "Ash · clear and conversational", "Multilingual"),
        new("ballad", "Ballad · warm and expressive", "Multilingual"),
        new("coral", "Coral · warm and friendly", "Multilingual"),
        new("echo", "Echo · smooth and composed", "Multilingual"),
        new("fable", "Fable · expressive storyteller", "Multilingual"),
        new("nova", "Nova · bright and energetic", "Multilingual"),
        new("onyx", "Onyx · deep and confident", "Multilingual"),
        new("sage", "Sage · calm and measured", "Multilingual"),
        new("shimmer", "Shimmer · clear and upbeat", "Multilingual")
    };

    private readonly SpeechConnectionOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Func<Stream, TimeSpan, CancellationToken, Task<bool>>? _playbackOverride;
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly object _syncRoot = new();
    private SpeechSynthesizer? _activeSynthesizer;
    private SoundPlayer? _activeSoundPlayer;
    private CancellationTokenSource? _speechCancellation;
    private IReadOnlyList<VoiceOption>? _elevenLabsVoices;
    private bool _isSpeaking;
    private bool _stopRequested;
    private bool _disposed;

    public SpeechService(SpeechConnectionOptions options)
        : this(options, SharedHttpClient, null)
    {
    }

    internal SpeechService(
        SpeechConnectionOptions options,
        HttpClient httpClient,
        Func<Stream, TimeSpan, CancellationToken, Task<bool>>? playbackOverride)
    {
        _options = options;
        _httpClient = httpClient;
        _playbackOverride = playbackOverride;
    }

    public bool IsConfigured => _options.IsConfigured;

    public bool IsSpeaking
    {
        get
        {
            lock (_syncRoot)
            {
                return _isSpeaking;
            }
        }
    }

    public async Task<OperationResult> SpeakAsync(string text, string voiceName, int ratePercent)
    {
        if (!IsConfigured)
        {
            return OperationResult.Fail("No Azure speech provider is configured in .env.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult.Fail("There is no text to speak.");
        }

        await _speechGate.WaitAsync();
        CancellationTokenSource? speechCancellation = null;
        try
        {
            lock (_syncRoot)
            {
                speechCancellation = new CancellationTokenSource();
                _speechCancellation = speechCancellation;
                _isSpeaking = true;
                _stopRequested = false;
            }

            if (_options.IsAzureSpeechConfigured)
            {
                return await SpeakWithAzureSpeechAsync(text, voiceName, ratePercent);
            }

            return _options.IsElevenLabsConfigured
                ? await SpeakWithElevenLabsAsync(text, voiceName, ratePercent, speechCancellation.Token)
                : await SpeakWithAzureOpenAiAsync(text, voiceName, ratePercent, speechCancellation.Token);
        }
        catch (Exception ex)
        {
            return WasStopRequested()
                ? OperationResult.Stopped()
                : OperationResult.Fail($"Cloud speech failed: {ex.Message}");
        }
        finally
        {
            lock (_syncRoot)
            {
                _activeSynthesizer = null;
                _activeSoundPlayer = null;
                if (ReferenceEquals(_speechCancellation, speechCancellation))
                {
                    _speechCancellation = null;
                }
                _isSpeaking = false;
            }

            speechCancellation?.Dispose();
            _speechGate.Release();
        }
    }

    private async Task<OperationResult> SpeakWithAzureSpeechAsync(string text, string voiceName, int ratePercent)
    {
        var speechConfig = CreateSpeechConfig(voiceName);
        using var audioConfig = AudioConfig.FromDefaultSpeakerOutput();
        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig);

        lock (_syncRoot)
        {
            _activeSynthesizer = synthesizer;
        }

        foreach (var chunk in ChunkText(text, AzureSpeechMaximumChunkLength))
        {
            if (WasStopRequested())
            {
                return OperationResult.Stopped();
            }

            var ssml = BuildSsml(chunk, voiceName, ratePercent);
            var result = await synthesizer.SpeakSsmlAsync(ssml);
            if (WasStopRequested())
            {
                return OperationResult.Stopped();
            }

            if (result.Reason == ResultReason.Canceled)
            {
                var details = SpeechSynthesisCancellationDetails.FromResult(result);
                var message = details.Reason == CancellationReason.Error
                    ? $"Azure Speech canceled synthesis: {details.ErrorDetails}"
                    : $"Azure Speech canceled synthesis: {details.Reason}.";
                return OperationResult.Fail(message);
            }

            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                return OperationResult.Fail($"Azure Speech returned an unexpected result: {result.Reason}.");
            }
        }

        return OperationResult.Ok("Speech complete.");
    }

    private async Task<OperationResult> SpeakWithAzureOpenAiAsync(
        string text,
        string voiceName,
        int ratePercent,
        CancellationToken cancellationToken)
    {
        var compatibleVoice = _options.GetCompatibleVoice(voiceName);
        foreach (var chunk in ChunkText(text, OpenAiMaximumChunkLength))
        {
            if (WasStopRequested())
            {
                return OperationResult.Stopped();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildOpenAiSpeechUri());
            request.Headers.Add("api-key", _options.AzureOpenAiKey);
            request.Content = JsonContent.Create(new
            {
                model = _options.AzureOpenAiDeployment,
                input = chunk,
                voice = compatibleVoice,
                response_format = "wav",
                speed = Math.Clamp(1.0 + (ratePercent / 100.0), 0.6, 1.5)
            });

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                return OperationResult.Fail(
                    $"Foundry TTS returned HTTP {(int)response.StatusCode}: {ExtractServiceError(responseBody)}");
            }

            await using var audioStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var bufferedAudio = new MemoryStream();
            await audioStream.CopyToAsync(bufferedAudio, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            bufferedAudio.Position = 0;
            var duration = GetWaveDuration(bufferedAudio);
            var completed = await PlayCloudAudioAsync(bufferedAudio, duration, cancellationToken);
            if (!completed || WasStopRequested())
            {
                return OperationResult.Stopped();
            }
        }

        return OperationResult.Ok("Speech complete.");
    }

    private async Task<OperationResult> SpeakWithElevenLabsAsync(
        string text,
        string voiceName,
        int ratePercent,
        CancellationToken cancellationToken)
    {
        var voiceResult = await GetElevenLabsVoicesAsync();
        if (!voiceResult.Success || voiceResult.Voices.Count == 0)
        {
            return OperationResult.Fail(voiceResult.Message);
        }

        var voiceId = voiceResult.Voices.FirstOrDefault(
            voice => voice.ShortName.Equals(voiceName, StringComparison.OrdinalIgnoreCase))?.ShortName
            ?? voiceResult.Voices[0].ShortName;

        foreach (var chunk in ChunkText(text, OpenAiMaximumChunkLength))
        {
            if (WasStopRequested())
            {
                return OperationResult.Stopped();
            }

            var endpoint = $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}" +
                           "?output_format=pcm_24000";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("xi-api-key", _options.ElevenLabsKey);
            request.Content = JsonContent.Create(new
            {
                text = chunk,
                model_id = "eleven_multilingual_v2",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75,
                    speed = Math.Clamp(1.0 + (ratePercent / 100.0), 0.7, 1.2)
                }
            });

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                return OperationResult.Fail(
                    $"Cloud voice returned HTTP {(int)response.StatusCode}: {ExtractServiceError(responseBody)}");
            }

            var pcmAudio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var waveAudio = CreateWaveStream(pcmAudio, sampleRate: 24_000, channels: 1, bitsPerSample: 16);
            var duration = TimeSpan.FromSeconds(pcmAudio.Length / 48_000d);
            var completed = await PlayCloudAudioAsync(waveAudio, duration, cancellationToken);
            if (!completed || WasStopRequested())
            {
                return OperationResult.Stopped();
            }
        }

        return OperationResult.Ok("Speech complete.");
    }

    public async Task StopAsync()
    {
        SpeechSynthesizer? synthesizer;
        SoundPlayer? soundPlayer;
        CancellationTokenSource? speechCancellation;
        lock (_syncRoot)
        {
            _stopRequested = true;
            synthesizer = _activeSynthesizer;
            soundPlayer = _activeSoundPlayer;
            speechCancellation = _speechCancellation;
        }

        speechCancellation?.Cancel();
        soundPlayer?.Stop();
        if (synthesizer is not null)
        {
            try
            {
                await synthesizer.StopSpeakingAsync();
            }
            catch (ObjectDisposedException)
            {
                // The current utterance completed while the stop command was dispatched.
            }
        }
    }

    public async Task<VoiceListResult> GetVoicesAsync()
    {
        if (!IsConfigured)
        {
            return VoiceListResult.Failed("No Azure speech provider is configured.");
        }

        if (_options.IsElevenLabsConfigured && !_options.IsAzureSpeechConfigured)
        {
            return await GetElevenLabsVoicesAsync();
        }

        if (!_options.IsAzureSpeechConfigured)
        {
            return new VoiceListResult(
                true,
                $"Loaded {OpenAiVoiceOptions.Length} Foundry TTS voices.",
                OpenAiVoiceOptions);
        }

        try
        {
            var config = CreateSpeechConfig(AppSettings.DefaultVoiceName);
            using var synthesizer = new SpeechSynthesizer(config, null);
            var result = await synthesizer.GetVoicesAsync();
            if (result.Reason != ResultReason.VoicesListRetrieved)
            {
                return VoiceListResult.Failed($"Azure Speech could not list voices: {result.Reason}.");
            }

            var voices = result.Voices
                .Where(voice => !string.IsNullOrWhiteSpace(voice.ShortName))
                .Select(voice => new VoiceOption(
                    voice.ShortName,
                    $"{voice.LocalName} · {voice.Locale} · {voice.Gender}",
                    voice.Locale))
                .OrderBy(voice => voice.Locale, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return new VoiceListResult(true, $"Loaded {voices.Length} voices.", voices);
        }
        catch (Exception ex)
        {
            return VoiceListResult.Failed($"Azure voices could not be loaded: {ex.Message}");
        }
    }

    private async Task<VoiceListResult> GetElevenLabsVoicesAsync()
    {
        if (_elevenLabsVoices is { Count: > 0 } cachedVoices)
        {
            return new VoiceListResult(true, $"Loaded {cachedVoices.Count} cloud voices.", cachedVoices);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
            request.Headers.Add("xi-api-key", _options.ElevenLabsKey);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                return VoiceListResult.Failed(
                    $"Cloud voices returned HTTP {(int)response.StatusCode}: {ExtractServiceError(responseBody)}");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(responseStream);
            if (!document.RootElement.TryGetProperty("voices", out var voiceArray))
            {
                return VoiceListResult.Failed("The cloud voice list had an unexpected format.");
            }

            var voices = voiceArray.EnumerateArray()
                .Select(voice =>
                {
                    var id = voice.TryGetProperty("voice_id", out var idElement)
                        ? idElement.GetString()
                        : null;
                    var name = voice.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;
                    var category = voice.TryGetProperty("category", out var categoryElement)
                        ? categoryElement.GetString()
                        : null;
                    return new { Id = id, Name = name, Category = category };
                })
                .Where(voice => !string.IsNullOrWhiteSpace(voice.Id))
                .Select(voice => new VoiceOption(
                    voice.Id!,
                    $"{voice.Name ?? "Unnamed voice"} · {voice.Category ?? "cloud"}",
                    "Multilingual"))
                .OrderBy(voice => voice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            _elevenLabsVoices = voices;
            return voices.Length == 0
                ? VoiceListResult.Failed("No cloud voices are available to this account.")
                : new VoiceListResult(true, $"Loaded {voices.Length} cloud voices.", voices);
        }
        catch (Exception ex)
        {
            return VoiceListResult.Failed($"Cloud voices could not be loaded: {ex.Message}");
        }
    }

    private SpeechConfig CreateSpeechConfig(string voiceName)
    {
        var config = !string.IsNullOrWhiteSpace(_options.Region)
            ? SpeechConfig.FromSubscription(_options.Key, _options.Region)
            : SpeechConfig.FromEndpoint(_options.Endpoint!, _options.Key);
        config.SpeechSynthesisVoiceName = string.IsNullOrWhiteSpace(voiceName)
            ? AppSettings.DefaultVoiceName
            : voiceName;
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm);
        return config;
    }

    private Uri BuildOpenAiSpeechUri()
    {
        var endpoint = _options.AzureOpenAiEndpoint
            ?? throw new InvalidOperationException("The Foundry TTS endpoint is missing.");
        var builder = new UriBuilder(endpoint);
        if (!builder.Path.Contains("/audio/speech", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = $"{builder.Path.TrimEnd('/')}/openai/deployments/" +
                           $"{Uri.EscapeDataString(_options.AzureOpenAiDeployment)}/audio/speech";
        }

        if (!builder.Query.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
        {
            var apiVersion = Uri.EscapeDataString(_options.AzureOpenAiApiVersion);
            builder.Query = string.IsNullOrWhiteSpace(builder.Query)
                ? $"api-version={apiVersion}"
                : builder.Query.TrimStart('?') + $"&api-version={apiVersion}";
        }

        return builder.Uri;
    }

    private static MemoryStream CreateWaveStream(byte[] pcmAudio, int sampleRate, short channels, short bitsPerSample)
    {
        var stream = new MemoryStream(capacity: pcmAudio.Length + 44);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var blockAlign = (short)(channels * (bitsPerSample / 8));
            var byteRate = sampleRate * blockAlign;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmAudio.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write(bitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmAudio.Length);
            writer.Write(pcmAudio);
        }

        stream.Position = 0;
        return stream;
    }

    private async Task<bool> PlayWaveAsync(
        Stream waveAudio,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var player = new SoundPlayer(waveAudio);
        player.Load();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_stopRequested || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            _activeSoundPlayer = player;
        }

        using var stopRegistration = cancellationToken.Register(
            static state => ((SoundPlayer)state!).Stop(),
            player);

        try
        {
            player.Play();
            await Task.Delay(duration + TimeSpan.FromMilliseconds(150), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            player.Stop();
            return false;
        }
        finally
        {
            player.Stop();
            lock (_syncRoot)
            {
                if (ReferenceEquals(_activeSoundPlayer, player))
                {
                    _activeSoundPlayer = null;
                }
            }
        }
    }

    private Task<bool> PlayCloudAudioAsync(
        Stream waveAudio,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        _playbackOverride is null
            ? PlayWaveAsync(waveAudio, duration, cancellationToken)
            : _playbackOverride(waveAudio, duration, cancellationToken);

    private static TimeSpan GetWaveDuration(Stream waveAudio)
    {
        var originalPosition = waveAudio.Position;
        try
        {
            waveAudio.Position = 0;
            using var reader = new BinaryReader(waveAudio, System.Text.Encoding.ASCII, leaveOpen: true);
            if (new string(reader.ReadChars(4)) != "RIFF")
            {
                return EstimateWaveDuration(waveAudio.Length);
            }

            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
            {
                return EstimateWaveDuration(waveAudio.Length);
            }

            var byteRate = 0;
            var dataLength = 0;
            while (waveAudio.Position + 8 <= waveAudio.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkLength = reader.ReadInt32();
                if (chunkLength < 0 || waveAudio.Position + chunkLength > waveAudio.Length)
                {
                    break;
                }

                if (chunkId == "fmt " && chunkLength >= 16)
                {
                    reader.ReadInt16();
                    reader.ReadInt16();
                    reader.ReadInt32();
                    byteRate = reader.ReadInt32();
                    waveAudio.Position += chunkLength - 12;
                }
                else if (chunkId == "data")
                {
                    dataLength = chunkLength;
                    waveAudio.Position += chunkLength;
                }
                else
                {
                    waveAudio.Position += chunkLength;
                }

                if ((chunkLength & 1) == 1 && waveAudio.Position < waveAudio.Length)
                {
                    waveAudio.Position++;
                }

                if (byteRate > 0 && dataLength > 0)
                {
                    return TimeSpan.FromSeconds(dataLength / (double)byteRate);
                }
            }

            return EstimateWaveDuration(waveAudio.Length);
        }
        catch (EndOfStreamException)
        {
            return EstimateWaveDuration(waveAudio.Length);
        }
        finally
        {
            waveAudio.Position = originalPosition;
        }
    }

    private static TimeSpan EstimateWaveDuration(long length) =>
        TimeSpan.FromSeconds(Math.Max(0.1, length / 48_000d));

    private static string ExtractServiceError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No error details were returned.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "Unknown service error.";
            }
        }
        catch (JsonException)
        {
            // Return a safely bounded plain-text response below.
        }

        return responseBody.Length <= 300 ? responseBody : responseBody[..300] + "…";
    }

    private bool WasStopRequested()
    {
        lock (_syncRoot)
        {
            return _stopRequested;
        }
    }

    private static string BuildSsml(string text, string voiceName, int ratePercent)
    {
        var safeText = SecurityElement.Escape(text) ?? string.Empty;
        var safeVoice = SecurityElement.Escape(
            string.IsNullOrWhiteSpace(voiceName) ? AppSettings.DefaultVoiceName : voiceName) ?? AppSettings.DefaultVoiceName;
        var rate = Math.Clamp(ratePercent, -40, 50).ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture);
        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
               $"<voice name='{safeVoice}'><prosody rate='{rate}%'>{safeText}</prosody></voice></speak>";
    }

    private static IEnumerable<string> ChunkText(string text, int maximumLength)
    {
        var remaining = text.Trim();
        while (remaining.Length > maximumLength)
        {
            var split = FindSplitPoint(remaining, maximumLength);
            yield return remaining[..split].Trim();
            remaining = remaining[split..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    private static int FindSplitPoint(string text, int maximumLength)
    {
        var minimum = maximumLength / 2;
        for (var index = maximumLength; index >= minimum; index--)
        {
            if (text[index - 1] is '.' or '!' or '?' or '\n')
            {
                return index;
            }
        }

        for (var index = maximumLength; index >= minimum; index--)
        {
            if (char.IsWhiteSpace(text[index - 1]))
            {
                return index;
            }
        }

        return maximumLength;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _speechCancellation?.Cancel();
        _activeSoundPlayer?.Stop();
        _speechCancellation?.Dispose();
        _speechGate.Dispose();
        _disposed = true;
    }
}