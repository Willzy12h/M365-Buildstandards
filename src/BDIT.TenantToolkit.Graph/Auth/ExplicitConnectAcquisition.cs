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
        Func<TAccount, Task<TResult>> silent, Func<ConnectInteractionReason, Task<TResult>> interactive, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reason = !knownContext ? ConnectInteractionReason.UnknownAccount
            : accounts.Count == 0 ? ConnectInteractionReason.AccountNotCached : ConnectInteractionReason.AmbiguousAccount;
        if (knownContext && accounts.Count == 1)
        {
            try { return await silent(accounts[0]); }
            catch (MsalUiRequiredException) { reason = ConnectInteractionReason.MicrosoftInteractionRequired; }
        }
        ct.ThrowIfCancellationRequested();
        return await interactive(reason);
    }

    internal static string Explain(ConnectInteractionReason reason) => reason switch
    {
        ConnectInteractionReason.UnknownAccount => "Choose an account for this explicit connection.",
        ConnectInteractionReason.AccountNotCached => "No cached account matches the confirmed sign-in address.",
        ConnectInteractionReason.AmbiguousAccount => "More than one cached account matches; choose the intended account.",
        _ => "Microsoft requires interaction for the requested tenant, application or permissions."
    };
}
