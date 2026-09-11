# Pro-Spec — inventaire documentaire de MyLocalAgent

## 1. Périmètre et niveau de preuve

État local examiné le 11 septembre 2026, commit `4c169f7`. L’inventaire couvre les documents Markdown suivis dans le dépôt, les manuels HTML, les diagrammes et les exemples de `Docs`, ainsi que les instructions de dépôt et les supports de vérification pertinents. Le dossier Pro-Spec créé par cette étude est exclu du corpus source.

Les structures ont été recensées sur l’ensemble des documents. La lecture de fond a porté sur les consignes de gouvernance, les introductions et les clôtures de lots, les règles de priorité et de remplacement, les mises à jour documentaires, les validations et les comptes rendus représentatifs. Il ne s’agit pas d’une validation exhaustive de toutes les spécifications fonctionnelles ni d’un audit complet du code.

Les documents d’implémentation ont été traités comme des sources à analyser, pas comme des instructions pour exécuter leurs lots. Les conversations et les données d’exploitation externes au dépôt n’ont pas été consultées. Les preuves relatées par le ledger et les rapports sont donc des **preuves consignées**, sans reproduction de leurs exécutions dans cette étude.

L’annexe donne les fichiers documentaires et leurs sections. Les chemins sont conservés pour permettre la vérification ; le modèle de méthode extrait leurs rôles, sans reprendre les fonctionnalités propres à MyLocalAgent.

## 2. Carte des documents et fonctions

| Source | Fonction observée | Structure et usage dans la méthode |
|---|---|---|
| [README du produit](../../README.md) | Entrée générale du produit livré et référence pratique étendue. | État, prérequis, démarrage, configuration, règles d’usage, commandes, formats, exemples, architecture, diagnostic, extensions et tests. À consulter pour l’état présenté aux utilisateurs ; à actualiser quand cet état change. |
| [AGENTS.md](../../AGENTS.md) | Consignes d’exécution pour l’agent. | Publication Git et tests ; règles persistantes indépendantes du prompt courant. Les règles globales fournies dans la session complètent ce fichier et n’y figurent pas toutes. |
| [Instructions Copilot](../../.github/copilot-instructions.md) | Consigne ciblée pour un autre point d’entrée de l’IA. | Une directive de projet sur le traitement des placeholders. Illustration d’instructions dispersées selon l’outil. |
| [Index des prompts](../Prompts/README.md) | Ordonnancement et mode d’emploi du pack. | Table fichier/rôle, ordre d’exécution, prérequis, corrections de la v1 et conseils de reprise. Principal témoin de la méthode initiale. |
| [Spécification commune](../Prompts/00-Shared-Specification.md) | Contrat initial normatif et compléments successifs. | Rôle, mission, processus, règles, pile technique, structure, fonctionnalités, contrats, décisions, conventions, acceptation, addenda. À lire avant les lots ; à maintenir lors des changements transversaux. |
| [Ledger](../ARCHITECTURE-LEDGER.md) | Mémoire obligatoire entre les étapes. | Décisions clés, versions, conventions ; entrées par étape, correction ou version, avec changements, API, décisions, écarts et vérifications. Son organisation est devenue hétérogène avec les ajouts. |
| [Acceptation](../acceptance-report.md) | Consignation des contrôles exécutés et des suites humaines. | Tableau critère/état/preuve, commandes, publication, addenda, réserves et observations. Les premières lignes définissent réussi, à vérifier et échoué. |
| [Audit des thèmes](../theme-audit.md) | Audit spécialisé d’une exigence transversale. | Résultat initial, couverture des extensions, observations et suivis. Le document ne constitue pas un gate de gouvernance distinct ; il alimente l’acceptation. |
| [Première utilisation FR](../FirstRun.md), [EN](../FirstRun-EN.md) | Parcours de mise en route. | Prérequis, construction, publication, configuration, connecteurs, premier test, usages et extensions. Guides à maintenir lorsque la mise en route évolue. |
| [Manuel utilisateur FR](../User-Manual.html), [EN](../User-Manual.en.html) | Aide autonome orientée usages, avec référence détaillée. | Parcours, pages, tâches, formats, outils, planification, protections, rapports, diagnostic et exemples. Les lots récents exigent une couverture cohérente entre langues. |
| [Manuel technique](../Technical-Manual.html) | Référence bilingue d’écriture des tâches et workflows. | Modèle de tâche, droits, syntaxe, sorties, substitutions, catalogue, recettes et validation. Ne pas lui attribuer à tort toute la description de l’architecture du code. |
| [WhatsWhere](../WhatsWhere.md) | Carte des emplacements et de certaines responsabilités. | Installation, configuration, données, rapports, migration, extensions ; ajouts sur la propriété de composants Core/UI. |
| [Publication expliquée](../PublishExplained.md) | Explication d’une procédure opérationnelle. | Paramètres, protections, préparation, publication, conservation des réglages, validation, remplacement et nettoyage. À réconcilier avec le script correspondant. |
| [Interopérabilité des fournisseurs](../Provider-Interoperability.md) | Note technique transversale spécialisée. | Périmètre, capacités, configuration, santé, exécution, choix du modèle, erreurs et validation. |
| [Brief correctif](../Prompts/Discover-AntiRepetition-Implementation-Brief.md) | Mandat de correction issu de constats d’exploitation. | Mission, preuves, causes confirmées, hypothèses distinctes, règles, architecture proposée, migration, points d’entrée, ordre, tests, diagnostics et livrables. |
| [Note d’exploitation associée](../Discover-AntiRepetition-Operations.md) | Passage du correctif livré à son utilisation opérationnelle. | Contrat livré, état du déploiement consigné, répétition sur copie, ordre de déploiement, retour arrière, vérification et limites. |
| [Diagrammes V141](../V141/README.md) et trois SVG | Photographie architecturale datée. | Dépendances de projets, classes du moteur et séquence d’exécution. L’index les qualifie expressément d’historiques et renvoie aux documents actuels. |
| [Règle OldSpecs](../Prompts/OldSpecs/READM.MD) et documents archivés | Conservation des options anciennes et limitation de leur autorité. | Raison de conservation et interdiction d’implémenter ces versions. Certaines archives servent de base à des révisions. |
| `Docs/samples/*.json` | Exemples de contrats et supports de démonstration ou de contrôle. | Définitions de tâches et workflows : identité, réglages, droits, étapes et arguments selon l’exemple. Ils peuvent être vérifiés par des tests et réutilisés dans les manuels. |

