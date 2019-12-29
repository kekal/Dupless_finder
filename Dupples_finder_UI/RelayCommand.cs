using System;
using System.Windows.Input;

namespace Dupples_finder_UI
{
    public class RelayCommand : ICommand
    {
        readonly Action _targetExecuteMethod;
        //readonly Func<bool> _targetCanExecuteMethod;

        public RelayCommand(Action executeMethod)
        {
            _targetExecuteMethod = executeMethod;
        }

        //public RelayCommand(Action executeMethod, Func<bool> canExecuteMethod)
        //{
        //    _targetExecuteMethod = executeMethod;
        //    _targetCanExecuteMethod = canExecuteMethod;
        //}

        //public void RaiseCanExecuteChanged()
        //{
        //    CanExecuteChanged(this, EventArgs.Empty);
        //}
        #region ICommand Members

        public bool CanExecute(object parameter)
        {
            //if (_targetCanExecuteMethod != null)
            //{
            //    return _targetCanExecuteMethod();
            //}
            return _targetExecuteMethod != null;
        }

        // Beware - should use weak references if command instance lifetime is longer than lifetime of UI objects that get hooked up to command
        // Prism commands solve this in their implementation
        public event EventHandler CanExecuteChanged = delegate { };

        public void Execute(object parameter)
        {
            _targetExecuteMethod?.Invoke();
        }
        #endregion
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _targetExecuteMethod;
        //private readonly Func<T, bool> _targetCanExecuteMethod;

        public RelayCommand(Action<T> executeMethod)
        {
            _targetExecuteMethod = executeMethod;
        }

        //public RelayCommand(Action<T> executeMethod, Func<T,bool> canExecuteMethod)
        //{
        //    _targetExecuteMethod = executeMethod;
        //    _targetCanExecuteMethod = canExecuteMethod;
        //}

        //public void RaiseCanExecuteChanged() 
        //{
        //     CanExecuteChanged(this, EventArgs.Empty); 
        //}
        #region ICommand Members

        public bool CanExecute(object parameter)
        {
            //if (_targetCanExecuteMethod != null)
            //{
            //    T tparm = (T)parameter;
            //    return _targetCanExecuteMethod(tparm);
            //}
            return _targetExecuteMethod != null;
        }

        // Beware - should use weak references if command instance lifetime is longer than lifetime of UI objects that get hooked up to command
        // Prism commands solve this in their implementation
        public event EventHandler CanExecuteChanged = delegate { };

        public void Execute(object parameter)
        {
            _targetExecuteMethod?.Invoke((T)parameter);
        }
        #endregion
    }
}
