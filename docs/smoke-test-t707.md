# T-707 Frontend Smoke Test Report

| Property | Value |
|---|---|
| Test date | 2026-10-27 |
| Tester | Claude Code (automated checks) + developer manual browser verification |
| Environment | Windows 11, .NET 8, SQL Server (local), Angular 20 |
| Backend | Running on http://localhost:5214 (100,000 seed rows) |
| Angular build | Production build (`ng build --configuration production`) — succeeded, 0 errors |
| Branch | master |
| Commit | ea86ead |
| Status | **COMPLETE — all 19 test cases PASS** |

---

## Method

Each test case was checked by one or more of:

- **API**: Direct HTTP call to the backend and assertion on the response.
- **Static**: Code review of the Angular component/template to confirm the logic is correctly implemented.
- **Manual**: Developer personally verified the behavior in a running browser session (`http://localhost:4200`).

---

## Test Cases

| # | Test case | Method | Result | Evidence |
|---|---|---|---|---|
| TC-01 | List loads with default sort (CreatedAt DESC) | API + Static | **PASS** | API: `totalCount=100000`, 5 items returned; `DEFAULT_FILTERS` has `sortBy=createdAt, sortDirection=desc` |
| TC-02 | Status filter: select each value | API | **PASS** | `?status=New` → 25,010 items, all `status=New`; filter wired via `(change)="onImmediateChange()"` |
| TC-03 | Priority filter combined with status | API | **PASS** | `?status=InProgress&priority=High` → 8,331 items, all `status=InProgress, priority=High` |
| TC-04 | OrganizationName filter (StartsWith) | API | **PASS** | `?organizationName=Acme` → 5,034 items, all `organizationName="Acme Corp"`; prefix match confirmed |
| TC-05 | AssignedTo filter (exact match) | API | **PASS** | `?assignedTo=alice` → 7,905 items, all `assignedTo=alice` |
| TC-06 | Date range inclusive bounds | API | **PASS** | `?createdFrom=2024-01-01&createdTo=2024-01-01` → 144 items on 2024-01-01; exclusive-upper-bound fix (BF-3) confirmed |
| TC-07 | Free-text search; debounce fires once per settled input | API + Static + Manual | **PASS** | API: `?search=Corp` → 5,034 items ✓. Static: `debounceTime(300)` in `textChange$` pipe ✓. **Developer verified in browser DevTools: only one non-cancelled request per settled input** |
| TC-08 | Sort by Created / Priority / Title; toggle Asc/Desc | API + Static | **PASS** | `sortBy=title&sortDirection=asc` → alphabetical order ✓; `sortBy=priority&sortDirection=desc` → all High ✓; toggle button calls `toggleSortDirection()` ✓ |
| TC-09 | Pagination: multi-page nav; page resets to 1 on filter change | API + Static | **PASS** | page=2 returns correct slice ✓; deep page 500 returns 20 items, totalPages=5,000 ✓; `buildParams(1)` called on filter change ✓ |
| TC-10 | Empty state: no-results message | API + Static | **PASS** | `?search=XYZXYZ_NOTFOUND` → `totalCount=0, items=[]` ✓; template renders "No requests match the current filters." ✓ |
| TC-11 | Loading state: spinner visible during fetch | Static + Manual | **PASS** | Static: `startWith({ kind: 'loading' })` emits before API response; spinner template present ✓. **Developer verified spinner visible with Slow 3G throttling** |
| TC-12 | Error state: message + Retry button; Retry restores list | Static + Manual | **PASS** | Static: `catchError` → `{ kind: 'error' }`; message and Retry button in template ✓. **Developer verified: stopping backend shows error message; Retry successfully restores the list** |
| TC-13 | Single-row status update: spinner; row updates inline | API + Static + Manual | **PASS** | API: PATCH `91047` `New→InProgress` → HTTP 200, new rowVersion ✓. Static: `rowPatches[id]` updated on success; `row-spinner` shown during update ✓. **Developer verified: spinner visible, row updates inline without page reload** |
| TC-14 | 409 Conflict: two-tab scenario, conflict message + refreshed data | API + Static + Manual | **PASS** | API: stale rowVersion → HTTP 409, `currentStatus` and `currentRowVersion` returned ✓. Static: 409 handler sets `rowState[id].error` ✓. **Developer verified two-tab scenario: second tab shows "הפנייה עודכנה על ידי משתמש אחר" and row refreshes to current server state** |
| TC-15 | Completed requests cannot transition; 400 blocked at UI level | API + Static + Manual | **PASS** | API: `Completed→InProgress` → HTTP 400 ✓. Static: `ALLOWED_TRANSITIONS['Completed'] = []` disables select ✓. **Developer verified: Completed-row selects are disabled in the UI; no 400 can be triggered through normal interaction** |
| TC-16 | Summary panel: counts correct; refreshes after status update | API + Static + Manual | **PASS** | API: `byStatus` total = 100,000, `byPriority` total = 100,000 ✓. Static: `summaryRefresh$.next()` wired ✓. **Developer verified: Summary panel counts update immediately after an inline status change** |
| TC-17 | History panel: History button expands, shows correct fields | API + Static + Manual | **PASS** | API: GET `/api/requests/91047/history` → `New→InProgress, changedBy=smoke-tester` ✓. Static: `toggleHistory()` renders history table ✓. **Developer verified: panel expands on click and displays From / To / Changed by / When** |
| TC-18 | History empty state: "No status changes recorded yet." | API + Static + Manual | **PASS** | API: GET `/api/requests/1/history` → `[]` ✓. Static: `items.length === 0` → `{ kind: 'empty' }` ✓. **Developer verified: newly-seeded request shows empty history message, not an error** |
| TC-19 | History cache invalidation after status update | API + Static + Manual | **PASS** | API: GET history after PATCH returns new entry ✓. Static: `delete this.historyState[id]` + `loadHistory(id)` on success ✓. **Developer verified: history panel auto-refreshes and shows new audit entry when panel is open during a status update** |

---

## Summary

| Result | Count | Test cases |
|---|---|---|
| **PASS** | **19** | TC-01 through TC-19 |
| FAIL | 0 | — |

All 19 acceptance criteria met. Zero failures.

---

## Count correction note

The initial partial report (before developer manual verification) stated "11 of 19 test cases require a browser to complete." The correct number was **10**: 8 PARTIAL cases (TC-07, TC-13–TC-19) + 2 NOT VERIFIED cases (TC-11, TC-12). The summary table row for PARTIAL listed only 8 IDs while showing count=9 — a transcription error in the initial draft. No test case was omitted; TC-09 was correctly PASS throughout. The developer subsequently verified all 10 outstanding cases, making all 19 PASS.

---

## Known Limitations

| Limitation | Note |
|---|---|
| No automated Angular tests | Not required by the exam. Angular behavior verified manually. |
| `Completed` status rows show a disabled select | Correct behavior — no allowed transitions. The UI cannot trigger a 400 from normal interaction. |
| `changedBy` field hardcoded to `'user'` in `onStatusChange()` | Known limitation documented in decisions.md and README. No authentication. |
