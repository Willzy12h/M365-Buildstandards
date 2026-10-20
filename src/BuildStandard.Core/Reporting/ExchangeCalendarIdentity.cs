namespace BuildStandard.Core.Reporting;

/// <summary>Typed mailbox/folder argument from verified GUID and the returned folder path; never a command string.</summary>
public static class ExchangeCalendarIdentity
{
    public static string FromReturnedPath(string mailboxGuid, string path)
    {
        if(!ReportValues.IsCanonicalGuid(mailboxGuid)||string.IsNullOrWhiteSpace(path)||path.Length>980
            ||path[0]!='/'||path.Contains('\\')||path.Contains(':')||path.Any(char.IsControl)
            ||path.Split('/').Skip(1).Any(part=>string.IsNullOrWhiteSpace(part)||part is "." or ".."))
            throw new ConfigurationException("A calendar argument requires the exact verified mailbox GUID and a returned absolute folder path without ambiguous segments.");
        return mailboxGuid+":"+path.Replace('/','\\');
    }
}
