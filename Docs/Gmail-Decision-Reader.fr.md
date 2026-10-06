# Lecteur de décision Gmail

Cette extension ajoute un parcours facultatif, réservé aux workflows. Un appel collecte Gmail
une seule fois, classe les messages séquentiellement avec un modèle de décision local et rend
une couverture structurée. Un second outil produit le rapport Markdown par code. Les lecteurs
Gmail existants, leurs protections et les tâches enregistrées conservent leur fonctionnement.

## Configuration

Dans **Settings → Local decision models**, activez le lecteur et choisissez un modèle déjà
installé, par défaut `tev1:latest`. Aucun téléchargement automatique n'est effectué. Le fournisseur
pris en charge est `OllamaSystemOne`. Laissez l'endpoint vide pour hériter d'Ollama : seules les
adresses HTTP(S) de boucle locale sont acceptées, sans chemin, identifiants ou redirection.

Les modèles d'arbitrage et de secours héritent du modèle de chat Ollama lorsque les champs sont
vides, même si le fournisseur général actif est OpenAI. Le modèle de décision doit avoir une
identité installée distincte des modèles de chat, d'arbitrage, de secours et d'embedding sur le
même endpoint. Deux tags partageant le même digest ne contournent pas cette protection.

**Save** valide et enregistre localement, sans charger de modèle ni attendre une inférence.
**Refresh models** consulte le catalogue installé. **Test decision model** lance deux requêtes
synthétiques sur une salutation, indique l'identité et les temps observés, puis libère uniquement
le modèle de décision. Ce test ne lit aucun message Gmail.

Le maintien en mémoire vaut 10 secondes par défaut ; la libération en fin de lot est obligatoire.
La valeur zéro recharge le modèle pour chaque message et peut coûter beaucoup de temps. Les
limites par défaut sont 120 secondes pour une requête froide, 15 secondes pour une requête chaude,
un arbitrage ou un secours, et 180 secondes pour toute l'opération, collecte et attentes comprises.
Le nettoyage dispose d'un budget indépendant de cinq secondes, même après annulation.

## Création du workflow

