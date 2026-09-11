# Pro-Spec — modèles de documents et de prompts

Ces trames sont des **propositions réutilisables**, tirées des rôles observés dans MyLocalAgent. Les champs entre crochets sont à remplacer. Les identifiants et les statuts sont des conventions proposées, pas une nomenclature historique du projet.

Un prompt d’exécution renvoie vers un contrat conservé dans le dépôt. Il ne doit pas devenir une deuxième spécification qui diverge de la première. Pour un petit lot, les sections peuvent être regroupées, mais le résultat attendu, les exclusions et les preuves doivent rester explicites.

## 1. Organisation minimale

```text
README.md                         Présentation et démarrage du produit
AGENTS.md                         Règles de travail et point d’entrée pour l’IA
docs/
  specification.md                Contrat commun en vigueur
  lots/README.md                  Ordre, dépendances et statuts
  lots/L-001.md                   Mandat du lot
  ledger.md                       Décisions et mémoire des livraisons
  acceptance.md                   Vérifications, preuves et réserves
  technique.md                    Contrats et fonctionnement utiles
  utilisateur.md                  Parcours et limites d’usage
  examples/                       Exemples vérifiables
  archives/                       Documents remplacés ou abandonnés
```

Les noms sont libres. Un document autonome de décision, un rapport par lot ou plusieurs guides sont possibles lorsque le volume le justifie. L’index doit alors pointer vers le bon fichier.

## 2. Instructions de travail de l’IA

```markdown
# Règles de travail du projet

## Point d’entrée
Lire [index des lots], puis la fiche active et ses lectures obligatoires.
Consulter [spécification] et [ledger] avant de modifier le projet.
Les archives ne sont pas des mandats d’implémentation.

## Environnement et commandes
- Prérequis : [environnement réellement requis].
- Compilation : [commande exacte].
- Tests autonomes : [commande exacte].
- Tests conditionnels : [conditions et autorisation nécessaires].
- Autres vérifications : [commande ou procédure].

## Conventions
[Langue du produit, style du code, architecture, documentation.]

## Mandat et limites d’action
[Actions déléguées ; décisions à soumettre ; environnements concernés.]
Ne pas déclarer un contrôle réussi s’il n’a pas été exécuté.
Ne pas modifier les critères pour masquer un échec.

## Clôture
Actualiser les documents concernés, le ledger, le rapport d’acceptation et l’index.
Présenter les résultats et les réserves avec leurs preuves.
```

Dans MyLocalAgent, conserver les règles applicables, notamment `-m:1` sur chaque `dotnet build` et `dotnet test`, et les tests d’intégration uniquement sur demande explicite. Le modèle ci-dessus ne remplace pas ces instructions.

## 3. Spécification partagée

```markdown
# Spécification de [produit]

Statut : [proposition / active / remplacée]
Révision : [identifiant et date]
Responsable des arbitrages : [personne ou rôle]
Remplace : [document ou aucun]

## 1. Problème et résultat utile
[Qui rencontre quel problème ? Quel résultat doit changer ?]

## 2. Utilisateurs et parcours essentiels
[Actions attendues et résultats observables.]

## 3. Périmètre et exclusions
[Ce que le produit couvre ; ce qu’il ne prend pas en charge.]

## 4. Règles durables
| ID | Règle | Motif | Vérification |
|---|---|---|---|
| R-001 | [...] | [...] | [...] |

## 5. Architecture et contraintes communes
[Responsabilités des composants, dépendances, données, environnement.]

## 6. Contrats partagés
[Formats, interfaces, valeurs, erreurs, compatibilité et limites.]

## 7. Qualité et acceptation du produit
[Parcours à prouver ; contraintes mesurables ; contrôles humains.]

## 8. Organisation du travail
[Lien vers l’index ; protocole de lot ; règles de modification des décisions.]

## 9. Décisions ouvertes
[Question précise, conséquence et responsable de la réponse.]

## 10. Révisions
[Décision, date, portée, lien vers le ledger et documents affectés.]
```

Éviter les promesses invérifiables comme « ne plante jamais ». Décrire les erreurs prises en charge, la conservation des données et les conditions de reprise effectivement exigées.

## 4. Index des lots

