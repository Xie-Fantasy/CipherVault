namespace CipherVault.Infrastructure.Crypto;

using System.Runtime.InteropServices;
using System.Security;

/// <summary>
/// Provides secure conversion between <see cref="SecureString"/> and byte arrays,
/// and utilities for securely zeroing managed arrays.
/// </summary>
public static class SecureStringHelper
{
    /// <summary>
    /// Converts a <see cref="SecureString"/> to a managed byte array (UTF-8 encoded).
    /// The caller is responsible for securely zeroing the returned array after use.
    /// </summary>
    /// <param name="secureString">The secure string to convert.</param>
    /// <returns>A byte array containing the UTF-8 encoded contents of the secure string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secureString"/> is null.</exception>
    public static byte[] SecureStringToBytes(SecureString secureString)
    {
        ArgumentNullException.ThrowIfNull(secureString);

        if (secureString.Length == 0)
            return Array.Empty<byte>();

        IntPtr bstr = IntPtr.Zero;
        char[] chars = Array.Empty<char>();
        try
        {
            bstr = Marshal.SecureStringToBSTR(secureString);

            // Read the length prefix (first 4 bytes of BSTR)
            int length = Marshal.ReadInt32(bstr, -4);
            chars = new char[length / 2]; // BSTR stores length in bytes, chars are 2 bytes each

            Marshal.Copy(bstr, chars, 0, chars.Length);

            return System.Text.Encoding.UTF8.GetBytes(chars);
        }
        finally
        {
            // Zero out the intermediate char array to prevent plaintext lingering in memory
            if (chars.Length > 0)
                Array.Clear(chars);

            if (bstr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(bstr);
            }
        }
    }

    /// <summary>
    /// Converts a byte array (UTF-8 encoded) to a <see cref="SecureString"/>.
    /// </summary>
    /// <param name="bytes">The byte array to convert. Expected to contain valid UTF-8 data.</param>
    /// <returns>A <see cref="SecureString"/> containing the decoded characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null.</exception>
    public static SecureString BytesToSecureString(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var secureString = new SecureString();

        if (bytes.Length == 0)
        {
            secureString.MakeReadOnly();
            return secureString;
        }

        char[] chars = System.Text.Encoding.UTF8.GetChars(bytes);
        try
        {
            foreach (char c in chars)
            {
                secureString.AppendChar(c);
            }

            secureString.MakeReadOnly();
            return secureString;
        }
        finally
        {
            // Zero out the temporary char array
            Array.Clear(chars);
        }
    }

    /// <summary>
    /// Securely zeroes all elements of a byte array.
    /// Uses <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/>
    /// to prevent the compiler from optimizing away the zeroing operation.
    /// </summary>
    /// <param name="array">The array to zero. If null, this method does nothing.</param>
    public static void SecureZeroArray(byte[] array)
    {
        if (array == null || array.Length == 0)
            return;

        // CryptographicOperations.ZeroMemory is guaranteed not to be optimized away
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(array.AsSpan());
    }
}
