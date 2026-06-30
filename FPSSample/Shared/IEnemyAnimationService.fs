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
/// The environment (composition root) carrying all backend-specific services.
/// Created once per backend before the program starts, then captured by
/// <c>init</c>/<c>update</c>/<c>view</c> via partial application.
/// </summary>
type Env = { Animation: IEnemyAnimationService }
