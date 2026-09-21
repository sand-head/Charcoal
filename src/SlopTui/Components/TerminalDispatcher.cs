using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;

namespace SlopTui.Components;

/// <summary>
/// A Blazor dispatcher for the app loop's thread. Work from that thread runs
/// inline; work from other threads is queued and wakes the loop through
/// <see cref="Signal"/>.
/// </summary>
public sealed class TerminalDispatcher : Dispatcher
{
    private readonly ConcurrentQueue<Func<Task>> _queue = new();
    private int _threadId;

    /// <summary>Released whenever work is queued.</summary>
    public SemaphoreSlim Signal { get; } = new(0);

    public void BindToCurrentThread() => _threadId = Environment.CurrentManagedThreadId;

    public override bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public override Task InvokeAsync(Action workItem) =>
        InvokeAsync(() =>
        {
            workItem();
            return Task.CompletedTask;
        });

    public override Task InvokeAsync(Func<Task> workItem)
    {
        if (!CheckAccess()) return Post(workItem);
        try
        {
            return workItem();
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) =>
        InvokeAsync(() => Task.FromResult(workItem()));

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        if (CheckAccess())
        {
            try
            {
                return workItem();
            }
            catch (Exception exception)
            {
                return Task.FromException<TResult>(exception);
            }
        }

        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Post(async () =>
        {
            try
            {
                completion.SetResult(await workItem());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    private Task Post(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(async () =>
        {
            try
            {
                await work();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        Signal.Release();
        return completion.Task;
    }

    /// <summary>
    /// Runs everything queued so far. Failures of work nobody awaits are
    /// reported to <paramref name="onError"/>.
    /// </summary>
    public void RunPending(Action<Exception>? onError = null)
    {
        while (_queue.TryDequeue(out var work))
        {
            var task = work();
            if (task.IsCompleted)
            {
                ReportFailure(task, onError);
            }
            else
            {
                task.ContinueWith(finished => ReportFailure(finished, onError), TaskScheduler.Default);
            }
        }
    }

    private static void ReportFailure(Task task, Action<Exception>? onError)
    {
        if (task.IsFaulted) onError?.Invoke(task.Exception!.GetBaseException());
    }
}
