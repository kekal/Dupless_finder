using System;
using Prism.Events;
using Prism.Mvvm;

namespace Dupples_finder_UI.Modules.ViewModels
{
    public abstract class ViewModelBase : BindableBase, IDisposable
    {
        protected IEventAggregator EventAggregator { get; }

        protected ViewModelBase()
        {
            DefineCommands();
            DefineEvents();
        }

        protected ViewModelBase(IEventAggregator eventAggregator)
        {
            EventAggregator = eventAggregator;
            DefineCommands();
            DefineEvents();
        }

        protected virtual void DefineCommands() { }

        protected virtual void DefineEvents() { }

        private bool _disposed;

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(true);
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
