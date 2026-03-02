using Prism.Ioc;
using Prism.Modularity;

namespace Dupples_finder_UI.Modules
{
    /// <summary>
    /// Reserved for future lazy-loaded module features.
    /// Core services are registered in App.RegisterTypes() because
    /// Prism 9 initializes modules after the shell is created.
    /// </summary>
    public class DuplessModule : IModule
    {
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
        }
    }
}
