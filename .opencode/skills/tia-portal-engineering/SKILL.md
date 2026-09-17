---
name: tia-portal-engineering
description: Siemens TIA Portal, STEP 7, WinCC, PLC, PROFINET, drive, HMI, diagnostics, commissioning, and safety engineering guidance. Use when the task concerns engineering or troubleshooting a TIA Portal project or Siemens automation equipment.
---

# TIA Portal Engineering

For this workspace, interpret `TIA` as `TIA Portal V21`. Preserve explicit
version references when compatibility or installed API details matter.

Use a disciplined engineering workflow. The goal is a correct change that can
be reviewed, compiled, tested, and commissioned without surprising effects on
the plant.

## Establish Context

Collect or infer from project files:

- TIA Portal major version and update level.
- CPU family, order number, and firmware version.
- PLC language and software structure: OB, FB, FC, DB, UDT, libraries, and
  naming conventions.
- Network topology, fieldbus protocol, remote I/O, drives, HMI, and external
  communications involved.
- Whether work is offline only, connected online, or intended for download.
- Safety classification and the required approval/test procedure.

Do not substitute hardware variants or firmware versions based on a similar
device. Hardware catalog properties and available instructions depend on the
installed TIA version and device firmware.

## Engineering Workflow

1. Define the required observable behavior, operating modes, failures, and
   acceptance criteria.
2. Locate the owning program block, tag table, technology object, device, or
   HMI screen before changing anything.
3. Preserve interface contracts and data layout unless a migration is planned.
   Check every call site, HMI connection, alarm, recipe, external client, and
   retained-data implication of an interface change.
4. Implement clear state handling, initialization, interlocks, fault handling,
   and manual/automatic behavior appropriate to the application.
5. Compile the affected PLC/HMI configuration and resolve warnings that could
   indicate a behavioral mistake.
6. Test offline where possible, then define an explicitly authorized
   commissioning sequence with expected signals and rollback steps.

## PLC Quality Rules

- Prefer symbolic tags and typed structures over raw addresses and magic
  constants.
- Define units, ranges, scaling, invalid-value behavior, and quality/status
  handling at boundaries.
- Use edge detection and explicit state machines for command-driven sequences;
  avoid relying on scan-cycle timing implicitly.
- Ensure timer, counter, and instance DB behavior is intentional during restart
  and fault recovery.
- Treat asynchronous communication and device diagnostics as stateful; handle
  busy, done, error, timeout, and retry paths.
- Document non-obvious process assumptions next to the applicable block or
  configuration.

## Troubleshooting

Separate evidence from hypothesis. For diagnostics, request or inspect:

- Compile errors and full diagnostic-buffer events, including timestamps.
- Online/offline comparison results and block consistency.
- CPU operating state, protection level, and reachable-device details.
- Module/drive diagnostic status, network topology, and relevant LED states.
- Signal values, watch-table results, or traces around the fault event.

Give a minimally invasive diagnostic sequence first. Do not recommend forcing,
downloading, CPU state changes, or clearing memory as default troubleshooting
steps.

## Safety And Plant Impact

Offline edits can become plant-impacting after download. Before any online
operation, identify the affected assets, current operating mode, safe state,
authorization, backup/rollback mechanism, and validation method. Safety PLC
and F-device changes require the user to follow their approved safety process;
do not represent an automated change as safety validation or acceptance.
