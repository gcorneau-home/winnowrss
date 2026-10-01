<p align="center"><img src="assets/icon/winnow-128.png" width="96" alt=""></p>

# WinnowRSS

[English](README.md) · **Français**

Un lecteur RSS simple pour Windows qui **filtre les articles selon vos intérêts avec un modèle de langue qui tourne sur votre propre PC**. Vous décrivez en mots simples ce qui vous intéresse et ce qui ne vous intéresse pas ; les articles qui ne correspondent pas sont mis de côté avec la raison, jamais supprimés.

> Construit avec [Claude Code](https://claude.com/claude-code) : l'auteur s'était fixé le défi de **n'écrire aucune ligne de code à la main**. Conception, code, tests, icône et documentation ont tous été produits par l'agent d'IA ; l'auteur décidait et testait. Voir [docs/DESIGN.md](docs/DESIGN.md) (en anglais).

![WinnowRSS : flux par catégories, articles filtrés barrés, un article gardé parce que son flux est de confiance](docs/images/WinnowRSS.jpg)

## Fonctionnalités

- **Flux classés par catégories**, affichés en arbre avec les articles sous chaque flux ; un glisser-déposer change un flux de catégorie.
- **Lecture en onglets** : l'en-tête de l'article (titre, auteur, date, description, étiquettes), puis le contenu complet. Les liens s'ouvrent dans votre navigateur ; les scripts des flux ne s'exécutent jamais.
- **Filtre d'intérêt** avec un modèle local (Ollama) : intérêts et exclusions en mots simples, mots-clés et flux de confiance comme règles, et un verdict avec sa raison pour chaque article. Les articles filtrés restent dans l'arbre, grisés, ou masqués.
- **Archives** par catégorie (l'article et ses images sont conservés), **corbeille** récupérable, épingles, notes 👍/👎.
- **Recherche plein texte** dans tous les articles, sans tenir compte des accents.
- **Thèmes** : clair, sombre, celui de Windows, ou n'importe quel **thème de couleurs VS Code** depuis Open VSX ou un fichier.
- Interface en **anglais, français et espagnol**, qui change de langue en direct.
- **Réglages** : ouvrir les articles d'un clic simple ou double, façon de signaler les articles non lus et filtrés, nombre maximum d'onglets ouverts, zoom par défaut.
- Les flux s'actualisent toutes les 30 minutes. Les articles lus et la corbeille sont purgés après 30 jours, sauf les articles épinglés, notés et archivés.

## Prérequis

- Windows 10 ou 11, 64 bits.
- Le **runtime Microsoft Edge WebView2**, déjà présent sur Windows 11 et sur la plupart des Windows 10 ([téléchargement](https://developer.microsoft.com/microsoft-edge/webview2/)).
- Pour le filtre (facultatif) : [Ollama](https://ollama.com) et le modèle `qwen3:8b`, qui demande environ 6 Go de mémoire sur la carte graphique (il tourne aussi sur le processeur, plus lentement).

## Installation

### Depuis une release

1. Téléchargez `WinnowRSS-<version>-win-x64.zip` depuis la page [Releases](../../releases).
2. Extrayez-le où vous voulez, par exemple dans `%LOCALAPPDATA%\Programs\WinnowRSS`, et lancez `WinnowRSS.exe`. Rien d'autre à installer : .NET est inclus.
3. L'exécutable n'est pas signé : Windows SmartScreen peut vous avertir la première fois. Choisissez **Informations complémentaires**, puis **Exécuter quand même**.

### Depuis les sources

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download).

```
git clone <ce dépôt>
cd winnowrss
dotnet run --project src/Winnow.App
```

Tests : `dotnet test WinnowRSS.sln --filter "Category!=UI"` (sans ce filtre, les tests d'interface ouvrent des fenêtres sur votre bureau).

## Premiers pas

1. **Catégorie**, dans la barre d'outils, crée une catégorie ; sélectionnez-la, puis **Flux** ajoute un flux à partir de son adresse (RSS ou Atom).
2. Dépliez le flux et double-cliquez sur un article pour le lire. Les articles non lus sont en gras, précédés d'une pastille de couleur.
3. Un clic droit sur un flux ou un article donne accès au reste : renommer, actualiser, ouvrir les articles non lus, archiver, mettre à la corbeille.

## Le filtre

1. Installez Ollama, puis dans un terminal : `ollama pull qwen3:8b`.
2. Dans WinnowRSS, **Filtre** dans la barre d'outils : cochez **Filtrer les nouveaux articles avec un modèle local**, puis **Tester la connexion**.
3. Écrivez vos **intérêts** (sujets à garder) et vos **exclusions** (sujets à écarter même s'ils correspondent à un intérêt), une courte expression chacun, en français ou en anglais. Un article qui ne correspond à aucun intérêt est filtré lui aussi.
4. Deux règles s'appliquent avant le modèle, instantanément :
   - **Mots-clés** : un titre qui contient l'un d'eux (mot entier, majuscules ou non) est filtré. Pratique pour les sujets récurrents sur lesquels le modèle hésite.
   - **Flux de confiance** : jamais filtrés, tous leurs articles sont gardés.
5. Le bouton en forme d'œil, dans la barre d'outils, affiche ou masque les articles filtrés. Survolez un article pour voir pourquoi il a été gardé ou filtré.

Conseils : gardez des critères courts et concrets ; après une modification, cochez **Refiltrer les articles non lus après l'enregistrement**. Notez les articles 👍/👎 au fil de vos lectures : le [banc d'essai du filtre](tools/Winnow.FilterBench) rejoue le filtre sur vos notes pour comparer modèles et prompts (`dotnet run --project tools/Winnow.FilterBench -- --help`).

## Thèmes

La liste des thèmes, dans la barre d'outils, passe du clair au sombre ou au réglage de Windows. Le bouton palette ouvre **Thèmes** : cherchez parmi les thèmes de couleurs VS Code sur [Open VSX](https://open-vsx.org) et installez-en un, ou importez un fichier `.json` ou `.vsix`. Le thème recolore toute la fenêtre et le volet de lecture.

## Confidentialité

Tout reste sur votre PC. WinnowRSS ne se connecte qu'aux flux que vous ajoutez et aux images de leurs articles, à votre Ollama local, et à Open VSX quand vous cherchez des thèmes. Pas de compte, pas de télémétrie.

## Données et désinstallation

Vos données sont dans `%LOCALAPPDATA%\WinnowRSS` : `winnow.db` (flux, articles, réglages), `themes` et `WebView2` (cache du navigateur). Pour désinstaller, supprimez le dossier de l'application et ce dossier.

## Licence

[MIT](LICENSE) © 2026 Gaétan Corneau. Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Contributions : [CONTRIBUTING.md](CONTRIBUTING.md).
