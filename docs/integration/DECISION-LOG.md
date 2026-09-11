# Integration decision log

| ID | Date | Area | Decision | Reason | Source | Validation | Status |
|---|---|---|---|---|---|---|---|
| INT-001 | | Repository model | Preserve two source repositories and build the shared product in this master repository | Maintains provenance and gives both LLMs a controlled integration target | User decision | Repository structure | Accepted |
| INT-002 | 2026-09-11 | Canonical base | Build the integrated product on the Claude toolkit (`337c8e6`); treat the Astra/Codex console (`020c69b`) as reference and port source rather than a merge input | The two implementations are different platforms, so no merge is possible — one is the base and the other is ported into it. The stated case for the toolkit is its safety model (read/write separation enforced before any request, the write guard in three layers, ambiguous-write handling) which its own review found could not be retrofitted onto the console architecture. **Not yet accepted. Proposed by Claude, about Claude's own implementation, and open for Astra/Codex to contest — see `REVIEW-BRIEF-ASTRA.md`.** | Claude toolkit `docs/MIGRATION-FROM-TENANT-CONSOLE.md` and `docs/REVIEW-REPORT.md`; both contested until the review lands | Pending Astra/Codex review and `COMPARISON-MATRIX.md` | Proposed |

Status values: Proposed, Accepted, Replaced or Rejected.

Record architecture, schema, security, permission and migration decisions. Link the relevant commit or pull request when available.

## INT-002 and what would overturn it

INT-002 decides the platform, and it is the decision everything after it rests on. It is recorded as Proposed rather than Accepted on purpose: the argument for it was written by one of the two agents about its own code, and has not yet been read by anyone who might disagree.

It is not a judgement that one agent writes better code. The two implementations cannot be merged — C#/.NET with a native Windows interface on one side, Node and PowerShell workers with a browser interface on the other — so the only available choices are to pick a base and port into it, or to rebuild again. Picking a base is cheaper, and that is the whole of the reasoning.

Accept, amend or reject it on the evidence in `COMPARISON-MATRIX.md`, not on the proposal text. Any of the following should change it:

- An area where the console's behaviour is materially better and porting it would lose that quality. Record it as a per-area carve-out; INT-002 can be accepted with exceptions.
- A capability in the console that the parity checklist in `MIGRATION-FROM-TENANT-CONSOLE.md` does not list. The checklist is the basis for retiring the console, and an incomplete checklist makes INT-002 unsafe to accept as written.
- Evidence that the safety claims for the toolkit are weaker in practice than the document states. The claims are argued, not yet proven against a live tenant.
- Anything in the console that is validated against a real tenant where the toolkit's equivalent is only reasoned. Real Graph payloads that have worked outrank carefully designed ones that have never run.

Until INT-002 is Accepted, `integration` is not seeded with either implementation. Seeding first would settle the decision in the file tree and make the review ceremonial.
