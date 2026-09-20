# Code Cleanup Review

Perform a read-only review of the codebase for structure, cleanliness, and maintainability.

Look for:
- Dead or unused code
- Duplicate or near-duplicate code
- Misplaced code or responsibilities
- Large classes/files that contain multiple distinct responsibilities and should be split
- Redundant abstractions or unnecessary indirection
- Inconsistent organization, naming, or file placement
- Obsolete helpers, wrappers, comments, or compatibility code
- Logic that belongs in an existing shared component instead of being repeated locally

Prefer deletion and simplification when describing recommended changes. Avoid proposing new
abstractions unless they clearly reduce duplication or misplaced responsibilities.

Do not evaluate feature behavior, redesign features, optimize performance, or propose unrelated
architectural changes.

Produce a concise report containing:
- The finding
- Relevant file(s) and code location(s)
- Why it should be cleaned up
- A recommended action
- Any uncertainty or risk involved

Do not make any code changes - only create or modify the report itself.

Save the report to:

`docs/reports/task-code-cleanup-YYYYMMDD.md`
