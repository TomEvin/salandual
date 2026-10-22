// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Operations/OperationsService.cs

using Avalonia.Threading;
using Salandual.Core.Operations;
using Salandual.Platform.Windows.FileSystem;
using Salandual.Platform.Windows.Shell;

namespace Salandual.App.Operations;

public sealed class OperationsService : IDisposable
{
    private readonly TaskbarProgress _taskbar = new();
    private readonly Dictionary<OperationJob, OperationQuestion> _asked = [];
    private bool _posted;
    private bool _awake;
    private bool _hadWork;

    public OperationsService()
    {
        Queue.Changed += (_, _) => Post();
        Queue.JobChanged += (_, _) => Post();
    }

    public OperationQueue Queue { get; } = new();

    public LocalOperations Files { get; } = new();

    public Func<nint>? Window { get; set; }

    public event EventHandler? Changed;

    public event EventHandler<OperationJob>? DialogQuestion;

    public event EventHandler<OperationJob>? Finished;

    public WhenDone WhenDone { get; set; }

    public event EventHandler<WhenDone>? QueueDone;

    public IReadOnlyList<OperationJob> Jobs => Queue.Jobs;

    public IEnumerable<OperationJob> Active => Queue.Jobs.Where(j => j.IsActive);

    public void Add(OperationJob job, bool toQueue)
    {
        _hadWork = true;
        job.QuestionsInPanel |= toQueue;
        var finished = false;
        job.Changed += (_, _) =>
        {
            if (job.IsFinished && !finished)
            {
                finished = true;
                Dispatcher.UIThread.Post(() => Finished?.Invoke(this, job));
            }
        };
        Queue.Add(job, toQueue);
    }

    public void Minimize(OperationJob job)
    {
        job.QuestionsInPanel = true;
        Post();
    }

    public void BringToFront(OperationJob job)
    {
        job.QuestionsInPanel = false;
        if (job.Question is (ConflictQuestion or CheckQuestion) and { } question)
        {
            _asked[job] = question;
            DialogQuestion?.Invoke(this, job);
        }
    }

    private void Post()
    {
        if (_posted)
        {
            return;
        }

        _posted = true;
        Dispatcher.UIThread.Post(Update, DispatcherPriority.Background);
    }

    private void Update()
    {
        _posted = false;
        var jobs = Queue.Jobs;
        foreach (var job in jobs)
        {
            if (job.Question is (ConflictQuestion or CheckQuestion) and { } question && !job.QuestionsInPanel)
            {
                if (!_asked.TryGetValue(job, out var shown) || !ReferenceEquals(shown, question))
                {
                    _asked[job] = question;
                    DialogQuestion?.Invoke(this, job);
                }
            }
            else if (job.IsFinished)
            {
                _asked.Remove(job);
            }
        }

        UpdateTaskbar(jobs);
        var working = jobs.Any(j => !j.IsFinished);
        if (_hadWork && !working && WhenDone != WhenDone.Nothing)
        {
            var choice = WhenDone;
            WhenDone = WhenDone.Nothing;
            QueueDone?.Invoke(this, choice);
        }

        _hadWork = working;
        var active = jobs.Any(j => j.IsActive);
        if (active != _awake)
        {
            _awake = active;
            KeepAwake.Set(active);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTaskbar(IReadOnlyList<OperationJob> jobs)
    {
        var window = Window?.Invoke() ?? 0;
        var active = jobs.Where(j => j.IsActive).ToList();
        if (active.Count == 0)
        {
            _taskbar.Set(window, TaskbarState.None);
            return;
        }

        var fraction = active.Average(j => j.Progress.Fraction);
        var state = active.Any(j => j.Question is ErrorQuestion) ? TaskbarState.Error
            : active.Any(j => j.Question is not null) || active.All(j => j.State == JobState.Paused) ? TaskbarState.Paused
            : TaskbarState.Normal;
        _taskbar.Set(window, state, fraction);
    }

    public void Dispose()
    {
        foreach (var job in Queue.Jobs.Where(j => !j.IsFinished))
        {
            job.Cancel();
        }

        if (_awake)
        {
            KeepAwake.Set(false);
        }

        _taskbar.Dispose();
    }
}
