using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlenderSuite.RenderQueue.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            _disposed = true;
        }
    }
}
