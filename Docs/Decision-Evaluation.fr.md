# Évaluation des décisions — Stage 32 C2 et D

## Routage intégré « Route by decision » (phase D)

Dans l’éditeur **Evaluate a decision**, cochez **Route by decision**, renseignez la destination
de chaque option et les destinations explicites **Uncertain** et **Unavailable**. Laissez **Next**
vide. Chaque destination est un identifiant d’étape existante ou `@end`. Apply valide tout le graphe
et modifie le brouillon ; **Test decision** évalue seulement l’échantillon sans exécuter de destination.

Le bloc `decisionRoutes` se place au même niveau que `args` :

```json
"decisionRoutes": {
  "choices": { "process": "handleProcess", "clarify": "handleClarify", "review": "handleReview" },
  "uncertain": "handleUncertain",
  "unavailable": "handleUnavailable"
}
```

Chaque option doit avoir exactement une destination ; plusieurs options peuvent partager la même.
Seuls les résultats `accepted` suivent la destination de leur choix. Un candidat `uncertain`
ne déclenche jamais cette branche, et `unavailable` suit sa destination dédiée. L’option sémantique
`review` garde sa propre branche. Le moteur global et son instantané restent communs ; en central,
la base demeure `validatedChoice` et les scores sont nuls. La sortie complète version 2 reste
accessible avec `{{stepId.output}}`, notamment sa provenance et ses temps d’exécution.

L’[exemple complet désactivé](samples/decision-route-readiness.json) fonctionne avec les deux moteurs.
Les workflows `flow.decision` existants restent valides. Les seuils natifs explicites conservent leurs
contrôles et restent incompatibles avec le mode central. Le bloc de routage n’ajoute aucun seuil.
Les permissions, l’absence de cycles, l’accessibilité des étapes, les sorties garanties et l’annulation
restent contrôlées. `decisionRoutes` ne se combine pas avec `next` ou `forEach`. Les exceptions
d’exécution gardent la politique Stop/Retry/ContinueOnError ; ContinueOnError passe à l’étape suivante
dans l’ordre de déclaration après un échec, et cette arête est validée dans le graphe. Une évaluation
`unavailable` est une sortie normale de l’outil, avec son routage explicite.

**Settings → Decision models** propose un choix global : **Use application LLM** (`CentralLlm`) ou
**Use a decision model** (`NativeDecision`). Les décisions génériques, les tests explicites de l’éditeur
et Gmail utilisent ce même moteur. Le workflow capture configuration, fournisseur, modèle et bornes
avant sa première étape ; une modification prend effet au prochain run. Un test capture ses propres
réglages. Aucun remplacement par tâche, étape ou conversation, second avis ou repli implicite.

## Rédaction et inspection

Activez séparément l’évaluation générique et le groupe `decision` de la tâche. Ils sont désactivés par
défaut. `decision.evaluate` reste réservé aux workflows, en lecture seule. Il exige `source` non vide,
`question` littérale (1000 caractères maximum) et `options` (2–24 identifiants stables, descriptions de
400 caractères maximum). Seule la source accepte les placeholders de sorties. Les champs inconnus,
doublons et politiques issues d’une sortie sont rejetés avant inférence.

```json
{"step":"decision.evaluate","id":"readiness","args":{
  "source":"{{request.output}}",
  "question":"Is essential information present?",
  "options":{"process":"Essential facts are present.","clarify":"Essential facts are missing.","review":"Human review is needed."}
}}
```

L’éditeur **Evaluate a decision** affiche moteur et modèle globaux en lecture seule. **Decision settings**
ouvre les réglages en conservant le brouillon. Les seuils avancés et les lignes de scores s’appliquent au
natif. Une surcharge explicite existante reste visible et bloque le moteur central ; **Remove explicit
native acceptance** permet de la retirer volontairement. **Test decision** exige exemple explicite,
activation et permission. Il est annulable, ne résout aucun placeholder, n’exécute aucune autre étape et
ne sauvegarde ni exemple ni résultat. Les réponses tardives après annulation sont ignorées. Édition
et sauvegarde ne déclenchent jamais d’inférence.

## Résultat version 2 et routage

Champs : `schemaVersion: 2`, `engine`, `capabilities.scores`, `acceptanceBasis`, `status`, `choice`,
`probabilities`, `topProbability`, `margin`, `confidence`, `acceptance`, `reasons`, `provider`, `model`
et `timing` (`gateWaitMs`, `inferenceMs`, `totalMs`, `cold`). Les scores natifs sont normalisés [0,1].
La concentration ne mesure pas la probabilité que la réponse soit correcte.

| Moteur | Base d’acceptation | Scores | Statuts |
| --- | --- | --- | --- |
| NativeDecision | nativeThresholds | Distributions natives validées | accepted : meilleur choix unique satisfaisant les seuils ; égalité ou seuil insuffisant : uncertain |
| CentralLlm | validatedChoice | probabilities/topProbability/margin/confidence/acceptance tous null | accepted : choix autorisé strictement validé ; échec technique, format invalide ou limite dépassée : unavailable |

Un choix sémantique `review` est un choix ordinaire du LLM central. Il ne devient pas une incertitude
native. Aucune base ne garantit la justesse ni n’autorise des effets externes. Un résultat unavailable
conserve moteur/base/provenance et temps disponibles, avec choix et scores nuls. L’annulation se propage ;
contrat invalide, outil désactivé et permission manquante restent des erreurs.

`acceptance` facultatif contient `minTopProbability` et `minMargin` dans [0,1], uniquement pour renforcer
les seuils natifs globaux (.80 et .20 par défaut). Le contrôle de concentration reste natif. Tout objet
acceptance explicite, même `{}`, est incompatible avec CentralLlm et doit être modifié volontairement.

