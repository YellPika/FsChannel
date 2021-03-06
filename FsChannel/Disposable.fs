﻿namespace FsChannel

open System

/// Operations on ITaskDisposables.
module TaskDisposable =
    /// Creates an ITaskDisposable that calls
    /// the given task when Dispose is invoked.
    let Create task = {
        new ITaskDisposable with
            member this.Dispose = task
    }

    /// Gets the dispose task of an ITaskDisposable.
    let Dispose (source : #ITaskDisposable) = source.Dispose

    /// Creates an ITaskDisposable from an IDisposable.
    let FromDisposable (source : #IDisposable) =
        Create (Task (source.Dispose >> Done))

/// Operations on IDisposables.
module Disposable =
    /// Creates an IDisposable that calls the
    /// given function when Dispose is called.
    let Create dispose = {
        new IDisposable with
            member this.Dispose () = dispose ()
    }

    /// Disposes an IDisposable.
    let Dispose (source : #IDisposable) = source.Dispose ()

    /// Creates an ITaskDisposable from an IDisposable.
    let ToTaskDisposable = TaskDisposable.FromDisposable
