# FsChannel

An implementation of Go style fibers and channels in FSharp.

## Fibers

Fibers (cooperative threads) are created using the `fiber`
computation expression syntax.
* `yield ()` is used to give control to the next available fiber.
* `yield! f` is used to start `f` as a new fiber.
* `Fiber.Run : Fiber<unit> -> unit` runs a fiber and does not return until that fiber completes execution.
Like in Go, any fiber started using `yield!` will terminate when the main one terminates.

Example:

    Fiber.Run (fiber {
        printfn "Hello World"
        
        let doneCount = ref 0
        for i in [1 .. 10] do
            yield! fiber {
                printfn "%d" i
                doneCount := !doneCount + 1
            }
        
        while !doneCount < 10 do
            yield ()
        
        printfn "done"
    })

    // Output:
    // Hello World
    // 1
    // 2
    // 3
    // 4
    // 5
    // 6
    // 7
    // 8
    // 9
    // 10
    // done

## Channels

Channels are represented by the `Channel<'a>` type. A channel has two members:
* `Send : 'a -> Signal<unit>` sends a value over a channel.
* `Receive : Signal<'a>` receives a value from a channel.
Both members return instances of the `Signal<'a>` class. `Signal<'a>`s are analogous to events in CSP.
* `Signal.Sync : Signal<'a> -> Fiber<'a>` causes a fiber to wait until a signal is invoked. For example,
`let! x = Signal.Sync myChannel.Receive` will block until a corresponding `do! Signal.Sync (myChannel.Send myValue)`
is made on another fiber.
* `Signal.Select : Signal<'a> list -> Signal<'a>` creates a new signal out of multiple signals. When the signal is
synced, it will return the value of the first signal in the list that is invoked.

Example:

    Fiber.Run (fiber {
        let channel = Channel<string> ()
        
        yield! fiber {
            printfn "Before Send"
            do! Signal.Sync (channel.Send "Hello World!")
            printfn "After Send"
        }
        
        printfn "Before Receive"
        let! x = Signal.Sync channel.Receive
        printfn "%s" x
        printfn "After Receive"
    })

    // Output:
    // Before Receive
    // Before Send
    // After Send
    // Hello World
    // After Receive

## Future Work
There are a few items on the todo list:
* **Support For Threads**: Right now, `Channel`s only work with `Fiber`s. It would be nice if they could be used with
regular threads as well.
* **Other Threading Constructs**: Channels serve as a good base for many other synchronization primitives. They should
be working their way into the library at some point in the future.
