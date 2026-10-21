namespace BuildStandard.Core.Reporting;

/// <summary>INT-129 fixed CSV sources. These columns do not establish identity/privacy provenance or licence entitlement.</summary>
public static class UsageReportRegistry
{
    public sealed record Definition(string Id, string Name, string Function, IReadOnlyList<string> Columns)
    {
        public string Route(string period) => IsPeriod(period) ? $"/reports/{Function}(period='{period}')"
            : throw new ConfigurationException("Choose D7, D30, D90 or D180; no arbitrary report query is permitted.");
    }
    public static bool IsPeriod(string period) => period is "D7" or "D30" or "D90" or "D180";
    public static IReadOnlyList<Definition> Definitions {get;} = Array.AsReadOnly(new Definition[]
    {
        new("active-users","Microsoft 365 active users","getOffice365ActiveUserDetail",Array.AsReadOnly(new[] { "Report Refresh Date", "User Principal Name", "Display Name", "Is Deleted", "Deleted Date", "Has Exchange License", "Has OneDrive License", "Has SharePoint License", "Has Skype For Business License", "Has Yammer License", "Has Teams License", "Exchange Last Activity Date", "OneDrive Last Activity Date", "SharePoint Last Activity Date", "Skype For Business Last Activity Date", "Yammer Last Activity Date", "Teams Last Activity Date", "Exchange License Assign Date", "OneDrive License Assign Date", "SharePoint License Assign Date", "Skype For Business License Assign Date", "Yammer License Assign Date", "Teams License Assign Date", "Assigned Products" })),
        new("email-activity","Email activity","getEmailActivityUserDetail",Array.AsReadOnly(new[] { "Report Refresh Date", "User Principal Name", "Display Name", "Is Deleted", "Deleted Date", "Last Activity Date", "Send Count", "Receive Count", "Read Count", "Meeting Created Count", "Meeting Interacted Count", "Assigned Products", "Report Period" })),
        new("teams-activity","Teams activity","getTeamsUserActivityUserDetail",Array.AsReadOnly(new[] { "Report Refresh Date", "Tenant Display Name", "Shared Channel Tenant Display Names", "User Id", "User Principal Name", "Last Activity Date", "Is Deleted", "Deleted Date", "Assigned Products", "Team Chat Message Count", "Private Chat Message Count", "Call Count", "Meeting Count", "Post Messages", "Reply Messages", "Urgent Messages", "Meetings Organized Count", "Meetings Attended Count", "Ad Hoc Meetings Organized Count", "Ad Hoc Meetings Attended Count", "Scheduled One-time Meetings Organized Count", "Scheduled One-time Meetings Attended Count", "Scheduled Recurring Meetings Organized Count", "Scheduled Recurring Meetings Attended Count", "Audio Duration", "Video Duration", "Screen Share Duration", "Audio Duration In Seconds", "Video Duration In Seconds", "Screen Share Duration In Seconds", "Has Other Action", "Is Licensed", "Report Period" })),
        new("onedrive-activity","OneDrive activity","getOneDriveActivityUserDetail",Array.AsReadOnly(new[] { "Report Refresh Date", "User Principal Name", "Is Deleted", "Deleted Date", "Last Activity Date", "Viewed Or Edited File Count", "Synced File Count", "Shared Internally File Count", "Shared Externally File Count", "Assigned Products", "Report Period" })),
    });
    public static Definition Find(string id) => Definitions.SingleOrDefault(d=>d.Id==id)
        ?? throw new ConfigurationException("Unknown usage source. Only the four registered reports are admitted.");
}
