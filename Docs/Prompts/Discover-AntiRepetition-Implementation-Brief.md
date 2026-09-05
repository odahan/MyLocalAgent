# Brief d’implémentation — empêcher les répétitions de la veille Discover

Destinataire : GPT SOL, effort de raisonnement Moyen. Analyse effectuée le 5 septembre 2026.

## 1. Mission et résultat attendu

Corriger la chaîne de veille éditoriale pour qu’un rapport ne puisse pas être exporté avec des sujets non mémorisés ou sans validation de nouveauté. Empêcher les reprises de la même actualité sous un autre titre, dans une autre langue ou sur un autre site, tout en conservant les véritables évolutions d’un produit.

La demande actuelle porte sur ce brief uniquement. Ce document décrit le travail à réaliser lors de l’implémentation ; sa rédaction n’autorise pas à modifier maintenant le code, les workflows actifs ou les données de production.

Ne pas refaire une enquête générale : partir des constats ci-dessous, vérifier les interfaces actuelles, puis avancer par étapes. Signaler explicitement tout écart entre le dépôt et l’application installée. Les journaux examinés indiquent la version 2.5.0 ; le code analysé est celui du dépôt courant.

## 2. Périmètre exact et preuves

Le workflow concerné est **Daily Editorial Watch-DB**, identifiant `editorial-watch-daily-db`. La tâche **Daily Gmail analysis ODMLA** ne fait que l’analyse Gmail ; ne pas la modifier. L’ancien workflow `editorial-watch-daily` était désactivé lors de l’inspection.

Ressources locales à consulter en lecture seule :

- Workflow actif : `C:/OD.MyLocalAgent/.od-my-local-agent/tasks/editorial-watch-daily-db.json`.
- Base : `C:/Users/odaha/Dropbox/VeilleAutomatique/editorial-proposals.db`.
- Rapports : `C:/Users/odaha/Dropbox/VeilleAutomatique/2026/09/VeilleAuto-20260901.md` à `VeilleAuto-20260905.md`.
- Journaux : `C:/OD.MyLocalAgent/.od-my-local-agent/logs/runs/editorial-watch-daily-db/`.
- Rapports techniques : `C:/OD.MyLocalAgent/.od-my-local-agent/outputs/reports/editorial-watch-daily-db/`.

La base a été ouverte avec SQLite en mode lecture seule. Aucun contenu n’a été modifié.

### Constats confirmés

| Exécution | Rapport | Appels réussis `sqlite.storeProposal` dans les journaux |
|---|---|---:|
| 2026-09-01 à 14:00 UTC | 5 sujets et 3 posts | 0 |
| 2026-09-02 à 14:00 UTC | 3 sujets et 3 posts | 3 |
| 2026-09-03 à 16:12 UTC | 2 sujets et 2 posts | 2 |
| 2026-09-04 à 14:00 UTC | 3 sujets et 3 posts | 0 |
| 2026-09-05 à 00:04 UTC | Exécution nocturne | 3 |

Les exécutions des 1er et 4 septembre se terminent avec succès malgré l’absence d’enregistrement. La base contient 14 propositions au moment de l’inspection, toutes avec `embeddinggemma:300m`, dimension 768. Elle ne contient aucune ligne créée le 1er ou le 4 septembre.

Exemples à convertir en cas de non-régression :

- NativeAOT / .NET 11 Preview 7 revient les 1er et 2 septembre avec la même URL : `https://byteiota.com/net-11-preview-7-nativeaot-default-and-faster-builds/`.
- GitHub Copilot / mise à jour d’août revient aux mêmes dates avec la même URL : `https://github.blog/changelog/2026-08-28-github-copilot-in-visual-studio-august-update-2/`.
- Les types union C# reviennent via The Register le 3 septembre, LavX le 4, et SecurityExpress dans la base le 5. Les propositions stockées portent les identifiants 10 et 12 pour les 3 et 5 septembre.
- Le cosinus calculé directement entre les embeddings stockés des propositions 10 et 12 est **0,6780445327**, contre un seuil configuré de **0,92**. Attention : ce n’est pas le score exact du candidat brut lors de Discover. Les traces disponibles ne permettent pas de reconstituer ce dernier avec certitude.

