# FPSSample — Architecture Guide

This document explains how the FPS sample is structured as an **Elmish application** where `Systems.fs` acts as a **message router** that coordinates independent sub-systems through `Cmd<Msg>` events, with a **`System` pipeline** that enforces a readonly snapshot boundary between mutation and query phases.

## The Elmish Loop as a Router

The core Elmish loop is `init → update → view`, driven by messages. In FPSSample, `Systems.fs` does **not** contain game logic — it routes messages to the appropriate sub-system and translates cross-system events into new messages, mirroring SpaceBattle's `Program.fs`.

```
  ┌──────────────────────────────────────────────────────────────────────┐
  │                          Systems.update                              │
  │                                                                      │
  │   Msg ──┬──▶ InputMapped   ──▶ model.Actions                          │
  │         ├──▶ MouseLook     ──▶ model.Player (yaw/pitch)               │
  │         ├──▶ Shoot         ──▶ Combat.handleShoot → WeaponEvent seq   │
  │         │                     router → AudioMsg + EffectMsg cmds       │
  │         ├──▶ Reload        ──▶ Combat.startReload → WeaponEvent seq   │
  │         │                     router → AudioMsg cmd                   │
  │         └──▶ Tick          ──▶ System pipeline (see below)             │
  │                                                                      │
  │   WeaponEvent ──▶ translateWeaponEvent ──▶ Cmd<Msg> (audio/effect)     │
  │   EnemyEvent  ──▶ translateEnemyEvent  ──▶ Cmd<Msg> (audio/player)     │
  │   PickupEvent ──▶ translatePickupEvent ──▶ Cmd<Msg> (player/weapon)     │
  └──────────────────────────────────────────────────────────────────────┘
```

Each sub-system owns its **model**, **message type**, **update function**, and the **events** it emits. The main `Msg` type wraps the input entry points and the cross-system sub-messages:

```fsharp
type Msg =
  | Tick of tick: GameTime
  | InputMapped of actions: ActionState<GameAction>
  | MouseLook of deltaX: float32 * deltaY: float32
  | Shoot
  | Reload
  | WeaponMsg of weaponMsg: WeaponMsg
  | EnemyMsg of enemyMsg: EnemyMsg
  | EffectMsg of effectMsg: EffectMsg
  | AudioMsg of audioMsg: AudioMsg
  | PlayerMsg of playerMsg: PlayerMsg
  | PickupMsg of pickupMsg: PickupMsg
```

## Sub-Models

`GameModel` is an aggregate of per-system sub-models, each owning one slice of state. Each system mutates **only** its own slice; cross-system communication goes through `Cmd<Msg>` events, never through model flags.

```fsharp
type GameModel() =
  member val Player = PlayerModel()   // position, velocity, yaw, pitch, health, grounded, score
  member val Weapon = WeaponModel()  // ammo, cooldowns, reload, recoil, muzzle flash, equipped weapon
  member val Effect = EffectModel()  // hit-flash timer, smoke puff pool
  member val Enemy  = EnemyModel()   // enemies[] + colliders ref
  member val Pickup = PickupModel()  // pickups[]
  member val Actions = ActionState.empty
  member val Level = ...
  member val Colliders = ...
  member val TotalTime = 0.0f
```

## Systems

| System | File | Owns | Events emitted | Purpose |
| --- | --- | --- | --- | --- |
| **Input** | `Systems.fs` | `model.Actions`, `model.Player.Yaw/Pitch` | — | Mouse look clamping, action storage |
| **Physics** | `Physics.fs` | `PlayerModel` | — | Movement, gravity, collision, jump |
| **Weapon** | `Systems.fs` + `Combat.fs` | `WeaponModel` | `WeaponEvent.Fired` / `ReloadStarted` / `EnemyKilled` | Fire/reload lifecycle, recoil, cooldown |
| **Effect** | `Systems.fs` | `EffectModel` | — | Smoke puff physics, hit-flash timer |
| **Enemy** | `EnemyAi.fs` | `EnemyModel` (enemies[]) | `EnemyEvent.PlayerDamaged` / `AttackBite` / `Robotic` / `ChildLaugh` / `EnemyKilled` | AI state machine, chase/attack, SFX timers |
| **Pickup** | `Systems.fs` | `PickupModel` | `PickupEvent.HealthPickup` / `AmmoPickup` | Proximity pickup, respawn timers |
| **Animation** | backend `View.fs` | (backend animation state) | — | Per-enemy 3D animation (readonly post-snapshot) |
| **Audio** | backend `AudioService.fs` | (backend sound instances) | — | One-shot SFX via `Consume`, looping footsteps via `Update` |

