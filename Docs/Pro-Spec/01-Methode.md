# Pro-Spec — gouverner un projet logiciel réalisé avec l’IA

## 1. Le problème auquel la méthode répond

Lorsque l’IA aide à écrire une fonction isolée, une demande et une vérification peuvent suffire. Lorsqu’elle intervient sur une application entière, il faut conserver des objectifs, des choix, des interfaces et des règles sur plusieurs sessions. Une réponse satisfaisante aujourd’hui peut compromettre un choix fait la veille ou préparer une difficulté pour le lot suivant.

L’outil de travail de l’IA — son **harnais**, c’est-à-dire l’environnement qui lui permet de lire les fichiers, modifier le projet et lancer des vérifications — rend possible un travail plus continu. Il ne décide pas à lui seul de ce que le produit doit devenir, de ce qui fait autorité ni de ce qui suffit à déclarer une livraison acceptable.

**Interprétation.** Plus on peut confier de travail à l’IA, plus on a besoin d’expliciter les limites de cette délégation. La méthode doit permettre de répondre à cinq questions : que veut-on construire, qu’est-ce qui est déjà décidé, que peut-on changer maintenant, comment vérifier le résultat et que faut-il laisser pour la suite ?

Dans MyLocalAgent, le README du pack décrit le remplacement d’un prompt unique par une spécification commune, des étapes successives et des contrôles exécutés dans le dépôt. C’est un point de départ documentaire concret, sans supposer une histoire générale du développement avec l’IA. Voir [le pack et son tableau de révision](../Prompts/README.md).

## 2. Définition et domaine d’emploi

**Pro-Spec est une méthode empirique de gouvernance de projets logiciels réalisés par ou avec l’IA. Elle organise le travail autour d’une intention écrite, de lots bornés, de points de validation fondés sur des preuves et d’une mémoire maintenue dans le projet.**

Gouverner signifie ici décider du résultat attendu, donner un mandat de travail, contrôler le résultat et garder une trace des décisions. Cela peut être assuré par une seule personne avec une IA ; aucun comité n’est nécessaire.

Un lot est un ensemble cohérent de changements que l’on peut réaliser, vérifier et expliquer avant de passer au suivant. Un *gate*, parfois appelé gateway dans la discussion, est un **point de validation** : des conditions précises déterminent si l’on peut poursuivre. Le *ledger* est le **registre des décisions et des livraisons**.

Pro-Spec convient quand les travaux successifs se dépendent, que le projet dépasse une session ou que les conséquences d’un changement traversent plusieurs parties du logiciel. Le nombre de fichiers et le langage de programmation ne suffisent pas à déterminer ce besoin. Un petit programme manipulant des données importantes peut exiger une forte discipline ; une modification simple dans une grande application peut rester légère.

La méthode ne démontre pas que l’IA comprend tout le projet, ne garantit pas l’absence de défaut et ne remplace pas l’expérience technique. Son objectif est de rendre les décisions visibles et les résultats contrôlables.

## 3. Ce que le dépôt permet d’affirmer

**Observé.** Les premières étapes possèdent déjà une structure stable : lectures préalables, mission, contraintes, périmètre, règles de travail, commandes de validation et compte rendu final. La spécification commune impose de lire le ledger avant de modifier le dépôt et d’y ajouter une entrée avant de terminer.

**Observé.** Les extensions enrichissent cette structure : décisions numérotées et verrouillées, contrats détaillés, compatibilité, cas d’échec, tests obligatoires, documentation concernée, liste d’acceptation et recherche finale des endroits à réconcilier. Des lots préparatoires sont insérés avant des extensions qui en ont besoin.

**Observé.** La pratique accepte les révisions et les abandons. Des versions sont déclarées remplacées ; d’anciennes propositions sont conservées dans un dossier qui interdit leur implémentation. Des corrections après livraison prennent aussi la forme de briefs fondés sur des symptômes et des preuves.

**Limite de l’observation.** Il n’existe pas un gabarit unique respecté par tous les lots. Certains documents récents sont beaucoup plus courts et mêlent description du contrat et résultat livré. Il n’existe pas non plus, dans le corpus recensé, un état central uniforme de tous les lots et de toutes leurs réserves. Les règles homogènes proposées ci-dessous comblent ces manques ; elles ne sont pas présentées comme déjà appliquées partout.