Les documents d’exploitation démontrent qu’une livraison concerne aussi l’installation, les données et la configuration actives. Cette étude ne vérifie pas leur état actuel sur la machine : elle relève le rôle des procédures et les états qui y sont consignés.

## 3. Les fichiers de lots : familles et variations

| Fichiers ou étapes | Structure observée | Apport à Pro-Spec |
|---|---|---|
| 01 à 07 : étapes 0 à 6 | Sept rubriques récurrentes : lire, mission, contraintes, périmètre, accords de travail, gate, avant de terminer. | Contrat de lot avec entrée et sortie explicites ; construction progressive jusqu’à l’acceptation du produit. |
| 08 V2 et 09 : étapes 7 et 8 | Décisions verrouillées, contraintes, contrats, intégration, tests, documentation et acceptation. | Extension d’un produit accepté ; remplacement explicite d’une règle précédente. |
| 12 : étape 11 | But, périmètre, décisions, contraintes d’ingénierie, tests, livrables documentaires, gate. Lectures dans l’introduction. | Même fonction avec un gabarit plus court. |
| 13, 14, 15 : étapes 12, 13, 14 | Lectures, mission, décisions, contraintes, contrats ou périmètre, tests, documentation, gate et clôture. | Formalisation progressive des comportements et des impacts transversaux. Le numéro de fichier ne correspond pas toujours au numéro d’étape. |
| Étape 15 | Pas de fichier de lot autonome recensé ; addendum §18 de la spécification et entrée du ledger. | Une unité de travail peut exister sans fichier propre ; faiblesse de repérage à traiter dans un index. |
| 16 : étape 16 | Lectures, mission, décisions, contraintes, travail attendu, vérification. Documentation intégrée au périmètre. | Exemple de regroupement de rubriques sans disparition de leur rôle. |
| 17 V2 | Lectures, mission, décisions, contraintes, plusieurs contrats, exemples normatifs, tests, documentation, gate, acceptation et clôture. | La fiche devient un contrat d’implémentation complet. V1 conservée dans les archives. |
| 18 | Lectures, mission, décisions, contraintes, périmètre, tests, documentation, gate, acceptation et contrôle final des règles remplacées. | Changement explicite d’un comportement antérieur, avec recherche des incohérences résiduelles. |
| 18A | Contrats Core/UI, intégration, compatibilité, tests, docs, gate, acceptation, recherche finale. | Lot préparatoire inséré pour rendre les extensions suivantes plus cohérentes. |
| 18C | Contrats, comportement, réglages/UI, tests, documentation, gate et acceptation. | Extension qui dépend du travail préparatoire. Aucun fichier 18B recensé ; ne pas en inventer un. |
| 18D | Contrats déclaratifs, découverte, validation, activation, consommateurs, migration, amendements des lots 19 et 22, tests, docs et clôture. | Un lot peut imposer de réviser des fiches futures avant leur exécution. |
| 19 | Lectures, mission, décisions, contraintes, configuration, contrats, frontières d’accès, résultats, échecs, intégration, UI, exemples, tests, documentation et clôture ; extension ajoutée ensuite. | Fiche particulièrement détaillée sur les limites, la compatibilité et les erreurs. |
| 20 et 21 | Lectures, mission, décisions, contraintes, matrice des surfaces, contrat normatif, architecture, erreurs/confidentialité, tests, docs et acceptation. | Écrire aussi les surfaces qui restent inchangées et les règles de propagation. |
| 22 | Contrats par famille d’outils, limites, droits, résultats, UI, tests par famille, documentation, gate, acceptation et recherches finales. | Cohérence entre contrats, catalogue, exemples et manuels. |
| 23 actif | Lectures, mission, décisions, contrats, compilation des requêtes, comportement, intégration, confidentialité, tests/gate, extension. | Réduction d’un ancien périmètre : le contrat actif ne doit pas être déduit de l’archive. |
| Addendum 23 | Lectures, mission/périmètre, décisions, extension, contrat de données, plan et exemple complet. | Changement localisé attaché à un lot existant. Il ne reprend pas toutes les rubriques du gabarit long. |
| 26 | Contrats, données, services, UI, sécurité, tests, documentation, exclusions, ordre, gate et checklist ; essai conditionnel pour une optimisation. | Dépendances nombreuses et vraie décision de faisabilité avec repli prévu. |
| 27 | Contrats, migration, traitement, configuration, interface, corpus et gate de qualité, sécurité, tests, docs, exclusions, ordre et acceptation. | Qualité comparée à une référence, au-delà du nombre de tests réussis. |
| 28 | Lectures étendues, décisions, parcours et contrats, autorisations, compatibilité, accessibilité, séquence imposée, tests, documentation, exclusions, gate et acceptation. | Exemple mûr de coordination de nombreuses surfaces dans un mandat borné. |
| 29 | But, syntaxe, réponse structurée, règles d’exécution, surface livrée, exclusions différées. | Fiche compacte avec formulation de l’état livré ; pas de protocole complet autonome. |
| 30 | Mission, décisions, contrat persistant, exécution/échecs, édition et vérification avec résultat historique. | Contrat et preuve de livraison réunis dans un document court. |
| Brief anti-répétition, sans numéro de lot | Diagnostic sourcé, règles à implémenter, plan, tests, exploitation et livrables. | La méthode continue après les lots de construction et accueille les correctifs de production. |

Toutes les fiches individuelles et leurs rubriques figurent dans l’annexe. Le tableau ci-dessus décrit leurs **fonctions méthodologiques** ; il ne valide pas la conformité de leur implémentation.

## 4. Les archives et les remplacements

