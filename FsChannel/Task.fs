namespace FsChannel

open System

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
    /// Temporarily halts execution and hand control over to the next available task.
    | Yield of Task<'a>
    /// Temporarily halts execution for the specified amount of time.
    | Wait of TimeSpan * Task<'a>
    /// Requests a lock object.
    | Lock of (ILock -> unit) * Task<'a>
/// A synchronization primitive that provides exclusive access to a critical section.
and ILock =
    /// Acquires exclusive access to this lock.
    abstract Acquire : Task<ITaskDisposable>
/// Defines a method to release allocated resources within a task.
and ITaskDisposable =
    /// Performs application-defined tasks associated with
    /// freeing, releasing, or resetting unmanaged resources.
    abstract Dispose : Task<unit>
