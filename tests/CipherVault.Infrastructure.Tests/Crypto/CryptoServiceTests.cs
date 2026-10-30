namespace CipherVault.Infrastructure.Tests.Crypto;

using System.Text;
using CipherVault.Core.Exceptions;
using CipherVault.Infrastructure.Crypto;
using FluentAssertions;
using Xunit;

public sealed class CryptoServiceTests
{
    private readonly CryptoService _sut = new();

    [Fact]
    public void DeriveRecoveryKey_WithBip39Vector_ReturnsFirst256BitsOfStandardSeed()
    {
        string[] mnemonic =
        [
            "abandon", "abandon", "abandon", "abandon", "abandon", "abandon",
            "abandon", "abandon", "abandon", "abandon", "abandon", "about"
        ];

        byte[] key = _sut.DeriveRecoveryKey(mnemonic);

        key.Should().Equal(Convert.FromHexString(
            "5EB00BBDDCF069084889A8AB9155568165F5C453CCB85E70811AAED6F6DA5FC1"));
    }

    [Fact]
    public void EntryPasswordCrypto_BindsCiphertextToEntryId_AndReadsLegacyBlob()
    {
        byte[] vaultKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] plaintext = Encoding.UTF8.GetBytes("entry-bound-password");

        var encrypted = EntryPasswordCrypto.Encrypt(_sut, vaultKey, "entry-a", plaintext);
        byte[] decrypted = EntryPasswordCrypto.Decrypt(
            _sut, vaultKey, "entry-a", encrypted.ciphertext, encrypted.nonce, encrypted.tag);
        Action wrongEntry = () => EntryPasswordCrypto.Decrypt(
            _sut, vaultKey, "entry-b", encrypted.ciphertext, encrypted.nonce, encrypted.tag);

        decrypted.Should().Equal(plaintext);
        wrongEntry.Should().Throw<VaultCorruptedException>();

        var legacy = _sut.Encrypt(vaultKey, plaintext);
        byte[] legacyDecrypted = EntryPasswordCrypto.Decrypt(
            _sut, vaultKey, "entry-a", legacy.ciphertext, legacy.nonce, legacy.tag);
        legacyDecrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void EncryptThenDecrypt_WithValidInputs_ReturnsOriginalPlaintext()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] plaintext = Encoding.UTF8.GetBytes("CipherVault AES-GCM round trip 密码");

        var (ciphertext, nonce, tag) = _sut.Encrypt(key, plaintext);
        byte[] decrypted = _sut.Decrypt(key, ciphertext, nonce, tag);

        nonce.Should().HaveCount(12);
        tag.Should().HaveCount(16);
        ciphertext.Should().NotEqual(plaintext);
        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsVaultCorruptedException()
    {
        byte[] key = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        byte[] plaintext = Encoding.UTF8.GetBytes("sensitive payload");
        var (ciphertext, nonce, tag) = _sut.Encrypt(key, plaintext);
        ciphertext[0] ^= 0x80;

        Action decrypt = () => _sut.Decrypt(key, ciphertext, nonce, tag);

        decrypt.Should()
            .Throw<VaultCorruptedException>()
            .WithMessage("*authentication tag mismatch*");
    }

    [Fact]
    public void Decrypt_WithTamperedAuthenticationTag_ThrowsVaultCorruptedException()
    {
        byte[] key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        byte[] plaintext = Encoding.UTF8.GetBytes("another sensitive payload");
        var (ciphertext, nonce, tag) = _sut.Encrypt(key, plaintext);
        tag[^1] ^= 0x01;

        Action decrypt = () => _sut.Decrypt(key, ciphertext, nonce, tag);

        decrypt.Should().Throw<VaultCorruptedException>();
    }
}