Les affirmations techniques contenues dans ces articles et rapports n’ont pas été vérifiées. Les fixtures doivent les utiliser comme exemples de répétition, pas comme vérités techniques.

### Causes dans le code

1. Le prompt demande au modèle d’appeler `sqlite.storeProposal`, mais aucune postcondition ne garantit un enregistrement par sujet. `SequentialWorkflowEngine` accepte le texte rendu. `StopOnError` ne détecte pas une opération que le modèle n’a jamais appelée.
2. `ResearchDiscoverTool.BuildProtectedResultAsync` filtre les documents avant traduction et rédaction. Il n’existe pas de contrôle de nouveauté des propositions finales dans ce workflow.
3. `ProposalMemoryService.FilterAsync` compare les titres normalisés, les URL canoniques et le cosinus des embeddings. La projection est `titre + résumé court`. Les candidats anglais sont comparés à des propositions éditoriales françaises reformulées.
4. `StoreAsync` effectue un upsert sur une clé SHA-256 combinant titre normalisé et URL. Il ne refuse pas les doublons sémantiques. Une autre formulation avec une autre URL crée une ligne distincte.
5. Le workflow ne consulte pas un RAG des anciens rapports. Il utilise seulement la mémoire de propositions et son service d’embeddings.

### Faiblesses supplémentaires à traiter, sans les présenter comme causes prouvées de ces exécutions

- Une seule URL est mémorisée par proposition, même si plusieurs sources sont citées.
- Pas de comparaison sémantique entre les candidats d’un même lot dans `FilterAsync`.
- Les embeddings incompatibles sont comptés puis exclus de la comparaison sémantique ; il ne faut pas assimiler cette couverture partielle à une validation de nouveauté complète.
- La fenêtre de mémoire est de six mois avec `usePreviousProposalUsedTag=false`. Ce paramètre ne signifie pas que les propositions récentes `used=false` sont ignorées : elles sont bien prises en compte.
- Les objectifs de collecte et les fenêtres glissantes sont évalués avant le filtrage mémoire. Cela peut raréfier les résultats réellement nouveaux ; ce n’est pas une raison pour réintroduire des doublons.
- L’export journalier utilise le même chemin pour toutes les exécutions d’une journée et écrase le précédent. Le fichier du 5 septembre ne représente donc pas nécessairement l’exécution nocturne.

## 3. Règles métier à implémenter

- Une proposition exportée doit correspondre à un enregistrement durable et à une décision de nouveauté traçable.
- Même annonce, mêmes faits, nouvelle URL ou nouvel angle : doublon.
- Même produit mais nouvelle version, correctif distinct ou fait nouveau matériel attesté : évolution potentiellement admissible. Ne pas bloquer un produit ou un thème entier.
- Une simple reformulation, traduction, date de republication ou appréciation éditoriale ne constitue pas un fait nouveau.
- Une URL connue portant un contenu réellement évolutif doit pouvoir représenter plusieurs événements distincts. Éviter une interdiction permanente et aveugle de toutes les pages de releases.
- Si la nouveauté reste ambiguë, ne pas exporter le sujet dans la veille automatique. Retourner un statut explicite permettant une revue.
- Le quota est un maximum de trois sujets nouveaux, jamais une obligation de remplir trois places. Zéro sujet nouveau est un résultat valide.
- Une panne de mémoire ou de validation est une erreur distincte de « zéro nouveauté ».
- Conserver les identités et empreintes historiques des annonces au-delà de la fenêtre de recherche sémantique. Une fenêtre d’optimisation ne doit pas autoriser à reproposer automatiquement une ancienne annonce identique.

## 4. Architecture cible recommandée

Réutiliser les services, contrats structurés, autorisations et primitives de workflow existants. Ne pas créer une infrastructure RAG parallèle ni une refonte générale du moteur d’agents.