```markdown
# Lots du projet

Spécification active : [lien et révision]
Décisions en vigueur : [lien]

## Statuts
Proposé ; prêt ; en cours ; bloqué ; à accepter ; accepté ; remplacé ; abandonné.
Le statut de déploiement est suivi séparément.

## Ordre et dépendances
| ID | Résultat attendu | Dépend de | Fiche active | Statut | Preuves et réserves | Déploiement |
|---|---|---|---|---|---|---|
| L-001 | [...] | [...] | [...] | [...] | [...] | [...] |

## Documents remplacés
| Document | Statut | Successeur | Motif connu |
|---|---|---|---|
| [...] | [...] | [...] | [...] |
```

Un lot « à accepter » peut avoir tous ses tests techniques réussis. L’acceptation se note avec son auteur ou sa règle de délégation ; le déploiement indique l’environnement et la version. Le numéro du lot reste stable même si son ordre d’exécution change.

## 5. Fiche de lot

```markdown
# L-[NNN] — [résultat observable]

Statut : [proposé / prêt / en cours / bloqué / à accepter / accepté]
Révision : [date et identifiant]
Dépendances : [lots et réserves applicables]
Autorisation de travail : [référence de la décision ou du mandat]
Remplace ou modifie : [document et passages précis, ou aucun]

## 1. Lire avant de commencer
- [Instructions de travail.]
- [Spécification et sections applicables.]
- [Décisions en vigueur et entrées pertinentes du ledger.]
- [Lots dépendants.]
- [Documentation technique et utilisateur concernée.]
- [Sources, tests et exemples à examiner.]

## 2. Mission et résultat attendu
[Situation de départ → comportement à obtenir → utilité.]

## 3. Périmètre
Inclus : [...]
Exclus : [...]

## 4. Décisions déjà prises
| ID | Décision | Motif | Référence de validation |
|---|---|---|---|
| D-[NNN] | [...] | [...] | [...] |

## 5. Contraintes héritées
[Règles à préserver et liens vers leurs définitions.]

## 6. Contrats et comportements
[Entrées, sorties, valeurs absentes, erreurs, droits, limites, compatibilité.]
[Parcours utilisateur et effets attendus.]

## 7. Ordre de réalisation
[Sous-travaux et dépendances ; essai de faisabilité si nécessaire.]

## 8. Critères et vérifications
| Critère | Résultat attendu | Moyen de vérification | Conditions |
|---|---|---|---|
| C-001 | [...] | [...] | [...] |
Inclure les refus, erreurs et non-régressions pertinents.

## 9. Documents à maintenir
| Document | Mise à jour attendue ou raison de non-applicabilité |
|---|---|
| Spécification | [...] |
| Ledger | [...] |
| Index | [...] |
| Acceptation | [...] |
| README, guides, exemples, audits | [...] |

## 10. Point de validation du lot
[Commandes exactes et contrôles manuels.]
[Conditions bloquantes ; traitement autorisé des réserves.]

## 11. Livraison et reprise
[Compte rendu demandé, preuves, décisions ouvertes, prochaine action.]
```

Pour un essai de faisabilité, ajouter avant son exécution : question, durée ou périmètre maximal, conditions de réussite et solution de repli. Un essai raté peut être utile s’il tranche la question ; il ne vaut pas acceptation de la fonctionnalité.

## 6. Entrée de ledger et décision remplacée

```markdown
## [Date] — L-[NNN] — [objet]

État examiné : [commit ou état local précisément décrit]
Statut du lot : [...]

### Résultat livré
[Changements significatifs et fichiers ou composants concernés.]

### Décisions
| ID | Problème | Décision et motif | Portée | Validation |
|---|---|---|---|---|
| D-[NNN] | [...] | [...] | [...] | [...] |

### Contrats et dépendances
[Interfaces, formats, compatibilité, versions ajoutées ou modifiées.]

### Écarts à la spécification
[Attendu, constaté, correction ou décision autorisant le changement.]

### Vérification
[Lien vers les critères et preuves du rapport d’acceptation.]

### Documentation
[Documents actualisés et éléments non concernés.]

### Réserves et suite
[Limite, conséquence, action, responsable et dépendances bloquées.]
```

Pour remplacer une décision, conserver l’entrée ancienne et ajouter : « D-014 remplace D-006 uniquement sur [portée], à compter de [révision], pour [motif], sur décision de [référence]. » Mettre à jour le résumé des décisions actives. Ne pas attribuer une validation humaine si elle n’a pas été donnée.

## 7. Rapport d’acceptation

