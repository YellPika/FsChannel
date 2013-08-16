namespace FsChannel

/// Represents a lightweight cooperative thread of execution.
type [<NoComparison; NoEquality>]
    Task<'a> =
    /// Wraps a function representing a task.
    | Task of (unit -> TaskResult<'a>)
and [<NoComparison; NoEquality>]
    TaskResult<'a> =
    /// Terminate a task and return a value.
    | Done of 'a
    /// Queues the execution of an additional task.
    | Fork of Task<unit> * Task<'a>
    /// Temporarily halt execution and hand control over to the next available task.
    | Yield of Task<'a>
