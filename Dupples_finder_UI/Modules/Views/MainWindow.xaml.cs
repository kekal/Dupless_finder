using System;
using System.Diagnostics;
using System.Windows.Input;
using Dupples_finder_UI.Modules.ViewModels;

namespace Dupples_finder_UI.Modules.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
            }
        }

        private void PreviewButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShowAlternatePreview();
            }
        }

        private void PreviewButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RestorePrimaryPreview();
            }
        }
    }
}
