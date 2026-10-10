using BDIT.TenantToolkit.Core;
using Microsoft.Identity.Client;

namespace BDIT.TenantToolkit.Graph.Auth;

internal enum ConnectInteractionReason { UnknownAccount, AccountNotCached, AmbiguousAccount, MicrosoftInteractionRequired }

/// <summary>
/// The existing explicit-connect policy, separated only so its prompt decisions can be measured offline.
/// This owns no cache or session. Renewal during an operation must never call it.
/// </summary>
internal static class ExplicitConnectAcquisition
{
    internal static async Task<TResult> AcquireAsync<TAccount, TResult>(bool knownContext, IReadOnlyList<TAccount> accounts,
        Func<TAccount, CancellationToken, Task<TResult>> silent,
        Func<ConnectInteractionReason, CancellationToken, Task<TResult>> interactive, CancellationToken ct,
        Func<CancellationToken, Task>? beforeInteractive = null, TimeSpan? acquisitionTimeout = null,
        TimeProvider? timeProvider = null)
    {
        ct.ThrowIfCancellationRequested();
        var remaining = acquisitionTimeout ?? TimeSpan.FromMinutes(5);
        if (remaining <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(acquisitionTimeout));
        var clock = timeProvider ?? TimeProvider.System;

        // Only MSAL acquisition consumes this shared budget. Permission review remains cancellable by the
        // engineer, but reading the requested access must not consume the time allowed for Microsoft sign-in.
        async Task<TResult> AcquireWithinBudget(Func<CancellationToken, Task<TResult>> acquire)
        {
            ct.ThrowIfCancellationRequested();
            if (remaining <= TimeSpan.Zero) throw TimedOut();
            using var deadline = new CancellationTokenSource(remaining, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var started = clock.GetTimestamp();
            try
            {
                var result = await acquire(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            { throw TimedOut(); }
            finally { remaining -= clock.GetElapsedTime(started); }
        }

        var reason = !knownContext ? ConnectInteractionReason.UnknownAccount
            : accounts.Count == 0 ? ConnectInteractionReason.AccountNotCached : ConnectInteractionReason.AmbiguousAccount;
        if (knownContext && accounts.Count == 1)
        {
            try { return await AcquireWithinBudget(token => silent(accounts[0], token)); }
            catch (MsalUiRequiredException) { reason = ConnectInteractionReason.MicrosoftInteractionRequired; }
        }
        ct.ThrowIfCancellationRequested();
        if (remaining <= TimeSpan.Zero) throw TimedOut();
        if (beforeInteractive is not null) await beforeInteractive(ct);
        ct.ThrowIfCancellationRequested();
        return await AcquireWithinBudget(token => interactive(reason, token));
    }

    private static AuthenticationRequiredException TimedOut() =>
        new("Microsoft sign-in did not complete within the allowed time. Try again.");

    internal static string Explain(ConnectInteractionReason reason) => reason switch
    {
        ConnectInteractionReason.UnknownAccount => "Choose an account for this explicit connection.",
        ConnectInteractionReason.AccountNotCached => "No cached account matches the confirmed sign-in address.",
        ConnectInteractionReason.AmbiguousAccount => "More than one cached account matches; choose the intended account.",
        _ => "Microsoft requires interaction for the requested tenant, application or permissions."
    };
}
