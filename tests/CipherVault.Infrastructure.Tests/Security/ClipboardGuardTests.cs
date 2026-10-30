namespace CipherVault.Infrastructure.Tests.Security;

using CipherVault.Infrastructure.Security;
using FluentAssertions;
using Xunit;

public sealed class ClipboardGuardTests
{
    [Fact]
    public async Task AutoClear_ClearsClipboardWhenContentIsStillOwned()
    {
        string? clipboard = null;
        int clearCount = 0;
        using var sut = new ClipboardGuard(
            text => clipboard = text,
            () => { clipboard = null; clearCount++; },
            () => clipboard)
        {
            AutoClearSeconds = 1,
        };

        sut.CopyToClipboard("secret");
        await Task.Delay(1300);

        clipboard.Should().BeNull();
        clearCount.Should().Be(1);
    }

    [Fact]
    public async Task AutoClear_DoesNotEraseClipboardContentCopiedLaterByUser()
    {
        string? clipboard = null;
        int clearCount = 0;
        using var sut = new ClipboardGuard(
            text => clipboard = text,
            () => { clipboard = null; clearCount++; },
            () => clipboard)
        {
            AutoClearSeconds = 1,
        };

        sut.CopyToClipboard("vault secret");
        clipboard = "user copied this later";
        await Task.Delay(1300);

        clipboard.Should().Be("user copied this later");
        clearCount.Should().Be(0);
    }
}
