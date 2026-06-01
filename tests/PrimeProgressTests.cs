using Xunit;
using ThumbnailPrimer;

namespace ThumbnailPrimer.Tests;

/// <summary>Unit tests for the PrimeProgress record.</summary>
public sealed class PrimeProgressTests
{
    [Fact]
    public void SkippedFile_IsNullByDefault()
    {
        var p = new PrimeProgress(Done: 5, Total: 10, ErrorCount: 0, LastFile: "image.jpg");
        Assert.Null(p.SkippedFile);
    }

    [Fact]
    public void SkippedFile_IsSet_WhenProvided()
    {
        var p = new PrimeProgress(5, 10, 1, "bad.jpg", SkippedFile: "bad.jpg");
        Assert.Equal("bad.jpg", p.SkippedFile);
    }

    [Fact]
    public void AllProperties_AreSetCorrectly()
    {
        var p = new PrimeProgress(Done: 7, Total: 20, ErrorCount: 2, LastFile: "photo.png", SkippedFile: "photo.png");
        Assert.Equal(7,           p.Done);
        Assert.Equal(20,          p.Total);
        Assert.Equal(2,           p.ErrorCount);
        Assert.Equal("photo.png", p.LastFile);
        Assert.Equal("photo.png", p.SkippedFile);
    }

    [Fact]
    public void RecordEquality_HoldsForIdenticalValues()
    {
        var a = new PrimeProgress(3, 10, 0, "a.jpg");
        var b = new PrimeProgress(3, 10, 0, "a.jpg");
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_FailsForDifferentDone()
    {
        var a = new PrimeProgress(3, 10, 0, "a.jpg");
        var b = new PrimeProgress(4, 10, 0, "a.jpg");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RecordEquality_FailsWhenSkippedFileDiffers()
    {
        var a = new PrimeProgress(5, 10, 1, "x.jpg", SkippedFile: "x.jpg");
        var b = new PrimeProgress(5, 10, 1, "x.jpg", SkippedFile: null);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_ProducesUpdatedRecord()
    {
        var original = new PrimeProgress(1, 5, 0, "first.jpg");
        var updated  = original with { Done = 2, LastFile = "second.jpg" };

        Assert.Equal(2,             updated.Done);
        Assert.Equal("second.jpg",  updated.LastFile);
        Assert.Equal(5,             updated.Total);      // unchanged
        Assert.Equal(0,             updated.ErrorCount); // unchanged
    }
}
