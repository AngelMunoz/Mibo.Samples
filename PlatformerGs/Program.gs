// file: Program.gs
// Boot: load assets via raylib directly, build the adaptive program
// (init → frame force, update → stepWorld) and run it on the
// AdaptiveRaylibGame host.

package PlatformerGs

import System
import System.Numerics
import Microsoft.FSharp.Core
import Mibo
import Mibo.Core
import Mibo.Elmish
import Mibo.Elmish.Graphics2D
import Mibo.Elmish.Graphics2D.Lighting
import Mibo.Animation
import Mibo.Adaptive
import Mibo.Raylib
import Raylib_cs
import Unit = Microsoft.FSharp.Core.Unit


func assetPath(relative string) string {
    return AppContext.BaseDirectory + "assets/" + relative
}

func loadKeyframes(def TileAnimDefV) Animation {
    var frames = [def.FrameXs.Length]Rectangle
    for var i = 0; i < def.FrameXs.Length; i++ {
        frames[i] = Rectangle(def.FrameXs[i], def.FrameYs[i], 64f, 64f)
    }
    return Animation(frames, def.FrameDuration, def.Loop)
}

// Load the sprite assets with raylib directly. The framework's IAssets
// service is registered by the host but is only reachable through
// GameContext.getService<'T>, whose type parameter appears in return
// position only — not callable from G# 0.4.273 (overload applicability is
// decided from the arguments alone). A local loader keeps the port
// self-contained.
func loadAssets() SpriteAssets {
    var assets = SpriteAssets()

    var playerTex = Raylib.LoadTexture(assetPath("kenney_platformer/Spritesheets/spritesheet-characters-default.png"))
    var tileTex = Raylib.LoadTexture(assetPath("kenney_platformer/Spritesheets/spritesheet-tiles-default.png"))
    Raylib.SetTextureFilter(tileTex, Raylib_cs.TextureFilter.Point)

    assets.Font = Raylib.LoadFont(assetPath("Fonts/monogram.ttf"))
    assets.JumpSound = Raylib.LoadSound(assetPath("sfx_jump.ogg"))

    var particleImg = Raylib.GenImageColor(1, 1, Raylib_cs.Color.White)
    assets.ParticleTexture = Raylib.LoadTextureFromImage(particleImg)
    Raylib.UnloadImage(particleImg)

    // Player sheet — idle/walk/jump/fall/duck.
    var playerAnims = [5]ValueTuple[string, Animation]
    var idleFrames = [1]Rectangle
    idleFrames[0] = Rectangle(645f, 0f, 128f, 128f)
    var walkFrames = [2]Rectangle
    walkFrames[0] = Rectangle(0f, 129f, 128f, 128f)
    walkFrames[1] = Rectangle(129f, 129f, 128f, 128f)
    var jumpFrames = [1]Rectangle
    jumpFrames[0] = Rectangle(774f, 0f, 128f, 128f)
    var duckFrames = [1]Rectangle
    duckFrames[0] = Rectangle(258f, 0f, 128f, 128f)
    playerAnims[0] = ValueTuple.Create("idle", Animation(idleFrames, 1f, false))
    playerAnims[1] = ValueTuple.Create("walk", Animation(walkFrames, 0.1f, true))
    playerAnims[2] = ValueTuple.Create("jump", Animation(jumpFrames, 1f, false))
    playerAnims[3] = ValueTuple.Create("fall", Animation(jumpFrames, 1f, false))
    playerAnims[4] = ValueTuple.Create("duck", Animation(duckFrames, 1f, false))
    assets.PlayerSheet = SpriteSheetModule.fromFrames(playerTex, Vector2.Zero, playerAnims)!!

    // Torch sheet — two flame frames.
    var torchAnims = [1]ValueTuple[string, Animation]
    var torchFrames = [2]Rectangle
    torchFrames[0] = Rectangle(65f, 1105f, 64f, 64f)
    torchFrames[1] = Rectangle(130f, 1105f, 64f, 64f)
    torchAnims[0] = ValueTuple.Create("lit", Animation(torchFrames, 0.15f, true))
    assets.TorchSheet = SpriteSheetModule.fromFrames(tileTex, Vector2(32f, 32f), torchAnims)!!

    // Tile-effect sheet — every animated tile effect + the flag.
    var defs = tileAnimDefs()
    var effectAnims = [defs.Length + 1]ValueTuple[string, Animation]
    for var i = 0; i < defs.Length; i++ {
        effectAnims[i] = ValueTuple.Create(defs[i].Name, loadKeyframes(defs[i]))
    }
    var flagFrames = [2]Rectangle
    flagFrames[0] = Rectangle(1105f, 130f, 64f, 64f)
    flagFrames[1] = Rectangle(0f, 195f, 64f, 64f)
    effectAnims[defs.Length] = ValueTuple.Create("flag_red", Animation(flagFrames, 0.3f, true))
    assets.TileEffectSheet = SpriteSheetModule.fromFrames(tileTex, Vector2(32f, 32f), effectAnims)!!

    assets.TileTexture = tileTex
    return assets
}

