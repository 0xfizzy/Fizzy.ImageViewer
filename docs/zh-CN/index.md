# Fizzy.ImageViewer 文档

[English](../en/index.md)

按应用需要选择组件指南，集成图像显示、绘制、测量与像素查询。

## 按需求选择

| 需求 | 从这里开始 |
| --- | --- |
| 升级现有应用 | [升级指南](upgrading.md) |
| 已有窗口内的图像控件 | [嵌入 WPF](embedding.md) |
| 显示第一张图像 | [入门](getting-started.md) |
| 集成相机或连续结果 | [实时流集成](live-streaming.md) |
| 编写交互工具 | [自定义测量工具](measurement-tools.md) |
| 查询类型或成员 | [英文 API 参考](../../artifacts/docs/api/index.md) |
| 修改库的实现 | [架构](architecture.md)与[验证](validation.md) |

## 入门与集成

- [入门](getting-started.md): 创建第一个图像显示应用。
- [实时流集成](live-streaming.md)：限制生产和叠加工作量。
- [Viewer 与生命周期](public-api.md): 配置 Viewer 并管理窗口生命周期。
- [应用端点](application-integration.md): DI、帧 sink 与应用显示接口。

## 帧与处理

- [帧与像素](frame-pipeline.md): 提交帧并查询原始像素。
- [CUDA 集成](cuda.md): 集成 CUDA 图像存储。
- [快照](snapshots.md): 捕获整幅图像或指定区域。

## 绘制与交互

- [绘制与 HUD](drawing-layers.md): 管理叠加绘制、图层与 HUD 内容。
- [测量](measurements.md): 使用和扩展交互测量。
- [自定义测量工具](measurement-tools.md)：创建并管理交互会话。
- [菜单](menus.md): 自定义上下文菜单。

## 开发与维护

- [架构](architecture.md): 了解模块边界与所有权。
- [验证](validation.md): 选择构建、测试与交互检查。
- [文档维护](documentation.md): 构建站点并维护翻译。
- [发布 SOP](releasing.md): 发布和恢复版本化产物。

英文 XML 和生成的 **API reference (English)** 定义符号级契约；组件指南定义跨 API 协议。中英文共用 API 标识及示例。
