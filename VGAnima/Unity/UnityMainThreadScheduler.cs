using System;
using System.Collections.Generic;
using UnityEngine;

namespace VGAnima.Unity;

/// <summary>
/// Unity MonoBehaviour that drains a thread-safe queue of <see cref="Action"/>s
/// every frame on the main thread. Callers enqueue continuations from arbitrary
/// threads (e.g. <c>Task.ContinueWith</c> after an HTTP call) and the actions run
/// on the next Unity <c>Update</c> tick, where game state is safe to touch.
///
/// Bootstrapped once in <see cref="Plugin.Awake"/> via
/// <c>gameObject.AddComponent&lt;UnityMainThreadScheduler&gt;()</c>. The host
/// GameObject is the Plugin's own <c>BaseUnityPlugin</c> GameObject, so the
/// scheduler lives for the process lifetime.
///
/// Draining per-frame has unbounded worst-case cost, but in practice the queue
/// holds at most one action per broker injection per bar refresh — single-digit
/// items over seconds. No throttle needed.
/// </summary>
internal sealed class UnityMainThreadScheduler : MonoBehaviour
{
    /// <summary>Captured on Awake so Enqueue can assert it's actually on the
    /// main thread and short-circuit enqueue → invoke when possible. Not used
    /// for that optimisation yet — this is just a forward hook.</summary>
    private static UnityMainThreadScheduler? _instance;

    private readonly Queue<Action> _queue = new();
    private readonly object _lock = new();

    private void Awake()
    {
        _instance = this;
    }

    private void OnDestroy()
    {
        // Clear pending work if the plugin is torn down mid-continuation —
        // actions closed over mutable game state would NRE otherwise.
        lock (_lock)
        {
            _queue.Clear();
        }
        _instance = null;
    }

    /// <summary>Schedules <paramref name="action"/> to run on the next Unity
    /// <c>Update</c> tick. Thread-safe; callable from any thread. Silently
    /// drops the action if the scheduler has been destroyed (plugin unload).</summary>
    public void Enqueue(Action action)
    {
        if (action == null) return;
        lock (_lock)
        {
            _queue.Enqueue(action);
        }
    }

    private void Update()
    {
        // Swap the current queue with an empty one under lock so long-running
        // actions can't block Enqueue callers. We only drain what's present at
        // tick start — anything enqueued by an action during drain runs next frame.
        Action[]? batch = null;
        lock (_lock)
        {
            if (_queue.Count == 0) return;
            batch = _queue.ToArray();
            _queue.Clear();
        }

        foreach (var action in batch)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"UnityMainThreadScheduler action threw: {ex}");
            }
        }
    }
}
