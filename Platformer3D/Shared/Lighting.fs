module Platformer3D.Lighting

open System.Numerics
open Mibo
open Platformer3D.Constants
open Platformer3D.DayNight

// -------------------------------------------------------------
// Lighting Sub-system (backend-agnostic)
// -------------------------------------------------------------

module LightingSystem =
  type LightingModel() =
    member val SkyColor = Color.Black with get, set
    member val AmbientColor = Color.Black with get, set
    member val AmbientIntensity = 0.0f with get, set
    member val LightDirection = Vector3.Zero with get, set
    member val LightColor = Color.White with get, set
    member val LightIntensity = 0.0f with get, set

  let init() = LightingModel()

  let update (timeOfDay: float32) (model: LightingModel) =
    model.SkyColor <- getSkyColor timeOfDay
    model.AmbientColor <- getAmbientColor timeOfDay
    model.AmbientIntensity <- getAmbientIntensity timeOfDay
    model.LightDirection <- getPrimaryLightDirection timeOfDay arcRadius
    model.LightColor <- getPrimaryLightColor timeOfDay
    model.LightIntensity <- getPrimaryLightIntensity timeOfDay
    model
