namespace Fizzy.ImageViewer.Enums
{
    public enum MenuItemType
    {
        General,        // 通用选项 (常驻，如保存图片、清除所有)
        MeasureTool,    // 测量工具 (仅在空闲时显示)
        ContextAction,  // 上下文操作 (仅在测量时显示，如取消)
        SelectionAction,// 选中操作 (仅在有选中项时显示，如删除)
        Separator       // 分割线
    }
}
