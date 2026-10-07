# Stage 32C — Pilote contrôlé d’évaluation des décisions

La [comparaison ultérieure Qwen 4B/9B](Stage32C-Qwen-Comparison.fr.md) ajoute deux comparateurs
généraux installés sans modifier les données ni les identités de ce premier pilote.

L’exécuteur et le premier pilote sont terminés. Tev1 **ne satisfait pas les critères validés
pour le routage des rendez-vous**. Les données publiques annotées apportent un complément utile :
sur notre adaptation de 40 entrées VitaminC, Tev1 retrouve 85 % des annotations, contre 80 %
pour le LLM général configuré. Ce petit écart ne suffit pas à établir un avantage général ni
à justifier un déploiement. Le [rapport anglais détaillé](Stage32C-Results.en.md) décrit les
conditions et la provenance ; les principaux résultats sont repris ci-dessous.

## Protocole et exécution

Le 7 octobre 2026, le propriétaire a [validé les annotations et les critères](decision-readiness-approval.v1.json)
avant les essais : aucune mauvaise route acceptée, couverture acceptée d’au moins 80 %, p95 total
natif d’au plus 180 secondes à froid et 30 secondes à chaud. Cette validation des annotations
proposées ne constitue pas une relecture bilingue distincte et aveugle. Aucune annotation,
question, règle ou valeur de seuil en production n’a été ajustée après observation des résultats.

Le corpus applicatif comprend 30 paires FR/EN : 20 de développement et 10 réservées au test.
Chacune des 60 entrées a été évaluée trois fois par les règles figées, le service natif commun
et un appel structuré au LLM local. Le pilote public comprend 40 entrées anglaises dans 11
familles VitaminC complètes, évaluées trois fois par chaque modèle. Cela représente **780
observations, dont 600 appels de modèle**. Les répétitions sont corrélées ; elles n’augmentent
pas la taille de l’échantillon indépendant. Pour chaque entrée et chaque comparateur, les choix
et statuts sont restés identiques sur les trois passages. Aucune indisponibilité technique.

Les essais utilisent Ollama 0.40.0, .NET 10.0.12, un i7-13700KF, environ 31,7 Gio de RAM et
une RTX 4080 avec 16 376 Mio de VRAM. Les modèles installés sont `tev1:latest` (4,2B, Q8_0)
et `gemma4:12b` (11,9B, Q4_K_M). Leurs empreintes complètes figurent dans les manifestes.

L’[exécuteur isolé](../../tools/DecisionEvaluation/README.md) utilise le véritable service natif,
ses bornes, son verrou et son nettoyage. Le comparateur LLM reçoit les mêmes source, question
et options, sans annotations de référence : température zéro, réflexion désactivée, contexte
8192, sortie limitée à 512 tokens, résidence de dix secondes. Aucun réglage applicatif ni tâche
n’a été sauvegardé, aucun modèle téléchargé, aucun mail réel lu et aucun test d’intégration lancé.

## Rendez-vous : critères non atteints

La partition réservée contient **20 entrées distinctes, soit 10 paires bilingues**, et 60
observations par comparateur. L’accord des choix inclut les candidats natifs incertains.
La couverture acceptée divise par toutes les tentatives ; l’accord parmi les décisions acceptées
divise uniquement par celles-ci. Les règles et le LLM ne fournissent aucune distribution native :
leur couverture de 100 % signifie un choix valide émis, sans seuil de confiance calibré.

| Comparateur | Accord des choix | Couverture acceptée | Accord parmi les acceptés | Mauvaises acceptations | Incertains |
| --- | ---: | ---: | ---: | ---: | ---: |
| Règles figées | 90 % (54/60) | 100 % | 90 % | 6/60 | 0 |
| Tev1, maximum ≥ 0,8 et marge ≥ 0,2 | 60 % (36/60) | 50 % (30/60) | 80 % (24/30) | 6/60 | 30 |
| Gemma4 | 60 % (36/60) | 100 % | 60 % | 24/60 | 0 |

