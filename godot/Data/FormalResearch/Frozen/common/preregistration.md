# Formal Experiment Preregistration — Current

> Status: **Preregistered replacement RQ1. The live prompt-development gate and complete RQ2 Summary batch are retained; the 30-block RQ1 deterministic preflight has passed. Replacement collection begins only from the committed clean revision and its matching freeze bundle.**
>
> This repository has one current formal experiment and does not assign numbered experiment-version labels. Reproduction identity comes from the clean Git revision, canonical asset hashes, prompt/tool/model-setting hashes, and the freeze-bundle hash.

## 1. Purpose and boundaries

The study evaluates two independent questions in the same 25-NPC Brackenford town:

- **RQ1:** Does agent-centric or event-centric L2 admission better allocate a shared, scarce L2 budget under simultaneous town pressure?
- **RQ2:** After an L2 need has already been admitted, does relevance-selected memory summary preserve grounded decisions while reducing context compared with verbatim memory?

RQ1 and RQ2 are not a factorial experiment. RQ2 begins only after admission and must not change scheduler semantics, candidate discovery, Authority truth, available actions, or the hidden answer.

The formal path uses English prompts and the configured live DeepSeek service. Mock, recorded, oracle, demo-trigger, or manually authored model outputs are not formal evidence.

## 2. Shared frozen rules

- Population: 25 NPCs.
- The 30 replacement RQ1 pairs may run concurrently on 30 fixed workers. The two conditions and four calls within each pair retain their counterbalanced serial order on the same worker. RQ2 execution remains serial and is not rerun for the replacement RQ1 collection.
- Provider timeout: 300 seconds per call.
- Missing or transport-failed responses are attempted at most three times for the exact same frozen request, with fixed 5-second and 15-second backoffs. Every attempt is a fresh stateless HTTP request; it never continues a previous response. This includes every typed `TransportFailure`, including an incomplete or unusable Provider envelope. Once a response passes transport/envelope decoding, protocol validation, Validator, Authority outcome, or answer quality never causes a retry.
- Output ceiling: 16,384 tokens. Reaching this ceiling is classified as `OutputTokenLimitReached` and receives the same at-most-three fresh attempts. Exhaustion remains a sealed failure and is reported as a token-consumption anomaly; the ceiling is a transport safety limit, not a study-wide token quota.
- Condition order: deterministically counterbalanced inside matched pairs.
- Each treatment sees the same actor-visible world facts and action catalogue for that matched pair.
- The model proposes typed actions; Validator and Authority remain the only writers of canonical world state.
- Hidden expected answers are unavailable to prompt construction, memory selection, summary generation, and the model.
- Provider service-version tracking is not a study requirement. The exact model settings and returned usage metadata are retained as diagnostics.
- Every physical Provider attempt retains a credential-free diagnostic sidecar containing the exact request JSON, exact response body when one was received, Provider-returned thinking blocks, latency, failure kind, and all returned usage fields. Hidden Provider reasoning that is not returned by the service cannot be observed or claimed.
- There is no experiment-wide token quota and no token-based early stopping. Actual input, cache, output, reasoning-detail, retry, latency, and cost values are measured results.
- Cost estimates are diagnostics only and are not outcomes.

Every condition is sealed even when it ends in transport, protocol, validation, or Authority failure. An exhausted or non-retryable invalid pair cannot be silently replaced.

## 3. Shared prompt-development gate

Before freezing formal assets, the shared planner prompt is tested against the real provider. This gate may improve only treatment-neutral protocol compliance. It must not mention hidden answers, RQ conditions, fixture-specific choices, or preferred actions.

The shared prompt requires the model to:

- emit exactly one JSON object that follows the supplied closed schema;
- include every required field and omit undeclared fields;
- avoid copying actor, plan, step, or request metadata unless that field is declared by the schema.

The development set is separate from all formal fixtures. It contains the six RQ2 strata at the two highest load tiers, with two repeats and both memory treatments:

`6 strata × 2 development tiers × 2 repeats × 2 treatments = 48 live calls`

Gate: **48/48 calls must produce a non-failure typed decision.** The development outputs are engineering evidence only and are never included in RQ scores.

## 4. RQ1 — shared-budget admission

### 4.1 Treatments

- **AgentCentric:** rank queued L2 needs using actor-centred urgency and fairness signals.
- **EventCentric:** rank the same queued L2 needs using event/dependency impact signals.

Both treatments receive the same simultaneous snapshot of ten pressure cases. The logical L2 budget is `B = 4`; therefore each condition makes four live model calls after its scheduler selects four needs.

### 4.2 Sampling plan

The replacement suite contains 30 distinct public town-context blocks and no repeated block. Each block contains
ten actor-distinct pressure cases and uses one common 240-tick admission window. The fixed matrix varies the
AgentCentric/EventCentric top-four overlap across three tiers: zero, one, and two shared admissions, with ten
blocks in each tier.

`30 distinct blocks × 1 matched pair = 30 matched pairs`

Each pair contains eight live calls, so replacement RQ1 requires **240 formal calls**. One distinct block is the
independent analysis unit; the ten pressure opportunities inside it are not reported as 300 independent samples.

