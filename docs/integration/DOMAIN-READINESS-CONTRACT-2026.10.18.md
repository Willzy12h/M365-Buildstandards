# Domain readiness: bounded read-only contract

Proposed decision INT-126 for approved expansion D. Base: canonical main `2e4a0635ddfdf28330f8271942d53fb2a8ca3b0a`. This decision contains no implementation, service permission, DNS query, DNS change or live acceptance. Merge and applicable independent review are prerequisites for dependent source work. It does not reopen the older backlog.

## Purpose and existing boundaries

Provide selected-domain MX, SPF and Windows enrolment observations, together with a DNS-owner worksheet. These complement the existing DKIM/DMARC observations; they do not replace the Exchange capture, establish mail delivery, verify an SPF sender, prove device enrolment or satisfy deployment before-evidence. macOS remains unsupported/off/blocked.

Reuse `Core.Safety.MailDomain`, the fixed owned-process pattern in `Engine.Exchange.WindowsDnsLookup`, current presentation tickets and the existing local report writers. Keep `ExchangeCapture`, `DnsObservation`, `DnsResultStatus.NoRecords`, `IDnsLookup` and their historical meaning intact. Do not add MX to the old enum and silently admit it into existing capture validation. The new typed query/result pair is a separate report responsibility, not a generic process/script facility.

## New query and observation semantics

An internal `IDomainReadinessResolver.QueryAsync(DomainDnsQuestion, CancellationToken)` accepts only a validated ASCII DNS name and one exact type: TXT, MX or CNAME. No arbitrary PowerShell, resolver address, executable, command flags, credentials or query expressions enter this interface. Source domains use existing domain validation; the collector constructs only the selected apex, `enterpriseenrollment.<domain>`, `enterpriseregistration.<domain>`, and validated literal SPF include/redirect domains. Unicode/URL/wildcard/IP/control-character input is refused with guidance; no silent IDNA conversion is added.

Each observation records the exact question, UTC start/end, resolver description, adapter version and one state:

| State | Meaning |
| --- | --- |
| Observed | A successful response with at least one admitted answer of the requested type. |
| NameAbsent | Explicit native NXDOMAIN error 9003, not an empty array. |
| TypeAbsent | Explicit native no-record error 9501, not a missing field or filtered-away response. |
| Failed | Timeout, access/platform/helper/parse/response failure; absence is unknown. |
| Limited | A row/byte/deadline/traversal limit prevented a complete observation. |
| Cancelled | The caller stopped collection; preserve prior observations and mark the report incomplete. |

Unknown states, duplicate fields, mismatched questions/types, malformed times or invalid answer values are refused as adapter output. A successful process with empty/missing/null output is Failed. Null data cannot become False, zero, an empty-success result or an inferred DNS error code.

Typed answers retain owner name, type and returned TTL when valid: TXT keeps the ordered chunks of each individual record and its concatenated text; MX keeps preference and exchange; CNAME keeps its target. Never concatenate separate TXT records. Domain comparisons use case-insensitive canonical DNS names with a terminal dot removed only from returned DNS values. Raw returned values remain available for inspection. Preserve answer multiplicity; do not create an answer by silently dropping malformed entries. Any invalid entry makes the observation incomplete with a reason.

The resolver description is `Windows configured DNS resolver` unless the actual server is observed; never invent a server IP or claim authoritative/DNSSEC validation. Do not use hosts-file, LLMNR/NetBIOS or HTTP fallback. Query only via fixed `DnsClient\Resolve-DnsName` with `-DnsOnly -NoHostsFile`, not a general shell. No execution-policy change, module installation or application-control bypass.

## Bounded collection and ownership

One selected domain per run; up to 100 answers per question, 65,536 characters each for stdout/stderr, 20 seconds per owned helper and 120 seconds per report. Drain bounded streams concurrently, stop/kill only the owned process tree on cancellation/limit, and observe exit before claiming cleanup. A failure to end the owned process is explicit; do not start a replacement/fallback query. The helper path remains the fixed system Windows PowerShell path. Synthetic tests may inject resolver responses; production cannot choose an arbitrary host.

At most 32 distinct question/type pairs per report. Deduplicate network questions within that run, retaining all reference edges and their timestamps. Reused answers do not erase SPF term occurrences or their semantic cost. No persistent DNS cache or background refresh is introduced. Early cancellation/deadline/limits preserve observations but prevent overall Complete status.

## Readiness projections

**MX:** list all returned preferences/exchanges. Compare only with an explicitly selected expected gateway/target set, which is displayed with its provenance. Without one, report Observed / expected gateway not configured, not Compliant. Microsoft MX is not mandatory for a domain using another gateway. Mixed/unexpected targets are review findings; DNS alone does not establish delivery, connector configuration or domain ownership.

**SPF:** a structural readiness analysis, explicitly not RFC7208 `check_host()` or a sender-authentication verdict. Inspect each TXT record separately. Distinguish no SPF policy, exactly one, multiple and unreadable. Preserve mechanisms/modifiers/qualifiers and original policy text; malformed syntax is a finding. Recognise `ip4`, `ip6`, `all`, `include`, `redirect`, `a`, `mx`, `exists`, `ptr` and `exp` without claiming all can be evaluated without message context. Do not flatten or rewrite policies.