Les six mauvaises acceptations natives correspondent à deux entrées anglaises répétées trois
fois : un contact retiré (`p26-en`) et une demande de réparation non prise en charge avec des
faits manquants (`p29-en`). En français, la couverture acceptée est de 40 %, sans mauvaise
acceptation observée ; en anglais, elle est de 60 %, avec six. L’échantillon ne permet pas une
conclusion générale sur les langues. Sur l’ensemble des 30 paires, Tev1 donne des choix différents
entre langues sur six paires et des statuts différents sur six ; le LLM sur trois paires pour
les choix ; les règles sur aucune.

Sur développement et test réunis, l’accord des choix est de 93,33 % pour les règles, 65 % pour
Tev1 et 78,33 % pour Gemma4. Les règles présentent des erreurs lexicales connues sur du contenu
hors sujet ou adversarial ; leur score élevé sur cette politique synthétique étroite ne prouve
pas une compréhension générale. Ces erreurs ont été conservées après les essais.

L’[évaluation des critères figés](results/readiness-v1.criteria-assessment.json) échoue sur la
couverture et les mauvaises routes acceptées ; les budgets de latence natifs sont respectés.
L’analyse descriptive après exécution donne 30 % de couverture sans mauvaise acceptation
observée au seuil maximum de 0,9, puis 15 % à 0,95. Ce ne sont **pas des seuils de remplacement
sélectionnés ou validés**, et aucun n’atteint 80 % de couverture. Les seuils en production restent
inchangés. Tev1 n’est pas recommandé pour automatiser le routage de cette politique de rendez-vous.

## Données publiques : VitaminC

