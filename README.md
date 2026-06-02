# AudioSensible

Simulateur de surdité en temps réel pour Linux. Capture l'audio depuis un microphone, applique des filtres audiologiques simulant différents niveaux de perte auditive (légère, modérée), et restitue le son transformé en temps réel via ALSA.

## Téléchargement

Téléchargez la dernière version depuis les [Releases GitHub](https://github.com/louislatuvillieres/AudioSensible/releases).

## Installation et lancement

### AppImage (recommandé)

```bash
chmod +x AudioSensible-*.AppImage
./AudioSensible-*.AppImage
```

Ou **double-cliquez** sur le fichier `.AppImage` dans votre gestionnaire de fichiers — un terminal s'ouvre automatiquement.

### Tarball (alternative si FUSE indisponible)

```bash
tar -xzf AudioSensible-*-linux-x64.tar.gz
./HearingLossSimulator
```

## Dépendances système requises

Les bibliothèques audio natives doivent être présentes sur le système hôte.
Le runtime .NET est **inclus** dans l'AppImage — aucune installation de dotnet n'est nécessaire.

| Distribution       | Commande                                          |
|--------------------|---------------------------------------------------|
| Ubuntu / Debian    | `sudo apt install libasound2 libopenal1`          |
| Fedora / RHEL      | `sudo dnf install alsa-lib openal-soft`           |
| Arch Linux         | `sudo pacman -S alsa-lib openal`                  |
| openSUSE           | `sudo zypper install alsa openal-soft`            |

## Compatibilité

| Critère        | Valeur                                                    |
|----------------|-----------------------------------------------------------|
| Architecture   | x86_64                                                    |
| glibc minimum  | 2.35 (Ubuntu 22.04+, Fedora 36+, Debian 12+, Arch Linux) |
| .NET runtime   | Inclus dans le binaire                                    |
| Audio          | ALSA requis sur le système hôte                           |

## Profils audiologiques disponibles

| Profil           | Atténuation par bande (125 Hz → 8 kHz)       |
|------------------|----------------------------------------------|
| Audition normale | 0 dB sur toutes les fréquences               |
| Perte légère     | 25 → 45 dB (progressif vers les aigus)       |
| Perte modérée    | 45 → 55 dB (atténuation forte et uniforme)   |

## Compilation depuis les sources

Prérequis : [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) et `libasound2-dev`.

```bash
dotnet build -c Release
dotnet run
```