Sources représentatives : [spécification, §3](../Prompts/00-Shared-Specification.md), [lot initial](../Prompts/01-Stage0-Foundation.md), [lot détaillé](../Prompts/28-Stage28-WorkflowStepEditor.md), [règle des archives](../Prompts/OldSpecs/READM.MD), [brief correctif](../Prompts/Discover-AntiRepetition-Implementation-Brief.md).

## 4. Les documents : une question et une responsabilité par document

L’unité importante est le rôle documentaire. Plusieurs rôles peuvent tenir dans un même fichier tant qu’ils restent reconnaissables.

| Document | Question traitée | Structure utile | Quand le maintenir |
|---|---|---|---|
| README du produit | Qu’est-ce que ce logiciel et comment commencer ? | Présentation, état livré, prérequis, démarrage, limites, liens vers les guides. | Dès qu’un changement affecte ce qu’un nouvel utilisateur doit savoir. |
| Instructions de travail de l’IA | Quelles règles appliquer dans ce dépôt ? | Commandes, conventions, limites d’action, point d’entrée documentaire. | Quand une règle de travail change. Les choix fonctionnels détaillés restent dans la spécification. |
| Spécification partagée | Quel produit veut-on construire et sous quelles contraintes ? | Objectif, utilisateurs, périmètre, règles durables, architecture, contrats communs, critères globaux d’acceptation. | Au cadrage, puis à chaque changement accepté du contrat commun. |
| Index des lots | Que faut-il faire, dans quel ordre, avec quelle version des documents ? | Identifiant, but, dépendances, fichier actif, statut, réserves, remplacement éventuel. | À la préparation, à la révision et à la clôture d’un lot. |
| Fiche de lot | Quel est le mandat de cette intervention ? | Lectures, résultat attendu, inclus/exclus, décisions, contrats, plan, tests, documentation, point de validation. | Avant le travail ; pendant celui-ci uniquement pour une clarification ou une révision explicitement tracée. |
| Ledger | Qu’a-t-on décidé ou appris, et qu’est-ce qui a changé ? | Décisions en vigueur, versions pertinentes, entrées datées : motifs, changements, contrats, écarts, preuves, suites. | À chaque décision significative et avant la fin du lot. |
| Rapport d’acceptation | Qu’a-t-on réellement vérifié ? | Critère, résultat attendu, observation, état, preuve, limite et action restante. | Au fil des vérifications et à la clôture. |
| Documentation technique | Comment utiliser ou faire évoluer correctement les contrats du logiciel ? | Formats, interfaces, règles, limites, erreurs, exemples, architecture et exploitation selon les besoins. | Quand les contrats ou le fonctionnement changent. |
| Documentation utilisateur | Comment accomplir une tâche avec la version livrée ? | Mise en route, parcours, prérequis, résultat attendu, erreurs fréquentes, limites. | Quand les usages changent ; maintenir les langues concernées ensemble. |
| Exemples et audits spécialisés | Dispose-t-on d’exemples valides et de preuves sur un risque particulier ? | Exemples exécutables ; pour chaque audit : périmètre, méthode, observation, statut, suite. | Dès qu’un changement invalide un exemple ou une preuve. |
| Archives | Qu’a-t-on remplacé ou abandonné ? | Ancien document, statut explicite, motif connu, lien vers le successeur. | Lors du remplacement, avec correction des liens actifs. |

**Observé.** MyLocalAgent possède ces rôles, parfois dispersés ou combinés. Son manuel technique est notamment une référence pour les auteurs de tâches et de workflows : ce n’est pas, à lui seul, un manuel exhaustif du code interne. Le ledger et les autres documents techniques complètent cette fonction.

**Proposition.** Ajouter à chaque document qui prescrit du travail un identifiant stable, un statut, une date de révision et, s’il existe, le document remplacé. L’index doit permettre de distinguer immédiatement une proposition d’un mandat actif. Le README utilisateur n’a pas à porter l’historique de tous les lots.

## 5. L’autorité : distinguer l’intention, la décision et le fait

La spécification décrit un comportement voulu. Le code permet d’observer un comportement réalisé. Le ledger explique certaines différences. Un test prouve une propriété dans les conditions où il a été exécuté. Aucun de ces éléments ne remplace les trois autres.