[Ollama renvoie à une suite de treize sous-ensembles publics](https://ollama.com/blog/ollama-now-supports-jev-style-decision-models).
L’importeur suit le [guide Bespoke figé à la révision `dcfdbd9`](https://github.com/bespokelabsai/nimble/blob/dcfdbd9a64f0d869f658d7a72f1beaee32737773/docs/PUBLIC_BENCHMARKS.md).
VitaminC demande de vérifier une affirmation à partir d’éléments fournis, avec trois choix.
Le sous-ensemble amont contient 599 entrées dans 171 familles. L’importeur vérifie le SHA-256
de la source, puis sélectionne des familles entières dans l’ordre du manifeste, sans consulter
les étiquettes et dans la limite de quarante entrées.
Le [manifeste amont](results/vitaminc-v1.upstream-manifest.json) conserve l’empreinte de source,
les identifiants et les métadonnées d’annotation séparément de l’empreinte du corpus adapté.

| Comparateur | Accord des choix | Couverture acceptée | Accord parmi les acceptés | Mauvaises acceptations | Incertains |
| --- | ---: | ---: | ---: | ---: | ---: |
| Tev1 | 85 % (102/120 ; 34/40 distinctes) | 70 % (84/120) | 85,71 % (72/84) | 12/120 ; quatre entrées distinctes | 36 |
| Gemma4 | 80 % (96/120 ; 32/40 distinctes) | 100 % | 80 % | 24/120 ; huit entrées distinctes | 0 |

Tev1 retrouve deux annotations supplémentaires sur quarante entrées. Onze familles liées
constituent peu d’éléments indépendants. Il n’y a ici ni partition publique réservée ni critère
de réussite fixé pour cette tâche ; les résultats sont descriptifs. Les critères de rendez-vous
et leur comparateur à règles ne s’appliquent pas à VitaminC.

Les formulations de question et de descriptions sont conservées. Pour le contrat applicatif,
l’objet d’état devient une source JSON littérale et `NOT ENOUGH INFO` devient `NOT_ENOUGH_INFO`.
Le budget prudent de l’application est conservé sans troncature. Ces adaptations, la sélection
réduite et le mode Choice uniquement empêchent de présenter ces essais comme une reproduction
exacte des scores publiés ou de les comparer à l’agrégat complet. Les autres domaines, les modes
Boolean/rubrique et la suite complète restent à évaluer.

[VitaminC](https://huggingface.co/datasets/tals/vitaminc) est sous licence CC BY-SA 3.0.
Les données importées portent attribution et notes d’adaptation. Les entrées publiques brutes
et les résultats individuels restent dans `artifacts/decision-evaluation/`, exclu de Git.
Seuls agrégats et provenance sont conservés dans le dépôt. L’accord avec une annotation humaine
ne garantit pas que celle-ci soit vraie.

## Latences et résidence

Les durées natives comprennent le nettoyage ; les percentiles utilisent le rang le plus proche.
Les conditions froid/chaud sont observées, sans chargement forcé.

| Tâche / modèle / condition | Appels | Médiane totale | p95 total |
| --- | ---: | ---: | ---: |
| Rendez-vous / Tev1 / froid | 19 | 2,587 s | 4,442 s |
| Rendez-vous / Tev1 / chaud | 161 | 0,206 s | 0,253 s |
| Rendez-vous / Gemma4 / résident avant appel | 180 | 0,227 s | 0,238 s |
| VitaminC / Tev1 / froid | 120 | 3,088 s | 3,390 s |
| VitaminC / Gemma4 / froid | 1 | 6,557 s | 6,557 s |
| VitaminC / Gemma4 / résident avant appel | 119 | 0,225 s | 0,336 s |

Gemma4 était résident avant les essais de rendez-vous, mais le premier appel a pris 6,664
secondes en remplaçant son contexte précédent de 16384 par 8192 tokens. L’indicateur du
comparateur vérifie seulement la présence de l’empreinte avant chaque appel ; un modèle présent
peut être rechargé pour changer de contexte. Ce premier essai ne contient donc aucun échantillon
LLM réellement froid. Les conditions de résidence différentes ne permettent pas d’isoler la
vitesse d’inférence des deux modèles.

Les instantanés Ollama montrent environ 4,35 Gio de VRAM pour Tev1 et 7,8 Gio pour Gemma4,
parfois présents ensemble. Ce ne sont pas des mesures de pic mémoire. Le nettoyage natif est
demandé à chaque opération, mais le déchargement est asynchrone : beaucoup d’appels du corpus
applicatif trouvent encore Tev1 résident, alors que tous les appels publics le trouvent froid.
Le comparateur a modifié le contexte et la durée de résidence en cache de Gemma4 sans changer
ses réglages persistants. Aucun chat ni embedding concurrent n’a été mesuré ; une absence
d’interférence n’est pas démontrée.

## Provenance et vérification

Les manifestes originaux [rendez-vous](results/readiness-v1.manifest.json) et
[VitaminC](results/vitaminc-v1.manifest.json) conservent les empreintes des sources à l’exécution,
du corpus, de la configuration expurgée et des modèles, les instructions, horaires et instantanés
de résidence. La révision Git est la base 32B ; l’exécuteur n’était pas encore commité lors des
essais. Une analyse hors ligne ultérieure a corrigé la partition du balayage des seuils publics,
sans relancer les modèles. Les synthèses [rendez-vous](results/readiness-v1.summary.json) et
[VitaminC](results/vitaminc-v1.summary.json) indiquent séparément l’heure et l’empreinte du binaire
d’analyse. La provenance originale n’a pas été remplacée rétroactivement. Les futurs essais
enregistrent aussi les empreintes des binaires d’exécution et l’interprétation de l’indicateur froid.

Compilation : zéro avertissement et zéro erreur. Suite par défaut avec `-m:1` explicite :
**1073 tests réussis, aucun échec, un contrôle attendu ignoré**. Les nouveaux tests déterministes
couvrent métriques, égalités de scores, isolation des annotations, appel unique, sortie invalide,
annulation, critères, importeur/empreintes/familles et compatibilité du transport existant.
La qualité des modèles reste un résultat expérimental, sans assertion dans les tests unitaires.

Le premier pilote C est terminé avec un résultat défavorable pour les rendez-vous et des
observations publiques limitées. Le routage intégré D et l’exposition agentique E restent
différés. Une nouvelle expérience devra figer une nouvelle politique et son corpus, ou élargir
les familles publiques annotées ; les cas réservés ci-dessus ne doivent pas devenir un ensemble
d’ajustement non déclaré.
