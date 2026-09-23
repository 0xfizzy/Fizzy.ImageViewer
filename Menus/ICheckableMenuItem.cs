namespace Fizzy.ImageViewer.Interfaces;

/// <summary>
/// 可勾选的菜单项接口。
/// </summary>
public interface ICheckableMenuItem : IMenuItem
{
    /// <summary>
    /// 当前是否选中。
    /// </summary>
    bool IsChecked { get; }
}
