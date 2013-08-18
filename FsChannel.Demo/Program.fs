open System
open FsChannel

#nowarn "40"

type KeyListener = {
    Next : Signal<char>
    Close : Signal<unit>
} with
    interface ITaskDisposable with
        member this.Dispose = Signal.Sync this.Close

let keyListener = task {
    let next = Channel<_> ()
    let close = Channel<_> ()

    let rec waitForKey = task {
        let! shouldClose = (Signal.Sync << Signal.Select) [
            Signal.Map (fun _ -> true) close.Receive;
            Signal.Always false
        ]

        if not shouldClose then
            if Console.KeyAvailable then
                let key = Console.ReadKey ()
                let! result = (Signal.Sync << Signal.Select) [
                    Signal.Map Choice1Of2 (next.Send key.KeyChar);
                    Signal.Map Choice2Of2 close.Receive
                ]
               
                match result with
                | Choice1Of2 () -> return! waitForKey
                | Choice2Of2 () -> return ()
            else
                yield ()
                return! waitForKey
    }

    yield! waitForKey

    return {
        Next = next.Receive
        Close = close.Send ()
    }
}

Task.Run (task {
    use! listener = keyListener

    try
        while true do
            printf "Please enter a single digit number: "
            let! key = Signal.Sync listener.Next

            let number = Int32.Parse (string key)
            printfn "\nYou entered: %d" number

            do! Task.Wait (TimeSpan.FromSeconds 1.0)
    with ex ->
        printfn "\nYou entered a non-digit! I HATES YOUS! Bai."
})
