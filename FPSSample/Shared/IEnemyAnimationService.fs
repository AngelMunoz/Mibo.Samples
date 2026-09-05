namespace FPSSample

open Mibo.Elmish
open FPSSample.Types

/// <summary>
/// Service interface for per-enemy 3D animation, implemented by each backend.
/// The shared program wires this into the system pipeline so animation playback
/// is driven the same way on every backend — only the concrete animation types
/// differ (raylib's Animation3DState vs MonoGame's AnimatedModel).
/// </summary>
type IEnemyAnimationService =

  /// <summary>
  /// Called once at init to create per-enemy animation states.
  /// Each enemy gets its own animation state; different characters are cycled for variety.
  /// </summary>
  abstract Init: ctx: GameContext * enemyCount: int -> unit

  /// <summary>
  /// Called each frame during the system pipeline (after enemy AI updates
  /// <c>CurrentAnim</c>) to advance animation playback for all living enemies.
  /// </summary>
  abstract Update: dt: float32 * enemies: Enemy[] -> unit

/// <summary>
/// Service interface for audio playback, implemented by each backend. The
/// implementations delegate playback to the host-registered framework audio
/// service (<c>Mibo.Audio.IAudio</c>, plus MonoGame's 3D surface) — the sample
/// owns only intent: translating events and snapshots into keys and voices.
/// One-shot sounds route through <c>Consume</c>; looping footsteps are derived
/// from the snapshot in <c>Update</c> and re-triggered on the clip's length.
/// </summary>
type IAudioService =

  /// <summary>
  /// Called once at init to resolve the framework audio service from the
  /// game context and cache the per-frame player frame.
  /// </summary>
  abstract Init: ctx: GameContext -> unit

  /// <summary>
  /// Consumes a single one-shot audio event (fire, reload, robotic, bite,
  /// laugh, injured, gasp). The router dispatches each <c>AudioMsg</c> emitted
  /// via <c>Cmd</c> here. <c>AudioMsg</c> carries the bank key to play plus the
  /// emitter world position and whether it is positional. The service owns its
  /// internal state (listener frame, footstep timers) entirely outside Elmish.
  /// </summary>
  abstract Consume: audioMsg: AudioMsg -> unit

  /// <summary>
  /// Called each frame during the system pipeline (after the snapshot) to:
  /// <list type="bullet">
  /// <item>Manage looping footsteps based on player/enemy velocity and
  /// <c>IsChasing</c> (read from the snapshot, not from model flags) by
  /// re-triggering the footstep keys on the clip length</item>
  /// <item>Apply positional voices for enemy footsteps (MonoGame
  /// <c>Play3D</c>, raylib manual distance/pan into a <c>Voice</c>)</item>
  /// </list>
  /// </summary>
  abstract Update: dt: float32 * snapshot: Snapshot -> unit

/// <summary>
/// The environment (composition root) carrying all backend-specific services.
/// Created once per backend before the program starts, then captured by
/// <c>init</c>/<c>update</c>/<c>view</c> via partial application.
/// </summary>
type Env = {
  Animation: IEnemyAnimationService
  Audio: IAudioService
}
