# Core Isolation Drivers — manual VM checklist

Run before merging changes that touch driver delete/restore.

- [ ] Windows 10 19041 (or current supported baseline) and Windows 11
- [ ] Scan with authoritative enumeration; status + orphan findings show What/Why (+ debug)
- [ ] Create a known orphan OEM package (install then remove device, leave package); scan marks it actionable
- [ ] Disconnected USB/dock package is **not** treated as orphan
- [ ] Opt-in `Allow remove in-use blockers` required for bound CI-correlated packages; warning visible
- [ ] UAC cancel leaves no committed restore point / no partial delete
- [ ] Apply orphan → export identity present → package removed; restore re-stages package
- [ ] pnputil exit 3010 surfaces as reboot-required success in Clean Tasks
- [ ] de-DE / en-US module name and description from `.resw`
