# STAGE 24 — Publications & Markdown Reports

> This stage requires Stage 23 Storage Spaces. It adds a user-owned publication register and
> Markdown reporting. It is intentionally generic: Microsoft MVP reporting is one useful report
> profile, not a product-specific data model.

## 1. Mission

Let a user record published work, review it by period and theme, and export a concise Markdown
report. The stage records publication metadata, not publication content. It reuses the selected
Stage 23 storage space so one person can keep personal, editorial, or professional data separate.

## 2. Locked product decisions

| # | Decision |
|---|---|
| S24-D1 | The data entity is `PublicationRecord`; the generated artifact is `PublicationReport`. Neither contains “MVP” in its identifier, schema, UI text, or required fields. |
| S24-D2 | A record has a stable id and a mutable `periodKey`. `periodKey` is a user-owned classification such as `2026` or `FY2026`; it is not a primary key and may differ from the calendar year of `publishedAt`. |
| S24-D3 | Publication data has no automatic retention purge. The user may filter, export or delete records for a chosen period or storage space. Deletion is explicit and recoverable through the existing recycle-bin policy only when applicable; it is never a scheduled purge. |
| S24-D4 | Store no article, blog, newsletter, or social-post body. Store title, public URL, type, date, language, bounded short summary, role, status, eligibility, period and tags. |
| S24-D5 | `LinkedInPost` and `BlogArticle` are distinct explicit values. The application never infers a publication type solely from an URL. |
| S24-D6 | A user selects the active storage space before creating, viewing, exporting, or deleting publications. A report never combines spaces unless a future explicit cross-space feature is added. |
| S24-D7 | The UI is simple data entry and filtering; it does not attempt to become a general CRM, social-media publisher, browser automation client, or content editor. |
| S24-D8 | Markdown export is local, explicit, bounded, UTF-8 and uses the existing authorized report/output conventions. It never contacts LinkedIn, a blog platform, or an external service. |
| S24-D9 | A workflow may read publication data through the future Stage 19 `sqlite.query(storageSpaceId)` bridge, but Stage 24 exposes no raw SQL editor and no workflow write tool. |

## 3. Data model

Add the following public enum and immutable record contracts with English XML documentation:

```csharp
public enum PublicationType
{
    LinkedInPost,
    BlogArticle,
    Newsletter,
    Video,
    Podcast,
    Presentation,
    Other
}

public enum PublicationStatus
{
    Draft,
    Published,
    NeedsVerification
}

public enum PublicationRole
{
    Author,
    CoAuthor,
    Contributor
}

public enum PublicationEligibility
{
    Include,
    Review,
    Exclude
}
```

`PublicationRecord` contains `Id`, `PeriodKey`, `Title`, `Url`, `PublicationType`, nullable
`PublishedAt`, `Language`, `ShortSummary`, `Status`, `Role`, `Eligibility`, ordered tags,
`CreatedUtc`, and `UpdatedUtc`. Use UTC for timestamps and retain the user-entered publication
date’s local calendar date without silently converting it into another date.

Validation rules:

- Id is application-generated and immutable.
- `periodKey` is 1–32 visible characters after trim; it is not normalized to a calendar year.
- Title is required and limited to 300 Unicode scalar values.
- URL is optional for a draft and required for `Published`; it must be absolute HTTP/HTTPS when
  supplied. A canonical URL is unique within one storage space.
- Short summary is optional and limited to 600 Unicode scalar values.
- Language is optional BCP-47 text limited to 35 characters.
- Tags are unique case-insensitively, individually 1–64 characters, ordered by first entry, with
  a maximum of 20.

Stage 23 must create and migrate these stable tables in every storage space:

```text
publication_records(
  publication_id, period_key, title, canonical_url, publication_type,
  published_local_date, language, short_summary, status, role, eligibility,
  created_utc, updated_utc)

publication_tags(publication_id, tag)
```

Add indexes for period/date/type/status/eligibility and a unique non-null canonical URL index.
Do not use an entity-attribute-value table, generic JSON blob, unbounded notes field, or a schema
whose meaning depends on an MVP program.

## 4. Repository and workflow visibility

Add `IPublicationRepository` with typed create, update, delete, get and filter operations. All
database access is parameterized, cancellable, transactional for a multi-table change, and scoped
to one resolved `StorageSpaceSnapshot`.

Expose fixed columns directly to the future Stage 19 managed-space read-only query mode. Document
the two table names and columns in the README. Database writes remain exclusively behind
`IPublicationRepository` in this stage.

The repository may attach an optional Stage 23 research-document id as provenance internally, but
the UI must allow a publication with no research origin. A discovered external document never
automatically creates a publication record.

## 5. User interface

Add a navigation page **Publications** with an active storage-space selector. It contains:

- a compact period selector, defaulting to the current calendar year text;
- optional filters for tag, publication type, status and eligibility;
- a read-only ordered list with date, type, title, tags, eligibility and public-link command;
- **New**, **Edit**, **Delete**, **Clear filters**, and **Export Markdown** commands;
- an empty-state explanation which distinguishes “no records in this space” from “filters hide
  all records”.

New and Edit open one accessible, keyboard-friendly modal dialog. Use normal form controls,
multi-line text only for the short summary, and a tag entry control that does not require comma
escaping. The dialog uses the active application theme, validates locally before Save, preserves
unsaved values on validation failures, and never sends values to an LLM.

Delete requires a confirmation naming the selected record title and storage-space display name.
It deletes only the selected record and its tags in one transaction. Bulk period deletion is out
of scope for the first UI, but the repository and future Settings tooling must support an explicit
period-scoped deletion operation without raw SQL.

## 6. Markdown export

`PublicationReport` is generated from the current storage space and active filters. It has a
deterministic heading, selected period/filter summary, generation time, total count, and sections
grouped by tag then publication type. Within a section, sort by publication date descending then
title using ordinal case-insensitive ordering.

Each published record produces a compact bullet containing date, type, title, optional summary,
role, and Markdown link. Drafts are exported only when the user explicitly includes the Draft
status filter. Escape Markdown text and URLs safely; a missing URL is rendered as plain text.

The export destination uses a Save dialog and an explicit existing-file overwrite choice. Write
UTF-8 with LF line endings. Cancellation leaves no partial destination file. The output file is a
user-requested artifact, not a hidden database backup.

## 7. Privacy, observability and documentation

- Publication titles, URLs, summaries, tags and dates are personal data. Do not log, incident
  report, telemetry-report, or place them in a snackbar beyond the user-visible current action.
- The page must explain that records are local to the selected storage space and do not publish
  anything to a social platform or blog.
- Update README and First Run guidance with storage-space selection, retention distinction,
  publication data fields, Markdown export and the fact that a publication type is user-selected.

## 8. Tests and gate

Add deterministic tests for validation, canonical URL uniqueness, tag normalization/order,
period filtering, storage-space isolation, all enum values, create/edit/delete atomicity,
no-auto-purge, Markdown escaping/order/grouping, cancellation, overwrite behavior, UI command
state, keyboard navigation, dark and light active-theme rendering, and content-free logging.

Run:

```text
dotnet build OD.MyLocalAgent.sln --configuration Release --no-restore -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --no-restore -m:1
```

Update `docs/ARCHITECTURE-LEDGER.md`, the shared specification addendum, README, First Run guide,
theme audit, and a synthetic Markdown-report sample. No real publication, account, URL, or
personal contribution is used by tests or samples.
