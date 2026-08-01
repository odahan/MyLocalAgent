# OD.MyLocalAgent — Pack de prompts v2 (génération par étapes)

Ce dossier contient la version révisée du « Master Prompt » v1, corrigée puis **découpée en une séquence d'étapes** à exécuter l'une après l'autre dans un agent de génération de code en mode agentique (Codex, Copilot Workspace, Windsurf…).

## Contenu

| Fichier | Rôle |
|---|---|
| `00-Shared-Specification.md` | Spécification complète et normative (v2). Remplace le master prompt v1. C'est la source de vérité unique à laquelle toutes les étapes se réfèrent. |
| `01-Stage0-Foundation.md` | Squelette de solution compilable : sln, 7 projets, pinning des versions, `Directory.Build.props`, `global.json`, `appsettings.json`, ledger. |
| `02-Stage1-LlmProviders-Connectors.md` | Couche LLM (Ollama par défaut, OpenAI opt-in) + connecteurs Gmail/Calendar/Dropbox + tests. |
| `03-Stage2-Core.md` | Core : incident pipeline, task store, scheduler, Tool Map + outils, skills, agent. |
| `04-Stage3-Workflows-Runner.md` | Moteur de workflows + runner CLI headless (Planificateur de tâches). |
| `05-Stage4-UI-Foundation.md` | UI WPF : shell, thème sombre global, DI/host, gestion d'incidents, Dashboard, Settings, Logs. |
| `06-Stage5-UI-Pages.md` | Pages restantes : Tasks, éditeur de tâche, éditeur de prompt, config outils, tool tester, AI playground, Diagnostics. |
| `07-Stage6-Hardening-Docs.md` | Audit thème sombre, « exception drill », README complet, script de publication, checklist d'acceptation exécutée. |
| `08-Stage7-Guardrails-PromptInjectionFilter.V2.MD` | **Extension post-v1** — filtre anti prompt-injection : nouveau projet `Guardrails` (screener LLM sans outils, score de confiance 0–100, contenu clair ou Base64), câblage GmailTool + HttpTool, panneau de test UI, docs. À exécuter après acceptation complète de l'étape 6. |
| `09-Stage8-WebSearch.md` | **Extension post-v1** — recherche web : outil `web.search` via l'API Ollama Web Search (clé gratuite, stockée en DPAPI), filtrage Guardrails obligatoire des résultats, groupe d'outils `web`, bloc de configuration UI, tâche d'exemple. Requiert les étapes 0–7. |
| `10-Stage9-WorkflowGraphEngine-Library.md` | **Proposition abandonnée — ne pas exécuter.** Ancienne proposition de moteur en graphe et bibliothèque de workflows, remplacée par la continuité du modèle séquentiel. |
| `11-Stage10-WorkflowsPage.md` | **Proposition abandonnée — ne pas exécuter.** Ancienne proposition de page Workflows séparée, incompatible avec les décisions désormais verrouillées. |
| `12-Stage11-PerTaskFileSystemAuthorization.md` | **Extension post-v1** — autorisations absolues par tâche, en lecture seule ou lecture-écriture, propagées sans état singleton aux workflows, agents et chemins locaux Dropbox. |
| `13-Stage12-SequentialWorkflowContext.md` | **Extension post-v1** — sélections structurées dans les sorties JSON, contexte accumulé optionnel pour `ai.prompt`, placeholders date/heure et aide légère à l'édition JSON séquentielle. |
| `14-Stage13-AIWorkflowHelper.md` | **Extension post-v1** — fenêtre AI Workflow Helper : création et affinage de tableaux JSON par LLM, explication de workflows, validation déterministe, propositions d'outils et d'autorisations, rendu Markdown sécurisé. |
| `15-Stage14-HierarchicalAIGenerationSettings.md` | **Extension post-v1** — réglages hiérarchiques de génération IA : valeurs globales Temperature/Top P, overrides par tâche et par step IA, fenêtre de contexte globale et politiques internes figées. |
| `16-Stage16-WindowlessRunnerAndRunMonitor.md` | **Extension post-v1** — runner sans fenêtre (`WinExe` + AttachConsole pour le shell), journal JSONL par exécution avec rétention, et page **Runs** : liste des exécutions passées et en cours, suivi live « en marche », limite de lignes, clear, export, auto-scroll. |

## Mode d'emploi