**Observé.** La spécification initiale se déclare prioritaire sur les prompts de lot ; les lots 26 à 28 donnent aussi une autorité à l’état du dépôt et au ledger lorsqu’un ancien prompt contredit le comportement réalisé. Cette évolution est raisonnable pour éviter les retours en arrière, mais elle ne forme pas une règle universelle de résolution des conflits.

**Proposition : appliquer le protocole suivant.**

1. Identifier les passages contradictoires et le comportement effectivement observé.
2. Chercher une décision de remplacement explicite : objet, portée, auteur ou autorisation connue, date et document successeur.
3. Si cette décision existe, appliquer sa portée et corriger les références actives qui induisent en erreur. Conserver l’ancienne décision comme histoire.
4. Si le code ne respecte pas une exigence active, traiter l’écart comme un défaut ou une demande de changement. Son existence ne l’autorise pas automatiquement.
5. Si deux intentions actives restent incompatibles, demander à la personne responsable de trancher ce point précis. Continuer les travaux indépendants lorsque c’est possible.

L’IA peut choisir une solution technique à l’intérieur de son mandat. Elle ne doit pas élargir le produit ou réécrire un critère pour rendre sa livraison conforme après coup. Une décision dite « verrouillée » signifie qu’elle n’est pas à rouvrir spontanément ; elle reste révisable par une décision explicite.

## 6. Préparer le projet et les lots

**Observé.** Le premier socle de MyLocalAgent suit les dépendances de construction : fondations, composants de bas niveau, cœur, exécution, interface, puis consolidation et acceptation. Les extensions ajoutent ensuite des capacités et parfois une refactorisation préparatoire.

**Proposition.** Pour un autre projet, conserver ce raisonnement sans copier cet ordre technique :

1. Écrire le problème, les utilisateurs, le résultat utile et les principales exclusions.
2. Fixer les règles durables : données, compatibilité, sécurité, environnement, qualité et limites d’action.
3. Identifier les dépendances et les inconnues qui pourraient rendre le plan irréalisable.
4. Définir un premier socle vérifiable, puis des lots qui produisent chacun un résultat cohérent. Privilégier un parcours de bout en bout tôt lorsqu’il permet de tester les raccordements.
5. Définir le résultat attendu et les preuves de chaque lot avant son implémentation.
6. Réserver une étape de consolidation du produit : installation, usages réels, documentation et réserves ouvertes.

Un lot est trop grand si sa réussite ne peut pas être expliquée clairement, si ses décisions indépendantes s’accumulent ou si une moitié peut être livrée et vérifiée sans l’autre. Il est trop petit s’il ne produit qu’un fragment impossible à vérifier autrement qu’en anticipant plusieurs lots.

Il est permis d’insérer un lot préparatoire. Il faut alors mettre à jour les dépendances avant de poursuivre. Le numéro d’un fichier n’est pas une preuve de l’ordre réel : MyLocalAgent comporte des lots avec suffixes, des propositions abandonnées et une étape documentée uniquement dans la spécification commune.

## 7. Avant chaque lot : reconstruire le contexte utile

**Observé.** La pratique d’origine demande souvent une lecture complète de la spécification et du ledger. Les lots plus tardifs ajoutent les lots dépendants, des parties du README, des sources et des tests précis.

**Proposition d’ordre de lecture réutilisable :**

1. Les instructions de travail applicables au dépôt.
2. L’index : fiche active, dépendances, statut et réserves des travaux précédents.
3. La fiche du lot, pour connaître les lectures qu’elle exige et son périmètre exact.
4. La spécification partagée et les décisions en vigueur, puis les entrées du ledger liées au lot.
5. Les contrats des lots dont il dépend, la documentation technique concernée et les usages utilisateurs affectés.
6. Le code, les tests, les exemples et l’état des changements locaux réellement présents.

Pour une première intervention ou un changement transversal, lire la spécification et le ledger en entier. Une lecture ciblée sur un grand projet n’est acceptable que si un sommaire à jour permet d’identifier les décisions applicables. Cette lecture ciblée est une amélioration proposée ; elle ne prétend pas décrire l’instruction historique de lecture intégrale.

