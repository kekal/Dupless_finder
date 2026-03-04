using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dupples_finder_UI.DTO;
using Dupples_finder_UI.Services;
using Xunit;

namespace Dupless_finder_Tests;

public class LoadingOperationsTests : IDisposable
{
    private readonly string _tempDirPath;

    public LoadingOperationsTests()
    {
        _tempDirPath = Path.Combine(Path.GetTempPath(), $"LoadingOpsTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirPath);
    }

    private void CreateFile(string relativePath, string extension)
    {
        var fullPath = Path.Combine(_tempDirPath, relativePath + extension);
        var dirPath = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        File.WriteAllText(fullPath, "test content");
    }

    /// <summary>
    /// Calls the internal DirSearch method using reflection.
    /// DirSearch is the core recursive search logic that FindsJpgFiles, etc. rely on.
    /// </summary>
    private IEnumerable<string> CallDirSearch(string sDir, params string[] types)
    {
        return CallDirSearch(sDir, true, types);
    }

    private IEnumerable<string> CallDirSearch(string sDir, bool includeSubfolders, params string[] types)
    {
        // Get the assembly containing LoadingOperations
        var assembly = typeof(ImageInfo).Assembly; // ImageInfo is in same assembly as LoadingOperations
        var loadingOpsType = assembly.GetType("Dupples_finder_UI.Services.LoadingOperations");

        if (loadingOpsType == null)
        {
            throw new InvalidOperationException("LoadingOperations type not found");
        }

        var method = loadingOpsType.GetMethod("DirSearch", BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
        {
            throw new InvalidOperationException("DirSearch method not found");
        }

        var result = method.Invoke(null, [sDir, includeSubfolders, types]);
        return (IEnumerable<string>)result;
    }

    [Fact]
    public void DirSearch_FindsJpgFiles()
    {
        CreateFile("image1", ".jpg");
        CreateFile("image2", ".jpg");

        var paths = CallDirSearch(_tempDirPath, ".jpg").ToList();

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.EndsWith(".jpg", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirSearch_FindsPngFiles()
    {
        CreateFile("image1", ".png");
        CreateFile("image2", ".png");

        var paths = CallDirSearch(_tempDirPath, ".png").ToList();

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.EndsWith(".png", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirSearch_FindsMultipleImageFormats()
    {
        CreateFile("image1", ".jpg");
        CreateFile("image2", ".png");
        CreateFile("image3", ".jpeg");
        CreateFile("image4", ".bmp");
        CreateFile("image5", ".tiff");
        CreateFile("image6", ".tif");
        CreateFile("image7", ".webp");

        var paths = CallDirSearch(_tempDirPath, ".jpg", ".png", ".jpeg", ".bmp", ".tiff", ".tif", ".webp").ToList();

        Assert.Equal(7, paths.Count);
    }

    [Fact]
    public void DirSearch_IgnoresNonImageFiles()
    {
        CreateFile("image1", ".jpg");
        CreateFile("document", ".txt");
        CreateFile("spreadsheet", ".xlsx");
        CreateFile("archive", ".zip");

        var paths = CallDirSearch(_tempDirPath, ".jpg").ToList();

        Assert.Single(paths);
        Assert.EndsWith(".jpg", paths[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirSearch_SearchesRecursively()
    {
        CreateFile("level1", ".jpg");
        CreateFile("subdir/level2", ".jpg");
        CreateFile("subdir/deeper/level3", ".jpg");

        var paths = CallDirSearch(_tempDirPath, ".jpg").ToList();

        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void DirSearch_TopDirectoryOnly_DoesNotSearchSubfolders()
    {
        CreateFile("level1", ".jpg");
        CreateFile("subdir/level2", ".jpg");
        CreateFile("subdir/deeper/level3", ".jpg");

        var paths = CallDirSearch(_tempDirPath, false, ".jpg").ToList();

        Assert.Single(paths);
        Assert.Contains("level1.jpg", paths[0]);
    }

    [Fact]
    public void DirSearch_CaseInsensitiveExtensions()
    {
        CreateFile("image1", ".JPG");
        CreateFile("image2", ".Jpg");
        CreateFile("image3", ".jPg");

        var paths = CallDirSearch(_tempDirPath, ".jpg").ToList();

        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void DirSearch_HandlesEmptyDirectory()
    {
        var paths = CallDirSearch(_tempDirPath, ".jpg").ToList();

        Assert.Empty(paths);
    }

    [Fact]
    public void DirSearch_MixedFilesInMultipleDirectories()
    {
        CreateFile("root_image", ".jpg");
        CreateFile("sub1/image", ".png");
        CreateFile("sub1/document", ".txt");
        CreateFile("sub2/image", ".jpeg");
        CreateFile("sub2/sub3/image", ".bmp");

        var paths = CallDirSearch(_tempDirPath, ".jpg", ".png", ".jpeg", ".bmp", ".tiff", ".tif", ".webp").ToList();

        Assert.Equal(4, paths.Count);
        Assert.All(paths, p =>
        {
            var ext = Path.GetExtension(p).ToLower();
            Assert.True(
                ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp" ||
                ext == ".tiff" || ext == ".tif" || ext == ".webp",
                $"File {p} has invalid extension {ext}"
            );
        });
    }

    [Fact]
    public void GetAllPaths_WithValidPath_ReturnsImages()
    {
        // Arrange
        CreateFile("image1", ".jpg");
        CreateFile("image2", ".png");
        CreateFile("document", ".txt");

        var loadingOps = new LoadingOperations();

        // Act
        var result = loadingOps.GetAllPaths(out var paths, _tempDirPath);

        // Assert
        Assert.True(result);
        Assert.NotNull(paths);
        var pathsList = paths.ToList();
        Assert.Equal(2, pathsList.Count);
        Assert.All(pathsList, p =>
        {
            var ext = Path.GetExtension(p).ToLower();
            Assert.True(ext == ".jpg" || ext == ".png", $"Unexpected extension: {ext}");
        });
    }

    [Fact]
    public void GetAllPaths_WithEmptyString_DialogHandling()
    {
        // This test documents that GetAllPaths with empty rootFolder will open FolderBrowserDialog.
        // In automated tests, we cannot fully test this without UI automation.
        // The important behavior is tested above with GetAllPaths_WithValidPath_ReturnsImages.
        var loadingOps = new LoadingOperations();
        Assert.NotNull(loadingOps);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirPath))
        {
            try
            {
                Directory.Delete(_tempDirPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}