| Document archivé | Statut attesté ou relation observable |
|---|---|
| `OD.MyLocalAgent-MasterPrompt.md` | Ancien prompt unique : rôle, mission, règles, pile, structure, fonctionnalités, contrats, conventions, format de sortie et auto-contrôle. Le README du pack décrit son remplacement par la v2 découpée. |
| `08-Stage7-Guardrails-PromptInjectionFilter.md` | Remplacé explicitement par le fichier V2 actif. |
| `10-Stage9-WorkflowGraphEngine-Library.md` | Proposition abandonnée d’après l’index. L’existence ultérieure d’un routage conditionnel ne réactive pas cet ancien mandat. |
| `11-Stage10-WorkflowsPage.md` | Proposition abandonnée d’après l’index. |
| `17-Stage17-WorkflowAuthoringAids.md` | V1 remplacée explicitement par la V2. |
| `23-Stage23-WebDiscoveryResearchStorage.md` | Ancienne spécification remplacée par le lot 23 actif. |
| `24-Stage24-PublicationsAndMarkdownReports.md` | Conservé uniquement dans OldSpecs dans l’état examiné. L’index actif le cite encore comme extension ; le ledger emploie « Stage 24 » pour Direct Chat. Aucun successeur autonome de même nom n’est présent à la racine des prompts. |
| `25-Stage25-HumanWorkflowTextEditing.md` | Conservé uniquement dans OldSpecs ; l’index le cite encore comme extension. L’historique contient des évolutions de l’édition, mais n’autorise pas à traiter le fichier archivé comme mandat actif. |

La règle OldSpecs est claire ; les liens et statuts de l’index ne le sont pas tous. La formalisation propose donc de mettre à jour simultanément document actif, archive et index, et de conserver des identifiants stables.

## 5. Points de validation réellement présents

| Source | Nature du passage | Enseignement transférable |
|---|---|---|
| Premiers lots, introduction et gate | Étape précédente verte ; compilation, tests et contrôles ciblés. | Dépendance explicite et résultat exécutable avant la suite. |
| Lot 6, §§4.4–6 | Acceptation globale : usage réel, publication, incident, sécurité, interface et preuves. Contrôle humain explicite lorsque l’environnement ne permet pas de vérifier. | Une livraison dépasse les tests unitaires ; une impossibilité doit être qualifiée. |
| Lots 17 à 22 | Régressions, manuels, exemples, contrôles de publication et traces. | Les surfaces documentaires appartiennent à la livraison. |
| Lot 26, §10.3 | Essai de compatibilité avant activation d’une optimisation ; solution portable si l’essai échoue. | Lever une inconnue avec des critères préalables, sans forcer une technologie. |
| Lot 27, §10 | Corpus d’évaluation, métriques et comparaison avec une référence. | Vérifier la qualité annoncée, pas seulement le fonctionnement du mécanisme. |
| Lot 28, §§18–19 | Compilation/test, publication et contrôle manuel des thèmes. | Distinguer les preuves automatisées des observations d’interface. |
| Brief correctif, §§7–9 et note d’exploitation | Vérification sur copies, simulation, ordre de déploiement et retour arrière. | Le passage aux données actives mérite ses propres conditions. |

Le dépôt contient aussi des *Safety gates* fonctionnels et un mécanisme de limitation des appels LLM. Ce sont des contrôles **dans le logiciel** ; ils ne sont pas des étapes de gouvernance Pro-Spec. Aucun fichier autonome de gouvernance nommé « gateways » n’a été trouvé dans le corpus recensé.

## 6. Écarts et limites à enseigner

1. **Priorité documentaire variable.** L’ouverture de la spécification donne la priorité au document commun. Le lot 28, §0, donne la priorité au dépôt et au ledger face à un ancien prompt. Il faut une procédure explicite de remplacement ; « le code gagne toujours » légitimerait les défauts.
2. **Règles historiques encore visibles.** La spécification conserve une règle « dark-only », tandis que l’audit des thèmes et les lots ultérieurs reconnaissent un thème clair décidé explicitement. Cela prouve un besoin de signaler les règles remplacées, pas nécessairement une erreur du code.
3. **Index partiellement périmé.** Les lots 24 et 25 sont cités comme extensions alors que leurs fichiers se trouvent seulement dans OldSpecs. L’addendum commun §§25.1–25.3 conserve aussi des éléments de ces anciens périmètres. Une lecture du seul document dit « source unique » est insuffisante.
4. **Référence introuvable.** Le lot 7 V2 demande `Docs/stage6-final-report.md`, absent des fichiers recensés. `acceptance-report.md` existe. On ne peut pas affirmer qu’il remplace formellement ce rapport sans décision écrite.
5. **Mémoire volumineuse.** Le ledger compte plus de 2 400 lignes ; la spécification commune dépasse 2 200 lignes dans cet état. Les demandes de lecture complète ont un coût et ne garantissent pas que les règles actives seront distinguées de l’histoire.
6. **Statuts hétérogènes.** Étapes, versions, corrections et descriptions rétrospectives se mélangent. Le ledger commence par une entrée du lot 28 avant sa présentation générale ; les dernières entrées ne suivent pas toutes le même ordre. L’ordre du fichier ne peut pas servir seul de chronologie fiable.
7. **Trace et preuve indépendante.** Les commandes et résultats relatés sont utiles, mais cette étude ne les a pas rejoués. Le nombre de tests réussis ne constitue pas un certificat global de qualité du produit.
8. **Acceptation humaine inégalement centralisée.** Des réserves et suites sont explicites, mais il n’existe pas un registre homogène des validations humaines de tous les lots. Le terme « implémenté » ne doit pas être interprété comme une signature d’acceptation.
9. **Documentation parfois plus ambitieuse que ses contrôles.** Le test de parité Stage 22 vérifie ancres, structure, exemples et couverture de termes ; il ne démontre pas la justesse de toutes les explications des manuels. Source : [test documentaire](../../tests/OD.MyLocalAgent.Tests/Stage22ManualParityTests.cs).
10. **Qualité IA à part entière.** Le lot 27 demande un rapport d’évaluation dans Docs ; aucun rapport autonome clairement identifié comme tel n’a été recensé. Le brief correctif distingue déjà les tests avec doubles d’une évaluation sur un modèle réel. Il faut conserver cette distinction, sans déduire de l’absence d’un fichier autonome qu’aucune évaluation n’a jamais eu lieu.

Ces limites justifient les propositions de statuts, d’identifiants, de résumé des décisions actives et de liens entre critères et preuves. Elles ne justifient pas de réécrire rétroactivement l’histoire du projet pour lui donner une régularité qu’elle n’avait pas.

## 7. Supports exécutables et fichiers complémentaires

Les fichiers suivants ne sont pas tous des documents de gouvernance, mais rendent certaines exigences vérifiables :

