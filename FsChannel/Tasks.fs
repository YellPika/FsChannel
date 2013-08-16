namespace FsChannel

open System

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
            | Fork (next, current) -> Fork (next, TryWith handler current)
            | Yield current -> Yield (TryWith handler current)
        with ex -> Invoke (handler ex)

    /// Specifies an action to take when a task completes,
    /// regardless of whether it threw an exception.
    let TryFinally handler =
        Bind (fun x -> Map (fun () -> x) handler)
        >> TryWith (fun ex -> Map (fun () -> raise ex) handler)
    
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
        |> TaskDisposable.FromDisposable
        |> Using (fun _ -> While enum.MoveNext (Delay (fun () -> selector enum.Current)))

    /// Creates a task that immediately returns a value.
    let Done value = Return value
    
    /// Creates a task that immediately queues
    /// the execution of another task.
    let Fork task = Task (fun () -> Fork (task, Done ()))

    /// Creates a task that immediately halts execution and
    /// passes control to the next available task.
    let Yield = Task (Done >> Yield)

    /// Creates a task that passes control to other tasks
    /// until the specified timespan has elapsed.
    let Wait (span : TimeSpan) = Delay (fun () ->
        let start = DateTime.Now
        While (fun () -> DateTime.Now - start < span)
            Yield)

    /// Fully executes a list of tasks.
    let Run task =
        let rec evaluate xs = function
            | Done () -> xs
            | Fork (a, b) -> b :: xs @ [a]
            | Yield a -> xs @ [a]

        let rec run = function
            | [] -> ()
            | x::xs -> run (evaluate xs (Invoke x))

        run [task]

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

    let task = TaskBuilder ()
