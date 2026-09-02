// file: View.gs
// The view function — packs nothing, draws everything: sky gradient, camera,
// dynamic lighting (sun/moon/torches + occluders), tiles, player, particles
// and HUD. Ported from Platformer/Raylib/View.fs onto the emitted RenderBuffer2D
// Add* surface with neutral Mibo.Color values.

package PlatformerGs

import System
import System.Numerics
import System.Collections.Generic
import Mibo
import Mibo.Elmish
import Mibo.Elmish.Graphics2D
import Mibo.Elmish.Graphics2D.Lighting
import Mibo.Animation
import Raylib_cs

// Insertion sort by squared distance to the player — nearest first, so the
// caps (maxOccluders / maxTorchLights) keep the closest instances.
func sortByDistSq(items []OccluderV, count int32, px float32, py float32) {
    for var i = 1; i < count; i++ {
        var item = items[i]
        var dx = (item.P1.X + item.P2.X) * 0.5f - px
        var dy = (item.P1.Y + item.P2.Y) * 0.5f - py
        var dk = dx * dx + dy * dy
        var j = i - 1
        while j >= 0 {
            var o = items[j]
            var ox = (o.P1.X + o.P2.X) * 0.5f - px
            var oy = (o.P1.Y + o.P2.Y) * 0.5f - py
            if ox * ox + oy * oy > dk {
                items[j + 1] = o
                j = j - 1
            } else {
                break
            }
        }
        items[j + 1] = item
    }
}

func sortByDistSqT(items []TorchLight, count int32, px float32, py float32) {
    for var i = 1; i < count; i++ {
        var item = items[i]
        var dx = item.Position.X - px
        var dy = item.Position.Y - py
        var dk = dx * dx + dy * dy
        var j = i - 1
        while j >= 0 {
            var o = items[j]
            var ox = o.Position.X - px
            var oy = o.Position.Y - py
            if ox * ox + oy * oy > dk {
                items[j + 1] = o
                j = j - 1
            } else {
                break
            }
        }
        items[j + 1] = item
    }
}

func chunkVisible(chunk ChunkV, viewBounds Rectangle) bool {
    var cx = chunk.OriginX
    var cy = chunk.OriginY
    var s = chunkWorldSize()
    return cx < viewBounds.X + viewBounds.Width && cx + s > viewBounds.X
        && cy < viewBounds.Y + viewBounds.Height && cy + s > viewBounds.Y
}

