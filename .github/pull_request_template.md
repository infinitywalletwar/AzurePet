## Summary

<!-- What does this PR change and why? -->

## Checklist

From `docs/DELIVERY.md` §7.
Mark items that do not apply yet as "n/a" with a short reason.

- [ ] Lab/issue reference and spec link
- [ ] All acceptance criteria ticked; manual checks recorded
- [ ] Tests added for new behaviour (domain, authorization, isolation)
- [ ] Cross-tenant suite extended for new data paths (from Lab 04)
- [ ] CSP and axe pass on new pages
- [ ] No secrets, no `listKeys()`, no shared keys, no SQL auth
- [ ] Migrations are expand-only (destructive changes only in a later release)
- [ ] `teardown` role checked if `workload.bicep` resource types changed (from Lab 09)
- [ ] ADRs followed; deviations listed with the ADR to supersede
- [ ] Cost impact noted for Azure changes