## Cross-System Communication

Systems never call each other directly. They communicate through **Events** that `Systems.update` intercepts and translates into `Cmd<Msg>` for other systems.

### WeaponEvent → Program → Audio / Effect / Player

`Combat.handleShoot` / `Combat.startReload` return `WeaponEvent` values. The router translates each into commands:

```
WeaponEvent.Fired(path, muzzlePos, dir)       ──▶ AudioMsg.OneShot(fire) + EffectMsg.SpawnSmoke + EffectMsg.MuzzleFlash
WeaponEvent.ReloadStarted(path)               ──▶ AudioMsg.OneShot(reload)
WeaponEvent.EnemyKilled(enemyPos)            ──▶ AudioMsg.OneShot(injured) + PlayerMsg.AddScore(100)
```

The weapon system **never knows** about audio or effects — it just emits events.

### EnemyEvent → Program → Audio / Player / Effect

`EnemyAi.update` returns `EnemyEvent` values. The router translates each into commands:

```
EnemyEvent.PlayerDamaged(amount)  ──▶ PlayerMsg.TakeDamage + AudioMsg.OneShot(gasp) + EffectMsg.TriggerHitFlash
EnemyEvent.AttackBite(enemyPos)   ──▶ AudioMsg.OneShot(bite)
EnemyEvent.Robotic(path, pos)    ──▶ AudioMsg.OneShot(robotic)
EnemyEvent.ChildLaugh(pos)       ──▶ AudioMsg.OneShot(childLaugh)
EnemyEvent.EnemyKilled(pos)      ──▶ AudioMsg.OneShot(injured)
```

The enemy system **never knows** about the player's health or audio — it just emits events.

### PickupEvent → Program → Player / Weapon

`pickupSystem` returns `PickupEvent` values. The router translates each into commands:

```
PickupEvent.HealthPickup ──▶ PlayerMsg.Heal(HealthPickupAmount)
PickupEvent.AmmoPickup   ──▶ WeaponMsg.RefillAmmo
```

The pickup system **never knows** about the player's health or the weapon's ammo — it just emits events.

## The Tick Pipeline (System module)

The `Tick` handler runs a type-enforced `System` pipeline so ordering is explicit and a readonly boundary separates mutation phases from query/dispatch phases:

```fsharp
| Msg.Tick gt ->
    let dt = float32 gt.ElapsedGameTime.TotalSeconds
    model.TotalTime <- model.TotalTime + dt

    // ── System pipeline: mutation → snapshot → readonly → finish ──
    System.start model
    |> System.pipeMutable (fun model -> inputSystem dt model
                                  Physics.update dt model.Player ...
                                  model, Cmd.none)
    |> System.pipeMutable (weaponSystem dt)              // cooldowns, recoil, reload
    |> System.pipeMutable (effectSystem dt)              // smoke puffs, hit-flash timer
    |> System.pipeMutable (enemySystem dt)               // AI → EnemyEvent seq → Cmd
    |> System.pipeMutable (pickupSystem dt)             // pickups → PickupEvent → Cmd
    |> System.pipeMutable (R-key reload/restart)         // input edge
    |> GameModel.toReadonly                              // ── readonly boundary ──
    |> System.pipe (fun snapshot ->                      // backend services (readonly)
        env.Animation.Update(dt, snapshot.Enemy.Enemies)
        env.Audio.Update(dt, snapshot)
        snapshot, Cmd.none)
    |> System.finish (fun _ -> model)
```