```markdown
# Acceptation de L-[NNN]

Date : [...]
État testé : [commit ; changements locaux éventuels]
Environnement : [...]
Rapport établi par : [...]

## Résultats
| Critère | Attendu | Observation | État | Preuve | Limite / suite |
|---|---|---|---|---|---|
| C-001 | [...] | [...] | Réussi / Échoué / Non exécuté / Non applicable | [...] | [...] |

## Conditions des vérifications
[Commandes, configuration utile, données de test, doubles, modèle si évalué.]
[Aucun secret ni contenu personnel inutile dans les preuves.]

## Réserves
| ID | Impact | Action | Responsable | Bloque quelle suite ? |
|---|---|---|---|---|
| RES-001 | [...] | [...] | [...] | [...] |

## Décision d’acceptation
[En attente / acceptée / acceptée avec réserves / refusée.]
[Auteur ou règle de délégation, date, portée.]

## Déploiement
[Non demandé / non effectué / environnement, version et preuve.]
```

## 8. Documentation d’usage, technique et exploitation

| Trame | Sections à conserver |
|---|---|
| README | But ; état disponible ; prérequis ; démarrage ; limites essentielles ; liens vers guides, exemples et référence technique. |
| Guide de première utilisation | Prérequis ; installation ; configuration ; premier parcours ; résultat attendu ; diagnostic ; vérifications restantes. |
| Guide utilisateur | Parcours par objectif ; permissions et choix ; étapes ; exemples ; erreurs ; limites ; repères de version. |
| Référence technique | Terminologie ; formats ; contrats d’entrée/sortie ; erreurs ; compatibilité ; exemples ; validation. |
| Note d’architecture | Périmètre ; responsabilités ; dépendances ; flux ; décisions et motifs ; liens vers le code et le ledger. |
| Procédure d’exploitation | Version et état initial ; sauvegarde ; ordre des opérations ; contrôles ; retour arrière ; état final ; limites. |
| Audit ciblé | Objet ; version ; périmètre ; méthode ; résultats avec preuves ; réserves et responsables. |
| Archive | Statut historique ; document remplacé ; successeur ; portée ; raison connue de l’abandon ou du remplacement. |

Maintenir seulement les sections touchées par le lot, puis vérifier qu’elles restent cohérentes avec le reste. Pour des guides multilingues, vérifier la couverture des mêmes parcours et exemples ; cette parité ne dispense pas d’une relecture linguistique.

## 9. Prompts réutilisables

### A. Cadrer un projet

```text
Nous allons réaliser [produit] avec Pro-Spec. Le besoin est [problème et public].

Examine d’abord le dépôt s’il existe. Prépare une spécification partagée et un index
de lots à partir du besoin. Sépare les faits constatés, les décisions déjà prises,
les hypothèses et les propositions. Définis les exclusions et les critères de réussite.

Propose des lots vérifiables selon leurs dépendances, avec un premier résultat utile
et les essais nécessaires pour lever les inconnues critiques. Identifie les questions
qui exigent réellement mon arbitrage. À ce stade, livre les documents de cadrage ;
la présente demande n’autorise pas encore l’implémentation du produit.
```

### B. Préparer un lot

```text
Prépare la fiche du lot [ID] pour obtenir [résultat].
Lis [index], [spécification], [ledger] et les sources concernées.
Vérifie les dépendances et les décisions remplacées. Décris inclusions, exclusions,
contrats, erreurs, compatibilité, vérifications et documents à maintenir.
Propose des critères observables avant toute implémentation.
Signale les contradictions avec leurs références et prépare les arbitrages nécessaires.
Ne lance pas encore l’implémentation : le livrable demandé est la fiche prête à examiner.
```

### C. Exécuter un lot autorisé

```text
Implémente le lot [ID] selon sa fiche active [chemin et révision].
Cette demande autorise le travail décrit dans cette fiche.

Applique les instructions du dépôt. Effectue les lectures obligatoires, puis examine
l’état réel des fichiers et des tests. Signale tout écart qui affecte le mandat ;
ne réouvre pas les décisions déjà tranchées sans raison nouvelle.

Réalise le lot dans son périmètre. Exécute les vérifications applicables et corrige
les échecs relevant du lot. Consigne les décisions importantes au fil du travail.
Maintiens la spécification, le ledger, les guides, les exemples et l’index concernés.

À la fin, fournis le résultat, les preuves, les contrôles non exécutés et les réserves.
Ne confonds pas fin de l’implémentation, acceptation et déploiement. Respecte les
autorisations de déploiement déjà données ; ne les suppose pas si elles manquent.
```

