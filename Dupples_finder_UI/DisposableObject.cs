using System;

namespace Dupples_finder_UI
{
    public class DisposableObject : IDisposable
    {
        private bool IsDisposed { get; set; }

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Clean()
        {
            throw new NotImplementedException();
        }


        private void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    Clean();
                }
                IsDisposed = true;
            }
        }
        
        ~DisposableObject()
        {
            Dispose(false);
        }
    }
}