using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Dupples_finder_UI.Services;

namespace Dupples_finder_UI.Modules.Helpers;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that supports batch-adding items with a single <see cref="NotifyCollectionChangedAction.Reset"/> notification instead of firing one event per item. This dramatically reduces WPF layout passes when populating large galleries. </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private static int _addRangeCallIndex;

    /// <summary>
    /// Adds all items to the collection and raises a single Reset notification.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();

        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        using var op = PerfLogger.TimedVerbose("ADDRANGE");

        foreach (var item in list)
        {
            Items.Add(item);
        }
        op.Lap("insert");

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        op.Lap("notify");

        var idx = Interlocked.Increment(ref _addRangeCallIndex);
        op.Detail($"#{idx}");
        op.Detail($"batchSize={list.Count}");
        op.Detail($"totalItems={Items.Count}");
        op.Detail($"thread={System.Environment.CurrentManagedThreadId}");

        if (!(op.ElapsedMs > 50 || idx % 10 == 0))
        {
            op.Suppress();
        }
    }
}
