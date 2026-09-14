# Windows device candidates

For the current 2026.09.5 LAPS, antivirus, firewall and EDR additions, see [policy automation APIs](POLICY-AUTOMATION-CODE.md). The BitLocker and long-path settings below remain unchanged.

Standard **2026.09.4** adds two optional reviewed creation recipes. Both create an **unassigned** policy, record its exact object ID, read it back and support selective removal while still unassigned. Creation never assigns policies or starts device encryption. The standard now has 45 controls, 14 recipes and 14 collection definitions.

## BitLocker: CFG-WIN-001

The user supplied these values and confirmed every other option is **not configured**:

| Setting | Candidate value |
|---|---|
| Encrypt devices | Require |
| Warning for other disk encryption | Block |
| Standard users enabling encryption during Entra join | Allow |
| Additional authentication at startup | Require |
| TPM startup | Require TPM |
| TPM PIN / startup key / startup key and PIN | Prohibited |
| Minimum PIN length | 14; inactive while startup PIN is prohibited |
| OS and fixed-drive recovery | Enabled |
| Save recovery information before enabling encryption | Required for both OS and fixed drives |
| Other recovery choices, algorithm, removable drives, rotation | Omitted; not configured |

The repeated escrow settings in the supplied list are represented for OS and fixed drives. No recovery-password/key type, data-recovery-agent choice, recovery UI restriction or encryption method is invented. Omitted server defaults must be checked against the portal before assignment.

The Endpoint Protection schema exposes the supplied nested drive settings through Graph beta; a separate `endpointProtection` collection makes that API use explicit and leaves existing v1.0 recipes unchanged. It overlaps the normal configuration inventory. Recovery permits beta only on the device-configuration resource, with the same exact-ID, ownership, assignment, drift and evidence checks. Other beta recovery routes remain blocked.

Supported-device behaviour, third-party encryption compatibility, key escrow and recovery must be tested on pilot devices. Suppressing other-encryption warnings is a consequential setting the user explicitly selected. Removing a policy does not decrypt drives, restore device state or remove escrowed recovery information. Windows edition/licence eligibility requires a device-level check in addition to the tool's tenant Intune-plan check; no licence inference is made from API acceptance.

## Long paths: CFG-WIN-007

Explicitly select this optional control for the client. The v1.0 custom-configuration recipe sends a string `<enabled/>` to `./Device/Vendor/MSFT/Policy/Config/ADMX_FileSys/LongPathsEnabled`. Microsoft documents supported Windows 10 builds and Windows 11; individual applications must support long paths, and a restart may be needed. Review overlapping Settings Catalogue policies before assignment.

## Validation boundary

Synthetic tests exercise actual shipped payloads through plan, durable deployment evidence, readback, assignment protection and deletion/absence recovery. They do not prove Intune accepts all omitted defaults or that devices encrypt, escrow keys or apply long-path settings. Preserve standard 2026.09.3 for existing evidence; use a fresh snapshot and reviewed plan for 2026.09.4.

Sources checked 14 September 2026: [Endpoint Protection beta schema](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-windows10endpointprotectionconfiguration?view=graph-rest-beta), [OS-drive settings](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-bitlockersystemdrivepolicy?view=graph-rest-beta), [recovery options](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-bitlockerrecoveryoptions?view=graph-rest-beta), [BitLocker CSP and recovery behaviour](https://learn.microsoft.com/en-us/windows/client-management/mdm/bitlocker-csp), [long paths CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-filesys).
