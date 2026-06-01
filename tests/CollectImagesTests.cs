using Xunit;
using ThumbnailPrimer;

namespace ThumbnailPrimer.Tests;

/// <summary>
/// Unit tests for ThumbnailCachePrimer.CollectImages.
/// No COM, no Windows Shell — pure filesystem logic only.
/// </summary>
public sealed class CollectImagesTests : IDisposable
{
    private readonly string _dir;

    public CollectImagesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ThumbnailPrimerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ── extension filtering ──────────────────────────────────────────────────

    [Fact]
    public void ReturnsEmpty_WhenFolderContainsNoImages()
    {
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "text");
        File.WriteAllText(Path.Combine(_dir, "data.csv"), "col1,col2");
        File.WriteAllText(Path.Combine(_dir, "archive.zip"), "zip");

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsEmpty_WhenFolderIsEmpty()
    {
        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    [InlineData(".webp")]
    [InlineData(".heic")]
    [InlineData(".heif")]
    public void AcceptsAllSupportedExtensions(string extension)
    {
        var path = Path.Combine(_dir, $"image{extension}");
        File.WriteAllBytes(path, []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Single(result);
        Assert.Equal(path, result[0]);
    }

    [Fact]
    public void ExtensionMatchIsCaseInsensitive()
    {
        File.WriteAllBytes(Path.Combine(_dir, "A.JPG"), []);
        File.WriteAllBytes(Path.Combine(_dir, "B.Png"), []);
        File.WriteAllBytes(Path.Combine(_dir, "C.JPEG"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ExcludesNonImageFiles_WhenMixedWithImages()
    {
        File.WriteAllBytes(Path.Combine(_dir, "photo.jpg"), []);
        File.WriteAllText(Path.Combine(_dir, "document.pdf"), "pdf");
        File.WriteAllText(Path.Combine(_dir, "video.mp4"), "mp4");

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Single(result);
    }

    [Fact]
    public void ImageExtensionsSet_ContainsAllExpectedExtensions()
    {
        string[] expected = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif"];
        foreach (var ext in expected)
            Assert.True(ThumbnailCachePrimer.ImageExtensions.Contains(ext), $"{ext} missing from ImageExtensions");

        Assert.Equal(expected.Length, ThumbnailCachePrimer.ImageExtensions.Count);
    }

    // ── recursion ────────────────────────────────────────────────────────────

    [Fact]
    public void NonRecursive_DoesNotReturnSubdirectoryFiles()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(_dir, "top.jpg"), []);
        File.WriteAllBytes(Path.Combine(sub, "nested.jpg"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Single(result);
        Assert.Contains("top.jpg", result[0]);
    }

    [Fact]
    public void Recursive_ReturnsFilesFromSubdirectories()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(_dir, "top.jpg"), []);
        File.WriteAllBytes(Path.Combine(sub, "nested.jpg"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Recursive_HandlesDeepNesting()
    {
        var deep = Path.Combine(_dir, "a", "b", "c", "d");
        Directory.CreateDirectory(deep);
        File.WriteAllBytes(Path.Combine(deep, "deep.png"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: true);

        Assert.Single(result);
        Assert.Contains("deep.png", result[0]);
    }

    [Fact]
    public void CollectImages_DoesNotThrow_ForNormalFolderTree()
    {
        var sub = Path.Combine(_dir, "album");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(_dir, "root.jpg"), []);
        File.WriteAllBytes(Path.Combine(sub, "sub.jpg"), []);

        var ex = Record.Exception(() => ThumbnailCachePrimer.CollectImages(_dir, recurse: true));

        Assert.Null(ex);
    }

    // ── count correctness ─────────────────────────────────────────────────────

    [Fact]
    public void ReturnsCorrectCount_ForManyFiles()
    {
        for (int i = 0; i < 25; i++)
            File.WriteAllBytes(Path.Combine(_dir, $"img_{i:D2}.jpg"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: false);

        Assert.Equal(25, result.Count);
    }

    [Fact]
    public void RecursiveCount_SumsTopLevelAndAllSubdirectories()
    {
        var sub1 = Path.Combine(_dir, "album1");
        var sub2 = Path.Combine(_dir, "album2");
        Directory.CreateDirectory(sub1);
        Directory.CreateDirectory(sub2);

        for (int i = 0; i < 5;  i++) File.WriteAllBytes(Path.Combine(_dir,  $"r{i}.jpg"), []);
        for (int i = 0; i < 10; i++) File.WriteAllBytes(Path.Combine(sub1, $"a{i}.jpg"), []);
        for (int i = 0; i < 7;  i++) File.WriteAllBytes(Path.Combine(sub2, $"b{i}.jpg"), []);

        var result = ThumbnailCachePrimer.CollectImages(_dir, recurse: true);

        Assert.Equal(22, result.Count);
    }
}
