namespace CipherVault.Core.Security;

using System.Text;

/// <summary>
/// Shared master-password length rules used by setup and recovery flows.
/// </summary>
public static class MasterPasswordPolicy
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 128;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool IsValid(string? password) =>
        password is not null
        && password.Length is >= MinimumLength and <= MaximumLength;

    public static bool IsValidUtf8(ReadOnlySpan<byte> password)
    {
        try
        {
            int characterCount = StrictUtf8.GetCharCount(password);
            return characterCount is >= MinimumLength and <= MaximumLength;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
