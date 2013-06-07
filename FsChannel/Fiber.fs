namespace FsChannel

open System

/// Represents a lightweight cooperative thread of execution.
type [<NoComparison; NoEquality>]
    Fiber<'a> =
    /// Wraps a function representing a fiber.
    | Fiber of (unit -> FiberResult<'a>)
and [<NoComparison; NoEquality>]
    FiberResult<'a> =
    /// Terminate a fiber and return a value.
    | Done of 'a
    /// Queues the execution of an additional fiber.
    | Fork of Fiber<unit> * Fiber<'a>
    /// Temporarily halt execution and hand control over to the next available fiber.
    | Yield of Fiber<'a>

/// Operations on fibers.
module Fiber =
    /// Unwraps a fiber an executes the contained function.
    let Invoke (Fiber f) = f ()

    /// Returns a fiber that is build from the delayed specification of a fiber.
    let Delay f = Fiber (f >> Invoke)

    /// Creates a fiber that immediately returns a value.
    let Return x = Fiber (fun () -> Done x)

    /// A fiber that immediately returns the unit value.
    let Zero = Return ()

    /// Sequentially composes two fibers, passing the value
    /// produced by the first as an argument to the second.
    let rec Bind f x = Fiber <| fun () ->
        match Invoke x with
        | Done x' -> Invoke (f x')
        | Fork (f', x') -> Fork (f', Bind f x')
        | Yield x' -> Yield (Bind f x')

    /// Sequentially composes two fibers, discarding
    /// the value produced by the first.
    let Combine x y = Bind (fun () -> y) x

    /// Applies a transformation to the result of a fiber operation.
    let Map f = Bind (f >> Return)

    /// Sequentially composes a fiber and its return value.
    let Join x = Bind id x

    /// Creates a fiber that immediately returns a value.
    let Done x = Return x
    
    /// Creates a fiber that immediately queues
    /// the execution of another fiber.
    let Fork f = Fiber (fun () -> Fork (f, Done ()))

    /// Creates a fiber that immediately halts execution and
    /// passes control to the next available fiber.
    let Yield = Fiber (Done >> Yield)

    /// Creates a fiber that passes control to other fibers
    /// until the specified timespan has elapsed.
    let rec Wait (span : TimeSpan) =
        Return DateTime.Now
        |> Bind (fun start ->
            let elapsed = DateTime.Now - start
            if elapsed < span then
                Combine Yield (Wait (span - elapsed))
            else
                Zero)

    /// Fully executes a list of fibers.
    let Run x =
        let isRunning = ref true
        let mutable state = [Map (fun () -> isRunning := false) x]
        
        while !isRunning do
            state <-
                match state with
                | [] -> []
                | x::xs ->
                    match Invoke x with
                    | Done () -> xs
                    | Fork (a, b) -> b :: xs @ [a]
                    | Yield a -> xs @ [a]    

[<AutoOpen>]
module FiberComputation =
    type FiberBuilder () =
        // ABUSE!
        member this.Yield (()) = Fiber.Yield
        member this.YieldFrom x = Fiber.Fork x

        member this.Delay f = Fiber.Delay f

        member this.Return x = Fiber.Return x
        member this.ReturnFrom x = x
        member this.Zero () = Fiber.Zero

        member this.Bind (x, f) = Fiber.Bind f x
        member this.Combine (x, y) = this.Bind (x, fun () -> y)

        member this.TryWith (x, f) = Fiber (fun () ->
            try
                match Fiber.Invoke x with
                | Done value -> Done value
                | Fork (next, current) -> Fork (next, this.TryWith (current, f))
                | Yield current -> Yield (this.TryWith (current, f))
            with ex -> Fiber.Invoke (f ex))

        member this.TryFinally (x, f) = Fiber (fun () ->
            let mutable doFinally = false
            try
                try
                    match Fiber.Invoke x with
                    | Done value ->
                        doFinally <- true
                        Done value
                    | Fork (next, current) -> Fork (next, this.TryFinally (current, f))
                    | Yield current -> Yield (this.TryFinally (current, f))
                with _ ->
                    doFinally <- true
                    reraise ()
            finally
                if doFinally then
                    f ())

        member this.Using (x : #IDisposable, f) =
            this.TryFinally (f x, x.Dispose)

        member this.While (p, x) =
            match p () with
            | true -> this.Bind (x, fun () -> this.While (p, x))
            | false -> this.Zero ()

        member this.For (xs : seq<_>, f) =
            this.Using (xs.GetEnumerator(), fun enum ->
                this.While (enum.MoveNext, this.Delay (fun () -> f enum.Current)))

    /// Builds a fiber using computation expression syntax.
    let fiber = FiberBuilder ()
