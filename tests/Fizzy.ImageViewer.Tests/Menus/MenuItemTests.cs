using Fizzy.ImageViewer.Menus;
using Xunit;

namespace Fizzy.ImageViewer.Tests;

public class MenuItemTests
{
    [Fact]
    public void RequiredDelegatesAreValidatedBeforeAnyInteraction()
    {
        Assert.Throws<ArgumentNullException>("action", () => new MenuItem("item", (Action)null!));
        Assert.Throws<ArgumentNullException>("action", () => new MenuItem("item", (Func<ValueTask>)null!));
        Assert.Throws<ArgumentNullException>("action", () => new CheckableMenuItem("item", () => false, (Action)null!));
        Assert.Throws<ArgumentNullException>("action", () => new CheckableMenuItem("item", () => false, (Func<ValueTask>)null!));
        Assert.Throws<ArgumentNullException>("isChecked", () => new CheckableMenuItem("item", null!, (Action)(() => { })));
        Assert.Throws<ArgumentNullException>("isChecked", () => new CheckableMenuItem("item", null!, () => ValueTask.CompletedTask));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void BlankHeadersAreRejectedByEveryOverload(string? header)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MenuItem(header!, (Action)(() => { })));
        Assert.ThrowsAny<ArgumentException>(() => new MenuItem(header!, () => ValueTask.CompletedTask));
        Assert.ThrowsAny<ArgumentException>(() => new CheckableMenuItem(header!, () => false, (Action)(() => { })));
        Assert.ThrowsAny<ArgumentException>(() => new CheckableMenuItem(header!, () => false, () => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task ConstructionPreservesHeaderAndDefersCallbacks()
    {
        int executions = 0, checks = 0;
        var item = new MenuItem("  _Run  ", () => { executions++; });
        var checkable = new CheckableMenuItem("Toggle", () => { checks++; return true; },
            () => { executions++; return ValueTask.CompletedTask; });
        Assert.Equal(0, executions);
        Assert.Equal(0, checks);
        Assert.Equal("  _Run  ", item.Header);
        Assert.True(item.IsVisible);
        Assert.True(checkable.IsVisible);
        Assert.True(checkable.IsChecked);
        Assert.Equal(1, checks);
        await item.ExecuteAsync();
        await checkable.ExecuteAsync();
        Assert.Equal(2, executions);
    }
}