### D. Reprendre dans une nouvelle session

```text
Reprends le lot [ID]. Commence par [index], la fiche active, les décisions en vigueur,
le ledger, le dernier rapport de vérification et le point de reprise s’il existe.
Examine l’état réel du dépôt et les changements locaux avant toute modification.

Retrouve le résultat attendu, ce qui est terminé, ce qui reste et les preuves encore
applicables. Distingue travail incomplet et choix laissé volontairement hors périmètre.
Poursuis le mandat autorisé. Ne recommence pas les parties terminées sans raison
et ne transforme pas les anciens résultats de tests en preuve de l’état courant.
```

### E. Préparer un changement de décision

```text
Nous envisageons de remplacer [décision] par [intention nouvelle].
Analyse les impacts sur les usages, les contrats, les données, les lots et les preuves.
Présente les options utiles avec leurs conséquences. Prépare la révision documentaire
et les vérifications nécessaires ; identifie précisément l’arbitrage attendu.
Ne traite pas une intention à examiner comme une décision déjà validée.
```

### F. Corriger un défaut après livraison

```text
Analyse puis corrige [symptôme] dans le périmètre [limite].
Distingue faits observés, reproduction, causes confirmées et hypothèses.
Examine le contrat attendu avant de modifier le code. Ajoute la non-régression utile,
corrige la cause et vérifie le résultat sur le cas d’origine lorsque c’est possible.

Actualise les documents concernés et le ledger. Si des données ou une installation
active sont affectées, prépare la procédure, les contrôles et le retour arrière.
Applique uniquement les opérations couvertes par le mandat déjà donné.
```

### G. Examiner une livraison

```text
Examine la livraison du lot [ID] contre sa fiche active et les décisions applicables.
Lis les changements et les preuves, puis recherche les exigences non couvertes,
les exclusions dépassées, les exemples obsolètes et les réserves masquées.

N’accorde pas de valeur probante à une simple déclaration de réussite. Pour chaque
problème, indique le critère, l’observation, l’impact et la correction attendue.
Conserve la distinction entre contrôle technique et acceptation du produit.
Le livrable de cette demande est un rapport de revue.
```

### H. Entretenir la mémoire

```text
Vérifie la cohérence des documents de gouvernance : index, fiches actives,
spécification, ledger, rapports d’acceptation et archives.
Repère liens cassés, décisions incompatibles, anciens documents encore présentés
comme actifs et réserves sans suite. Corrige les incohérences documentaires dont
la résolution est déjà établie. Soumets les conflits d’intention non tranchés.
Conserve l’historique et prépare un résumé sourcé des décisions en vigueur.
```

## 10. Exemple court, fictif et indépendant de MyLocalAgent

Lot L-004 : permettre à un utilisateur d’exporter la liste filtrée de ses dossiers.

**Mandat.** Ajouter l’export CSV de la sélection visible. Exclure l’envoi par courriel et l’export planifié. Conserver les droits d’accès existants. Les valeurs comportant des séparateurs ou des retours à la ligne doivent rester lisibles à la réouverture.

**Critères avant implémentation.** C-01 : seules les lignes autorisées et filtrées sont exportées. C-02 : les valeurs spéciales sont conservées. C-03 : l’annulation laisse les données inchangées. C-04 : le guide décrit le même parcours que l’interface.

**Décision pendant le lot.** Le séparateur doit être choisi pour l’usage visé. Si le contrat n’en prévoit aucun et que plusieurs choix changent le résultat pour l’utilisateur, obtenir cette décision ; ne pas présenter une préférence technique comme une demande initiale.

**Rapport fictif.** C-01 et C-02 réussis sur les jeux de données indiqués ; C-03 réussi par test et inspection ; C-04 non exécuté car le parcours manuel reste à vérifier. Lot techniquement vérifié sur trois critères, proposé à l’acceptation avec une réserve. Aucun déploiement n’est déduit de cet état.

**Reprise.** La session suivante retrouve la fiche, la décision de format, les preuves et le parcours restant. Elle termine ce contrôle et les documents concernés, sans reconstruire le besoin depuis une conversation.
