# Enseigner Pro-Spec

## 1. Intention du cours

Faire comprendre pourquoi un travail logiciel largement confié à l’IA réclame une mémoire, un mandat et des preuves ; montrer comment Pro-Spec répond à ce besoin ; apprendre à appliquer la méthode et à en reconnaître les limites.

Le fil conducteur proposé est : **l’IA peut accomplir davantage de travail ; il faut rendre ce travail gouvernable dans la durée.** La création empirique de Pro-Spec fournit un cas concret pour expliquer ce besoin et les moyens de le traiter.

Cette progression est une proposition pédagogique. Elle n’attribue pas à l’auteur l’invention des spécifications, des lots ou des revues. Le corpus montre une combinaison locale de pratiques ; il ne suffit pas à établir une chronologie générale des méthodes ou des capacités des modèles.

## 2. Poser les mots avant d’enseigner les documents

Un **projet centré sur l’IA** désigne ici un projet dont l’IA prend en charge une part importante de la réalisation. Cela ne signifie pas que le logiciel construit contient nécessairement un modèle d’IA. MyLocalAgent se trouve dans les deux situations : le cours doit les distinguer.

Un **harnais** est l’environnement de travail qui permet à l’agent d’utiliser des outils, de consulter le dépôt, de modifier les fichiers et de vérifier ses actions. Un **modèle frontière** désigne, dans le discours du cours, un modèle parmi les plus capables de son époque. Aucun modèle précis n’est nécessaire pour expliquer Pro-Spec.

Présenter l’évolution du besoin par trois **situations pédagogiques**, sans les confondre avec des périodes historiques universelles :

| Situation | Travail confié | Question de contrôle |
|---|---|---|
| Aide ponctuelle | Expliquer ou produire un fragment. | Ce fragment répond-il à la demande ? |
| Intervention dans un dépôt | Modifier plusieurs fichiers et exécuter des vérifications. | Le changement est-il correct et cohérent avec l’existant ? |
| Réalisation continue par lots | Enchaîner conception, implémentation, maintenance et reprises. | Le projet garde-t-il son cap et sa mémoire d’une intervention à l’autre ? |

Pour un chapitre historique daté sur les harnais et les modèles, ajouter ultérieurement des sources primaires et des exemples vérifiés. Le présent dossier fournit l’étude du besoin et la matière Pro-Spec, pas une histoire documentée de toute l’industrie.

## 3. Progression proposée

| Séquence | Question de départ | Matière et démonstration | Acquisition attendue |
|---|---|---|---|
| 1. Confier plus de travail | Que faut-il encore décider lorsque l’IA sait modifier et tester un projet ? | Comparer les trois situations ci-dessus. Faire distinguer résultat souhaité, travail délégué et preuve. | Situer la responsabilité humaine sans confondre autonomie et absence de contrôle. |
| 2. Perdre le fil | Pourquoi ne suffit-il pas de redonner un bon prompt ? | Scénario : nouvelle session, ancienne décision oubliée, extension qui casse un contrat. | Identifier les besoins de mémoire et de limites de périmètre. |
| 3. Observer une réponse empirique | Qu’a-t-on écrit pour éviter de tout répéter ? | Master prompt, README du pack et structure du premier lot. | Comprendre le passage à une spécification commune et des mandats successifs. |
| 4. Exécuter un lot | Que doit savoir l’IA avant de commencer, et que doit-elle laisser ? | Fiche de lot, ledger et rapport d’acceptation. | Savoir préparer, réaliser, vérifier et transmettre un lot. |
| 5. Faire évoluer le produit | Comment changer une décision sans perdre l’histoire ? | V1/V2, archives, lots préparatoires et addenda. | Distinguer décision active, décision remplacée et défaut du code. |
| 6. Exiger les bonnes preuves | Que démontre réellement un résultat vert ? | Gate technique, contrôle humain et brief correctif distinguant tests et qualité IA. | Délimiter la portée d’une preuve ; formuler des réserves honnêtes. |
| 7. Voir les limites de Pro-Spec | Quand les documents deviennent-ils eux-mêmes difficiles à gouverner ? | Index obsolète, règle historique encore visible, ledger volumineux. | Diagnostiquer le besoin de cohérence, de traçabilité et de séparation des rôles documentaires. |
| 8. Reprendre et expliquer | Une nouvelle session peut-elle continuer sans récit oral ? | Échange des dossiers entre groupes ; lecture du mandat, des décisions et des preuves. | Vérifier l’utilité réelle de la mémoire et savoir décrire Pro-Spec avec des critères précis. |

