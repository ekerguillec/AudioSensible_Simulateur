using System;

namespace HearingLossSimulator
{
    public class AudioProcessor
    {
        private AudiologicalProfile profile;
        private bool useHRTF;
        private volatile BiQuadFilter[]? filters;

        // Courbes audiométriques (en dB de réduction)
        // Fréquences: 125, 250, 500, 1000, 2000, 4000, 8000 Hz
        private static readonly float[] NormalProfile = { 0, 0, 0, 0, 0, 0, 0 };
        private static readonly float[] MildLossProfile = { 25, 30, 35, 35, 40, 45, 45 };
        private static readonly float[] ModerateLossProfile = { 45, 45, 50, 50, 55, 55, 55 };
        
        private static readonly int[] FrequencyBands = { 125, 250, 500, 1000, 2000, 4000, 8000 };
        private const int SAMPLE_RATE = 44100;

        private volatile float globalGainLinear;

        public AudioProcessor(AudiologicalProfile prof, bool hrtf, float globalGainDb = 0f)
        {
            profile = prof;
            useHRTF = hrtf;

            globalGainLinear = (float)Math.Pow(10.0, globalGainDb / 20.0);

            InitializeFilters();
        }

        private void InitializeFilters()
        {
            float[] lossProfile = profile switch
            {
                AudiologicalProfile.Normal => NormalProfile,
                AudiologicalProfile.MildLoss => MildLossProfile,
                AudiologicalProfile.ModerateLoss => ModerateLossProfile,
                _ => NormalProfile
            };

            filters = new BiQuadFilter[FrequencyBands.Length];

            for (int i = 0; i < FrequencyBands.Length; i++)
            {
                float attenuationDb = -lossProfile[i];
                
                filters[i] = BiQuadFilter.PeakingEQ(
                    SAMPLE_RATE, 
                    FrequencyBands[i], 
                    1.0f, 
                    attenuationDb
                );
            }
        }

        public void SetProfile(AudiologicalProfile newProfile)
        {
            profile = newProfile;
            InitializeFilters();
        }

        public void SetGain(float gainDb)
        {
            globalGainLinear = (float)Math.Pow(10.0, gainDb / 20.0);
        }

        public short[] Process(short[] inputBuffer)
        {
            // Conversion vers float pour traitement
            float[] samples = new float[inputBuffer.Length];
            for (int i = 0; i < inputBuffer.Length; i++)
            {
                samples[i] = inputBuffer[i] / 32768.0f;
            }

            // Application des filtres audiologiques
            float[] processed = ApplyAudiologicalFilters(samples);

            // Conversion retour vers short avec limitation
            short[] outputBuffer = new short[inputBuffer.Length];
            for (int i = 0; i < inputBuffer.Length; i++)
            {
                float withGain = processed[i] * globalGainLinear;

                float limited = Math.Max(-1.0f, Math.Min(1.0f, withGain));
                outputBuffer[i] = (short)(limited * 32767.0f);
            }

            return outputBuffer;
        }

        private float[] ApplyAudiologicalFilters(float[] samples)
        {
            float[] output = new float[samples.Length];
            Array.Copy(samples, output, samples.Length);

            // Application séquentielle de chaque filtre
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    for (int i = 0; i < output.Length; i++)
                    {
                        output[i] = filter.Transform(output[i]);
                    }
                }
            }

            return output;
        }
    }

    // Filtre BiQuad pour le traitement fréquentiel
    public class BiQuadFilter
    {
        private float a0, a1, a2, b1, b2;
        private float z1, z2;

        private BiQuadFilter(float a0, float a1, float a2, float b0, float b1, float b2)
        {
            this.a0 = b0 / a0;
            this.a1 = b1 / a0;
            this.a2 = b2 / a0;
            this.b1 = a1 / a0;
            this.b2 = a2 / a0;
        }

        public static BiQuadFilter PeakingEQ(int sampleRate, float frequency, float q, float gainDb)
        {
            float A = (float)Math.Pow(10, gainDb / 40.0);
            float omega = 2.0f * (float)Math.PI * frequency / sampleRate;
            float sin = (float)Math.Sin(omega);
            float cos = (float)Math.Cos(omega);
            float alpha = sin / (2.0f * q);

            float b0 = 1 + alpha * A;
            float b1 = -2 * cos;
            float b2 = 1 - alpha * A;
            float a0 = 1 + alpha / A;
            float a1 = -2 * cos;
            float a2 = 1 - alpha / A;

            return new BiQuadFilter(a0, a1, a2, b0, b1, b2);
        }

        public float Transform(float input)
        {
            float output = a0 * input + a1 * z1 + a2 * z2 - b1 * z1 - b2 * z2;
            
            z2 = z1;
            z1 = output;
            
            return output;
        }

        public void Reset()
        {
            z1 = z2 = 0;
        }
    }
}