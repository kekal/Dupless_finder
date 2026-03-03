using System.Collections.Generic;

namespace Dupples_finder_UI.Services.Interfaces;

public interface ILoadingOperations
{
    bool GetAllPaths(out IEnumerable<string> paths, string rootFolder);
}