Chaque séquence doit partir d’un problème ou d’un artefact court. Montrer l’ensemble du dossier documentaire dès le début risquerait de faire passer Pro-Spec pour une quantité de fichiers à produire.

## 4. Pièces du dépôt à utiliser en cours

### Le prompt unique et le lot initial

Montrer seulement les rubriques du [master prompt archivé](../Prompts/OldSpecs/OD.MyLocalAgent-MasterPrompt.md), puis celles du [premier lot](../Prompts/01-Stage0-Foundation.md). Ajouter le tableau de révision du [README du pack](../Prompts/README.md).

Question : où trouve-t-on désormais ce qui est commun à tout le produit, ce qui est autorisé maintenant et ce qui prouve que l’étape est terminée ?

Conclusion attendue : le changement porte sur l’organisation et la vérification du travail, pas seulement sur la longueur du texte envoyé à l’IA.

### Une décision conservée pour la session suivante

Choisir une entrée du [ledger](../ARCHITECTURE-LEDGER.md) avec décision, motif et vérification. Masquer les détails métier inutiles. Faire lire ensuite une fiche qui impose de respecter ce contrat.

Question : quelles informations manqueront si l’on conserve uniquement le dernier compte rendu de conversation ?

Conclusion attendue : le registre permet de reprendre les raisons d’un choix, pas seulement de constater le fichier modifié.

### Une preuve partielle honnêtement décrite

Utiliser une ligne du [rapport d’acceptation](../acceptance-report.md) où le contrôle réel dépend de credentials et où un parcours de remplacement a été exécuté.

Question : qu’est-ce qui a réussi exactement, qu’est-ce qui reste inconnu et quelle suite est raisonnable ?

Conclusion attendue : un test de remplacement ne transforme pas le parcours non testé en réussite. Une réserve exploitable nomme l’action restante.

### Un document qui ne doit plus commander le travail

Comparer la règle [OldSpecs](../Prompts/OldSpecs/READM.MD) et les références aux lots 24/25 dans l’index. Ne pas demander aux apprenants de résoudre les choix fonctionnels de MyLocalAgent.

Question : quelle information doit être corrigée avant de donner ce pack à une nouvelle session ?

Conclusion attendue : l’autorité documentaire doit être identifiable. L’existence d’un fichier et son numéro ne suffisent pas.

### Des tests corrects qui ne prouvent pas tout

Utiliser le §8 du [brief anti-répétition](../Prompts/Discover-AntiRepetition-Implementation-Brief.md). Il distingue les tests contrôlés du mécanisme et l’évaluation de reconnaissance sur un modèle réel.

Question : quelle promesse peut-on faire avec chaque type de preuve ?

Conclusion attendue : on peut prouver une obligation d’enregistrement et un traitement des cas ambigus sans promettre une reconnaissance sémantique parfaite.

## 5. Exercice fil rouge

Prendre un produit fictif simple : un outil de suivi de dossiers avec recherche et export. Il n’a pas besoin de contenir de l’IA. Fournir une demande vague : « ajouter un export pratique ».

