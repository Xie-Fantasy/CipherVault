using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CipherVault.UI.ViewModels;

/// <summary>
/// Base class for all ViewModels in the CipherVault application.
/// Extends <see cref="ObservableObject"/> from CommunityToolkit.Mvvm
/// with common error handling utilities.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    public static event EventHandler<ViewModelErrorEventArgs>? ErrorReported;

    /// <summary>
    /// Executes an async action with centralized error handling.
    /// Exceptions are logged and optionally surfaced via an error message property.
    /// </summary>
    /// <param name="action">The async operation to execute.</param>
    /// <param name="errorContext">A description of the operation for logging purposes.</param>
    /// <returns>True if the operation completed successfully; otherwise false.</returns>
    protected async Task<bool> ExecuteWithErrorHandlingAsync(Func<Task> action, string errorContext)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("[{0}] Error during {1}: {2}", GetType().Name, errorContext, ex);
            OnError(ex, errorContext);
            return false;
        }
    }

    /// <summary>
    /// Executes a synchronous action with centralized error handling.
    /// </summary>
    /// <param name="action">The operation to execute.</param>
    /// <param name="errorContext">A description of the operation for logging purposes.</param>
    /// <returns>True if the operation completed successfully; otherwise false.</returns>
    protected bool ExecuteWithErrorHandling(Action action, string errorContext)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("[{0}] Error during {1}: {2}", GetType().Name, errorContext, ex);
            OnError(ex, errorContext);
            return false;
        }
    }

    /// <summary>
    /// Called when an error occurs during an operation. Override in derived classes
    /// to surface error messages to the UI (e.g., by setting an ErrorMessage property).
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="errorContext">A description of the operation that failed.</param>
    protected virtual void OnError(Exception ex, string errorContext)
    {
        Debug.WriteLine($"[{GetType().Name}] Error during {errorContext}: {ex.Message}");
        ErrorReported?.Invoke(
            this,
            new ViewModelErrorEventArgs(GetType().Name, errorContext, ex));
    }
}

public sealed record ViewModelErrorEventArgs(
    string ViewModelName,
    string ErrorContext,
    Exception Exception);
