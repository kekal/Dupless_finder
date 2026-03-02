using System.Windows;
using Dupples_finder_UI.Data;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Modules.Views;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using Prism.Ioc;
using Prism.Mvvm;

namespace Dupples_finder_UI
{
    public partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // View-ViewModel registration
            containerRegistry.RegisterForNavigation<MainWindow, MainViewModel>();

            // Services — must be registered here (not in a module) because
            // Prism 9 initializes modules AFTER CreateShell/AutoWireViewModel.
            containerRegistry.RegisterSingleton<IPhotoDbService, PhotoDbService>();
            containerRegistry.RegisterSingleton<IThumbnailService, ThumbnailService>();
            containerRegistry.Register<ICalcOperations, CalcOperations>();
            containerRegistry.Register<ILoadingOperations, LoadingOperations>();
            containerRegistry.Register<IMatSerializer, MatSerializer>();
        }

        protected override void ConfigureViewModelLocator()
        {
            base.ConfigureViewModelLocator();
            ViewModelLocationProvider.Register<MainWindow, MainViewModel>();
        }
    }
}
