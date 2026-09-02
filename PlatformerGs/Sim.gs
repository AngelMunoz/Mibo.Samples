// file: Sim.gs
// The Update phase of the adaptive program: input → physics → chunk
// streaming → particles → day/night → animation → camera.
// Ported from Platformer/Shared/Physics.fs, Particles.fs, DayNight.fs and
// Platformer/Raylib/Systems.fs + Camera.fs. Input polls raylib directly
// (see Program.gs — the framework's IInput service is out of reach from G#,
// its accessor is generic-only-in-return).

package PlatformerGs

import System
import System.Numerics
import System.Collections.Generic
import Mibo
import Mibo.Elmish
import Mibo.Animation
import Raylib_cs

// ── Rect helpers ─────────────────────────────────────────────

func playerBounds(pos Vector2) RectV {
    return RectV{X: pos.X, Y: pos.Y, W: playerWidth(), H: playerHeight()}
}

func overlaps(a RectV, b RectV) bool {
    return a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y
}

// ── Collision resolution (Physics.resolvePlatformCollision) ──

// Solids: land from above, block from below and the sides.
// One-way: land from above only — suppressed while dropping through.
func resolveCollision(prevPos Vector2, pos Vector2, vel Vector2, solids List[RectV], oneWay List[RectV], dropDown bool) Vector4 {
    var p = pos
    var v = vel
    var grounded = false

    for var i = 0; i < solids.Count; i++ {
        var pb = solids[i]
        if overlaps(playerBounds(p), pb) {
            var prevFeetY = prevPos.Y + playerHeight()
            var currFeetY = p.Y + playerHeight()
            var platformTop = pb.Y
            var crossedSurface = prevFeetY <= platformTop + 5f && currFeetY >= platformTop
            if crossedSurface && v.Y >= 0f {
                p = Vector2(p.X, platformTop - playerHeight())
                v = Vector2(v.X, 0f)
                grounded = true
            } else if v.Y < 0f {
                p = Vector2(p.X, pb.Y + pb.H)
                v = Vector2(v.X, 0f)
            } else if v.X > 0f && prevPos.X + playerWidth() <= pb.X {
                p = Vector2(pb.X - playerWidth(), p.Y)
                v = Vector2(0f, v.Y)
            } else if v.X < 0f && prevPos.X >= pb.X + pb.W {
                p = Vector2(pb.X + pb.W, p.Y)
                v = Vector2(0f, v.Y)
            }
        }
    }

    if !dropDown {
        for var i = 0; i < oneWay.Count; i++ {
            var pb = oneWay[i]
            if overlaps(playerBounds(p), pb) {
                var prevFeetY = prevPos.Y + playerHeight()
                var currFeetY = p.Y + playerHeight()
                var platformTop = pb.Y
                var crossedSurface = prevFeetY <= platformTop + 5f && currFeetY >= platformTop
                if crossedSurface && v.Y >= 0f {
                    p = Vector2(p.X, platformTop - playerHeight())
                    v = Vector2(v.X, 0f)
                    grounded = true
                }
            }
        }
    }

    return Vector4(p.X, p.Y, v.X, v.Y)
}

// ── Physics step (PhysicsSystem.update) ──────────────────────

