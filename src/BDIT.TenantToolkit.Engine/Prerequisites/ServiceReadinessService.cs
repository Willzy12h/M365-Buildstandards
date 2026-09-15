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
            var notes = new List<string>(); var good = profile.Parameters.EmergencyAccountIds.Count >= 2;
            foreach (var id in profile.Parameters.EmergencyAccountIds)
            {
                var user = await graph.GetAsync(GraphApi.V1, "/users/" + id + "?$select=id,displayName,userPrincipalName,accountEnabled,userType,onPremisesSyncEnabled", reads.Token);
                var cloud = user.ContainsKey("onPremisesSyncEnabled") && user["onPremisesSyncEnabled"]?.ToString() != "true"
                    && user["userType"]?.ToString() == "Member" && user["userPrincipalName"]?.ToString().EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase) == true;
                good &= cloud && user["accountEnabled"]?.ToString() == "true";
                notes.Add(user["displayName"] + " [" + id + "] · " + (cloud ? "cloud member" : "review identity origin"));
            }
            return new ServiceReadiness("ID-001", good ? "Configuration observed" : "Action required", string.Join("\n", notes),
                "Confirm two independent emergency-access identities, strong authentication, credential custody, monitoring and tested recovery. This read cannot prove any of those operational controls.");
        });
        await Check("ID-003", async () =>
        {
            var assignments = await graph.GetAllAsync(GraphApi.V1, "/roleManagement/directory/roleAssignments", reads.Token);
            var ids = assignments.Where(a => a["roleDefinitionId"]?.ToString() == "62e90394-69f5-4237-9190-012177145e10")
                .Select(a => a["principalId"]?.ToString()).Where(id => id is not null).Distinct().ToList();
            return new ServiceReadiness("ID-003", ids.Count is >= 2 and <= 4 ? "Configuration observed" : "Review required",
                "Direct Global Administrator principals: " + ids.Count + ". IDs: " + string.Join(", ", ids),
                "Review dedicated admin identities, ownership and daily-use separation. Group-derived access, PIM eligibility and mailbox/licence separation need independent review.");
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
            var bound = settings["bindStatus"]?.ToString() == "boundAndValidated";
            return new ServiceReadiness("ENR-006", bound ? "Configuration observed" : "Action required",
                "State: " + settings["bindStatus"] + " · owner: " + settings["ownerUserPrincipalName"] + " · last sync: " + settings["lastAppSyncDateTime"] + " / " + settings["lastAppSyncStatus"],
                "Complete Google's organisation ownership and consent flow in Intune. Confirm corporate custody and renewal ownership. No enrolment tokens are requested or recorded.");
        });
        return rows;
        async Task Check(string control, Func<Task<ServiceReadiness>> operation)
        {
            try { rows.Add(await operation()); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var status = ex is GraphRequestException { StatusCode: 404 } ? "Not configured" : "Unknown";
                rows.Add(new ServiceReadiness(control, status, "Read did not complete: " + ex.GetType().Name, "Check access and service setup. A failed read does not prove the control is missing."));
            }
        }
    }
}