Utilisez [la tâche d'exemple désactivée](samples/gmail-decision-watch.json) comme nouvelle tâche.
Elle autorise les groupes `gmailDecision` et `filesystem`, classe les messages récents, crée le
rapport et écrit `reports/gmail-watch.md` sous la racine de fichiers configurée. Un chemin absolu
nécessite toujours l'autorisation d'écriture de la tâche. L'exemple ne remplace aucune tâche de
production et ne modifie aucun horaire.

Dans l'éditeur d'étape, choisissez `gmailDecision.readClassify`, puis **Load triage example**.
Les champs dédiés permettent de modifier le nom, le type, les instructions et les descriptions
des réponses. Les boutons Move up/Move down définissent l'ordre des niveaux de priorité, numérotés
depuis zéro. **Apply question fields** valide ces champs dans le brouillon ; Apply les applique
aussi si des modifications sont en attente. Après une édition directe du JSON des questions,
utilisez **Reload fields from JSON** avant de reprendre l'édition des champs. Les mappings,
seuils par question/réponse et conflits entre questions sont éditables dans les arguments JSON.
Tout renommage exige la mise à jour de leurs références. Aucun profil global de questions n'est créé.

Les trois types sont `choice` (2 à 24 choix nommés), `noul` (score natif de la réponse vraie,
affiché ensuite comme booléen) et `score` (2 à 10 descriptions de niveaux ordonnés). Les
instructions et descriptions peuvent être en français ou en anglais. Les identifiants doivent
être littéraux et uniques, y compris sans collision de casse. Les clés, politiques, nombres,
valeurs d'énumération et mappings ne contiennent pas de placeholders. Le texte des questions peut
utiliser les placeholders ordinaires de tâche/date, mais aucun résultat précédent ou contexte
de messages ne peut modifier une question. Les références incohérentes et propriétés inconnues
sont rejetées avant la collecte.

`gmailDecision.renderReport` reçoit `{{mailTriage.output}}`, un titre et `includeNoise`. Il ne lit
pas Gmail, n'appelle aucun modèle et n'écrit aucun fichier. Ajoutez `filesystem.write` pour
enregistrer le Markdown avec les autorisations habituelles. N'ajoutez pas de résumé, de passage
Guardrails, de Knowledge/RAG ou de mise en forme par LLM à ce parcours.

Ces alias sont interdits aux agents, à Direct Chat, à `ai.prompt.toolAliases` et à `forEach`.
Le Tool tester exige une tâche sélectionnée et autorisée, ainsi que la case **Authorized workflow
test**. Un identifiant de tâche seul ou l'ancien groupe `gmail` ne suffit pas. L'extension ne
donne aucun droit d'envoi, suppression, marquage comme lu ou récupération de pièces jointes.

## Scores, arbitrage et couverture

Les scores natifs ne sont pas des probabilités calibrées d'exactitude. Les seuils par défaut sont
des points de départ. **Top answer score (%)** concerne le premier score, **Minimum lead
(percentage points)** l'écart entre les deux premiers, **Concentration score (%)** leur concentration,
et **Second-opinion budget (%)** le nombre maximal d'arbitrages. Les comparaisons sont inclusives,
sans arrondi. Deux scores de 0,81 et 0,16 donnent un écart de 65 points. Pour `noul`, un score vrai
de 0,05 donne une certitude de 95 % du côté faux ; le seuil d'affichage vrai/faux vaut séparément
50 % par défaut. La priorité utilise le niveau le plus probable, tout en conservant le score
pondéré natif ; on ne compare pas ce score pondéré à un seuil en pourcentage.

Le budget d'arbitrage est `min(M, floor(M × pourcentage / 100), maxCalls si non nul)` pour M
messages collectés. Les tentatives échouées consomment aussi le budget. Les réponses natives
valides restent **Provisional** si l'arbitrage est désactivé, épuisé ou échoue. Sans réponse valide,
le message devient **Unresolved**. Un secours local valide peut résoudre le message sans demander
une vérification humaine au seul motif que Tev1 était indisponible. Un choix explicitement
incertain reste provisoire. Si un modèle conserve un échange visible et l'autre le classe en bruit
de rang zéro, la catégorie qui attire l'attention est conservée, le désaccord est indiqué et les
valeurs de réponse/priorité du second avis restent intactes.

Le comportement d'indisponibilité par défaut, **LightweightLlmFallback**, effectue un seul appel
local simplifié par message concerné : sans outils, sans streaming, sans raisonnement, avec une
température de zéro et une réponse structurée strictement validée. Le même service fournit le
second avis indépendant, sans recevoir les scores ou réponses de Tev1. Aucun basculement cloud
ou vers l'ancien parcours à plusieurs passes n'est possible. **Fail** fait échouer l'étape en cas
d'indisponibilité technique. Une erreur transitoire peut être réessayée une seule fois. Deux
réponses malformées consécutives ouvrent le circuit pour le lot ; un modèle absent/incompatible
ou une route non prise en charge l'ouvre immédiatement. Les messages restants passent en secours,
sans relire Gmail ni relancer les sondes de décision.

Chaque message collecté reste présent dans le JSON de version 1, dans l'ordre source, avec un état
Classified, Provisional ou Unresolved. Les comptes respectent `classified + provisional + unresolved
= collected`. Les messages sautés au dépassement du délai portent `NotProcessedBeforeDeadline`.
Une annulation ne publie aucun résultat réussi. La collecte partielle est explicite, sans inventer
un total fournisseur. Au-delà de 512 Kio, les distributions détaillées sont supprimées en premier
avec `scoreDetailsOmitted`, sans supprimer de lignes ; si les métadonnées requises dépassent encore
la limite, l'étape échoue précisément.

Le rapport groupe les lignes par rang d'attention décroissant et conserve leur ordre dans chaque
groupe. Il expose couverture, modèles locaux, recours au secours/arbitrage et temps par phase.
Les lignes provisoires/non résolues portent un badge de vérification humaine. `includeNoise=false`
affiche le nombre de lignes volontairement cachées. Une collecte vide indique **No messages found**.
Expéditeur, objet et titre sont échappés ; les liens Gmail sont construits uniquement à partir
d'identifiants opaques hexadécimaux validés. Corps, extraits, prompts et raisonnement ne sont jamais
renvoyés au workflow.

## Mémoire, budgets et vérifications

Le contexte de décision observé de Tev1 est d'environ 2 050 tokens. En l'absence d'un tokenizer
local vérifié, l'estimateur retient une borne conservatrice d'un token par octet UTF-8 pour le BPE
avec repli par octet, et réserve 400 tokens pour le template, le scoring et la sortie. Les
questions ne sont jamais raccourcies ; celles qui laissent moins de 128 tokens conservateurs pour
le mail sont rejetées. Le texte est tronqué à une limite Unicode avec un marqueur d'omission.
L'historique cité retiré et le contenu courant tronqué sont distingués. Une troncature du contenu
courant force un second avis. L'appel classique a son propre contexte borné de 8 192 tokens et
une sortie limitée à 512 tokens. Cette prudence peut augmenter le taux d'arbitrage.

Des handles de fichiers détenus par le système d'exploitation coordonnent les processus UI et
Runner. Un fichier de rendez-vous vide ne constitue pas une propriété ; la mort du processus
libère son handle. Les embeddings en attente prennent la priorité entre deux messages, libèrent
le modèle de décision vérifié puis utilisent le même verrou global LLM. Les lecteurs conservent
leur progression et détectent un rechargement avant de choisir le délai froid/chaud. L'ordre des
verrous est intention embedding, frontière de requête de l'endpoint, puis verrou LLM. Le nettoyage
de fin de lot ne décharge pas le modèle tant qu'un autre lecteur conserve son intention, même avec
un alias de modèle. Les clients Ollama externes restent hors de cette coordination.

Runs affiche **Completed with warnings** et les rapports de workflow exposent les avertissements
de dégradation et de qualité. Les diagnostics utilisent les corrélations opération/tâche/étape,
l'index opaque du mail, les hashes du contrat/résultat, les estimations, scores, règles et temps.
Ils omettent corps, expéditeur/objet, prompts, raisonnement et texte arbitraire d'erreur fournisseur.

La tâche d'exemple utilise StopOnError. Un Retry ajouté volontairement au workflow recommence
la collecte et le classement du lot avec leur coût complet. L'évaluation de qualité sur des mails
FR/EN réels et des temps cibles de l'installation nécessite une autorisation distincte. Les sondes
synthétiques précédentes démontrent la faisabilité API, pas la précision de production. Aucun
déploiement, téléchargement, redémarrage ou migration de tâche n'est effectué avec cette implémentation.

Référence : [API Tev1 Ollama](https://ollama.com/library/tev1).
