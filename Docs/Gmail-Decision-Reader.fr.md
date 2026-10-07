# Lecteur de décision Gmail — moteur global

Le lecteur optionnel réservé aux workflows collecte Gmail une fois, classe les messages bornés
séquentiellement via le service commun et renvoie une couverture sans corps de mail.
`gmailDecision.renderReport` produit le Markdown de façon déterministe, sans appel modèle, connecteur
ou fichier. Les lecteurs Gmail protégés existants conservent leur contrat. Aucune planification de
production n’est réécrite.

## Configuration et création

Activez le lecteur et accordez séparément le groupe `gmailDecision` à la tâche. Choisissez **Use application
LLM** ou **Use a decision model** dans **Settings → Decision models**. L’activation générique reste
indépendante. Le workflow/lot capture une configuration, un fournisseur et un modèle uniques. Le modèle
central local garde sa résidence habituelle ; le natif dédié utilise un nettoyage borné respectant les
autres propriétaires. Le mail brut ne part jamais vers un fournisseur cloud, même via un alias Ollama
adossé à un modèle distant. Une configuration incompatible échoue sans choisir un autre fournisseur.

Arguments obligatoires : `questions`, `reportMapping` et **`contractVersion: 2`**. De 1 à 8 questions
nommées peuvent combiner choice (2–24 choix), noul (booléen) et score (2–10 niveaux ordonnés). Les
questions et politiques sont littérales ; une sortie de mail ne peut pas les modifier. Champs facultatifs :
selection Today/Unread/Recent, daysBack uniquement avec Recent (0–3650), maxMessages dans les limites
Settings/Guardrails et batchTimeoutSeconds pour raccourcir le délai total. Voir l’[exemple désactivé](samples/gmail-decision-watch.json)
et le [Technical Manual](Technical-Manual.html#gmail-decision) pour la syntaxe complète.

`acceptance` natif garde les unités historiques en pourcentage, avec surcharges perQuestion/perChoice
renforçant les seuils globaux. Tout objet explicite est incompatible avec le mode central. Les réponses
noul centrales sont des booléens sémantiques ; score fournit un indice de niveau à partir de zéro, jamais
une espérance native pondérée. Un mapping replyTrueAtPercent différent de 50 doit être revu en mode central.

## Migration volontaire

La version 2 marque une migration explicite, sans conversion automatique. Une version absente/ancienne
bloque l’exécution. Avant de fixer contractVersion à 2 :

1. Choisissez le moteur global. Hybrid n’est compatible qu’avec NativeDecision, LlmOnly qu’avec CentralLlm.
   Les nouvelles étapes omettent classificationMode ; l’ancien champ ne remplace jamais Settings.
2. Retirez volontairement arbitration et unavailableBehavior, même avec false ou Fail. Leurs anciennes
   valeurs Settings restent enregistrées mais inactives. Aucun second avis ni repli n’est appelé.
3. Revoyez acceptance natif, alwaysArbitrateChoices et conflits probabilistes. Un seuil numérique ne doit
   pas disparaître silencieusement : conservez le natif ou modifiez le workflow délibérément.
4. Conservez collecte, limites, questions, labels/rangs de rapport et permissions indépendantes.
5. Adaptez les consommateurs à schemaVersion 2, engine, capabilities et scores absents/nuls. Aucune tâche
   planifiée n’est sauvegardée ou replanifiée automatiquement ; édition et sauvegarde n’infèrent pas.

## Couverture et rapports

La version 2 indique `engine`, `capabilities.scores`, `acceptanceBasis`, `provider`, le modèle dans
`effectiveModels.decision` et `classificationMode: Global`. La base native est nativeThresholds, la base
centrale validatedChoice. Les scores centraux restent absents/vides, sans distribution inventée.
Chaque mail collecté reste présent : Classified est résolu, Provisional conserve une réponse native
nécessitant revue, Unresolved signifie absence de réponse valide ou délai dépassé. Un choix sémantique
review reste un choix central ordinaire. Un échec technique ne devient jamais une catégorie normale.

Les compteurs vérifient classified + provisional + unresolved = collected. Les compteurs historiques
arbitrage/repli restent à zéro ; directLlmResolved et classificationMs décrivent les appels centraux.
Le rendu accepte les anciens rapports version 1 et les nouveaux version 2, valide types/identités/compteurs,
échappe le texte et affiche base, capacité, omissions et avertissements. includeNoise=false masque les
catégories de rang zéro en indiquant leur nombre. Une collecte vide affiche **No messages found**.

La collecte et les délais restent bornés. Les adaptateurs communs budgètent le prompt complet ; une
source native dépassant la limite conservatrice de 2050 tokens devient indisponible sans appeler le LLM.
Les journaux contiennent identités, empreinte de questions, statut, raisons assainies et durées, sans
corps de mail. Un Retry externe volontaire répète collecte et classement ; il ne constitue pas un repli
interne vers un autre moteur. Les tests utilisent des réponses simulées. Évaluation réelle, téléchargement,
déploiement et publication nécessitent une demande distincte.

Voir le [guide commun](Decision-Evaluation.fr.md), les [résultats C](evaluation/Stage32C-Results.fr.md)
et le [contrat structuré Ollama](https://docs.ollama.com/capabilities/structured-outputs).
