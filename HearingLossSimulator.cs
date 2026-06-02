using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HearingLossSimulator
{
    public class HearingLossSimulator
    {
        private string captureDeviceName;
        private string playbackDeviceName;

        private IAudioDevice? captureDevice;
        private IAudioDevice? playbackDevice;

        private const int SAMPLE_RATE = 44100;
        private const int PERIOD_SIZE = 1024;      // Plus grand = plus stable
        private const int BUFFER_PERIODS = 2;      // 3 périodes = ~70ms latence

        // Buffer circulaire simple - sans queue ni lock
        private short[] ringBuffer;
        private int ringBufferSize;
        private volatile int writePos = 0;
        private volatile int readPos = 0;

        private AudioProcessor? processor;
        private AudiologicalProfile profile;
        private float globalGainDb;

        private float inputLevel;
        private float outputLevel;

        public float InputLevel => inputLevel;
        public float OutputLevel => outputLevel;
        private double latencyMs;
        private int underrunCount = 0;

        private bool isRunning;
        private Thread? audioThread;

        public HearingLossSimulator(string captureDev, string playbackDev, AudiologicalProfile prof, float gainDb = 0f)
        {
            captureDeviceName = captureDev;
            playbackDeviceName = playbackDev;
            profile = prof;
            globalGainDb = gainDb;

            // Ring buffer : 4 secondes pour absorber les variations
            ringBufferSize = SAMPLE_RATE * 4;
            ringBuffer = new short[ringBufferSize];
        }

        public bool Initialize()
        {
            try
            {
                Console.WriteLine($"🎤 Initialisation capture: {captureDeviceName}");
                Console.WriteLine($"🔊 Initialisation playback: {playbackDeviceName}");

                // Capture mono
                captureDevice = AudioDeviceFactory.Create(captureDeviceName, true, SAMPLE_RATE, 1,
                    PERIOD_SIZE, PERIOD_SIZE * BUFFER_PERIODS);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Capture: {SAMPLE_RATE}Hz, période={PERIOD_SIZE}, buffer={PERIOD_SIZE * BUFFER_PERIODS}");
                Console.ResetColor();

                // Playback stéréo
                playbackDevice = AudioDeviceFactory.Create(playbackDeviceName, false, SAMPLE_RATE, 2,
                    PERIOD_SIZE, PERIOD_SIZE * BUFFER_PERIODS);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Playback: {SAMPLE_RATE}Hz, période={PERIOD_SIZE}, buffer={PERIOD_SIZE * BUFFER_PERIODS}");
                Console.ResetColor();

                // DSP
                processor = new AudioProcessor(profile, false, globalGainDb);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Simulation audio initialisée avec succès !\n");
                Console.ResetColor();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur initialisation: {ex.Message}");
                return false;
            }
        }

        public void SetGain(float gainDb)
        {
            globalGainDb = gainDb;
            processor?.SetGain(gainDb);
        }

        public void SetProfile(AudiologicalProfile newProfile)
        {
            profile = newProfile;
            processor?.SetProfile(newProfile);
        }

        public void Start()
        {
            isRunning = true;
            underrunCount = 0;
            writePos = 0;
            readPos = 0;
            
            // Pré-remplir le ring buffer avec du silence (1 période)
            int prefillSamples = PERIOD_SIZE * 1;
            writePos = prefillSamples;

            captureDevice?.Start();

            audioThread = new Thread(AudioLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            audioThread.Start();
        }

        private void AudioLoop()
        {
            short[] captureBuffer = new short[PERIOD_SIZE];
            short[] processedMono = new short[PERIOD_SIZE];
            short[] playbackBuffer = new short[PERIOD_SIZE * 2]; // stéréo

            Stopwatch sw = new Stopwatch();

            while (isRunning)
            {
                try
                {
                    sw.Restart();

                    // ==== 1️⃣ CAPTURE ====
                    int framesRead = captureDevice!.Read(captureBuffer, PERIOD_SIZE);
                    if (framesRead <= 0)
                    {
                        Thread.Yield(); // Pas de Sleep() !
                        continue;
                    }

                    // ==== 2️⃣ DSP - Traitement direct de toute la période ====
                    inputLevel = CalculateRMS(captureBuffer);
                    processedMono = processor!.Process(captureBuffer);
                    outputLevel = CalculateRMS(processedMono);

                    // ==== 3️⃣ Écriture dans le ring buffer ====
                    for (int i = 0; i < framesRead; i++)
                    {
                        ringBuffer[writePos] = processedMono[i];
                        writePos = (writePos + 1) % ringBufferSize;
                        
                        // Protection contre overflow (buffer plein)
                        if (writePos == readPos)
                        {
                            readPos = (readPos + 1) % ringBufferSize;
                        }
                    }

                    // ==== 4️⃣ PLAYBACK ====
                    int available = GetAvailableSamples();
                    
                    // Attendre qu'on ait au moins 1 période disponible
                    if (available < PERIOD_SIZE * 2)
                    {
                        Thread.Yield();
                        continue;
                    }

                    // Lire depuis le ring buffer
                    int framesToPlay = Math.Min(PERIOD_SIZE, available);
                    for (int i = 0; i < framesToPlay; i++)
                    {
                        short sample = ringBuffer[readPos];
                        readPos = (readPos + 1) % ringBufferSize;
                        
                        playbackBuffer[i * 2] = sample;      // Left
                        playbackBuffer[i * 2 + 1] = sample;  // Right
                    }

                    // Écriture ALSA non-bloquante
                    int written = playbackDevice!.Write(playbackBuffer, framesToPlay);
                    
                    if (written <= 0)
                    {
                        underrunCount++;
                        // Ne pas sleep, juste continuer
                    }

                    // ==== 5️⃣ Latence ====
                    latencyMs = (available / (double)SAMPLE_RATE) * 1000.0;

                    // Micro-pause uniquement si on est trop en avance
                    double elapsed = sw.Elapsed.TotalMilliseconds;
                    double targetTime = (PERIOD_SIZE / (double)SAMPLE_RATE) * 1000.0;
                    if (elapsed < targetTime * 0.5)
                    {
                        Thread.Yield(); // Yield au lieu de Sleep
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erreur audio: {ex.Message}");
                    Thread.Sleep(5); // Seul Sleep en cas d'erreur
                }
            }
        }

        private int GetAvailableSamples()
        {
            int w = writePos;
            int r = readPos;
            
            if (w >= r)
                return w - r;
            else
                return ringBufferSize - r + w;
        }

        private float CalculateRMS(short[] buffer)
        {
            long sum = 0;
            foreach (short s in buffer) sum += s * s;
            double mean = sum / (double)buffer.Length;
            double rms = Math.Sqrt(mean);
            return (float)(rms / 32768.0);
        }

        public void UpdateDisplay()
        {
            Console.SetCursorPosition(0, 6);
            string profileName = profile == AudiologicalProfile.Normal ? "Audition Normale" :
                                profile == AudiologicalProfile.MildLoss ? "Surdité Légère" : "Surdité Moyenne";
            Console.WriteLine($"Profil: {profileName}");
            string gainSign = globalGainDb >= 0 ? "+" : "";
            Console.WriteLine($"Gain global: {gainSign}{globalGainDb:F0} dB   ");
            Console.WriteLine($"Configuration: Période={PERIOD_SIZE}, Buffer={PERIOD_SIZE * BUFFER_PERIODS} samples  ");

            Console.Write("🔥 Entrée:  "); DrawVUMeter(inputLevel, ConsoleColor.Green);
            Console.WriteLine($" {(inputLevel * 100):F1}%  ");

            Console.Write("🔊 Sortie:  "); DrawVUMeter(outputLevel, ConsoleColor.Cyan);
            Console.WriteLine($" {(outputLevel * 100):F1}%  ");

            Console.WriteLine();
            Console.ForegroundColor = latencyMs < 50 ? ConsoleColor.Green : 
                                      latencyMs < 100 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.WriteLine($"⏱️  Latence: {latencyMs:F2} ms         ");
            Console.ResetColor();

            int available = GetAvailableSamples();
            Console.WriteLine($"📊 Ring buffer: {available}/{ringBufferSize} samples ({(available * 100.0 / ringBufferSize):F1}%)    ");
            
            Console.ForegroundColor = underrunCount == 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"⚠️  Underruns: {underrunCount}       ");
            Console.ResetColor();
        }

        private void DrawVUMeter(float level, ConsoleColor color)
        {
            const int width = 40;
            int filled = (int)(level * width);
            Console.ForegroundColor = color;
            Console.Write("[");
            for (int i = 0; i < width; i++)
                Console.Write(i < filled ? "█" : "░");
            Console.Write("]");
            Console.ResetColor();
        }

        public void Stop()
        {
            isRunning = false;
            audioThread?.Join(1000);
            captureDevice?.Dispose();
            playbackDevice?.Dispose();
        }
    }
}