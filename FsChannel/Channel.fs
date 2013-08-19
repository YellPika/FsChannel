namespace FsChannel

#nowarn "21" "40"

open System
open System.Threading
open SignalStatus

/// Represents a synchronous channel.
type Channel<'a> private (lock) =
    let senders = ResizeArray<_ * _ * 'a> ()
    let receivers = ResizeArray<_> ()

    let DequeueMatch predicate (list : ResizeArray<_>) =
        let rec DequeueMatch i =
            if i >= list.Count then
                None
            else if predicate list.[i] then
                let output = Some list.[i]
                list.RemoveAt i
                output
            else
                DequeueMatch (i + 1)

        DequeueMatch 0

    let Dequeue (list : ResizeArray<_>) =
        match list.Count with
        | 0 -> None
        | _ ->
            let output = list.[0]
            list.RemoveAt 0
            Some output

    let Enqueue value (list : ResizeArray<_>) =
        list.Add value

    let Undequeue value (list : ResizeArray<_>) =
        list.Insert (0, value)

    static member Create = task {
        let! lock = Task.Lock
        return Channel<'a> lock
    }

    /// Creates a signal that, when synchronized,
    /// sends the specified value over the channel.
    member this.Send value = {
        Poll = fun () -> receivers.Count <> 0
        Commit = task {
            use! lock = lock.Acquire

            let rec commit = task {
                match Dequeue receivers with
                | Some (state, receiver) ->
                    let rec sync = task {
                        match Interlocked.CompareExchange (state, Synced, Waiting) with
                        | Synced -> return! commit
                        | Waiting ->
                            receiver value
                            return Some ()
                        | _ ->
                            yield ()
                            return! sync
                    }

                    return! sync
                | None -> return None
            }

            return! commit
        }
        Block = fun (a, sender) -> task {
            use! lock = lock.Acquire

            let rec block = task {
                match DequeueMatch (fun (b, _) -> not (obj.ReferenceEquals (a, b))) receivers with
                | Some (b, receiver) ->
                    let rec sync = task {
                        match Interlocked.CompareExchange (a, Claimed, Waiting) with
                        | Waiting ->
                            let result = Interlocked.CompareExchange (b, Synced, Waiting)
                    
                            let a' = if result = Waiting then Synced else Waiting
                            ignore (Interlocked.Exchange (a, a'))

                            match result with
                            | Synced -> return! block
                            | Claimed -> return! sync
                            | _ ->
                                sender ()
                                receiver value
                        | _ -> Undequeue (b, receiver) receivers
                    }

                    do! sync
                | None -> senders.Add (a, sender, value)
            }

            do! block
        }
    }

    /// Creates a signal that, when synchronized,
    /// receives a value from the channel.
    member this.Receive = {
        Poll = fun () -> senders.Count <> 0
        Commit = task {
            use! lock = lock.Acquire

            let rec commit = task {
                match Dequeue senders with
                | Some (state, sender, value) ->
                    let rec sync = task {
                        match Interlocked.CompareExchange (state, Synced, Waiting) with
                        | Synced -> return! commit
                        | Waiting ->
                            sender ()
                            return Some value
                        | _ ->
                            yield ()
                            return! sync
                    }

                    return! sync
                | None -> return None
            }

            return! commit
        }
        Block = fun (a, receiver) -> task {
            use! lock = lock.Acquire

            let rec block = task {
                match DequeueMatch (fun (b, _, _) -> not (obj.ReferenceEquals (a, b))) senders with
                | Some (b, sender, value) ->
                    let rec sync = task {
                        match Interlocked.CompareExchange (a, Claimed, Waiting) with
                        | Waiting ->
                            let result = Interlocked.CompareExchange (b, Synced, Waiting)
                    
                            let a' = if result = Waiting then Synced else Waiting
                            ignore (Interlocked.Exchange (a, a'))

                            match result with
                            | Synced -> return! block
                            | Claimed -> return! sync
                            | _ ->
                                sender ()
                                receiver value
                        | _ -> Undequeue (b, sender, value) senders
                    }

                    do! sync
                | None -> receivers.Add (a, receiver)
            }

            do! block
        }
    }
