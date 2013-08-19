namespace FsChannel

open System

/// An abstract representation of a synchronous operation.
[<NoComparison; NoEquality>]
type Signal<'a> = {
    Poll : unit -> bool
    Commit : Task<'a option>
    Block : int ref * ('a -> unit) -> Task<unit>
}

/// Constants representing signal statuses.
module SignalStatus =
    /// Indicates that a signal is waiting to be synced.
    [<Literal>]
    let Waiting = 0

    /// Indicates that a signal is in the middle of synchronization.
    [<Literal>]
    let Claimed = 1

    /// Indicates that a signal has been synced.
    [<Literal>]
    let Synced = 2

/// Operations on signals.
module Signal =
    let private random = Random ()

    /// Creates an event which computes the given function upon
    /// synchronization, and then behaves like the returned event.
    let Delay source =
        let source = lazy (source ())
        { Poll = fun () -> source.Value.Poll ()
          Commit = Task.Delay (fun () -> source.Value.Commit)
          Block = fun x -> source.Value.Block x }

    /// Creates a signal that always passes the specified value to subscribers.
    let Always value = {
        Poll = fun () -> true
        Commit = Task.Return (Some value)
        Block = ignore >> Task.Return
    }

    /// A signal that never notifies its subscribers.
    let Never<'a> : Signal<'a> = {
        Poll = fun () -> false
        Commit = Task.Return None
        Block = ignore >> Task.Return
    }

    /// Wraps a post synchronization operation around the specified signal.
    let Map selector source = {
        Poll = source.Poll
        Commit = Task.Map (Option.map selector) source.Commit
        Block = fun (s, f) -> source.Block (s, selector >> f)
    }

    /// Creates a signal that represents the non-deterministic choice of two signals.
    let Choose signal1 signal2 =
        let signal1, signal2 =
            match random.Next (0, 2) with
            | 0 -> signal2, signal1
            | _ -> signal1, signal2

        { Poll = fun () -> signal1.Poll () || signal2.Poll ()
          Commit = task {
            let! signal1 = signal1.Commit
            match signal1 with
            | None -> return! signal2.Commit
            | output -> return output
          }
          Block = fun (s, f) -> task {
            yield! signal1.Block (s, f)
            return! signal2.Block (s, f)
          }
        }

    /// Creates a signal that represents the non-deterministic
    /// choice of the specified list of signals.
    let Select signals = Seq.fold Choose Never signals

    /// Synchronizes the calling fiber with the specified signal.
    let Sync signal = task {
        let output = ref None

        if signal.Poll () then
            let! result = signal.Commit
            output := result

        if Option.isNone !output then
            yield! signal.Block (ref SignalStatus.Waiting, (Some >> (:=) output))

        while Option.isNone !output do
            yield ()

        return Option.get !output
    }
