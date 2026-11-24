# R-006 — C-96 screen group time estimates

Do NOT start C-96. Estimates only, in hours, per screen group.

| C-96 screen group | Estimate (hours) | Notes |
|---|---|---|
| Setup | 6 | Store name entry, local Hub start on free port, owner session, first-run flow to MainPage |
| Review queue | 5 | Replication items list, approve/reject with backoff, idempotent retry, nothing lost / nothing sent twice |
| Invoice issue | 8 | Create invoice, add items with product groups/subgroups, calculate totals, save to Hub |
| Invoices list + detail | 5 | List of issued invoices per store, open detail view (number, date, amounts, customer) |
| Product CRUD | 6 | Create/read/update/delete products, group/subgroup assignment |
| Customers | 4 | Customer list, details, edit, link to mobile-number identity |
| Return | 5 | Process returns linked to original invoice, stock adjustment |
| Settings | 4 | App settings, Hub connection, preferences, role switch store/customer/both |
| Devices | 4 | Paired devices list, revoke device, device credential management |
| Empty + Error states | 3 | Empty lists, error pages (status code + response body + copy-report button), retry |

**Total: ~50 hours**

Estimate covers the C# MAUI app screens only. Does not include: emulator (BLOCKED, no KVM on ubuntu-latest), PHP P-31 customer API dependency for C-92 OTP flow, or docs/design/canvas/canvas.json screen-fidelity (HELD until canvas.json lands on main).
