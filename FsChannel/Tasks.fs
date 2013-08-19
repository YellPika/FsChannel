namespace FsChannel

open System
open System.Collections.Generic
open System.Threading

/// Operations on tasks.
module Task =
    /// Unwraps a task an executes the contained function.
    let Invoke (Task invoke) = invoke ()

    /// Returns a task that is build from the delayed specification of a task.
    let Delay func = Task (func >> Invoke)

    /// Creates a task that immediately returns a value.
    let Return value = Task (fun () -> Done value)

    /// A task that immediately returns the unit value.
    let Zero = Return ()

    /// Sequentially composes two tasks, passing the value
    /// produced by the first as an argument to the second.
    let rec Bind selector source = Task <| fun () ->
        match Invoke source with
        | Done value -> Invoke (selector value)
        | Fork (task, next) -> Fork (task, Bind selector next)
        | Yield next -> Yield (Bind selector next)
        | Wait (span, next) -> Wait (span, Bind selector next)
        | Lock (assign, next) -> Lock (assign, Bind selector next)

    /// Sequentially composes two tasks, discarding
    /// the value produced by the first.
    let Combine first second = first |> Bind (fun () -> second)

    /// Applies a transformation to the result of a task operation.
    let Map selector = Bind (selector >> Return) 

    /// Applies a transformation returned by a task to the result of a task operation.
    let Apply selector = Bind (fun x -> Map ((|>) x) selector)
    
    /// Sequentially composes a task and its return value.
    let Join source = Bind id source

    /// Specifies the action to take if a task throws an exception.
    let rec TryWith handler source = Task <| fun () ->
        try
            match Invoke source with
            | Done value -> Done value
            | Fork (task, next) -> Fork (task, TryWith handler next)
            | Yield current -> Yield (TryWith handler current)
            | Wait (span, next) -> Wait (span, TryWith handler next)
            | Lock (assign, next) -> Lock (assign, TryWith handler next)
        with ex -> Invoke (handler ex)

    /// Specifies an action to take when a task completes,
    /// regardless of whether it threw an exception.
    let TryFinally handler =
        TryWith (fun x -> handler |> Map (fun () -> raise x))
        >> Bind (fun x -> handler |> Map (fun () -> x))
    
    /// Executes a task with a disposable argument,
    /// and ensures the argument is disposed when the task completes.
    let Using selector source =
        TryFinally (TaskDisposable.Dispose source) (selector source)

    /// Repeats a task while a predicate is true.
    let rec While predicate source = Delay (fun () ->
        match predicate () with
        | true -> Bind (fun () -> While predicate source) source
        | false -> Zero)

    /// Executes a task for every item in a sequence.
    let For selector (source : seq<_>) =
        let enum = source.GetEnumerator ()
        enum
        |> Disposable.ToTaskDisposable
        |> Using (fun _ -> While enum.MoveNext (Delay (fun () -> selector enum.Current)))

    /// Creates a task that immediately returns a value.
    let Done value = Return value
    
    /// Creates a task that immediately queues
    /// the execution of another task.
    let Fork task = Task (fun () -> Fork (task, Zero))

    /// Creates a task that immediately halts execution and
    /// passes control to the next available task.
    let Yield = Task (fun () -> Yield Zero)

    /// Creates a task that passes control to other tasks
    /// until the specified timespan has elapsed.
    let Wait (span : TimeSpan) = Task (fun () -> Wait (span, Zero))

    /// Creates a task that returns a newly created lock object.
    let Lock = Task (fun () ->
        let output = ref Unchecked.defaultof<ILock>
        let next = Delay (fun () -> Return !output)
        Lock ((:=) output, next))

    /// Fully executes a task in a single thread.
    let Run task =
        let wait (span : TimeSpan) = Delay <| fun () ->
            let start = DateTime.Now
            While (fun () -> DateTime.Now - start < span)
                Yield

        let createLock () =
            let queue = Queue<_> ()

            let unlock = {
                new ITaskDisposable with
                    member this.Dispose =
                        Delay (Return << ignore << queue.Dequeue)
            }

            { new ILock with
                member this.Acquire = Delay <| fun () ->
                    let id = obj ()
                    queue.Enqueue id
                    
                    Combine
                        (While (queue.Peek >> (<>) id)
                            Yield)
                        (Return unlock)
            }

        let rec evaluate xs = function
            | Done () -> xs
            | Fork (x, y) -> x :: xs @ [y]
            | Yield x -> xs @ [x]
            | Wait (t, x) -> Combine (wait t) x :: xs
            | Lock (a, x) ->
                a (createLock ())
                x :: xs

        let rec run = function
            | [] -> ()
            | x::xs -> run (evaluate xs (Invoke x))

        run [task]

    /// Fully executes a task over multiple threads.
    let RunAsync task =
        let createLock () =
            let semaphore = new SemaphoreSlim 1

            let unlock = {
                new ITaskDisposable with
                    member this.Dispose =
                        Delay (semaphore.Release
                               >> ignore
                               >> Return)
            }

            { new ILock with
                member this.Acquire = Delay <| fun () ->
                    ignore (semaphore.Wait ())
                    Return unlock
            }

        let rec toAsync task = async {
            match Invoke task with
            | Done () -> return ()
            | Fork (x, y) ->
                do! [toAsync x; toAsync y]
                    |> Async.Parallel
                    |> Async.Ignore
            | Yield x ->
                do! Async.SwitchToThreadPool ()
                do! toAsync x
            | Wait (t, x) ->
                do! Async.Sleep (int t.TotalMilliseconds)
                do! toAsync x
            | Lock (a, x) ->
                a (createLock ())
                do! toAsync x
        }

        Async.RunSynchronously (toAsync task)

[<AutoOpen>]
module TaskComputation =
    type TaskBuilder () =
        // ABUSE!
        member this.Yield (()) = Task.Yield
        member this.YieldFrom x = Task.Fork x

        member this.Delay f = Task.Delay f

        member this.Return x = Task.Return x
        member this.ReturnFrom x = x
        member this.Zero () = Task.Zero

        member this.Bind (x, f) = Task.Bind f x
        member this.Combine (x, y) = Task.Combine x y

        member this.TryWith (x, f) = Task.TryWith f x
        member this.TryFinally (x, f) = Task.TryFinally f 
        member this.Using (x, f) = Task.Using f x

        member this.While (p, x) = Task.While p x
        member this.For (xs : seq<_>, f) = Task.For f xs

    /// Builds a task using computation expression syntax.
    let task = TaskBuilder ()