Chaîne cible :

`collecter les sources → filtrer les sources connues → sélectionner des sujets structurés → contrôler la nouveauté finale → réserver/mémoriser les sujets approuvés → rédiger à partir de cette sélection → valider le rapport → exporter → confirmer l’export`

### A. Sélection structurée et orchestration déterministe

Faire produire au modèle des propositions structurées contenant au minimum : identifiant temporaire, titre, résumé factuel, identifiants des sources, entités/produits, version ou date d’événement si connue, faits nouveaux revendiqués et références qui les étayent.

- Valider le schéma et les références aux candidats réellement collectés. Le modèle ne peut pas inventer des identifiants ou ajouter des sources non collectées.
- Résoudre les URL dans le code à partir des identifiants sources, pas à partir de chaînes inventées par le modèle.
- Déplacer la persistance hors des appels d’outil optionnels du rédacteur : étape de workflow explicite ou service applicatif obligatoire.
- La rédaction finale ne doit utiliser que les sujets approuvés. Produire des blocs structurés par identifiant de proposition et assembler le Markdown dans le code pour contrôler le nombre et l’identité des sujets.
- Conserver un contrôle du contenu éditorial : une association correcte d’identifiants ne suffit pas si le texte réintroduit un autre sujet. Réessai borné ou rejet, jamais export non validé.

### B. Mémoire enrichie, rétrocompatible

Prévoir une migration versionnée et transactionnelle. Séparer logiquement :

- l’événement/actualité et ses faits distinctifs ;
- les sources associées, URL canoniques, titres et résumés originaux ;
- la proposition éditoriale, sa langue et sa représentation sémantique ;
- l’exécution, les décisions et les états de persistance/export.

Conserver les identifiants existants, dates, `used` et données originales. Un réimport ou une relance ne doit pas remettre à faux un indicateur `used` déjà vrai. Ne pas confondre `used` avec « exporté dans un rapport ».

Identifier les embeddings par modèle, dimension et version de projection ; prévoir leur reconstruction. Une normalisation linguistique peut compléter les représentations originales, mais ne doit pas les remplacer.

### C. Déduplication en plusieurs niveaux

1. Identités exactes : URL normalisées/alias pertinents, titre, empreinte de contenu exploitable et identité d’événement lorsque suffisamment établie.
2. Recherche de voisins sémantiques dans l’historique avec une couverture mesurée ; comparaison également au sein de la sélection courante.
3. Pour les voisins plausibles ou les cas incertains, décision structurée : `Duplicate`, `MaterialUpdate`, `New`, `Uncertain`, avec références historiques et faits justifiant la différence.

Le modèle peut assister le niveau 3 mais sa réponse reste soumise à un schéma, à des références vérifiables et à une règle conservatrice. Ne pas prétendre que le cosinus seul ou un identifiant d’événement généré par le modèle garantit la nouveauté.

Calibrer les seuils sur les cas réels. **Ne pas simplement remplacer 0,92 par une valeur plus basse**, ni descendre artificiellement sous 0,678 pour faire passer un test. Évaluer aussi les faux positifs sur de véritables évolutions.

### D. Cohérence persistance/export et concurrence

SQLite et un fichier Markdown ne peuvent pas partager une transaction unique. Concevoir explicitement les états et les reprises : par exemple `Reserved`, `ReadyToExport`, `Exported`, `Failed`, ou un équivalent plus simple démontrant les mêmes garanties.

- Identifiant stable d’exécution et idempotence par proposition/export.
- Sérialiser la phase finale de validation/réservation pour les exécutions concurrentes partageant la même mémoire ; une transaction courte seule ne protège pas une comparaison sémantique faite auparavant.
- Ne pas garder une transaction SQLite ouverte pendant un appel de génération long.
- En cas d’échec d’écriture du fichier, conserver une reprise identifiable ; éviter de perdre définitivement une proposition parce qu’elle a été réservée sans jamais être livrée.
- Si le fichier existe après une interruption mais que l’état n’est pas confirmé, pouvoir réconcilier le résultat par identifiant/empreinte.
- Export immuable par exécution ; conserver éventuellement une copie « du jour » pour compatibilité.
- Respecter les autorisations filesystem/SQLite existantes dans tous les nouveaux chemins de service.

