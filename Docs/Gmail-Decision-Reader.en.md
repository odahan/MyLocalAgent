# Gmail decision reader — global engine contract

The opt-in workflow reader collects Gmail once, classifies bounded messages sequentially through the
common global decision service and returns source-free coverage. `gmailDecision.renderReport` renders
Markdown deterministically; it performs no model, connector or file call. Existing protected Gmail
readers keep their normal contract. No production schedule is rewritten.

## Configuration and authoring

Enable Gmail decision reader and grant the task `gmailDecision` separately. Choose **Use application LLM**
or **Use a decision model** in **Settings → Decision models**. Generic enablement is independent.
One configuration/provider/model snapshot applies to the whole workflow/batch. The central local
model retains ordinary residency; the native dedicated model uses bounded ownership-aware cleanup.
Raw mail never enters a cloud provider, even through remotely backed Ollama aliases. Incompatible
configuration is reported before raw-source inference, without selecting another provider.

Required arguments are `questions`, `reportMapping` and **`contractVersion: 2`**. Named questions support
choice (2–24 options), noul (Boolean), and score (2–10 ordered levels), 1–8 questions per request.
Question descriptions/keys and report policy are literal; previous mail output cannot author policy.
Optional collection fields: selection Today/Unread/Recent, daysBack for Recent only (0–3650),
maxMessages within Settings/protection limits, and batchTimeoutSeconds to shorten the complete deadline.
See the [disabled sample](samples/gmail-decision-watch.json) and
[Technical Manual](Technical-Manual.html#gmail-decision) for executable syntax.

Native `acceptance` uses historical percent units with perQuestion/perChoice overrides and may only
tighten global thresholds. Central mode rejects every explicit acceptance object. Central noul answers
are semantic Booleans; rubric answers are zero-based levels, not native probability-weighted expected
scores. Nondefault replyTrueAtPercent probability mappings must be reviewed for central mode.

## Deliberate legacy migration

Version 2 is a migration acknowledgement, not an automatic conversion. Missing/older contractVersion
blocks execution with a migration error. Review each authored requirement before setting it to 2:

1. Choose the global engine. Hybrid is compatible only with NativeDecision; LlmOnly only with CentralLlm.
   Remove classificationMode in new steps. A legacy selector never overrides Settings.
2. Remove arbitration and unavailableBehavior explicitly, including false/Fail values. Historical
   Settings fields remain persisted and inactive. There are no second opinions or fallback calls.
3. Review native acceptance, alwaysArbitrateChoices and probability-based cross-question rules. Do not
   discard numeric requirements silently; retain the native engine or deliberately edit the workflow.
4. Preserve selection, limits, named questions, report labels/ranks and independent permissions.
5. Check output consumers for schema 2, engine, capabilities and null scores. No scheduled task is saved
   or rescheduled by migration code. Editing/saving does not run a model.

## Coverage and reports

Output schema 2 records `engine`, `capabilities.scores`, `acceptanceBasis`, `provider`, the selected
`effectiveModels.decision` and `classificationMode: Global`. Native basis is nativeThresholds;
central basis is validatedChoice. Central scores remain absent/empty; no distributions are invented.
Each collected row remains present: Classified is resolved, Provisional retains valid native answers
requiring review, Unresolved has no valid answer or ran out of time. Central semantic review options
are ordinary choices. Technical failure never becomes an ordinary category.

Counts satisfy classified + provisional + unresolved = collected. Historical arbitration/fallback
counters remain zero in schema 2; directLlmResolved and classificationMs describe central calls.
The deterministic report accepts archived schema 1 and schema 2, validates types/identities/counts,
escapes display text and explains basis, capability, omissions and warnings. includeNoise=false hides
rank-zero rows while counting them. Empty collection renders **No messages found**.

Collection and complete deadlines remain bounded. The common adapters budget full prompts; a native
source exceeding the conservative 2050-token limit is unavailable, without invoking the central model.
Diagnostics log identities, question hash, status, sanitized reasons and timing, not source bodies.
Outer workflow Retry repeats collection and evaluation at the user's authored policy; it is never
an internal alternate-engine mechanism. Tests use fake responses. Live mail/model quality evaluation,
download, deployment and publication require a separate request.

See the [common engine guide](Decision-Evaluation.en.md),
[phase C evidence](evaluation/Stage32C-Results.en.md) and
[Ollama structured output contract](https://docs.ollama.com/capabilities/structured-outputs).