1. **Créez le dossier du dépôt** `OD.MyLocalAgent/` et copiez-y ce pack, par exemple sous `docs/prompts/`.
2. Ouvrez votre agent **dans ce dossier**, en lui donnant la permission d'écrire des fichiers et d'exécuter des commandes (`dotnet build`, `dotnet test`).
3. Collez le **prompt de l'étape 0**. Attendez que l'agent annonce le *gate* vert (build + tests OK). Les prompts supposent que la spec partagée est lisible à `docs/prompts/00-Shared-Specification.md` ; si votre agent ne lit pas les fichiers du dépôt, collez la spec partagée en entier avant le prompt d'étape 0 (elle tiendra dans la fenêtre de contexte).
4. Vérifiez rapidement le résultat (arborescence, absence de `// TODO`, `dotnet build` vert de votre côté aussi si possible), puis enchaînez avec l'étape suivante.
5. Suivez l'ordre 0–8, puis 11–16 (l'étape 15 est documentée directement dans la spec partagée, §18, sans fichier de prompt dédié). Les anciennes propositions 9 et 10 sont abandonnées et ne doivent pas être exécutées. Ne fusionnez pas les autres étapes : chacune suppose l'état exact laissé par la précédente.
6. Le fichier `docs/ARCHITECTURE-LEDGER.md`, créé à l'étape 0 puis complété à chaque étape, est la **mémoire inter-étapes** (décisions, API publiques, versions choisies). Si une étape dévie, c'est là qu'on le voit.

## Prérequis machine

Windows 10/11, .NET 10 SDK, Ollama (`ollama pull gemma4:12b` — vérifiez le tag exact avec `ollama list`). WPF ne compile que sous Windows. La vérification end-to-end (étapes 3 et 6) exige Ollama démarré ; les credentials Google/Dropbox ne sont nécessaires que pour les essais manuels finaux.

## Ce qui change par rapport à la v1 (changelog)

| # | Faiblesse v1 | Correction v2 |
|---|---|---|
| 1 | Un seul prompt géant, émission textuelle + `CONTINUE` | Découpage en 7 étapes avec gates `dotnet build` / `dotnet test` ; l'agent écrit les fichiers et itère jusqu'au vert |
| 2 | Cycle de dépendances caché (factory Ollama → incident dans Core) | La `LlmClientFactory` **lève** des exceptions typées ; l'enregistrement d'incident est fait par l'appelant (Core/UI/Runner). Règle explicite §7.1 + ADR §9 |
| 3 | « Plan » vs « Workflow » confus | Fusionnés : un seul concept, « workflow », spécifié de bout en bout (§8.3) |
| 4 | Correspondance `tool.method` ↔ méthode C# absente | **Tool Map** normative (§8.3) : alias workflow ↔ nom de fonction agent ↔ méthode C# ↔ args JSON |
| 5 | `errorPolicy` par étape non défini | Forme objet complète : `onError`, `retryCount`, `retryBackoffSeconds` (§8.3) |
| 6 | Aucune version de package épinglée | `Directory.Packages.props` obligatoire + politique de versions (§5) : stable d'abord, prerelease autorisée seulement si pas de stable (Agent Framework, System.CommandLine) ; FluentAssertions épinglé en 7.x (licence v8 commerciale) — à vérifier au restore |
| 7 | `UnobservedTaskException` comme pilier + test flaky | Pipeline d'incidents recentré sur `IIncidentReporter` + `AppExceptionHandlers` testables directement ; les événements CLR restent de la défense en profondeur (§7.3) |
| 8 | Scheduler « rolling trigger » fragile (run manqué = arrêt définitif) | `StartWhenAvailable = true`, `TimeZoneInfo.Local` explicite, commande `resync-schedules`, type de logon (S4U/Interactive) documenté (§7.3, §7.5, ADR) |
| 9 | « Run now », chemin des settings, découverte de config par le runner ambigus | Tranchés : Run now = process runner enfant ; settings utilisateur dans `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json` ; config en couches (appsettings côté exe + settings + secrets injectés au bootstrap) (§7.5, §7.6, §8.5) |
| 10 | Secrets Dropbox/OpenAI en clair dans appsettings | Magasin de secrets DPAPI (`secrets.dat`) injecté dans la configuration au bootstrap (§8.5, ADR) |
| 11 | Aucune garde injection de prompt / destructeur / SSRF | Règle R9 + section `Safety` : send/delete/POST désactivés par défaut, allowlist HTTP optionnelle, avertissement « contenu non fiable » dans le prompt système de l'agent (§4, §8.2) |
| 12 | Pas de stratégie de déploiement | Publication `dotnet publish` vers un dossier stable, le scheduler y pointe (§7.3, étape 6) |
| 13 | Timeout Ollama 120 s court pour un 12B local | 300 s par défaut (§8.2) |
| 14 | « Human-readable cron preview » sans API | Spécifié : occurrences suivantes + nommage des motifs courants, sans nouveau package |
| 15 | Self-check §10 impossible en mode texte | Critères d'acceptation répartis par étape + checklist finale réellement exécutable par l'agent |

## Conseils pratiques

- Si l'agent oublie une contrainte en cours d'étape, rappelez-lui simplement : « Re-read `docs/prompts/00-Shared-Specification.md`, section X ».
- Si une étape échoue à rendre le gate vert après plusieurs itérations, demandez-lui « list the remaining build errors » avant d'intervenir : c'est presque toujours une version de package ou un style XAML.
- Les vérifications finales (thème sombre « zéro artefact clair », exécution réelle de la tâche d'exemple, OAuth Google) restent des contrôles **humains** — l'étape 6 vous prépare la checklist.
