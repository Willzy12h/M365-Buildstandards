using BuildStandard.App.Services;
using BuildStandard.App.Services.Reader;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ReaderAdmissionTests
{
    private static PresentationContextKey Context(long generation) => new(null,null,null,"synthetic client","synthetic account","standard","evidence",generation);
    [Fact] public void Client_ABA_and_new_document_discard_old_initialisation_callbacks()
    {
        var admission = new ReaderAdmission(); var a = Context(1); var first = admission.Begin(a,"digest-one");
        Assert.True(admission.IsCurrent(first,a)); Assert.False(admission.IsCurrent(first,Context(2)));
        var replacement = admission.Begin(Context(3),"digest-two");
        Assert.False(admission.IsCurrent(first,Context(3))); Assert.False(admission.IsCurrent(first,a));
        Assert.True(admission.IsCurrent(replacement,Context(3)));
    }
    [Fact] public void Late_completion_cannot_reopen_a_reader_after_unrelated_navigation_in_the_same_client()
    {
        var admission=new ReaderAdmission(); var key=Context(1); var ticket=admission.Begin(key,"digest",7);
        Assert.True(admission.IsCurrent(ticket,key,7)); Assert.False(admission.IsCurrent(ticket,key,8));
    }
    [Fact] public void Close_invalidates_even_unchanged_context_and_late_completion()
    {
        var admission = new ReaderAdmission(); var key = Context(1); var ticket=admission.Begin(key,"digest");
        admission.Close(); Assert.False(admission.IsCurrent(ticket,key));
    }
}
