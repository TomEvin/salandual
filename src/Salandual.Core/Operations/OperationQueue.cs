// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/OperationQueue.cs

namespace Salandual.Core.Operations;

public sealed class OperationQueue
{
    private readonly object _gate = new();
    private readonly List<OperationJob> _jobs = [];
    private readonly HashSet<OperationJob> _queued = [];
    private bool _paused;

    public event EventHandler? Changed;

    public event EventHandler<OperationJob>? JobChanged;

    public IReadOnlyList<OperationJob> Jobs
    {
        get { lock (_gate) { return [.. _jobs]; } }
    }

    public IReadOnlyList<OperationJob> Queued
    {
        get { lock (_gate) { return [.. _jobs.Where(j => _queued.Contains(j) && j.State == JobState.Queued)]; } }
    }

    public bool IsQueuePaused
    {
        get { lock (_gate) { return _paused; } }
    }

    public bool IsFromQueue(OperationJob job)
    {
        lock (_gate)
        {
            return _queued.Contains(job);
        }
    }

    public void Add(OperationJob job, bool toQueue = false)
    {
        job.SetWaiting(toQueue);
        job.Changed += OnJobChanged;
        lock (_gate)
        {
            _jobs.Add(job);
            if (toQueue)
            {
                _queued.Add(job);
            }
        }

        Pump();
    }

    public void StartNow(OperationJob job)
    {
        lock (_gate)
        {
            if (!_queued.Remove(job) || job.State != JobState.Queued)
            {
                return;
            }

            job.SetWaiting(userQueued: false);
        }

        Pump();
    }

    public void MoveUp(OperationJob job) => Move(job, -1);

    public void MoveDown(OperationJob job) => Move(job, +1);

    public void Remove(OperationJob job)
    {
        if (job.State is JobState.Queued or JobState.Waiting)
        {
            job.Cancel();
        }

        lock (_gate)
        {
            if (!job.IsFinished || !_jobs.Remove(job))
            {
                return;
            }

            _queued.Remove(job);
            job.Changed -= OnJobChanged;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        Pump();
    }

    public void SetQueuePaused(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused)
            {
                return;
            }

            _paused = paused;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        Pump();
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            foreach (var job in _jobs.Where(j => j.IsFinished).ToList())
            {
                _jobs.Remove(job);
                _queued.Remove(job);
                job.Changed -= OnJobChanged;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Move(OperationJob job, int step)
    {
        lock (_gate)
        {
            var waiting = _jobs.Where(j => _queued.Contains(j) && j.State == JobState.Queued).ToList();
            var at = waiting.IndexOf(job);
            var to = at + step;
            if (at < 0 || to < 0 || to >= waiting.Count)
            {
                return;
            }

            var other = waiting[to];
            var i = _jobs.IndexOf(job);
            var k = _jobs.IndexOf(other);
            (_jobs[i], _jobs[k]) = (_jobs[k], _jobs[i]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnJobChanged(object? sender, EventArgs e)
    {
        var job = (OperationJob)sender!;
        JobChanged?.Invoke(this, job);
        if (job.IsFinished)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            Pump();
        }
    }

    private void Pump()
    {
        var start = new List<OperationJob>();
        lock (_gate)
        {
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queueBusy = false;
            foreach (var job in _jobs)
            {
                if (job.IsActive)
                {
                    busy.UnionWith(job.Volumes);
                    queueBusy |= _queued.Contains(job);
                }
            }

            foreach (var job in _jobs)
            {
                var fromQueue = _queued.Contains(job);
                if (job.State is not (JobState.Waiting or JobState.Queued))
                {
                    continue;
                }

                var free = !job.Volumes.Any(busy.Contains);
                if (fromQueue)
                {
                    if (_paused || queueBusy)
                    {
                        continue;
                    }

                    queueBusy = true;
                }

                if (free)
                {
                    start.Add(job);
                }

                busy.UnionWith(job.Volumes);
            }
        }

        foreach (var job in start)
        {
            job.Start();
        }

        if (start.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
