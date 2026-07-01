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
/// Service interface for audio playback, implemented by each backend.
/// The shared program wires this into the system pipeline so one-shot sounds
/// and looping footsteps are driven the same way on every backend — only the
/// concrete audio APIs differ (raylib's manual distance/pan vs MonoGame's
/// native Apply3D).
/// </summary>
type IAudioService =

  /// <summary>
  /// Called once at init to preload sound assets and initialize audio state.
  /// </summary>
  abstract Init: ctx: GameContext -> unit

  /// <summary>
  /// Consumes a single one-shot audio event (fire, reload, robotic, bite,
  /// laugh, injured, gasp). The router dispatches each <c>AudioMsg</c> emitted
  /// via <c>Cmd</c> here. <c>AudioMsg</c> carries everything the backend
  /// needs to play with positional attenuation + pan (path, emitter world
  /// position, isPositional). The service owns its own internal state (sound
  /// instances, loop handles) entirely outside Elmish.
  /// </summary>
  abstract Consume: audioMsg: AudioMsg -> unit

  /// <summary>
  /// Called each frame during the system pipeline (after the snapshot) to:
  /// <list type="bullet">
  /// <item>Manage looping footstep instances based on player/enemy velocity
  /// and <c>IsChasing</c> (read from the snapshot, not from model flags)</item>
  /// <item>Apply per-frame positional 3D updates (MonoGame <c>Apply3D</c>,
  /// raylib distance/pan)</item>
  /// </list>
  /// The service computes loop intent from the snapshot and idempotently
  /// starts/stops looping instances against its native instance state
  /// (<c>Raylib.IsSoundPlaying</c>, <c>SoundEffectInstance.State</c>) — no
  /// transition/edge detection in the systems and no audio flags in Elmish.
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
