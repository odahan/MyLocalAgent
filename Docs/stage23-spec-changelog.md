# Stage 23 — Spec changelog

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
