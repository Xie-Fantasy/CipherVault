namespace CipherVault.Infrastructure.Crypto;

using Konscious.Security.Cryptography;

/// <summary>
/// Encapsulates Argon2id key derivation using the Konscious.Security.Cryptography library.
/// Provides a static helper for deriving 256-bit keys from passwords with configurable
/// memory, iteration, and parallelism parameters.
/// </summary>
public static class Argon2KeyDeriver
{
    public const int MinimumMemoryKiB = 8192;
    public const int MaximumMemoryKiB = 262144;
    public const int MinimumIterations = 1;
    public const int MaximumIterations = 10;
    public const int MinimumParallelism = 1;
    public const int MaximumParallelism = 16;

    /// <summary>
    /// Derives a 256-bit (32-byte) key from the given password and salt using Argon2id.
    /// </summary>
    /// <param name="password">The password bytes to derive from.</param>
    /// <param name="salt">The salt bytes. Should be at least 16 bytes for security.</param>
    /// <param name="memoryKB">Memory usage in KiB. Default is 65536 (64 MiB).</param>
    /// <param name="iterations">Number of iterations (time cost). Default is 3.</param>
    /// <param name="parallelism">Degree of parallelism (lanes). Default is 4.</param>
    /// <returns>A 32-byte (256-bit) derived key.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="password"/> or <paramref name="salt"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="memoryKB"/>, <paramref name="iterations"/>,
    /// or <paramref name="parallelism"/> is out of valid range.
    /// </exception>
    public static byte[] DeriveKey(
        byte[] password,
        byte[] salt,
        int memoryKB = 65536,
        int iterations = 3,
        int parallelism = 4)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        if (salt.Length < 16)
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        if (memoryKB is < MinimumMemoryKiB or > MaximumMemoryKiB)
            throw new ArgumentOutOfRangeException(
                nameof(memoryKB),
                $"Memory must be between {MinimumMemoryKiB} and {MaximumMemoryKiB} KiB.");
        if (iterations is < MinimumIterations or > MaximumIterations)
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"Iterations must be between {MinimumIterations} and {MaximumIterations}.");
        if (parallelism is < MinimumParallelism or > MaximumParallelism)
            throw new ArgumentOutOfRangeException(
                nameof(parallelism),
                $"Parallelism must be between {MinimumParallelism} and {MaximumParallelism}.");

        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memoryKB,
        };

        return argon2.GetBytes(32);
    }
}
