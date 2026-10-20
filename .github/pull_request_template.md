## Problem and change

<!-- What happens before/after? Link related issues. Explain why this approach fits. -->

## Impact

<!-- Affected APIs, consumers, platforms, deployment modes and adjacent behavior.
Describe compatibility or migration effects and remaining risks. -->

## Validation and real-device evidence

<!-- Read CONTRIBUTING.md. Code/behavior changes need real-device evidence;
missing or insufficient evidence may result in rejection even when CI passes.
Repeat the device row for additional testers/platforms. Label CI, VM, container,
headless and physical-device runs accurately. Specific PC specifications and
device/CPU/GPU/monitor model names are not required; omit them by default.
Remove user-identifying details from the body, diff, logs, images and artifacts.
If disclosure is ambiguous, agree with the user before publishing and keep that
agreement in approved private local storage, outside the repository. -->

| Evidence | Details |
| --- | --- |
| Tested commit SHA | |
| Execution type (physical/VM/headless), OS and architecture | |
| SDK/runtime, WebView/native versions and deployment mode | |
| Reproduction steps / exact commands | |
| Expected and observed results | |
| Regression, failure and cleanup checks | |
| Untested platforms/scenarios or known failures | |

<!-- For documentation-only or other non-runtime changes, explain why device
execution is not applicable and what you checked instead. Do not invent a run
or check a box based on an agent's claim. Maintainers review exceptions. -->

## AI assistance and review

<!-- State tools used and their role, or "None". Explain what you personally
reviewed and verified. Full prompts or chat transcripts are not required.
These checklist items record internal review steps and their limits; they are
not warranties, support commitments or an acceptance of additional liability.
See CONTRIBUTING.md#maintainer-workflow. External PRs are not accepted. -->

- [ ] I reviewed the complete diff and described the affected paths and any limits of that review.
- [ ] I reviewed AI-assisted content using the same criteria as other changes and identified any unverified parts.
- [ ] I supplied verification evidence or an explicit non-runtime justification.
- [ ] I described the test and documentation coverage for the changed behavior and identified gaps and unverified claims.
- [ ] Borrowed material retains its source attribution and required notices.
- [ ] I reviewed the outgoing body, diff and evidence for user-identifying information and omitted unnecessary environment details.
