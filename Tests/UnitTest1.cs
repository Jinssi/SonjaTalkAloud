using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Input;
using Sonja.ReadAloud.Models;
using Sonja.ReadAloud.Services;

namespace Sonja.ReadAloud.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void DefaultHotkeyIsSafeAndReadable()
    {
        var hotkey = new AppSettings().Hotkey;

        Assert.True(hotkey.IsValid);
        Assert.Equal("Ctrl + Alt + R", hotkey.DisplayName);
    }

    [Fact]
    public void HotkeyWithoutModifierIsRejected()
    {
        var hotkey = new HotkeyDefinition { Key = Key.R };

        Assert.False(hotkey.IsValid);
    }

    [Fact]
    public void ElevenLabsKeepsAppConfiguredWhenAzureKeysAreUnavailable()
    {
        var environment = new Dictionary<string, string>
        {
            ["SPEECH_REGION"] = "westeurope",
            ["ELEVENLABS_API_KEY"] = "test-key"
        };

        var options = SpeechConnectionOptions.FromEnvironment(environment);

        Assert.True(options.IsConfigured);
        Assert.False(options.IsAzureSpeechConfigured);
        Assert.True(options.IsElevenLabsConfigured);
        Assert.Contains("ElevenLabs", options.ConnectionSummary);
    }

    [Fact]
    public void AzureSpeechHasProviderPriorityWhenItsKeyExists()
    {
        var environment = new Dictionary<string, string>
        {
            ["SPEECH_KEY"] = "speech-key",
            ["SPEECH_REGION"] = "westeurope",
            ["ELEVENLABS_API_KEY"] = "fallback-key"
        };

        var options = SpeechConnectionOptions.FromEnvironment(environment);

        Assert.True(options.IsAzureSpeechConfigured);
        Assert.StartsWith("Azure Speech", options.ConnectionSummary);
    }

    [Fact]
    public async Task StopAsyncCancelsCloudPlaybackMidSentence()
    {
        var options = new SpeechConnectionOptions
        {
            Key = string.Empty,
            ElevenLabsKey = "test-key"
        };
        using var httpClient = new HttpClient(new FakeSpeechHandler());
        var playbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> SimulateLongPlayback(
            Stream audio,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            playbackStarted.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        using var service = new SpeechService(options, httpClient, SimulateLongPlayback);
        var speakingTask = service.SpeakAsync(
            "This sentence is intentionally long enough to be stopped during playback.",
            "voice-1",
            0);

        await playbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(service.IsSpeaking);

        var stopwatch = Stopwatch.StartNew();
        await service.StopAsync();
        var result = await speakingTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.WasStopped);
        Assert.False(result.Success);
        Assert.False(service.IsSpeaking);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    private sealed class FakeSpeechHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"voices\":[{\"voice_id\":\"voice-1\",\"name\":\"Test voice\",\"category\":\"premade\"}]}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[480_000])
            });
        }
    }
}