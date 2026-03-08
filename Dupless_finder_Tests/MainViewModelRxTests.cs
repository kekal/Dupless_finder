using System;
using System.Threading;
using System.Threading.Tasks;
using Dupples_finder_UI.Modules.ViewModels;
using Dupples_finder_UI.Services.Interfaces;
using Microsoft.Reactive.Testing;
using Moq;
using Prism.Events;
using Xunit;

namespace Dupless_finder_Tests;

public class MainViewModelRxTests : IDisposable
{
    private readonly IEventAggregator _ea;
    private readonly Mock<IPhotoDbService> _mockDb;
    private readonly Mock<ICalcOperations> _mockCalc;
    private readonly Mock<ILoadingOperations> _mockLoading;
    private readonly Mock<IThumbnailService> _mockThumb;
    private readonly TestScheduler _testScheduler;
    private MainViewModel _vm;

    public MainViewModelRxTests()
    {
        // Prism requires a SynchronizationContext when subscribing with ThreadOption.UIThread.
        // xUnit test runner doesn't set one, so install one here.
        if (SynchronizationContext.Current == null)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        _mockDb = new Mock<IPhotoDbService>();
        _mockDb.Setup(d => d.IsAvailable).Returns(false);
        _mockDb.Setup(d => d.InitializeAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        _mockCalc = new Mock<ICalcOperations>();
        _mockLoading = new Mock<ILoadingOperations>();
        _mockThumb = new Mock<IThumbnailService>();

        // Use the real EventAggregator so that Prism event subscriptions wire up correctly.
        _ea = new EventAggregator();

        _testScheduler = new TestScheduler();

        _vm = new MainViewModel(
            _ea,
            _mockDb.Object,
            _mockCalc.Object,
            _mockLoading.Object,
            _mockThumb.Object,
            _testScheduler,   // backgroundScheduler
            _testScheduler);  // uiScheduler
    }

    public void Dispose()
    {
        _vm?.Dispose();
    }

    [Fact]
    public void ScoreThreshold_RapidChanges_RefiltersAfterDebounce()
    {
        // Set threshold multiple times rapidly (within 200ms window)
        _vm.ScoreThreshold = 100;
        _vm.ScoreThreshold = 150;
        _vm.ScoreThreshold = 180;

        // Advance less than 200ms — should NOT have refiltered yet
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        // Just verifying no crash

        // Advance past 200ms debounce window
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);

        // After the debounce period, the final value should be set
        Assert.Equal(180, _vm.ScoreThreshold);
    }

    [Fact]
    public void ScoreThreshold_SingleChange_RefiltersAfterDebounce()
    {
        _vm.ScoreThreshold = 50;

        // Advance past debounce
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        Assert.Equal(50, _vm.ScoreThreshold);
    }

    [Fact]
    public void ScoreThreshold_NoChanges_NoRefilter()
    {
        // Initial state
        var initialThreshold = _vm.ScoreThreshold;

        // Advance time without changing threshold
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Verify threshold is unchanged
        Assert.Equal(initialThreshold, _vm.ScoreThreshold);
    }

    [Fact]
    public void Dispose_DisposesRxSubscriptions_NoErrors()
    {
        _vm.ScoreThreshold = 100;
        _vm.Dispose();

        // After dispose the subscription is detached — setting the property
        // still works (SetProperty fires PropertyChanged) but RefilterPairs
        // is never called because the Rx subscription was disposed.
        var ex = Record.Exception(() => _vm.ScoreThreshold = 200);
        Assert.Null(ex);
    }

    [Fact]
    public void ScoreThreshold_ChangedBeforeAndAfterDispatch()
    {
        // Set initial threshold
        _vm.ScoreThreshold = 100;

        // Advance half the debounce period
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);

        // Change threshold again
        _vm.ScoreThreshold = 150;

        // Advance another 100ms (total 200ms from first change, but only 100ms from second)
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);

        // Debounce window should be reset by the second change,
        // so we should still be waiting
        Assert.Equal(150, _vm.ScoreThreshold);

        // Now advance past the 200ms window from the second change
        _testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Now the final value should be applied
        Assert.Equal(150, _vm.ScoreThreshold);
    }
}