- `global.json`, `Directory.Build.props`, `Directory.Packages.props` : environnement, règles de compilation et versions. Le ledger explique les choix ; ces fichiers règlent effectivement le build.
- `appsettings.json` : configuration de référence livrée, distincte d’un récit documentaire.
- `scripts/publish.ps1` : procédure de publication exécutable, expliquée dans PublishExplained.
- `scripts/Migrate-DataToWorkspace.ps1` : support de migration lié à l’évolution des emplacements.
- `scripts/Build-DownloadsInventory.ps1` et `scripts/Invoke-DownloadsSortPlan.ps1` : aides opérationnelles documentées dans le README.
- `scripts/get-dropbox-refresh-token.ps1` : aide de configuration ; `scripts/create-app-icon.ps1` : support de fabrication d’un asset. Ils ne définissent pas la gouvernance.
- `tests/OD.MyLocalAgent.Tests/` : vérifications des contrats et comportements. Certains tests ciblent explicitement les catalogues, exemples et manuels.
- `tests/OD.MyLocalAgent.Tests/Fixtures/` : données contrôlées, dont les corpus `knowledge-hybrid-evaluation.json` et `discover-anti-repetition-corpus.json`. Un corpus est un support de mesure, pas un résultat de mesure.

Exemples documentaires recensés : `acceptance-filesystem.json`, `daily-mail-summary.json`, `editorial-blog-sunday.json`, `editorial-watch-daily-db.json`, `editorial-watch-daily.json`, `foreach-mail-analysis.json`, `knowledge-rag-example.json`, `multiline-prompt-report.json`, `sqlite-read-example.json`, `translate-local-file.json`, `web-daily-brief.json`.

## 8. Annexe — fichiers et sections relevés

Le relevé ci-dessous conserve les intitulés des sections principales pour chaque document. Les sous-sections fonctionnelles détaillées restent consultables dans les fichiers sources ; elles n’ont pas vocation à devenir un gabarit universel. Les titres présents à l’intérieur d’un exemple de code Markdown sont exclus du relevé.

Relevé : **58 documents textuels**, auxquels s’ajoutent les 11 exemples JSON et les trois diagrammes SVG recensés plus haut.

<details><summary>.github/copilot-instructions.md</summary>

[Ouvrir le document](../../.github/copilot-instructions.md)

- Directives de projet

</details>

<details><summary>AGENTS.md</summary>

[Ouvrir le document](../../AGENTS.md)

- Git publishing
- Testing

</details>

<details><summary>Docs/acceptance-report.md</summary>

[Ouvrir le document](../../Docs/acceptance-report.md)

- Specification §11 checklist
- Packaging and dependency evidence
- Executed command forms
- Stage 7 V2 acceptance addendum
- Stage 8 acceptance addendum
- Stage 11 per-task file-system authorization
- Stage 13 AI Workflow Helper
- Stage 14 hierarchical AI generation settings
- Stage 15 agent loop hardening and run observability
- Post-release Tasks diagnostics correction
- Stage 17 workflow authoring aids
- Stage 19 SQLite Tool
- Stage 20 agent prompt placeholders
- Stage 18 compact external-content Guardrails
- Stage 18A extensible tool integration foundation
- Stage 18C File-system Recycle Bin and document conversions
- Stage 22 — Archive, Office and PDF tools
- Stage 23 Web Discovery Tool
- Stage 24 Direct Chat and subsequent 1.4.0 improvements
- Version 1.4.1 workspace reports and UI refinements
- Version 1.4.9 workspace-centered durable storage
- Stage 28 — Workflow step editor
- Stage 29 — Conditional workflow routing and structured prompt responses — 2026-08-24
- Stage 30 — Limited step-level `forEach`

</details>

<details><summary>Docs/ARCHITECTURE-LEDGER.md</summary>

[Ouvrir le document](../../Docs/ARCHITECTURE-LEDGER.md)

- Stage 28 — Reflection-driven workflow step editor
- Key architecture decisions
- Package versions
- Solution-wide conventions
- Stage entries
- Stage 22 — local content parsing
- Stage 26 — Embedded Knowledge and declarative prompt tools (2026-08-20)
- Stage 27 — Hybrid Knowledge retrieval and context selection — 2026-08-22
- Stage 29 — Conditional workflow routing and structured prompt responses — 2026-08-24
- Stage 30 — Limited step-level `forEach` — 2026-08-25
- Stage 23 addendum — Web Search proposal memory (2026-08-30)
- Version 2.5.5 — Provider-specific reasoning levels (2026-09-09)
- Version 2.5.4 — Repair editorial selection enum handling (2026-09-09)
- Version 2.5.3 — Preserve Gmail analysis after individual AI failures (2026-09-08)
- Version 2.5.2 — Honor explicit AI prompt reasoning (2026-09-07)
- Version 2.5.1 — Discover anti-repetition and deterministic editorial delivery (2026-09-05)

</details>

<details><summary>Docs/Discover-AntiRepetition-Operations.md</summary>

[Ouvrir le document](../../Docs/Discover-AntiRepetition-Operations.md)

- Delivered contract
- Deployment status
- Safe historical import rehearsal
- Deployment order
- Verification and limits

</details>

<details><summary>Docs/FirstRun-EN.md</summary>

[Ouvrir le document](../../Docs/FirstRun-EN.md)

- 1. Prerequisites
- 2. Build and verify the source code
- 3. Publish the application
- 4. First launch and local settings
- 5. Validate the local filesystem pipeline
- 6. Configure Google: Gmail and Calendar
- 7. Configure Dropbox with a refresh token
- 8. Configure Web Search (Ollama cloud)
- 9. Tools without credentials
- 10. Final check before the first task
- 11. Authoring a workflow without starting from scratch
- 12. Authoring a dated prompt-only task
- 13. Understand compact Gmail and HTTP content
- Stage 22 — local files
- Per-task sensitive permissions
- Embedded Knowledge

</details>

<details><summary>Docs/FirstRun.md</summary>

[Ouvrir le document](../../Docs/FirstRun.md)

- 1. Prérequis
- 2. Compiler et vérifier le code source
- 3. Publier l’application
- 4. Premier démarrage et réglages locaux
- 5. Valider le pipeline local de fichiers
- 6. Configurer Google : Gmail et Calendar
- 7. Configurer Dropbox avec un refresh token
- 8. Configurer Web Search (Ollama cloud)
- 9. Outils sans identifiants
- 10. Contrôle final avant la première tâche
- 11. Écrire un workflow sans partir de zéro
- 12. Écrire une tâche sans workflow avec un nom daté
- 13. Comprendre les contenus Gmail et HTTP compacts
- Stage 22 — fichiers locaux
- Autorisations sensibles par tâche
- Connaissances intégrées

</details>

