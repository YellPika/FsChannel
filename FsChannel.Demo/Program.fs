open System
open FsChannel

Task.Run (task {
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
})
