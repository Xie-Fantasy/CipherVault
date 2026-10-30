namespace CipherVault.Infrastructure.Tests.TestSupport;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "CipherVault.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string relativePath) => Path.Combine(RootPath, relativePath);

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
            return;

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup is best effort; files are isolated under the OS temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup is best effort; files are isolated under the OS temp directory.
        }
    }
}
