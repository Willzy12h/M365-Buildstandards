using System.Diagnostics;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Scripts.Runner;

namespace BuildStandard.Engine.Exchange;

internal interface IDomainReadinessResolver
{
    Task<DomainDnsObservation> QueryAsync(DomainDnsQuestion question,CancellationToken ct);
}
internal sealed record DomainDnsHelperResult(OwnedProcessOutcome Process,string Output);
public sealed class WindowsDomainReadinessResolver : IDomainReadinessResolver
{
    private readonly Func<DomainDnsQuestion,CancellationToken,Task<DomainDnsHelperResult>> _run;
    private int _active;
    private bool _terminationUnconfirmed;
    public WindowsDomainReadinessResolver() : this(RunWindowsAsync) { }
    internal WindowsDomainReadinessResolver(Func<DomainDnsQuestion,CancellationToken,Task<DomainDnsHelperResult>> run)=>_run=run;
    public async Task<DomainDnsObservation> QueryAsync(DomainDnsQuestion question,CancellationToken ct)
    {
        var started=Timestamps.Format(DateTimeOffset.UtcNow);
        DomainDnsObservation Incomplete(DomainDnsState state,string reason)=>new(question.Name,question.Type,state,
            started,Timestamps.Format(DateTimeOffset.UtcNow),DomainDnsResponse.Resolver,DomainDnsResponse.AdapterVersion,Array.Empty<DomainDnsAnswer>(),null,null,reason);
        if(ct.IsCancellationRequested)return Incomplete(DomainDnsState.Cancelled,"Stopped before the DNS helper started; no query was made.");
        if(Interlocked.CompareExchange(ref _active,1,0)!=0)return Incomplete(DomainDnsState.Failed,"Another DNS helper is active; this query was not started.");
        try
        {
            if(_terminationUnconfirmed)return Incomplete(DomainDnsState.Failed,"Earlier helper cleanup was not confirmed; this query was not started.");
            var result=await _run(question,ct).ConfigureAwait(false);
            if(result.Process.End is OwnedProcessEnd.NotStopped or OwnedProcessEnd.OutputHeldOpen)
            {
                _terminationUnconfirmed=true;
                return Incomplete(DomainDnsState.Failed,"The DNS helper or a child may still be running. Cleanup was not confirmed; no replacement query will start.");
            }
            if(result.Process.End==OwnedProcessEnd.Cancelled || ct.IsCancellationRequested)
                return Incomplete(DomainDnsState.Cancelled,"Stopped before a complete DNS response was admitted.");
            if(result.Process.End==OwnedProcessEnd.LimitExceeded)
                return Incomplete(DomainDnsState.Limited,"DNS helper output exceeded 65,536 characters; a complete response was not admitted.");
            if(result.Process.End!=OwnedProcessEnd.Exited || result.Process.ExitCode!=0)
                return Incomplete(DomainDnsState.Failed,"The fixed DNS helper did not finish successfully ("+result.Process.End+"); DNS absence is unknown.");
            return DomainDnsResponse.Parse(result.Output,question);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        { return Incomplete(DomainDnsState.Cancelled,"Stopped before a complete DNS response was admitted."); }
        catch(Exception ex) when(ex is ToolkitException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        { return Incomplete(DomainDnsState.Failed,"DNS helper output or execution could not be verified; DNS absence is unknown. "+(ex is ToolkitException ? ex.Message : "Inspect local prerequisites and retry only after cleanup.")); }
        finally { Interlocked.Exchange(ref _active,0); }
    }

    public static string QueryScript(DomainDnsQuestion question)
    {
        // Reconstruct even at this boundary: only the immutable typed, validated name/type enter the fixed template.
        question=new(question.Name,question.Type);
        return $$"""
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $rows = [Collections.Generic.List[object]]::new()
            $limited = $false
            $result = [ordered]@{ name = '{{question.Name}}'; type = '{{question.Type}}'; state = 'Failed';
                startedAt = [DateTimeOffset]::UtcNow.ToString('O'); completedAt = $null;
                resolver = 'Windows configured DNS resolver'; adapterVersion = '1'; answers = @() }
            try {
                DnsClient\Resolve-DnsName -Name '{{question.Name}}' -Type {{question.Type}} -DnsOnly -NoHostsFile -QuickTimeout -ErrorAction Stop | ForEach-Object {
                    if ($rows.Count -ge 100) { $limited = $true; throw 'DNS answer limit reached' }
                    $record = $_
                    $row = [ordered]@{ owner = $record.Name; type = $null; section = $null; ttl = $record.TTL }
                    if ($null -ne $record.Type) { $row.type = $record.Type.ToString() }
                    if ($null -ne $record.Section) { $row.section = $record.Section.ToString() }
                    switch ($row.type) {
                        'TXT' { if ($null -eq $record.Strings) { $row.textChunks = $null } else { $row.textChunks = @($record.Strings) } }
                        'MX' { $row.exchange = $record.NameExchange; $row.preference = $record.Preference }
                        'CNAME' { $row.target = $record.NameHost }
                    }
                    $rows.Add($row)
                }
                # Empty or malformed success is rejected by typed admission; never invent no-record status.
                $result.state = 'Observed'
            }
            catch {
                $code = $null
                if ($_.Exception -is [ComponentModel.Win32Exception]) { $code = $_.Exception.NativeErrorCode }
                if ($limited) { $result.state = 'Limited'; $result.reason = 'More than 100 answer records; collection incomplete.' }
                elseif ($code -eq 9003) { $result.state = 'NameError'; $result.nativeErrorCode = $code }
                elseif ($code -eq 9501) { $result.state = 'NoData'; $result.nativeErrorCode = $code }
                else { $result.state = 'Failed'; $result.reason = 'DNS query failed; absence is unknown.' }
            }
            $result.answers = @($rows.ToArray())
            $result.completedAt = [DateTimeOffset]::UtcNow.ToString('O')
            $result | ConvertTo-Json -Depth 8 -Compress
            """;
    }
    private static Task<DomainDnsHelperResult> RunWindowsAsync(DomainDnsQuestion question,CancellationToken ct)
    {
        if(!OperatingSystem.IsWindows())throw new ConfigurationException("Domain DNS collection requires Windows; no query was made.");
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"))
        { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(QueryScript(question)))})start.ArgumentList.Add(arg);
        return RunHelperAsync(start,TimeSpan.FromSeconds(20),ct);
    }
    internal static async Task<DomainDnsHelperResult> RunHelperAsync(ProcessStartInfo start,TimeSpan timeout,CancellationToken ct)
    {
        var output=new StringBuilder();var limited=0;
        async Task Read(StreamReader reader,bool retain)
        {
            var count=0;var buffer=new char[2048];int read;
            while((read=await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false))!=0)
            {
                var remaining=65536-count;
                if(read>remaining)Interlocked.Exchange(ref limited,1);
                if(retain && remaining>0)output.Append(buffer,0,Math.Min(read,remaining));
                count=Math.Min(65536,count+read);
            }
        }
        var result=await OwnedProcess.RunAsync(start,r=>Read(r,true),r=>Read(r,false),timeout,
            ()=>Volatile.Read(ref limited)!=0,()=>{},TimeSpan.Zero,ct).ConfigureAwait(false);
        // Readers may still own the buffer when process/pipe termination is unconfirmed. Never inspect or trust it.
        if(result.End is OwnedProcessEnd.NotStopped or OwnedProcessEnd.OutputHeldOpen)return new(result,"");
        return new(result,output.ToString());
    }
}
