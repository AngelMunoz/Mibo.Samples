namespace Defli3D

// ─────────────────────────────────────────────────────────────
// Shell — the windowed frontend's per-frame state: the middle-
// button drag flag plus the frame-diagnostics object (shell-owned;
// the WorldDiag lives on the state). Holds no gameplay state.
// Keyboard pan lives on the camera model (CameraMsg.AddKeyboardPan
// → Camera.tick); MousePos was write-only dead state — both dropped.
// ─────────────────────────────────────────────────────────────

type Shell = {
  mutable MiddleDown: bool
  Diag: FrameDiag
}
