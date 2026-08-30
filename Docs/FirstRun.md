# MyLocalAgent — guide de première mise en route

Version de l’application : **2.1.0**.

Ce guide reprend le parcours réalisé pour compiler l’application, la publier et
configurer ses connecteurs. Il ne couvre ni la création d’une tâche métier ni
les incidents de mise en place.

## 1. Prérequis

- Windows 10 ou 11 x64 ;
- .NET SDK 10 ;
- PowerShell 5.1 ou PowerShell 7 ;
- Ollama installé, démarré, avec le modèle choisi téléchargé ;
- un compte Google, Dropbox et/ou Ollama.com uniquement pour les connecteurs
  utilisés.

Pour le modèle local par défaut :

```powershell
ollama pull gemma4:12b
ollama list
```

Le modèle affiché par `ollama list` doit correspondre exactement à celui choisi
dans MyLocalAgent.

## 2. Compiler et vérifier le code source

Depuis la racine du dépôt (`E:\OD.MyLocalAgent`) :

```powershell
dotnet restore OD.MyLocalAgent.sln -m:1
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
```

La compilation et les tests doivent se terminer sans erreur. Un test
d’intégration Ollama peut être ignoré lorsqu’il dépend de l’environnement local.

## 3. Publier l’application

Publier la version autonome Windows :

```powershell
.\scripts\publish.ps1 -NoRestore
```

Le script publie la version **Release**, en mode autonome (`win-x64`), dans :

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent
```

Il produit notamment :

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\UI\OD.MyLocalAgent.UI.exe
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe
```

Pour déployer une modification ultérieure, il suffit de relancer ce même script
après avoir recompilé et testé. Fermer MyLocalAgent avant la publication.

## 4. Premier démarrage et réglages locaux

Lancer :

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\UI\OD.MyLocalAgent.UI.exe
```

Dans **Settings** :

1. Vérifier l’endpoint Ollama local, normalement `http://localhost:11434`.
2. Vérifier ou sélectionner le modèle local, puis utiliser **Refresh models** si
   nécessaire.
3. Cliquer sur **Test connection** et vérifier que l’endpoint et le modèle sont
   disponibles.
4. Vérifier la racine du système de fichiers. Les chemins relatifs des tâches
   resteront confinés dans ce dossier.
5. Laisser `Scheduler:RunnerPath` vide : l’application retrouve alors
   automatiquement le Runner publié à côté d’elle.
6. Utiliser **Create desktop shortcut** si un raccourci Windows est souhaité.

Les boutons de copie permettent de récupérer séparément le dossier des rapports
et le dossier de l’exécutable.

