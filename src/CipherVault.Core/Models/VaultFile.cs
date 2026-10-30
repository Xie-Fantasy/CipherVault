namespace CipherVault.Core.Models;

public sealed class VaultFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public byte[] Salt { get; set; } = Array.Empty<byte>();
    public int Argon2MemoryKB { get; set; } = 65536;
    public int Argon2Iterations { get; set; } = 3;
    public int Argon2Parallelism { get; set; } = 4;
    public uint FailedAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public byte[] RecoveryKeyBlock { get; set; } = Array.Empty<byte>();
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();
}
