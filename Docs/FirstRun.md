# MyLocalAgent — guide de première mise en route

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
dotnet restore
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

## 9. Outils sans identifiants

- **Filesystem** : aucun compte n’est requis. Les chemins relatifs restent sous
  la racine configurée ; une tâche peut recevoir des autorisations explicites
  supplémentaires pour des chemins absolus.
- **HTTP** : aucune clé n’est requise. Définir dans **Settings** les hôtes HTTP
  autorisés avant une tâche non supervisée. Une liste vide autorise tous les
  hôtes HTTP/HTTPS et convient seulement à un essai contrôlé.
- Les opérations sensibles restent désactivées par défaut : envoi d’e-mail,
  suppression de fichiers et HTTP POST. Les activer uniquement lorsqu’une tâche
  précise le justifie.

## 10. Contrôle final avant la première tâche

Dans **Tools**, vérifier que les connecteurs nécessaires affichent `Connected` :

- Gmail ;
- Calendar ;
- Dropbox ;
- Web Search, seulement s’il est utilisé.

La création de la première tâche peut alors se faire en n’activant que les
groupes d’outils et les permissions nécessaires à son objectif.

Pour le premier essai, utilisez **Run now**. La sortie suit automatiquement les
nouvelles lignes. Pour un workflow, **Show workflow context** affiche les sorties
accumulées après chaque étape réussie et reste consultable jusqu’au lancement de
la tâche suivante. Ce contexte peut contenir les données traitées par la tâche.