Avant de coder, l’IA doit être capable de formuler brièvement le résultat attendu, les limites du mandat, les contraintes héritées et les vérifications prévues. Elle ne doit pas imposer une validation humaine de ce résumé lorsque le lot est déjà autorisé et sans ambiguïté.

Si une lecture requise est introuvable, signaler le fichier et chercher son éventuel remplaçant. Si l’état antérieur est déjà défectueux, le consigner pour ne pas attribuer ce défaut au nouveau lot. Ne pas exécuter aveuglément d’anciennes commandes incompatibles avec les règles de travail actuelles.

## 8. Pendant le lot : avancer et garder les décisions visibles

L’IA réalise les changements dans le périmètre autorisé, vérifie progressivement les comportements à risque et tient la documentation concernée à jour. Une correction locale d’un raccordement existant peut être nécessaire ; elle doit rester proportionnée et être expliquée.

**Observé.** Les prompts demandent de petits changements, la réutilisation des contrats existants, l’enregistrement des modifications d’API antérieures et la vérification réelle des dépendances. Les lots détaillés demandent aussi de rechercher toutes les surfaces affectées avant de terminer.

**Proposition.** Chaque décision importante laisse immédiatement une trace courte : problème, décision, motif, conséquence et périmètre. L’entrée peut être complétée lors de la clôture. On évite ainsi de reconstruire de mémoire les motifs à la fin d’une longue session.

Une découverte conduit à l’un des traitements suivants :

| Découverte | Traitement |
|---|---|
| Détail technique laissé libre | Le résoudre dans le lot ; consigner les choix qui affecteront les travaux suivants. |
| API ou dépendance différente de l’hypothèse initiale | Vérifier la réalité, adapter au plus près de l’intention, documenter l’écart et ses effets. |
| Besoin utile mais hors périmètre | Le noter comme proposition de lot ; ne pas l’ajouter silencieusement. |
| Changement d’objectif, de droits ou de compatibilité | Préparer un arbitrage concret et obtenir la décision manquante avant ce changement. |
| Blocage d’environnement | Décrire la cause, ce qui reste vérifiable et ce qui exige une autre intervention. |

En cas d’interruption, laisser un point de reprise : fichiers touchés, état partiel, vérifications déjà faites, échecs, prochaine action et décisions encore ouvertes. Un brouillon n’est pas un lot accepté.

## 9. Les points de validation

**Observé.** Le gate de fin de lot est central. Il dépasse progressivement la compilation et les tests : publication, contrôles visuels, parité des manuels, exemples et vérifications de compatibilité. Le lot 26 prévoit également un essai de faisabilité avant une optimisation facultative ; le lot 27 définit une comparaison de qualité sur un corpus.

**Proposition.** Donner des noms simples aux quatre passages utiles. Leur numérotation n’existe pas comme système dans le dépôt.

| Passage | Conditions | Résultat attendu |
|---|---|---|
| P0 — Prêt à commencer | Mandat clair, dépendances suffisantes, documents actifs identifiés, critères vérifiables. | Travail autorisé dans un périmètre connu. |
| P1 — Faisabilité, si nécessaire | Essai limité répondant à une inconnue critique ; critères de décision et solution de repli définis avant l’essai. | Continuer avec la solution prouvée, choisir le repli prévu ou soumettre un arbitrage. |
| P2 — Lot vérifié | Contrats satisfaits, vérifications applicables exécutées, documentation et ledger actualisés, réserves explicites. | Lot proposé à l’acceptation, avec son dossier de preuves. |
| P3 — Livraison utilisable | Installation ou déploiement vérifié selon le mandat, parcours essentiels contrôlés, réserves traitées ou acceptées par la personne responsable. | Livraison acceptée pour l’usage annoncé. |

Ces passages ne créent pas quatre réunions obligatoires. Les contrôles automatisables sont exécutés par l’agent ; l’humain intervient pour les décisions qui lui appartiennent et les vérifications nécessitant son contexte. Une acceptation humaine peut être courte ou déléguée selon une règle explicite. L’IA ne l’invente pas.

Pour chaque critère, utiliser des états distincts : **réussi**, **échoué**, **non exécuté**, **non applicable**. Une vérification partielle reste partielle. Un contrôle à faire par l’humain n’est pas une réussite technique différée par convention.