func physicsStep(w World, dt float32, moveDir float32, jumpHeld bool, jumpStarted bool, downHeld bool, respawnStarted bool) {
    var player = w.Player

    var vel = Vector2(moveDir * moveSpeed(), player.Velocity.Y + gravity() * dt)

    var canJump = player.IsGrounded
    var velocityY = vel.Y
    if jumpStarted && canJump {
        player.JumpTriggered = true
        velocityY = jumpSpeed()
    } else if !canJump && !jumpHeld && velocityY < 0f {
        velocityY = velocityY * jumpCutMultiplier()
    }
    vel = Vector2(vel.X, velocityY)

    var prevPos = player.Position
    var newPos = prevPos + vel * dt

    // Collect colliders from nearby chunks.
    var nearbySolids = List[RectV](256)
    var nearbyOneWay = List[RectV](64)
    var nearbySpikes = List[RectV](64)
    var nearbyCoins = List[RectV](64)
    var pcx = int32(MathF.Floor(newPos.X / chunkWorldSize()))
    var pcy = int32(MathF.Floor(newPos.Y / chunkWorldSize()))
    var r = chunkLoadRadius()

    for entry in w.Chunks {
        var key = entry.Key
        var cx = int32(key & 0xFFFFFFFF)
        var cy = int32(key >> 32)
        var adx = cx - pcx
        if adx < 0 { adx = -adx }
        var ady = cy - pcy
        if ady < 0 { ady = -ady }
        if adx <= r && ady <= r {
            var chunk = entry.Value
            for var i = 0; i < chunk.Platforms.Length; i++ { nearbySolids.Add(chunk.Platforms[i]) }
            for var i = 0; i < chunk.OneWayPlatforms.Length; i++ { nearbyOneWay.Add(chunk.OneWayPlatforms[i]) }
            for var i = 0; i < chunk.Spikes.Length; i++ { nearbySpikes.Add(chunk.Spikes[i]) }
            for var i = 0; i < chunk.Coins.Length; i++ { nearbyCoins.Add(chunk.Coins[i]) }
        }
    }

    player.IsDucking = downHeld
    var res = resolveCollision(prevPos, newPos, vel, nearbySolids, nearbyOneWay, downHeld)
    var finalPos = Vector2(res.X, res.Y)
    var finalVel = Vector2(res.Z, res.W)
    var isGrounded = false // resolved below from the landing check result

    // The resolve step lands the player exactly on platform tops; detect
    // grounded by testing a 1px probe below the feet against solids/oneways.
    var feet = RectV{X: finalPos.X + 2f, Y: finalPos.Y + playerHeight(), W: playerWidth() - 4f, H: 1f}
    for var i = 0; i < nearbySolids.Count; i++ {
        if overlaps(feet, nearbySolids[i]) {
            isGrounded = true
        }
    }
    if !downHeld {
        for var i = 0; i < nearbyOneWay.Count; i++ {
            if overlaps(feet, nearbyOneWay[i]) {
                isGrounded = true
            }
        }
    }

    // Spike collision → respawn.
    var playerRect = playerBounds(finalPos)
    for var i = 0; i < nearbySpikes.Count; i++ {
        if overlaps(playerRect, nearbySpikes[i]) {
            finalPos = Vector2(spawnX(), groundSurface() - playerHeight())
            finalVel = Vector2.Zero
            isGrounded = true
        }
    }

    // Coin collection — bump score and clear the tile.
    var collected = List[RectV](16)
    for var i = 0; i < nearbyCoins.Count; i++ {
        if overlaps(playerRect, nearbyCoins[i]) {
            player.Score = player.Score + 1
            collected.Add(nearbyCoins[i])
        }
    }
    for var i = 0; i < collected.Count; i++ {
        var coinRect = collected[i]
        var coinCx = int32(MathF.Floor(coinRect.X / chunkWorldSize()))
        var coinCy = int32(MathF.Floor(coinRect.Y / chunkWorldSize()))
        var chunkKey = chunkDictKey(coinCx, coinCy)
        if w.Chunks.ContainsKey(chunkKey) {
            var chunk = w.Chunks[chunkKey]
            var cellX = int32((coinRect.X - chunk.OriginX) / tileSize())
            var cellY = int32((coinRect.Y - chunk.OriginY) / tileSize())
            if cellX >= 0 && cellX < chunkCells() && cellY >= 0 && cellY < chunkCells() {
                chunk.Tiles.Remove(chunkTileKey(cellX, cellY))
            }
        }
    }

    // Fell out of the world → respawn.
    if finalPos.Y > groundLevel() + 500f {
        finalPos = Vector2(spawnX(), groundSurface() - playerHeight())
        finalVel = Vector2.Zero
        isGrounded = true
    }

    if respawnStarted {
        finalPos = Vector2(spawnX(), groundSurface() - playerHeight())
        finalVel = Vector2.Zero
        isGrounded = true
    }

    player.Position = finalPos
    player.Velocity = finalVel
    player.IsGrounded = isGrounded

    if moveDir < 0f {
        player.Facing = -1f
    } else if moveDir > 0f {
        player.Facing = 1f
    }
}

