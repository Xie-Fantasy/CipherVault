namespace CipherVault.UI.Tests;

using CipherVault.UI.Helper;
using FluentAssertions;
using Xunit;

public sealed class IconGeometryTests
{
    [Fact]
    public void EveryIconGeometry_StaysInsideLucide24PixelViewport()
    {
        foreach (string name in Icon.Names)
        {
            var geometry = Icon.Get(name);
            geometry.Should().NotBeNull($"icon '{name}' must have geometry");
            var bounds = geometry!.Bounds;
            bounds.IsEmpty.Should().BeFalse($"icon '{name}' must render visible geometry");
            bounds.Left.Should().BeGreaterThanOrEqualTo(-0.01, $"icon '{name}' must not overflow left");
            bounds.Top.Should().BeGreaterThanOrEqualTo(-0.01, $"icon '{name}' must not overflow top");
            bounds.Right.Should().BeLessThanOrEqualTo(24.01, $"icon '{name}' must not overflow right");
            bounds.Bottom.Should().BeLessThanOrEqualTo(24.01, $"icon '{name}' must not overflow bottom");
        }
    }
}
