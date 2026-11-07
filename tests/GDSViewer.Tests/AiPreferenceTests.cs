using GDSViewer.Ai;
using GDSViewer.Models;
using Microsoft.JSInterop;

namespace GDSViewer.Tests;

///
///The two AI settings kept in the browser - which way the microphone writes speech out, and how many rounds of tool
///calls a message may take - read by AI settings and the chat alike, as a page comes up, one after the other and before
///the browser has answered the first. Both are told what was kept, from one read, and a choice made while the read is
///out is kept over it (2026-11-07, from GraphDBViewer's audit): the first ask used to mark a setting loaded before the
///storage had answered, so the second reader was told the default and a fresh choice was overwritten by the stale read.
///
public class AiPreferenceTests
{
    ///<summary>The browser's half of AppStorage with its IndexedDB read answered when the test lets it, as a real one is.</summary>
    private sealed class SlowBrowser : IJSRuntime
    {
        private readonly TaskCompletionSource<string?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads { get; private set; }

        public List<(string Key, string Value)> Written { get; } = new();

        public void Answer(string? value)
        {
            read.SetResult(value);
        }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            args ??= Array.Empty<object?>();

            switch (identifier)
            {
                case "gdsStorage.available":
                    return (TValue)(object)true;

                case "gdsStorage.get":
                    Reads++;

                    return (TValue)(object?)await read.Task!;

                case "gdsStorage.set":
                    Written.Add(((string)args[0]!, (string)args[1]!));

                    return (TValue)(object)true;
            }

            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }
    }

    [Fact]
    public async Task The_speech_setting_read_twice_before_the_browser_answers_is_what_was_kept_both_times()
    {
        var browser = new SlowBrowser();
        var preference = new SpeechPreference(new AppStorage(browser));

        var settings = preference.GetAsync();
        var chat = preference.GetAsync();

        browser.Answer(nameof(SpeechMode.Provider));

        Assert.Equal(SpeechMode.Provider, await settings.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SpeechMode.Provider, await chat.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, browser.Reads);
        Assert.Equal(SpeechMode.Provider, await preference.GetAsync());
    }

    [Fact]
    public async Task The_rounds_read_twice_before_the_browser_answers_are_what_was_kept_both_times()
    {
        var browser = new SlowBrowser();
        var rounds = new ToolRoundsPreference(new AppStorage(browser));

        var settings = rounds.GetAsync();
        var chat = rounds.GetAsync();

        browser.Answer("7");

        Assert.Equal(7, await settings.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(7, await chat.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, browser.Reads);
        Assert.Equal(7, await rounds.GetAsync());
    }

    ///<summary>A number typed into AI settings while the read is out is newer than what the read brings back, and is kept over it.</summary>
    [Fact]
    public async Task A_number_set_while_the_read_is_out_is_kept_over_what_was_read()
    {
        var browser = new SlowBrowser();
        var rounds = new ToolRoundsPreference(new AppStorage(browser));

        var settings = rounds.GetAsync();

        Assert.Equal(7, await rounds.SetAsync(7));
        Assert.Contains(browser.Written, written => written.Key == ToolRoundsPreference.StorageKey);

        browser.Answer("30");

        Assert.Equal(7, await settings.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(7, await rounds.GetAsync());
    }
}