<details><summary>Docs/Prompts/00-Shared-Specification.md</summary>

[Ouvrir le document](../../Docs/Prompts/00-Shared-Specification.md)

- 1. ROLE
- 2. MISSION
- 3. STAGED GENERATION PROCESS
- 4. PRODUCT RULES (non-negotiable)
- 5. TECH STACK & VERSION POLICY
- 6. SOLUTION STRUCTURE
- 7. FUNCTIONAL SPECIFICATION (by project)
- 8. DATA CONTRACTS
- 9. KEY ARCHITECTURE DECISIONS (record these in the ledger at Stage 0)
- 10. CODING STANDARDS
- 11. STAGE PROTOCOL & ACCEPTANCE
- 12. GUARDRAILS ADDENDUM V2
- 13. WEB SEARCH ADDENDUM
- 14. PER-TASK FILE-SYSTEM AUTHORIZATION ADDENDUM
- 15. SEQUENTIAL WORKFLOW CONTEXT ADDENDUM
- 16. AI WORKFLOW HELPER ADDENDUM
- 17. HIERARCHICAL AI GENERATION SETTINGS ADDENDUM
- 18. Stage 15 addendum — Agent loop hardening and run observability
- 19. Stage 16 addendum — Windowless runner and run monitor
- 20. Stage 17 addendum — Workflow authoring aids
- 21. Stage 18 addendum — Compact external-content Guardrails
- 21A. Stage 18A addendum — Extensible tool integration foundation
- 21C. Stage 18C addendum — File-system Recycle Bin and document conversions
- 21D. Stage 18D addendum — Declarative dynamic tooling
- 22. Stage 19 addendum — SQLite Tool
- 23. Stage 20 addendum — Agent prompt placeholders
- 24. Stage 21 addendum — Model information placeholders
- 25. Stages 23–25 addendum — Research, publications, and human workflow editing
- Addendum Stage 22 — Archive, Office and PDF tools
- Addendum Stage 28 — Workflow step editor
- Addendum Stage 30 — Limited step-level `forEach`
- Addendum Stage 23 — Task-owned editorial proposal memory

</details>

<details><summary>Docs/Prompts/01-Stage0-Foundation.md</summary>

