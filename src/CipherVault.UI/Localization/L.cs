namespace CipherVault.UI.Localization;

/// <summary>Concise accessors for localized ViewModel and dialog text.</summary>
public static class L
{
    public static string T(string chinese) => LocalizationManager.Instance.Translate(chinese);

    public static string F(string chineseFormat, params object?[] args)
        => LocalizationManager.Instance.Format(chineseFormat, args);

    public static bool IsEnglish => LocalizationManager.Instance.IsEnglish;
}