The `GameModel.toReadonly` call is a pipeline-compatible function that takes `struct (GameModel * Cmd<Msg>)` and returns `struct (Snapshot * Cmd<Msg>)` — it transitions the pipeline from the mutable `GameModel` to a readonly `Snapshot` (a struct record sharing sub-model references — zero allocation). After the boundary, only `System.pipe` (readonly) operations are allowed. This enforces at compile time that mutation phases finish before any query/dispatch phase reads state. The backend services (animation, audio) run inside the readonly `System.pipe` phase, reading from the snapshot.

## The Snapshot Boundary

`Snapshot` is a **struct record** holding the same sub-model references as `GameModel`:

```fsharp
[<Struct>]
type Snapshot = {
  Player: PlayerModel
  Weapon: WeaponModel
  Effect: EffectModel
  Enemy: EnemyModel
  Pickup: PickupModel
  Actions: ActionState<GameAction>
  Level: Level.LevelData
  Colliders: BoundingBox[]
  TotalTime: float32
}
```

It is **not** a deep copy — it shares references, so it's allocation-free in the hot path. It serves as a type-enforced boundary: post-snapshot code (the `System.dispatch` phase and the backend service `Update` calls) reads from a consistent this-frame state, never mutating it.

## Audio: Consume + Update Split

The `IAudioService` has two seams, matching how one-shot and looping sounds differ:

```fsharp
type IAudioService =
  abstract Init: ctx: GameContext -> unit
  abstract Consume: audioMsg: AudioMsg -> unit
  abstract Update: dt: float32 * snapshot: Snapshot -> unit
```

### Consume (one-shots)

`Consume` plays a single `AudioMsg.OneShot` — fire, reload, robotic, bite, child laugh, injured, gasp. The router dispatches each `AudioMsg` emitted via `Cmd` here. `AudioMsg` carries everything the backend needs:

```fsharp
type AudioMsg = | OneShot of path: string * position: Vector3 * isPositional: bool
```

The service owns its own internal state (sound instances, cached listener) entirely outside Elmish — keeping a `SoundEffectInstance` alive is not game logic. Many one-shots can fire per tick (robotic + bite + laugh + injured + gasp), so the router batches them (`Cmd.batch`) and the service drains them as the Elmish loop processes each `AudioMsg` message.

### Update (loops + positional)

`Update` runs each frame on the snapshot and handles **continuous** audio:

- **Looping footsteps** — derived from the snapshot (player horizontal velocity + `IsGrounded`; nearest enemy `IsChasing`). The service **idempotently** starts/stops looping instances against its native `isPlaying` state (`Raylib.IsSoundPlaying`, `SoundEffectInstance.State`). No transition/edge detection in the systems, no audio flags in Elmish.
- **Per-frame positional 3D updates** — MonoGame `Apply3D(listener, emitter)` for active loops; raylib manual distance/pan for positional one-shots and loops.

The `IsPlayerWalking` flag and the `SoundQueue` ring buffer are **gone**. Loop intent is computed from the snapshot each frame; one-shots arrive as `AudioMsg` events. `IsChasing` stays on the `Enemy` struct as genuine AI/behavior state (like `CurrentAnim` for animation) — the audio service reads it from the snapshot, it is not an audio flag.

## Message Flow: Shoot

Here's the full lifecycle of a shot, showing how messages and events flow through the system:

```
1. User clicks left mouse button
   │
   ▼
2. Msg.Shoot
   │  Systems.update calls Combat.handleShoot(player, weapon, enemies, colliders)
   │  handleShoot mutates weapon ammo/cooldown/recoil and returns WeaponEvent.Fired
   │  Systems.update translates the event:
   │
   ├─▶ Msg.AudioMsg(OneShot(fire, muzzlePos, non-positional))   ← fire sound
   ├─▶ Msg.EffectMsg(SpawnSmoke(muzzlePos, dir))                ← smoke puff
   └─▶ Msg.EffectMsg(MuzzleFlash)                              ← weapon muzzle flash timer
   │
   ▼
3. Elmish loop drains the batched Cmd (same frame)
   │  AudioMsg handler   ──▶ env.Audio.Consume(fire)   ──▶ backend plays sound
   │  EffectMsg handlers ──▶ spawn smoke puff, set weapon.MuzzleFlash active
   │
   ▼
4. Next Tick
   │  weaponSystem ticks down FireCooldown, MuzzleFlash timer, recoil recovery
   │  effectSystem ticks smoke puff physics (drag + buoyancy + scale)
```

