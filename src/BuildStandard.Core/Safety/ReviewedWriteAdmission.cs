namespace BuildStandard.Core.Safety;

/// <summary>
/// Last software admission for an already reviewed candidate write. Original expiry inputs cannot be refreshed by
/// preflight or authentication. This is not approval, a retry mechanism or remote transactional atomicity.
/// The binding check is supplied by the trusted execution engine, never by document or script content.
/// </summary>
public sealed class ReviewedWriteAdmission
{
    private readonly DateTimeOffset _snapshotAt, _planAt;
    private readonly TimeSpan _maxSnapshotAge, _maxPlanAge;
    private readonly IClock _clock;
    private readonly Action _assertCurrentBinding;
    private readonly Func<bool> _stopRequested;

    public ReviewedWriteAdmission(DateTimeOffset snapshotAt, DateTimeOffset planAt, TimeSpan maxSnapshotAge,
        TimeSpan maxPlanAge, IClock clock, Action assertCurrentBinding, Func<bool> stopRequested)
    {
        if (maxSnapshotAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxSnapshotAge));
        if (maxPlanAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxPlanAge));
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(assertCurrentBinding);
        ArgumentNullException.ThrowIfNull(stopRequested);
        _snapshotAt = snapshotAt; _planAt = planAt; _maxSnapshotAge = maxSnapshotAge; _maxPlanAge = maxPlanAge;
        _clock = clock; _assertCurrentBinding = assertCurrentBinding; _stopRequested = stopRequested;
    }

    public void AssertAllowed()
    {
        AssertNotStopped();
        _assertCurrentBinding();
        // Binding verification can take time. Check the original limits after it, at the actual admission boundary.
        AssertNotStopped();
        var now = _clock.UtcNow;
        if (now - _snapshotAt > _maxSnapshotAge || now - _planAt > _maxPlanAge)
            throw new PlanValidationException("The original reviewed plan or before-change snapshot expired before dispatch. No write may be sent; capture and review a fresh plan.");
    }

    private void AssertNotStopped()
    {
        if (_stopRequested()) throw new OperationCanceledException("Stopped before Graph dispatch; no write may be sent.");
    }
}
