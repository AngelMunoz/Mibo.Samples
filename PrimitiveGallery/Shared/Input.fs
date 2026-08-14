namespace PrimitiveGallery

open Mibo.Adaptive
open Mibo.Elmish
open Mibo.Input

module Input =

  /// The single keyboard subscription: digits select a screen, Tab cycles.
  let subscriptions
    (cell: StateCell)
    (frameCtx: AdaptiveFrameContext)
    : amap<SubId, AdaptiveSub> =
    let input = frameCtx.Context |> GameContext.getService<IInput>

    AMap.ofList [
      SubId.ofString "keyboard",
      {
        Id = SubId.ofString "keyboard"
        Attach =
          fun _post ->
            input.KeyboardDelta.Subscribe(fun delta ->
              for code in delta.Pressed do
                match code with
                | KeyCode.D1 -> cell.Value.Screen |> CVal.set Screen.Shapes2D
                | KeyCode.D2 -> cell.Value.Screen |> CVal.set Screen.Shapes3D
                | KeyCode.D3 -> cell.Value.Screen |> CVal.set Screen.Split
                | KeyCode.Tab ->
                  let next =
                    match cell.Value.Screen |> AVal.getValue with
                    | Screen.Shapes2D -> Screen.Shapes3D
                    | Screen.Shapes3D -> Screen.Split
                    | Screen.Split -> Screen.Shapes2D

                  cell.Value.Screen |> CVal.set next
                | _ -> ())
      }
    ]
