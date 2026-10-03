/// The numbers LiveMap is built from, in one place, so a reader can
/// change a feel without reading the rest of the sample.
module LiveMap.Constants

open Mibo.Elmish.Graphics2D

/// The window.
[<Literal>]
let windowWidth = 1280

[<Literal>]
let windowHeight = 720

/// The 2D view draws one cell as a square of this many pixels at zoom 1.
[<Literal>]
let cellPixels = 48.0f

/// The 2D camera: how fast the arrow keys move the view at zoom 1, and
/// how far the zoom keys take it.
let panCellsPerSecond = 16.0f
let zoomPerSecond = 1.6f
let minZoom = 0.2f
let maxZoom = 6.0f

/// The 3D view draws one cell as a square of this many world units, and a
/// column one cell tall is this many units high.
[<Literal>]
let cellSize = 1.0f

/// The 3D orbit camera: the angle it starts at, how far it may go, how
/// fast the arrow keys slide its target, and how fast Q and E turn it.
let orbitYaw = 0.7f
let orbitPitch = 0.85f
let orbitDistance = 24.0f
let minOrbitDistance = 6.0f
let maxOrbitDistance = 80.0f
let orbitPanUnitsPerSecond = 10.0f
let orbitYawPerSecond = 1.6f

/// The layers the 2D pass draws in: the camera opens at `mapLayer`, the
/// cells sit above it, the hover highlight above them, the camera closes
/// at `overlayLayer`, and the overlay rides on top.
let mapLayer: int<RenderLayer> = 0<RenderLayer>
let spriteLayer: int<RenderLayer> = 10<RenderLayer>
let hoverFillLayer: int<RenderLayer> = 100<RenderLayer>
let hoverCellLayer: int<RenderLayer> = 101<RenderLayer>
let hoverRegionLayer: int<RenderLayer> = 102<RenderLayer>
let overlayLayer: int<RenderLayer> = 200<RenderLayer>
let hudPanelLayer: int<RenderLayer> = 1000<RenderLayer>
let hudLayer: int<RenderLayer> = 1001<RenderLayer>

/// The render layer of one map layer: the document's layer 0 draws at
/// `spriteLayer`, layer 1 above it, and so on, so the document's own
/// order is the draw order and an upper grid paints over the one below.
let mapLayerOf(index: int) : int<RenderLayer> =
  spriteLayer + max 0 index * 1<RenderLayer>
