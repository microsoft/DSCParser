using DSCParser.PSDSC;
using Xunit;

namespace DSCParser.Tests;

/// <summary>
/// Calls through the <c>DscClassCacheReflection</c> wrappers. The wrappers are designed to tolerate
/// the internal DscClassCache type being absent, so these tests assert that every wrapper completes
/// without throwing in either environment. Engine mutation is cleaned up afterwards so that cache
/// state never leaks into the rest of the suite.
/// </summary>
public class DscClassCacheReflectionTests
{
    [Fact]
    public void IsDscClassCacheAvailable_ShouldNotThrow()
    {
        _ = DscClassCacheReflection.IsDscClassCacheAvailable;
    }

    [Fact]
    public void LoadDefaultCimKeywords_ShouldNotThrow()
    {
        try
        {
            DscClassCacheReflection.LoadDefaultCimKeywords();
        }
        finally
        {
            DscClassCacheReflection.ResetDynamicKeywords();
            DscKeywordRegistry.Reset();
        }
    }

    [Fact]
    public void GetCachedClassByFileName_WithUnknownFile_ShouldReturnEmptyList()
    {
        var result = DscClassCacheReflection.GetCachedClassByFileName("__NoSuchSchemaFile__");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetCachedKeywords_ShouldNotThrow()
    {
        _ = DscClassCacheReflection.GetCachedKeywords();
    }

    [Fact]
    public void GetFileDefiningClass_WithUnknownClass_ShouldNotThrow()
    {
        _ = DscClassCacheReflection.GetFileDefiningClass("__NoSuchClass__");
    }

    [Fact]
    public void HasCachedClass_WithUnknownClass_ShouldNotThrow()
    {
        _ = DscClassCacheReflection.HasCachedClass("__NoSuchClass__");
    }

    [Fact]
    public void ClearCache_ShouldNotThrow()
    {
        try
        {
            DscClassCacheReflection.ClearCache();
        }
        finally
        {
            DscKeywordRegistry.Reset();
        }
    }
}