/// Populate the render buffer for one frame.
func renderView(ctx GameContext, frame RenderFrame, buffer RenderBuffer2D) {
    var w = frame.World
    w.Lighting.Reset()

    var playerCenterX = w.Player.Position.X + playerWidth() / 2f
    var camera = w.Camera2D

    var sky = getSkyColors(w.TimeOfDay)
    var ambient = getAmbientColor(w.TimeOfDay)
    var sunIntensity = getSunIntensity(w.TimeOfDay)
    var moonIntensity = getMoonIntensity(w.TimeOfDay)
    var orbs = orbitalPositions(playerCenterX, w.TimeOfDay)
    var sunPos = Vector2(orbs.X, orbs.Y)
    var moonPos = Vector2(orbs.Z, orbs.W)

    var viewBounds = Camera2D.viewportBounds(in camera, float32(ctx.WindowWidth), float32(ctx.WindowHeight))

    // Sky gradient (screen space, behind everything).
    buffer.AddRectGradientV(0, 0, ctx.WindowWidth, ctx.WindowHeight, sky.Top, sky.Bot, -1000)

    // World camera.
    buffer.AddBeginCamera(camera, 0)

    // Ambient light from the sky palette.
    buffer.AddSetAmbient(w.Lighting, ambient, 5)

    // Sun / moon directional lights (both cast shadows).
    var focus = Vector2(playerCenterX, groundLevel() - 200f)
    if sunIntensity > 0f {
        var sunDir = Vector2.Normalize(focus - sunPos)
        buffer.AddDirectionalLight(w.Lighting, sunDir,
            Mibo.Color(byte(255), byte(245), byte(220), byte(255)),
            sunIntensity * 1.5f, true, 6)
    }
    if moonIntensity > 0f {
        var moonDir = Vector2.Normalize(focus - moonPos)
        buffer.AddDirectionalLight(w.Lighting, moonDir,
            Mibo.Color(byte(180), byte(200), byte(255), byte(255)),
            moonIntensity * 0.8f, true, 6)
    }

    // Collect nearby occluders + torch lights.
    var pcx = int32(MathF.Floor(w.Player.Position.X / chunkWorldSize()))
    var pcy = int32(MathF.Floor(w.Player.Position.Y / chunkWorldSize()))
    var playerPos = w.Player.Position
    var vw = float32(ctx.WindowWidth)
    var vh = float32(ctx.WindowHeight)
    var maxOccluderDistSq = vw * 1.5f * vw * 1.5f + vh * 1.5f * vh * 1.5f

    var occluders = [maxOccluders()]OccluderV
    var occluderCount int32 = 0
    var torches = [maxTorchLights()]TorchLight
    var torchCount int32 = 0
    var allTorches = List[TorchLight](64)

    for entry in w.Chunks {
        var key = entry.Key
        var cx = int32(key & 0xFFFFFFFF)
        var cy = int32(key >> 32)
        var adx = cx - pcx
        if adx < 0 { adx = -adx }
        var ady = cy - pcy
        if ady < 0 { ady = -ady }
        if adx <= chunkLoadRadius() && ady <= chunkLoadRadius() {
            var chunk = entry.Value
            for var i = 0; i < chunk.Occluders.Length; i++ {
                var o = chunk.Occluders[i]
                var mx = (o.P1.X + o.P2.X) * 0.5f - playerPos.X
                var my = (o.P1.Y + o.P2.Y) * 0.5f - playerPos.Y
                if mx * mx + my * my <= maxOccluderDistSq && occluderCount < maxOccluders() {
                    occluders[occluderCount] = o
                    occluderCount = occluderCount + 1
                }
            }
            for var i = 0; i < chunk.Torches.Length; i++ {
                if allTorches.Count < 256 {
                    allTorches.Add(chunk.Torches[i])
                }
            }
        }
    }

    sortByDistSq(occluders, occluderCount, playerPos.X, playerPos.Y)

    // Nearest torches first (into the capped scratch array).
    var allArr = allTorches.ToArray()
    sortByDistSqT(allArr, allArr.Length, playerPos.X, playerPos.Y)
    for var i = 0; i < allArr.Length; i++ {
        if torchCount < maxTorchLights() {
            torches[torchCount] = allArr[i]
            torchCount = torchCount + 1
        }
    }

    // Torches: point light + lit flame sprite.
    var torchSrc = AnimatedSpriteModule.currentSource(w.TorchSprite)
    for var i = 0; i < torchCount; i++ {
        var torch = torches[i]
        buffer.AddPointLight(w.Lighting, PointLight2D(
                torch.Position,
                RaylibColor.toRaylibColor(torch.Color),
                1.2f, torch.Radius, 1.5f, false), 7)

        var torchDest = Rectangle(torch.Position.X - 16f, torch.Position.Y - 32f, 32f, 32f)
        var ss = SpriteStateModule.create(w.Assets.TorchSheet.Texture, torchDest, torchSrc)
        ss = SpriteStateModule.withLayer(7, ss)
        buffer.AddLitSprite(w.Lighting, ss)
    }

    // Occluders (shadow casters).
    for var i = 0; i < occluderCount; i++ {
        var o = occluders[i]
        buffer.AddOccluder(w.Lighting, Occluder2D(o.P1, o.P2), 8)
    }

    // Tiles + animated collectibles.
    for entry in w.Chunks {
        var key = entry.Key
        var cx = int32(key & 0xFFFFFFFF)
        var cy = int32(key >> 32)
        var adx = cx - pcx
        if adx < 0 { adx = -adx }
        var ady = cy - pcy
        if ady < 0 { ady = -ady }
        if adx <= chunkLoadRadius() && ady <= chunkLoadRadius() && chunkVisible(entry.Value, viewBounds) {
            var chunk = entry.Value
            var tiles = chunk.Tiles
            for var y = 0; y < chunkCells(); y++ {
                for var x = 0; x < chunkCells(); x++ {
                    var k = chunkTileKey(x, y)
                    if tiles.ContainsKey(k) {
                        var tile = tiles[k]
                        if tile.Kind != TileKind.Empty && tile.Kind != TileKind.Coin && tile.Kind != TileKind.Flag {
                            var info = tileInfo(tile.Kind, tile.Biome)
                            var wx = chunk.OriginX + float32(x) * tileSize()
                            var wy = chunk.OriginY + float32(y) * tileSize()
                            var dest = Rectangle(wx, wy, tileSize(), tileSize())
                            var src = Rectangle(info.SpriteX, info.SpriteY, 64f, 64f)
                            var ss = SpriteStateModule.create(w.Assets.TileTexture, dest, src)
                            ss = SpriteStateModule.withLayer(10, ss)
                            buffer.AddLitSprite(w.Lighting, ss)
                        }
                    }
                }
            }

            for var i = 0; i < chunk.Coins.Length; i++ {
                var coin = chunk.Coins[i]
                buffer.AddLitAnimatedSprite(w.Lighting, Rectangle(coin.X, coin.Y, coin.W, coin.H), w.CoinSprite, 10)
            }
            for var i = 0; i < chunk.Flags.Length; i++ {
                var flag = chunk.Flags[i]
                buffer.AddLitAnimatedSprite(w.Lighting, Rectangle(flag.X, flag.Y, flag.W, flag.H), w.FlagSprite, 10)
            }
        }
    }

    // Player.
    var playerDrawY = w.Player.Position.Y + playerHeight() - 64f
    var playerDest = Rectangle(w.Player.Position.X, playerDrawY, 64f, 64f)
    buffer.AddLitAnimatedSprite(w.Lighting, playerDest, w.PlayerSprite, 20)

    // Particles.
    var particleBuffer = [512]Particle2D
    for var i = 0; i < w.ParticleCount; i++ {
        var p = w.Particles[i]
        particleBuffer[i] = Particle2D(
            p.Position, p.Size, p.Rotation,
            Rectangle(0f, 0f, 1f, 1f),
            RaylibColor.toRaylibColor(p.Color))
    }
    buffer.AddParticles(w.Assets.ParticleTexture, particleBuffer, w.ParticleCount, 3)

    // End lighting + camera.
    buffer.AddEndLighting(w.Lighting, 999)
    buffer.AddEndCamera(1000)

    // HUD.
    var tod = (MathF.Round(w.TimeOfDay * 10f) / 10f).ToString()
    buffer.AddText(w.Assets.Font,
        "Day/Night Cycle | Time: ${tod}h | Chunks: ${w.Chunks.Count.ToString()} | Score: ${w.Player.Score.ToString()} | WASD/Arrows: Move | Space: Jump | S/Down: Drop | R: Respawn",
        Vector2(10f, 10f), 20f, 1f, Mibo.ColorModule.White, 1001)
    buffer.AddText(w.Assets.Font,
        "FPS: ${Raylib.GetFPS().ToString()}",
        Vector2(10f, 32f), 20f, 1f, Mibo.ColorModule.White, 1001)
}