## Message Flow: Reload

```
1. User presses R (or right-click)
   │
   ▼
2. Msg.Reload
   │  If game over: restartModel (resets all sub-models)
   │  Else: Combat.startReload(weapon) returns WeaponEvent.ReloadStarted(path)
   │  Systems.update translates:
   │
   └─▶ Msg.AudioMsg(OneShot(reload, non-positional))   ← reload sound
   │
   ▼
3. Next Tick(s)
   │  weaponSystem ticks down ReloadTimer
   │  When timer elapses: updateReload refills ammo to MaxAmmo, clears IsReloading
```

## Message Flow: Enemy Attack

```
1. Tick (every frame)
   │  enemySystem calls EnemyAi.update(dt, playerPos, enemies, colliders)
   │  Enemy in attack range with cooldown expired:
   │    returns EnemyEvent.PlayerDamaged(dmg), EnemyEvent.AttackBite(enemyPos)
   │  Systems.update translates:
   │
   ├─▶ Msg.PlayerMsg(TakeDamage dmg)                              ← health reduction
   ├─▶ Msg.AudioMsg(OneShot(gasp, non-positional))                ← player pain sound
   ├─▶ Msg.EffectMsg(TriggerHitFlash)                             ← screen hit-flash
   └─▶ Msg.AudioMsg(OneShot(bite, enemyPos, positional))         ← bite sound
   │
   ▼
2. Elmish loop drains the batched Cmd (same frame)
   │  PlayerMsg handler  ──▶ reduces Player.Health, sets Effect.HitEffectTimer
   │  AudioMsg handlers  ──▶ env.Audio.Consume(gasp), env.Audio.Consume(bite)
   │  EffectMsg handler  ──▶ sets Effect.HitEffectTimer
   │
   ▼
3. Backend services (readonly, post-snapshot)
   │  env.Audio.Update(dt, snapshot) ──▶ updates looping footsteps + positional pan
```

## Message Flow: Pickup

```
1. Tick (every frame)
   │  pickupSystem checks player proximity to active pickups
   │  Player walks over a health pickup:
   │    pickup.IsActive <- false, RespawnTimer set
   │    returns PickupEvent.HealthPickup
   │  Systems.update translates:
   │
   └─▶ Msg.PlayerMsg(Heal HealthPickupAmount)   ← health restored
   │
   ▼
2. Elmish loop drains the Cmd
   │  PlayerMsg handler ──▶ increases Player.Health (clamped to max)
   │
   ▼
3. Respawn
   │  pickupSystem ticks down RespawnTimer each frame
   │  When timer elapses: pickup.IsActive <- true
```

## Message Flow: Game Over + Restart

```
1. Enemy damage reduces Player.Health to <= 0
   │
   ▼
2. HudLayout.isGameOver(model) returns true
   │  HUD renders the "GAME OVER - Press R to Restart" overlay
   │
   ▼
3. User presses R
   │  subscribeMouseButtons emits Msg.Reload (right-click) OR
   │  the Tick pipeline's R-key check detects GameAction.Reload
   │
   ▼
4. Systems.update Msg.Reload (or Tick R-key branch)
   │  HudLayout.isGameOver is true → restartModel(model)
   │  restartModel resets every sub-model:
   │    Player (health, position, score), Weapon (ammo, cooldowns),
   │    Effect (hit-flash, smoke puffs), Enemy (enemies[]), Pickup (pickups[])
```

## Backend Services

The `Env` (composition root) carries two backend-specific services:

```fsharp
type Env = {
  Animation: IEnemyAnimationService
  Audio: IAudioService
}
```

### IEnemyAnimationService (readonly post-snapshot consumer)

