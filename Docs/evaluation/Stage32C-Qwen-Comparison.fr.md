# Stage 32C — Comparaison Qwen3.5 4B et 9B

Les essais supplémentaires demandés sont terminés. **Tev1 et Qwen3.5 4B sont à égalité sur les
rendez-vous réservés au test ; Tev1 retrouve davantage d’annotations sur le pilote VitaminC.**
Qwen3.5 9B obtient moins sur ces deux partitions dans ce protocole sans réflexion. Cela ne
constitue ni un classement général des modèles, ni une évaluation de leur qualité comme modèle
principal de conversation. Le [rapport anglais](Stage32C-Qwen-Comparison.en.md) détaille aussi
les conditions et la provenance.

## Conditions conservées

Le 7 octobre 2026, le propriétaire a fourni les deux tailles Qwen installées pour ces essais.
Chaque modèle a évalué les mêmes 60 entrées de rendez-vous, dont 20 réservées au test, et les
mêmes 40 entrées publiques VitaminC réparties dans 11 familles complètes, trois fois chacune.
Cela ajoute **600 appels de modèle valides**. Aucune indisponibilité technique, aucun choix
invalide, aucune variation de choix ou de statut entre les trois répétitions. Les répétitions
n’augmentent pas le nombre de cas indépendants. Les [mesures initiales Tev1/Gemma4](Stage32C-Results.fr.md)
sont réutilisées avec leurs propres manifestes, sans réexécution ni remplacement.

Sources, annotations, questions, options et instruction générale restent inchangées. Les
comparateurs généraux font un seul appel JSON contraint : température zéro, réflexion
désactivée, contexte 8192, sortie limitée à 512 tokens et résidence de dix secondes. Aucune
recherche externe, aucun outil, appel de raisonnement supplémentaire ou score de confiance
inventé. Tev1 conserve son interface native, son contexte et sa politique d’acceptation.
Toutes les sources restent dans son budget initial. Un choix valide émis par Qwen ou Gemma4
n’équivaut pas à une décision acceptée au moyen d’un seuil de confiance.

| Modèle installé | Taille déclarée | Quantification | Moteur | Empreinte |
| --- | ---: | --- | --- | --- |
| `qwen3.5:4B` | 4,2B | Q4_K_M | llamacpp | `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04` |
| `qwen3.5:9b`, passages valides | 9,7B | Q4_K_M | llamacpp | `c97eb11d70b1acdc88af01eef566c1fe4f7fbe93eb1afc06871132f293ff425a` |

Tev1 est un 4,2B en Q8_0 : la taille est comparable au Qwen4B, mais la quantification et
l’interface diffèrent. Les deux Qwen déclarent aussi une pénalité de présence de 1,5, top-k 20
et top-p 0,95 ; seuls les paramètres explicites ci-dessus sont remplacés. Les réglages hérités,
les gabarits et les quantifications ne sont donc pas tous identiques entre modèles. L’expérience
ne permet pas d’attribuer chaque différence uniquement à la spécialisation. Le matériel et le
runtime sont ceux du premier pilote. Aucun réglage applicatif ni modèle principal n’a été
enregistré ou changé ; aucun téléchargement, mail réel, test d’intégration ou déploiement
n’a été effectué par l’exécuteur.

## Accord avec les annotations

Les nombres ci-dessous portent sur les entrées distinctes, puisque les répétitions concordent.
Les synthèses JSON conservent toutes les observations, matrices de confusion et partitions.
L’accord natif inclut ses candidats incertains.

| Comparateur | Rendez-vous : 60 entrées | Test réservé : 20 entrées / 10 paires | VitaminC : 40 entrées / 11 familles |
| --- | ---: | ---: | ---: |
| Règles de rendez-vous figées | 93,33 % (56/60) | 90 % (18/20) | Sans objet |
| Tev1 4B | 65 % (39/60) | **60 % (12/20)** | **85 % (34/40)** |
| Qwen3.5 4B | 61,67 % (37/60) | **60 % (12/20)** | **77,5 % (31/40)** |
| Qwen3.5 9B | 68,33 % (41/60) | **50 % (10/20)** | **67,5 % (27/40)** |
| Gemma4 12B | 78,33 % (47/60) | **60 % (12/20)** | **80 % (32/40)** |

L’égalité entre Tev1 et Qwen4B masque des erreurs différentes : sur les vingt entrées réservées,
les deux réussissent neuf cas ; Tev1 seul en réussit trois, Qwen4B seul trois, et les deux se
trompent sur cinq. Leurs choix diffèrent sur huit entrées. Sur VitaminC, Tev1 retrouve trois
annotations de plus que Qwen4B et sept de plus que Qwen9B. Onze familles et un sous-ensemble
adapté ne suffisent pas à établir un avantage général ou sur la suite publique complète.

Sur le test réservé, Qwen4B obtient 60 % dans chaque langue ; Qwen9B obtient 40 % en français
et 60 % en anglais. Sur les trente paires réunies, les choix FR/EN diffèrent sur huit paires
pour Qwen4B et trois pour Qwen9B. Cet échantillon ne mesure pas la qualité linguistique générale.
Le résultat sur développement et test réunis ne remplace pas celui de la partition réservée.

