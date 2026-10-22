// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.Core/Operations/OperationJob.cs

using System.Diagnostics;

namespace Salandual.Core.Operations;

public enum JobState
{
    Queued,
    Waiting,
    Running,
    Paused,
    Done,
    DoneWithIssues,
    Cancelled,
    Failed,
}

public sealed class OperationJob
{
    private static int _lastId;
    private readonly object _gate = new();
    private readonly Action<OperationJob> _work;
    private readonly ManualResetEventSlim _running = new(true);
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<OperationIssue> _issues = [];
    private readonly List<string> _targets = [];
    private readonly Stopwatch _clock = new();
    private readonly Queue<(double Seconds, long Bytes, int Items)> _window = new();
    private readonly List<SpeedSample> _samples = [];
    private TaskCompletionSource<OperationAnswer>? _answer;
    private OperationProgress _progress = OperationProgress.Start;
    private long _lastReport;
    private double _peak, _lastSampleSeconds = -1, _lastSampleFraction = -1;
    private long _speedLimit;
    private int _speedLimitVersion;
    private JobState _state;

    public OperationJob(OperationKind kind, string title, string route, IReadOnlyCollection<string> volumes, Action<OperationJob> work)
    {
        Id = Interlocked.Increment(ref _lastId);
        Kind = kind;
        Title = title;
        Route = route;
        Volumes = volumes;
        _work = work;
    }

    public int Id { get; }

    public OperationKind Kind { get; }

    public string Title { get; }

    public string Route { get; }

    public string? Subject { get; init; }

    public IReadOnlyCollection<string> Volumes { get; }

    public JobState State
    {
        get { lock (_gate) { return _state; } }
    }

    public bool IsFinished => State is JobState.Done or JobState.DoneWithIssues or JobState.Cancelled or JobState.Failed;

    public bool IsActive => State is JobState.Running or JobState.Paused;

    public OperationProgress Progress
    {
        get { lock (_gate) { return _progress; } }
    }

    public OperationQuestion? Question { get; private set; }

    public IReadOnlyList<OperationIssue> Issues
    {
        get { lock (_gate) { return [.. _issues]; } }
    }

    public Exception? Error { get; private set; }

    public Task Completion => _completion.Task;

    public CancellationToken CancellationToken => _cancel.Token;