// ── Particles (Particles.fs) ─────────────────────────────────

func confettiColor(n int32) Mibo.Color {
    switch n {
    case 0 { return Mibo.Color(byte(255), byte(50), byte(50), byte(255)) }
    case 1 { return Mibo.Color(byte(50), byte(255), byte(50), byte(255)) }
    case 2 { return Mibo.Color(byte(50), byte(50), byte(255), byte(255)) }
    case 3 { return Mibo.Color(byte(255), byte(255), byte(50), byte(255)) }
    case 4 { return Mibo.Color(byte(255), byte(50), byte(255), byte(255)) }
    case 5 { return Mibo.Color(byte(50), byte(255), byte(255), byte(255)) }
    case 6 { return Mibo.Color(byte(255), byte(150), byte(50), byte(255)) }
    default { return Mibo.Color(byte(255), byte(50), byte(150), byte(255)) }
    }
}

func spawnConfetti(w World, pos Vector2) {
    var rng = Random.Shared
    var pc = w.ParticleCount
    for var i = 0; i < 20; i++ {
        if pc < w.Particles.Length {
            var ox = float32(rng.NextDouble() * 20.0 - 10.0)
            w.Particles[pc] = ParticleV{
                Position: pos + Vector2(playerWidth() / 2f + ox, playerHeight() * 0.3f),
                Size: Vector2(4f, 4f),
                Rotation: float32(rng.NextDouble() * Math.PI * 2.0),
                Color: confettiColor(rng.Next(8))
            }
            w.ParticleVelocities[pc] = Vector2(
                float32(rng.NextDouble() * 300.0 - 150.0),
                float32(rng.NextDouble() * -250.0 - 50.0))
            pc = pc + 1
        }
    }
    w.ParticleCount = pc
}

func tickParticles(w World, dt float32) {
    var count = w.ParticleCount
    for var i = 0; i < count; i++ {
        var vel = w.ParticleVelocities[i]
        var newVel = Vector2(vel.X, vel.Y + gravity() * dt * 0.05f)
        w.ParticleVelocities[i] = newVel
        var p = w.Particles[i]
        w.Particles[i] = ParticleV{
            Position: p.Position + newVel * dt,
            Size: p.Size,
            Rotation: p.Rotation,
            Color: p.Color
        }
    }

    // Fade + compact.
    var fadeAmount = 60f * dt
    var writeIdx int32 = 0
    for var readIdx = 0; readIdx < count; readIdx++ {
        var p = w.Particles[readIdx]
        var newAlpha = MathF.Max(0f, float32(p.Color.A) - fadeAmount)
        if newAlpha > 0f {
            w.Particles[writeIdx] = ParticleV{
                Position: p.Position,
                Size: p.Size,
                Rotation: p.Rotation,
                Color: Mibo.Color(p.Color.R, p.Color.G, p.Color.B, byte(newAlpha))
            }
            writeIdx = writeIdx + 1
        }
    }
    w.ParticleCount = writeIdx
}

// ── Day/Night (DayNight.fs) ──────────────────────────────────

func lerpColor(a Mibo.Color, b Mibo.Color, tt float32) Mibo.Color {
    var t = Math.Clamp(tt, 0f, 1f)
    return Mibo.Color(
        byte(float32(a.R) + t * (float32(b.R) - float32(a.R))),
        byte(float32(a.G) + t * (float32(b.G) - float32(a.G))),
        byte(float32(a.B) + t * (float32(b.B) - float32(a.B))),
        byte(255))
}

func rgb255(r int32, g int32, b int32) Mibo.Color {
    return Mibo.Color(byte(r), byte(g), byte(b), byte(255))
}

data struct SkyPalette {
    var Top Mibo.Color
    var Bot Mibo.Color
}

