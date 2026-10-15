using System.Collections.ObjectModel;
using BuildStandard.App.Infrastructure;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

public sealed class OfficeLocationRow : ObservableObject
{
    private string _name = "", _ranges = "";
    public string Key { get; init; } = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Ranges { get => _ranges; set => SetProperty(ref _ranges, value); }
}

public sealed class OfficeLocationEditor : ObservableObject
{
    public ObservableCollection<OfficeLocationRow> Rows { get; } = new();
    public void Load(IEnumerable<OfficeLocation>? locations) => throw new NotSupportedException("Office row editor not implemented.");
    public List<OfficeLocation> Read() => throw new NotSupportedException("Office row editor not implemented.");
}
