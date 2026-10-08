using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Scripts;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    /// <summary>
    /// Library run history (INT-081, proposed): one immutable record per Run under <c>script-runs/</c>, separate from
    /// snapshots, assessments, plans, deployment runs and INT-071 report evidence.
    /// </summary>
    public string ScriptRunsDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "script-runs");

    private string ScriptRunFile(string tenantId, string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out var id) || id == Guid.Empty) throw new ConfigurationException("Invalid run record ID.");
        return Path.Combine(ScriptRunsDirectory(tenantId), id.ToString("D") + ".json");
    }

    /// <summary>Writes a sealed run record with the shared create-only writer. An existing record is never replaced.</summary>
    public string SaveScriptRun(ScriptRunRecord record)
    {
        var json = ScriptRunSchema.Serialize(record);
        ScriptRunSchema.Read(json, record.TenantId);
        var file = ScriptRunFile(record.TenantId, record.Id);
        // A JSON node keeps the explicit nulls (exit code, failure) the strict reader requires.
        CreateRecordFile(file, System.Text.Json.Nodes.JsonNode.Parse(json)!, "run record");
        return file;
    }

    public ScriptRunRecord? LoadScriptRun(string tenantId, string runId)
    {
        var file = ScriptRunFile(tenantId, runId);
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ScriptRunSchema.MaximumBytes) throw new ConfigurationException("The run record exceeds its reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ScriptRunSchema.MaximumBytes + 1);
        if (bytes.Length > ScriptRunSchema.MaximumBytes) throw new ConfigurationException("The run record exceeds its reader limit.");
        var record = ScriptRunSchema.Read(new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'), tenantId);
        if (!string.Equals(record.Id, runId, StringComparison.OrdinalIgnoreCase)) throw new IntegrityException("The run file contains a different record ID.");
        return record;
    }
}