[Ouvrir le document](../../Docs/Prompts/01-Stage0-Foundation.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated from the spec)
- 4. Scope
- 5. Working agreements
- 6. Stage gate (all must pass — show the actual command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/02-Stage1-LlmProviders-Connectors.md</summary>

[Ouvrir le document](../../Docs/Prompts/02-Stage1-LlmProviders-Connectors.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/03-Stage2-Core.md</summary>

[Ouvrir le document](../../Docs/Prompts/03-Stage2-Core.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope (in scope = everything below; out of scope = Workflows/Runner/UI projects)
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/04-Stage3-Workflows-Runner.md</summary>

[Ouvrir le document](../../Docs/Prompts/04-Stage3-Workflows-Runner.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/05-Stage4-UI-Foundation.md</summary>

[Ouvrir le document](../../Docs/Prompts/05-Stage4-UI-Foundation.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope (in scope only)
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/06-Stage5-UI-Pages.md</summary>

[Ouvrir le document](../../Docs/Prompts/06-Stage5-UI-Pages.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/07-Stage6-Hardening-Docs.md</summary>

[Ouvrir le document](../../Docs/Prompts/07-Stage6-Hardening-Docs.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Critical constraints (restated)
- 4. Scope
- 5. Working agreements
- 6. Stage gate (all must pass — show command output)
- 7. Before you finish

</details>

<details><summary>Docs/Prompts/08-Stage7-Guardrails-PromptInjectionFilter.V2.MD</summary>

[Ouvrir le document](../../Docs/Prompts/08-Stage7-Guardrails-PromptInjectionFilter.V2.MD)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product-owner decisions (locked)
- 4. Critical constraints
- 5. `OD.MyLocalAgent.Guardrails`
- 6. Core integration
- 7. UI
- 8. Tests
- 9. Documentation and specification upkeep
- 10. Stage gate
- 11. Stage-7 V2 acceptance addendum
- 12. Before finishing implementation

</details>

<details><summary>Docs/Prompts/09-Stage8-WebSearch.md</summary>

[Ouvrir le document](../../Docs/Prompts/09-Stage8-WebSearch.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Design decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Working agreements
- 7. Stage gate (all must pass — show command output)
- 8. Before you finish

</details>

<details><summary>Docs/Prompts/12-Stage11-PerTaskFileSystemAuthorization.md</summary>

[Ouvrir le document](../../Docs/Prompts/12-Stage11-PerTaskFileSystemAuthorization.md)

- Goal
- Scope
- Product decisions
- Engineering constraints
- Required tests
- Documentation deliverables
- Gate

</details>

<details><summary>Docs/Prompts/13-Stage12-SequentialWorkflowContext.md</summary>

[Ouvrir le document](../../Docs/Prompts/13-Stage12-SequentialWorkflowContext.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Design decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Documentation
- 7. Stage gate (all must pass — show command output)
- 8. Before you finish

</details>

<details><summary>Docs/Prompts/14-Stage13-AIWorkflowHelper.md</summary>

[Ouvrir le document](../../Docs/Prompts/14-Stage13-AIWorkflowHelper.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Required tests
- 7. Documentation
- 8. Stage gate (all must pass — show command output)
- 9. Before you finish

</details>

<details><summary>Docs/Prompts/15-Stage14-HierarchicalAIGenerationSettings.md</summary>

[Ouvrir le document](../../Docs/Prompts/15-Stage14-HierarchicalAIGenerationSettings.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Data contracts and validation
- 6. Runtime propagation
- 7. User interface
- 8. Required tests
- 9. Documentation
- 10. Stage gate (all must pass — show command output)
- 11. Before you finish

</details>

<details><summary>Docs/Prompts/16-Stage16-WindowlessRunnerAndRunMonitor.md</summary>

[Ouvrir le document](../../Docs/Prompts/16-Stage16-WindowlessRunnerAndRunMonitor.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope of work
- 6. Required verification (gate)

</details>

<details><summary>Docs/Prompts/17-Stage17-WorkflowAuthoringAids.V2.md</summary>

[Ouvrir le document](../../Docs/Prompts/17-Stage17-WorkflowAuthoringAids.V2.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Critical constraints
- 5. Core metadata contracts
- 6. Snippet construction and insertion
- 7. Workflow editor caret integration
- 8. Shared parsing and validation
- 9. Tool catalog dialog
- 10. Workflow Prompt Editor
- 11. Task editor integration and assisted undo
- 12. Privacy and incident handling
- 13. Normative examples
- 14. Required tests
- 15. Documentation
- 16. Stage gate
- 17. Acceptance addendum
- 18. Before finishing implementation

</details>

<details><summary>Docs/Prompts/18-Stage18-CompactExternalContentGuardrails.md</summary>

[Ouvrir le document](../../Docs/Prompts/18-Stage18-CompactExternalContentGuardrails.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Required automated tests
- 7. Documentation
- 8. Stage gate (all mandatory)
- 9. Acceptance addendum
- 10. Before you finish

</details>

<details><summary>Docs/Prompts/18A-Stage18A-ExtensibleToolIntegration.md</summary>

[Ouvrir le document](../../Docs/Prompts/18A-Stage18A-ExtensibleToolIntegration.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Core contract
- 5. Task editor contract
- 6. Other integration surfaces
- 7. Compatibility and privacy
- 8. Required automated tests
- 9. Documentation
- 10. Stage gate
- 11. Acceptance addendum
- 12. Before finishing implementation

</details>

<details><summary>Docs/Prompts/18C-Stage18C-FileSystemRecycleAndDocumentConversions.md</summary>

[Ouvrir le document](../../Docs/Prompts/18C-Stage18C-FileSystemRecycleAndDocumentConversions.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Tool Map contract
- 5. Runtime behavior
- 6. Settings and WPF
- 7. Required automated tests
- 8. Documentation
- 9. Stage gate
- 10. Acceptance addendum

</details>

<details><summary>Docs/Prompts/18D-Stage18D-DeclarativeDynamicTooling.md</summary>

[Ouvrir le document](../../Docs/Prompts/18D-Stage18D-DeclarativeDynamicTooling.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Declarative attribute contract
- 5. Discovery and immutable catalog
- 6. Metadata validation and fail-fast rules
- 7. Generic dependency injection and runtime activation
- 8. Catalog-driven consumers
- 9. Migration of existing tools
- 10. Required amendments to Stage 19
- 11. Required amendments to Stage 22
- 12. Compatibility, security, and privacy
- 13. Required automated tests
- 14. Documentation
- 15. Stage gate
- 16. Acceptance addendum
- 17. Before finishing implementation

</details>

<details><summary>Docs/Prompts/19-Stage19-SQLiteTool.md</summary>

[Ouvrir le document](../../Docs/Prompts/19-Stage19-SQLiteTool.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Critical constraints
- 5. Package, configuration, and persisted task contract
- 6. Declarative tool metadata and public contracts
- 7. SQL and filesystem confinement
- 8. Deterministic result contract and limits
- 9. SQLite Guardrails
- 10. Cancellation, timeouts, and concurrency
- 11. Core, workflow, and authoring integration
- 12. User interface
- 13. Privacy, errors, and observability
- 14. Normative examples
- 15. Required automated tests
- 16. Documentation
- 17. Stage gate
- 18. Acceptance addendum
- 19. Before finishing implementation
- 20. Stage 23 proposal-memory extension

</details>

<details><summary>Docs/Prompts/20-Stage20-AgentPromptPlaceholders.md</summary>

[Ouvrir le document](../../Docs/Prompts/20-Stage20-AgentPromptPlaceholders.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Critical constraints
- 5. Scope and execution matrix
- 6. Agent prompt placeholder contract
- 7. Shared placeholder architecture
- 8. Runtime, warnings, reports, and UI
- 9. Privacy, observability, and failure semantics
- 10. Normative examples
- 11. Required automated tests
- 12. Documentation
- 13. Stage gate
- 14. Acceptance addendum
- 15. Before finishing implementation

</details>

<details><summary>Docs/Prompts/21-Stage21-ModelInformationPlaceholders.md</summary>

[Ouvrir le document](../../Docs/Prompts/21-Stage21-ModelInformationPlaceholders.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Critical constraints
- 5. Scope and execution matrix
- 6. Normative placeholder contract
- 7. Shared architecture
- 8. Task run logs
- 9. Failure, privacy, and compatibility
- 10. Required automated tests
- 11. Documentation
- 12. Stage gate
- 13. Acceptance addendum
- 14. Before finishing implementation

</details>

<details><summary>Docs/Prompts/22-Stage22-ArchiveOfficePdfTools.md</summary>

[Ouvrir le document](../../Docs/Prompts/22-Stage22-ArchiveOfficePdfTools.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Locked product decisions
- 4. Critical constraints
- 5. Package and configuration contract
- 6. Declarative tool groups, catalog, and public contracts
- 7. Archive contract
- 8. DOCX contract
- 9. Spreadsheet contract
- 10. PDF contract
- 11. Authorization, Safety, runtime, and authoring
- 12. Results, privacy, cancellation, and failure semantics
- 13. User interface
- 14. Required automated tests
- 15. Documentation
- 16. Stage gate
- 17. Acceptance addendum
- 18. Before finishing implementation

</details>

<details><summary>Docs/Prompts/23-Stage23-Addentum-WebSearchProposalMemory.md</summary>

[Ouvrir le document](../../Docs/Prompts/23-Stage23-Addentum-WebSearchProposalMemory.md)

- 0. Read first (mandatory)
- 1. Mission and scope
- 2. Locked decisions
- 3. `research.discover` extension
- 4. Database interoperability contract
- 5. Implementation plan
- 6. Example: daily LinkedIn editorial watch with memory

</details>

<details><summary>Docs/Prompts/23-Stage23-WebDiscoveryResearch.md</summary>

[Ouvrir le document](../../Docs/Prompts/23-Stage23-WebDiscoveryResearch.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. Data contracts
- 4. Query compilation
- 5. Discovery and retrieval behavior
- 6. Workflow integration
- 7. Privacy, safety and observability
- 8. Tests and gate
- 9. Proposal-memory extension

</details>

<details><summary>Docs/Prompts/26-Stage26-EmbeddedKnowledgeAndDeclarativePromptTools.md</summary>

[Ouvrir le document](../../Docs/Prompts/26-Stage26-EmbeddedKnowledgeAndDeclarativePromptTools.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. Terminology and authorization model
- 4. Persisted contracts
- 5. Generic `ai.prompt` tool execution
- 6. Knowledge tool contract
- 7. Workspace storage and database
- 8. Embedding subsystem
- 9. Extraction and chunking
- 10. Vector index and compatibility spike
- 11. Knowledge application services
- 12. WPF user experience
- 13. Workflow authoring and analysis
- 14. Security, privacy, and failure behavior
- 15. Tests
- 16. Documentation and architecture ledger
- 17. Explicit non-goals
- 18. Recommended implementation order
- 19. Stage gate
- 20. Acceptance checklist
- 21. Before finishing implementation

</details>

<details><summary>Docs/Prompts/27-Stage27-HybridKnowledgeRetrievalAndContextSelection.md</summary>

[Ouvrir le document](../../Docs/Prompts/27-Stage27-HybridKnowledgeRetrievalAndContextSelection.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. Terminology and retrieval pipeline
- 4. Persisted schema and migration
- 5. Safe lexical retrieval
- 6. Semantic candidates and rank fusion
- 7. MMR and controlled small-to-big context
- 8. Settings and index contract
- 9. Tool and user experience
- 10. Evaluation corpus and quality gate
- 11. Security, privacy, concurrency, and failure behavior
- 12. Tests
- 13. Documentation and architecture ledger
- 14. Explicit non-goals
- 15. Recommended implementation order
- 16. Stage gate
- 17. Acceptance checklist
- 18. Before finishing implementation

</details>

<details><summary>Docs/Prompts/28-Stage28-WorkflowStepEditor.md</summary>

[Ouvrir le document](../../Docs/Prompts/28-Stage28-WorkflowStepEditor.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. User experience and command states
- 4. Caret location and structural mutation
- 5. Reflection-backed argument metadata
- 6. On-demand form model
- 7. `ai.prompt` specialized section
- 8. Authorization review
- 9. Dialog request, result, and Apply
- 10. Clean deletion
- 11. Task editor integration and undo
- 12. Reuse and compatibility
- 13. Accessibility, theme, privacy, and performance
- 14. Required implementation sequence
- 15. Required tests
- 16. Documentation and architecture ledger
- 17. Explicit non-goals
- 18. Stage gate
- 19. Acceptance checklist
- 20. Before finishing implementation

</details>

<details><summary>Docs/Prompts/29-Stage29-ConditionalWorkflowRouting.md</summary>

[Ouvrir le document](../../Docs/Prompts/29-Stage29-ConditionalWorkflowRouting.md)

- Purpose
- Persisted syntax
- Structured AI response
- Graph and execution rules
- Delivered surface
- Deferred intentionally

</details>

<details><summary>Docs/Prompts/30-Stage30-ForEachStepExecution.md</summary>

[Ouvrir le document](../../Docs/Prompts/30-Stage30-ForEachStepExecution.md)

- 1. Mission
- 2. Locked product decisions
- 3. Persisted contract
- 4. Runtime and failure contract
- 5. Authoring contract
- 6. Verification

</details>

<details><summary>Docs/Prompts/Discover-AntiRepetition-Implementation-Brief.md</summary>

[Ouvrir le document](../../Docs/Prompts/Discover-AntiRepetition-Implementation-Brief.md)

- 1. Mission et résultat attendu
- 2. Périmètre exact et preuves
- 3. Règles métier à implémenter
- 4. Architecture cible recommandée
- 5. Remise à niveau de l’historique
- 6. Points d’entrée dans le dépôt
- 7. Ordre de réalisation
- 8. Tests et critères d’acceptation
- 9. Diagnostics et livrables

</details>

<details><summary>Docs/Prompts/OldSpecs/08-Stage7-Guardrails-PromptInjectionFilter.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/08-Stage7-Guardrails-PromptInjectionFilter.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Design decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Working agreements
- 7. Stage gate (all must pass — show command output)
- 8. Before you finish

</details>

<details><summary>Docs/Prompts/OldSpecs/10-Stage9-WorkflowGraphEngine-Library.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/10-Stage9-WorkflowGraphEngine-Library.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Design decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Working agreements
- 7. Stage gate (all must pass — show command output)
- 8. Before you finish

</details>

<details><summary>Docs/Prompts/OldSpecs/11-Stage10-WorkflowsPage.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/11-Stage10-WorkflowsPage.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Design decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Working agreements
- 7. Stage gate (all must pass — show command output)
- 8. Before you finish

</details>

<details><summary>Docs/Prompts/OldSpecs/17-Stage17-WorkflowAuthoringAids.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/17-Stage17-WorkflowAuthoringAids.md)

- 1. Read first (mandatory)
- 2. Mission
- 3. Product decisions (locked by the product owner — do not re-open)
- 4. Critical constraints (restated)
- 5. Scope
- 6. Required tests
- 7. Documentation
- 8. Stage gate (all must pass — show command output)
- 9. Before you finish

</details>

<details><summary>Docs/Prompts/OldSpecs/23-Stage23-WebDiscoveryResearchStorage.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/23-Stage23-WebDiscoveryResearchStorage.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Read first
- 3. Locked product decisions
- 4. Data contracts
- 5. Discovery and retrieval behavior
- 6. Tool surface and workflow integration
- 7. Settings and UI
- 8. Privacy, safety and observability
- 9. Tests and gate

</details>

<details><summary>Docs/Prompts/OldSpecs/24-Stage24-PublicationsAndMarkdownReports.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/24-Stage24-PublicationsAndMarkdownReports.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. Data model
- 4. Repository and workflow visibility
- 5. User interface
- 6. Markdown export
- 7. Privacy, observability and documentation
- 8. Tests and gate

</details>

<details><summary>Docs/Prompts/OldSpecs/25-Stage25-HumanWorkflowTextEditing.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/25-Stage25-HumanWorkflowTextEditing.md)

- 0. Read first (mandatory)
- 1. Mission
- 2. Locked product decisions
- 3. Deterministic workflow-text grammar
- 4. Core contracts
- 5. User interface
- 6. Conversion and assisted authoring behavior
- 7. Privacy and accessibility
- 8. Tests and gate

</details>

<details><summary>Docs/Prompts/OldSpecs/OD.MyLocalAgent-MasterPrompt.md</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/OD.MyLocalAgent-MasterPrompt.md)

- 1. ROLE
- 2. MISSION
- 3. PRODUCT RULES (non-negotiable)
- 4. TECH STACK (exact packages)
- 5. SOLUTION STRUCTURE
- 6. FUNCTIONAL SPECIFICATION (by project)
- 7. DATA CONTRACTS
- 8. CODING STANDARDS
- 9. OUTPUT FORMAT (how YOU must answer)
- 10. ACCEPTANCE CRITERIA (self-check before finishing)

</details>

<details><summary>Docs/Prompts/OldSpecs/READM.MD</summary>

[Ouvrir le document](../../Docs/Prompts/OldSpecs/READM.MD)

- Copy for the record

</details>

<details><summary>Docs/Prompts/README.md</summary>

[Ouvrir le document](../../Docs/Prompts/README.md)

- Contenu
- Mode d'emploi
- Prérequis machine
- Ce qui change par rapport à la v1 (changelog)
- Conseils pratiques
- Stage 22

</details>

<details><summary>Docs/Provider-Interoperability.md</summary>

[Ouvrir le document](../../Docs/Provider-Interoperability.md)

- Scope
- Explicit multimodal capabilities
- Settings and secret handling
- Health and model discovery contract
- Execution path and timeout behavior
- Reasoning preference and safe fallback
- Effective model resolution
- Validation

</details>

<details><summary>Docs/PublishExplained.md</summary>

[Ouvrir le document](../../Docs/PublishExplained.md)

- 1. Script Parameters
- 2. Strict Mode and Error Handling
- 3. Path Resolution
- 4. Safety Guards
- 5. Staging and Backup Paths
- 6. The `Invoke-ProjectPublish` Function
- 7. Publish Both Projects
- 8. Preserve User Settings
- 9. Validate Executables
- 10. Atomic Deployment Swap
- 11. Cleanup
- 12. Summary Output
- 13. Final Cleanup (finally block)

</details>

<details><summary>Docs/Technical-Manual.html</summary>

[Ouvrir le document](../../Docs/Technical-Manual.html)

- 1. Modèle de tâche JSON
- 2. Autorisations : deux niveaux obligatoires
- 3. Syntaxe du workflow
- 4. Tâche avec prompt agentique
- 5. Placeholders de workflow
- 5.1 Sorties d’étape et chemins disponibles
- 6. RAG local et autorisation par tags
- 7. Catalogue exhaustif des outils
- 8. Recettes vérifiables
- 9. Validation et erreurs fréquentes

</details>

<details><summary>Docs/theme-audit.md</summary>

[Ouvrir le document](../../Docs/theme-audit.md)

- Final Stage 6 result
- Stage 7 Trust filter coverage
- Stage 8 Web Search coverage
- Control inventory
- Template boundaries
- Not applicable controls
- Remaining human visual checks
- Stage 11 authorized-location editor
- Stage 13 AI Workflow Helper modal
- Stage 14 hierarchical AI generation settings
- Stage 15 AI Helper theme and rendered document
- Post-release Tasks diagnostic controls
- Stage 17 workflow authoring aids
- Stage 20 Prompt editor placeholder help and warnings
- Stage 18A metadata-driven tool whitelist
- Stage 18C Recycle Bin Safety setting
- Stage 19 SQLite controls

</details>

<details><summary>Docs/User-Manual.en.html</summary>

[Ouvrir le document](../../Docs/User-Manual.en.html)

- The recommended path
- Installation and prerequisites
- First start: the checklist
- Configure Ollama and select a model
- Create a fully local first task
- Test a prompt, then run the real task
- Connect Gmail and Google Calendar
- Connect Dropbox
- Enable guarded Ollama Web Search
- Explore a prompt in the AI playground
- Have a conversation in Direct Chat
- Test one tool in isolation
- Enable an OpenAI-compatible provider
- Understand every application page
- Tasks, prompts, and workflows
- Task structure
- Placeholders and outputs
- Tool catalog
- Workflow reference
- Scheduling and cron format
- Understand the content trust filter
- Safety: grant the minimum required access
- Track executions and read reports
- Diagnostics, exports, and incidents
- Resolve common problems
- Data locations and runner commands
- Archives, Office documents, and PDF
- Embedded Knowledge and RAG
- Per-task sensitive permissions

</details>

<details><summary>Docs/User-Manual.html</summary>

[Ouvrir le document](../../Docs/User-Manual.html)

- Le parcours recommandé
- Installation et prérequis
- Premier démarrage : la checklist
- Configurer Ollama et choisir un modèle
- Créer une première tâche 100 % locale
- Tester un prompt, puis lancer la vraie tâche
- Connecter Gmail et Google Calendar
- Connecter Dropbox
- Activer la recherche web Ollama protégée
- Explorer un prompt dans l’AI playground
- Converser avec Direct Chat
- Tester un outil isolément
- Activer un fournisseur OpenAI-compatible
- Comprendre chaque page
- Tâches, prompts et workflows
- Structure d’une tâche
- Placeholders et sorties
- Catalogue des outils
- Référence des workflows
- Planification et format cron
- Comprendre le filtre de confiance
- Sécurité : autoriser le minimum nécessaire
- Suivre les exécutions et lire les rapports
- Diagnostics, exports et incidents
- Résoudre les problèmes courants
- Emplacements de données et commandes CLI
- Archives, documents Office et PDF
- Knowledge embarqué et RAG
- Autorisations sensibles par tâche

</details>

<details><summary>Docs/V141/README.md</summary>

[Ouvrir le document](../../Docs/V141/README.md)

Document court sans sous-section de niveau principal.

</details>

<details><summary>Docs/WhatsWhere.md</summary>

[Ouvrir le document](../../Docs/WhatsWhere.md)

- Installation Directory
- Global User Configuration Directory
- Workspace Data Directory
- Configuration Layers
- File System Sandbox
- Task Reports
- One-time Migration
- Windows Task Scheduler
- Embedded Knowledge and RAG
- Workflow step editor
- Workflow `forEach` execution

</details>

<details><summary>README.md</summary>

[Ouvrir le document](../../README.md)

- Product status
- Prerequisites
- Quick start
- Google Cloud OAuth setup
- Dropbox app setup
- Web search (Ollama)
- Configuration and data locations
- Project-wide AI instructions
- AI generation policy
- Sensitive task capability defaults
- Prompt-injection guardrails
- Windows Task Scheduler
- Runner CLI
- Task and workflow syntax
- Architecture
- Direct Chat
- Provider switching
- Diagnostics and incidents
- Troubleshooting
- Extension points
- Build and test
- Debugging in Visual Studio
- Stage 22 local file tools
- Task-owned sensitive permissions
- Embedded Knowledge and RAG

</details>
