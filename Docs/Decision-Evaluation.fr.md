# Décisions locales génériques — Stage 32, phase A

`decision.evaluate` évalue le texte explicitement fourni avec le modèle de décision local installé.
L’outil est réservé aux workflows et ne réalise ni collecte Gmail, ni recherche Knowledge, ni accès
aux fichiers. Il ne télécharge aucun modèle et ne remplace pas une décision indisponible par un appel
au LLM général. Activez **Settings → Decision models → Enable generic workflow evaluation**, puis
accordez séparément le groupe **Decision evaluation** à la tâche. L’activation du lecteur Gmail
n’active pas cet outil. Les agents, Direct Chat, `ai.prompt.toolAliases` et `forEach` ne peuvent pas
l’appeler.

La phase A utilise le formulaire générique existant et l’éditeur JSON. L’éditeur spécialisé avec
lignes d’options, test sur texte d’exemple et affichage des scores relève de la phase B.

Arguments obligatoires : `source` non vide, `question` littérale de 1000 caractères maximum et
`options` associant 2 à 24 identifiants stables à des descriptions non vides de 400 caractères maximum.
Les identifiants suivent `[A-Za-z][A-Za-z0-9_]{0,63}` ; les doublons, collisions de casse et arguments
inconnus sont refusés. Seule la source accepte des placeholders de sorties précédentes. Un contenu
externe ne peut pas définir les questions, options ou seuils. Les protections des outils en amont
restent applicables.

`acceptance` permet éventuellement de préciser `minTopProbability` et `minMargin` entre 0 et 1,
uniquement pour renforcer les valeurs héritées. Les valeurs par défaut sont 0,80 et 0,20.

| Statut | Signification | Traitement |
| --- | --- | --- |
| `accepted` | Meilleur candidat unique respectant tous les seuils | Router sur `choice` après vérification du statut |
| `uncertain` | Distribution valide, égalité ou seuil non atteint | Traitement explicite ; `choice` reste un candidat |
| `unavailable` | Modèle absent, réponse invalide, délai dépassé ou entrée trop grande | Examiner les codes techniques ; choix et scores sont nuls |

La sortie porte `schemaVersion: 1`. Les probabilités, seuils, marge et concentration sont normalisés
entre 0 et 1. La marge est la différence entre les deux meilleurs scores. `confidence`, si présent,
mesure la concentration de la distribution ; ce n’est pas une garantie de justesse. Une égalité reste
incertaine même avec des seuils nuls. L’annulation utilisateur est propagée. Un contrat mal formé,
une fonctionnalité désactivée ou une permission manquante produit une erreur, et non un statut
`unavailable`. Les temps distinguent l’attente du verrou, l’inférence et le total incluant le nettoyage.

L’[exemple désactivé](samples/decision-readiness.json) vérifie d’abord `status`, puis `choice` seulement
sur la branche acceptée, avec le routage `flow.decision` existant. Chaque cas dispose d’une branche
explicite. Les étapes finales `ai.summarize` utilisent le LLM général pour présenter le traitement
de la branche ; elles illustrent une action en aval et ne constituent pas un repli de classification.
Remplacez-les par les actions autorisées souhaitées. L’import n’installe rien et n’active aucune tâche.

## Réglages et migration

La section `Decision` possède les paramètres natifs partagés. Si elle existe, elle est prioritaire,
y compris pour les valeurs par défaut des champs omis. Sinon, les valeurs historiques de
`GmailDecision` sont reprises, en gardant l’outil générique désactivé. La sauvegarde écrit la section
canonique et retire les paramètres natifs partagés de la section Gmail enregistrée. Gmail conserve
son activation, ses limites de collecte, son délai de lot, ses réglages de rapport, son arbitrage et
son repli allégé. `LlmOnly` ne nécessite toujours pas Tev1.

Le fichier de configuration livré conserve volontairement la section historique jusqu’à la première
sauvegarde : des valeurs canoniques dans ce fichier de base masqueraient les réglages historiques
des utilisateurs. Un [exemple canonique complet](decision-settings.example.json) est fourni séparément.
Le [guide technique anglais](Decision-Evaluation.en.md) détaille chaque borne.

Les nouvelles limites génériques sont indépendantes du nombre de mails : délai d’opération de
180 secondes par défaut, réglable entre 1 et 3600 ; budget complet de 2050 tokens, réglable entre
512 et 2050. L’estimation prudente compte un token par octet UTF-8 de la source et du contrat sérialisé,
plus une réserve fixe de 400 tokens. Elle peut refuser un texte qui tiendrait avec un tokenizer exact.
Une entrée trop grande renvoie `InputTooLarge` avant tout accès réseau, sans troncature ni résumé.

Les deux appelants et les embeddings utilisent le même verrou d’exécution et le même coordinateur
de résidence. Le nettoyage dispose d’un délai indépendant de cinq secondes et ne décharge pas un
modèle encore possédé par une autre opération. Le keep-alive reste limité à 60 secondes maximum.

## Vérification et limites

Les tests déterministes utilisent des réponses simulées, les vrais moniteurs de configuration, le
registre d’outils et le moteur de workflow. Commandes :

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore
dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"
```

Le contrat a été revérifié le 7 octobre 2026 dans la [documentation Ollama](https://ollama.com/library/tev1).
Le CLI installé indique la version 0.40.0, mais aucun serveur en cours d’exécution n’était accessible
depuis cet environnement. La borne de 2050 tokens reprend la [mesure locale existante](Tev1-Local-Feasibility.md).
Aucun essai de modèle, aucune lecture de mail réel et aucun test d’intégration n’ont été lancés.
La phase C devra mesurer l’utilité, la qualité français/anglais et les latences à froid et à chaud
avec des cas annotés et réservés à l’évaluation. Les tests d’implémentation ne prouvent pas la qualité du modèle.
