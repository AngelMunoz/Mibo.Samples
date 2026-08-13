# Defli3D

A 3D port of the 2D tower-defense sample [Defli](../Defli/README.md): the same
adaptive simulation (Mibo.Adaptive roots/projections, forced `RenderFrame`)
drives five backends — raylib and four MonoGame clients (MonoDX12/MonoDX11/
MonoVK/MonoGL) — rendering baked kenney models from the shared
`assets/kenney_tower_defense_kit` (110 GLBs built through MGCB, all sharing
`Models/Textures/colormap.png`).

**Status: sim core ported, views/tests pending.** All project files, the
Content pipeline (`MonoDX12/Content/Content.mgcb`) and the full simulation
core (`Shared/`) are in place; the sim mirrors `Defli/` with the
`Defli` → `Defli3D` namespaces and a 3D orbit camera replacing the 2D one
(`Shared/State/Systems/Camera.fs` — see the file header for the conventions
the views must match). The backend views (Raylib/MonoDX12/MonoGL/MonoVK/
MonoDX11) and the test suite are placeholders to be filled in next. The
model dataset is compile-time generated in `Shared/State/Models.fs`
(contract pinned in `Shared/State/Domain.fs`).

```bash
dotnet run --project Defli3D/Raylib       # raylib backend (any platform)
dotnet run --project Defli3D/MonoDX12     # DirectX 12 (Windows)
dotnet run --project Defli3D/MonoDX11     # DirectX 11 (Windows)
dotnet run --project Defli3D/MonoGL       # OpenGL
dotnet run --project Defli3D/MonoVK       # Vulkan
dotnet test Defli3D/Shared.Tests          # test suite
```
