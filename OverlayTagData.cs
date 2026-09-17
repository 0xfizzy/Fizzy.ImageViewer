using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Fizzy.ImageViewer.Editing;
using Fizzy.ImageViewer.Enums;

namespace Fizzy.ImageViewer;

/// <summary>
/// 覆盖层形状的变换数据。
/// </summary>
public sealed class OverlayTransformData
{
    /// <summary>
    /// 缩放模式。
    /// </summary>
    public OverlayScaleMode Mode { get; init; }

    /// <summary>
    /// 锚点位置（图像坐标）。
    /// </summary>
    public Point AnchorPoint { get; set; }

    /// <summary>
    /// 屏幕偏移量（用于 AnchoredLabel 模式）。
    /// </summary>
    public Vector ScreenOffset { get; set; }

    /// <summary>
    /// 缓存的 ScaleTransform 引用，避免每次缩放时类型检查。
    /// </summary>
    public ScaleTransform? CachedScaleTransform { get; set; }
}

/// <summary>
/// 覆盖层形状的选择和关联数据。
/// </summary>
public sealed class OverlaySelectionData
{
    /// <summary>
    /// 原始画刷，用于取消选中时恢复。
    /// </summary>
    public Brush? OriginalBrush { get; set; }

    /// <summary>
    /// 关联的形状列表。选中/删除主形状时，关联形状一起处理。
    /// </summary>
    public List<UIElement>? LinkedShapes { get; set; }

    /// <summary>
    /// 当形状被移除时调用的回调。
    /// </summary>
    public Action? OnRemoved { get; set; }
}

/// <summary>
/// 覆盖层形状的完整标签数据，组合变换和选择数据。
/// </summary>
public sealed class OverlayTagData(OverlayScaleMode mode, ShapeType type)
{
    public OverlayTransformData Transform { get; } = new() { Mode = mode };
    public OverlaySelectionData Selection { get; } = new();
    public EditData Edit { get; } = new();
    public ShapeType ShapeType { get; } = type;
    
    public OverlayScaleMode Mode => Transform.Mode;
    
    public Point AnchorPoint
    {
        get => Transform.AnchorPoint;
        set => Transform.AnchorPoint = value;
    }
    
    public Vector ScreenOffset
    {
        get => Transform.ScreenOffset;
        set => Transform.ScreenOffset = value;
    }
    
    public Brush? OriginalBrush
    {
        get => Selection.OriginalBrush;
        set => Selection.OriginalBrush = value;
    }
    
    public List<UIElement>? LinkedShapes
    {
        get => Selection.LinkedShapes;
        set => Selection.LinkedShapes = value;
    }
    
    public Action? OnRemoved
    {
        get => Selection.OnRemoved;
        set => Selection.OnRemoved = value;
    }
}
