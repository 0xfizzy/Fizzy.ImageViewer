# Fizzy.ImageViewer documentation

[简体中文](../zh-CN/index.md)

Choose a component guide to integrate image viewing, drawing, measurements, and pixel inspection.

## Choose your path

| Need | Start here |
| --- | --- |
| A control inside an existing window | [Embed in WPF](embedding.md) |
| First image window | [Getting started](getting-started.md) |
| Camera or continuous results | [Live streaming](live-streaming.md) |
| A new interactive tool | [Custom measurement tools](measurement-tools.md) |
| A type or member | [API reference (English)](../../artifacts/docs/api/index.md) |
| Library development | [Architecture](architecture.md) and [validation](validation.md) |

## Getting started and integration

- [Getting started](getting-started.md): Build your first viewer application.
- [Live streaming](live-streaming.md): Bound producer and overlay work.
- [Viewer and lifetime](public-api.md): Configure the viewer and manage its window lifetime.

## Frames and processing

- [Frames and pixels](frame-pipeline.md): Submit frames and query original pixels.
- [CUDA integration](cuda.md): Integrate CUDA-backed image storage.
- [Snapshots](snapshots.md): Capture whole-image or region snapshots.

## Drawing and interaction

- [Drawing and HUD](drawing-layers.md): Manage overlays, layers, and HUD content.
- [Measurements](measurements.md): Use and extend interactive measurements.
- [Custom measurement tools](measurement-tools.md): Create and own interactive sessions.
- [Menus](menus.md): Customize context menus.

## Development and maintenance

- [Architecture](architecture.md): Understand module boundaries and ownership.
- [Validation](validation.md): Choose build, test, and interaction checks.
- [Documentation maintenance](documentation.md): Build the site and maintain translations.
- [Release SOP](releasing.md): Publish and recover versioned releases.

English XML and the generated **API reference (English)** define symbol contracts; component guides define cross-API protocols. Both languages share API identifiers and examples.
