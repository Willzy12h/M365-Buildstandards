# INT-128/132: one frozen, typed query. Only the private gate chooses source dates, filters and caps.
param([string]$StartDate,[string]$EndDate,[string]$Sender='',[string]$Recipient='',[string]$InternetMessageId='',[string]$MessageTraceId='',[bool]$IncludeSubject=$false,[int]$MaximumRows=5000,$TracePlan)
Set-StrictMode -Version Latest
function TextOf($Item,[string]$Name) {
    $p=$Item.PSObject.Properties[$Name]
    if($null -eq $p -or $p.Value -isnot [string] -or $p.Value.Length -gt 1024 -or $p.Value -match '\p{Cc}'){return $null}
    return $p.Value
}
function Setting($Item,[string]$Name) {
    $p=$Item.PSObject.Properties[$Name]
    if($null -eq $p){return @{state='Missing';value=$null;reason=($Name+' was not returned.')}}
    if($null -eq $p.Value){return @{state='ReturnedNull';value=$null;reason=($Name+' returned null.')}}
    if($p.Value -is [string] -and $p.Value.Length -le 1024 -and $p.Value -notmatch '\p{Cc}') {return @{state='Value';value=$p.Value;reason=$null}}
    if($Name -ceq 'Size' -and ($p.Value -is [int] -or $p.Value -is [long] -or $p.Value -is [uint64])){return @{state='Value';value=$p.Value.ToString();reason=$null}}
    return @{state='Unsupported';value=$null;reason=($Name+' returned an unsupported value.')}
}
$intervals=[Collections.Generic.List[object]]::new();$traces=[Collections.Generic.List[object]]::new();$seen=@{}
for($index=0;$index -lt $TracePlan.Windows.Count;$index++) {
    $window=$TracePlan.Windows[$index]
    $intervals.Add([ordered]@{id=$null;name=$null;readStatus='NotAttempted';error='This reviewed window was not attempted.';windowIndex=$index;frozenAtUtc=$TracePlan.FrozenAtUtc;startUtc=$window.startUtc;endUtc=$window.endUtc;filterDigest=$TracePlan.FilterDigest;requestedResultSize=$null;returnedEntries=$null;excludedEntries=$null})
}
foreach($interval in $intervals) {
    $batch=[Collections.Generic.List[object]]::new();$batchKeys=@{}
    try {
        $read=Use-BditRead -Command 'Get-MessageTraceV2' -Parameters @{WindowIndex=$interval.windowIndex}
        $source=@($read.Rows);$interval.requestedResultSize=$read.ResultSize;$interval.returnedEntries=$source.Count;$interval.excludedEntries=0;$interval.readStatus='Collected';$interval.error=$null
        if($source.Count -ge $read.ResultSize){$interval.readStatus='Partial';$interval.error='The bounded source cap was reached; completeness is unknown.'}
        foreach($item in $source) {
            $guid=$null;$p=$item.PSObject.Properties['MessageTraceId']
            if($null -ne $p -and ($p.Value -is [guid] -or $p.Value -is [string])){try{$g=[guid]$p.Value;if($g -ne [guid]::Empty -and ([string]$p.Value).Length -eq 36){$guid=$g.ToString('D')}}catch{}}
            $address=TextOf $item 'RecipientAddress';$received=TextOf $item 'Received'
            if($null -eq $guid -or [string]::IsNullOrWhiteSpace($address) -or [string]::IsNullOrWhiteSpace($received)){$interval.excludedEntries++;$interval.readStatus='Partial';$interval.error='A recipient lacked its exact source identity; excluded, never assigned an invented identity.';continue}
            $key=$guid+'|'+$address+'|'+$received
            if($seen.ContainsKey($key) -or $batchKeys.ContainsKey($key)){throw 'Repeated recipient identity; affected window is not admitted.'}
            $batchKeys[$key]=$true
            $subject=if($TracePlan.IncludeSubject){Setting $item 'Subject'}else{@{state='NotAttempted';value=$null;reason='Subject collection was not selected.'}}
            $row=[ordered]@{id=$null;name=$null;readStatus='Collected';error=$null;windowIndex=$interval.windowIndex;messageTraceId=$guid;recipientAddress=$address;receivedUtc=$received;
                senderAddress=(Setting $item 'SenderAddress');internetMessageId=(Setting $item 'MessageId');status=(Setting $item 'Status');size=(Setting $item 'Size');subject=$subject}
            foreach($field in @('senderAddress','internetMessageId','status','size','subject')){if($row[$field].state -cne 'Value' -and -not ($field -ceq 'subject' -and -not $TracePlan.IncludeSubject)){$row.readStatus='Partial';$row.error='Some trace metadata could not be checked.';$interval.readStatus='Partial';$interval.error='Trace metadata contains unknown values.'}}
            $batch.Add($row)
        }
        foreach($row in $batch){$traces.Add($row);$seen[$row.messageTraceId+'|'+$row.recipientAddress+'|'+$row.receivedUtc]=$true}
    }catch{$interval.readStatus='Failed';$interval.error='The bounded trace window failed, stopped or returned repeated identities. Later windows were not read; no retry or successful empty claim.';$interval.returnedEntries=$null;$interval.excludedEntries=$null;break}
}
$status='Collected';$reason=$null
foreach($interval in $intervals){if($interval.readStatus -cne 'Collected'){$status='Partial';$reason='Trace collection is incomplete; inspect interval diagnostics and unknown values.'}}
if($null -eq $TracePlan.MessageTraceId -or $TracePlan.CurrentDayIncomplete){$status='Partial';$reason='General high-recipient coverage or current-day results cannot establish completeness.'}
@{Sections=@(@{Id='interval-reads';Status=$status;Error=$reason;Rows=$intervals.ToArray()},@{Id='traces';Status=$status;Error=$reason;Rows=$traces.ToArray()})}