Si une réserve empêche un lot dépendant de fonctionner, elle bloque cette dépendance. Si elle ne la concerne pas, la suite peut être autorisée avec une réserve explicite. Distinguer « implémenté », « vérifié techniquement », « accepté » et « déployé » évite de donner à un seul voyant vert plusieurs sens incompatibles.

## 10. Les preuves : démontrer exactement ce qui est annoncé

Une preuve indique au minimum le contrôle, l’état testé, le résultat attendu, l’observation et la limite. Lorsque cela aide à reproduire le résultat, ajouter commande, version, environnement et emplacement du journal. Un nombre de tests seul ne prouve pas qu’un besoin métier est satisfait.

Les vérifications ont des fonctions différentes :

| Vérification | Ce qu’elle apporte | Limite à conserver |
|---|---|---|
| Compilation et analyse statique | Cohérence du code avec les règles du projet. | Ne prouve pas que le parcours utilisateur fonctionne. |
| Tests déterministes | Propriétés précises sur des entrées contrôlées, y compris refus et erreurs. | Dépendent des cas écrits et de la justesse des attentes. |
| Intégration réelle | Fonctionnement avec un composant ou service réel. | Dépend de l’environnement et de sa configuration. |
| Contrôle manuel | Utilisabilité, affichage, accès, geste réel ou contexte non simulable. | Conserver la version et le parcours effectivement observés. |
| Évaluation d’une fonction IA | Qualité sur un corpus identifié et des critères annoncés. | Ne démontre pas une qualité universelle hors de ce corpus. |
| Vérification documentaire | Exemples, liens, formats et instructions cohérents avec le contrat livré. | Une structure HTML valide ne garantit pas la justesse de toutes les explications. |

Le brief correctif de septembre est instructif : il distingue le calcul correct d’un seuil avec des vecteurs artificiels et la reconnaissance réelle des répétitions par un modèle. C’est une exigence de précision sur la preuve, pas une raison de renoncer aux tests automatisés. Voir [le brief, §8](../Prompts/Discover-AntiRepetition-Implementation-Brief.md).

Pour MyLocalAgent, les commandes actuelles imposent `-m:1` à chaque compilation et test .NET ; les tests d’intégration nécessitent une demande explicite. Ces choix appartiennent au projet. La règle transférable est de documenter les commandes applicables et de distinguer les contrôles autonomes des contrôles conditionnels.

## 11. Terminer le lot et préparer le suivant

**Observé.** Les prompts réclament un ledger complété et un rapport final ; plusieurs exigent aussi de mettre à jour la spécification, l’index, le README, les manuels, les exemples et le rapport d’acceptation.

**Proposition de clôture :**

1. Relire les changements au regard du résultat attendu et des exclusions.
2. Exécuter les vérifications finales adaptées au changement, sur l’état réellement livré.
3. Reporter les preuves et les réserves dans le rapport d’acceptation.
4. Compléter le ledger : décisions, contrats, écarts, compatibilité, versions pertinentes et suites.
5. Réconcilier les documents actifs, les exemples, les traductions et les liens affectés. Un document inchangé peut être explicitement déclaré non concerné.
6. Mettre à jour l’index avec le statut exact et les dépendances encore bloquées.
7. Présenter un compte rendu court : résultat, vérifications, limites, décisions attendues et point de reprise.

La documentation fait partie de la livraison. L’IA doit pouvoir transmettre le travail à une nouvelle session sans demander à l’utilisateur de reconstituer la conversation.

Un journal n’a pas à recopier tout le code ni toutes les sorties de commandes. Il conserve ce qui change la compréhension du projet et pointe vers les preuves utiles. Une référence au commit testé améliore cette traçabilité ; cette formalisation la propose comme champ, sans supposer qu’elle existe dans toutes les entrées historiques.

## 12. Gérer les révisions et la croissance de la mémoire

Un correctif après livraison suit le même principe, avec un cadrage plus court : symptôme constaté, reproduction, cause confirmée ou hypothèse, correction attendue, non-régression, conséquences documentaires et exploitation éventuelle. Le nom d’un modèle utilisé pour implémenter le correctif ne fait pas partie du contrat durable.

Une proposition abandonnée reste accessible pour comprendre les options envisagées, mais cesse d’être une lecture d’implémentation. Le document actif indique précisément ce qu’il remplace. L’index et les références sont mis à jour dans la même intervention.

