namespace Defli3D

// ─────────────────────────────────────────────────────────────
// Shell — the windowed frontend's per-frame state: the middle-
// button drag flag plus the diagnostics overlay switch. Holds no
// gameplay state. Keyboard pan lives on the camera model
// (CameraMsg.SetKeyboardPan → Camera.tick); MousePos was
// write-only dead state — both dropped. Measurement itself is
// owned by the Mibo.Diagnostics FrameProfiler; F3 flips this
// switch and the profiler's Enabled together.
// ─────────────────────────────────────────────────────────────

type Shell = {
  mutable MiddleDown: bool
  mutable ShowDiag: bool
}
