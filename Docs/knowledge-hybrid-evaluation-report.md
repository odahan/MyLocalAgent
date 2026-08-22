# Knowledge hybrid retrieval evaluation fixture

The deterministic, non-production fixture is stored in `tests/OD.MyLocalAgent.Tests/Fixtures/knowledge-hybrid-evaluation.json` and is copied into the test output. It owns four synthetic documents (including one private document), their chunks, headings, page boundaries, tags, and expected results. Its schema and referential integrity are validated by the unit suite.

| Fixture cases | Coverage |
|---:|---|
| 5 synthetic cases | French diacritics, exact identifier/error token, semantic paraphrase, diversity, and unauthorized access |

| Metric | Result | Gate |
|---|---:|---|
| Recall@5 | 1.00 | Pass |
| Semantic-only Recall@5 baseline | 1.00 | Hybrid is not lower |
| MRR | 0.875 | ≥ 0.80 |
| Duplicate result units | 0 | 0 |
| Average returned token estimate | 8 | Bounded |
| Unauthorized leaks | 0 | 0 |

The deterministic unit evaluator materializes the fixture with synthetic vectors and executes the production retrieval service. The fixture and report contain no user documents or production queries.
