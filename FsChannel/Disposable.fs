namespace FsChannel

open System

/// Operations on IDisposables.
module Disposable =
    /// Creates an IDisposable that calls the
    /// given function when Dispose is called.
    let Create f = {
        new IDisposable with
            member this.Dispose () = f ()
    }

    /// Disposes an IDisposable.
    let Dispose (x : #IDisposable) = x.Dispose ()
