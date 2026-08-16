# Stage 23 — Spec changelog

## V4 — 2026-08-16

- Made local publication-date comparison authoritative; provider `after:` syntax is only an
  absolute-date query hint.
- Reserved the global retrieval budget across cumulative 2/4/7-day phase ceilings and exposed
  `windowHistory`, preventing the first window from exhausting all later expansion capacity.
- Sliding mode now excludes old, future, and unknown-date documents and reports rejection counts.
- Moved Guardrails after deterministic collection, date extraction, filtering, deduplication,
  ranking, and delivery budgeting.
- Replaced raw candidate titles and summaries with one bounded `protectedResults` block and safe
  metadata, including mandatory Base64 fallback when LLM screening is disabled or fails.
- Replaced the summarization-based translation stub with a dedicated tool-less translation
  service. Translation runs only after a clean source verdict, and its exact output is screened
  again before delivery.
- Updated both bundled editorial workflows: the daily LinkedIn report emits no posts for an empty
  selection. The Sunday workflow emits only a diagnostic for zero candidates, but writes a
  carefully scoped article from one or two sources rather than discarding usable evidence.

## V3 — 2026-08-09

**Fichier :** `Prompts/23-Stage23-WebDiscoveryResearch.md`
**Archivé :** `Prompts/OldSpecs/23-Stage23-WebDiscoveryResearchStorage.md`

### Périmètre resserré

La spec V3 réduit le périmètre au seul outil `research.discover` ajouté à la
capacité web existante (Stage 8). Tout ce qui concerne le stockage persistant,
les profils et l'UI de settings est supprimé.

### Changements détaillés

| Élément | V2 (ancienne) | V3 (nouvelle) |
|---|---|---|
| Profils de recherche | Persistés en JSON, UI Settings dédiée | **Supprimés** — paramètres inline dans le step workflow |
| `profileId` | Paramètre obligatoire du tool | **Supprimé** — remplacé par `themes[]` |
| Espaces de stockage | SQLite, registre, tables, migration | **Supprimés** |
| `storageSpaceId` | Paramètre optionnel partout | **Supprimé** |
| `language` | Qualifiant de recherche (défaut `"en"`) | Renommé `sourceLanguage` — qualifiant de recherche |
| `language` (nouveau) | — | Cible de traduction LLM (titre + short_summary), vide par défaut |
| `freshnessWindow` | Codé en dur (2→4→7 jours) | Paramètre : `"sliding"` (défaut) ou `"none"` |
| Guardrails | Non explicité | Règles identiques à `web.search` (S23-D9) |
| Traduction | — | Appel LLM interne sans outil, en fin de pipeline déterministe |
| Décisions | S23-D1 à S23-D15 | S23-D1 à S23-D10 (nettoyées) |

### Impact sur les specs dependantes

- **Stage 19 (SQLite)** : les références à `storageSpaceId` dans `sqlite.query` ne
  s'appliquent plus tant que Stage 23 V3 est en vigueur. La spec Stage 19 conserve
  ses propres décisions ; le bridge sera à revoir si le stockage est réintroduit.
- **Stage 24 (Publications)** : dépendance à Stage 23 pour les espaces de stockage.
  Cette dépendance n'est plus satisfaite par la V3. Stage 24 devra être réécrit ou
  son périmètre ajusté.
- **00-Shared-Specification.md, §25** : contient des références aux espaces de
  stockage et profils de recherche de Stage 23. À mettre à jour lorsque la V3 sera
  implémentée.