**Proposition.** Lorsque la mémoire devient volumineuse, séparer un résumé des décisions en vigueur de l’historique daté. Le résumé comporte des liens vers les décisions sources ; il ne réécrit pas l’histoire. Les règles durables ne doivent pas devoir être déduites de milliers de lignes de comptes rendus.

Prévoir périodiquement une courte revue documentaire : liens cassés, doubles règles actives, états incohérents, critères sans preuve et réserves sans responsable. La déclencher notamment lors d’un remplacement de spécification ou d’une livraison importante. Accumuler du texte sans ce travail augmente le coût de reprise.

## 13. Répartition des responsabilités et coût de la méthode

La personne responsable porte l’objectif, les priorités, les arbitrages du produit et l’acceptation selon la délégation établie. L’IA propose les lots, analyse les impacts, implémente dans son mandat, exécute les contrôles accessibles, maintient les documents et signale les écarts. Le harnais fournit les moyens et les limites d’exécution ; il n’atteste pas à lui seul de la pertinence du résultat.

Pro-Spec demande du temps de lecture, de mise à jour et de vérification. Sa valeur se juge à la facilité de reprise, à la visibilité des écarts et à la qualité des décisions, pas au volume de documents produits. Le dépôt ne fournit pas de mesure comparative permettant de chiffrer un gain de productivité ou une baisse des défauts attribuable à la méthode.

Pour commencer, cinq éléments peuvent suffire : une spécification commune, un index, une fiche pour le lot actif, un ledger et un rapport de vérification. Ajouter les instructions de dépôt et la documentation d’usage nécessaires au projet ; regrouper les rôles dans quelques fichiers au départ. N’ouvrir un document spécialisé que lorsqu’il répond à un besoin distinct.

Le test pratique de la méthode est une reprise par une nouvelle session : peut-elle retrouver le mandat actif, les décisions à respecter, les preuves disponibles et l’action suivante sans réinventer le projet ? Les [modèles et prompts](02-Modeles-et-prompts.md) permettent d’essayer ce test. Le [parcours pédagogique](04-Progression-pedagogique.md) en fait un exercice d’apprentissage de Pro-Spec.

## 14. Fiche de référence de Pro-Spec

Cette fiche rassemble la formalisation proposée sous des rubriques stables. Elle permet de décrire la méthode sans dépendre du langage, du produit ou de l’agent utilisé.

| Rubrique | Réponse Pro-Spec |
|---|---|
| Finalité | Garder le cap et rendre le travail confié à l’IA vérifiable et reprenable. |
| Unité de travail | Un lot borné par un résultat, un périmètre, des dépendances et des critères. |
| Entrées d’un lot | Mandat actif, règles de dépôt, spécification applicable, décisions, état réel, preuves et réserves héritées. |
| Sorties d’un lot | Changements réalisés, vérifications qualifiées, documentation cohérente, entrée de ledger et état de reprise. |
| Acteurs | Personne responsable, IA chargée du travail et, selon le besoin, personne qui vérifie un usage réel. Un individu peut cumuler les rôles. |
| Support de travail | Dépôt et harnais capables de conserver les fichiers et d’exécuter les contrôles requis ; aucun outil particulier imposé par la méthode. |
| Autorité | Mandat et décisions actives identifiables ; contradictions traitées explicitement ; archives sans autorité d’exécution. |
| Mémoire | Ledger, contrats maintenus et liens vers les preuves ; résumé des décisions en vigueur lorsque le volume l’exige. |
| Déroulement | Cadrer, découper, préparer, réaliser, vérifier, documenter, accepter selon le mandat, reprendre ou livrer. |
| Validation | Critères définis avant le travail, résultats observés et limites indiquées ; faisabilité et exploitation contrôlées quand nécessaires. |
| Gestion du changement | Décision de remplacement avec portée et motif, mise à jour des références actives, conservation de l’histoire. |
| Responsabilité humaine | Objectif, priorités, arbitrages et acceptation selon la délégation explicitement définie. |
| Adaptation | Regrouper ou séparer les documents selon le besoin ; garder leurs fonctions sans imposer un volume uniforme. |
| Limites | Discipline de mise à jour nécessaire, coût de lecture, preuves de portée bornée, absence de garantie automatique de cohérence ou de qualité globale. |
