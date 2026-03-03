using System;

namespace Dupples_finder_UI.Modules.Helpers;

public class DisposableObject : IDisposable
{
    ~DisposableObject()
    {
        Dispose();
    }

    private bool IsDisposed { get; set; }

    protected virtual void Clean()
    {
    }

    public virtual void Dispose()
    {
        if (!IsDisposed)
        {
            Clean();
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}