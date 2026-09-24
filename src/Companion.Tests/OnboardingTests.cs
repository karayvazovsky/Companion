using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class OnboardingTests
{
    private sealed class Rig : IDisposable
    {
        public TempDir Tmp { get; } = new();
        public PetStateStore Pet { get; }
        public SecretStore Secrets { get; }
        public Exception? ValidationError { get; set; }
        public List<string> ValidatedKeys { get; } = new();

        public Rig()
        {
            Pet = new PetStateStore(Tmp.PathOf("pet.json"), FakeClock.At(2026, 9, 24, 12), NullLog.Instance);
            Secrets = new SecretStore(Tmp.PathOf("secrets.bin"), new DpapiProtector(), NullLog.Instance);
        }

        public OnboardingFlow Flow() => new(Pet, Secrets, (key, _) =>
        {
            ValidatedKeys.Add(key);
            return ValidationError is null ? Task.CompletedTask : Task.FromException(ValidationError);
        }, "Ctrl+Alt+Space");

        public void Dispose() => Tmp.Dispose();
    }

    [Fact]
    public async Task Walks_through_names_and_key()
    {
        using var rig = new Rig();
        var flow = rig.Flow();
        Assert.Equal(OnboardingStep.PetName, flow.Step);
        Assert.False(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets));

        Assert.Null(await flow.SubmitAsync(" Pip ", CancellationToken.None));
        Assert.Equal(OnboardingStep.UserName, flow.Step);
        Assert.Contains("Pip", flow.Prompt);

        Assert.Null(await flow.SubmitAsync("Karco", CancellationToken.None));
        Assert.Equal(OnboardingStep.ApiKey, flow.Step);
        Assert.Contains("console.anthropic.com", flow.Prompt);

        Assert.Null(await flow.SubmitAsync("sk-ant-good", CancellationToken.None));
        Assert.Equal(OnboardingStep.Done, flow.Step);
        Assert.Contains("Ctrl+Alt+Space", flow.Prompt);
        Assert.Equal("sk-ant-good", rig.Secrets.LoadApiKey());
        Assert.Equal("Pip", rig.Pet.Current.PetName);
        Assert.True(OnboardingFlow.IsComplete(rig.Pet, rig.Secrets));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A name that is far too long")]
    public async Task Rejects_bad_names(string name)
    {
        using var rig = new Rig();
        var flow = rig.Flow();

        Assert.NotNull(await flow.SubmitAsync(name, CancellationToken.None));
        Assert.Equal(OnboardingStep.PetName, flow.Step);
    }

    [Fact]
    public async Task Rejects_keys_that_do_not_look_right_or_fail_validation()
    {
        using var rig = new Rig();
        rig.Pet.Current.PetName = "Pip";
        rig.Pet.Current.UserName = "Karco";
        var flow = rig.Flow();
        Assert.Equal(OnboardingStep.ApiKey, flow.Step);

        Assert.NotNull(await flow.SubmitAsync("hello", CancellationToken.None));
        Assert.Empty(rig.ValidatedKeys);

        rig.ValidationError = new ClaudeException(ClaudeErrorKind.Unauthorized, "401");
        Assert.Contains("didn't work", await flow.SubmitAsync("sk-ant-bad", CancellationToken.None));

        rig.ValidationError = new ClaudeException(ClaudeErrorKind.Network, "offline");
        Assert.Contains("Network", await flow.SubmitAsync("sk-ant-bad", CancellationToken.None));

        Assert.Equal(OnboardingStep.ApiKey, flow.Step);
        Assert.False(rig.Secrets.HasKey);
    }
}
