# CouncilAudit engine

The rules engine behind a public audit of UK council spending-over-GBP-500 publications. It reads the spending files councils
publish and flags rows that look like repeated or mismatched payments, then tries to explain each flag by a pattern a reader
can check (a standing monthly payment, a VAT rounding, a reversed entry, a redacted line).

**Licence: source-available under the PolyForm Noncommercial License 1.0.0. It is not open source and it is not licensed for
commercial use.** See `LICENSE` (a short preamble, then the PolyForm text unchanged). Noncommercial use, including research,
study, verification of the results, charities, public bodies and councils' own audit work, is permitted. Commercial use, resale
and hosted or paid audit services are not. The preamble also gives journalists and verifiers an explicit extra permission to
check the engine and publish what they find, even at a commercial publisher.

**What this licence does not give you.** It covers only the files in this repository. It grants no right to any
`EvaluatedApplications.*` package. The library and tests here need none. The command-line tool and a prototype that use
`EvaluatedApplications.HoloDb` are not in this repository; running software that depends on that package needs its own right under
the package's terms.

**A flagged row is an exception to be checked, not a finding.** Councils publish payer-side data only. Repeated amounts are often
legitimate (standing payments, per-resident care invoices, catch-up months). The engine reports exceptions, discrepancies and
unreconciled amounts in published files. Nothing here says anyone made an error or did anything wrong, and the engine has no
way to tell.

## What is here

| Path | What it is |
|---|---|
| `src/CouncilAudit/` | The engine library (`net10.0`, no package references, browser-safe: bytes and rows in, plain objects and strings out). |
| `src/CouncilAudit.Tests/` | The regression tests (xunit). Fixtures are small synthetic or hand-reduced rows in code; no council data files are needed. |
| `CHECKLIST.md` | The error categories the engine is held to (19, numbered C01 to C20; there is no C19), what each guards and how it fails. |
| `check_rules.csv` | The rule registry: every filter and threshold the scans apply, in plain words, so a reader can say "this rule is wrong". |
| `PREREG_*.md` | Pre-registrations: predictions and nulls written before each run, with the wrong predictions left in. |
| `AUDIT_HISTORY.md` | What the independent audits found, what was fixed, and what is still open. |
| `LICENSE` | Preamble plus PolyForm Noncommercial 1.0.0. |

Not here: the command-line runner that reads council files and writes the exports, raw council downloads, the exports and the
tables derived from them. Documents such as `CHECKLIST.md` and the `PREREG_*.md` files describe the author's working folder and
mention paths and commands (`scratch/`, `inbox/`, `export/`, `checklist all`, `export <slug>`) from the runner. Those paths and
commands are not part of this repository; they are kept as written so the record is not rewritten after the fact. The rules the
checklist applies are in `src/CouncilAudit/ChecklistRules.cs` and are tested here.

The council data and the derived tables are published separately on the Evaluated Applications site
(https://evaluatedapplications.github.io) under the councils' own licences (typically the Open Government Licence) and the
licensor's terms. They are not covered by this software licence.

## Redactions

Private individuals' names that appear in council data (landlords, sole traders, performers) have been removed from this public copy. In prose and code comments they read "[individual's name withheld]"; in test fixtures they are replaced by synthetic stand-ins. The author keeps the unredacted originals privately. A Markdown file that was changed this way says so in a note at its end; in source files the marker itself is the only notice. Names of companies and other organisations are generally kept. If you find a person's name anywhere in this repository, see "Report a wrong rule" below.

## Build and test

You need the .NET 10 SDK. From the repository root:

```
dotnet test src/CouncilAudit.Tests
```

The test project restores only Microsoft.NET.Test.Sdk, xunit and xunit.runner.visualstudio. The library has no package references.
## Report a wrong rule

If a rule in `check_rules.csv` or `CHECKLIST.md` is wrong, or a test fixture is not what the council published, open an issue on
this repository naming the rule or test, the council and month, and what the published file shows. Issues are read, but no
response time is promised, and a report may be answered by correcting the record rather than the code. A fix is welcome as a
pull request with a test that fails before and passes after. Pull requests may be declined or reworked. A contribution is accepted under the same licence
terms as the rest of this repository. Please do not put personal data in an issue: if you find a name that should have been
withheld, say which file and row by number and do not paste the name.

## Licensor and authorship

Licensor: Dongyang Stephen Chen, trading as Evaluated Applications.

Written by an autonomous AI agent (the "virtual-customer agent") working for the licensor. The licensor is responsible for what is published here.