Le résultat d’acceptation de Tev1 reste inchangé : couverture de 50 %, accord de 80 % parmi
les acceptés et deux entrées distinctes mal routées avec acceptation. Les critères figés ne sont
pas atteints. Qwen4B émet huit choix incorrects sur vingt et Qwen9B dix, sans mécanisme de
confiance pour les retenir. Aucun de ces résultats ne justifie l’automatisation sans validation
de ce routage. Le bon score des règles reste limité à la politique synthétique étroite.

## Latences et ressources

Chaque passage Qwen valide contient un appel observé à froid puis des appels avec modèle
résident. Une seule mesure à froid ne constitue pas une estimation fiable du p95 à froid.
L’indicateur vérifie la présence de l’empreinte ; un modèle résident peut être rechargé pour
changer de contexte. Les durées générales comprennent l’opération du comparateur et son attente
de verrou, mais excluent les vérifications supplémentaires du catalogue et de l’identité.

| Tâche / modèle | Appels résidents | Médiane résidente | p95 résident | Appel froid unique |
| --- | ---: | ---: | ---: | ---: |
| Rendez-vous / Qwen4B | 179 | 0,127 s | 0,159 s | 3,573 s |
| VitaminC / Qwen4B | 119 | 0,131 s | 0,150 s | 3,088 s |
| Rendez-vous / Qwen9B | 179 | 0,218 s | 0,244 s | 4,320 s |
| VitaminC / Qwen9B | 119 | 0,226 s | 0,254 s | 4,343 s |

Les instantanés Ollama indiquent environ 3,11 Gio de VRAM pour Qwen4B et 5,34 Gio pour Qwen9B,
avec contexte 8192. Il s’agit de mesures de résidence, pas de pics mémoire. Le premier rapport
distingue le coût du nettoyage natif et ses conditions de chargement différentes : on ne peut
pas attribuer l’écart de durée au seul entraînement spécialisé. Aucun chat ni embedding
concurrent n’a été mesuré. La durée de résidence bornée affecte les caches, sans modifier les
réglages persistants.

## Passage exclu et provenance

Pendant le premier passage Qwen9B sur les rendez-vous, l’alias a changé d’empreinte : celle
du modèle finalement résident différait de celle relevée avant l’exécution. Ollama a ensuite
exposé deux entrées homonymes, pour les moteurs `ggml` et `llamacpp`. Les 180 observations de
ce passage sont **exclues**, avec leurs indicateurs froid trompeurs. Le contrôle préalable du
premier passage public a alors échoué sur les noms dupliqués, avant tout appel d’inférence.
Le [manifeste original](results/readiness-qwen9b-v1.invalidated-manifest.json) et l’[audit d’exclusion](results/readiness-qwen9b-v1.audit.json)
sont conservés ; ces résultats n’ont pas été incorporés silencieusement.

L’exécuteur associe désormais le moteur déclaré par `/api/show` aux entrées correspondantes
de `/api/tags`, exige une empreinte unique et la vérifie avant et après chaque appel général.
Une ambiguïté ou un changement arrête l’essai. Les deux passages Qwen9B `v2` valides utilisent
la même empreinte `llamacpp`, stable. Pour Qwen4B, l’identité initiale et celle du modèle résident
final concordent ; ses essais ont précédé ce contrôle renforcé à chaque appel. Cette limite
est conservée, sans prétendre rétrospectivement qu’il avait été effectué.

Pièces conservées :

- Qwen4B, rendez-vous : [manifeste](results/readiness-qwen4b-v1.manifest.json), [synthèse](results/readiness-qwen4b-v1.summary.json).
- Qwen4B, public : [manifeste](results/vitaminc-qwen4b-v1.manifest.json), [synthèse](results/vitaminc-qwen4b-v1.summary.json).
- Qwen9B, rendez-vous : [manifeste](results/readiness-qwen9b-v2.manifest.json), [synthèse](results/readiness-qwen9b-v2.summary.json).
- Qwen9B, public : [manifeste](results/vitaminc-qwen9b-v2.manifest.json), [synthèse](results/vitaminc-qwen9b-v2.summary.json).

Les validations originales portent toujours sur les mêmes annotations et critères natifs.
Les fichiers d’évaluation des passages LLM seuls indiquent que ces critères ne s’appliquent
pas ; ils ne déclarent pas une réussite native. Données publiques et résultats individuels
restent exclus de Git, avec les notes de licence et d’attribution VitaminC. Empreintes des
binaires, sources, corpus et configurations, paramètres déclarés et résidence figurent dans
les nouveaux manifestes. Le [guide de l’exécuteur](../../tools/DecisionEvaluation/README.md)
décrit la sélection du modèle et des comparateurs, limitée au processus.

Compilation finale sans avertissement ni erreur. Suite par défaut avec `-m:1` explicite :
**1078 réussites, aucun échec, un contrôle attendu ignoré**, dont les tests d’identité unique
ou ambiguë des moteurs et de sélection isolée du modèle. Aucune assertion de qualité de modèle
n’est ajoutée aux tests unitaires.

La spécialisation reste une hypothèse utile pour le laboratoire : Tev1 peut ici dépasser un
comparateur général de sa famille sur une classification à partir de preuves fournies. Il ne
gagne pas en accord des choix sur les rendez-vous réservés, et ses probabilités ne satisfont
toujours pas notre politique d’acceptation. Le choix d’un modèle principal, les performances
avec réflexion et une comparaison à quantification égale demandent des expériences distinctes,
avec de nouveaux cas réservés si les instructions sont ajustées.
