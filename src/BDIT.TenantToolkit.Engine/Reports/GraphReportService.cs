using System.Globalization;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Assessment;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Read-only registered reports over a caller's existing verified Graph context. Never authenticates.</summary>
public sealed class GraphReportService(IClock clock, TimeSpan? readBudget = null)
{
    public async Task<ReportEvidence> CaptureAsync(IGraphClient graph, TenantSession session, string reportId,
        ReportParameters parameters, CancellationToken ct)
    {
        var definition = GraphReportRegistry.Find(reportId);
        ReportEvidenceSchema.ValidateParameters(definition, parameters);
        var budget = readBudget ?? TimeSpan.FromMinutes(5);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes(5)) throw new ConfigurationException("Report read budget must be positive and no more than five minutes.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(budget);
        if (!session.TenantVerified || !ProfileValidator.IsGuid(session.AccountObjectId)
            || !ProfileValidator.IsGuid(session.TenantId) || graph.Mode != session.Mode || !string.Equals(graph.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Reports require the existing verified Graph tenant/account context.");
        if (parameters.End is not null && Timestamps.TryParse(parameters.End, out var requestedEnd) && requestedEnd > clock.UtcNow)
            throw new ConfigurationException("A live log report cannot end in the future.");
        var account = session.AccountObjectId; var tenant = session.TenantId; var mode = session.Mode;
        var report = new ReportEvidence
        {
            Id = Guid.NewGuid().ToString(), ReportId = definition.Id, TenantId = tenant, AccountObjectId = account,
            StartedAt = Timestamps.Format(clock.UtcNow), ToolkitVersion = ToolkitVersion.Current, ModuleVersion = GraphReportRegistry.AdapterVersion,
            Parameters = new() { Start = parameters.Start, End = parameters.End },
            Sources = definition.Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path, Reference = definition.Reference }).ToList(),
            Limitations = [definition.Limitations, "Sources identify registered routes, not proof of successful reads. These unsigned source claims and digests are not authentication or complete deployment before-evidence.",
                "Report reads have a cancellation budget of no more than five minutes and a per-section 5,000-row limit. Bounds do not establish available retention or completeness of omitted records."],
            Sections = definition.Sections.Select(s => new ReportSection { Id = s.Id, Error = "No report read attempted." }).ToList()
        };
        var missing = definition.Routes.Where(r => !GraphReportRegistry.HasAccess(session, r)).Select(r => r.Scope).Distinct().ToList();
        if (missing.Count > 0)
        {
            foreach (var section in report.Sections) section.Error = "The current authorised context lacks " + string.Join(", ", missing)
                + ". Review Application setup and consent impact before a deliberate reconnect. Reports never request consent or interactively retry.";
        }
        else if (reportId == "users-licences") await CollectUsers(graph, report, bounded.Token);
        else await CollectSingle(graph, report, bounded.Token);
        if (session.AccountObjectId != account || session.TenantId != tenant || session.Mode != mode
            || !session.TenantVerified || !string.Equals(graph.TenantId, tenant, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The verified report context changed during collection; no result was accepted.");
        report.EndedAt = Timestamps.Format(clock.UtcNow);
        report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(s => s.Status));
        ReportEvidenceSchema.Seal(report);
        return report;
    }

    private async Task CollectUsers(IGraphClient graph, ReportEvidence report, CancellationToken ct)
    {
        var subscriptions = report.Sections[0]; var users = report.Sections[1];
        LicenceInventory inventory;
        try { inventory = await new LicenceInventoryService(clock).CaptureAsync(graph, report.TenantId, true, ct); }
        catch (OperationCanceledException)
        {
            foreach (var section in report.Sections) { section.Status = ReportReadState.Cancelled; section.Error = "Licence inventory cancelled before a complete inventory was returned; no partial list is available from the existing collector."; }
            return;
        }
        subscriptions.Status = inventory.SubscriptionsComplete ? ReportReadState.Collected : ReportReadState.Failed;
        subscriptions.Error = inventory.SubscriptionsComplete ? null : Safe(inventory.SubscriptionError ?? "Subscription read failed.");
        if (inventory.SubscriptionsComplete)
        {
            var subscriptionItems = inventory.Subscriptions.Take(GraphReportRegistry.MaximumRows).ToList();
            var duplicateSubscriptions = Duplicates(subscriptionItems.Select(i => TextIdentity(i, "id")));
            var duplicateSkus = Duplicates(subscriptionItems.Select(i => GuidText(i, "skuId")));
            var seenSubscriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in subscriptionItems)
            {
                var id = TextIdentity(item, "id");
                if (id is not null && !seenSubscriptions.Add(id)) continue;
                var row = new SubscriptionReportRow
                {
                    Id = id, Name = Text(item, "skuPartNumber"), SkuId = GuidText(item, "skuId"),
                    CapabilityStatus = Text(item, "capabilityStatus"), ConsumedUnits = Number(item, "consumedUnits"),
                    EnabledUnits = Number(item["prepaidUnits"] as JsonObject, "enabled"), WarningUnits = Number(item["prepaidUnits"] as JsonObject, "warning"),
                    SuspendedUnits = Number(item["prepaidUnits"] as JsonObject, "suspended"), ReadStatus = ReportReadState.Collected
                };
                if (row.Id is null || row.SkuId is null || duplicateSubscriptions.Contains(row.Id) || duplicateSkus.Contains(row.SkuId))
                    MarkPartial(row, "Subscription identity fields are missing, malformed or duplicated; no SKU join is inferred.");
                subscriptions.Rows.Add(ReportEvidenceSchema.Row(row));
            }
            Finish(subscriptions, inventory.Subscriptions.Count > GraphReportRegistry.MaximumRows ? "Subscription row cap reached." : null);
        }
        if (!inventory.UsersComplete && inventory.Users.Count == 0)
        { users.Status = ReportReadState.Failed; users.Error = Safe(inventory.UserError ?? "No complete user inventory was returned."); return; }
        var duplicateIds = Duplicates(inventory.Users.Select(i => GuidText(i, "id")));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in inventory.Users.Take(GraphReportRegistry.MaximumRows))
            {
                ct.ThrowIfCancellationRequested();
                var id = GuidText(item, "id");
                if (id is not null && !seen.Add(id)) continue;
                var row = new UserLicenceReportRow
                {
                    Id = id, Name = Text(item, "displayName"), UserPrincipalName = Text(item, "userPrincipalName"),
                    UserType = Text(item, "userType"), AccountEnabled = Boolean(item, "accountEnabled"), ReadStatus = ReportReadState.Collected
                };
                if (id is null || duplicateIds.Contains(id)) MarkPartial(row, "Missing, malformed or duplicate user identity; no licence query or name-based join is attempted.");
                else
                {
                    try
                    {
                        var details = await graph.GetAllAsync(GraphApi.V1, "/users/" + id + "/licenseDetails", ct);
                        row.Products = details.Select(Product).ToList();
                        row.ProductsReadStatus = ProductsComplete(row.Products) ? ReportReadState.Collected : ReportReadState.Partial;
                        if (row.ProductsReadStatus != ReportReadState.Collected) MarkPartial(row, "Assigned product/service-plan fields are missing or duplicated; eligibility must remain unknown.");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { row.ProductsReadStatus = ReportReadState.Failed; MarkPartial(row, Safe(ex.Message)); }
                }
                users.Rows.Add(ReportEvidenceSchema.Row(row));
            }
            Finish(users, !inventory.UsersComplete ? Safe(inventory.UserError ?? "Original user inventory fields are incomplete.")
                : inventory.Users.Count > GraphReportRegistry.MaximumRows ? "User row cap reached; omitted users were not checked." : null);
        }
        catch (OperationCanceledException)
        { users.Status = ReportReadState.Cancelled; users.Error = $"Cancelled after {users.Rows.Count} of {inventory.Users.Count} user detail results; remaining users are not checked."; }
    }

    private async Task CollectSingle(IGraphClient graph, ReportEvidence report, CancellationToken ct)
    {
        var section = report.Sections[0];
        string path;
        try
        {
            path = report.ReportId switch
            {
                "intune-devices" => "/deviceManagement/managedDevices?$select=id,deviceName,userId,managedDeviceOwnerType,deviceEnrollmentType,managementAgent,operatingSystem,osVersion,complianceState,lastSyncDateTime",
                "mfa-registration" => "/reports/authenticationMethods/userRegistrationDetails?$select=id,userDisplayName,userPrincipalName,isMfaRegistered,isMfaCapable,isSsprRegistered,methodsRegistered,lastUpdatedDateTime",
                // Microsoft Learn lists no $select for either log list and only eq/ge/le on these timestamps.
                "sign-ins" => LogPath("/auditLogs/signIns", "createdDateTime", report.Parameters),
                "directory-audit" => LogPath("/auditLogs/directoryAudits", "activityDateTime", report.Parameters),
                _ => throw new ConfigurationException("No registered report adapter.")
            };
        }
        catch (Exception ex) { section.Status = ReportReadState.Failed; section.Error = Safe(ex.Message); return; }

        // Read one row past the cap to detect overflow, then stop requesting pages.
        var items = new List<JsonObject>();
        var truncated = false; var cancelled = false; Exception? failure = null;
        try
        {
            await foreach (var item in graph.GetBoundedAsync(GraphApi.V1, path, GraphReportRegistry.MaximumRows + 1, ct))
            {
                if (items.Count == GraphReportRegistry.MaximumRows) { truncated = true; break; }
                items.Add(item);
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex) { failure = ex; }

        try { Project(report, section, items); }
        catch (Exception ex) { section.Rows.Clear(); section.Status = ReportReadState.Failed; section.Error = Safe(ex.Message); return; }
        if (cancelled)
        {
            section.Status = ReportReadState.Cancelled;
            section.Error = section.Rows.Count > 0
                ? $"Report cancelled after {section.Rows.Count} returned rows; retained rows are partial and remaining pages/rows were not checked."
                : "Report cancelled before any rows were returned; no rows are retained and nothing was checked.";
        }
        else if (failure is not null)
        {
            section.Status = section.Rows.Count > 0 ? ReportReadState.Partial : ReportReadState.Failed;
            section.Error = section.Rows.Count > 0
                ? Safe($"Read stopped after {section.Rows.Count} returned rows; retained rows are partial and remaining pages were not checked. " + failure.Message)
                : Safe(failure.Message);
        }
        else Finish(section, truncated ? $"Report row cap of {GraphReportRegistry.MaximumRows} reached; reading stopped and omitted records were not read or checked." : null);
    }

    private static void Project(ReportEvidence report, ReportSection section, List<JsonObject> items)
    {
        var guidIdentity = report.ReportId is "intune-devices" or "mfa-registration";
        string? Identity(JsonObject item) => guidIdentity ? GuidText(item, "id") : TextIdentity(item, "id");
        var duplicates = Duplicates(items.Select(Identity));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = Identity(item);
            if (id is not null && !seen.Add(id)) continue;
            GraphReportRow row = report.ReportId switch
            {
                "intune-devices" => new DeviceReportRow { Id = id, Name = Text(item, "deviceName"), UserId = GuidText(item, "userId"), Ownership = Text(item, "managedDeviceOwnerType"), EnrolmentType = Text(item, "deviceEnrollmentType"), ManagementAgent = Text(item, "managementAgent"), OperatingSystem = Text(item, "operatingSystem"), OsVersion = Text(item, "osVersion"), ComplianceState = Text(item, "complianceState"), LastSyncAt = Date(item, "lastSyncDateTime") },
                "mfa-registration" => new RegistrationReportRow { Id = id, Name = Text(item, "userDisplayName"), UserPrincipalName = Text(item, "userPrincipalName"), IsMfaRegistered = Boolean(item, "isMfaRegistered"), IsMfaCapable = Boolean(item, "isMfaCapable"), IsSsprRegistered = Boolean(item, "isSsprRegistered"), MethodsRegistered = Strings(item["methodsRegistered"]), LastUpdatedAt = Date(item, "lastUpdatedDateTime") },
                "sign-ins" => new SignInReportRow { Id = id, Name = Text(item, "userDisplayName"), UserId = GuidText(item, "userId"), UserPrincipalName = Text(item, "userPrincipalName"), CreatedAt = Date(item, "createdDateTime"), ApplicationId = GuidText(item, "appId"), ApplicationName = Text(item, "appDisplayName"), ClientApplication = Text(item, "clientAppUsed"), IpAddress = Text(item, "ipAddress"), ErrorCode = Number(item["status"] as JsonObject, "errorCode"), FailureReason = Text(item["status"] as JsonObject, "failureReason") },
                _ => new DirectoryAuditReportRow { Id = id, Name = Text(item, "activityDisplayName"), ActivityAt = Date(item, "activityDateTime"), Category = Text(item, "category"), Result = Text(item, "result"), ResultReason = Text(item, "resultReason"), ActorUserId = GuidText(item["initiatedBy"]?["user"] as JsonObject, "id"), ActorApplicationId = GuidText(item["initiatedBy"]?["app"] as JsonObject, "appId"), TargetIds = Targets(item["targetResources"]) }
            };
            row.ReadStatus = ReportReadState.Collected;
            if (row.Id is null || duplicates.Contains(row.Id)) MarkPartial(row, "Missing, malformed or duplicate returned identity; no name-based join is made.");
            if (row is DeviceReportRow device && (device.Ownership is null || device.OperatingSystem is null || device.ComplianceState is null || device.LastSyncAt is null)) MarkPartial(row, "Some ownership, OS, compliance or last-sync fields are missing; null values remain unknown.");
            if (row is RegistrationReportRow r && (r.MethodsRegistered is null || r.IsMfaRegistered is null || r.IsMfaCapable is null || r.IsSsprRegistered is null)) MarkPartial(row, "Registration fields are missing; registration, enforcement and use cannot be inferred.");
            if (row is SignInReportRow s && (s.CreatedAt is null || s.ErrorCode is null)) MarkPartial(row, "Missing sign-in time/status; no success or complete coverage is inferred.");
            if (row is DirectoryAuditReportRow a && (a.ActivityAt is null || a.TargetIds is null || a.Result is null)) MarkPartial(row, "Some directory-audit fields are missing.");
            var eventAt = row is SignInReportRow signIn ? signIn.CreatedAt : row is DirectoryAuditReportRow audit ? audit.ActivityAt : null;
            if (eventAt is not null && Timestamps.TryParse(eventAt, out var at) && Timestamps.TryParse(report.Parameters.Start, out var start)
                && Timestamps.TryParse(report.Parameters.End, out var end) && (at < start || at >= end)) MarkPartial(row, "The returned event is outside the requested interval; inspect this partial result.");
            section.Rows.Add(row switch
            { DeviceReportRow deviceRow => ReportEvidenceSchema.Row(deviceRow), RegistrationReportRow registrationRow => ReportEvidenceSchema.Row(registrationRow), SignInReportRow signInRow => ReportEvidenceSchema.Row(signInRow), DirectoryAuditReportRow auditRow => ReportEvidenceSchema.Row(auditRow), _ => throw new ConfigurationException("Unknown row schema.") });
        }
    }

    private static void Finish(ReportSection section, string? limitation)
    {
        var partial = section.Rows.Any(r => Text(r, "readStatus") != ReportReadState.Collected) || limitation is not null;
        section.Status = partial ? ReportReadState.Partial : ReportReadState.Collected;
        section.Error = partial ? limitation ?? "Some returned rows are incomplete; inspect their explicit read states." : null;
    }
    private static AssignedProductReport Product(JsonObject item) => new()
    {
        SkuId = GuidText(item, "skuId"), SkuPartNumber = Text(item, "skuPartNumber"),
        ServicePlans = item["servicePlans"] is JsonArray plans && plans.All(p => p is JsonObject) ? plans.OfType<JsonObject>().Select(p => new AssignedPlanReport
        { ServicePlanId = GuidText(p, "servicePlanId"), ServicePlanName = Text(p, "servicePlanName"), ProvisioningStatus = Text(p, "provisioningStatus"), AppliesTo = Text(p, "appliesTo") }).ToList()
            : [new AssignedPlanReport()]
    };
    private static bool ProductsComplete(List<AssignedProductReport> products) => products.All(p => p.SkuId is not null && ReportValues.HasText(p.SkuPartNumber)
        && p.ServicePlans.All(s => s.ServicePlanId is not null && ReportValues.HasText(s.ServicePlanName) && ReportValues.HasText(s.ProvisioningStatus))
        && p.ServicePlans.Select(s => s.ServicePlanId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == p.ServicePlans.Count)
        && products.Select(p => p.SkuId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == products.Count;
    /// <summary>Inclusive range using the documented ge/le operators: the end is one tick before the exclusive requested end.</summary>
    private static string LogPath(string route, string timestamp, ReportParameters p)
    {
        if (!Timestamps.TryParse(p.End, out var end)) throw new ConfigurationException("Log reports require a UTC end.");
        var inclusiveEnd = end.ToUniversalTime().AddTicks(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        return route + "?$filter=" + Uri.EscapeDataString(timestamp + " ge " + p.Start + " and " + timestamp + " le " + inclusiveEnd);
    }
    private static HashSet<string> Duplicates(IEnumerable<string?> ids) => ids.OfType<string>().GroupBy(i => i, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static void MarkPartial(GraphReportRow row, string error) { row.ReadStatus = ReportReadState.Partial; row.Error = error; }
    private static string Safe(string error) { var text = SensitiveDataScrubber.Scrub(error); return text.Length > 4096 ? text[..4096] : text; }
    private static string? Text(JsonObject? item, string key) => item?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string? GuidText(JsonObject? item, string key) => ReportValues.TryGuid(Text(item, key), out var id) ? id : null;
    private static string? TextIdentity(JsonObject? item, string key) { var text = Text(item, key); return ReportValues.IsTextIdentity(text) ? text : null; }
    private static long? Number(JsonObject? item, string key) => item?[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
    private static bool? Boolean(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
    private static string? Date(JsonObject item, string key) { var text = Text(item, key); return Timestamps.TryParse(text, out _) ? text : null; }
    private static List<string>? Strings(JsonNode? value) => value is JsonArray values && values.All(v => v is JsonValue j && j.TryGetValue<string>(out _))
        ? values.Select(v => v!.GetValue<string>()).ToList() : null;
    private static List<string>? Targets(JsonNode? value) => value is JsonArray values && values.All(v => v is JsonObject o && Text(o, "id") is not null)
        ? values.Select(v => Text(v!.AsObject(), "id")!).ToList() : null;
}
