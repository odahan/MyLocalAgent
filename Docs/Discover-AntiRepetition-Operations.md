# Discover anti-repetition — operations and deployment

## Delivered contract

The revised `editorial-watch-daily-db` workflow is stored at
`Docs/samples/editorial-watch-daily-db.json`. It replaces optional model tool calls with this
mandatory sequence:

`discover → structured selection → final novelty validation/reservation → structured drafting → deterministic report preparation → immutable file write → export confirmation`.

The final validator resolves URLs from collected `hitId` values, retains all selected sources,
checks historical exact identities without an expiry window, compares compatible embeddings in
the configured semantic window and inside the current batch, and fails closed as `Uncertain` when
semantic coverage is incomplete. A known release URL is accepted only for a declared material
update with a distinct version or date and at least one claimed new fact.

SQLite schema version 2 is additive and transactional. Existing `editorial_proposals` identifiers,
timestamps, content, and `used` values are retained. An upsert can set `used` to true but cannot
clear an existing true value. Supplemental tables separate event metadata, sources, facts, runs,
run decisions, export state, and import provenance. Embeddings are identified by model, dimension,
and projection version.

## Deployment status

The active `C:/OD.MyLocalAgent/.od-my-local-agent/tasks/editorial-watch-daily-db.json` file was
synchronized with the validated repository sample for version 2.5.1 on 2026-09-05. Its previous
definition is retained beside it with a `.json.bak` extension so the task loader cannot interpret
the backup as another task. Logs created before this deployment identify application version
2.5.0. `Daily Gmail analysis ODMLA` and the disabled legacy `editorial-watch-daily` workflow remain
outside scope.

## Safe historical import rehearsal

Do not start against the active database. First stop or disable the scheduled editorial workflow,
then copy both the database and historical report tree into a dedicated rehearsal directory. Keep
an untouched timestamped backup of the database, including any `-wal` and `-shm` files copied while
the application is stopped.

On the rehearsal copy:

1. Run `sqlite.createProposalDatabase` once to apply the additive schema migration.
2. Run `sqlite.importEditorialHistory` with `dryRun=true`.
3. Review every `Ambiguous` item. The importer never auto-merges it and does not treat diagnostic
   sections as subjects.
4. Run the import with `dryRun=false` only after review.
5. Run it a second time. `importedCount` must be zero.
6. Verify that historical `created_at_utc` values use report dates, existing `used=true` values
   remain true, and all available canonical source URLs appear in `editorial_proposal_sources`.
7. Run `sqlite.rebuildEditorialEmbeddings` with `dryRun=true`, review the count, then apply it on
   the rehearsal copy. A second dry run must report no required reconstruction.

The importer cannot reconstruct reports that were overwritten. Existing rows and logs are partial
provenance and must be reported as such; no missing prose should be invented.

## Deployment order

1. Back up the active workflow, database, and report directory while the scheduler is stopped.
2. Deploy the newly built runner before the workflow JSON; the revised workflow uses new SQLite
   aliases and `{{workflow.runId}}` placeholder contract version 4.
3. Apply `sqlite.createProposalDatabase` to the active database and validate schema version 2.
4. Replace only `editorial-watch-daily-db` with the reviewed repository sample.
5. Re-enable scheduling and run one supervised execution.
6. Confirm that the run reaches `Exported`, the immutable report name includes its run id, and the
   recorded SHA-256 matches the file.

If any step fails, keep scheduling disabled, restore the backed-up database and workflow, and
restore the matching runner version. A run left at `Reserved` or `ReadyToExport` is intentionally
visible and recoverable; do not delete its rows to make the next run appear successful.

## Verification and limits

CI uses deterministic embedding doubles. The annotated corpus is in
`tests/OD.MyLocalAgent.Tests/Fixtures/discover-anti-repetition-corpus.json`. It covers the observed
NativeAOT, Copilot, and cross-site C# union-type repetitions plus a true release evolution and an
ambiguous case. These tests demonstrate orchestration and conservative decision rules, not
universal semantic recognition. No local model evaluation or production-data mutation is performed
implicitly.
