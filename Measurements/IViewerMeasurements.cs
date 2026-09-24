namespace Fizzy.ImageViewer.Measurements;

/// <summary>Measurement tools, interaction, style and notifications on the viewer STA.</summary>
public interface IViewerMeasurements
{
    /// <summary>The entire measurement layer. Clearing it cancels interaction and removes all measurements.</summary>
    MeasurementLayer Measurements { get; }

    /// <summary>Style for future measurements. Brushes are copied and frozen before UI dispatch.</summary>
    MeasurementStyle MeasurementStyle { get; set; }

    /// <summary>
    /// 程序化启动已注册的测量工具。
    /// </summary>
    void StartMeasurement(string toolId);

    /// <summary>
    /// 结束当前创建或编辑交互，保留已完成的测量及已应用的编辑。
    /// </summary>
    void EndInteraction();

    /// <summary>Completed measurements only; raised on the viewer STA, excluding previews.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementCompleted;

    /// <summary>Removal of completed measurements; raised on the viewer STA, including closure.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementRemoved;

    /// <summary>Geometry and query snapshots plus a live editable handle after editing, publication or invalidation.</summary>
    event EventHandler<MeasurementEventArgs>? MeasurementChanged;

    /// <summary>
    /// 注册自定义测量工具；CreateSession 为每次启动创建独立会话。会话通过 IMeasurementToolContext.CreateMeasurement 创建测量并托管资源，
    /// 调用 Complete 保留完成结果；取消、删除、清空和关闭由查看器统一清理。
    /// 调度器、任意几何实现及逐帧查询注册不是公共扩展接口。
    /// </summary>
    void RegisterMeasurementTool(IMeasurementTool tool);

    /// <summary>Removes the viewer-wide registration for this ID, including an active session using it.</summary>
    bool UnregisterMeasurementTool(string toolId);
}
