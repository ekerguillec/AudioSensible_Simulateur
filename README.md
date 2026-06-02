# AudioSensible

Simulateur de surdité en temps réel pour Linux, Windows et macOS. Capture l'audio depuis un microphone, applique des filtres audiologiques simulant différents niveaux de perte auditive (légère, modérée), et restitue le son transformé en temps réel.

## Téléchargement

Téléchargez la dernière version depuis les [Releases GitHub](https://github.com/ekerguillec/AudioSensible_Simulateur/releases).

## Installation et lancement

### 🐧 Linux

**AppImage (recommandé)**
```bash
chmod +x AudioSensible-*.AppImage
./AudioSensible-*.AppImage
```
Ou **double-cliquez** sur le fichier `.AppImage` dans votre gestionnaire de fichiers — un terminal s'ouvre automatiquement.

**Tarball (alternative si FUSE indisponible)**
```bash
tar -xzf AudioSensible-*-linux-x64.tar.gz
./HearingLossSimulator
```

### 🍎 macOS

Télécharger `osx-arm64` (Apple Silicon M1/M2/M3) ou `osx-x64` (Intel) :
```bash
tar -xzf AudioSensible-*-osx-arm64.tar.gz
./HearingLossSimulator
```

> Si macOS bloque le lancement (Gatekeeper) :
> ```bash
> xattr -d com.apple.quarantine ./HearingLossSimulator
> ```

### 🪟 Windows

Extraire le `.zip` et lancer `HearingLossSimulator.exe` dans un terminal (PowerShell ou Invite de commandes) :
```
HearingLossSimulator.exe
```

## Dépendances système requises

Le runtime .NET est **inclus** dans tous les binaires — aucune installation de dotnet n'est nécessaire.

**Linux uniquement** — les bibliothèques audio natives doivent être présentes :

| Distribution       | Commande                                          |
|--------------------|---------------------------------------------------|
| Ubuntu / Debian    | `sudo apt install libasound2`                     |
| Fedora / RHEL      | `sudo dnf install alsa-lib`                       |
| Arch Linux         | `sudo pacman -S alsa-lib`                         |
| openSUSE           | `sudo zypper install alsa`                        |

**Windows et macOS** : aucune dépendance supplémentaire requise.

## Compatibilité

| Critère        | Linux                                                     | Windows       | macOS                          |
|----------------|-----------------------------------------------------------|---------------|--------------------------------|
| Architecture   | x86_64                                                    | x86_64        | x86_64 (Intel) / arm64 (M1+)  |
| Version min.   | glibc ≥ 2.35 (Ubuntu 22.04+)                             | Windows 10+   | macOS 12+                      |
| Audio          | ALSA                                                      | WinMM (natif) | OpenAL (natif)                 |
| .NET runtime   | Inclus                                                    | Inclus        | Inclus                         |

## Profils audiologiques disponibles

| Profil           | Atténuation par bande (125 Hz → 8 kHz)       |
|------------------|----------------------------------------------|
| Audition normale | 0 dB sur toutes les fréquences               |
| Perte légère     | 25 → 45 dB (progressif vers les aigus)       |
| Perte modérée    | 45 → 55 dB (atténuation forte et uniforme)   |

## Compilation depuis les sources

Prérequis : [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet build -c Release
dotnet run
```
