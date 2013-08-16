namespace FsChannel

open System

/// An abstract representation of a synchronous operation.
[<NoComparison; NoEquality>]
type Signal<'a> = {
    /// Registers a function to be invoked when the signal is activated.
    /// A disposable object is returned, which may be used to cancel the registration.
    Connect : ('a -> unit) -> Task<IDisposable>
}

/// Operations on signals.
module Signal =
    let private random = Random ()

    /// Creates a signal using the specified function as the connection function.
    let Create connect = { Connect = connect }
    
    /// Registers a function to be invoked when the signal is activated.
    /// A disposable object is returned, which may be used to cancel the registration.
    let Connect handler source = source.Connect handler

    /// Creates an event which computes the given function upon
    /// synchronization, and then behaves like the returned event.
    let Delay source = Create (fun handler ->
        let source' = source ()
        Connect handler source')

    /// Creates a signal that always passes the specified value to subscribers.
    let Always value = Create (fun handler -> task {
        handler value
        return Disposable.Create ignore
    })

    /// A signal that never notifies its subscribers.
    let Never<'a> : Signal<'a> = Create (fun _ ->
        Task.Return (Disposable.Create ignore))

    /// Wraps a post synchronization operation around the specified signal.
    let Map selector source = Create (fun handler ->
        Connect (selector >> handler) source)

    /// Creates a signal that represents the non-deterministic choice of two signals.
    let Choose signal1 signal2 = Create (fun handler -> task {
        // Randomize the choices.
        let signal1, signal2 = 
            match random.Next (0, 2) with
            | 0 -> signal1, signal2
            | _ -> signal2, signal1

        let connection1 = ref (Disposable.Create id)
        let connection2 = ref (Disposable.Create id)

        let ignoreSecond = ref false

        // Need to store the result in temp because we can't directly
        // assign to a reference cell using let!.
        let! temp = signal1.Connect (fun value ->
            // Connecting to signal1 could cause this callback to trigger
            // immediately, in which case we can forgo connecting to the second.
            ignoreSecond := true

            (!connection2).Dispose ()
            handler value)
        connection1 := temp

        if not !ignoreSecond then
            let! temp = signal2.Connect (fun value ->
                (!connection1).Dispose ()
                handler value)
            connection2 := temp

        return Disposable.Create (fun () ->
            (!connection1).Dispose ()
            (!connection2).Dispose ())
    })

    /// Creates a signal that represents the non-deterministic
    /// choice of the specified list of signals.
    let Select signals = Seq.fold Choose Never signals

    /// EXPERIMENTAL: Monadic bind for signals.
    let Bind selector source = Create (fun f -> task {
        let connection = ref (Disposable.Create id)
        let sourceValue = ref None

        let! temp = source.Connect (Some >> ((:=) sourceValue))
        connection := temp

        yield! (task {
            while Option.isNone !sourceValue do
                yield ()

            let! temp = (selector !sourceValue).Connect f
            connection := temp
        })

        return Disposable.Create (fun () ->
            (!connection).Dispose ())
    })

    /// Delays the communication of a signal by the specified time span.
    let Wait span source = Create (fun handler -> task {
        let cancel = ref false
        let connection = ref (Disposable.Create (fun () -> cancel := true))

        yield! task {
            do! Task.Wait span

            // If the user cancelled the connection before
            // span elapsed, then we can ignore this.
            if not !cancel then
                let! temp = Connect handler source
                connection := temp
        }

        return Disposable.Create (fun () -> (!connection).Dispose ())        
    })

    /// Wraps an signal such that it will return None if the specified
    /// time span elapses before the signal is communicated.
    let TimeOut span source = Choose (Map Some source) (Wait span (Always None))

    /// Synchronizes the calling fiber with the specified signal.
    let Sync signal = task {
        let value = ref None
        let! _ = signal.Connect (Some >> (:=) value)

        while Option.isNone !value do
            yield ()

        return Option.get !value
    }
