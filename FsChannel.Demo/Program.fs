open System
open System.Threading
open FsChannel

let channelTest1 = task {
    let! channel1 = Channel.Create
    let! channel2 = Channel.Create
    let! channel3 = Channel.Create

    let test send1 send2 receive = task {
        while true do
            let! result = (Signal.Sync << Signal.Select) [
                Signal.Map Some receive
                Signal.Map (fun () -> None) send1
                Signal.Map (fun () -> None) send2
            ]

            match result with
            | Some value ->
                // Turns out printf isn't thread safe.
                Console.Write (string value)
            | None -> ()
    }

    yield! test (channel1.Send "a") (channel2.Send "b") channel3.Receive
    yield! test (channel2.Send "c") (channel3.Send "d") channel1.Receive
    yield! test (channel3.Send "e") (channel1.Send "f") channel2.Receive
}

let channelTest2 = task {
    let! channel1 = Channel.Create
    let! channel2 = Channel.Create

    let test send receive = task {
        while true do
            let! result = (Signal.Sync << Signal.Select) [
                Signal.Map Some receive
                Signal.Map (fun () -> None) send
            ]

            match result with
            | Some value -> printf "%s" value
            | None -> ()
    }

    yield! test (channel1.Send "Foo") channel2.Receive
    yield! test (channel2.Send "Bar") channel1.Receive
}

let channelTest3 = task {
    let! channel = Channel.Create

    let test name send receive = task {
        while true do
            let! result = (Signal.Sync << Signal.Select) [
                Signal.Map Some receive
                Signal.Map (fun () -> None) send
            ]

            match result with
            | Some value -> printfn "%s: %s" name value
            | None -> ()
    }

    yield! test "Foo" (channel.Send "Foo") channel.Receive
    yield! test "Bar" (channel.Send "Bar") channel.Receive
}

let lockTest = task {
    let! lock = Task.Lock

    yield! (task {
        while true do
            do! (task {
                use! lock = lock.Acquire
                printfn "Spam!"
            })

            do! Task.Wait (TimeSpan.FromSeconds 0.5)
    })

    yield! (task {
        while true do
            while not Console.KeyAvailable do
                yield ()
            ignore (Console.ReadKey true)

            let! lock = lock.Acquire
            printfn "Lock acquired."

            while not Console.KeyAvailable do
                yield ()
            ignore (Console.ReadKey true)

            printfn "Lock released."
            do! lock.Dispose
    })
}

let asyncTest = task {
    yield! (task {
        while true do
            printfn "Hai World!"
            Thread.Sleep 1000
    })

    yield! (task {
        Thread.Sleep 500
        while true do
            printfn "Bai World!"
            Thread.Sleep 1000
    })
}

Task.RunAsync channelTest3