`Update(dt, enemies)` runs after the snapshot in the readonly phase. It reads `CurrentAnim` / `State` / `Position` and mutates only its own backend animation state (raylib `Animation3DState` or MonoGame `AnimatedModel`). Emits no events. The animation service never knows about audio or weapons — it just advances animation playback to match the AI state.

### IAudioService (blended Consume + Update)

See the [Audio: Consume + Update Split](#audio-consume--update-split) section above. The raylib backend uses manual inverse-distance attenuation + stereo pan; the MonoGame backend uses native `SoundEffectInstance.Apply3D(listener, emitter)`. Both derive loop intent idempotently from the snapshot.

## Key Patterns

### Systems return events, the router orchestrates

Weapon returns `WeaponEvent`, Enemy returns `EnemyEvent`, Pickup returns `PickupEvent`. None knows about the others. `Systems.update` is the only place where cross-system wiring exists.

### Snapshot as a type-enforced boundary

The `System` pipeline's `snapshot` call transitions from mutable `GameModel` to readonly `Snapshot` (struct record, zero-allocation). Post-snapshot phases (`dispatch`, backend `Update` calls) cannot mutate — the type system enforces it.

### Mutable sub-models, immutable messages

The sub-model classes use mutable properties for performance (avoiding large immutable copies each frame). All messages and event unions are `[<Struct>]` — value types that live on the stack, so message dispatch is allocation-free even at 60fps with many events per tick.

### No audio flags in Elmish

`LastFireSound`, `LastReloadSound`, `SoundQueue`, `IsPlayerWalking` are all gone. One-shots arrive as `AudioMsg` events; loops are derived from the snapshot each frame. The audio service owns its native instance state and applies idempotent start/stop against `isPlaying` — no edge detection in the systems.

### Backend-agnostic core

`Mibo.Core` (Cmd/Sub/System/GameTime) has no raylib or MonoGame dependency. The shared `Systems.fs` / `Combat.fs` / `EnemyAi.fs` / `Physics.fs` depend only on `System.Numerics` and `Mibo.Core`. Backends differ only in `View.fs` (rendering), `AudioService.fs` (audio API), and `Program.fs` (composition root).

## File Map

```
FPSSample/
├── Shared/                     ← Backend-agnostic core
│   ├── Constants.fs            ← Tunable constants
│   ├── Assets.fs               ← Logical asset paths + weapon classes
│   ├── Level.fs                ← Voxel grid, colliders, spawns
│   ├── Types.fs                ← Sub-models, Msg, AudioMsg, Event unions, Snapshot
│   ├── IEnemyAnimationService.fs ← Service interfaces + Env
│   ├── ViewMath.fs             ← Shared view math (camera, lights, weapon pos)
│   ├── HudLayout.fs            ← HUD layout (reads sub-model fields)
│   ├── Physics.fs              ← Player physics (operates on PlayerModel)
│   ├── Combat.fs               ← Shooting/reloading (returns WeaponEvent)
│   ├── EnemyAi.fs              ← Enemy AI (returns EnemyEvent)
│   ├── Systems.fs              ← Router + System pipeline
│   ├── Game.fs                 ← init/initModel/input map
│   └── GameLoop.fs             ← Shared Elmish program wiring
├── Raylib/                     ← raylib backend
│   ├── Program.fs              ← Composition root (env, init/update/subscribe)
│   ├── View.fs                 ← 3D scene + EnemyAnimationService
│   ├── HudView.fs              ← 2D HUD
│   ├── AudioService.fs         ← Consume + Update(dt, snapshot)
│   └── Skybox.fs               ← Procedural starry skybox shader
├── MonoShared/                 ← MonoGame backend (shared by Desktop + WindowsDX)
│   ├── Program.fs              ← Composition root
│   ├── View.fs                 ← 3D scene + EnemyAnimationService
│   ├── HudView.fs              ← 2D HUD
│   ├── AudioService.fs         ← Consume + Update(dt, snapshot)
│   └── Skybox.fs               ← Starry skybox
├── MonoDesktop/                ← DesktopGL thin client
├── MonoWindowsDX/              ← WindowsDX thin client
└── Shared.Tests/               ← Expecto tests (router translation, event emission)
```