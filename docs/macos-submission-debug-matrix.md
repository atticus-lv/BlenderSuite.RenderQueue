# macOS Submission Debug Matrix

Use the non-AOT app as the baseline when validating Blender submission behavior.

## Package Under Test

- App variant:
- RID:
- App path:
- Blender extension version:
- Blender path:

## Scenario A: Cold Start Submit

- Desktop app initially not running:
- Blender submit returns:
- `submission_endpoint.json` appears within:
- Task appears in UI:
- File properties auto-load:
- Crash report generated:
- Notes:

## Scenario B: Warm Submit

- Desktop app already running:
- Blender submit returns:
- Task appears in UI:
- File properties auto-load:
- Crash report generated:
- Notes:

## Scenario C: Immediate Refresh

- Right-click refresh after submit:
- Refresh succeeds:
- Crash report generated:
- Latest session log excerpt:
- Notes:

## Scenario D: Cold Start With Existing Queue

- Existing persisted tasks present:
- New submitted task appears:
- Existing queue state preserved:
- File properties auto-load:
- Crash report generated:
- Notes:

## Evidence Checklist

- Latest `submission_endpoint.json`
- Latest session log
- Latest `DiagnosticReports` entry
- Screenshot or screen recording if UI timing is suspect
