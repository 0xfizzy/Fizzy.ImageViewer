namespace Fizzy.ImageViewer.Menus;

/// <summary>Registers borrowed menu objects and returns independently revocable registration handles.</summary>
public interface IViewerMenu
{
    /// <summary>
    /// 注册自定义菜单项；释放返回句柄撤销本次注册，不释放菜单对象。
    /// 句柄可从任意线程重复释放，查看器关闭后释放仍然安全。
    /// </summary>
    IDisposable RegisterMenuItem(IMenuItem menuItem);
}
