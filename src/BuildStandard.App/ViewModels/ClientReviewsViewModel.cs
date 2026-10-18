using System.Collections.ObjectModel;
using BuildStandard.App.Infrastructure;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Identity;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.App.ViewModels;

public sealed record ClientPortfolioRow(string ProfileId, string TenantId, string Company, string Assessment, string Standard,
    string Writes, string Jobs, string Detail);
public sealed class ClientImportRow(PartnerCustomerRow row) : ObservableObject
{
    private bool _selected;
    public PartnerCustomerRow Row { get; } = row;
    public bool CanSelect => Row.State == CustomerImportState.Ready;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, CanSelect && value); }
}

public sealed class ClientReviewsViewModel : PageViewModel
{
    public ClientReviewsViewModel(ShellViewModel shell) : base(shell,"Clients and reviews") { }
    public ObservableCollection<ClientPortfolioRow> Clients { get; } = new();
    public ObservableCollection<ClientImportRow> ImportRows { get; } = new();
    public ClientPortfolioRow? Selected { get; set; }
    public ClientReviewResult? Review { get; private set; }
    public PartnerCustomerPreview? ImportPreview { get; private set; }
    public string Status { get; private set; } = "Read local evidence or preview a customer CSV. No connection is made.";
    public Task ScanAsync() => throw new NotImplementedException();
    public Task PreviewImportAsync(byte[] bytes) => throw new NotImplementedException();
    public Task SaveImportAsync() => throw new NotImplementedException();
    public Task ReviewSelectedAsync() => throw new NotImplementedException();
    public Task OpenSelectedAsync() => throw new NotImplementedException();
    public override void Refresh() { }
}
