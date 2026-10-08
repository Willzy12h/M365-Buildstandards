using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Core.Diagnostics;

namespace BDIT.TenantToolkit.Engine.Checks;

/// <summary>Shared desktop/offline-CLI evaluation. Never writes tenant state or the ordinary snapshot/assessment store.</summary>
/// <param name="standardsDirectory">
/// The installation's standards folder, from which the full assessment reads verified release lineage. Hosts pass the
/// same folder so a selected control carries the same lineage review as the full assessment. Null verifies no lineage,
/// so every ownership record from an earlier release is flagged for review.
/// </param>
public sealed class ScopedCheckService(IClock clock, string toolkitVersion, IToolkitLog log, string? standardsDirectory = null)
{
    public async Task<ScopedCheckEvidence> CollectAsync(IGraphClient graph, TenantSession session, StandardCatalogue catalogue,
        TenantProfile profile, CheckSelection selection, ManagedObjectMappings mappings, IReadOnlyList<Deviation> deviations,
        IProgress<CollectionProgress>? progress, CancellationToken ct)
    {
        if (!session.TenantVerified || !Guid.TryParse(session.AccountObjectId, out var account) || account == Guid.Empty)
            throw new ConfigurationException("A scoped live check needs a verified tenant and account. Reconnect, or review stored evidence instead.");
        var accountId = session.AccountObjectId;
        var accountName = session.Account;
        var tenantId = session.TenantId;
        var capture = await new TenantCollector(log, clock, toolkitVersion).CollectScopedAsync(graph, session, profile,
            catalogue, selection, progress, ct, preservePartialOnCancellation: true);
        if (session.AccountObjectId != accountId || session.Account != accountName || session.TenantId != tenantId)
            throw new TenantMismatchException("The verified identity changed while the scoped check was running. No result was accepted.");
        return Build(capture, catalogue, profile, selection, mappings, deviations, accountName, accountId, "liveScoped", null);
    }

    /// <param name="separateExchange">
    /// Separately captured or imported Exchange/Purview evidence, used as the full assessment uses it: in place of any
    /// Exchange capture inside the source. It is carried, and its ID and digest recorded, only when an Exchange or
    /// Purview control is selected.
    /// </param>
    public ScopedCheckEvidence ReviewHistorical(TenantSnapshot source, StandardCatalogue catalogue, TenantProfile profile,
        CheckSelection selection, ManagedObjectMappings mappings, IReadOnlyList<Deviation> deviations, string reviewedBy,
        ExchangeCapture? separateExchange = null)
        => Build(source, catalogue, profile, selection, mappings, deviations, reviewedBy, null, "historicalFiltered", separateExchange);

    private ScopedCheckEvidence Build(TenantSnapshot source, StandardCatalogue catalogue, TenantProfile profile,
        CheckSelection selection, ManagedObjectMappings mappings, IReadOnlyList<Deviation> deviations, string actor,
        string? accountId, string sourceMode, ExchangeCapture? separateExchange)
    {
        selection.ValidateFor(catalogue, profile);
        if (!string.Equals(source.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The check source belongs to a different tenant.");
        if (separateExchange is not null && !string.Equals(separateExchange.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The separate Exchange/Purview evidence belongs to a different tenant.");
        var sourceIntegrity = AssessmentEngine.IntegrityOf(source);
        if (sourceIntegrity == SnapshotIntegrityState.Modified)
            throw new ConfigurationException("The source capture is modified. Scoped filtering cannot make it trusted evidence.");
        if (sourceMode == "liveScoped" && source.Complete)
            throw new ConfigurationException("A full or historical capture cannot be relabelled as a live scoped read.");
        if (!Timestamps.TryParse(source.CapturedAt, out var capturedAt))
            throw new ConfigurationException("The source capture time is invalid.");
        var capture = ToolkitJson.Deserialize<TenantSnapshot>(ToolkitJson.Serialize(source));
        capture.Id = Guid.NewGuid().ToString();
        capture.Complete = false;
        capture.Collections = selection.CollectionKeys.ToDictionary(k => k, k => capture.Collections.TryGetValue(k, out var value)
            ? value : new CollectionCapture
            {
                Api = catalogue.Collections[k].Api, Path = catalogue.Collections[k].Path,
                Status = CaptureStatus.NotAttempted, Error = "Required dependency is not present in the source capture.", DetailIncomplete = true
            }, StringComparer.Ordinal);
        // An unrelated Graph control must not acquire or reproduce separate Exchange/Purview evidence.
        var controls = ControlInstances.All(catalogue, profile).Where(c => selection.ControlIds.Contains(c.Id)).ToList();
        var exchangeSelected = controls.Any(c => ControlAreas.For(c) is "Exchange" or "Purview");
        if (!exchangeSelected) capture.ExchangeCapture = null;
        // As in the full assessment, separate Exchange/Purview evidence takes the place of the source's own.
        else if (separateExchange is not null)
            capture.ExchangeCapture = ToolkitJson.Deserialize<ExchangeCapture>(ToolkitJson.Serialize(separateExchange));
        var separate = exchangeSelected && separateExchange is not null
            ? new ScopedSourceCapture { Id = separateExchange.Id, Sha256 = EvidenceIntegrity.Compute(capture.ExchangeCapture) } : null;
        capture.IntegrityDigest = EvidenceIntegrity.Compute(capture);
        var assessment = new AssessmentEngine(clock, toolkitVersion).AssessSelected(capture, catalogue, profile, mappings,
            deviations, actor, selection, evidenceTime: sourceMode == "historicalFiltered" ? capturedAt : null);
        // The same release-lineage review as the full assessment, limited to the selected controls (CLA-20261008-01).
        AssessmentContext.AnnotateLineage(assessment, mappings, catalogue, standardsDirectory, selection.ControlIds);
        if (sourceMode == "historicalFiltered")
        {
            // The derived capture's new digest only shows this record is unchanged; it does not authenticate the source,
            // so the assessment reports the source's own integrity state (CLA-20261008-04).
            assessment.SnapshotIntegrity = sourceIntegrity;
            if (sourceIntegrity == SnapshotIntegrityState.NotRecorded && !assessment.Limitations.Contains(AssessmentEngine.IntegrityNotRecordedLimitation))
                assessment.Limitations.Insert(0, AssessmentEngine.IntegrityNotRecordedLimitation);
            assessment.Limitations.Insert(0, "Historical filtered review: source capture " + source.Id + ", original integrity "
                + sourceIntegrity + (separate is null ? "" : "; separate Exchange/Purview capture " + separate.Id)
                + ". A digest of this derived record does not establish original capture authenticity or a fresh sign-in.");
        }
        var result = new ScopedCheckEvidence
        {
            Id = Guid.NewGuid().ToString(), TenantId = profile.TenantId, AccountObjectId = accountId, SourceMode = sourceMode,
            RecordedAt = Timestamps.Format(clock.UtcNow), CatalogueRelease = catalogue.Release, CatalogueDigest = catalogue.IntegrityDigest,
            ProfileId = profile.Id, ClientScopeDigest = ReviewedClientScope.Digest(profile),
            Areas = controls.Select(ControlAreas.For)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            ControlIds = selection.ControlIds.ToList(), CollectionKeys = selection.CollectionKeys.ToList(),
            SourceCapture = sourceMode == "historicalFiltered" ? new ScopedSourceCapture { Id = source.Id, Sha256 = EvidenceIntegrity.Compute(source) } : null,
            SeparateExchange = separate,
            Capture = capture, Assessment = assessment
        };
        ScopedCheckSchema.Seal(result, catalogue, profile);
        return result;
    }
}
