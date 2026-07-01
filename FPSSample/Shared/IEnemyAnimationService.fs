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
/// The shared program wires this into the system pipeline (after animation)
/// so one-shot sounds, looping footsteps, and positional 3D audio are driven
/// the same way on every backend — only the concrete audio APIs differ
/// (raylib's manual distance/pan vs MonoGame's native Apply3D).
/// </summary>
type IAudioService =

  /// <summary>
  /// Called once at init to preload sound assets and initialize audio state.
  /// </summary>
  abstract Init: ctx: GameContext -> unit

  /// <summary>
  /// Called each frame during the system pipeline (after animation) to:
  /// - Drain the <c>SoundQueue</c> (one-shot sounds: robotic, bite, laugh, etc.)
  /// - Manage looping footstep instances based on <c>IsPlayerWalking</c> /
  ///   <c>IsChasing</c> flags
  /// - Apply positional distance attenuation + stereo pan
  /// </summary>
  abstract Update: dt: float32 * model: GameModel -> unit

/// <summary>
/// The environment (composition root) carrying all backend-specific services.
/// Created once per backend before the program starts, then captured by
/// <c>init</c>/<c>update</c>/<c>view</c> via partial application.
/// </summary>
type Env = {
  Animation: IEnemyAnimationService
  Audio: IAudioService
}
