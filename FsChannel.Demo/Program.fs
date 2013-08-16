open System
open FsChannel

Task.Run (task {
    let c1 = Channel<_> ()
    let c2 = Channel<_> ()

    yield! (task {
        while true do
            let! v = (Signal.Sync << Signal.Select) [
                Signal.Map Choice1Of2 (c1.Send "Hai");
                Signal.Map Choice2Of2 c2.Receive
            ]

            match v with
            | Choice1Of2 () -> do! Task.Wait (TimeSpan.FromSeconds 1.0)
            | Choice2Of2 value -> printfn "%s" value
    })

    yield! (task {
        while true do
            let! v = (Signal.Sync << Signal.Select) [
                Signal.Map Choice1Of2 (c2.Send "Bai");
                Signal.Map Choice2Of2 c1.Receive
            ]

            match v with
            | Choice1Of2 () -> do! Task.Wait (TimeSpan.FromSeconds 1.0)
            | Choice2Of2 value -> printfn "%s" value
    })
})