## 5. Remise à niveau de l’historique

Construire un importateur avec mode simulation, à exécuter d’abord sur une copie de la base et des rapports dans le workspace.

- Inventorier les rapports historiques disponibles, y compris ceux antérieurs à l’introduction de SQLite.
- Extraire les sujets, toutes les sources disponibles et la date historique. Ne pas utiliser la date d’import comme date de proposition.
- Réconcilier les sujets avec les lignes existantes, regrouper les reprises et conserver leur provenance.
- Ne pas interpréter les sections de diagnostic sans sujet comme des propositions.
- Ne pas reconstruire des rapports écrasés en inventant leur texte. Les lignes existantes et les journaux constituent une provenance partielle à signaler.
- Ne pas fusionner automatiquement les cas ambigus ; les présenter dans le rapport d’import.
- Démontrer l’idempotence d’un deuxième import.

Préparer une sauvegarde, un compte rendu de simulation et une procédure de retour arrière avant toute application à la base active. À défaut d’autorisation explicite de mise en production lors de l’implémentation, livrer la migration, le workflow révisé et la simulation sans modifier les fichiers actifs.

## 6. Points d’entrée dans le dépôt

Chemins relatifs à `E:/OD.MyLocalAgent` :

- `src/OD.MyLocalAgent.Core/ProposalMemoryService.cs` : filtre, projection, clé et persistance.
- `src/OD.MyLocalAgent.Core/IProposalMemoryService.cs` : contrats et résultats.
- `src/OD.MyLocalAgent.Core/ResearchDiscoverTool.cs` : filtrage avant traduction, collecte et diagnostics.
- `src/OD.MyLocalAgent.Core/SqliteTool.cs` : exposition et autorisations des opérations mémoire.
- `src/OD.MyLocalAgent.Core/KnowledgeEmbeddingService.cs` et `KnowledgeEmbeddingGeneratorFactory.cs` : embeddings existants.
- `src/OD.MyLocalAgent.Core/UrlCanonicalizer.cs` : normalisation des URL.
- `src/OD.MyLocalAgent.Core/WorkflowStepDefinition.cs` et `WorkflowResponseSchemaValidator.cs` : sorties structurées.
- `src/OD.MyLocalAgent.Workflows/SequentialWorkflowEngine.cs` : exécution des prompts et étapes.
- `src/OD.MyLocalAgent.Core/MyLocalAgentService.cs` : interaction avec le modèle.
- Tests : `ProposalMemoryServiceTests.cs`, `ResearchDiscoverToolTests.cs`, `ResearchDiscoverContractTests.cs`, `WorkflowEngineTests.cs`, `EditorialWorkflowSampleTests.cs`.

Vérifier les catalogues d’outils, descriptions, enregistrements DI et exemples lorsqu’un contrat change. Maintenir la compatibilité des workflows utilisant les outils existants ; ne pas imposer les règles éditoriales à toutes les étapes `ai.prompt` de l’application.

## 7. Ordre de réalisation

1. Ajouter des fixtures minimales issues des répétitions observées et les tests révélant les trous de mémorisation.
2. Implémenter les contrats structurés et la persistance obligatoire avec blocage de l’export en cas d’échec.
3. Ajouter le modèle de données rétrocompatible, les sources multiples et le suivi d’exécution.
4. Ajouter la détection d’événements répétés et le contrôle final, puis calibrer les seuils.
5. Ajouter l’import historique et la reconstruction des embeddings avec simulation.
6. Fournir le workflow révisé, les diagnostics et la documentation d’exploitation.
7. Valider sur copies, puis présenter les changements, preuves et étapes de déploiement.

