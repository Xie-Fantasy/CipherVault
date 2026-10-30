namespace CipherVault.UI.Helpers;

using CipherVault.UI.Localization;

public static class PasswordStrengthEstimator
{
    /// <summary>
    /// Evaluates password strength using zxcvbn-core.
    /// Returns a score (0-4, where 0 is extremely weak and 4 is very strong)
    /// and a human-readable label in Chinese.
    /// </summary>
    public static (int score, string label) Estimate(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (0, "");

        var result = Zxcvbn.Core.EvaluatePassword(password);
        var label = result.Score switch
        {
            0 or 1 => L.T("弱"),
            2 or 3 => L.T("中"),
            4 => L.T("强"),
            _ => L.T("中")
        };

        return (result.Score, label);
    }
}