### 4.3 Outcomes

The primary outcome is correct grounded terminal success among the ten opportunities in each treatment and block,
evaluated from typed terminal receipts and the hidden test-case ledger. A correct terminal is either the expected
Authority commit or an explicitly expected justified defer. Unproductive admitted sessions are a secondary
explanation measure. Further diagnostics include actor coverage, protocol failure, latency, and token usage.
Token diagnostics are reported for every matched block and pressure case, comparing AgentCentric with
EventCentric, and as treatment-wide totals. Retry attempts count toward the treatment that caused them. A
pressure not admitted under `B=4` has zero Provider attempts and is labelled `missed_due_to_budget`; a no-response
attempt has unknown usage rather than zero usage.

The minimum meaningful absolute paired difference is 0.15. Point estimates, paired uncertainty intervals, and all invalid-pair counts are reported; small-sample uncertainty is not hidden behind a binary significance claim.

## 5. RQ2 — memory representation after admission

### 5.1 Treatments

- **Verbatim:** pack the frozen candidate memories in canonical ranked order up to the shared context ceiling.
- **Summary:** provide a source-linked summary generated from the identical frozen candidate set.

Candidate discovery uses the same normalized relevance, recency, and importance signals with weights `1:1:1`. Both treatments share the same current plan, action catalogue, Authority snapshot, output ceiling, provider profile, and required decision source.

### 5.2 Sampling plan

Six strata are crossed with six increasing load tiers:

- simple current state;
- stale state;
- conflicting reports;
- commitment lifecycle;
- failed-plan revision;
- salient distraction.

The tier target ranges are approximately:

- T1: 4,000–6,000 candidate tokens;
- T2: 6,000–9,000;
- T3: 9,000–12,000;
- T4: 12,000–18,000;
- T5: 18,000–23,000;
- T6: 23,000–30,000.

The required source must remain present in the bounded Verbatim packet at the common 8,192-token context ceiling. Each stratum/tier cell has eight independent model repeats:

`6 strata × 6 tiers × 8 repeats = 288 matched pairs`

Each pair contains two live calls, so RQ2 requires **576 formal calls**.

### 5.3 Frozen summaries

Exactly one complete batch of 36 source-linked summaries is generated with the real provider before formal collection. Missing or invalid summaries invalidate the batch; individual summaries are not selectively regenerated. Summary-generation calls are preparation evidence, not RQ2 observations.

### 5.4 Outcomes

The primary outcome is grounded action success: a typed proposal must pass validation and reach the expected terminal Authority receipt. The primary interpretation is non-inferiority: Summary should be no more than five percentage points worse than Verbatim.

Secondary outcomes are:

- context-token reduction, with a target of at least 40%;
- required-source retention and source-link fidelity;
- protocol/validation/Authority failure rates;
- latency and input/output token usage;
- paired discordance by stratum and load tier.

Token diagnostics compare Verbatim with Summary for every matched fixture/repeat, aggregate by stratum and load tier, and report treatment-wide totals. All retry attempts are included. Provider `output_tokens` is treated as inclusive of billed reasoning; a separate reasoning/thinking count is reported only when the Provider supplies that detail.

Paired intervals and exact paired tests are reported as uncertainty summaries. Tier and stratum breakdowns are exploratory and remain visibly labelled as such.

## 6. Call envelope

The replacement collection contains **240 RQ1 live calls**. The completed 48-call prompt-development gate and
36-summary generation batch remain preparation evidence. The sealed 576-call RQ2 collection is unchanged and is
not rerun or pooled with replacement RQ1 observations.

## 7. Freeze and collection procedure

1. Normalize and schema-validate all 30 RQ1 public/private block pairs.
2. Run the complete no-Provider action, admission, suite-shape, concurrency, and regression preflight.
3. Commit the implementation and normalized inputs.
4. Regenerate formal assets; the existing fixed RQ2 Summary batch is reused without Provider calls.
5. From that clean revision, generate one freeze bundle containing canonical hashes for the repository sources, public fixtures, prompts, tools, model settings, hidden scorers, pair manifests, and summary registry.
6. Run the 30 replacement RQ1 pairs with one pair per fixed worker; both conditions remain sequential within a pair.
7. Validate all evidence seals and exact 30/30 RQ1 coverage before aggregation.

The earlier 10-block RQ1 batch remains historical evidence only. It is not pooled with the replacement and stops
being the dissertation's RQ1 result once the complete replacement batch is sealed and validated.

## 8. Invalidation and reporting

A pair is invalid for treatment comparison if either condition lacks a sealed terminal outcome, uses a mismatched artifact hash/model profile, violates the matched context contract, or cannot replay to the same typed receipt. Invalid pairs remain in the audit report and denominator accounting.

All exclusions, provider failures, schema failures, validation rejections, Authority failures, timings, per-attempt token counts, matched token comparisons, treatment totals, output-limit anomalies, and unfinished cells are reported. No result is described as a live-model finding unless it comes from the sealed current formal batch.