Utilisez `flow.decision` pour vérifier `status` avant `choice`. L’[exemple de choix](samples/decision-readiness.json)
fonctionne avec les deux moteurs. L’[exemple numérique protégé](samples/decision-native-score.json) vérifie
statut, `capabilities.scores` et absence de null avant de comparer `topProbability`. Sa branche sans
scores exige une revue explicite et ne contourne pas le seuil numérique. Un passage au moteur central
rejette avant inférence les routes natives numériques non protégées. Les consommateurs doivent gérer
la version 2 et les scores nuls.

## Réglages, migration et limites

Un nouveau profil choisit explicitement CentralLlm. Un fichier existant sans Engine conserve NativeDecision.
Une section `Decision` explicite est prioritaire ; sinon les valeurs natives historiques Gmail sont
reprises. Le modèle de configuration embarqué ne masque pas ces anciennes valeurs. La sauvegarde
conserve paramètres natifs inactifs, historique arbitrage/repli Gmail, autres réglages et activations.
Aucun téléchargement, changement de permission, inférence ou réécriture des tâches planifiées.

Le mode central utilise le fournisseur et modèle centraux ainsi que le contexte global déclaré.
`CentralTimeoutSeconds` vaut 180 (1–3600), `CentralGateWaitSeconds` 30 (1–300),
`CentralMaxOutputTokens` 512 (64–8192). La requête isolée et structurée n’a ni outils, historique,
prompt de tâche, RAG ou secret dans le contenu. Le raisonnement est désactivé sans nouvelle tentative.
Le budget compte de façon conservatrice les octets UTF-8 des instructions, source/questions sérialisées
et schéma, plus réserve de sortie et enveloppe. Un dépassement générique devient `InputTooLarge`, sans
troncature ni résumé. Le modèle central local suit sa résidence habituelle et n’est pas déchargé.

Le natif conserve OllamaSystemOne, tev1:latest, l’adresse loopback héritée et la limite conservatrice de
2050 tokens (`MaxInputTokens`, 512–2050). `OperationTimeoutSeconds` vaut 180 (1–3600), timeouts froid/chaud
120/15 (1–300 / 1–120), attente de verrou 30 (1–300), keep-alive 10 (0–60) et nettoyage requis.
Le budget natif ajoute une réserve de 400 tokens ; le nettoyage a une borne indépendante de cinq secondes
et respecte les autres propriétaires. Le chargement à froid n’est indiqué que lorsqu’il est observable.
Les seuils top/marge/concentration sont 80/20/0 sur 100 ; la certitude booléenne vaut 80 (50–100).
Voir l’[exemple complet de réglages](decision-settings.example.json).

Gmail exige `contractVersion: 2`, son activation et le groupe `gmailDecision`. Retirez volontairement
arbitration/unavailableBehavior. Hybrid n’est compatible qu’avec NativeDecision, LlmOnly qu’avec CentralLlm ;
ces champs ne sélectionnent jamais le moteur. Les nouvelles étapes omettent classificationMode.
Acceptance natif, alwaysArbitrateChoices et seuils de réponse probabilistes doivent être revus.
Le mail brut reste local, y compris pour les alias Ollama adossés au cloud ; une configuration cloud
échoue sans fournisseur local de substitution. Booléens et niveaux de rubrique centraux sont des valeurs
sémantiques, jamais des probabilités. Le rendu déterministe accepte les anciens rapports version 1 et
les nouveaux version 2. Voir le [guide Gmail](Gmail-Decision-Reader.fr.md).

## Vérification et limites

Les tests déterministes utilisent des réponses simulées, les vrais moniteurs de configuration, le
registre d’outils et le moteur de workflow. Commandes :

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore
dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"
```

Le contrat a été revérifié le 7 octobre 2026 dans la [documentation Ollama](https://ollama.com/library/tev1).
Pendant la phase A, le CLI indiquait la version 0.40.0 sans pouvoir joindre le serveur. La phase C
a ensuite atteint ce serveur et exécuté les essais locaux autorisés. La borne de 2050 tokens reprend
la [mesure locale existante](Tev1-Local-Feasibility.md). Les [résultats C](evaluation/Stage32C-Results.fr.md)
montrent que Tev1 ne satisfait pas les critères figés pour les rendez-vous. Le pilote public
VitaminC adapté reste descriptif. Aucune lecture de mail réel ni aucun test d’intégration n’ont
été lancés. Les tests d’implémentation ne prouvent pas la qualité du modèle.

La phase B prépare [30 paires français/anglais synthétiques](evaluation/decision-readiness-pairs.v1.json),
dont 20 de développement et 10 réservées à l’évaluation. Le propriétaire a validé les annotations
avant les essais C ; aucune relecture bilingue distincte et aveugle n’est revendiquée.
Le [protocole](evaluation/Decision-Evaluation-Protocol.md)
précise relecture, comparaisons, critères, identités des modèles, répétitions, latences et mesures.
`tools/Prepare-DecisionEvaluation.ps1` valide le corpus hors ligne et peut produire 60 tâches
désactivées d’une étape, avec manifeste et empreinte du corpus. Il n’importe et n’exécute aucune
tâche. L’[exécuteur comparatif](../tools/DecisionEvaluation/README.md) réalise désormais les pilotes
isolés autorisés et conserve observations, agrégats et évaluation des critères. Aucun déploiement
n’a été réalisé ; le routage intégré est maintenant livré en phase D ; exposition agentique différée.
