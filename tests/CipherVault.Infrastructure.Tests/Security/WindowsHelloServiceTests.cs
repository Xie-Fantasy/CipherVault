namespace CipherVault.Infrastructure.Tests.Security;

using System.Security.Cryptography;
using CipherVault.Infrastructure.Security;
using CipherVault.Infrastructure.Tests.TestSupport;
using FluentAssertions;
using Xunit;

public sealed class WindowsHelloServiceTests
{
    [Fact]
    public async Task ConstructorPurgesLegacyDpapiBlob()
    {
        using var temp = new TemporaryDirectory();
        string keyDirectory = temp.GetPath("hello_keys");
        Directory.CreateDirectory(keyDirectory);
        string legacyBlob = Path.Combine(keyDirectory, "legacy.dat");
        await File.WriteAllBytesAsync(legacyBlob, Enumerable.Repeat((byte)0xA5, 128).ToArray());

        _ = new WindowsHelloService(keyDirectory, new FakeHelloKeyProtector());

        File.Exists(legacyBlob).Should().BeFalse();
    }

    [Fact]
    public async Task StoreAndRetrieveRoundTripsWithoutWritingPlaintextKey()
    {
        using var temp = new TemporaryDirectory();
        string keyDirectory = temp.GetPath("hello_keys");
        var protector = new FakeHelloKeyProtector();
        var sut = new WindowsHelloService(keyDirectory, protector);
        byte[] masterKey = RandomNumberGenerator.GetBytes(32);
        string vaultPath = temp.GetPath("personal.cvault");

        await sut.StoreKeyAsync(vaultPath, masterKey);

        sut.HasStoredKey(vaultPath).Should().BeTrue();
        string storedBlob = Directory.EnumerateFiles(keyDirectory, "*.hello").Should().ContainSingle().Subject;
        byte[] storedBytes = await File.ReadAllBytesAsync(storedBlob);
        storedBytes.AsSpan().IndexOf(masterKey).Should().Be(-1);
        (await sut.RetrieveKeyAsync(vaultPath)).Should().Equal(masterKey);
    }

    [Fact]
    public async Task BindingsAreScopedByCanonicalVaultPathAndCanBeRemoved()
    {
        using var temp = new TemporaryDirectory();
        var protector = new FakeHelloKeyProtector();
        var sut = new WindowsHelloService(temp.GetPath("hello_keys"), protector);
        string firstVault = temp.GetPath("first.cvault");
        string secondVault = temp.GetPath("second.cvault");

        await sut.StoreKeyAsync(firstVault, RandomNumberGenerator.GetBytes(32));

        sut.HasStoredKey(firstVault).Should().BeTrue();
        sut.HasStoredKey(secondVault).Should().BeFalse();
        (await sut.RetrieveKeyAsync(secondVault)).Should().BeNull();

        sut.RemoveStoredKey(firstVault);
        sut.HasStoredKey(firstVault).Should().BeFalse();
        (await sut.RetrieveKeyAsync(firstVault)).Should().BeNull();
    }

    [Fact]
    public async Task UnsupportedDeviceCannotStoreKey()
    {
        using var temp = new TemporaryDirectory();
        var sut = new WindowsHelloService(
            temp.GetPath("hello_keys"),
            new FakeHelloKeyProtector(isSupported: false));

        Func<Task> action = () => sut.StoreKeyAsync("personal.cvault", new byte[32]);

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task StoreRejectsInvalidVaultKeyLength(int length)
    {
        using var temp = new TemporaryDirectory();
        var sut = new WindowsHelloService(
            temp.GetPath("hello_keys"),
            new FakeHelloKeyProtector());

        Func<Task> action = () => sut.StoreKeyAsync("personal.cvault", new byte[length]);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CorruptBlobReturnsNullWithoutInvokingPrivateKey()
    {
        using var temp = new TemporaryDirectory();
        string keyDirectory = temp.GetPath("hello_keys");
        var protector = new FakeHelloKeyProtector();
        var sut = new WindowsHelloService(keyDirectory, protector);
        string vaultPath = temp.GetPath("personal.cvault");
        await sut.StoreKeyAsync(vaultPath, RandomNumberGenerator.GetBytes(32));
        string storedBlob = Directory.EnumerateFiles(keyDirectory, "*.hello").Single();
        await File.WriteAllBytesAsync(storedBlob, "not-a-valid-blob"u8.ToArray());

        (await sut.RetrieveKeyAsync(vaultPath)).Should().BeNull();
    }

    private sealed class FakeHelloKeyProtector(bool isSupported = true) : IWindowsHelloKeyProtector
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<bool> IsSupportedAsync() => Task.FromResult(isSupported);

        public byte[] Protect(string keyName, ReadOnlySpan<byte> plaintext)
        {
            _keys.Add(keyName);
            byte[] result = new byte[plaintext.Length];
            for (int index = 0; index < plaintext.Length; index++)
                result[index] = (byte)(plaintext[plaintext.Length - index - 1] ^ 0xA5);
            return result;
        }

        public byte[] Unprotect(string keyName, ReadOnlySpan<byte> ciphertext)
        {
            if (!_keys.Contains(keyName))
                throw new CryptographicException("Key not found.");

            byte[] result = new byte[ciphertext.Length];
            for (int index = 0; index < ciphertext.Length; index++)
                result[index] = (byte)(ciphertext[ciphertext.Length - index - 1] ^ 0xA5);
            return result;
        }

        public bool KeyExists(string keyName) => _keys.Contains(keyName);

        public void DeleteKey(string keyName) => _keys.Remove(keyName);
    }
}
