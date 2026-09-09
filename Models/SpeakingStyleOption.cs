namespace Sonja.ReadAloud.Models;

public sealed record SpeakingStyleOption(string Value, string DisplayName)
{
    public static readonly SpeakingStyleOption Natural = new(string.Empty, "Natural (no style)");

    // Azure neural "express-as" styles that are broadly available on expressive en-US voices
    // such as Aria, Jenny, Sara and Nancy. Voices that don't support a style ignore it.
    public static IReadOnlyList<SpeakingStyleOption> Catalog { get; } = new[]
    {
        Natural,
        new SpeakingStyleOption("chat", "Casual chat"),
        new SpeakingStyleOption("friendly", "Friendly"),
        new SpeakingStyleOption("cheerful", "Cheerful"),
        new SpeakingStyleOption("excited", "Excited"),
        new SpeakingStyleOption("hopeful", "Hopeful"),
        new SpeakingStyleOption("empathetic", "Empathetic"),
        new SpeakingStyleOption("newscast", "Newsreader"),
        new SpeakingStyleOption("narration-professional", "Narration (professional)"),
        new SpeakingStyleOption("customerservice", "Customer service"),
        new SpeakingStyleOption("assistant", "Assistant"),
        new SpeakingStyleOption("sad", "Sad"),
        new SpeakingStyleOption("angry", "Angry"),
        new SpeakingStyleOption("terrified", "Terrified"),
        new SpeakingStyleOption("shouting", "Shouting"),
        new SpeakingStyleOption("whispering", "Whispering"),
        new SpeakingStyleOption("unfriendly", "Unfriendly"),
    };
}
