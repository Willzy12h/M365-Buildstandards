using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Planning;

public sealed class PlanRequest
{
    public required TenantProfile Profile { get; init; }
    public required StandardCatalogue Standard { get; init; }
    public required TenantSnapshot Snapshot { get; init; }
    public required ManagedObjectMappings Mappings { get; init; }
    public required IReadOnlyList<Deviation> Deviations { get; init; }
    public required IReadOnlyList<string> SelectedControlIds { get; init; }
    public required TenantSession Session { get; init; }
}

public sealed class PlanValidationContext
{
    public required TenantProfile Profile { get; init; }
    public required StandardCatalogue Standard { get; init; }
    public required TenantSnapshot Snapshot { get; init; }
    public required ManagedObjectMappings Mappings { get; init; }
    public required TenantSession Session { get; init; }
    public IReadOnlyList<Deviation> Deviations { get; init; } = Array.Empty<Deviation>();
    public string? AcknowledgedSnapshotId { get; init; }
    public required DateTimeOffset Now { get; init; }
    public TimeSpan MaxSnapshotAge { get; init; } = TimeSpan.FromMinutes(20);
    public TimeSpan MaxPlanAge { get; init; } = TimeSpan.FromMinutes(20);
}

/// <summary>
/// Turns selected controls into an integrity-bound plan of safe candidate writes. The planner never adopts existing
/// objects by name, never proposes activation or assignment, and forces Conditional Access candidates to the disabled
/// state with the emergency accounts and the verified operator excluded.
/// </summary>
public sealed class DeploymentPlanner
{
    private readonly IClock _clock;
    private readonly string _toolkitVersion;

    public DeploymentPlanner(IClock clock, string toolkitVersion)
    {
        _clock = clock;
        _toolkitVersion = toolkitVersion;
    }

    public static string StandardDigest(StandardCatalogue standard) =>
        string.IsNullOrEmpty(standard.IntegrityDigest) ? CanonicalJson.Sha256Value(standard) : standard.IntegrityDigest;

    public static string SnapshotDigest(TenantSnapshot snapshot) => EvidenceIntegrity.Compute(snapshot);

    public static string ComputeDigest(DeploymentPlan plan)
    {
        var node = ToolkitJson.ToNode(plan) as JsonObject ?? throw new ConfigurationException("Plan must serialise to an object.");
        node["planDigest"] = "";
        return CanonicalJson.Sha256(node);
    }

