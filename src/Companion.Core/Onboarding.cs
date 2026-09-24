namespace Companion.Core;

public enum OnboardingStep { PetName, UserName, ApiKey, Done }

/// <summary>The hatch conversation: pet name → user name → API key. Resumes at the first missing piece.</summary>
public sealed class OnboardingFlow
{
    public const int MaxNameLength = 20;

    private readonly PetStateStore _pet;
    private readonly SecretStore _secrets;
    private readonly Func<string, CancellationToken, Task> _validateKey;
    private readonly string _hotkey;

    public OnboardingFlow(PetStateStore pet, SecretStore secrets, Func<string, CancellationToken, Task> validateKey, string hotkey)
    {
        _pet = pet;
        _secrets = secrets;
        _validateKey = validateKey;
        _hotkey = hotkey;
        Step = NextStep();
    }

    public OnboardingStep Step { get; private set; }

    /// <summary>Re-derives the step from the stores, e.g. after the key was saved in Settings.</summary>
    public OnboardingStep Refresh() => Step = NextStep();

    public static bool IsComplete(PetStateStore pet, SecretStore secrets) =>
        pet.Current.PetName is not null && pet.Current.UserName is not null && secrets.HasKey;

    public string Prompt => Step switch
    {
        OnboardingStep.PetName => "*blinks* Hi! I just hatched. What should my name be?",
        OnboardingStep.UserName => $"**{_pet.Current.PetName}**! I love it. And what should I call you?",
        OnboardingStep.ApiKey =>
            $"Nice to meet you, {_pet.Current.UserName}! Last thing: I need an Anthropic API key to think.\n\n" +
            "1. Open **console.anthropic.com** → **API Keys**\n" +
            "2. Create a key and paste it here (it is stored encrypted on this PC only).",
        _ => $"All set! Double-click me or press **{_hotkey}** whenever you need me. *wags*",
    };

    public async Task<string?> SubmitAsync(string input, CancellationToken ct)
    {
        input = input.Trim();
        switch (Step)
        {
            case OnboardingStep.PetName:
            case OnboardingStep.UserName:
                if (input.Length is 0 or > MaxNameLength) return $"Please pick a name between 1 and {MaxNameLength} characters.";
                if (Step == OnboardingStep.PetName) _pet.Current.PetName = input;
                else _pet.Current.UserName = input;
                _pet.Save();
                break;

            case OnboardingStep.ApiKey:
                if (!input.StartsWith("sk-", StringComparison.Ordinal))
                    return "That doesn't look like an API key (they start with **sk-**). Try again?";
                try
                {
                    await _validateKey(input, ct);
                }
                catch (ClaudeException ex) when (ex.Kind == ClaudeErrorKind.Unauthorized)
                {
                    return "Hmm, that key didn't work. Double-check it and paste it again?";
                }
                catch (ClaudeException ex) when (ex.Kind == ClaudeErrorKind.NoAccess)
                {
                    return Brain.NoAccessMessage + " Paste a different key?";
                }
                catch (ClaudeException ex)
                {
                    return $"I couldn't check the key right now ({ex.Kind}). Try again in a moment?";
                }
                _secrets.SaveApiKey(input);
                break;

            default:
                return null;
        }

        Step = NextStep();
        return null;
    }

    private OnboardingStep NextStep() =>
        _pet.Current.PetName is null ? OnboardingStep.PetName
        : _pet.Current.UserName is null ? OnboardingStep.UserName
        : !_secrets.HasKey ? OnboardingStep.ApiKey
        : OnboardingStep.Done;
}
