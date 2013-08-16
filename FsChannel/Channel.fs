namespace FsChannel

open System
open System.Collections.Generic

/// Represents a synchronous channel.
type Channel<'a> () =
    let senders = List<(unit -> unit) * 'a> ()
    let receivers = List<'a -> unit> ()

    let Enqueue value (list : List<_>) =
        list.Add value
        Disposable.Create (fun () -> list.Remove value |> ignore)

    let Dequeue value sender receiver =
        sender ()
        receiver value
        Disposable.Create id

    /// Creates a signal that, when synchronized,
    /// sends the specified value over the channel.
    member this.Send value = Signal.Create (fun sender -> task {
        match receivers.Count with
        | 0 -> return Enqueue (sender, value) senders
        | _ ->
            let receiver = receivers.[0]
            receivers.RemoveAt 0
            
            return Dequeue value sender receiver
    })

    /// Creates a signal that, when synchronized,
    /// receives a value from the channel.
    member this.Receive = Signal.Create (fun receiver -> task {
        match senders.Count with
        | 0 -> return Enqueue receiver receivers
        | _ ->
            let sender, value = senders.[0]
            senders.RemoveAt 0

            return Dequeue value sender receiver
    })
