using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Scripts;

public abstract record ScriptValue;
public sealed record ScriptText(string Value) : ScriptValue;
public sealed record ScriptTextList(IReadOnlyList<string> Values) : ScriptValue;
public sealed record ScriptFlag(bool Value) : ScriptValue;
public sealed record ScriptNumber(long Value) : ScriptValue;

public sealed record ScriptArgument(string Name, ScriptValue Value);

/// <summary>The typed values a form produced, or the reasons it cannot be used yet. Problems are written for the engineer.</summary>
public sealed record ScriptBinding(IReadOnlyList<ScriptArgument> Arguments, IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Turns what an engineer typed into typed script arguments. Blank optional fields are left out entirely, a default is
/// used only where the manifest shows one, and every value is checked against its declared type and format. Values are
/// only ever passed as data: as a splatted argument when run, or as a quoted literal in a copied script.
/// </summary>
public static class ScriptInputs
{
    private static readonly char[] ListSeparators = { '\n', '\r', ',', ';' };
    private static readonly Regex LocalPart = new("^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(\\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex Alias = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

    public static ScriptBinding Bind(ScriptManifest manifest, IReadOnlyDictionary<string, string?> input, DateTimeOffset now)
    {
        var problems = new List<string>();
        var arguments = new List<ScriptArgument>();
        foreach (var key in input.Keys)
            if (!manifest.Parameters.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{key} is not a field of {manifest.Name}.");

        foreach (var parameter in manifest.Parameters)
        {
            var raw = input.FirstOrDefault(kv => string.Equals(kv.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(raw)) raw = parameter.Default;
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (parameter.Required) problems.Add($"{parameter.Label} is required.");
                continue;
            }
            var value = Convert(parameter, raw, problems);
            // An unticked box is the script's own default, so it is left out like any other blank field.
            if (value is not null && value is not ScriptFlag { Value: false }) arguments.Add(new ScriptArgument(parameter.Name, value));
        }

        foreach (var rule in manifest.Rules) CheckRule(manifest, rule, arguments, now, problems);
        return new ScriptBinding(arguments, problems);
    }

    /// <summary>Converts one field. Returns null and records a problem when the value is not acceptable.</summary>
    internal static ScriptValue? Convert(ScriptParameter parameter, string raw, List<string> problems)
    {
        if (!parameter.Array) return ConvertSingle(parameter, raw.Trim(), problems) is { } single ? Wrap(parameter, single) : null;

        var items = raw.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count > parameter.MaxItems)
        {
            problems.Add($"{parameter.Label} takes at most {parameter.MaxItems} values; {items.Count} were entered.");
            return null;
        }
        var converted = new List<string>();
        foreach (var item in items)
            if (ConvertSingle(parameter, item, problems) is { } value) converted.Add(value);
        return converted.Count == items.Count && converted.Count > 0 ? new ScriptTextList(converted) : null;
    }

    private static ScriptValue Wrap(ScriptParameter parameter, string value) => parameter.Type switch
    {
        ScriptParameterType.Boolean => new ScriptFlag(value == "true"),
        ScriptParameterType.Integer => new ScriptNumber(long.Parse(value, CultureInfo.InvariantCulture)),
        _ => new ScriptText(value)
    };

    private static string? ConvertSingle(ScriptParameter p, string value, List<string> problems)
    {
        string? Fail(string reason) { problems.Add($"{p.Label}: {reason}"); return null; }

        switch (p.Type)
        {
            case ScriptParameterType.Boolean:
                return value.ToLowerInvariant() switch { "true" or "yes" or "1" => "true", "false" or "no" or "0" => "false", _ => Fail("tick or untick the box.") };
            case ScriptParameterType.Integer:
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) return Fail("enter a whole number.");
                if (number < p.Minimum || number > p.Maximum) return Fail($"enter a number from {p.Minimum} to {p.Maximum}.");
                return number.ToString(CultureInfo.InvariantCulture);
            case ScriptParameterType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return Fail("enter a date as YYYY-MM-DD.");
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case ScriptParameterType.Guid:
                return Guid.TryParse(value, out var guid) ? guid.ToString("D") : Fail("enter a GUID.");
            case ScriptParameterType.Enum:
                return p.Allowed!.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase))
                    ?? Fail("choose one of " + string.Join(", ", p.Allowed!) + ".");
        }

        var maximum = p.MaxLength ?? 256;
        if (value.Length > maximum) return Fail($"use at most {maximum} characters.");
        if (value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned))
            return Fail("remove line breaks and hidden formatting characters.");
        return (p.Format ?? ScriptValueFormat.Text) switch
        {
            ScriptValueFormat.Text => value,
            ScriptValueFormat.Upn => IsAddress(value) ? value : Fail("enter a sign-in name such as alex@example.com."),
            ScriptValueFormat.Smtp => IsAddress(value) ? value : Fail("enter an email address such as alex@example.com."),
            ScriptValueFormat.Mailbox => IsAddress(value) || Alias.IsMatch(value) ? value : Fail("enter the mailbox's email address or alias."),
            ScriptValueFormat.Domain => IsDomain(value) ? value.ToLowerInvariant() : Fail("enter a domain such as example.com."),
            ScriptValueFormat.IpAddress => (value.Contains('.') || value.Contains(':')) && IPAddress.TryParse(value, out var ip) ? ip.ToString() : Fail("enter an IPv4 or IPv6 address."),
            _ => Fail("this field has an unknown format.")
        };
    }

    private static bool IsAddress(string value)
    {
        var at = value.LastIndexOf('@');
        return value.Length <= 254 && at > 0 && at <= 64 && LocalPart.IsMatch(value[..at]) && IsDomain(value[(at + 1)..]);
    }

    private static bool IsDomain(string value)
    {
        try { MailDomain.Validate(value); return true; }
        catch (ConfigurationException) { return false; }
    }

    private static void CheckRule(ScriptManifest manifest, ScriptRule rule, List<ScriptArgument> arguments, DateTimeOffset now, List<string> problems)
    {
        string Label(string name) => manifest.Parameters.First(p => p.Name == name).Label;
        ScriptArgument? Find(string name) => arguments.FirstOrDefault(a => a.Name == name);

        if (rule.Kind == ScriptRuleKind.AtLeastOne)
        {
            if (!rule.Parameters!.Any(n => Find(n) is not null))
                problems.Add("Enter at least one of: " + string.Join(", ", rule.Parameters!.Select(Label)) + ".");
            return;
        }

        var start = Find(rule.Start!);
        var end = Find(rule.End!);
        if (start is null && end is null) return;
        if (start is null || end is null)
        {
            problems.Add($"Enter both {Label(rule.Start!)} and {Label(rule.End!)}.");
            return;
        }
        var from = DateOnly.ParseExact(((ScriptText)start.Value).Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(((ScriptText)end.Value).Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (to < from) problems.Add($"{Label(rule.End!)} must be on or after {Label(rule.Start!)}.");
        else if (to.DayNumber - from.DayNumber > rule.MaximumDays) problems.Add($"Choose a range of {rule.MaximumDays} days or fewer.");
        if (to > today.AddDays(1)) problems.Add($"{Label(rule.End!)} cannot be in the future.");
        if (rule.MaximumAgeDays is { } age && from < today.AddDays(-age)) problems.Add($"{Label(rule.Start!)} can be at most {age} days ago.");
    }
}
