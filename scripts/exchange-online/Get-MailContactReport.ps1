<#
Mail contacts and mail users: each one's display name, primary SMTP address, the external address mail is sent to,
whether it is hidden from address lists, its recipient type and object ID.
Read only. The external address is shown exactly as Exchange returned it, including any SMTP: prefix; it is not
resolved or checked. An external address or primary address Exchange did not return, or returned null, is Unknown,
never blank. A true or false value Exchange did not return is shown as Unknown, never as False. Each type is read up to
the limit plus one, so a longer list is marked partial. Group memberships of contacts are not read.
#>
param(
    [string[]]$RecipientType = @('MailContact', 'MailUser'),
    [int]$MaxRecipients = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# True when any column holds Unknown, alone or as a name=Unknown setting, so the run can say what was not measured.
function Test-UnknownValue([object]$Row) {
    foreach ($property in @($Row.PSObject.Properties)) {
        if ([string]$property.Value -cmatch '(^|=)Unknown(;|$)') { return $true }
    }
    return $false
}
$unknownRows = 0

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

function Get-Text([object]$Value) {
    if ($null -eq $Value) { return '' }
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join '; ') -replace '\s+', ' '
}

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
}

foreach ($type in @('MailContact', 'MailUser')) {
    if ($RecipientType -notcontains $type) { continue }
    # One more than the limit is asked for, so a longer list is reported as partial rather than cut short silently.
    switch ($type) {
        'MailContact' { $recipients = @(Get-MailContact -ResultSize ($MaxRecipients + 1)) }
        'MailUser' { $recipients = @(Get-MailUser -ResultSize ($MaxRecipients + 1)) }
    }
    if ($recipients.Count -gt $MaxRecipients) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxRecipients + ' ' + $type + ' recipients exist; only the first ' + $MaxRecipients + ' are listed.')
        $recipients = @($recipients | Select-Object -First $MaxRecipients)
    }
    foreach ($recipient in $recipients) {
        $notes = @()
        $address = Get-Text (Get-Value $recipient 'PrimarySmtpAddress')
        if (-not $address) { $address = 'Unknown'; $notes += 'Exchange did not return a primary SMTP address.' }
        $external = Get-Text (Get-Value $recipient 'ExternalEmailAddress')
        if (-not $external) { $external = 'Unknown'; $notes += 'Exchange did not return an external email address.' }
        $objectId = Get-Text (Get-Value $recipient 'ExternalDirectoryObjectId')
        if (-not $objectId) { $objectId = 'Unknown'; $notes += 'Exchange did not return a directory object ID.' }
        $hidden = Get-Flag $recipient 'HiddenFromAddressListsEnabled'
        if ($hidden -eq 'Unknown') { $notes += 'Exchange did not return whether this recipient is hidden from address lists.' }
        $details = Get-Text (Get-Value $recipient 'RecipientTypeDetails')
        if (-not $details) { $details = 'Unknown'; $notes += 'Exchange did not return the recipient type details.' }
        $row = [pscustomobject]@{
            RecipientType = $type
            DisplayName = Get-Text (Get-Value $recipient 'DisplayName')
            PrimarySmtpAddress = $address
            ExternalEmailAddress = $external
            HiddenFromAddressLists = $hidden
            RecipientTypeDetails = $details
            ExternalDirectoryObjectId = $objectId
            WhenCreated = Get-Text (Get-Value $recipient 'WhenCreated')
            Notes = $notes -join ' '
        }
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