    public DeploymentPlan Build(PlanRequest request)
    {
        var profile = request.Profile;
        var standard = request.Standard;
        var snapshot = request.Snapshot;
        var session = request.Session;
        if (!string.Equals(snapshot.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Snapshot tenant does not match the selected client.");
        if (!string.Equals(session.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The connected tenant does not match the selected client.");
        if (!string.Equals(request.Mappings.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Managed-object mapping belongs to a different tenant.");

        var names = NameResolver.FromSnapshot(snapshot);
        var parameters = profile.Parameters.ToTemplateValues(profile.TenantId);
        var rows = new List<PlanRow>();
        foreach (var id in request.SelectedControlIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var control = standard.FindControl(id);
            if (control is null) continue;
            rows.Add(BuildRow(control, standard, snapshot, profile, request.Mappings, request.Deviations, session, names, parameters, _clock.UtcNow));
        }

        var plan = new DeploymentPlan
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = profile.TenantId,
            TenantName = snapshot.TenantName,
            ProfileId = profile.Id,
            ProfileDigest = CanonicalJson.Sha256Value(profile),
            Release = standard.Release,
            StandardDigest = StandardDigest(standard),
            StandardContentDigest = CanonicalJson.Sha256Value(standard),
            SnapshotId = snapshot.Id,
            SnapshotDigest = SnapshotDigest(snapshot),
            MappingsDigest = CanonicalJson.Sha256Value(request.Mappings),
            DeviationsDigest = CanonicalJson.Sha256Value(request.Deviations),
            OperatorObjectId = session.OperatorObjectId ?? "",
            OperatorAccount = session.Account,
            ClientId = session.ClientId,
            CreatedAt = Timestamps.Format(_clock.UtcNow),
            ToolkitVersion = _toolkitVersion,
            Rows = rows
        };
        plan.PlanDigest = ComputeDigest(plan);
        return plan;
    }

    private static PlanRow BuildRow(ControlDefinition control, StandardCatalogue standard, TenantSnapshot snapshot, TenantProfile profile,
        ManagedObjectMappings mappings, IReadOnlyList<Deviation> deviations, TenantSession session, NameResolver names, IReadOnlyDictionary<string, JsonNode?> parameters,
        DateTimeOffset now)
    {
        var row = new PlanRow
        {
            ControlId = control.Id,
            Name = control.Name,
            Collection = control.Collection ?? "",
            Action = PlanAction.Blocked,
            ExpectedProductionState = control.ExpectedProduction.State,
            ExpectedProductionAssignment = control.ExpectedProduction.Assignment,
            SafeState = control.SafeDeployment.State
        };
        foreach (var dep in control.Dependencies) row.Warnings.Add($"Activation prerequisite {dep}: confirm it is in place before enabling or assigning this candidate. Creating a disabled/unassigned candidate does not fulfil it.");

        var deviation = deviations.FirstOrDefault(d => string.Equals(d.ControlId, control.Id, StringComparison.OrdinalIgnoreCase));
        if (deviation is not null)
        {
            row.Action = PlanAction.Deviation;
            row.Reason = (deviation.Kind == DeviationKind.NotApplicable ? "Recorded as not applicable: " : "Approved deviation: ") + deviation.Reason;
            return row;
        }

        var def = standard.FindCollection(control.Collection);
        if (def is null || !control.HasRecipe)
        {
            row.Action = PlanAction.Manual;
            row.Reason = string.IsNullOrWhiteSpace(control.Assessment.ManualInstructions) ? "No reviewed deployment recipe; follow the build standard manually." : control.Assessment.ManualInstructions;
            return row;
        }
        if (!def.Writable)
        {
            row.Action = PlanAction.Manual;
            row.Reason = $"The {def.Label} collection is assessed but not written by this release of the toolkit.";
            return row;
        }
        var incomplete = SnapshotRequirements.IncompleteReason(snapshot, standard);
        if (incomplete is not null) { row.Reason = incomplete; return row; }
        var licenceReason = LicenceReadiness(control, snapshot);
        if (licenceReason is not null) { row.Reason = licenceReason; return row; }
        if (!snapshot.Collections.TryGetValue(control.Collection!, out var capture) || !capture.Usable)
        {
            row.Reason = $"The {def.Label} collection is unavailable or incomplete in the snapshot; absence cannot be inferred, so nothing will be created.";
            return row;
        }

        JsonObject payload;
        try
        {
            // A reviewable setting the client has not supplied yet falls back to the standard's default and warns,
            // rather than blocking the whole control. Identity inputs declare no default, so they still block here.
            var defaults = PolicyInputDefaults.Apply(control.Payload!, standard, parameters, now);
            foreach (var warning in defaults.Warnings) row.Warnings.Add(warning);
            row.UsesDefaultInputs = defaults.Warnings.Count > 0;
            PolicyInputValidator.ValidateUsed(control.Payload!, standard, defaults.Values);
            payload = (JsonObject)CanonicalJson.Resolve(control.Payload, defaults.Values)!;
        }
        catch (MissingParameterException ex)
        {
            row.Reason = ex.Message;
            return row;
        }
        payload.Remove("id");
        payload.Remove("assignments");

        var isConditionalAccess = ConditionalAccessSafety.IsConditionalAccess(def);
        var mapping = mappings.Find(control.Id);
        if (isConditionalAccess)
        {
            ConditionalAccessSafety.EnforceSafeState(payload);
            if (profile.Parameters.EmergencyAccountIds.Count == 0)
            {
                row.Reason = "Emergency access account object IDs are required in the client profile before any Conditional Access candidate is created.";
                return row;
            }
            ConditionalAccessSafety.InjectUserExclusions(payload, profile.Parameters.EmergencyAccountIds);
            ConditionalAccessSafety.InjectUserExclusions(payload, profile.Parameters.AdditionalExclusionAccountIds);
            if (profile.Parameters.CaExclusionGroupId.Length > 0) InjectGroupExclusion(payload, profile.Parameters.CaExclusionGroupId);

            // Once the standard's exclusion group exists, every candidate excludes it, so the exempt population is one
            // group an engineer can open rather than a list repeated in each policy.
            var exclusions = ExclusionGroupCoverage.ForUsers(standard, snapshot, mappings, session, profile.Parameters.EmergencyAccountIds, names);
            if (exclusions.GroupId is not null) InjectGroupExclusion(payload, exclusions.GroupId);
            foreach (var warning in exclusions.Warnings) row.Warnings.Add(warning);
            if (mapping?.OperatorExclusion is not null && ProfileValidator.IsGuid(mapping.OperatorExclusion.ObjectId))
                ConditionalAccessSafety.InjectUserExclusions(payload, new[] { mapping.OperatorExclusion.ObjectId });

            var operatorId = session.OperatorObjectId ?? "";
            if (!session.OperatorVerified || !ProfileValidator.IsGuid(operatorId) || !string.Equals(session.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                row.Reason = "The signed-in operator's object ID could not be verified in this tenant. Conditional Access candidates are not created without a verified operator exclusion. Reconnect and check permissions.";
                return row;
            }
            ConditionalAccessSafety.InjectUserExclusions(payload, new[] { operatorId });
            row.OperatorExclusion = new OperatorExclusion
            {
                ObjectId = operatorId,
                DisplayName = session.OperatorDisplayName,
                UserPrincipalName = session.OperatorUpn,
                TenantId = profile.TenantId
            };
            row.Warnings.Add($"The signed-in operator {session.OperatorUpn} [{operatorId}] is excluded from this policy to prevent lockout. The exclusion does not expire; remove it deliberately after testing.");
        }

        try { WritePayloadGuard.Assert(def, payload); }
        catch (SafetyViolationException ex) { row.Reason = ex.Message; return row; }
        var deviceProblem = DeviceCandidateReadiness.Problem(payload, standard, snapshot);
        if (deviceProblem is not null) { row.Reason = deviceProblem; return row; }
        var referenceReason = CreationReferenceProblem(def, payload, standard, snapshot);
        if (referenceReason is not null) { row.Reason = referenceReason; return row; }

        var items = capture.Items;
        var nameKey = def.NameProperty;
        var proposedName = payload[nameKey]?.GetValue<string>() ?? "";
        var owned = mapping is null ? null : items.FirstOrDefault(i => string.Equals(i["id"]?.GetValue<string>(), mapping.ObjectId, StringComparison.OrdinalIgnoreCase));
        var sameName = items.Where(i => string.Equals(i[nameKey]?.GetValue<string>(), proposedName, StringComparison.OrdinalIgnoreCase)
                                        && (owned is null || !ReferenceEquals(i, owned))).ToList();
        row.Payload = payload;

        if (mapping is null && sameName.Count > 0)
        {
            row.Action = PlanAction.Conflict;
            row.Reason = $"An unmanaged object already uses the name '{proposedName}'. Review it; the toolkit never adopts objects by name and never creates a duplicate.";
            return row;
        }
        if (mapping is not null && owned is null)
        {
            row.Action = PlanAction.Conflict;
            row.Reason = $"The object the toolkit created earlier ({mapping.ObjectId}) is missing from the tenant. Investigate before replacing it.";
            return row;
        }
        if (mapping is null)
        {
            var overlapping = AssessmentEngine.FindCandidates(items, payload, control.Assessment, def, names, mappings)
                .Where(c => c.SettingsMatch || c.Score >= control.Assessment.PartialMatchThreshold).ToList();
            if (overlapping.Count > 0)
            {
                row.Action = PlanAction.Conflict;
                row.Reason = "A differently named object has overlapping settings (" + string.Join(", ", overlapping.Select(c => c.Name)) + "). Review the comparison; automatic adoption and duplicate creation are blocked.";
                return row;
            }
            row.Action = PlanAction.Create;
            row.Reason = isConditionalAccess
                ? "Create a disabled Conditional Access candidate with the expected targeting, emergency accounts and the signed-in operator excluded. Enabling remains a separate reviewed step."
                : "Create an unassigned candidate. Assignment remains a separate reviewed step.";
            return row;
        }

        var current = owned!;
        row.Before = current;
        if (mapping.LastApplied is null || !string.Equals(mapping.Collection, row.Collection, StringComparison.Ordinal))
        {
            row.Action = PlanAction.Manual;
            row.Reason = "The ownership record lacks the last applied settings or identifies a different collection. Reconcile it before updating.";
            return row;
        }
        if (isConditionalAccess && !string.Equals(current["state"]?.GetValue<string>(), ConditionalAccessSafety.SafeState, StringComparison.OrdinalIgnoreCase))
        {
            row.Action = PlanAction.Manual;
            row.Reason = "The toolkit-created policy is no longer disabled. Active Conditional Access policies are changed only through an explicitly scoped change procedure.";
            return row;
        }
        if (current[TenantCollector.AssignmentsUnknownKey] is not null || current[TenantCollector.SettingsUnknownKey] is not null || current[TenantCollector.RelationshipUnknownKey] is not null
            || (current[TenantCollector.AssignmentsKey] is JsonArray assignments && assignments.Count > 0))
        {
            row.Action = PlanAction.Manual;
            row.Reason = "The toolkit-created object is assigned, or its assignments could not be read. Only inactive, unassigned objects are updated automatically.";
            return row;
        }
        if (mapping.LastApplied is not null && !CanonicalJson.IsSubset(current, mapping.LastApplied))
        {
            row.Action = PlanAction.Drift;
            row.Reason = "The current settings differ from what the toolkit last applied. Something else changed this object; review before the toolkit touches it again.";
            return row;
        }
        if ((!string.IsNullOrEmpty(def.Relationship) || !string.IsNullOrEmpty(def.Children)) && !CanonicalJson.IsSubset(current, payload))
        {
            row.Action = PlanAction.Manual;
            row.Reason = "Updating this object's related settings (for example compliance actions) requires a separate procedure; review manually.";
            return row;
        }
        if (CanonicalJson.IsSubset(current, payload))
        {
            row.Action = PlanAction.NoChange;
            row.Reason = "The toolkit-created object already matches the recipe.";
            row.ObjectId = current["id"]?.GetValue<string>();
            return row;
        }
        row.Action = PlanAction.Update;
        row.ObjectId = current["id"]?.GetValue<string>();
        row.Reason = "Update the inactive, toolkit-created object to the current recipe. It stays " + (isConditionalAccess ? "disabled." : "unassigned.");
        return row;
    }

    private static void InjectGroupExclusion(JsonObject payload, string groupId)
    {
        if (payload["conditions"] is not JsonObject conditions || conditions["users"] is not JsonObject users) return;
        if (users["excludeGroups"] is not JsonArray groups)
        {
            groups = new JsonArray();
            users["excludeGroups"] = groups;
        }
        var present = groups.Any(g => g is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s, groupId, StringComparison.OrdinalIgnoreCase));
        if (!present) groups.Add(groupId);
    }

    /// <summary>Throws <see cref="PlanValidationException"/> (or a tenant mismatch) unless the plan can still be executed safely.</summary>
    public static void Validate(DeploymentPlan plan, PlanValidationContext ctx)
    {
        if (!string.Equals(plan.TenantId, ctx.Profile.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.TenantId, ctx.Snapshot.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.TenantId, ctx.Session.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.TenantId, ctx.Mappings.TenantId, StringComparison.OrdinalIgnoreCase)
            || ctx.Deviations.Any(d => !string.Equals(plan.TenantId, d.TenantId, StringComparison.OrdinalIgnoreCase)))
            throw new TenantMismatchException($"Plan is bound to tenant {plan.TenantId}; the connected tenant, profile or snapshot differ. Execution blocked.");
        if (ctx.Session.Mode != SessionMode.Deployment)
            throw new PlanValidationException("Connect with deployment access before executing a plan.");
        if (!ctx.Session.TenantVerified || !ctx.Session.OperatorVerified || !ProfileValidator.IsGuid(ctx.Session.OperatorObjectId ?? ""))
            throw new PlanValidationException("Deployment requires a verified tenant and current operator identity. Reconnect before planning.");
        if (!string.Equals(plan.OperatorObjectId, ctx.Session.OperatorObjectId ?? "", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.OperatorAccount, ctx.Session.Account, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.ClientId, ctx.Session.ClientId, StringComparison.OrdinalIgnoreCase))
            throw new PlanValidationException("The authentication context changed since the plan was built (different account or application). Rebuild the plan.");
        if (plan.ProfileId != ctx.Profile.Id || plan.ProfileDigest != CanonicalJson.Sha256Value(ctx.Profile))
            throw new PlanValidationException("The client profile changed after the plan was built. Rebuild the plan.");
        if (plan.Release != ctx.Standard.Release || plan.StandardDigest != StandardDigest(ctx.Standard)
            || plan.StandardContentDigest != CanonicalJson.Sha256Value(ctx.Standard))
            throw new PlanValidationException("The Build Standard changed after the plan was built. Rebuild the plan.");
        if (plan.SnapshotId != ctx.Snapshot.Id || plan.SnapshotDigest != SnapshotDigest(ctx.Snapshot))
            throw new PlanValidationException("The snapshot changed after the plan was built. Capture and review a new plan.");
        if (plan.MappingsDigest != CanonicalJson.Sha256Value(ctx.Mappings))
            throw new PlanValidationException("The managed-object mapping changed after the plan was built. Rebuild the plan.");
        if (plan.DeviationsDigest != CanonicalJson.Sha256Value(ctx.Deviations))
            throw new PlanValidationException("The deviation register changed after the plan was built. Rebuild the plan.");
        if (ComputeDigest(plan) != plan.PlanDigest)
            throw new PlanValidationException("Plan integrity check failed: the plan content differs from its digest. Rebuild the plan.");
        if (ctx.MaxSnapshotAge <= TimeSpan.Zero || ctx.MaxPlanAge <= TimeSpan.Zero)
            throw new PlanValidationException("Plan and snapshot age limits must be positive.");
        if (!Timestamps.TryParse(ctx.Snapshot.CapturedAt, out var captured) || captured > ctx.Now || ctx.Now - captured > ctx.MaxSnapshotAge)
            throw new PlanValidationException($"The snapshot is older than {ctx.MaxSnapshotAge.TotalMinutes:0} minutes. Capture the tenant again and rebuild the plan.");
        if (!Timestamps.TryParse(plan.CreatedAt, out var created) || created > ctx.Now || created < captured || ctx.Now - created > ctx.MaxPlanAge)
            throw new PlanValidationException($"The plan is older than {ctx.MaxPlanAge.TotalMinutes:0} minutes. Rebuild it.");
        if (!string.Equals(ctx.AcknowledgedSnapshotId, ctx.Snapshot.Id, StringComparison.OrdinalIgnoreCase))
            throw new PlanValidationException("Review and acknowledge the before-change snapshot before deploying.");
        SnapshotRequirements.AssertComplete(ctx.Snapshot, ctx.Standard);
        if (plan.Rows.Select(r => r.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Rows.Count)
            throw new PlanValidationException("The plan contains duplicate controls. Rebuild it before deployment.");
        var writes = plan.WriteRows.ToList();
        if (writes.Count == 0) throw new PlanValidationException("The plan contains no supported changes.");
        foreach (var row in writes)
        {
            if (row.Payload is null) throw new PlanValidationException($"{row.ControlId}: write row has no payload.");
            var def = ctx.Standard.FindCollection(row.Collection) ?? throw new PlanValidationException($"{row.ControlId}: unknown collection '{row.Collection}'.");
            var control = ctx.Standard.FindControl(row.ControlId) ?? throw new PlanValidationException($"Unknown control {row.ControlId}.");
            var expected = BuildRow(control, ctx.Standard, ctx.Snapshot, ctx.Profile, ctx.Mappings, ctx.Deviations, ctx.Session,
                NameResolver.FromSnapshot(ctx.Snapshot), ctx.Profile.Parameters.ToTemplateValues(ctx.Profile.TenantId), ctx.Now);
            if (!expected.IsWrite || expected.Action != row.Action || expected.Collection != row.Collection || expected.ObjectId != row.ObjectId
                || CanonicalJson.Serialize(expected.Payload) != CanonicalJson.Serialize(row.Payload))
                throw new PlanValidationException($"{row.ControlId}: the write is not ready or differs from the current reviewed recipe. {expected.Reason}");
            WritePayloadGuard.Assert(def, row.Payload);
            if (ConditionalAccessSafety.IsConditionalAccess(def))
            {
                if (row.OperatorExclusion is null || !ProfileValidator.IsGuid(row.OperatorExclusion.ObjectId))
                    throw new PlanValidationException($"{row.ControlId}: Conditional Access row has no verified operator exclusion.");
                var count = ConditionalAccessSafety.ExcludedUsers(row.Payload).Count(u => string.Equals(u, row.OperatorExclusion.ObjectId, StringComparison.OrdinalIgnoreCase));
                if (count != 1) throw new PlanValidationException($"{row.ControlId}: the operator exclusion must appear exactly once (found {count}).");
                if (!string.Equals(row.OperatorExclusion.ObjectId, ctx.Session.OperatorObjectId, StringComparison.OrdinalIgnoreCase))
                    throw new PlanValidationException($"{row.ControlId}: the plan excludes a different operator than the one signed in. Rebuild the plan.");
            }
            if (row.Action == PlanAction.Update && !ProfileValidator.IsGuid(row.ObjectId ?? ""))
                throw new PlanValidationException($"{row.ControlId}: update row has no valid object ID.");
        }
    }

    private static string? LicenceReadiness(ControlDefinition control, TenantSnapshot snapshot)
    {
        if (control.Licence.ServicePlans.Count == 0) return null;
        var licences = LicenceEvaluator.FromSnapshot(snapshot);
        if (!licences.Available) return "Required licences could not be verified. Capture subscribed licences before preparing this change.";
        var missing = control.Licence.ServicePlans.Where(p => !licences.Has(p)).ToList();
        return missing.Count == 0 ? null : "Required service plan(s) not available: " + string.Join(", ", missing) + ". Check tenant licensing before preparing this change.";
    }

    /// <summary>Resolve concrete creation references. Catalogue Dependencies remain activation/manual checks.</summary>
    private static string? CreationReferenceProblem(CollectionDefinition def, JsonObject payload, StandardCatalogue standard, TenantSnapshot snapshot)
    {
        if (!ConditionalAccessSafety.IsConditionalAccess(def)) return null;
        var references = new (string Path, string Route)[]
        {
            (Path: "conditions.users.includeUsers", Route: "/users"), ("conditions.users.excludeUsers", "/users"),
            ("conditions.users.includeGroups", "/groups"), ("conditions.users.excludeGroups", "/groups"),
            ("conditions.locations.includeLocations", "/identity/conditionalAccess/namedLocations"),
            ("conditions.locations.excludeLocations", "/identity/conditionalAccess/namedLocations")
        };
        foreach (var (path, route) in references)
        {
            JsonNode? node = payload;
            foreach (var part in path.Split('.')) node = (node as JsonObject)?[part];
            if (node is not JsonArray values) continue;
            foreach (var value in values)
            {
                if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out var id))
                    return $"Creation prerequisite {path}: every reference must be an object ID or a supported targeting value.";
                if ((route == "/users" && id is "All" or "None" or "GuestsOrExternalUsers")
                    || (route.EndsWith("/namedLocations", StringComparison.Ordinal) && id is "All" or "AllTrusted")) continue;
                if (!ProfileValidator.IsGuid(id)) return $"Creation prerequisite {path}: '{id}' is not a valid object ID or supported targeting value.";
                var key = standard.Collections.FirstOrDefault(c => c.Value.BasePath == route).Key;
                if (key is null || !snapshot.Collections.TryGetValue(key, out var capture) || !capture.Usable)
                    return $"Creation prerequisite {path}: the referenced object {id} cannot be verified from complete tenant evidence.";
                if (!capture.Items.Any(i => string.Equals(i["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)))
                    return $"Creation prerequisite {path}: object {id} is missing from the captured tenant. Resolve this reference before creating the candidate.";
            }
        }
        return null;
    }
}
