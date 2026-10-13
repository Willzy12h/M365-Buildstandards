namespace BuildStandard.App.Services.Reader;
public sealed record ReaderTicket(Guid Id, PresentationContextKey Context, string HtmlSha256, long NavigationEpoch = 0);
public sealed class ReaderAdmission
{
    private ReaderTicket? _active;
    public ReaderTicket Begin(PresentationContextKey context, string digest, long navigationEpoch = 0) => _active = new(Guid.NewGuid(), context, digest, navigationEpoch);
    public bool IsCurrent(ReaderTicket ticket, PresentationContextKey context, long navigationEpoch = 0) => _active == ticket && ticket.Context == context && ticket.NavigationEpoch == navigationEpoch;
    public void Close() => _active = null;
}
