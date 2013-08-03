namespace FsChannel

open System

/// An abstract representation of a synchronous operation.
[<NoComparison; NoEquality>]
type Signal<'a> = {
    Connect : ('a -> unit) -> Fiber<IDisposable>
}

/// Operations on signals.
module Signal =
    /// Creates a signal using the specified function as the connection function.
    let Create connect = { Connect = connect }
    
    /// Registers a function to be invoked when the signal is activated.
    /// A disposable object is returned, which may be used to cancel the registration.
    let Connect f (x : Signal<'a>) = x.Connect f

    /// Creates an event which computes the given function upon
    /// synchronization, and then behaves like the returned event.
    let Delay f = Create (fun g -> Connect g (f ()))

    /// Creates a signal that always passes the specified value to subscribers.
    let Always x = Create (fun f -> fiber {
        f x
        return Disposable.Create id
    })

    /// A signal that never notifies its subscribers.
    let Never<'a> : Signal<'a> = Create (fun _ -> Fiber.Return (Disposable.Create id))

    /// Wraps a post synchronization operation around the specified signal.
    let Map f x = Create (fun g -> Connect (f >> g) x)

    /// Creates a signal that represents the non-deterministic choice of two signals.
    let Choose signal1 signal2 = Create (fun f -> fiber {
        let connection1 = ref (Disposable.Create id)
        let connection2 = ref (Disposable.Create id)

        let isSignalled = ref false
        let! connection1' = signal1.Connect <| fun x ->
            isSignalled := true
            (!connection2).Dispose ()
            f x
        connection1 := connection1'

        if not !isSignalled then
            let! connection2' = signal2.Connect <| fun x ->
                (!connection1).Dispose ()
                f x
            connection2 := connection2'

        return Disposable.Create (fun () ->
            (!connection1).Dispose ()
            (!connection2).Dispose ())
    })

    /// Creates a signal that represents the non-deterministic
    /// choice of the specified list of signals.
    let Select signals = Seq.fold Choose Never signals

    /// Delays the communication of a signal by the specified time span.
    let Wait span x = Create (fun f -> fiber {
        let cancel = ref false
        let connection = ref (Disposable.Create (fun () -> cancel := true))

        yield! fiber {
            do! Fiber.Wait span
            if not !cancel then
                let! connection' = Connect f x
                connection := connection'
        }

        return Disposable.Create (fun () -> (!connection).Dispose ())        
    })

    /// Wraps an signal such that it will return None if the specified
    /// time span elapses before the signal is communicated.
    let TimeOut span x = Choose (Map Some x) (Wait span (Always None))

    /// Synchronizes the calling fiber with the specified signal.
    let Sync signal = fiber {
        let value = ref None
        let! _ = signal.Connect (fun x -> value := Some x)

        while Option.isNone !value do
            yield ()

        return Option.get !value
    }
