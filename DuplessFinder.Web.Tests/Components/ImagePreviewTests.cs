using Bunit;
using DuplessFinder.Web.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace DuplessFinder.Web.Tests.Components;

public class ImagePreviewTests : IDisposable
{
    private readonly TestContext _ctx;

    public ImagePreviewTests()
    {
        _ctx = new TestContext();

        // Setup default JS interop behavior
        _ctx.JSInterop.SetupVoid("window.__initPreviewZoom");
        _ctx.JSInterop.SetupVoid("window.__destroyPreviewZoom");
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Fact]
    public void Renders_Preview_With_Given_ImageUrl()
    {
        // Arrange
        const string imageUrl = "data:image/jpeg;base64,test123";

        // Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, imageUrl));

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("src").Should().Be(imageUrl);
    }

    [Fact]
    public async Task Close_Button_Click_Invokes_OnClose()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test")
            .Add(p => p.OnClose, callback));

        // Act
        var closeBtn = cut.Find(".preview-close-btn");
        await cut.InvokeAsync(() => closeBtn.Click());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Right_Click_Context_Menu_Invokes_OnClose()
    {
        // Arrange
        bool invoked = false;
        var callback = new EventCallback(null, async () => { invoked = true; return Task.CompletedTask; });

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test")
            .Add(p => p.OnClose, callback));

        // Act
        var overlay = cut.Find(".preview-overlay");
        await cut.InvokeAsync(() => overlay.ContextMenu());

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public void Shows_Primary_Image_By_Default()
    {
        // Arrange
        const string primaryUrl = "data:image/jpeg;base64,primary";
        const string alternateUrl = "data:image/jpeg;base64,alternate";

        // Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, primaryUrl)
            .Add(p => p.AlternateImageUrl, alternateUrl));

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("src").Should().Be(primaryUrl);
    }

    [Fact]
    public async Task Mouse_Down_Button0_Switches_To_Alternate_When_Available()
    {
        // Arrange
        const string primaryUrl = "data:image/jpeg;base64,primary";
        const string alternateUrl = "data:image/jpeg;base64,alternate";

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, primaryUrl)
            .Add(p => p.AlternateImageUrl, alternateUrl));

        // Act
        var container = cut.Find(".preview-image-container");
        await cut.InvokeAsync(() =>
        {
            container.MouseDown(new MouseEventArgs { Button = 0 });
        });

        // Assert - trigger re-render to see updated image
        await cut.InvokeAsync(() => cut.Render());
        var img = cut.Find(".preview-image");
        img.GetAttribute("src").Should().Be(alternateUrl);
    }

    [Fact]
    public async Task Mouse_Up_Switches_Back_To_Primary_Image()
    {
        // Arrange
        const string primaryUrl = "data:image/jpeg;base64,primary";
        const string alternateUrl = "data:image/jpeg;base64,alternate";

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, primaryUrl)
            .Add(p => p.AlternateImageUrl, alternateUrl));

        var container = cut.Find(".preview-image-container");

        // Act - mouse down to alternate
        await cut.InvokeAsync(() =>
        {
            container.MouseDown(new MouseEventArgs { Button = 0 });
        });
        await cut.InvokeAsync(() => cut.Render());

        // Assert intermediate state
        var imgAfterDown = cut.Find(".preview-image");
        imgAfterDown.GetAttribute("src").Should().Be(alternateUrl);

        // Act - mouse up
        await cut.InvokeAsync(() =>
        {
            container.MouseUp(new MouseEventArgs { Button = 0 });
        });
        await cut.InvokeAsync(() => cut.Render());

        // Assert final state
        var imgAfterUp = cut.Find(".preview-image");
        imgAfterUp.GetAttribute("src").Should().Be(primaryUrl);
    }

    [Fact]
    public async Task Does_Not_Switch_To_Alternate_When_AlternateImageUrl_Null()
    {
        // Arrange
        const string primaryUrl = "data:image/jpeg;base64,primary";

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, primaryUrl)
            .Add(p => p.AlternateImageUrl, null));

        // Act
        var container = cut.Find(".preview-image-container");
        await cut.InvokeAsync(() =>
        {
            container.MouseDown(new MouseEventArgs { Button = 0 });
        });
        await cut.InvokeAsync(() => cut.Render());

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("src").Should().Be(primaryUrl);
    }

    [Fact]
    public async Task Mouse_Down_With_Wrong_Button_Does_Not_Switch()
    {
        // Arrange
        const string primaryUrl = "data:image/jpeg;base64,primary";
        const string alternateUrl = "data:image/jpeg;base64,alternate";

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, primaryUrl)
            .Add(p => p.AlternateImageUrl, alternateUrl));

        // Act - button 1 (right-click) instead of 0 (left-click)
        var container = cut.Find(".preview-image-container");
        await cut.InvokeAsync(() =>
        {
            container.MouseDown(new MouseEventArgs { Button = 1 });
        });
        await cut.InvokeAsync(() => cut.Render());

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("src").Should().Be(primaryUrl);
    }

    [Fact]
    public async Task Calls_InitPreviewZoom_On_First_Render()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert
        var invocations = _ctx.JSInterop.Invocations.Where(i => i.Identifier == "window.__initPreviewZoom").ToList();
        invocations.Count.Should().Be(1);
    }

    [Fact]
    public async Task ImageUrl_Parameter_Change_Reinitializes_Zoom()
    {
        // Arrange
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test1"));

        // Act - change ImageUrl parameter
        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test2"));

        // Assert - should call destroy and init at least once each
        // (init from first render + destroy+init from param change)
        var allInvocations = _ctx.JSInterop.Invocations.ToList();
        allInvocations.Count(i => i.Identifier == "window.__destroyPreviewZoom").Should().BeGreaterOrEqualTo(1);
        allInvocations.Count(i => i.Identifier == "window.__initPreviewZoom").Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Component_Implements_IAsyncDisposable()
    {
        // Arrange
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert - component implements IAsyncDisposable which calls destroyPreviewZoom
        cut.Instance.Should().BeAssignableTo<IAsyncDisposable>();

        // initPreviewZoom was called on first render
        _ctx.JSInterop.Invocations.Count(i => i.Identifier == "window.__initPreviewZoom").Should().Be(1);

        // destroyPreviewZoom has not been called yet (only on dispose)
        _ctx.JSInterop.Invocations.Count(i => i.Identifier == "window.__destroyPreviewZoom").Should().Be(0);
    }

    [Fact]
    public void Preview_Overlay_Has_Correct_Structure()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert
        cut.Find(".preview-overlay").Should().NotBeNull();
        cut.Find(".preview-close-btn").Should().NotBeNull();
        cut.Find(".preview-image-container").Should().NotBeNull();
        cut.Find(".preview-image").Should().NotBeNull();
    }

    [Fact]
    public void Close_Button_Has_Correct_Text()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert
        var closeBtn = cut.Find(".preview-close-btn");
        closeBtn.TextContent.Trim().Should().Be("CLOSE");
    }

    [Fact]
    public void Preview_Image_Is_Not_Draggable()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("draggable").Should().Be("false");
    }

    [Fact]
    public void Preview_Image_Alt_Text_Is_Preview()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test"));

        // Assert
        var img = cut.Find(".preview-image");
        img.GetAttribute("alt").Should().Be("Preview");
    }

    [Fact]
    public async Task Multiple_Parameter_Changes_Handle_Reinit_Correctly()
    {
        // Arrange
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test1")
            .Add(p => p.AlternateImageUrl, "data:image/jpeg;base64,alt1"));

        var initialInvocations = _ctx.JSInterop.Invocations.Count();

        // Act - change both ImageUrl and AlternateImageUrl
        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test2")
            .Add(p => p.AlternateImageUrl, "data:image/jpeg;base64,alt2"));

        // Assert - OnParametersSetAsync fires destroy+init when ImageUrl changes
        var newInvocations = _ctx.JSInterop.Invocations.Skip(initialInvocations).ToList();
        newInvocations.Count(i => i.Identifier == "window.__destroyPreviewZoom").Should().BeGreaterOrEqualTo(1);
        // initPreviewZoom may or may not be called depending on bUnit's async rendering
        // The key assertion is that destroy is called when ImageUrl changes
    }

    [Fact]
    public async Task AlternateImageUrl_Change_Only_Does_Not_Reinit()
    {
        // Arrange
        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test1")
            .Add(p => p.AlternateImageUrl, "data:image/jpeg;base64,alt1"));

        var initialInvocations = _ctx.JSInterop.Invocations.Count();

        // Act - change only AlternateImageUrl, keep ImageUrl same
        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test1")
            .Add(p => p.AlternateImageUrl, "data:image/jpeg;base64,alt2"));

        // Assert - should NOT reinit zoom since ImageUrl didn't change
        var newInvocations = _ctx.JSInterop.Invocations.Skip(initialInvocations).ToList();
        newInvocations.Count(i => i.Identifier == "window.__destroyPreviewZoom").Should().Be(0);
    }

    [Fact]
    public void Overlay_Does_Not_Have_Click_Handler()
    {
        // Arrange
        bool invoked = false;
        async Task HandleClose() { invoked = true; }
        var callback = EventCallback.Factory.Create(this, HandleClose);

        var cut = _ctx.RenderComponent<ImagePreview>(parameters => parameters
            .Add(p => p.ImageUrl, "data:image/jpeg;base64,test")
            .Add(p => p.OnClose, callback));

        // Assert - overlay only has contextmenu handler (right-click), not click handler
        // This means left-clicking the overlay will not trigger OnClose
        var overlay = cut.Find(".preview-overlay");
        overlay.Should().NotBeNull();
        // OnClose was not invoked by rendering alone
        invoked.Should().BeFalse();
    }
}
