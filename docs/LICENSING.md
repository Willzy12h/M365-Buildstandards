# Licence overview and scope

Connect read-only from **Overview and licences**. Subscriptions load automatically after connection; use **Refresh subscriptions** for a fresh read. Select a subscription, choose **Load user assignments**, then search its users by name, sign-in name or object ID. User assignments are loaded on demand because a full directory read may take time in large tenants.

The dashboard shows Microsoft's SKU identifiers, status, enabled seats, consumed/assigned seats, available capacity, warning and suspended units. **Available = enabled seats minus consumed seats**, so negative capacity remains visible. These are subscription counts, not billing or purchased-seat guarantees. Do not add per-product assignments to infer unique people. Differences between user and subscription counts are surfaced; failed reads or missing count properties show unknown rather than zero.

Captures are saved locally under `data/tenants/<tenantId>/licensing/`. They contain real tenant/user information when connected and must remain outside Git. Refreshing subscriptions clears the old assignment view until users are loaded again, preventing mismatched captures.

## Policy scope

After building a plan, **Policy licence scope** checks required service plans against the captured subscription and user data. A usable subscription must report enabled status and positive enabled capacity; its service plan must report successful provisioning. A user must have the relevant assigned SKU, an enabled assigned service plan, and no corresponding disabled-plan entry.

The supported scope is reviewed Conditional Access `includeUsers` targeting with direct user IDs or All and direct user exclusions. Missing target users, guests, group/role targeting and Intune device/group scope remain explicit review items. A successful tenant subscription read alone never proves every targeted person is licensed. Results report eligible users in captured data or an assignment gap; they do not assert contractual entitlement or effective enforcement. This report does not assign licences or introduce automatic activation.

The planner still separately blocks missing or unconfirmed tenant service-plan requirements. The scope report is advisory and does not resolve unsupported memberships or replace the engineer's assignment/licensing review. Inspect capture time and refresh before making decisions.

## Permissions and references

The feature uses the existing delegated read permissions for organisation/subscription and user reads. It makes no tenant writes, purchases or grants. The current standard already requests `Organization.Read.All` and `User.Read.All`; changing permissions requires the ordinary reviewed setup/consent workflow.

Microsoft references checked on 14 September 2026: [subscribed SKUs](https://learn.microsoft.com/en-us/graph/api/subscribedsku-list?view=graph-rest-1.0), [users and selected properties](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0), [assigned licences and disabled plans](https://learn.microsoft.com/en-us/graph/api/resources/assignedlicense?view=graph-rest-1.0), [assigned service-plan status](https://learn.microsoft.com/en-us/graph/api/resources/assignedplan?view=graph-rest-1.0) and [provisioning status](https://learn.microsoft.com/en-us/graph/api/resources/provisionedplan?view=graph-rest-1.0).
