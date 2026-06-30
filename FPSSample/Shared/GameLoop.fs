namespace FPSSample

open Mibo.Elmish
open Mibo.Input
open FPSSample.Types
open FPSSample.Game
open FPSSample.Systems

/// <summary>
/// Shared Elmish program wiring following the Env (composition root) pattern.
/// Each backend creates an <c>Env</c> with its own service implementations,
/// then calls <c>GameLoop.create</c> to get the common init/update/subscribe functions.
/// </summary>
module GameLoop =

  /// <summary>
  /// Creates the init function, capturing the env. Calls the backend's
  /// pre-init hook (e.g. raylib DisableCursor), then shared Game.init
  /// (which initializes animation via env.Animation.Init).
  /// </summary>
  let createInit
    (env: Env)
    (preInit: GameContext -> unit)
    : (GameContext -> struct (GameModel * Cmd<Msg>)) =
    fun ctx ->
      preInit ctx
      Game.init env ctx

  /// <summary>
  /// Creates the update function, capturing the env. Delegates to
  /// Systems.update which runs the full system pipeline including
  /// animation via env.Animation.Update.
  /// </summary>
  let createUpdate
    (env: Env)
    : (Msg -> GameModel -> struct (GameModel * Cmd<Msg>)) =
    fun msg model -> Systems.update env msg model

  /// <summary>
  /// Creates the shared subscription function, capturing the env.
  /// The backend passes its input subscription factory (InputMapper.subscribeStatic
  /// is backend-specific); mouse look/buttons come from shared Core.
  /// </summary>
  let createSubscribe
    (inputSub: GameContext -> Sub<Msg>)
    : (GameContext -> GameModel -> Sub<Msg>) =
    fun ctx _model ->
      Sub.batch [
        inputSub ctx
        subscribeMouseLook ctx
        subscribeMouseButtons ctx
      ]
