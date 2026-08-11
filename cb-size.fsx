// Reads the $Globals constant-buffer size (Int16, per the mgfx format —
// Effect.cs ReadEffect: name string, then Int16 sizeInBytes) out of compiled
// .mgfx files. Usage: dotnet fsi cb-size.fsx -- <file1> [file2 ...]
open System.IO

let needle = [|
  0x24uy
  0x47uy
  0x6Cuy
  0x6Fuy
  0x62uy
  0x61uy
  0x6Cuy
  0x73uy
|] // "$Globals"

let findNeedle(bytes: byte[]) =
  let mutable found = -1
  let mutable i = 0

  while found < 0 && i <= bytes.Length - needle.Length do
    let mutable matches = true
    let mutable j = 0

    while matches && j < needle.Length do
      if bytes[i + j] <> needle[j] then
        matches <- false

      j <- j + 1

    if matches then
      found <- i

    i <- i + 1

  found

for path in fsi.CommandLineArgs |> Array.skip 1 do
  let bytes = File.ReadAllBytes path
  let idx = findNeedle bytes

  if idx < 0 then
    printfn "%s -> $Globals not found" (Path.GetFileName path)
  else
    let off = idx + needle.Length
    let size = System.BitConverter.ToInt16(bytes, off)
    printfn "%s -> $Globals size = %d bytes" (Path.GetFileName path) size
