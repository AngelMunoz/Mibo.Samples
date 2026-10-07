module LiveMap.FileWatch

open System
open System.IO
open FSharp.Control.Reactive

/// The document's live watch: one stream that reads the file once at
/// subscribe, then re-reads it after every save.
///
/// Every failure is an event rather than an exception: an unwatchable
/// path, a read that loses a race with the editor, and an overflowed
/// watcher buffer all arrive as `Failed` with the reason in the message.
/// The subscription owns the watcher's lifetime.
type WatchEvent =
  /// The document's text, as of this save.
  | Loaded of source: string
  | Failed of reason: string

/// Reads the watched file: its text, or why the read failed.
let read(path: string) : WatchEvent =
  try
    Loaded(File.ReadAllText path)
  with error ->
    Failed error.Message

/// A watcher over the document's own file, so the stream sees one file
/// and no other document in the directory.
let private startWatcher(path: string) : FileSystemWatcher =
  let directory = Path.GetDirectoryName path
  let name = Path.GetFileName path
  let watcher = new FileSystemWatcher(directory, name)

  watcher.NotifyFilter <-
    NotifyFilters.LastWrite ||| NotifyFilters.Size ||| NotifyFilters.FileName

  // an editor fires a burst of events per save; a bigger buffer means the
  // OS drops fewer of them
  watcher.InternalBufferSize <- 64 * 1024
  watcher.EnableRaisingEvents <- true
  watcher

/// Watches one document.
///
/// The stream is debounced by a quarter of a second: an editor writes a
/// file in several goes and often holds it open mid-write, so the single
/// read lands after the write settles and the lock is gone. That debounce
/// is the whole handling for a partially written file — there is no retry
/// loop and no existence check, because a document that is missing or
/// locked is one `Failed` event and the message says so.
let watch(path: string) : IObservable<WatchEvent> =
  { new IObservable<WatchEvent> with
      member _.Subscribe(observer: IObserver<WatchEvent>) : IDisposable =
        try
          let watcher = startWatcher path

          // the OS reports a dropped burst here; the watcher keeps running,
          // so the message asks for another save rather than restarting
          watcher.Error.Add(fun args ->
            observer.OnNext(
              Failed
                $"changes were missed ({args.GetException().Message}) - save again"
            ))

          let changes =
            Observable.merge
              (Observable.merge watcher.Changed watcher.Created)
              (watcher.Renamed
               |> Observable.map(fun args -> args :> FileSystemEventArgs))
            |> Observable.filter(fun (args: FileSystemEventArgs) ->
              String.Equals(
                args.FullPath,
                path,
                StringComparison.OrdinalIgnoreCase
              ))
            |> Observable.throttle(TimeSpan.FromMilliseconds 250.0)
            |> Observable.subscribe(fun _ -> observer.OnNext(read path))

          // the current content, once, so opening the editor shows the map
          observer.OnNext(read path)

          { new IDisposable with
              member _.Dispose() =
                changes.Dispose()
                watcher.EnableRaisingEvents <- false
                watcher.Dispose()
          }
        with error ->
          observer.OnNext(Failed $"watch failed: {error.Message}")

          { new IDisposable with
              member _.Dispose() = ()
          }
  }
