using Rox.FlowEngine;

namespace Rox.Security;

/// <summary>Tests a seed-key module against a sample seed (FR-06.2 configuration self-test).</summary>
public static class SecurityModuleTester
{
    public sealed record TestOutcome(bool Success, byte[] Key, string Message);

    public static TestOutcome Test(ISecurityProvider provider, byte[] sampleSeed)
    {
        try
        {
            var key = provider.ComputeKey(sampleSeed, sampleSeed.Length);
            return key.Length == 0
                ? new TestOutcome(false, key, "Module returned an empty key.")
                : new TestOutcome(true, key, $"Module returned a {key.Length}-byte key for the sample seed.");
        }
        catch (Exception ex)
        {
            return new TestOutcome(false, Array.Empty<byte>(), $"Module call failed: {ex.Message}");
        }
    }
}