// Seed the initial chunks around the spawn point.
func preloadChunks(w World) {
    // Parity with Platformer/Program.fs init: preload only x >= 0 around the
    // spawn chunk; the streaming step fills the rest.
    var pcx = int32(MathF.Floor(w.Player.Position.X / chunkWorldSize()))
    var pcy = int32(MathF.Floor(w.Player.Position.Y / chunkWorldSize()))
    var r = chunkLoadRadius()
    for var dx = -r; dx <= r; dx++ {
        if pcx + dx < 0 {
            continue
        }
        for var dy = -r; dy <= r; dy++ {
            var key = chunkDictKey(pcx + dx, pcy + dy)
            if !w.Chunks.ContainsKey(key) {
                w.Chunks[key] = generateChunk(pcx + dx, pcy + dy, w.Seed)
            }
        }
    }
}

// Generation parity dump: with GS_DUMP=1, print every tile of a grid of
// chunks (x in -2..4, y in -1..1) for seeds 12345 and 999 — same format as
// the F# WorldGen dump — so the two generators can be diffed directly.
func dumpGeneration() {
    for var seed = 0; seed <= 1; seed++ {
        var worldSeed = 12345
        if seed == 1 {
            worldSeed = 999
        }
        for var cx = -2; cx <= 4; cx++ {
            for var cy = -1; cy <= 1; cy++ {
                dumpChunkTiles(cx, cy, worldSeed)
            }
        }
    }
}

func dumpChunkTiles(cx int32, cy int32, worldSeed int32) {
    var chunk = generateChunk(cx, cy, worldSeed)
    for var y = 0; y < chunkCells(); y++ {
        for var x = 0; x < chunkCells(); x++ {
            var k = chunkTileKey(x, y)
            if chunk.Tiles.ContainsKey(k) {
                var tile = chunk.Tiles[k]
                Console.WriteLine(cx.ToString() + " " + cy.ToString() + " " + x.ToString() + " " + y.ToString() + " " + tile.Kind.ToString() + " " + tile.Biome.ToString())
            }
        }
    }
}

func Main() int32 {
    if Environment.GetEnvironmentVariable("GS_DUMP") != nil {
        dumpGeneration()
        return 0
    }

    var world = createWorld(Random.Shared.Next())

    // Frame force — packs the render frame the renderer draws. Runs after
    // the post drain each step; the renderer reads it between Steps.
    var frameBuilder = FuncConvert.FromFunc[Unit, RenderFrame]((u Unit) -> RenderFrame{World: world, Frame: 0})

    // Init — runs on the first Step (window already open, so asset loading
    // is safe here); the startup drain runs before the first frame is forced.
    var init = FuncConvert.FromFunc[AdaptiveFrameContext, AdaptiveInit[RenderFrame]]((ctx AdaptiveFrameContext) -> {
        world.Assets = loadAssets()
        world.Lighting = LightContext2D(nil, nil, nil, nil, nil, FSharpOption[float32](0.05f), FSharpOption[float32](2000f))
        world.Camera2D = Camera2D.create(
            camTarget(world.Player.Position.X, world.Player.Position.Y),
            1f,
            Vector2(float32(viewportWidth()), float32(viewportHeight())))
        world.PlayerSprite = AnimatedSpriteModule.create(world.Assets.PlayerSheet, "idle")
        world.TorchSprite = AnimatedSpriteModule.create(world.Assets.TorchSheet, "lit")
        world.CoinSprite = AnimatedSpriteModule.create(world.Assets.TileEffectSheet, "coin_gold")
        world.FlagSprite = AnimatedSpriteModule.create(world.Assets.TileEffectSheet, "flag_red")
        preloadChunks(world)
        return AdaptiveInit.ofFrameBuilder[RenderFrame](frameBuilder)
    })

    // Update — one simulation step per frame.
    var update = FuncConvert.FromFunc[AdaptiveContext, FSharpFunc[GameTime, Unit]]((ctx AdaptiveContext) ->
        FuncConvert.FromFunc[GameTime, Unit]((gt GameTime) -> {
            stepWorld(world, gt)
            return default(Unit)
        }))

    var program = AdaptiveProgram.mkProgram[RenderFrame](init, update)

    // Window configuration.
    program = AdaptiveProgram.withConfig[RenderFrame](FuncConvert.FromFunc[GameConfig, GameConfig]((cfg GameConfig) ->
        GameConfigModule.withTitle(
            "Mibo GSharp Platformer",
            GameConfigModule.withHeight(
                viewportHeight(),
                GameConfigModule.withWidth(viewportWidth(), cfg)))), program)

    // The renderer — Renderer2D.create with the curried view expressed as
    // nested FSharpFunc values (GameContext -> Frame -> RenderBuffer2D -> unit).
    var view = FuncConvert.FromFunc[GameContext, FSharpFunc[RenderFrame, FSharpFunc[RenderBuffer2D, Unit]]]((ctx GameContext) ->
        FuncConvert.FromFunc[RenderFrame, FSharpFunc[RenderBuffer2D, Unit]]((frame RenderFrame) ->
            FuncConvert.FromFunc[RenderBuffer2D, Unit]((buffer RenderBuffer2D) -> {
                renderView(ctx, frame, buffer)
                return default(Unit)
            })))
    program = AdaptiveProgram.withRenderer[RenderFrame](FuncConvert.FromFunc[Unit, IRenderer[RenderFrame]]((u Unit) ->
        Renderer2D.create[RenderFrame](view)), program)

    var game = AdaptiveRaylibGame[RenderFrame](program)
    game.Run()
    return 0
}
