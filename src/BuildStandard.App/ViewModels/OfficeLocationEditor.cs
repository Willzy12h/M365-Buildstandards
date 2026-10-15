using System.Collections.ObjectModel;
using BuildStandard.App.Infrastructure;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;

namespace BuildStandard.App.ViewModels;

/// <summary>Editable presentation only; stable instance identity and CIDR validation stay in the shared model.</summary>
public sealed class OfficeLocationRow : ObservableObject
{
    private string _name = "", _ranges = "";
    private readonly Action _changed;
    internal OfficeLocationRow(string key, Action changed) { Key = key; _changed = changed; }
    public string Key { get; }
    public string Name { get => _name; set { if (SetProperty(ref _name, value ?? "")) Edited(); } }
    public string Ranges { get => _ranges; set { if (SetProperty(ref _ranges, value ?? "")) Edited(); } }
    public string Problem
    {
        get { try { OfficeLocationValidator.Validate([Value()]); return ""; } catch (ConfigurationException ex) { return ex.Message; } }
    }
    internal OfficeLocation Value() => new() { Key = Key, Name = Name, IpRanges = Ranges.Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() };
    private void Edited() { OnPropertyChanged(nameof(Problem)); _changed(); }
}

public sealed class OfficeLocationEditor : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;
    public OfficeLocationEditor(Action? changed = null) { _changed = changed ?? (() => { }); }
    public ObservableCollection<OfficeLocationRow> Rows { get; } = new();
    public string Problem { get { try { Read(); return ""; } catch (ConfigurationException ex) { return ex.Message; } } }
    public void Load(IEnumerable<OfficeLocation>? locations)
    {
        var copy = OfficeLocationValidator.Validate(locations?.ToList()) ?? [];
        _loading = true;
        try
        {
            Rows.Clear();
            foreach (var location in copy) Rows.Add(new OfficeLocationRow(location.Key, Edited) { Name = location.Name, Ranges = string.Join(", ", location.IpRanges) });
        }
        finally { _loading = false; OnPropertyChanged(nameof(Problem)); }
    }
    public List<OfficeLocation> Read() => OfficeLocationValidator.Validate(Rows.Select(r => r.Value()).ToList())!;
    public void Add()
    {
        if (Rows.Count >= 100) throw new ConfigurationException("At most 100 office locations can be recorded.");
        Rows.Add(new OfficeLocationRow("OFFICE" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Edited)); Edited();
    }
    public void Remove(OfficeLocationRow row) { if (Rows.Remove(row)) Edited(); }
    private void Edited() { if (_loading) return; OnPropertyChanged(nameof(Problem)); _changed(); }
}
