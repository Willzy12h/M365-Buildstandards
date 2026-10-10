using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
namespace BDIT.TenantToolkit.Engine.Prerequisites;

public sealed record ServiceReadiness(string ControlId, string Status, string Detail, string NextStep);

/// <summary>Observed configuration is separate from owner approval, credential escrow and functional recovery.</summary>
public sealed class ServiceReadinessService
{
    public async Task<IReadOnlyList<ServiceReadiness>> CheckAsync(IGraphClient graph, TenantProfile profile, CancellationToken ct)
    {
        if (graph.TenantId != profile.TenantId) throw new TenantMismatchException("Readiness profile belongs to another tenant.");
        var rows = new List<ServiceReadiness>();
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromMinutes(2));
        await Check("ID-001", async () =>
        {
            var notes = new List<string>();
            var ids = profile.Parameters.EmergencyAccountIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var good = ids.Count >= 2;
            var unknown = false;
            var actionable = ids.Count < 2;
            var failedReadSteps = new List<string>();
            foreach (var id in ids)
            {
                if (!Guid.TryParse(id, out var expectedId))
                {
                    unknown = true;
                    notes.Add("An emergency-account reference is not a valid object ID. Review the client profile.");
                    continue;
                }
                JsonObject user;
                try { user = await graph.GetAsync(GraphApi.V1, "/users/" + id + "?$select=id,displayName,userPrincipalName,accountEnabled,userType,onPremisesSyncEnabled", reads.Token); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    unknown = true;
                    var failure = ReadFailure("ID-001", ex);
                    notes.Add("Emergency account [" + id + "] · unable to check. " + failure.Detail);
                    failedReadSteps.Add(failure.NextStep);
                    if (reads.IsCancellationRequested) break;
                    continue;
                }
                var exactIdentity = Guid.TryParse(Text(user, "id"), out var returnedId) && returnedId == expectedId;
                var enabled = Boolean(user, "accountEnabled");
                var type = Text(user, "userType");
                var upn = Text(user, "userPrincipalName");
                // Graph's explicit null means never synchronised; absence or a malformed value proves nothing.
                var syncKnown = user.ContainsKey("onPremisesSyncEnabled") &&
                    (user["onPremisesSyncEnabled"] is null || Boolean(user, "onPremisesSyncEnabled") is not null);
                var identityKnown = exactIdentity && enabled is not null && type is "Member" or "Guest" &&
                    !string.IsNullOrWhiteSpace(upn) && upn.IndexOf('@') > 0 && syncKnown;
                var cloud = identityKnown && Boolean(user, "onPremisesSyncEnabled") != true && type == "Member" &&
                    upn!.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase);
                unknown |= !identityKnown;
                good &= cloud && enabled == true;
                actionable |= identityKnown && (!cloud || enabled != true);
                var name = Text(user, "displayName") ?? "Emergency account";
                notes.Add(name + " [" + id + "] · " + (!identityKnown ? "unable to check: identity or required fields missing/malformed" : (cloud ? "cloud member" : "review identity origin") + (enabled == false ? " · disabled" : " · enabled")));
            }
            if (ids.Count < 2) notes.Add("Fewer than two distinct emergency-account object IDs are recorded in the client profile.");
            return new ServiceReadiness("ID-001", actionable ? "Action required" : unknown ? "Unknown" : good ? "Configuration observed" : "Action required", string.Join("\n", notes),
                "Confirm two independent emergency-access identities, strong authentication, credential custody, monitoring and tested recovery. This read cannot prove any of those operational controls. Review missing identity fields and the exact object IDs before accepting configuration." + (failedReadSteps.Count > 0 ? " " + string.Join(" ", failedReadSteps.Distinct()) : ""));
        });
        await Check("ID-003", async () =>
        {
            var assignments = await graph.GetAllAsync(GraphApi.V1, "/roleManagement/directory/roleAssignments", reads.Token);
            var ids = new HashSet<Guid>();
            var unknown = false;
            foreach (var assignment in assignments)
            {
                if (!Guid.TryParse(Text(assignment, "roleDefinitionId"), out var role) ||
                    !Guid.TryParse(Text(assignment, "principalId"), out var principal))
                {
                    unknown = true;
                    continue;
                }
                if (role == Guid.Parse("62e90394-69f5-4237-9190-012177145e10")) ids.Add(principal);
            }
            return new ServiceReadiness("ID-003", unknown ? "Unknown" : ids.Count is >= 2 and <= 4 ? "Configuration observed" : "Review required",
                "Direct Global Administrator principals observed: " + ids.Count + ". IDs: " + string.Join(", ", ids) +
                (unknown ? ". Some role assignments could not be interpreted; this count is incomplete." : ""),
                "Review dedicated admin identities, ownership and daily-use separation. Group-derived access, PIM eligibility and mailbox/licence separation need independent review. Missing role/principal identifiers must be checked before accepting this count.");
        });
        await Check("ENR-005", async () =>
        {
            var certificate = await graph.GetAsync(GraphApi.V1, "/deviceManagement/applePushNotificationCertificate?$select=expirationDateTime,appleIdentifier,lastModifiedDateTime", reads.Token);
            var valid = DateTimeOffset.TryParse(certificate["expirationDateTime"]?.ToString(), out var expiry);
            return new ServiceReadiness("ENR-005", !valid ? "Unknown" : expiry <= DateTimeOffset.UtcNow.AddDays(30) ? "Action required" : "Configuration observed",
                "Apple account: " + certificate["appleIdentifier"] + " · expiry: " + certificate["expirationDateTime"],
                "An organisation-owned Apple account must create/renew the certificate in Apple's portal. Renew the existing certificate with the same Apple identity; replacing it can require device re-enrolment.");
        });
        await Check("ENR-006", async () =>
        {
            var settings = await graph.GetAsync(GraphApi.Beta, "/deviceManagement/androidManagedStoreAccountEnterpriseSettings?$select=bindStatus,ownerUserPrincipalName,ownerOrganizationName,lastAppSyncDateTime,lastAppSyncStatus", reads.Token);
            var state = Text(settings, "bindStatus");
            var known = state is "boundAndValidated" or "notBound" or "bound" or "unbinding";
            var bound = state == "boundAndValidated";
            return new ServiceReadiness("ENR-006", !known ? "Unknown" : state == "unbinding" ? "Review required" : bound ? "Configuration observed" : "Action required",
                "State: " + (state ?? "not returned or malformed") + " · owner: " + settings["ownerUserPrincipalName"] + " · last sync: " + settings["lastAppSyncDateTime"] + " / " + settings["lastAppSyncStatus"],
                state == "unbinding" ? "Unbinding is in progress. Review its state in Intune and wait for completion before deliberately checking again. Do not start another bind or consent flow while this is unresolved. No enrolment tokens are requested or recorded." :
                "If the state is unknown, check read access and the returned binding state first. Complete Google's organisation ownership and consent flow in Intune. Confirm corporate custody and renewal ownership. No enrolment tokens are requested or recorded.");
        });
        return rows;
        async Task Check(string control, Func<Task<ServiceReadiness>> operation)
        {
            try { rows.Add(await operation()); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                rows.Add(ReadFailure(control, ex));
            }
        }
    }
    private static ServiceReadiness ReadFailure(string control, Exception ex)
    {
        var detail = ex is GraphRequestException request ? "Read did not complete: HTTP " + request.StatusCode + "." :
            ex is OperationCanceledException ? "The readiness read timed out or was interrupted." : "Read did not complete: " + ex.GetType().Name + ".";
        var remedy = ex switch
        {
            GraphRequestException { StatusCode: 401 } => "Reconnect read-only from Connect, then retry readiness. If it is rejected again, review the existing assessment application's access.",
            GraphRequestException { StatusCode: 403 } => "On Connect, open Set up or validate applications and validate the existing assessment application. Check its documented read permissions, administrator consent and the signed-in engineer's service roles. Review any consent change separately.",
            GraphRequestException { StatusCode: 404 } => "Check the exact object reference, service availability and read permissions. A missing or unsupported endpoint can return 404; inspect the relevant service portal before proposing setup changes.",
            GraphRequestException { StatusCode: 429 } => "Microsoft limited this read. Wait before deliberately running readiness again; no configuration change is needed on the basis of this response.",
            _ => "Check the connection and relevant service availability, then deliberately retry readiness."
        };
        return new ServiceReadiness(control, "Unknown", detail, remedy + " A failed read does not prove the control is missing.");
    }

    private static string? Text(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? Boolean(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
}