Les rapports de tâches sont toujours écrits dans le stockage central du workspace, sous
`<FileSystem:RootPath>\.od-my-local-agent\outputs\reports\<taskId>\`. La racine configurée
reste le bac à sable de la tâche : le dossier d’installation et `%LOCALAPPDATA%` ne sont pas des
destinations de repli pour les rapports. Une tâche sans workflow y produit un rapport Markdown
normal, sans section d’étapes vide.

Le dossier `.od-my-local-agent` est réservé à l’application et masqué par Windows. Les outils de
fichiers gérés ne peuvent ni l’afficher ni y accéder, même si une autorisation absolue le couvre ;
les opérations récursives ou destructrices sur un dossier parent sont également refusées. Le
masquage reste une protection visuelle. Les capacités `PowerShellExecution` et `ShellExecute`
lancent des processus locaux non confinés et ne doivent être accordées qu’à une tâche de confiance.

### Instructions IA globales

Dans **Settings → AI provider**, le champ **Project-wide AI instructions** permet de définir des
conventions stables, par exemple la langue de réponse, le format de sortie ou des règles de
nommage. Gardez-le court et réservez les demandes spécifiques au prompt de la tâche.

Chaque tâche applique ces instructions par défaut. Dans l’éditeur de tâche, décochez
**Apply project-wide AI instructions to this task** pour cette seule tâche, puis utilisez
**View system prompt** pour consulter le texte effectivement configuré ou confirmer qu’il ne sera
pas ajouté. Les garde-fous internes, les outils autorisés et les instructions de la tâche restent
prioritaires.

### Modèle temporaire pour une tâche

Le modèle choisi dans **Settings** reste le défaut. Pour une tâche ponctuelle, ajoutez
`"model": "nom-du-modèle"` dans son JSON ; pour une seule étape, ajoutez la même propriété à
une étape objet `ai.prompt`. La valeur de l’étape est prioritaire. Elle est limitée à cette
exécution et le défaut est restauré automatiquement. Avec Ollama, installez d’abord le tag exact
avec `ollama pull nom-du-modèle` : l’application le préchauffe avant l’exécution et échoue sans
modifier Settings s’il manque.

Le préchauffage ne concerne que le fournisseur local Ollama. Il n’est jamais envoyé vers OpenAI,
OpenRouter ou un autre endpoint cloud : il ne consomme donc aucun crédit cloud.

### Raisonnement des modèles

Activez ou désactivez **Reasoning** dans **Direct Chat** pour la prochaine réponse, y compris en
mode agent. Pour une tâche agentique, utilisez **Enable reasoning for agentic task calls** dans
l’éditeur de tâche. Le raisonnement est activé par défaut pour ces deux usages et désactivé pour
les opérations bornées : Trust Filter, étapes `ai.prompt`, compression d’historique, skills,
assistants internes et préchauffage.

Lorsqu’un fournisseur renvoie un texte de raisonnement affichable, Direct Chat le montre dans le
volet repliable **Model reasoning**. Ce texte ne fait pas partie de la réponse assistant et n’est
pas enregistré dans l’historique. Certains modèles ne renvoient aucun raisonnement lisible ; la
réponse normale reste alors disponible. Si le fournisseur ne prend pas en charge ce réglage,
l’application poursuit automatiquement avec le comportement par défaut du fournisseur.

### Trust Filter : vitesse et grands documents

Trust Filter demande une décision JSON courte, sans outils ni raisonnement. Cela évite que les
modèles de type reasoning passent du temps à délibérer avant d’émettre le score. Pour une page web
ou un PDF long, la capacité de sortie augmente avec la taille du texte (de 1 024 à 8 192 jetons)
afin de conserver les extraits suspects distincts. En cas de timeout ou d’échec, le contenu reste
protégé : il est livré uniquement sous forme Base64 marquée et l’écran indique le timeout configuré
lorsqu’il en est la cause.

## 5. Valider le pipeline local de fichiers

Le scénario d’acceptation suivant crée, écrit puis relit un fichier sous la
racine de fichiers configurée, et produit un rapport :

```powershell
$runner = Join-Path $env:LOCALAPPDATA `
  'Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe'

Copy-Item `
  -LiteralPath '.\Docs\samples\acceptance-filesystem.json' `
  -Destination (Join-Path $env:LOCALAPPDATA `
    'OD.MyLocalAgent\tasks\acceptance-filesystem.json')

