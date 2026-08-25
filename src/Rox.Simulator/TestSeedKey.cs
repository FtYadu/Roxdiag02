namespace Rox.Simulator;

/// <summary>
/// TEST-ONLY key derivation for the bundled simulator. This is NOT an OEM algorithm and carries
/// no security value — it exists solely so the SecurityAccess handshake completes against the sim.
/// Real vehicles require the operator's licensed seed-key module via ISecurityProvider.
/// </summary>
public static class TestSeedKey
{
    public static byte[] Compute(byte[] seed)
    {
        var key = new byte[seed.Length];
        for (int i = 0; i < seed.Length; i++) key[i] = (byte)((seed[i] ^ 0x5A) + 1);
        return key;
    }
}

public sealed class TestSecurityProvider : Rox.FlowEngine.ISecurityProvider
{
    public byte[] ComputeKey(byte[] seed, int length) => TestSeedKey.Compute(seed);
}
