# PrimitiveGallery

A visual regression gallery for Mibo's render primitives. Every cell draws one
primitive with representative parameters; the same layout data (`Shared/Catalog.fs`)
feeds every backend, so a side-by-side comparison is apples-to-apples.

- **Layout:** Defli3D project matrix — `Shared` (adaptive shell) + `Raylib` +
  canonical `MonoDX12` (+ `Content.mgcb`) + thin `MonoVK`/`MonoGL`/`MonoDX11`
  clients linking the MonoDX12 sources.
- **Screens:** `1` = 2D shapes, `2` = 3D shapes, `3` = split (2D grid left,
  3D scene right, one frame). `Tab` cycles.
- **In scope:** the 25 2D shape commands and `Primitive3D.*` + `line3D`.
  Text is used only for cell labels; one ambient + one directional light
  illuminate the PBR shapes (lighting itself is not under test here).

## Run

```bash
dotnet run --project PrimitiveGallery/Raylib      # raylib
dotnet run --project PrimitiveGallery/MonoDX12    # DirectX 12 (Windows)
dotnet run --project PrimitiveGallery/MonoDX11    # DirectX 11 (Windows)
dotnet run --project PrimitiveGallery/MonoGL      # OpenGL
dotnet run --project PrimitiveGallery/MonoVK      # Vulkan
```

## Findings — primitive × backend

Tick each cell after a visual pass: `OK`, `BROKEN`, or `N/A` (not available
on that backend). Keep a one-line note for every non-OK entry.

### 2D shapes

| Primitive        | Raylib | MonoDX12 | MonoDX11 | MonoGL | MonoVK | Notes |
| ---              | ---    | ---      | ---      | ---    | ---    | ---   |
| fillRect         |        |          |          |        |        |       |
| rectOutline      |        |          |          |        |        |       |
| fillRectRounded  |        |          |          |        |        |       |
| rectRoundedOutline |      |          |          |        |        |       |
| rectGradientV    |        |          |          |        |        |       |
| rectGradientH    |        |          |          |        |        |       |
| rectGradient     |        |          |          |        |        |       |
| fillCircle       |        |          |          |        |        |       |
| circleOutline    |        |          |          |        |        |       |
| circleSector     |        |          |          |        |        |       |
| circleSectorOutline |     |          |          |        |        |       |
| circleGradient   |        |          |          |        |        |       |
| fillRing         |        |          |          |        |        |       |
| ringOutline      |        |          |          |        |        |       |
| fillEllipse      |        |          |          |        |        |       |
| ellipseOutline   |        |          |          |        |        |       |
| line             |        |          |          |        |        |       |
| lineThick        |        |          |          |        |        |       |
| lineStrip        |        |          |          |        |        |       |
| bezier           |        |          |          |        |        |       |
| triangle         |        |          |          |        |        |       |
| triangleFan      |        |          |          |        |        |       |
| triangleStrip    |        |          |          |        |        |       |
| fillPoly         |        |          |          |        |        |       |
| polyOutline      |        |          |          |        |        |       |

### 3D shapes

| Primitive        | Raylib | MonoDX12 | MonoDX11 | MonoGL | MonoVK | Notes |
| ---              | ---    | ---      | ---      | ---    | ---    | ---   |
| cube             |        |          |          |        |        |       |
| sphere           |        |          |          |        |        |       |
| cylinder         |        |          |          |        |        |       |
| plane            |        |          |          |        |        |       |
| torus            |        |          |          |        |        |       |
| cone             |        |          |          |        |        |       |
| line3D           |        |          |          |        |        |       |
