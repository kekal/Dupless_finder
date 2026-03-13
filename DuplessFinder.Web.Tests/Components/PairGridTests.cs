using Bunit;
using DuplessFinder.Web.Components;
using DuplessFinder.Web.Models;
using DuplessFinder.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DuplessFinder.Web.Tests.Components;

public class PairGridTests : IDisposable
{
    private readonly TestContext _ctx;

    public PairGridTests()
    {
        _ctx = new TestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private ImageInfo CreateTestImage(string path, string name)
    {
        var entry = new FileEntry(path, name, 1000, 12345);
        return new ImageInfo(entry);
    }

    private ImagePair CreateTestPair(ImageInfo img1, ImageInfo img2, double score = 95.5)
    {
        return new ImagePair
        {
            Image1 = img1,
            Image2 = img2,
            Score = score
        };
    }

    private EventCallback<ImageInfo> CreateDeleteCallback(Action<ImageInfo> callback)
    {
        async Task Handler(ImageInfo img) => callback(img);
        return EventCallback.Factory.Create<ImageInfo>(this, Handler);
    }

    private EventCallback<(ImageInfo, ImageInfo?)> CreatePreviewCallback(Action<(ImageInfo, ImageInfo?)> callback)
    {
        async Task Handler((ImageInfo, ImageInfo?) tuple) => callback(tuple);
        return EventCallback.Factory.Create<(ImageInfo, ImageInfo?)>(this, Handler);
    }

    [Fact]
    public void Renders_Empty_Grid_When_Pairs_Empty()
    {
        // Arrange & Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, new List<ImagePair>()));

        // Assert
        cut.Find(".pair-grid").Should().NotBeNull();
        cut.FindAll(".pair-row").Should().BeEmpty();
    }

    [Fact]
    public void Renders_Pair_Rows_For_Given_Pairs()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        var img4 = CreateTestImage("/path/img4.jpg", "img4.jpg");

        var pairs = new List<ImagePair>
        {
            CreateTestPair(img1, img2),
            CreateTestPair(img3, img4)
        };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        cut.FindAll(".pair-row").Count.Should().Be(2);
    }