    public long SpeedLimit
    {
        get { lock (_gate) { return _speedLimit; } }
        set
        {
            lock (_gate)
            {
                _speedLimit = Math.Max(0, value);
                _speedLimitVersion++;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal int SpeedLimitVersion
    {
        get { lock (_gate) { return _speedLimitVersion; } }
    }

    public bool QuestionsInPanel { get; set; }

    public int? ExpectedConflicts { get; set; }

    public IReadOnlyList<SpeedSample> SpeedHistory
    {
        get { lock (_gate) { return [.. _samples]; } }
    }

    public event EventHandler? Changed;

    public void Pause()
    {
        lock (_gate)
        {
            if (_state != JobState.Running)
            {
                return;
            }

            _state = JobState.Paused;
            _running.Reset();
            _clock.Stop();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_state != JobState.Paused)
            {
                return;
            }

            _state = JobState.Running;
            _clock.Start();
            _running.Set();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        bool notStarted;
        lock (_gate)
        {
            if (IsFinishedState(_state))
            {
                return;
            }

            notStarted = _state is JobState.Queued or JobState.Waiting;
        }

        _cancel.Cancel();
        _answer?.TrySetResult(new OperationAnswer(AnswerKind.Cancel));
        _running.Set();
        if (notStarted)
        {
            Finish(JobState.Cancelled);
        }
    }

    public void Answer(OperationAnswer answer) => _answer?.TrySetResult(answer);

    internal void SetWaiting(bool userQueued)
    {
        lock (_gate)
        {
            _state = userQueued ? JobState.Queued : JobState.Waiting;
        }
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (_state is not (JobState.Queued or JobState.Waiting))
            {
                return;
            }

            _state = JobState.Running;
            _clock.Start();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        var thread = new Thread(Run) { IsBackground = true, Name = "Salandual " + Kind };
        thread.Start();
    }

    private void Run()
    {
        try
        {
            _work(this);
            Finish(Issues.Count > 0 ? JobState.DoneWithIssues : JobState.Done);
        }
        catch (OperationCanceledException)
        {
            Finish(JobState.Cancelled);
        }
        catch (Exception ex)
        {
            Error = ex;
            Finish(JobState.Failed);
        }
    }

    private void Finish(JobState state)
    {
        lock (_gate)
        {
            if (IsFinishedState(_state))
            {
                return;
            }

            _state = state;
            _clock.Stop();
            Question = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        _completion.TrySetResult();
    }

    private static bool IsFinishedState(JobState state) => state is JobState.Done or JobState.DoneWithIssues or JobState.Cancelled or JobState.Failed;

    public void Checkpoint()
    {
        _running.Wait(_cancel.Token);
        _cancel.Token.ThrowIfCancellationRequested();
    }

    public OperationAnswer Ask(OperationQuestion question)
    {
        var answer = new TaskCompletionSource<OperationAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _answer = answer;
            Question = question;
            _clock.Stop();
        }

        if (_cancel.IsCancellationRequested)
        {
            answer.TrySetResult(new OperationAnswer(AnswerKind.Cancel));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        var result = answer.Task.GetAwaiter().GetResult();
        lock (_gate)
        {
            Question = null;
            _answer = null;
            if (_state == JobState.Running)
            {
                _clock.Start();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (result.Kind == AnswerKind.Cancel)
        {
            _cancel.Cancel();
            throw new OperationCanceledException(_cancel.Token);
        }

        return result;
    }

    public IReadOnlyList<string> Targets
    {
        get { lock (_gate) { return [.. _targets]; } }
    }

    internal void AddTarget(string path)
    {
        lock (_gate)
        {
            _targets.Add(path);
        }
    }

    public void AddIssue(string path, string message)
    {
        lock (_gate)
        {
            _issues.Add(new OperationIssue(path, message));
        }
    }

    public void Report(OperationPhase phase, long doneBytes, long totalBytes, int doneItems, int totalItems, string? current, bool force = false, long currentDone = 0, long currentTotal = 0)
    {
        bool raise;
        lock (_gate)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            var previous = _progress;
            double speed = previous.BytesPerSecond, items = previous.ItemsPerSecond;
            var measured = false;
            if (phase == OperationPhase.Working)
            {
                if (_window.Count == 0 || seconds - _window.Last().Seconds >= 0.1)
                {
                    _window.Enqueue((seconds, doneBytes, doneItems));
                }

                while (_window.Count > 2 && seconds - _window.Peek().Seconds > 2)
                {
                    _window.Dequeue();
                }

                var (s0, b0, i0) = _window.Peek();
                if (seconds - s0 >= 0.25)
                {
                    speed = (doneBytes - b0) / (seconds - s0);
                    items = (doneItems - i0) / (seconds - s0);
                    measured = true;
                }
            }

            _peak = Math.Max(_peak, speed);
            var average = seconds > 0.25 ? doneBytes / seconds : 0;
            var averageItems = seconds > 0.25 ? doneItems / seconds : 0;
            _progress = new OperationProgress(phase, doneBytes, totalBytes, doneItems, totalItems, current, speed, average, _peak, items, averageItems, currentDone, currentTotal);
            if (measured)
            {
                AddSample(_progress, seconds);
            }

            var now = Environment.TickCount64;
            raise = force || now - _lastReport >= 100;
            if (raise)
            {
                _lastReport = now;
            }
        }

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddSample(OperationProgress p, double seconds)
    {
        var fraction = p.Fraction;
        if (fraction - _lastSampleFraction < 0.005 && seconds - _lastSampleSeconds < 0.5)
        {
            return;
        }

        _lastSampleFraction = fraction;
        _lastSampleSeconds = seconds;
        _samples.Add(new SpeedSample(fraction, p.BytesPerSecond, p.ItemsPerSecond));
        if (_samples.Count > 400)
        {
            for (var i = _samples.Count - 2; i > 0; i -= 2)
            {
                _samples.RemoveAt(i);
            }
        }
    }

    public TimeSpan Elapsed
    {
        get { lock (_gate) { return _clock.Elapsed; } }
    }
}