Only literal include/redirect domain references may be followed. No SPF macro expansion or source-derived executable input. Unsupported/macro-dependent terms remain Unable to check. Respect terminal `all` and redirect semantics when describing reachable references; an ignored redirect is not an active dependency. Recursion depth is at most 10, loops are explicit, and stop traversal at the 10 DNS-dependent-term analysis budget. Count terms/occurrences separately from deduplicated DNS questions; SPF's ten-term rule is not a ten-packet rule. Static traversal can overapproximate conditional paths, so budget exhaustion says Limited / cannot establish the path count, not a proven sender permerror. Do not claim a full RFC lookup count when address/MX/exists/ptr or other message-context-dependent evaluation was not performed. Preserve explicit absent include records versus failed queries.

**Enrolment:** show the two constructed CNAME questions and observed targets alongside current Microsoft guidance. `enterpriseenrollment` and `enterpriseregistration` have distinct meanings; a matching CNAME is DNS configuration only, not proof of MDM scope, licence, device eligibility, registration or enrolment. Expected targets depend on the applicable Microsoft cloud; this first slice is public-cloud only, with other clouds explicitly unsupported. Do not infer the cloud from arbitrary suffixes. Instructions for the DNS owner are manual text, never executable DNS changes.

## Evidence, native flow and exports

Use a distinct strict `domainReadinessReport` envelope, schema version 1, with report/run GUIDs, selected domain, optional verified tenant binding, initiating identity when actually known, source mode, UTC interval, toolkit/adapter version, requested checks, expected-target provenance, question/answer observations, per-check results/limitations and integrity digest. Unknown fields/types/versions refuse import; 5 MiB and 500 total observations/reference edges bound the entire envelope. Do not invent tenant binding for an offline domain. Imported records stay historical/unverified and retain original bytes/digest.

`Complete` means all requested supported reads finished within limits; it does not mean readiness passed. Result meaning remains separate: observed match, review finding, not configured or unable to check. Overall Failed/Partial/Cancelled is never an empty-success page. No report is admitted as `TenantSnapshot`, plan approval or execution evidence.

The native action states the domain, requested DNS reads, configured resolver and limits before explicit Run. Navigation performs no query. Capture an existing presentation ticket and immutable parameters; after every asynchronous completion and before export, recheck current context. Results from superseded context are not relabelled for the current client. Keep original unsaved-result recovery and local cancellation semantics. The existing catalogue browser remains catalogue-only.

Engine and CLI offline report projection/export share the same model. HTML/CSV/JSON include original observations, timestamps, selected checks, unknowns and DNS-owner next steps. Encode HTML and protect spreadsheet cells; source text is never rendered as markup or a command. Recheck source/current-context before publishing local exports; never overwrite configuration/standard evidence. No unattended CLI DNS collection is introduced by this contract.

## Implementation brief and acceptance

1. Add typed Core query/report models and strict schema validation; engine collector/projector with injected resolver and time budget. Tests first: malformed/unknown fields, explicit error states, mixed valid/invalid records, duplicate TXT policies, hostile text, no invented tenant binding, row/byte caps and historical import meaning.
2. Add the fixed Windows resolver using the existing ownership pattern, without changing old capture interfaces. Tests first: only supported names/types, exact response admission, stdout/stderr overflow, timeout/cancellation/failed termination, wrong type/question and empty output; PS5.1/7 parser/stub evidence with zero real DNS.
3. Add native selected-domain form, typed results and local export using existing context/progress/recovery controls. Test A–B–A supersession, domain changes, stop, export failure and zero queries on navigation; render all three harness sizes and inspect keyboard/disabled-action guidance.
4. Verify fresh composed Windows/App/native/hosted/portable source. Original package/hash must match changed bytes. Keep human accessibility, workstation-policy, public DNS and actual Microsoft enrolment acceptance unrun during development.

Expected files: new `Core/Models/DomainReadinessReport.cs`, `Engine/Exchange/DomainReadinessCollector.cs`, `Engine/Exchange/WindowsDomainReadinessResolver.cs`, strict report schema/export additions, native Reports controller/view integration and focused engine/App/PS tests. Exact file splits may follow existing patterns; responsibilities and admission must not move into WPF.

Rollback is removal of the additive report/route/registry. Existing Exchange capture and standards remain byte-compatible; retain any new evidence as a separately identified unsupported historical report rather than rewrite/delete it. No source change may add a generic DNS/script executor as a shortcut. Named safety review remains required where runner/process/context boundaries are affected; author review does not satisfy it.

## Primary references inspected 18 October 2026

- [Resolve-DnsName](https://learn.microsoft.com/en-us/powershell/module/dnsclient/resolve-dnsname?view=windowsserver2025-ps)
- [Microsoft SPF configuration guidance](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-spf-configure)
- [Windows automatic MDM enrolment](https://learn.microsoft.com/en-us/intune/device-enrollment/windows/enable-automatic-mdm)
- [RFC7208](https://www.rfc-editor.org/rfc/rfc7208.html), especially sections 4.6.4, 5, 6 and 7. Static readiness limits are not a replacement for the full sender-evaluation algorithm.

These are documentation observations, not live DNS or tenant validation. All implementation and expected tests above remain pending this contract's reviewed, owner-authorised merge.