Ces étapes peuvent être adaptées aux dépendances techniques découvertes. Ne pas s’arrêter à l’étape 2 en déclarant le problème entièrement résolu : elle corrige les oublis, pas les reprises inter-sites.

## 8. Tests et critères d’acceptation

Tests déterministes en CI, sans clé ni accès réseau. Utiliser des doubles pour les décisions du modèle et des fixtures d’embeddings pour tester l’algorithme ; distinguer ces tests d’une véritable évaluation de qualité du modèle local.

| Cas | Résultat exigé |
|---|---|
| Le rédacteur n’appelle aucun outil | La persistance reste effectuée par le code, ou l’export est bloqué ; jamais de rapport réussi non mémorisé |
| Même article, URL identique ou paramètres de tracking | Une seule proposition de l’événement |
| Même annonce C# via plusieurs sites et formulations | Rejet comme répétition ; référence au sujet antérieur |
| Deux candidats du même lot reprennent la même annonce | Un seul sujet conservé |
| Nouvelle version réelle du même produit | Admise si les faits nouveaux sont étayés |
| Page de releases connue avec nouvel événement | Ne pas la rejeter uniquement parce que son URL est connue |
| Source inconnue/inventée dans la sélection | Validation échouée, pas d’export |
| Nouveauté ambiguë | Sujet écarté avec statut explicite |
| Zéro nouveauté et collecte réussie | Rapport sans post, raison exacte |
| Mémoire indisponible ou embeddings incompatibles | Pas de validation silencieuse sur un historique incomplet |
| Panne entre persistance et export, puis relance | Reprise idempotente, ni doublon ni perte définitive |
| Deux exécutions concurrentes | Pas de double réservation/export de la même actualité |
| Migration ou import répété | Pas de doublon, données et indicateurs existants préservés |
| Annonce identique ancienne, hors fenêtre sémantique | Pas de réadmission automatique par simple expiration |

Les tests sémantiques actuels emploient notamment des vecteurs artificiels à 0,92 et 0,91. Ils prouvent le calcul du seuil, pas la qualité de reconnaissance des actualités. Fournir séparément un petit corpus annoté de doublons, évolutions réelles et cas ambigus, ainsi qu’un résultat d’évaluation avec le modèle local lorsqu’il est disponible. Ne pas télécharger de modèle ni appeler un service distant implicitement.

Commandes .NET : toujours ajouter explicitement `-m:1`.

```powershell
dotnet build -m:1
dotnet test -m:1 --filter "Category!=Integration"
```

Les tests d’intégration nécessitant le réseau ne doivent être lancés que sur demande explicite. Respecter les instructions `AGENTS.md`. Code et interfaces en anglais par défaut, commentaires de documentation XML en anglais ; conserver les rapports de veille en français. Si une interface est nécessaire, suivre MVVM avec MVVM Toolkit ; ne pas créer une interface uniquement pour cette correction.

## 9. Diagnostics et livrables

Pour chaque exécution, conserver : compteurs avant/après filtrage, sujets proposés/approuvés/rejetés/mémorisés/exportés, décisions et motifs, références aux antécédents, scores utiles, modèle et version de projection, nombre d’embeddings incompatibles, identifiant d’exécution et statut d’export. Éviter les secrets et les contenus externes complets dans les journaux.

Livrer :

1. Code et tests avec explication des garanties et limites.
2. Workflow révisé prêt à examiner, sans modification de Gmail.
3. Migration/import historique avec simulation et procédure de restauration.
4. Résultats des tests et évaluation sur les exemples réels ; identifier toute vérification non effectuée.
5. Instructions de déploiement précisant la mise à jour du runner installé, du workflow et de la base, afin de ne pas tester un nouveau code avec une ancienne configuration active.

Ne pas promettre une reconnaissance sémantique universelle parfaite. La garantie vérifiable est qu’aucun sujet ne contourne la mémorisation ni la validation, que les répétitions connues sont couvertes par des tests et que les cas ambigus ne sont pas exportés automatiquement.
