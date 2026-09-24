using System.Text;
using System.Windows.Threading;
using Companion.Core;

namespace Companion;

/// <summary>Connects the bubble to the Brain and the pet. Brain callbacks arrive on background threads.</summary>
public sealed class ChatController : IBrainListener
{
    private readonly ChatBubble _bubble;
    private readonly PetController _pet;
    private readonly Func<Brain?> _brain;
    private readonly Func<string> _petName;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _slowTimer;
    private readonly DispatcherTimer _stallTimer;
    private readonly StringBuilder _answer = new();
    private CancellationTokenSource? _turnCts;
    private string? _question;
    private string? _notice;
    private bool _dirty;
    private bool _canThinkHarder;
    private OnboardingFlow? _onboarding;

    public ChatController(ChatBubble bubble, PetController pet, Func<Brain?> brain, Func<string> petName, ILog log)
    {
        _bubble = bubble;
        _pet = pet;
        _brain = brain;
        _petName = petName;
        _log = log;
        _ui = bubble.Dispatcher;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(60) };
        _renderTimer.Tick += (_, _) => RenderIfDirty();
        _slowTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromSeconds(8) };
        _slowTimer.Tick += (_, _) =>
        {
            _slowTimer.Stop();
            _bubble.SetStatus("hmm, one sec…");
        };
        _stallTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = StallTimeout };
        _stallTimer.Tick += (_, _) =>
        {
            _stallTimer.Stop();
            if (_turnCts is null) return;
            _log.Info($"No progress for {StallTimeout.TotalSeconds:0} s; cancelling the turn.");
            _turnCts.Cancel();
        };
        bubble.Submitted += OnSubmitted;
        bubble.ThinkHarderClicked += OnThinkHarder;
        bubble.NewChatClicked += OnNewChat;
    }

    /// <summary>A turn with no stream progress for this long is cancelled.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(90);

    public event Action? OnboardingCompleted;

    public bool IsOpen => _bubble.IsVisible;

    public void Toggle()
    {
        if (_bubble.IsVisible) _bubble.Hide();
        else Open();
    }

    public void Open()
    {
        _bubble.SetTitle(_petName());
        if (_onboarding is not null && _onboarding.Refresh() == OnboardingStep.Done)
        {
            // The missing piece (usually the key) was saved elsewhere, e.g. in Settings.
            _onboarding = null;
            _bubble.SetSecretMode(false);
            OnboardingCompleted?.Invoke();
        }
        var chatting = _onboarding is null;
        _bubble.SetConversationButtons(chatting, chatting && _canThinkHarder && !(_brain()?.Busy ?? false));
        if (chatting && _answer.Length == 0 && _question is null)
        {
            _bubble.ShowContent(null, "*wags* What's up?", null);
        }
        _bubble.ShowAt(_pet.Bounds);
    }

    public void StartOnboarding(OnboardingFlow flow)
    {
        _onboarding = flow;
        _answer.Clear();
        _question = null;
        _notice = null;
        _canThinkHarder = false;
        ShowOnboardingPrompt(null);
        Open();
    }

    private void ShowOnboardingPrompt(string? error)
    {
        _bubble.SetSecretMode(_onboarding!.Step == OnboardingStep.ApiKey);
        _bubble.ShowContent(null, _onboarding.Prompt, error);
    }

    private async void OnSubmitted(string text)
    {
        if (_onboarding is { } flow)
        {
            await SubmitOnboardingAsync(flow, text);
            return;
        }
        await RunAsync(text, (brain, listener, ct) => brain.AskAsync(text, listener, ct));
    }

    private async Task SubmitOnboardingAsync(OnboardingFlow flow, string text)
    {
        _bubble.SetBusy(true);
        _bubble.SetStatus(flow.Step == OnboardingStep.ApiKey ? "checking the key…" : null);
        string? error;
        try
        {
            error = await flow.SubmitAsync(text, CancellationToken.None);
        }
        finally
        {
            _bubble.SetBusy(false);
            _bubble.SetStatus(null);
        }

        _bubble.SetTitle(_petName());
        if (flow.Step == OnboardingStep.Done)
        {
            _onboarding = null;
            _bubble.SetSecretMode(false);
            _bubble.ShowContent(null, flow.Prompt, null);
            _bubble.SetConversationButtons(true, false);
            OnboardingCompleted?.Invoke();
        }
        else
        {
            ShowOnboardingPrompt(error);
        }
        _bubble.FocusInput();
    }

    private async void OnThinkHarder() =>
        await RunAsync("🧠 Think harder", (brain, listener, ct) => brain.ThinkHarderAsync(listener, ct));

    private void OnNewChat()
    {
        if (_turnCts is not null)
        {
            // While a turn runs the button reads "Stop".
            _turnCts.Cancel();
            return;
        }
        _brain()?.NewChat();
        _answer.Clear();
        _question = null;
        _notice = null;
        _canThinkHarder = false;
        _bubble.ShowContent(null, "*fresh start!* What's up?", null);
        _bubble.SetConversationButtons(true, false);
        _bubble.FocusInput();
    }

    private async Task RunAsync(string question, Func<Brain, IBrainListener, CancellationToken, Task<BrainReply>> call)
    {
        var brain = _brain();
        if (brain is null)
        {
            _bubble.ShowContent(null, "I need an API key before I can think. Right-click me → **Settings**.", null);
            return;
        }
        if (brain.Busy) return;

        _question = question;
        _answer.Clear();
        _notice = null;
        _dirty = true;
        _canThinkHarder = false;
        _bubble.SetBusy(true);
        _renderTimer.Start();
        _turnCts = new CancellationTokenSource();
        _stallTimer.Start();
        BrainReply reply;
        try
        {
            reply = await call(brain, this, _turnCts.Token);
        }
        catch (Exception ex)
        {
            _log.Error("Chat turn failed.", ex);
            reply = new BrainReply(BrainOutcome.Error, _answer.ToString(), "Something went wrong (details are in data\\logs).");
        }
        finally
        {
            _slowTimer.Stop();
            _stallTimer.Stop();
            _turnCts.Dispose();
            _turnCts = null;
            _renderTimer.Stop();
            _bubble.SetBusy(false);
            _bubble.SetStatus(null);
        }

        _answer.Clear().Append(reply.Text);
        _notice = reply.Notice;
        _dirty = true;
        RenderIfDirty();
        _canThinkHarder = reply.Outcome is BrainOutcome.Ok or BrainOutcome.CutOff;
        _bubble.SetConversationButtons(true, _canThinkHarder);
        _pet.Life.OnChatFinished(reply.Outcome == BrainOutcome.Ok);
        _pet.Refresh();
        _bubble.FocusInput();
    }

    private void RenderIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        _bubble.ShowContent(_question, _answer.ToString(), _notice);
    }

    public void OnThinking() => _ui.InvokeAsync(() =>
    {
        _pet.Life.OnChatStarted();
        _pet.Refresh();
        _bubble.SetStatus("thinking…");
        _slowTimer.Start();
        RestartStallWatch();
    });

    public void OnFirstText() => _ui.InvokeAsync(() =>
    {
        _pet.Life.OnFirstToken();
        _pet.Refresh();
        _slowTimer.Stop();
        _bubble.SetStatus(null);
        RestartStallWatch();
    });

    public void OnText(string delta) => _ui.InvokeAsync(() =>
    {
        _answer.Append(delta);
        _dirty = true;
        RestartStallWatch();
    });

    public void OnStatus(string status) => _ui.InvokeAsync(() =>
    {
        _bubble.SetStatus(status);
        RestartStallWatch();
    });

    /// <summary>Any sign of life from the stream pushes the stall watchdog back. UI thread only.</summary>
    private void RestartStallWatch()
    {
        if (_turnCts is null) return;
        _stallTimer.Stop();
        _stallTimer.Start();
    }
}
