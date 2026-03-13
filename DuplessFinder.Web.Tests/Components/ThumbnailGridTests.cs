using Bunit;
using DuplessFinder.Web.Components;
using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DuplessFinder.Web.Tests.Components;

public class ThumbnailGridTests : IDisposable
{
    private readonly TestContext _ctx;

    public ThumbnailGridTests()
    {
        _ctx = new TestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private ImageInfo CreateTestImage(string path, string name)
    {
        var entry = new FileEntry(path, name, 1000, 12345L);
        return new ImageInfo(entry);
    }

    private EventCallback<ImageInfo> CreateDeleteCallback(Action<ImageInfo> callback)
    {
        async Task Handler(ImageInfo img) => callback(img);
        return EventCallback.Factory.Create<ImageInfo>(this, Handler);
    }

    [Fact]
    public void Renders_Empty_Grid_When_Images_Empty()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, new List<ImageInfo>()));

        // Assert
        cut.Find(".thumbnail-grid").Should().NotBeNull();
        cut.FindAll(".thumbnail-cell").Should().BeEmpty();
    }

    [Fact]
    public void Renders_Correct_Number_Of_Thumbnail_Cells()
    {
        // Arrange
        var images = new List<ImageInfo>
        {
            CreateTestImage("/path/img1.jpg", "img1.jpg"),
            CreateTestImage("/path/img2.jpg", "img2.jpg"),
            CreateTestImage("/path/img3.jpg", "img3.jpg")
        };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        cut.FindAll(".thumbnail-cell").Count.Should().Be(3);
    }

    [Fact]
    public void Displays_Image_Name_For_Each_Thumbnail()
    {
        // Arrange
        var images = new List<ImageInfo>
        {
            CreateTestImage("/path/image1.jpg", "image1.jpg"),
            CreateTestImage("/path/image2.jpg", "image2.jpg")
        };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var filenames = cut.FindAll(".thumbnail-filename");
        filenames.Count.Should().Be(2);
        filenames[0].TextContent.Should().Be("image1.jpg");
        filenames[1].TextContent.Should().Be("image2.jpg");
    }

    [Fact]
    public void Shows_Loading_Placeholder_When_ThumbnailDataUrl_Null()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        // ThumbnailDataUrl is null by default
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        cut.FindAll(".thumbnail-placeholder")
            .Should().Contain(p => p.TextContent == "Loading...");
    }

    [Fact]
    public void Shows_Img_Tag_When_ThumbnailDataUrl_Set()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        image.ThumbnailDataUrl = "data:image/jpeg;base64,abc123";
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var imgs = cut.FindAll(".thumbnail-image-wrapper img");
        imgs.Count.Should().Be(1);
        imgs[0].GetAttribute("src").Should().Be("data:image/jpeg;base64,abc123");
    }

    [Fact]
    public async Task Delete_Button_Click_Invokes_OnDeleteImage()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        var images = new List<ImageInfo> { image };

        ImageInfo? deletedImage = null;
        var callback = CreateDeleteCallback(img => { deletedImage = img; });

        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images)
            .Add(p => p.OnDeleteImage, callback));

        // Act
        var deleteBtn = cut.Find(".delete-btn");
        await cut.InvokeAsync(() => deleteBtn.Click());

        // Assert
        deletedImage.Should().NotBeNull();
        deletedImage!.Name.Should().Be("img.jpg");
        deletedImage.Path.Should().Be("/path/img.jpg");
    }

    [Fact]
    public async Task Double_Click_On_Thumbnail_Cell_Invokes_OnImageClick()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        var images = new List<ImageInfo> { image };

        ImageInfo? clickedImage = null;
        var callback = CreateDeleteCallback(img => { clickedImage = img; });

        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images)
            .Add(p => p.OnImageClick, callback));

        // Act
        var cell = cut.Find(".thumbnail-cell");
        await cut.InvokeAsync(() => cell.DoubleClick());

        // Assert
        clickedImage.Should().NotBeNull();
        clickedImage!.Name.Should().Be("img.jpg");
    }

    [Fact]
    public void Displays_Image_Path_In_Title_Attribute()
    {
        // Arrange
        const string imagePath = "/very/long/path/to/image.jpg";
        var image = CreateTestImage(imagePath, "image.jpg");
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var filename = cut.Find(".thumbnail-filename");
        filename.GetAttribute("title").Should().Be(imagePath);
    }

    [Fact]
    public async Task Delete_Button_Click_With_Multiple_Images()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        var images = new List<ImageInfo> { img1, img2, img3 };

        ImageInfo? deletedImage = null;
        var callback = CreateDeleteCallback(img => { deletedImage = img; });

        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images)
            .Add(p => p.OnDeleteImage, callback));

        // Act - Delete second image
        var deleteButtons = cut.FindAll(".delete-btn");
        await cut.InvokeAsync(() => deleteButtons[1].Click());

        // Assert
        deletedImage.Should().NotBeNull();
        deletedImage!.Name.Should().Be("img2.jpg");
    }

    [Fact]
    public async Task Double_Click_Invokes_Correct_Image_With_Multiple_Images()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        var images = new List<ImageInfo> { img1, img2, img3 };

        ImageInfo? clickedImage = null;
        var callback = CreateDeleteCallback(img => { clickedImage = img; });

        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images)
            .Add(p => p.OnImageClick, callback));

        // Act - Double-click third image
        var cells = cut.FindAll(".thumbnail-cell");
        await cut.InvokeAsync(() => cells[2].DoubleClick());

        // Assert
        clickedImage.Should().NotBeNull();
        clickedImage!.Name.Should().Be("img3.jpg");
    }

    [Fact]
    public void Mixed_Loaded_And_Unloaded_Thumbnails()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        img1.ThumbnailDataUrl = "data:image/jpeg;base64,data1";

        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        // img2 has no thumbnail

        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        img3.ThumbnailDataUrl = "data:image/jpeg;base64,data3";

        var images = new List<ImageInfo> { img1, img2, img3 };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var imgs = cut.FindAll(".thumbnail-image-wrapper img");
        imgs.Count.Should().Be(2);

        var placeholders = cut.FindAll(".thumbnail-placeholder");
        placeholders.Count.Should().Be(1);
    }

    [Fact]
    public void Delete_Button_Has_Correct_Attributes()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var deleteBtn = cut.Find(".delete-btn");
        deleteBtn.TextContent.Trim().Should().Be("X");
        deleteBtn.GetAttribute("title").Should().Be("Delete image");
    }

    [Fact]
    public void Thumbnail_Cell_Renders_With_Correct_Structure()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert - verify cell renders with expected child elements
        var cell = cut.Find(".thumbnail-cell");
        cell.Should().NotBeNull();
        cell.QuerySelector(".delete-btn").Should().NotBeNull();
        cell.QuerySelector(".thumbnail-filename").Should().NotBeNull();
    }

    [Fact]
    public void Img_Tag_Has_Lazy_Loading()
    {
        // Arrange
        var image = CreateTestImage("/path/img.jpg", "img.jpg");
        image.ThumbnailDataUrl = "data:image/jpeg;base64,abc123";
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var img = cut.Find(".thumbnail-image-wrapper img");
        img.GetAttribute("loading").Should().Be("lazy");
    }

    [Fact]
    public void Img_Tag_Alt_Text_Matches_Image_Name()
    {
        // Arrange
        var image = CreateTestImage("/path/photo.jpg", "photo.jpg");
        image.ThumbnailDataUrl = "data:image/jpeg;base64,xyz";
        var images = new List<ImageInfo> { image };

        // Act
        var cut = _ctx.RenderComponent<ThumbnailGrid>(parameters => parameters
            .Add(p => p.Images, images));

        // Assert
        var img = cut.Find(".thumbnail-image-wrapper img");
        img.GetAttribute("alt").Should().Be("photo.jpg");
    }
}
