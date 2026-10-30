using CipherVault.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace CipherVault.UI.Tests.ViewModels;

public sealed class ViewModelBaseTests
{
    [Fact]
    public void OnError_RaisesOneNonRecursiveErrorEvent()
    {
        var sut = new ErrorReportingViewModel();
        var exception = new InvalidOperationException("test failure");
        var received = new List<ViewModelErrorEventArgs>();

        void Handler(object? sender, ViewModelErrorEventArgs args)
        {
            if (ReferenceEquals(sender, sut))
                received.Add(args);
        }

        ViewModelBase.ErrorReported += Handler;
        try
        {
            sut.Report(exception, "running test");
        }
        finally
        {
            ViewModelBase.ErrorReported -= Handler;
        }

        received.Should().ContainSingle();
        received[0].ViewModelName.Should().Be(nameof(ErrorReportingViewModel));
        received[0].ErrorContext.Should().Be("running test");
        received[0].Exception.Should().BeSameAs(exception);
    }

    private sealed class ErrorReportingViewModel : ViewModelBase
    {
        public void Report(Exception exception, string context) => OnError(exception, context);
    }
}