    [Fact]
    public void Displays_Score_Between_Pair_Images()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2, 87.3);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var scoreSpan = cut.Find(".pair-score span");
        scoreSpan.TextContent.Should().Be("87.3");
    }

    [Fact]
    public void Shows_Image_Names_For_Both_Images_In_Pair()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var filenames = cut.FindAll(".pair-filename");
        filenames[0].TextContent.Should().Be("img1.jpg");
        filenames[1].TextContent.Should().Be("img2.jpg");
    }

    [Fact]
    public void Shows_Loading_Placeholder_When_Thumbnails_Null()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        // Both images have null ThumbnailDataUrl
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var placeholders = cut.FindAll(".thumbnail-placeholder");
        placeholders.Count.Should().Be(2);
        foreach (var p in placeholders)
        {
            p.TextContent.Should().Be("Loading...");
        }
    }

    [Fact]
    public void Shows_Img_Tags_When_Thumbnails_Set()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        img1.ThumbnailDataUrl = "data:image/jpeg;base64,data1";

        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        img2.ThumbnailDataUrl = "data:image/jpeg;base64,data2";

        var pair = CreateTestPair(img1, img2);
        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var imgs = cut.FindAll(".pair-image-wrapper img");
        imgs.Count.Should().Be(2);
        imgs[0].GetAttribute("src").Should().Be("data:image/jpeg;base64,data1");
        imgs[1].GetAttribute("src").Should().Be("data:image/jpeg;base64,data2");
    }

    [Fact]
    public async Task Delete_Button_On_Image1_Invokes_OnDeleteImage_With_Image1()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        ImageInfo? deletedImage = null;
        var callback = CreateDeleteCallback(img => { deletedImage = img; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnDeleteImage, callback));

        // Act
        var deleteButtons = cut.FindAll(".delete-btn");
        await cut.InvokeAsync(() => deleteButtons[0].Click());

        // Assert
        deletedImage.Should().NotBeNull();
        deletedImage!.Name.Should().Be("img1.jpg");
    }

    [Fact]
    public async Task Delete_Button_On_Image2_Invokes_OnDeleteImage_With_Image2()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        ImageInfo? deletedImage = null;
        var callback = CreateDeleteCallback(img => { deletedImage = img; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnDeleteImage, callback));

        // Act
        var deleteButtons = cut.FindAll(".delete-btn");
        await cut.InvokeAsync(() => deleteButtons[1].Click());

        // Assert
        deletedImage.Should().NotBeNull();
        deletedImage!.Name.Should().Be("img2.jpg");
    }

    [Fact]
    public async Task Clicking_Image1_Wrapper_Invokes_OnPreviewImage_With_Correct_Tuple()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        (ImageInfo? primary, ImageInfo? alternate) invokedTuple = (null, null);
        var callback = CreatePreviewCallback(t => { invokedTuple = t; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnPreviewImage, callback));

        // Act
        var imageWrappers = cut.FindAll(".pair-image-wrapper");
        await cut.InvokeAsync(() => imageWrappers[0].Click());

        // Assert
        invokedTuple.primary.Should().NotBeNull();
        invokedTuple.primary!.Name.Should().Be("img1.jpg");
        invokedTuple.alternate.Should().NotBeNull();
        invokedTuple.alternate!.Name.Should().Be("img2.jpg");
    }

    [Fact]
    public async Task Clicking_Image2_Wrapper_Invokes_OnPreviewImage_With_Correct_Tuple()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        (ImageInfo? primary, ImageInfo? alternate) invokedTuple = (null, null);
        var callback = CreatePreviewCallback(t => { invokedTuple = t; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnPreviewImage, callback));

        // Act
        var imageWrappers = cut.FindAll(".pair-image-wrapper");
        await cut.InvokeAsync(() => imageWrappers[1].Click());

        // Assert
        invokedTuple.primary.Should().NotBeNull();
        invokedTuple.primary!.Name.Should().Be("img2.jpg");
        invokedTuple.alternate.Should().NotBeNull();
        invokedTuple.alternate!.Name.Should().Be("img1.jpg");
    }

    [Fact]
    public void Displays_Image_Paths_In_Title_Attributes()
    {
        // Arrange
        const string path1 = "/very/long/path/to/first/image.jpg";
        const string path2 = "/very/long/path/to/second/image.jpg";

        var img1 = CreateTestImage(path1, "img1.jpg");
        var img2 = CreateTestImage(path2, "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var filenames = cut.FindAll(".pair-filename");
        filenames[0].GetAttribute("title").Should().Be(path1);
        filenames[1].GetAttribute("title").Should().Be(path2);
    }

    [Fact]
    public async Task Multiple_Pairs_Delete_Correct_Images()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        var img4 = CreateTestImage("/path/img4.jpg", "img4.jpg");

        var pairs = new List<ImagePair>
        {
            CreateTestPair(img1, img2),
            CreateTestPair(img3, img4)
        };

        ImageInfo? deletedImage = null;
        var callback = CreateDeleteCallback(img => { deletedImage = img; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnDeleteImage, callback));

        // Act - Delete first image of second pair
        var deleteButtons = cut.FindAll(".delete-btn");
        await cut.InvokeAsync(() => deleteButtons[2].Click());

        // Assert
        deletedImage.Should().NotBeNull();
        deletedImage!.Name.Should().Be("img3.jpg");
    }

    [Fact]
    public async Task Multiple_Pairs_Preview_Correct_Images()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var img3 = CreateTestImage("/path/img3.jpg", "img3.jpg");
        var img4 = CreateTestImage("/path/img4.jpg", "img4.jpg");

        var pairs = new List<ImagePair>
        {
            CreateTestPair(img1, img2),
            CreateTestPair(img3, img4)
        };

        (ImageInfo? primary, ImageInfo? alternate) invokedTuple = (null, null);
        var callback = CreatePreviewCallback(t => { invokedTuple = t; });

        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs)
            .Add(p => p.OnPreviewImage, callback));

        // Act - Click second image of second pair
        var imageWrappers = cut.FindAll(".pair-image-wrapper");
        await cut.InvokeAsync(() => imageWrappers[3].Click());

        // Assert
        invokedTuple.primary!.Name.Should().Be("img4.jpg");
        invokedTuple.alternate!.Name.Should().Be("img3.jpg");
    }

    [Fact]
    public void Mixed_Loaded_And_Unloaded_Thumbnails_In_Pair()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        img1.ThumbnailDataUrl = "data:image/jpeg;base64,data1";

        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        // img2 has no thumbnail

        var pair = CreateTestPair(img1, img2);
        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var imgs = cut.FindAll(".pair-image-wrapper img");
        imgs.Count.Should().Be(1);

        var placeholders = cut.FindAll(".thumbnail-placeholder");
        placeholders.Count.Should().Be(1);
    }

    [Fact]
    public void Score_Display_Format_Is_One_Decimal_Place()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");

        var pairs = new List<ImagePair>
        {
            CreateTestPair(img1, img2, 99.999),
            CreateTestPair(img1, img2, 50.0),
            CreateTestPair(img1, img2, 12.1)
        };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var scoreSpans = cut.FindAll(".pair-score span");
        scoreSpans[0].TextContent.Should().Be("100.0");
        scoreSpans[1].TextContent.Should().Be("50.0");
        scoreSpans[2].TextContent.Should().Be("12.1");
    }

    [Fact]
    public void Pair_Row_Renders_With_Correct_Structure()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert - verify row has expected child structure
        var row = cut.Find(".pair-row");
        row.Should().NotBeNull();
        row.QuerySelectorAll(".pair-image-cell").Length.Should().Be(2);
        row.QuerySelector(".pair-score").Should().NotBeNull();
    }

    [Fact]
    public void Delete_Button_Has_Correct_Attributes()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var deleteButtons = cut.FindAll(".delete-btn");
        foreach (var btn in deleteButtons)
        {
            btn.TextContent.Trim().Should().Be("X");
            btn.GetAttribute("title").Should().Be("Delete image");
        }
    }

    [Fact]
    public void Image_Cells_Structure_Is_Correct()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        var pair = CreateTestPair(img1, img2);

        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var imageCells = cut.FindAll(".pair-image-cell");
        imageCells.Count.Should().Be(2);

        var scoreDiv = cut.Find(".pair-score");
        scoreDiv.Should().NotBeNull();
    }

    [Fact]
    public void Img_Tags_Have_Correct_Alt_Text()
    {
        // Arrange
        var img1 = CreateTestImage("/path/img1.jpg", "img1.jpg");
        img1.ThumbnailDataUrl = "data:image/jpeg;base64,data1";

        var img2 = CreateTestImage("/path/img2.jpg", "img2.jpg");
        img2.ThumbnailDataUrl = "data:image/jpeg;base64,data2";

        var pair = CreateTestPair(img1, img2);
        var pairs = new List<ImagePair> { pair };

        // Act
        var cut = _ctx.RenderComponent<PairGrid>(parameters => parameters
            .Add(p => p.Pairs, pairs));

        // Assert
        var imgs = cut.FindAll(".pair-image-wrapper img");
        imgs[0].GetAttribute("alt").Should().Be("img1.jpg");
        imgs[1].GetAttribute("alt").Should().Be("img2.jpg");
    }
}