1. Les apprenants identifient les questions qui changent réellement le contrat : sélection, format, droits, destination et traitement des erreurs.
2. Ils écrivent une fiche de lot d’une page avec résultat, exclusions et critères.
3. L’enseignant introduit un imprévu : une contrainte de format ou de compatibilité découverte pendant l’implémentation. Les apprenants choisissent entre décision technique autorisée et arbitrage du produit.
4. Ils rédigent une entrée de ledger et un rapport comportant un contrôle non exécuté.
5. Un autre groupe reprend le lot avec les seuls documents, sans explication orale.
6. L’enseignant ajoute un ancien document contradictoire. Le groupe doit identifier ce qui fait autorité ou formuler l’arbitrage manquant.
7. Les apprenants révisent les documents qui ont rendu la reprise difficile et expliquent pourquoi chaque correction était nécessaire.

Critères d’évaluation : le lot est-il borné ? Les règles applicables sont-elles retrouvables ? Les critères sont-ils observables ? Les preuves sont-elles correctement limitées ? La reprise fonctionne-t-elle ? Les documents actifs se contredisent-ils ?

L’exercice peut être réalisé sans coder : l’apprentissage visé est la gouvernance. Pour une version avec implémentation, vérifier ensuite que les critères rédigés permettent effectivement de contrôler le logiciel.

## 6. Décrire la méthode de façon précise

Pour que les apprenants sachent ce qu’ils appliquent, reprendre les mêmes questions à chaque exercice :

| Dimension | Question sur Pro-Spec |
|---|---|
| Finalité | Quel problème de continuité ou de contrôle cherche-t-on à résoudre ? |
| Unité de travail | Quel résultat constitue un lot cohérent et vérifiable ? |
| Entrées | Quels documents et quel état faut-il connaître avant d’agir ? |
| Activités | Que fait l’IA avant, pendant et après le lot ? |
| Sorties | Quelles modifications, preuves et traces doit-elle laisser ? |
| Autorité | Comment reconnaître les décisions en vigueur et traiter les contradictions ? |
| Validation | Quelles conditions permettent de poursuivre ou d’accepter le résultat ? |
| Responsabilités | Que peut décider l’IA et que doit trancher la personne responsable ? |
| Changement | Comment remplacer une décision sans perdre son histoire ? |
| Limites | Qu’est-ce que les documents et les vérifications ne garantissent pas ? |

Ces rubriques donnent une description autonome et examinable de Pro-Spec. Aucun rapprochement avec une autre méthode n’est nécessaire à ce stade.

## 7. Texte d’introduction proposé

> Au début, nous pouvons demander une fonction, lire la réponse et décider si elle convient. Mais lorsque l’IA intervient sur une application entière, le problème s’élargit. Il faut retrouver les choix déjà faits, savoir ce que l’on peut changer et vérifier que le travail prépare correctement la suite.
>
> Dans MyLocalAgent, cette organisation s’est construite au fil des besoins : une spécification partagée, des lots, des points de validation, un registre des décisions et une documentation maintenue avec le logiciel. Nous appelons ici cette pratique Pro-Spec.
>
> Nous allons l’étudier à partir de ses documents réels, y compris leurs imperfections. L’objectif est de savoir préparer un mandat clair, vérifier ce qui a été livré et laisser une mémoire qui permette à une nouvelle session de continuer le travail.

## 8. Ce qu’il faut éviter dans le discours

Éviter le récit « avant l’IA, pas besoin de méthode ». Les objectifs, les décisions et les tests existaient déjà ; le cours traite de leur organisation lorsque l’IA réalise une part importante du travail.

Éviter d’attribuer à Pro-Spec des gains mesurés que le corpus n’établit pas. On peut montrer ses mécanismes et ses traces, pas en déduire un pourcentage de productivité ou une supériorité générale.

Éviter de présenter les incohérences du dépôt comme une disqualification. Elles rendent visible le problème de maintien de la mémoire et expliquent les améliorations proposées.

Éviter d’enseigner un nombre obligatoire de fichiers. Faire juger la méthode sur la clarté du mandat, la continuité, le coût documentaire, la qualité des preuves, la possibilité de reprise et la responsabilité des décisions.
