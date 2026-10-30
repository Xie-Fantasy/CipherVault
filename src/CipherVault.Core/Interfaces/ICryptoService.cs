namespace CipherVault.Core.Interfaces;

public interface ICryptoService
{
    byte[] DeriveKey(byte[] password, byte[] salt, int memoryKB, int iterations, int parallelism);
    byte[] DeriveRecoveryKey(string[] mnemonicWords);
    byte[] DeriveLegacyRecoveryKey(string[] mnemonicWords);
    (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(byte[] key, byte[] plaintext);
    byte[] Decrypt(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag);
    byte[] GenerateRandomBytes(int length);
    byte[] GenerateSalt(int length = 32);
}
