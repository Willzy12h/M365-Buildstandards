using System.Globalization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.App.Services;

public enum ExperimentalOperation { Deploy, ReviewedChange, EntraLaps, PublishPackage, Recovery }

/// <summary>Desktop-only, transient authority. It never replaces the engine's exact operation approvals.</summary>
public sealed class ExperimentalOperationGuard
{
    public const string ClosedReason = "Experimental tenant changes are off. On Connect, review the verified connection and enable experimental changes for this session. Each change still needs its own exact approval.";
    private readonly Func<DateTimeOffset> _now;
    private object? _connection;
    private string? _binding;
    private DateTimeOffset _expires;

    public ExperimentalOperationGuard(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    public void Invalidate() { _connection = null; _binding = null; _expires = default; }

    public bool IsEnabled(object? connection, TenantSession? session, StandardCatalogue? standard, TenantProfile? profile)
    {
        if (_binding is null) return false;
        if (!ReferenceEquals(_connection, connection) || _expires <= _now()
            || !TryBinding(session, standard, profile, out var current) || current != _binding)
        { Invalidate(); return false; }
        return true;
    }

    public void Enable(object connection, TenantSession session, StandardCatalogue standard, TenantProfile profile, bool approved)
    {
        Invalidate();
        if (!approved || !TryBinding(session, standard, profile, out var binding)
            || !DateTimeOffset.TryParse(session.TokenExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
            || expires <= _now())
            throw new SafetyViolationException("A current verified deployment connection and deliberate experimental approval are required. Connect with deployment access first.");
        _connection = connection; _binding = binding; _expires = expires;
    }

    public void Require(ExperimentalOperation operation, object? connection, TenantSession? session, StandardCatalogue? standard, TenantProfile? profile)
    {
        if (!Enum.IsDefined(operation) || !IsEnabled(connection, session, standard, profile))
            throw new SafetyViolationException(ClosedReason);
    }

    public static void RequireManualSetup(bool approved, string selectedTenant, string verifiedTenant, string verifiedOperator)
    {
        if (!approved || !ProfileValidator.IsGuid(verifiedTenant) || !ProfileValidator.IsGuid(verifiedOperator)
            || !string.Equals(selectedTenant.Trim(), verifiedTenant, StringComparison.OrdinalIgnoreCase))
            throw new SafetyViolationException("Experimental application setup needs deliberate approval in its own verified tenant and administrator context.");
    }

    private static bool TryBinding(TenantSession? session, StandardCatalogue? standard, TenantProfile? profile, out string binding)
    {
        binding = "";
        if (session is not { Mode: SessionMode.Deployment, TenantVerified: true, OperatorVerified: true }
            || standard is null || profile is null || !ProfileValidator.IsGuid(session.TenantId)
            || !ProfileValidator.IsGuid(session.AccountObjectId) || !ProfileValidator.IsGuid(session.OperatorObjectId ?? "")
            || !string.Equals(session.AccountObjectId, session.OperatorObjectId, StringComparison.OrdinalIgnoreCase)
            || !ProfileValidator.IsGuid(session.ClientId) || string.IsNullOrWhiteSpace(standard.IntegrityDigest)
            || !string.Equals(profile.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase)
            || !session.HasWriteScopes || session.Scopes.Any(string.IsNullOrWhiteSpace)) return false;
        binding = CanonicalJson.Sha256(new JsonObject
        {
            ["tenant"] = session.TenantId.ToLowerInvariant(), ["account"] = session.AccountObjectId.ToLowerInvariant(),
            ["operator"] = session.OperatorObjectId!.ToLowerInvariant(), ["client"] = session.ClientId.ToLowerInvariant(),
            ["resource"] = "https://graph.microsoft.com", ["mode"] = session.Mode.ToString(),
            ["scopes"] = new JsonArray(session.Scopes.Select(s => s.ToLowerInvariant()).Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["catalogue"] = standard.IntegrityDigest, ["clientScope"] = ReviewedClientScope.Digest(profile)
        });
        return true;
    }
}
