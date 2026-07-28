using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Dx12InstancingRepro;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private Effect _effect;
    private VertexBuffer _vbo;
    private IndexBuffer _ibo;
    private int _vertexCount;
    private int _indexCount;
    private Texture2D _paletteTex;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _effect = Content.Load<Effect>("InstancingRepro");

        // Build a small quad: two triangles, six vertices.
        // Position0 carries the vertex location; TexCoord0 carries the
        // palette-texture row index for the VTF technique and a zero for the
        // grouped technique. BlendIndices0 carries the bone index for the
        // grouped technique.
        var verts = new VertexPositionNormalTextureBlendIndices[]
        {
            // pos                       normal                uv      bone idx
            new(new Vector3(-0.5f, -0.5f, 0), Vector3.Forward, Vector2.Zero, 0),
            new(new Vector3( 0.5f, -0.5f, 0), Vector3.Forward, Vector2.Zero, 0),
            new(new Vector3( 0.5f,  0.5f, 0), Vector3.Forward, Vector2.Zero, 0),
            new(new Vector3(-0.5f,  0.5f, 0), Vector3.Forward, Vector2.Zero, 0),
        };
        var indices = new short[] { 0, 1, 2, 0, 2, 3 };
        _vertexCount = verts.Length;
        _indexCount = indices.Length;

        _vbo = new VertexBuffer(GraphicsDevice, VertexPositionNormalTextureBlendIndices.VertexDeclaration, _vertexCount, BufferUsage.WriteOnly);
        _vbo.SetData(verts);
        _ibo = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, _indexCount, BufferUsage.WriteOnly);
        _ibo.SetData(indices);

        // Build a tiny palette texture: 1 bone (4 rows), 2 instances.
        // Instance 0 = red tint, instance 1 = green tint — so if VTF works we
        // see color; if it's broken (DX12) we see black/nothing.
        _paletteTex = new Texture2D(GraphicsDevice, 4, 2, false, SurfaceFormat.Vector4);
        var pal = new Vector4[]
        {
            new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 0), // bone 0, instance 0
            new(0, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 0), // bone 0, instance 1
        };
        _paletteTex.SetData(pal);

        ProbeParameters();
    }

    private void ProbeParameters()
    {
        // On the current MGCB toolchain all four params resolve to OK on DX12
        // — the "params dropped from mgfx" hypothesis was wrong for a freshly
        // compiled effect. (Mibo's committed .mgfx blobs may be stale; see
        // README.)
        var tex = _effect.Parameters["paletteTex"];
        var texSize = _effect.Parameters["paletteTexSize"];
        var group = _effect.Parameters["bonePaletteGroup"];
        var count = _effect.Parameters["groupBoneCount"];

        System.Console.WriteLine($"[InstancingRepro] backend={_graphics.GraphicsDevice.Adapter?.Description ?? "??"}");
        System.Console.WriteLine($"[InstancingRepro] paletteTex        = {(tex == null ? "NULL" : "OK")}");
        System.Console.WriteLine($"[InstancingRepro] paletteTexSize    = {(texSize == null ? "NULL" : "OK")}");
        System.Console.WriteLine($"[InstancingRepro] bonePaletteGroup  = {(group == null ? "NULL" : "OK")}");
        System.Console.WriteLine($"[InstancingRepro] groupBoneCount    = {(count == null ? "NULL" : "OK")}");
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();
        base.Update(gameTime);
    }

    private bool _vtfCrashed;

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;

        var view = Matrix.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.Up);
        var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        // --- Grouped uniform first (so the VTF crash below doesn't skip it) ---
        var bones = new Matrix[]
        {
            Matrix.CreateTranslation(0, 0, 0),
            Matrix.CreateTranslation(0, 0, 0),
            Matrix.CreateTranslation(0, 0, 0),
            Matrix.CreateTranslation(0, 0, 0),
        };
        _effect.Parameters["viewProj"].SetValue(viewProj);
        _effect.Parameters["bonePaletteGroup"].SetValue(bones);
        _effect.Parameters["groupBoneCount"].SetValue(MAX_BONES);
        _effect.CurrentTechnique = _effect.Techniques["GroupedUniform"];
        DrawQuad();

        // --- VTF (vertex texture fetch) — crashes on DX12 -------------------
        // On DX12 the MonoGame backend throws NotSupportedException when binding
        // a texture to the vertex stage ("Vertex textures are not supported on
        // this device"). It does NOT silently return zeros — it hard-crashes.
        if (!_vtfCrashed)
        {
            try
            {
                _effect.Parameters["viewProj"].SetValue(viewProj);
                _effect.Parameters["paletteTexSize"].SetValue(new Vector2(4, 2));
                _effect.Parameters["paletteTex"].SetValue(_paletteTex);
                _effect.CurrentTechnique = _effect.Techniques["TextureFetch"];
                DrawQuad();
            }
            catch (System.NotSupportedException ex)
            {
                _vtfCrashed = true;
                System.Console.WriteLine($"[InstancingRepro] VTF CRASHED: {ex.Message}");
            }
        }

        base.Draw(gameTime);
    }

    private const int MAX_BONES = 4;

    private void DrawQuad()
    {
        GraphicsDevice.SetVertexBuffer(_vbo);
        GraphicsDevice.Indices = _ibo;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _indexCount / 3);
        }
    }
}

// A minimal vertex with Position, Normal, TexCoord, and BLENDINDICES so it
// matches the shader's input struct for both techniques.
public struct VertexPositionNormalTextureBlendIndices : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TextureCoordinate;
    public int BlendIndices;

    public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(32, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0)
    );

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public VertexPositionNormalTextureBlendIndices(Vector3 pos, Vector3 normal, Vector2 uv, int boneIdx)
    {
        Position = pos;
        Normal = normal;
        TextureCoordinate = uv;
        BlendIndices = boneIdx;
    }
}