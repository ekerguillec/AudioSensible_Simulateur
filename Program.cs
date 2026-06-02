using System;
using System.Threading;

namespace HearingLossSimulator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("    SIMULATEUR DE SURDITÉ (ALSA NATIVE)");
            Console.WriteLine("═══════════════════════════════════════════════════\n");

            try
            {
                bool quit = false;

                while (!quit)
                {
                    // 1. Sélection des périphériques audio
                    string? captureDevice = SelectDevice(capture: true);
                    if (captureDevice == null) break;
                    string? playbackDevice = SelectDevice(capture: false);
                    if (playbackDevice == null) break;

                    // 2. Sélection profil audiologique
                    AudiologicalProfile? profile = SelectAudiologicalProfile();
                    if (profile == null) break;

                    // 3. Démarrage simulation (inclut la sélection du gain en direct)
                    Console.Clear();
                    RunSimulation(captureDevice, playbackDevice, profile.Value, out bool reconfigure);
                    quit = !reconfigure;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Erreur: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
                Console.ResetColor();
                Console.WriteLine("\nAppuyez sur une touche pour quitter...");
                Console.ReadKey();
            }
        }

        static string? SelectDevice(bool capture)
        {
            string direction = capture ? "ENTRÉE (Microphone)" : "SORTIE (Haut-parleurs)";
            Console.WriteLine($"🎤 SÉLECTION DU PÉRIPHÉRIQUE {direction}");
            Console.WriteLine("─────────────────────────────────────────────────\n");

            var devices = AudioDeviceFactory.EnumerateDevices(capture);

            if (devices.Count == 0)
            {
                Console.WriteLine("Aucun périphérique trouvé. Utilisation de \"default\".\n");
                return "default";
            }

            for (int i = 0; i < devices.Count; i++)
            {
                string firstLine = devices[i].Description.Split('\n')[0];
                Console.WriteLine($"  [{i}] {devices[i].Name}");
                if (!string.IsNullOrEmpty(firstLine))
                    Console.WriteLine($"      {firstLine}");
                Console.WriteLine();
            }

            Console.Write($"➤ Sélectionnez (0-{devices.Count - 1}) ou [Echap/Q] Quitter : ");
            string? input = ReadLineOrEscape();
            if (input == null) return null;

            if (int.TryParse(input, out int choice) && choice >= 0 && choice < devices.Count)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Périphérique: {devices[choice].Name}\n");
                Console.ResetColor();
                return devices[choice].Name;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ Choix invalide. Utilisation de: {devices[0].Name}\n");
            Console.ResetColor();
            return devices[0].Name;
        }

        static AudiologicalProfile? SelectAudiologicalProfile()
        {
            Console.WriteLine("🔊 SÉLECTION DU PROFIL AUDIOLOGIQUE");
            Console.WriteLine("─────────────────────────────────────────────────\n");
            Console.WriteLine("  [0] Audition Normale (aucun filtre)");
            Console.WriteLine("      • Écoute sans simulation de perte\n");

            Console.WriteLine("  [1] Surdité Légère (20-40 dB HL)");
            Console.WriteLine("      • Difficulté avec sons faibles");
            Console.WriteLine("      • Conversations calmes difficiles\n");

            Console.WriteLine("  [2] Surdité Moyenne (40-70 dB HL)");
            Console.WriteLine("      • Nécessite élévation de voix");
            Console.WriteLine("      • Difficulté conversations normales\n");

            Console.Write("➤ Sélectionnez (0-2) ou [Echap/Q] Quitter : ");
            var choice = ReadLineOrEscape();
            if (choice == null) return null;

            AudiologicalProfile profile;

            if (choice == "0")
                profile = AudiologicalProfile.Normal;
            else if (choice == "2")
                profile = AudiologicalProfile.ModerateLoss;
            else
                profile = AudiologicalProfile.MildLoss;

            Console.ForegroundColor = ConsoleColor.Green;
            string profileName = profile == AudiologicalProfile.Normal ? "Audition Normale" :
                                profile == AudiologicalProfile.MildLoss ? "Surdité Légère" : "Surdité Moyenne";
            Console.WriteLine($"✓ Profil: {profileName}\n");
            Console.ResetColor();

            return profile;
        }

        static bool? SelectGlobalGainLive(HearingLossSimulator sim)
        {
            Console.Clear();
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("    ÉTALONNAGE DU VOLUME");
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("  ℹ  Réglez le volume pour que le son vous paraisse");
            Console.WriteLine("     aussi fort que dans la vraie vie, sans filtres.");
            Console.WriteLine();
            Console.WriteLine("  [↑/↓] ±1 dB  [Entrée] Valider  [R] Reconfigurer  [Q] Quitter");
            Console.WriteLine();

            float gain = 0f;
            const float MIN = -20f;
            const float MAX = 20f;
            const int BAR_WIDTH = 40;
            const int DYNAMIC_ROW = 9;

            while (true)
            {
                Console.SetCursorPosition(0, DYNAMIC_ROW);

                int pos = (int)((gain - MIN) / (MAX - MIN) * BAR_WIDTH);
                pos = Math.Clamp(pos, 0, BAR_WIDTH);

                Console.Write("  -20 ");
                for (int i = 0; i <= BAR_WIDTH; i++)
                    Console.Write(i == pos ? "●" : "─");
                Console.WriteLine(" +20  ");

                string sign = gain >= 0 ? "+" : "";
                Console.WriteLine($"  Gain: {sign}{gain:F0} dB       ");
                Console.WriteLine();

                DrawGainVU("🔥 Entrée:", sim.InputLevel, ConsoleColor.Green);
                DrawGainVU("🔊 Sortie:", sim.OutputLevel, ConsoleColor.Cyan);

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.UpArrow && gain < MAX) { gain += 1f; sim.SetGain(gain); }
                    else if (key == ConsoleKey.DownArrow && gain > MIN) { gain -= 1f; sim.SetGain(gain); }
                    else if (key == ConsoleKey.Enter) return null;
                    else if (key == ConsoleKey.R) return true;
                    else if (key == ConsoleKey.Q || key == ConsoleKey.Escape) return false;
                }

                Thread.Sleep(50);
            }
        }

        static string? ReadLineOrEscape()
        {
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var info = Console.ReadKey(true);
                switch (info.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        return sb.ToString();
                    case ConsoleKey.Escape:
                    case ConsoleKey.Q:
                        Console.WriteLine();
                        return null;
                    case ConsoleKey.Backspace:
                        if (sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
                        break;
                    default:
                        if (info.KeyChar != '\0' && !char.IsControl(info.KeyChar))
                        {
                            sb.Append(info.KeyChar);
                            Console.Write(info.KeyChar);
                        }
                        break;
                }
            }
        }

        static void DrawGainVU(string label, float level, ConsoleColor color)
        {
            const int width = 30;
            int filled = Math.Clamp((int)(level * width), 0, width);
            Console.Write($"  {label} ");
            Console.ForegroundColor = color;
            Console.Write("[");
            for (int i = 0; i < width; i++)
                Console.Write(i < filled ? "█" : "░");
            Console.Write("]");
            Console.ResetColor();
            Console.WriteLine($" {level * 100:F1}%  ");
        }

        static void RunSimulation(string captureDevice, string playbackDevice,
                                  AudiologicalProfile profile, out bool reconfigure)
        {
            var simulator = new HearingLossSimulator(captureDevice, playbackDevice, AudiologicalProfile.Normal);

            if (!simulator.Initialize())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Échec de l'initialisation du simulateur.");
                Console.ResetColor();
                Console.ReadKey();
                reconfigure = false;
                return;
            }

            simulator.Start();
            bool? gainResult = SelectGlobalGainLive(simulator);
            if (gainResult.HasValue)
            {
                simulator.Stop();
                reconfigure = gainResult.Value;
                return;
            }
            simulator.SetProfile(profile);

            // Header fixe 6 lignes : SetCursorPosition(0, 6) dans UpdateDisplay() doit tomber ici
            Console.Clear();
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine("    SIMULATION EN COURS");
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("  [R] Reconfigurer   [Q/Echap] Quitter");
            Console.WriteLine();

            reconfigure = false;
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.R) { reconfigure = true; break; }
                    if (key == ConsoleKey.Q || key == ConsoleKey.Escape) { reconfigure = false; break; }
                }

                simulator.UpdateDisplay();
                Thread.Sleep(100);
            }

            simulator.Stop();
        }
    }

    public enum AudiologicalProfile
    {
        Normal,        // Aucun filtre
        MildLoss,      // 20-40 dB HL
        ModerateLoss   // 40-70 dB HL
    }
}