func getSkyColors(time float32) SkyPalette {
    var midnightTop = rgb255(10, 10, 30)
    var midnightBot = rgb255(20, 20, 40)
    var dayTop = rgb255(100, 149, 237)
    var dayBot = rgb255(173, 216, 230)
    var sunsetTop = rgb255(50, 50, 100)
    var sunsetBot = rgb255(255, 80, 50)

    if time < 6f {
        return SkyPalette{Top: midnightTop, Bot: midnightBot}
    } else if time < 8f {
        var tt = (time - 6f) / 2f
        return SkyPalette{Top: lerpColor(midnightTop, dayTop, tt), Bot: lerpColor(midnightBot, dayBot, tt)}
    } else if time < 16f {
        return SkyPalette{Top: dayTop, Bot: dayBot}
    } else if time < 18f {
        var tt = (time - 16f) / 2f
        return SkyPalette{Top: lerpColor(dayTop, sunsetTop, tt), Bot: lerpColor(dayBot, sunsetBot, tt)}
    } else if time < 20f {
        var tt = (time - 18f) / 2f
        return SkyPalette{Top: lerpColor(sunsetTop, midnightTop, tt), Bot: lerpColor(sunsetBot, midnightBot, tt)}
    }
    return SkyPalette{Top: midnightTop, Bot: midnightBot}
}

func getAmbientColor(time float32) Mibo.Color {
    var sky = getSkyColors(time)
    var avg = float32(int32(sky.Top.R) + int32(sky.Top.G) + int32(sky.Top.B) + int32(sky.Bot.R) + int32(sky.Bot.G) + int32(sky.Bot.B)) / 6f
    var intensity = MathF.Max(avg / 255f, 0.12f)
    return Mibo.Color(byte(intensity * 255f), byte(intensity * 245f), byte(intensity * 230f), byte(255))
}

func getSunIntensity(time float32) float32 {
    if time < 5f || time > 19f {
        return 0f
    } else if time < 7f {
        return (time - 5f) / 2f
    } else if time < 17f {
        return 1f
    }
    return (19f - time) / 2f
}

func getMoonIntensity(time float32) float32 {
    if time >= 5f && time <= 19f {
        return 0f
    }
    return 1f
}

// Sun/moon orbital positions packed as (sunX, sunY, moonX, moonY).
func orbitalPositions(centerX float32, timeOfDay float32) Vector4 {
    var centerY = groundLevel() - 200f
    var sunAngle = (timeOfDay - 18f) / 24f * MathF.PI * 2f
    var moonAngle = sunAngle + MathF.PI
    return Vector4(
        centerX + 500f * MathF.Cos(sunAngle),
        centerY + 200f * MathF.Sin(sunAngle),
        centerX + 500f * MathF.Cos(moonAngle),
        centerY + 200f * MathF.Sin(moonAngle))
}

// ── Camera (Raylib/Camera.fs) ────────────────────────────────

func deadzoneHalfWidth() float32 { return 150f }
func deadzoneHalfHeight() float32 { return 80f }
func followRate() float32 { return 6f }

func camTarget(px float32, py float32) Vector2 {
    var clampedY = MathF.Max(-500f, MathF.Min(py, 2000f))
    return Vector2(px, clampedY)
}

func desiredTarget(current Vector2, framed Vector2) Vector2 {
    var dx = framed.X - current.X
    var dy = framed.Y - current.Y
    var tx = current.X
    if dx > deadzoneHalfWidth() {
        tx = current.X + (dx - deadzoneHalfWidth())
    } else if dx < -deadzoneHalfWidth() {
        tx = current.X + (dx + deadzoneHalfWidth())
    }
    var ty = current.Y
    if dy > deadzoneHalfHeight() {
        ty = current.Y + (dy - deadzoneHalfHeight())
    } else if dy < -deadzoneHalfHeight() {
        ty = current.Y + (dy + deadzoneHalfHeight())
    }
    return Vector2(tx, ty)
}

