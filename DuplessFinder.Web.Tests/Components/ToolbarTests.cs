using Bunit;
using DuplessFinder.Web.Components;
using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DuplessFinder.Web.Tests.Components;

public class ToolbarTests : IDisposable
{
    private readonly TestContext _ctx;

    public ToolbarTests()
    {
        _ctx = new TestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Fact]
    public void Renders_With_Default_State()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>();

        // Assert
        cut.Find(".toolbar").Should().NotBeNull();
        cut.Find("button.toolbar-btn").TextContent.Should().Contain("Open");
        var buttons = cut.FindAll("button.toolbar-btn");
        buttons.Count(b => b.TextContent.Trim() == "Analyze Gallery").Should().Be(1);
        cut.Find("span.toolbar-status").TextContent.Should().Be("Ready");
    }

    [Fact]
    public void Shows_Undo_Button_When_ShowUndo_True()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.ShowUndo, true));

        // Assert
        cut.FindAll("button.toolbar-btn")
            .Should().Contain(btn => btn.TextContent.Contains("Undo Deletion"));
    }

    [Fact]
    public void Hides_Undo_Button_When_ShowUndo_False()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.ShowUndo, false));

        // Assert
        cut.FindAll("button.toolbar-btn")
            .Should().NotContain(btn => btn.TextContent.Contains("Undo Deletion"));
    }

    [Fact]
    public void Shows_Cancel_Button_When_IsProgressVisible_And_CanCancel_True()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IsProgressVisible, true)
            .Add(p => p.CanCancel, true));

        // Assert
        cut.FindAll("button.toolbar-btn")
            .Should().Contain(btn => btn.TextContent.Trim() == "Cancel");
    }

    [Fact]
    public void Hides_Cancel_Button_When_IsProgressVisible_False()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IsProgressVisible, false)
            .Add(p => p.CanCancel, true));

        // Assert
        cut.FindAll("button.toolbar-btn")
            .Should().NotContain(btn => btn.TextContent.Trim() == "Cancel");
    }

    [Fact]
    public void Hides_Cancel_Button_When_CanCancel_False()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IsProgressVisible, true)
            .Add(p => p.CanCancel, false));

        // Assert
        cut.FindAll("button.toolbar-btn")
            .Should().NotContain(btn => btn.TextContent.Trim() == "Cancel");
    }

    [Fact]
    public void Shows_Progress_Bar_When_IsProgressVisible_True()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IsProgressVisible, true));

        // Assert
        cut.Find(".toolbar-progress-container").Should().NotBeNull();
        cut.Find(".toolbar-progress-bar").Should().NotBeNull();
    }

    [Fact]
    public void Hides_Progress_Bar_When_IsProgressVisible_False()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IsProgressVisible, false));

        // Assert
        cut.FindAll(".toolbar-progress-container").Should().BeEmpty();
    }

    [Fact]
    public void Displays_Correct_StatusText()
    {
        // Arrange
        const string expectedStatus = "Analyzing images...";

        // Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.StatusText, expectedStatus));

        // Assert
        cut.Find("span.toolbar-status").TextContent.Should().Be(expectedStatus);
    }

    [Fact]
    public void Displays_Correct_ScoreThreshold_Value()
    {
        // Arrange
        const double expectedScore = 150.5;

        // Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.ScoreThreshold, expectedScore));

        // Assert
        cut.Find("span.toolbar-score").TextContent.Should().Be("150");
    }

    [Fact]
    public async Task Clicking_Open_Button_Invokes_OnOpen_Callback()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.OnOpen, callback));

        // Act
        var openButton = cut.FindAll("button.toolbar-btn").First(b => b.TextContent.Trim() == "Open Folder");
        await cut.InvokeAsync(() => openButton.Click());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Clicking_Analyze_Button_Invokes_OnAnalyze_Callback()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.OnAnalyze, callback));

        // Act
        var analyzeButton = cut.FindAll("button.toolbar-btn").First(b => b.TextContent.Trim() == "Analyze Gallery");
        await cut.InvokeAsync(() => analyzeButton.Click());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Clicking_Cancel_Button_Invokes_OnCancel_Callback()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.OnCancel, callback)
            .Add(p => p.IsProgressVisible, true)
            .Add(p => p.CanCancel, true));

        // Act
        var cancelButton = cut.Find("button.toolbar-cancel");
        await cut.InvokeAsync(() => cancelButton.Click());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Clicking_Undo_Button_Invokes_OnUndo_Callback()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.OnUndo, callback)
            .Add(p => p.ShowUndo, true));

        // Act
        var undoButton = cut.FindAll("button.toolbar-btn")
            .First(b => b.TextContent.Contains("Undo Deletion"));
        await cut.InvokeAsync(() => undoButton.Click());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Slider_Input_Invokes_ScoreThresholdChanged_With_Parsed_Value()
    {
        // Arrange
        double? invokedValue = null;
        async Task HandleChange(double val) { invokedValue = val; }
        var callback = EventCallback.Factory.Create<double>(this, HandleChange);

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.ScoreThresholdChanged, callback)
            .Add(p => p.ScoreThreshold, 100));

        // Act
        var slider = cut.Find("input[type='range']");
        await cut.InvokeAsync(() =>
        {
            slider.Input(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "250" });
        });

        // Assert
        invokedValue.Should().Be(250.0);
    }

    [Fact]
    public async Task Checkbox_Change_Invokes_IncludeSubfoldersChanged()
    {
        // Arrange
        bool? invokedValue = null;
        async Task HandleChange(bool val) { invokedValue = val; }
        var callback = EventCallback.Factory.Create<bool>(this, HandleChange);

        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IncludeSubfoldersChanged, callback)
            .Add(p => p.IncludeSubfolders, false));

        // Act
        var checkbox = cut.Find("input[type='checkbox']");
        await cut.InvokeAsync(() =>
        {
            checkbox.Change(true);
        });

        // Assert
        invokedValue.Should().BeTrue();
    }

    [Fact]
    public void Progress_Bar_Width_Matches_Progress_Value()
    {
        // Arrange
        const double progress = 75.5;

        // Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.Progress, progress)
            .Add(p => p.IsProgressVisible, true));

        // Assert
        var progressBar = cut.Find(".toolbar-progress-bar");
        progressBar.GetAttribute("style").Should().Contain("75.5%");
    }

    [Fact]
    public void Include_Subfolders_Checkbox_Reflects_Parameter()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IncludeSubfolders, true));

        // Assert
        var checkbox = cut.Find("input[type='checkbox']");
        checkbox.GetAttribute("checked").Should().NotBeNull();
    }

    [Fact]
    public void Include_Subfolders_Checkbox_Unchecked_When_False()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.IncludeSubfolders, false));

        // Assert
        var checkbox = cut.Find("input[type='checkbox']");
        checkbox.HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void Progress_Container_Has_Correct_Title()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<Toolbar>(parameters => parameters
            .Add(p => p.Progress, 50.75)
            .Add(p => p.IsProgressVisible, true));

        // Assert
        var container = cut.Find(".toolbar-progress-container");
        container.GetAttribute("title").Should().Be("50.8%");
    }
}
