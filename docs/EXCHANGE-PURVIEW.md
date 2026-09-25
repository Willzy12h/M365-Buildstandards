# Exchange and Purview observations

In **Configuration > Exchange & Purview**, select a client and standard 2026.09.12, enter the client's accepted mail domain, then export the read-only capture script. Review it before running it manually in a fresh PowerShell process with a supported, already installed Exchange Online module and a delegated account with appropriate read-only RBAC. The toolkit does not install the module or run the script. Import the resulting JSON and review **Assessment > Exchange** or **Purview**. The separate **Check public DNS** action checks both DKIM CNAMEs and the DMARC TXT record; it does not sign in to a tenant.

An import creates a durable offline snapshot and clears the deployment plan and snapshot acknowledgement. It cannot be acknowledged for deployment or combined with Graph evidence to authorise a toolkit write. Replacing a current snapshot requires a new capture or an explicitly selected saved snapshot through the normal workflow.

The importer accepts only the capture schema's projected configuration fields, within 2 MiB. Unknown or duplicate fields, other tenants, non-delegated provenance, invalid timestamps and supplied DNS answers are refused. Source claims are **not independently authenticated**. Missing properties, failed reads and old observations remain unknown. A matching setting still requires manual verification of effective service behaviour, permissions and licensing.

The capture includes audit ingestion and retention policies, transport rules, both preset policy components, outbound forwarding policies, SMTP AUTH, external tags, mailbox audit configuration and DKIM. It excludes mailbox contents, audit events, credentials and connection objects. Purview retention collection is optional; a failed or omitted read does not establish absence. Default audit retention and entitlement must be checked for affected users; no retention or DLP policy is changed.

DKIM displays the **actual returned** selector targets. Both fresh DNS answers must match those targets before readiness can be reported. This is one resolver's view and does not establish successful message signing. DMARC reports presence and basic policy syntax; it is not a full RFC validator and does not change DNS. Check alignment, reporting destinations and actual mail manually.

The supported module has internal retries. Consequently, this release does not execute Exchange writes in the toolkit; see the fallback decision in [the API investigation](integration/API-INVESTIGATION-2026.09.12.md). The own-domain SPF spam-bypass rule also has an unverified header-trust/alignment boundary and must not be labelled spoof-safe.

## Source contracts and verification limits

- [Exchange connection information](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-connectioninformation?view=exchange-ps): `TenantID`, `State` and `IsEopSession` are checked before each scripted read. Command import allow-lists do not grant or constrain RBAC on their own.
- [Exchange Online module](https://learn.microsoft.com/en-us/powershell/exchange/exchange-online-powershell-v2): use a supported module/runtime pairing; no administrator requirement, installation or execution-policy change is introduced by the toolkit.
- [DKIM configuration](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-dkim-configure): use the service's returned CNAME targets, never construct them from an assumed format.
- [DMARC syntax and manual rollout](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-dmarc-configure): review policy, percentage, alignment and reporting before enforcement.
- [Resolve-DnsName](https://learn.microsoft.com/en-us/powershell/module/dnsclient/resolve-dnsname?view=windowsserver2025-ps) and [Windows DNS error codes](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--9000-11999-): the fixed Windows adapter uses DNS-only CNAME/TXT questions, excludes the hosts file, and distinguishes explicit absence from errors.

Implementation and tests use synthetic fixtures only. Actual Exchange/Purview responses, module/runtime compatibility, DNS resolver behaviour, service effects and licences remain **unverified**. The interface harness never presses the DNS action and never executes an exported script.
