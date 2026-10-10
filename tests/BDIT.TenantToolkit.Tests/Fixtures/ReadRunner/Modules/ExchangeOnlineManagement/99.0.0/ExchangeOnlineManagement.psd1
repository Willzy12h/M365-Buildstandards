@{
    RootModule = 'ExchangeOnlineManagement.psm1'
    ModuleVersion = '99.0.0'
    GUID = '0f0a1e5b-6d0e-4a59-9f1e-7a1d2c3b4e5f'
    Author = 'Synthetic test fixture'
    Description = 'Synthetic stand-in for runner regressions. Not Microsoft code; never signs in or contacts a service. Versioned 99.0.0 so it always outranks a real installed module.'
    FunctionsToExport = @('Connect-ExchangeOnline', 'Disconnect-ExchangeOnline', 'Get-ConnectionInformation', 'Get-EXOMailbox', 'Get-EXOMailboxStatistics', 'Set-Mailbox')
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
