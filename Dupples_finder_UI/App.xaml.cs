using System.Windows;
using Dupples_finder_UI.Data;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Modules.Views;
using Dupples_finder_UI.Services;
using Dupples_finder_UI.Services.Interfaces;
using Prism.Ioc;
using Prism.Mvvm;

namespace Dupples_finder_UI;

public partial class App
{
    protected override void ConfigureViewModelLocator()
    {
        base.ConfigureViewModelLocator();
        ViewModelLocationProvider.Register<MainWindow, MainViewModel>();
    }

    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterForNavigation<MainWindow, MainViewModel>();
        containerRegistry.RegisterSingleton<IPhotoDbService, PhotoDbService>();
        containerRegistry.RegisterSingleton<IThumbnailService, ThumbnailService>();
        containerRegistry.Register<ICalcOperations, CalcOperations>();
        containerRegistry.Register<ILoadingOperations, LoadingOperations>();
        containerRegistry.Register<IMatSerializer, MatSerializer>();
    }
}