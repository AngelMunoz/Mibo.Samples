namespace FPSSample

open System
open System.Numerics
open Mibo.Elmish
open Mibo.Input

/// Game initialization, subscription setup, and input map configuration.
/// The <c>create</c> function returns the three core functions (init, update, subscribe)
/// that each backend client plugs into its own Program builder.
module Game =

  open Types

  /// The default input map, expressed in backend-neutral triggers (KeyCode).
  let inputMap: InputMap<GameAction> =
    InputMap.empty
    |> InputMap.key GameAction.MoveForward KeyCode.W
    |> InputMap.key GameAction.MoveForward KeyCode.Up
    |> InputMap.key GameAction.MoveBackward KeyCode.S
    |> InputMap.key GameAction.MoveBackward KeyCode.Down
    |> InputMap.key GameAction.MoveLeft KeyCode.A
    |> InputMap.key GameAction.MoveLeft KeyCode.Left
    |> InputMap.key GameAction.MoveRight KeyCode.D
    |> InputMap.key GameAction.MoveRight KeyCode.Right
    |> InputMap.key GameAction.Jump KeyCode.Space
    |> InputMap.key GameAction.Sprint KeyCode.LeftShift
    |> InputMap.key GameAction.Reload KeyCode.R

  /// Creates the initial GameModel from a level definition. Sub-models are
  /// constructed by the GameModel default constructor; this seeds the enemy and
  /// pickup arrays from the level and shares the colliders array with the
  /// enemy model (used for enemy-vs-wall resolution).
  let initModel(level: Level.LevelData) : GameModel =
    let model = GameModel()
    model.Level <- level
    let colliders = Level.LevelData.extractColliders level
    model.Colliders <- colliders
    model.Enemy.Colliders <- colliders
    model.Player.Position <- level.PlayerSpawn

    model.Enemy.Enemies <-
      level.EnemySpawns |> Array.map(fun s -> Enemy.create s.Position)

    model.Pickup.Pickups <-
      level.PickupSpawns |> Array.map(fun s -> Pickup.create s.Kind s.Position)

    model

  /// Init function for the Elmish program. Initializes the level and model,
  /// then triggers per-enemy animation setup via the env's animation service.
  let init (env: Env) (ctx: GameContext) : struct (GameModel * Cmd<Msg>) =
    let level = Level.LevelData.createDefault()
    let model = initModel level
    env.Animation.Init(ctx, model.Enemy.Enemies.Length)
    struct (model, Cmd.none)

  /// Backend-neutral subscription for mouse look (PositionDelta → yaw/pitch).
  /// Each backend client batches this with its InputMapper subscription.
  let subscribeMouseLook(ctx: GameContext) : Sub<Msg> =
    Mouse.listen
      (fun delta -> Msg.MouseLook(delta.PositionDelta.X, delta.PositionDelta.Y))
      ctx

  /// Backend-neutral subscription for mouse buttons (left=shoot, right=reload).
  /// Each backend client batches this with its InputMapper subscription.
  let subscribeMouseButtons(ctx: GameContext) : Sub<Msg> =
    Mouse.onButton
      (fun (btn, _) ->
        match btn with
        | MouseButtonCode.Left -> Msg.Shoot
        | MouseButtonCode.Right -> Msg.Reload
        | _ -> Msg.Reload)
      ctx