& $runner run --task acceptance-filesystem
```

Vérifier l’état `Success` et le rapport indiqué par le Runner. Une fois la
vérification terminée, supprimer ou désenregistrer cette tâche d’acceptation.

## 6. Configurer Google : Gmail et Calendar

### Dans Google Cloud

1. Créer ou sélectionner un projet Google Cloud.
2. Activer **Gmail API** et **Google Calendar API**.
3. Configurer l’écran de consentement OAuth et ajouter le compte utilisé comme
   utilisateur de test tant que l’application reste en test.
4. Créer un client OAuth de type **Desktop app**.

### Dans MyLocalAgent

1. Fermer l’application.
2. Ouvrir `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`.
3. Renseigner les valeurs du client sans supprimer les autres sections :

```json
"Google": {
  "ClientId": "YOUR_DESKTOP_CLIENT_ID",
  "ClientSecret": "YOUR_DESKTOP_CLIENT_SECRET"
}
```

4. Relancer MyLocalAgent puis ouvrir **Tools**.
5. Cliquer sur **Connect** et terminer le consentement dans le navigateur avec
   le même compte Windows que celui qui exécutera les tâches planifiées.
6. Dans **Tools**, lancer séparément **Test connection** pour Gmail puis pour
   Calendar.

Les jetons Google renouvelables sont enregistrés sous
`%LOCALAPPDATA%\OD.MyLocalAgent\Auth\Google`. Le Runner ne peut pas ouvrir le
navigateur : le consentement doit être effectué depuis l’interface.

## 7. Configurer Dropbox avec un refresh token

Cette méthode convient aux tâches planifiées : le refresh token permet de
renouveler automatiquement les jetons d’accès courts.

### Dans la Dropbox App Console

1. Ouvrir <https://www.dropbox.com/developers/apps> puis créer une application.
2. Choisir **Scoped access**.
3. Choisir **App folder** pour limiter l’accès, ou **Full Dropbox** si la tâche
   doit accéder à des fichiers déjà présents dans toute la Dropbox.
4. Dans **Permissions**, activer :
   - `files.metadata.read` ;
   - `files.content.read` ;
   - `files.content.write`.
5. Cliquer sur **Submit**.
6. Relever l’**App key**. Ne pas communiquer l’App secret.

### Obtenir le refresh token par PKCE

Le script suivant ouvre une autorisation Dropbox et copie le refresh token dans
le presse-papiers. Il ne l’écrit pas sur disque :

```powershell
.\scripts\get-dropbox-refresh-token.ps1
```

1. Saisir l’App key à l’invite.
2. Autoriser l’application dans le navigateur.
3. Copier le code affiché par Dropbox et le coller dans PowerShell.
4. Vérifier le message indiquant que le refresh token est dans le
   presse-papiers.

### Dans MyLocalAgent

1. Ouvrir **Tools → Dropbox**.
2. Laisser **Replace Dropbox access token** vide.
3. Coller le refresh token dans **Replace Dropbox refresh token**.
4. Renseigner l’App key dans **Replace Dropbox app key**.
5. Cliquer sur **Save credentials**, puis sur **Test connection**.

Les identifiants Dropbox sont chiffrés par Windows DPAPI pour le compte courant
et ne sont pas écrits dans `settings.json`.

## 8. Configurer Web Search (Ollama cloud)

Cette fonction est optionnelle : les requêtes et les extraits de résultats sont
envoyés à Ollama.com.

1. Se connecter à <https://ollama.com/settings/keys> et créer une clé API.
2. Copier la clé sans la communiquer.
3. Dans **Tools → Web search (Ollama)**, cocher **Enable Ollama Web Search**.
4. Conserver l’endpoint officiel :

```text
https://ollama.com/api/web_search
```

5. Coller la clé dans **Replace Ollama Web Search API key**.
6. Cliquer sur **Save Web Search**, puis sur **Test connection**.

La clé est protégée par Windows DPAPI. Activer ensuite le groupe d’outils
**Web search (web) (cloud)** seulement sur les tâches qui en ont besoin.

Pour une veille planifiée multi-thèmes, activez **Web Discovery** et utilisez
`research.discover`. Son mode `sliding` par défaut vérifie localement les dates de publication
après téléchargement et élargit la fenêtre de 2 à 4 puis 7 jours calendaires seulement si
nécessaire. Les pages anciennes, futures ou sans date sont exclues. Les titres et résumés finaux
sont livrés uniquement dans le bloc `protectedResults` protégé par Guardrails ; un contrôle
désactivé ou en échec produit toujours du Base64 marqué. La traduction facultative ne s’exécute
qu’après un verdict propre et son résultat est contrôlé à nouveau.
Les erreurs du fournisseur ne sont pas assimilées à une collecte vide : `searchStatus` distingue
une recherche disponible, dégradée, indisponible ou limitée (HTTP 429). En cas de limitation
globale, les requêtes restantes sont arrêtées et le workflow indique clairement que la
disponibilité des sources n’a pas pu être déterminée.
Les exemples de veille éditoriale restent prudents lorsque les sources manquent : la veille
LinkedIn ne produit aucun post avec zéro candidat. L’article du dimanche est focalisé sur la
source disponible lorsqu’il n’y en a qu’une, devient une analyse croisée limitée avec deux, puis
un article multi-source à partir de trois. Seul zéro candidat produit uniquement un diagnostic.

### Activer des propositions éditoriales non répétitives

Le workflow fourni `Docs/samples/editorial-watch-daily.json` peut conserver une mémoire de
propositions optionnelle possédée par la tâche. Il active `research`, `sqlite` et `filesystem`,
accorde ReadWrite au dossier de la base et déclare `SqliteWrite`. Sa première étape appelle
`sqlite.createProposalDatabase` ; `research.discover` relit ensuite le même fichier via
`previousProposalsDatabasePath`, avec `previousProposalsLookbackMonths: 6`,
`usePreviousProposalUsedTag: false` et
`previousProposalsSemanticSimilarityThreshold: 0.92`. L’étape `ai.prompt` finale ne peut appeler
que `sqlite.storeProposal` pour les sujets effectivement retenus. Ce réglage par défaut exclut déjà toutes les propositions récentes, utilisées ou
non ; passez-la à `true` seulement pour continuer aussi à exclure les anciennes lignes marquées
comme publiées. Les embeddings de propositions exigent le fournisseur local Ollama configuré
pour Knowledge.

Ne créez pas ce schéma avec `sqlite.execute` et ne placez jamais de SQL, vecteur, modèle
d’embedding ou date de création dans le prompt. Une base absente ou incompatible et des
embeddings locaux indisponibles font échouer la découverte au lieu de désactiver silencieusement
la mémoire.
Une découverte réussie n’expose que des diagnostics agrégés, notamment
`proposalMemoryConsideredCount`, `proposalMemoryIncompatibleEmbeddingCount`,
`rejectedPreviousProposalExactCount`, `rejectedPreviousProposalSemanticCount` et
`rejectedPreviousProposalCount`.

## 9. Outils sans identifiants

- **Filesystem** : aucun compte n’est requis. Les chemins relatifs restent sous
  la racine configurée ; une tâche peut recevoir des autorisations explicites
  supplémentaires pour des chemins absolus.
- **HTTP** : aucune clé n’est requise. Définir dans **Settings** les hôtes HTTP
  autorisés avant une tâche non supervisée. Une liste vide autorise tous les
  hôtes HTTP/HTTPS et convient seulement à un essai contrôlé.
- **SQLite** : utilisez uniquement un fichier local jetable dans la racine autorisée.
  Les lectures restent soumises à l’autorisation du chemin ; une écriture exige en plus la capacité
  `SqliteWrite` de la tâche. Chaque tâche conserve son choix de filtrage Guardrails pour les
  résultats SQLite bornés ; si ce filtrage est demandé mais indisponible, le résultat est retenu.
- **Exécution locale** : `filesystem.shellExecute` exige `ShellExecute`, tandis que
  `filesystem.powerShell` exige la capacité distincte `PowerShellExecution` de la tâche.
- Les opérations sensibles restent désactivées par défaut : envoi d’e-mail,
  suppression de fichiers, HTTP POST, écriture SQLite et exécution de processus. Les activer
  uniquement lorsqu’une tâche précise le justifie.

### Première tâche SQLite sûre

Utilisez une base jetable sous la racine de fichiers configurée, par exemple
`data/sample.db`. Ne renseignez jamais une chaîne de connexion, une URI, un chemin
réseau ou `:memory:`.

1. Dans l’éditeur de tâche, cochez le groupe **SQLite** dans **Tool whitelist**.
2. Pour un essai en lecture, conservez uniquement l’autorisation ReadOnly du
   chemin ; les chemins relatifs restent sous `FileSystem:RootPath`.
3. Pour créer la base de démonstration, accordez temporairement ReadWrite au
   même emplacement et activez `SqliteWrite` dans **Sensitive operation permissions**.
   Le groupe, l’autorisation ReadWrite et cette capacité de tâche sont tous requis.
4. Dans **Tool tester**, sélectionnez `sqlite.execute` et créez la table avec une
   seule instruction :

```json
{
  "databasePath": "data/sample.db",
  "sql": "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL)",
  "createIfMissing": true
}
```

5. Désactivez à nouveau l’écriture SQLite, puis sélectionnez `sqlite.query` et
   vérifiez la lecture avec :

```json
{
  "databasePath": "data/sample.db",
  "sql": "SELECT id, name FROM items WHERE id >= @minimumId ORDER BY id",
  "parameters": { "@minimumId": 1 },
  "maxRows": 25
}
```

Le résultat de lecture est borné et peut être protégé par Guardrails. La case
**Protect SQLite read results with Guardrails** appartient à chaque tâche ; son
réglage par défaut dans **Settings → SQLite** ne s’applique qu’aux nouvelles tâches.
Avec un fournisseur OpenAI-compatible, le filtrage peut envoyer le contenu borné
de la base au fournisseur cloud. Désactiver le filtrage pour une tâche livre les
données en clair au workflow ou au modèle suivant. Supprimez ensuite la base de
démonstration ou laissez l’autorisation d’écriture désactivée.

## 10. Contrôle final avant la première tâche

Dans **Tools**, vérifier que les connecteurs nécessaires affichent `Connected` :

- Gmail ;
- Calendar ;
- Dropbox ;
- Web Search, seulement s’il est utilisé.

La création de la première tâche peut alors se faire en n’activant que les
groupes d’outils et les permissions nécessaires à son objectif.

Pour le premier essai, utilisez **Run now**. La sortie suit automatiquement les
nouvelles lignes tant que **Auto-scroll** reste cochée. Décochez-la pour conserver
la position de lecture, puis recochez-la pour revenir à la dernière ligne. Pour un
workflow, **Show workflow context** affiche les sorties
accumulées après chaque étape réussie et reste consultable jusqu’au lancement de
la tâche suivante. Ce contexte peut contenir les données traitées par la tâche.

Pour un diagnostic ponctuel, cochez **Debug (next run)** avant l’exécution : les
requêtes/réponses LLM et les arguments d’outils de cette exécution sont visibles
uniquement dans le Runner Output. Ce contenu peut inclure les données de la tâche
et ne doit pas être partagé ; les secrets configurés restent exclus. **Large**
ajoute 300 pixels au panneau pour faciliter la lecture, sans redimensionner la fenêtre.

## 11. Écrire un workflow sans partir de zéro

La carte **Workflow JSON** de l’éditeur de tâche propose trois aides. Les deux
premières sont entièrement déterministes : elles n’appellent aucun modèle, aucun
outil, aucun fichier et aucun réseau.

- **Tool catalog** : la référence consultable de tous les alias du Tool Map, avec
  description, arguments typés, exemples, valeurs autorisées et placeholders.
  Sélectionner une entrée en affiche l’aperçu, puis **Insert** l’insère à la
  position du curseur en gardant la fenêtre ouverte pour accumuler plusieurs
  étapes. **Cancel** abandonne toute la session. Si le JSON n’est pas analysable,
  le snippet est inséré tel quel avec un avertissement de correction manuelle.
- **Edit workflow text** : l’éditeur piloté par les métadonnées liste et modifie les arguments
  texte éligibles déjà présents, dans l’ordre, notamment `ai.prompt.prompt`,
  `filesystem.write.content` et `filesystem.powerShell.script`. **Apply** écrit toutes les
  modifications atomiquement.
- **AI Helper** : génération assistée par le modèle, à réserver aux cas où une
  description en anglais est plus rapide qu’une écriture directe.

Une étape provenant d’un groupe d’outils non autorisé pour la tâche affiche un
avertissement et une case **décochée** : l’autorisation n’est jamais élargie
silencieusement. Les capacités requises sont signalées et doivent être accordées explicitement.

La liste **Tool whitelist** de l’éditeur est générée depuis le catalogue intégré à
l’application. Une version future peut donc ajouter un groupe sans nouveau câblage de cette
liste ; le groupe apparaîtra décoché et restera séparé des autorisations de chemins et de
Safety. Il s’agit d’un catalogue compilé, pas d’un chargeur de plug-ins externes.

**Undo assisted change** annule d’un seul geste la dernière application assistée,
quelle qu’en soit l’origine, jusqu’à la sauvegarde ou au rechargement. Comme pour
toute modification de l’éditeur, seul **Save task** persiste le résultat.

### Répéter une étape pour chaque élément

Dans l’éditeur d’étape, **Repeat for each item** est proposé uniquement pour
`ai.prompt` et `filesystem.write`. La source doit être exactement un placeholder vers un tableau
produit par une étape précédente, par exemple `{{mails.output.messages}}` :

```json
{
  "step": "ai.prompt",
  "id": "analyses",
  "forEach": "{{mails.output.messages}}",
  "args": {
    "prompt": "Analyse le mail n°{{foreach.position}} : {{foreach.item.content}}"
  }
}
```

Les éléments sont traités séquentiellement, dans leur ordre d’origine, avec une limite de 100.
`{{foreach.item}}` désigne l’élément courant et `{{foreach.position}}` sa position à partir de 1.
La sortie de l’étape est un tableau JSON ordonné. Un tableau vide réussit avec `[]` ; une source
qui n’est pas un tableau échoue. Avec `Retry`, seul l’élément courant est retenté. Après un échec
final, les éléments restants ne sont pas exécutés et aucune sortie partielle n’est publiée, mais
les fichiers déjà écrits ne sont pas annulés. Pour écrire un fichier par élément, placez
`{{foreach.position}}` ou une propriété sûre de l’élément dans le chemin afin d’éviter de remplacer
le même fichier à chaque passage.

## 12. Écrire une tâche sans workflow avec un nom daté

Pour une tâche enregistrée sans workflow, MyLocalAgent peut remplacer `{{Today}}`, `{{Now}}`,
`{{task.id}}` et `{{task.name}}` juste avant que le prompt atteigne l’agent. Exemple :

```text
Create the Markdown report with the filesystem tool at:
C:\Reports\Sort-{{Today[yyyyMMdd]}}.md
```

À l’heure locale d’exécution `2026-08-03T14:30:25+02:00`, le chemin devient
`C:\Reports\Sort-20260803.md`. La sauvegarde et l’enregistrement du planning conservent le
modèle intact ; c’est l’heure d’exécution qui fournit la date. `{{Now[yyyyMMdd-HHmmss]}}`
permet d’ajouter les secondes lorsque plusieurs exécutions sont possibles le même jour.

La même expansion s’applique à `run` planifié/CLI, **Run now**, `test-prompt` et **Test the
prompt** dans l’éditeur, y compris au texte courant non sauvegardé. Le remplacement se fait en
une seule passe. Un marqueur inconnu ou réservé aux workflows reste inchangé avec un warning
non bloquant ; `{{previous.output}}` s’utilise uniquement dans un workflow. Les warnings
durables conservent les catégories et compteurs, jamais le prompt ou le marqueur brut. L’AI
Playground et les autres surfaces sans tâche restent inchangés.

Un chemin absolu résolu exige toujours le groupe File system de la tâche, un emplacement
autorisé en lecture-écriture et les droits Windows. L’expansion ne garantit ni que le modèle
choisira l’outil, ni que l’opération réussira.

## 13. Comprendre les contenus Gmail et HTTP compacts

Les lectures Gmail minimisent chaque message avant tout appel IA, puis traitent les projections
par lots. Le résultat d’une lecture réussie contient uniquement les en-têtes projetés From, To,
Subject et Received avec un résumé factuel protégé. Snippet, Body, l’historique cité et les
suffixes omis ne sont jamais transmis au workflow. `http.getText` retourne également un résumé
protégé uniquement ; `http.get` conserve un préfixe compact protégé, jamais la réponse complète.

Les compteurs `originalCharacterCount`, `projectedCharacterCount` et
`omittedCharacterCount` rendent la réduction visible. Cette compaction est une frontière de
minimisation, pas une preuve que le texte omis était sûr. Un résumé généré peut omettre des
détails : si l’exhaustivité compte, relire l’original dans Gmail ou à l’URL source.
# Stage 22 — fichiers locaux

Lors de la création d'une tâche, **Archives**, **Word documents**, **Spreadsheets** et **PDF
documents** sont décochés par défaut. Activez seulement le groupe requis puis ajoutez séparément
les emplacements autorisés en lecture ou lecture/écriture. L'extraction et la création ZIP
nécessitent aussi la capacité `ArchiveWrite` sur la tâche. Les textes extraits sont des
données non fiables; aucun OCR, mot de passe ou recalcul de formule n'est effectué.

# Autorisations sensibles par tâche

Les opérations sensibles sont refusées par défaut. Il n'existe plus de réglage global Safety :
chaque tâche doit accorder explicitement la capacité nécessaire, en plus du groupe d'outils et des
chemins `fileSystemAccess`. PowerShell et Shell Execute sont deux droits distincts. Une ancienne
tâche sans champ `capabilities` reste lisible mais ne reçoit aucun droit sensible. Utilisez
**Edit workflow text** pour modifier les prompts IA et scripts PowerShell longs sans modifier la
structure des étapes.
# Connaissances intégrées

Dans **Settings → Knowledge embeddings**, choisissez et testez le fournisseur et le modèle d’embeddings indépendamment du modèle de chat ; avec Ollama, la valeur par défaut épinglée est le modèle multilingue `embeddinggemma:300m`. Ouvrez ensuite **Knowledge**, saisissez un nom et au moins un tag d’autorisation, puis importez un fichier TXT, Markdown, DOCX ou un PDF textuel. L’OCR n’est pas pris en charge. Activez le groupe `knowledge` et choisissez explicitement les tags autorisés pour chaque tâche ou conversation Direct Chat ; une portée vide n’accorde aucun accès. Après une modification du modèle ou du découpage, utilisez **Reindex library** : les nouvelles révisions sont préparées puis activées ensemble uniquement si la reconstruction complète réussit.
