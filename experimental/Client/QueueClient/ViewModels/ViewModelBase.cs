using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QueueClient.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            // Dispose managed resources
        }
        _disposed = true;
    }
}
