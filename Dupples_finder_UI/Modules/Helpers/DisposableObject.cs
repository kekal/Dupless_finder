using System;

namespace Dupples_finder_UI.Modules.Helpers
{
    public class DisposableObject : IDisposable
    {
        private bool IsDisposed { get; set; }

        public virtual void Dispose()
        {
            if (!IsDisposed)
            {
                Clean();
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        protected virtual void Clean()
        {
        }


        ~DisposableObject()
        {
            Dispose();
        }
    }
}