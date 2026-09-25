using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Planning;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>
/// One labelled input on the policy automation page, generated from the standard's own parameter list.
///
/// Before this, every client input was typed as one hand-written JSON object in a single text box: an engineer had to
/// know the key names, the types, and JSON punctuation, and learned about a mistake only when a plan refused to build.
/// A field knows its own type, so it accepts what a person would naturally type - identifiers separated by commas or
/// newlines - and says what is wrong next to the box that is wrong.
/// </summary>
public sealed class PolicyInputField : ObservableObject
{
    private string _value = "";
    private string _problem = "";

    public PolicyInputField(ParameterDefinition parameter, JsonNode? current, DateTimeOffset now)
    {
        Key = parameter.Key;
        Label = parameter.Label.Length > 0 ? parameter.Label : parameter.Key;
        Description = parameter.Description;
        Type = parameter.Type;
        IsRequired = parameter.Required;
        DefaultText = parameter.HasDefault ? PolicyInputParser.Render(Type, parameter.Default) : "";
        HasDefault = parameter.HasDefault;
        IsStaleDefault = parameter.IsStale(now);
        ReviewedOn = parameter.ReviewedOn;
        _value = PolicyInputParser.Render(Type, current);
    }

    public string Key { get; }
    public string Label { get; }
    public string Description { get; }
    public string Type { get; }
    public bool IsRequired { get; }
    public bool HasDefault { get; }
    public bool IsStaleDefault { get; }
    public string ReviewedOn { get; }
    public string DefaultText { get; }

    /// <summary>What the engineer typed. Empty means "not supplied", which is not the same as an empty list.</summary>
    public string Value
    {
        get => _value;
        set { if (SetProperty(ref _value, value)) { Problem = ""; OnPropertyChanged(nameof(Status)); } }
    }

    /// <summary>Why this field's value could not be used, shown beside the field rather than in a dialog.</summary>
    public string Problem
    {
        get => _problem;
        private set { if (SetProperty(ref _problem, value)) OnPropertyChanged(nameof(HasProblem)); }
    }

    public string Hint => Type switch
    {
        "guid" => "One object ID.",
        "guidList" => "Object IDs separated by commas or new lines; [] means an explicitly empty list.",
        "jsonArray" => "A JSON array.",
        "integer" => "A whole number.",
        "boolean" => "true or false.",
        _ => "Text."
    } + (IsRequired ? " Required." : HasDefault ? $" Optional; the default {DefaultText} is used if you leave it empty." : " Optional.");

    public string Status =>
        Value.Trim().Length > 0 ? "Supplied"
        : IsRequired ? "Needed before this policy can be created"
        : HasDefault ? (IsStaleDefault ? $"Default {DefaultText}, last reviewed {ReviewedOn} - check it" : $"Default {DefaultText}")
        : "Not supplied";

    /// <summary>
    /// Converts what was typed into the node the standard expects, or records the reason it could not. Returns false
    /// and leaves <see cref="Problem"/> set rather than throwing, so one bad field does not discard the whole form.
    /// </summary>
    public bool TryRead(out JsonNode? node, bool requireNow = false)
    {
        var read = PolicyInputParser.TryRead(Type, Value, out node, out var problem);
        if (requireNow && (!read || node is JsonArray { Count: 0 })) problem = "Supply " + Label + " for the selected control before saving.";
        Problem = problem;
        return read;
    }

    public bool HasProblem => Problem.Length > 0;

}
