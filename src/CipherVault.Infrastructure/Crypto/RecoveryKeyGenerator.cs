namespace CipherVault.Infrastructure.Crypto;

using System.Reflection;
using System.Security.Cryptography;
using CipherVault.Core.Models;

/// <summary>
/// Generates and validates 12-word BIP39 mnemonics from 128 bits of entropy.
/// </summary>
public sealed class RecoveryKeyGenerator
{
    private const int EntropyBytes = 16;
    private const int ChecksumBits = 4;
    private const int BitsPerWord = 11;
    private const int WordListSize = 2048;
    private const string WordListResourceName =
        "CipherVault.Infrastructure.Crypto.Resources.Bip39English.txt";

    public const int MnemonicWordCount = 12;

    private readonly string[] _wordList;
    private readonly Dictionary<string, int> _wordIndexes;

    /// <summary>Gets a copy of the official BIP39 English word list.</summary>
    public static string[] DefaultEnglishWordList { get; } = LoadDefaultWordList();

    public RecoveryKeyGenerator()
        : this(DefaultEnglishWordList)
    {
    }

    public RecoveryKeyGenerator(string[] wordList)
    {
        ArgumentNullException.ThrowIfNull(wordList);

        if (wordList.Length != WordListSize)
            throw new ArgumentException($"Word list must contain exactly {WordListSize} words.", nameof(wordList));

        _wordList = (string[])wordList.Clone();
        _wordIndexes = new Dictionary<string, int>(WordListSize, StringComparer.Ordinal);

        for (int index = 0; index < _wordList.Length; index++)
        {
            string word = _wordList[index].Trim();
            if (word.Length == 0 || !_wordIndexes.TryAdd(word, index))
                throw new ArgumentException("Word list must contain non-empty, unique words.", nameof(wordList));
            _wordList[index] = word;
        }
    }

    public RecoveryKey Generate()
    {
        byte[] entropy = RandomNumberGenerator.GetBytes(EntropyBytes);
        try
        {
            return new RecoveryKey
            {
                Words = EncodeEntropy(entropy),
                CreatedAt = DateTime.UtcNow,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    /// <summary>Validates word membership and the BIP39 checksum.</summary>
    public bool Validate(RecoveryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryDecodeEntropy(key.Words, out _);
    }

    internal string[] EncodeEntropy(ReadOnlySpan<byte> entropy)
    {
        if (entropy.Length != EntropyBytes)
            throw new ArgumentException($"Entropy must be exactly {EntropyBytes} bytes.", nameof(entropy));

        byte[] hash = SHA256.HashData(entropy);
        try
        {
            string[] words = new string[MnemonicWordCount];
            for (int wordIndex = 0; wordIndex < MnemonicWordCount; wordIndex++)
            {
                int value = 0;
                int bitOffset = wordIndex * BitsPerWord;
                for (int bit = 0; bit < BitsPerWord; bit++)
                {
                    int sourceBit = bitOffset + bit;
                    int bitValue = sourceBit < EntropyBytes * 8
                        ? GetBit(entropy, sourceBit)
                        : GetBit(hash, sourceBit - EntropyBytes * 8);
                    value = (value << 1) | bitValue;
                }

                words[wordIndex] = _wordList[value];
            }

            return words;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private bool TryDecodeEntropy(string[]? words, out byte[] entropy)
    {
        entropy = Array.Empty<byte>();
        if (words is not { Length: MnemonicWordCount })
            return false;

        byte[] decoded = new byte[EntropyBytes];
        int providedChecksum = 0;

        for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            if (string.IsNullOrWhiteSpace(words[wordIndex]) ||
                !_wordIndexes.TryGetValue(words[wordIndex].Trim(), out int value))
            {
                CryptographicOperations.ZeroMemory(decoded);
                return false;
            }

            for (int bit = 0; bit < BitsPerWord; bit++)
            {
                int bitValue = (value >> (BitsPerWord - 1 - bit)) & 1;
                int targetBit = wordIndex * BitsPerWord + bit;
                if (targetBit < EntropyBytes * 8)
                    SetBit(decoded, targetBit, bitValue);
                else
                    providedChecksum = (providedChecksum << 1) | bitValue;
            }
        }

        byte[] hash = SHA256.HashData(decoded);
        bool checksumMatches = providedChecksum == (hash[0] >> (8 - ChecksumBits));
        CryptographicOperations.ZeroMemory(hash);

        if (!checksumMatches)
        {
            CryptographicOperations.ZeroMemory(decoded);
            return false;
        }

        entropy = decoded;
        return true;
    }

    private static int GetBit(ReadOnlySpan<byte> bytes, int bitIndex) =>
        (bytes[bitIndex / 8] >> (7 - bitIndex % 8)) & 1;

    private static void SetBit(Span<byte> bytes, int bitIndex, int value)
    {
        if (value != 0)
            bytes[bitIndex / 8] |= (byte)(1 << (7 - bitIndex % 8));
    }

    private static string[] LoadDefaultWordList()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(WordListResourceName)
            ?? throw new InvalidOperationException("The embedded BIP39 English word list is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        string[] words = reader.ReadToEnd()
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length != WordListSize)
            throw new InvalidOperationException($"The embedded BIP39 word list must contain {WordListSize} words.");

        return words;
    }
}