func updateCamera(w World, dt float32) {
    var c = w.Camera2D
    var goal = desiredTarget(c.Target, camTarget(w.Player.Position.X, w.Player.Position.Y))
    var factor = 1f - MathF.Exp(-followRate() * dt)
    Camera2D.smoothFollow(in c, goal, factor)
    w.Camera2D = c
}

// ── Animation (Shared/Animation.fs + Systems.fs sprite block) ──

func animStateFor(vel Vector2, isGrounded bool, isDucking bool) AnimState {
    if isGrounded && isDucking {
        return AnimState.Duck
    }
    if !isGrounded {
        if vel.Y > 0f {
            return AnimState.Fall
        }
        return AnimState.Jump
    }
    var ax = vel.X
    if ax < 0f { ax = -ax }
    if ax > 1f {
        return AnimState.Walk
    }
    return AnimState.Idle
}

func animStateName(state AnimState) string {
    switch state {
    case AnimState.Walk { return "walk" }
    case AnimState.Jump { return "jump" }
    case AnimState.Fall { return "fall" }
    case AnimState.Duck { return "duck" }
    default { return "idle" }
    }
}

// ── The adaptive Update phase ────────────────────────────────

// raylib returns CBool — convert via its implicit bool operator.
func keyDown(k KeyboardKey) bool {
    return bool(Raylib.IsKeyDown(k))
}

// One simulation step: raylib input → physics → chunk streaming →
// particles → day/night → animation → camera.
func stepWorld(w World, gt GameTime) {
    var dt = float32(gt.ElapsedGameTime.TotalSeconds)

    // Input — polled straight from raylib.
    var leftHeld = keyDown(KeyboardKey.A) || keyDown(KeyboardKey.Left)
    var rightHeld = keyDown(KeyboardKey.D) || keyDown(KeyboardKey.Right)
    var jumpHeld = keyDown(KeyboardKey.Space)
    var downHeld = keyDown(KeyboardKey.S) || keyDown(KeyboardKey.Down)
    var respawnHeld = keyDown(KeyboardKey.R)
    var jumpStarted = jumpHeld && !w.PrevJumpHeld
    var respawnStarted = respawnHeld && !w.PrevRespawnHeld
    w.PrevJumpHeld = jumpHeld
    w.PrevRespawnHeld = respawnHeld

    var moveDir = 0f
    if leftHeld {
        moveDir = -1f
    } else if rightHeld {
        moveDir = 1f
    }

    // Physics.
    physicsStep(w, dt, moveDir, jumpHeld, jumpStarted, downHeld, respawnStarted)

    // Chunk streaming (2 chunks per step budget — see streamChunks).
    streamChunks(w)

    // Particles + jump confetti/sound.
    tickParticles(w, dt)
    if w.Player.JumpTriggered {
        spawnConfetti(w, w.Player.Position)
        Raylib.PlaySound(w.Assets.JumpSound)
        w.Player.JumpTriggered = false
    }

    // Day/Night.
    w.TimeOfDay = (w.TimeOfDay + dt * (24f / 60f)) % 24f
    w.DayNight = DayNightState{TimeOfDay: w.TimeOfDay, TotalTime: w.DayNight.TotalTime + dt}

    // Animation — derive state from physics, keep the sprite playing it.
    var state = animStateFor(w.Player.Velocity, w.Player.IsGrounded, w.Player.IsDucking)
    var sprite = AnimatedSpriteModule.playIfNot(animStateName(state), w.PlayerSprite)
    sprite = AnimatedSpriteModule.update(dt, sprite)
    if w.Player.Facing < 0f {
        w.PlayerSprite = AnimatedSpriteModule.facingLeft(sprite)
    } else {
        w.PlayerSprite = AnimatedSpriteModule.facingRight(sprite)
    }
    w.TorchSprite = AnimatedSpriteModule.update(dt, w.TorchSprite)
    w.CoinSprite = AnimatedSpriteModule.update(dt, w.CoinSprite)
    w.FlagSprite = AnimatedSpriteModule.update(dt, w.FlagSprite)

    // Camera follows the player.
    updateCamera(w, dt)
}
