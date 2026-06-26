using NAudio.Wave;
using NESEmulator.Core.CPU;
using NESEmulator.Core.Memory;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NESEmulator.Core.APU
{
    public class APU2A03
    {
        private readonly byte[] registers = new byte[0x20];
        private const int AudioBatchSamples = 512;
        private readonly PulseChannel pulse1;
        private readonly PulseChannel pulse2;
        private readonly TriangleChannel triangle;
        private readonly NoiseChannel noise;
        private readonly DMCChannel dmc;

        private WaveOutEvent? waveOut;
        private BufferedWaveProvider? waveProvider;
        private readonly int sampleRate = 44100;
        private readonly int bufferSize = 4410;
        private readonly float[] audioBuffer;
        private readonly byte[] pcmBuffer;
        private int bufferIndex;

        private const float DefaultMasterVolume = 2.0f;
        private const double CpuClockRate = 1789773.0;
        private const double FrameCounterRate = CpuClockRate / 240.0;

        private MemoryBus? memoryBus;
        private CPU6502? cpu;
        private float masterVolume = DefaultMasterVolume;
        private ulong cycle;
        private int framePeriod;
        private int frameValue;
        private bool frameIrqEnabled;
        private bool frameIrqPending;
        private double sampleAccumulator;

        private float hp90PrevInput;
        private float hp90PrevOutput;
        private float hp440PrevInput;
        private float hp440PrevOutput;
        private float lp14000PrevOutput;
        private float deClickPrevOutput;

        private const float DeClickBaseSlewStep = 0.10f;
        private const float DeClickDynamicSlewFactor = 0.20f;

        private long lastClockTime;
        private double cycleAccumulator;
        private long totalCycles;

        #region debug-point infra
        private static readonly HttpClient DebugHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
        private static readonly string DebugEnvPath = Path.Combine(".dbg", "audio-interrupt-fps.env");
        private static readonly string? DebugServerUrl = TryReadDebugEnv("DEBUG_SERVER_URL");
        private static readonly string DebugSessionId = TryReadDebugEnv("DEBUG_SESSION_ID") ?? "audio-interrupt-fps";
        private static int debugOutputLogCount;
        private static int debugFlushLogCount;
        private static int debugLowBufferLogCount;

        private static string? TryReadDebugEnv(string key)
        {
            try
            {
                if (!File.Exists(DebugEnvPath))
                {
                    return null;
                }

                foreach (string line in File.ReadAllLines(DebugEnvPath))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line[(key.Length + 1)..].Trim();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void DebugReport(string runId, string hypothesisId, string location, string msg, object data)
        {
            if (string.IsNullOrWhiteSpace(DebugServerUrl))
            {
                return;
            }

            try
            {
                string payload = JsonSerializer.Serialize(new
                {
                    sessionId = DebugSessionId,
                    runId,
                    hypothesisId,
                    location,
                    msg = "[DEBUG] " + msg,
                    data,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                _ = DebugHttpClient.PostAsync(DebugServerUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch
            {
            }
        }
        #endregion

        public APU2A03()
        {
            pulse1 = new PulseChannel(1);
            pulse2 = new PulseChannel(2);
            triangle = new TriangleChannel();
            noise = new NoiseChannel();
            dmc = new DMCChannel();
            audioBuffer = new float[AudioBatchSamples];
            pcmBuffer = new byte[AudioBatchSamples * 2];

            InitializeAudio();
            Reset();
        }

        public void ConnectConsole(MemoryBus memoryBus, CPU6502 cpu)
        {
            this.memoryBus = memoryBus;
            this.cpu = cpu;
            dmc.Connect(memoryBus, cpu);
        }

        private void InitializeAudio()
        {
            try
            {
                waveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
                {
                    BufferLength = bufferSize * 16,
                    DiscardOnBufferOverflow = true
                };

                waveOut = new WaveOutEvent
                {
                    Volume = Math.Min(masterVolume, 1.0f)
                };
                waveOut.Init(waveProvider);
                waveOut.Play();
            }
            catch
            {
                waveOut = null;
                waveProvider = null;
            }
        }

        public void Reset()
        {
            Array.Clear(registers, 0, registers.Length);
            pulse1.Reset();
            pulse2.Reset();
            triangle.Reset();
            noise.Reset();
            dmc.Reset();
            dmc.Connect(memoryBus, cpu);

            cycle = 0;
            framePeriod = 4;
            frameValue = 0;
            frameIrqEnabled = false;
            frameIrqPending = false;
            sampleAccumulator = 0;
            bufferIndex = 0;
            hp90PrevInput = 0.0f;
            hp90PrevOutput = 0.0f;
            hp440PrevInput = 0.0f;
            hp440PrevOutput = 0.0f;
            lp14000PrevOutput = 0.0f;
            deClickPrevOutput = 0.0f;
            Array.Clear(audioBuffer, 0, audioBuffer.Length);
        }

        public void Clock()
        {
            ulong previousCycle = cycle;
            cycle++;
            totalCycles++;

            StepTimer();

            int previousFrameStep = (int)(previousCycle / FrameCounterRate);
            int currentFrameStep = (int)(cycle / FrameCounterRate);
            if (previousFrameStep != currentFrameStep)
            {
                StepFrameCounter();
            }

            sampleAccumulator += sampleRate;
            if (sampleAccumulator >= CpuClockRate)
            {
                sampleAccumulator -= CpuClockRate;
                GenerateSample();
            }

            cycleAccumulator = sampleAccumulator;
        }

        public void ClockWithPrecisionTiming()
        {
            lastClockTime = Stopwatch.GetTimestamp();
            Clock();
        }

        public APUClockStats GetClockStats()
        {
            return new APUClockStats
            {
                TotalCycles = totalCycles,
                FrameClock = frameValue,
                SampleClock = (int)cycle,
                BufferIndex = bufferIndex,
                LastClockTime = lastClockTime,
                CycleAccumulator = cycleAccumulator,
                Pulse1Enabled = pulse1.IsEnabled(),
                Pulse2Enabled = pulse2.IsEnabled(),
                TriangleEnabled = triangle.IsEnabled(),
                NoiseEnabled = noise.IsEnabled(),
                DMCEnabled = dmc.IsEnabled()
            };
        }

        private void StepTimer()
        {
            if ((cycle & 1UL) == 0)
            {
                pulse1.StepTimer();
                pulse2.StepTimer();
                noise.StepTimer();
                dmc.StepTimer();
            }

            triangle.StepTimer();
        }

        private void StepFrameCounter()
        {
            switch (framePeriod)
            {
                case 4:
                    frameValue = (frameValue + 1) % 4;
                    switch (frameValue)
                    {
                        case 0:
                        case 2:
                            StepEnvelope();
                            break;
                        case 1:
                            StepEnvelope();
                            StepSweep();
                            StepLength();
                            break;
                        case 3:
                            StepEnvelope();
                            StepSweep();
                            StepLength();
                            FireFrameIrq();
                            break;
                    }
                    break;
                case 5:
                    frameValue = (frameValue + 1) % 5;
                    switch (frameValue)
                    {
                        case 0:
                        case 2:
                            StepEnvelope();
                            break;
                        case 1:
                        case 3:
                            StepEnvelope();
                            StepSweep();
                            StepLength();
                            break;
                    }
                    break;
            }
        }

        private void StepEnvelope()
        {
            pulse1.StepEnvelope();
            pulse2.StepEnvelope();
            triangle.StepCounter();
            noise.StepEnvelope();
        }

        private void StepSweep()
        {
            pulse1.StepSweep();
            pulse2.StepSweep();
        }

        private void StepLength()
        {
            pulse1.StepLength();
            pulse2.StepLength();
            triangle.StepLength();
            noise.StepLength();
        }

        private void FireFrameIrq()
        {
            if (!frameIrqEnabled)
            {
                return;
            }

            frameIrqPending = true;
            cpu?.TriggerIRQ();
        }

        private void GenerateSample()
        {
            int pulseIndex = pulse1.GetOutput() + pulse2.GetOutput();
            int tndIndex = (3 * triangle.GetOutput()) + (2 * noise.GetOutput()) + dmc.GetOutput();

            float sample = APUConstants.PulseTable[pulseIndex] + APUConstants.TndTable[tndIndex];
            sample *= Math.Max(masterVolume, 1.0f);
            sample = ApplyOutputFilters(sample);
            sample = ApplyDeClickSmoothing(sample);
            sample = Math.Clamp(sample, -1.0f, 1.0f);

            audioBuffer[bufferIndex++] = sample;
            if (bufferIndex >= audioBuffer.Length)
            {
                OutputAudio(bufferIndex);
                bufferIndex = 0;
            }
        }

        public void FlushAudio()
        {
            if (bufferIndex == 0)
            {
                return;
            }

            #region debug-point B:apu-flush-audio
            if (debugFlushLogCount < 40)
            {
                debugFlushLogCount++;
                DebugReport("post-fix", "B", "APU2A03.FlushAudio", "flushing partial audio batch", new
                {
                    bufferIndex,
                    bufferedBytes = waveProvider?.BufferedBytes ?? -1,
                    totalCycles,
                    sampleClock = cycle
                });
            }
            #endregion

            OutputAudio(bufferIndex);
            bufferIndex = 0;
        }

        private void OutputAudio(int sampleCount)
        {
            if (waveProvider == null || sampleCount <= 0)
            {
                return;
            }

            int bufferedBytesBefore = waveProvider.BufferedBytes;
            long outputStartTicks = Stopwatch.GetTimestamp();

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(audioBuffer[i] * short.MaxValue);
                pcmBuffer[i * 2] = (byte)(sample & 0xFF);
                pcmBuffer[i * 2 + 1] = (byte)(sample >> 8);
            }

            waveProvider.AddSamples(pcmBuffer, 0, sampleCount * 2);

            #region debug-point A:apu-output-audio
            if (debugOutputLogCount < 80)
            {
                debugOutputLogCount++;
                DebugReport("post-fix", "A", "APU2A03.OutputAudio", "queued pcm audio", new
                {
                    sampleCount,
                    bufferedBytesBefore,
                    bufferedBytesAfter = waveProvider.BufferedBytes,
                    outputMs = (Stopwatch.GetTimestamp() - outputStartTicks) * 1000.0 / Stopwatch.Frequency,
                    totalCycles,
                    sampleClock = cycle
                });
            }

            if (waveProvider.BufferedBytes < 2048 && debugLowBufferLogCount < 80)
            {
                debugLowBufferLogCount++;
                DebugReport("post-fix", "B", "APU2A03.OutputAudio", "audio buffer running low", new
                {
                    sampleCount,
                    bufferedBytesBefore,
                    bufferedBytesAfter = waveProvider.BufferedBytes,
                    outputMs = (Stopwatch.GetTimestamp() - outputStartTicks) * 1000.0 / Stopwatch.Frequency,
                    totalCycles,
                    sampleClock = cycle
                });
            }
            #endregion
        }

        private float ApplyOutputFilters(float input)
        {
            float highPass90 = 0.9875f * (hp90PrevOutput + input - hp90PrevInput);
            hp90PrevInput = input;
            hp90PrevOutput = highPass90;

            float highPass440 = 0.9410f * (hp440PrevOutput + highPass90 - hp440PrevInput);
            hp440PrevInput = highPass90;
            hp440PrevOutput = highPass440;

            float lowPass14000 = lp14000PrevOutput + (0.666f * (highPass440 - lp14000PrevOutput));
            lp14000PrevOutput = lowPass14000;
            return lowPass14000;
        }

        private float ApplyDeClickSmoothing(float input)
        {
            float maxStep = DeClickBaseSlewStep + (Math.Abs(input) * DeClickDynamicSlewFactor);
            float minOutput = deClickPrevOutput - maxStep;
            float maxOutput = deClickPrevOutput + maxStep;
            float output = Math.Clamp(input, minOutput, maxOutput);
            deClickPrevOutput = output;
            return output;
        }

        public byte ReadRegister(ushort address)
        {
            if (address == 0x4015)
            {
                byte status = 0;
                if (pulse1.LengthValue > 0) status |= 0x01;
                if (pulse2.LengthValue > 0) status |= 0x02;
                if (triangle.LengthValue > 0) status |= 0x04;
                if (noise.LengthValue > 0) status |= 0x08;
                if (dmc.CurrentLength > 0) status |= 0x10;
                if (frameIrqPending) status |= 0x40;
                if (dmc.IrqPending) status |= 0x80;
                frameIrqPending = false;
                return status;
            }

            return registers[address & 0x1F];
        }

        public void WriteRegister(ushort address, byte value)
        {
            registers[address & 0x1F] = value;

            switch (address)
            {
                case 0x4000:
                    pulse1.WriteControl(value);
                    break;
                case 0x4001:
                    pulse1.WriteSweep(value);
                    break;
                case 0x4002:
                    pulse1.WriteTimerLow(value);
                    break;
                case 0x4003:
                    pulse1.WriteTimerHigh(value);
                    break;
                case 0x4004:
                    pulse2.WriteControl(value);
                    break;
                case 0x4005:
                    pulse2.WriteSweep(value);
                    break;
                case 0x4006:
                    pulse2.WriteTimerLow(value);
                    break;
                case 0x4007:
                    pulse2.WriteTimerHigh(value);
                    break;
                case 0x4008:
                    triangle.WriteControl(value);
                    break;
                case 0x400A:
                    triangle.WriteTimerLow(value);
                    break;
                case 0x400B:
                    triangle.WriteTimerHigh(value);
                    break;
                case 0x400C:
                    noise.WriteControl(value);
                    break;
                case 0x400E:
                    noise.WritePeriod(value);
                    break;
                case 0x400F:
                    noise.WriteLength(value);
                    break;
                case 0x4010:
                    dmc.WriteControl(value);
                    break;
                case 0x4011:
                    dmc.WriteValue(value);
                    break;
                case 0x4012:
                    dmc.WriteAddress(value);
                    break;
                case 0x4013:
                    dmc.WriteLength(value);
                    break;
                case 0x4015:
                    WriteStatus(value);
                    break;
                case 0x4017:
                    WriteFrameCounter(value);
                    break;
            }
        }

        private void WriteStatus(byte value)
        {
            pulse1.SetEnabled((value & 0x01) != 0);
            pulse2.SetEnabled((value & 0x02) != 0);
            triangle.SetEnabled((value & 0x04) != 0);
            noise.SetEnabled((value & 0x08) != 0);
            dmc.SetEnabled((value & 0x10) != 0);
            dmc.ClearInterrupt();
        }

        private void WriteFrameCounter(byte value)
        {
            framePeriod = 4 + ((value >> 7) & 0x01);
            frameIrqEnabled = (value & 0x40) == 0;
            if (!frameIrqEnabled)
            {
                frameIrqPending = false;
            }

            if (framePeriod == 5)
            {
                StepEnvelope();
                StepSweep();
                StepLength();
            }
        }

        public void Dispose()
        {
            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;
            waveProvider = null;
        }

        public void SetMasterVolume(float volume)
        {
            masterVolume = Math.Clamp(volume, 0.0f, 4.0f);
            if (waveOut != null)
            {
                waveOut.Volume = Math.Min(masterVolume, 1.0f);
            }
        }

        public float GetMasterVolume()
        {
            return masterVolume;
        }
    }

    internal static class APUConstants
    {
        internal static readonly byte[] LengthTable =
        {
            10, 254, 20, 2, 40, 4, 80, 6,
            160, 8, 60, 10, 14, 12, 26, 14,
            12, 16, 24, 18, 48, 20, 96, 22,
            192, 24, 72, 26, 16, 28, 32, 30
        };

        internal static readonly byte[][] DutyTable =
        {
            new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 },
            new byte[] { 0, 1, 1, 0, 0, 0, 0, 0 },
            new byte[] { 0, 1, 1, 1, 1, 0, 0, 0 },
            new byte[] { 1, 0, 0, 1, 1, 1, 1, 1 }
        };

        internal static readonly byte[] TriangleTable =
        {
            15, 14, 13, 12, 11, 10, 9, 8,
             7,  6,  5,  4,  3,  2, 1, 0,
             0,  1,  2,  3,  4,  5, 6, 7,
             8,  9, 10, 11, 12, 13, 14, 15
        };

        internal static readonly ushort[] NoisePeriodTable =
        {
            4, 8, 16, 32, 64, 96, 128, 160,
            202, 254, 380, 508, 762, 1016, 2034, 4068
        };

        internal static readonly ushort[] DmcRateTable =
        {
            214, 190, 170, 160, 143, 127, 113, 107,
             95,  80,  71,  64,  53,  42,  36,  27
        };

        internal static readonly float[] PulseTable;
        internal static readonly float[] TndTable;

        static APUConstants()
        {
            PulseTable = new float[31];
            for (int i = 0; i < PulseTable.Length; i++)
            {
                PulseTable[i] = i == 0 ? 0.0f : (float)(95.52 / ((8128.0 / i) + 100.0));
            }

            TndTable = new float[203];
            for (int i = 0; i < TndTable.Length; i++)
            {
                TndTable[i] = i == 0 ? 0.0f : (float)(163.67 / ((24329.0 / i) + 100.0));
            }
        }
    }

    internal sealed class PulseChannel
    {
        private readonly int channelNumber;
        private bool enabled;
        private bool lengthEnabled;
        private byte lengthValue;
        private ushort timerPeriod;
        private ushort timerValue;
        private byte dutyMode;
        private byte dutyValue;
        private bool sweepReload;
        private bool sweepEnabled;
        private bool sweepNegate;
        private byte sweepShift;
        private byte sweepPeriod;
        private byte sweepValue;
        private bool envelopeEnabled;
        private bool envelopeLoop;
        private bool envelopeStart;
        private byte envelopePeriod;
        private byte envelopeValue;
        private byte envelopeVolume;
        private byte constantVolume;

        public PulseChannel(int channelNumber)
        {
            this.channelNumber = channelNumber;
        }

        public byte LengthValue => lengthValue;

        public void Reset()
        {
            enabled = false;
            lengthEnabled = false;
            lengthValue = 0;
            timerPeriod = 0;
            timerValue = 0;
            dutyMode = 0;
            dutyValue = 0;
            sweepReload = false;
            sweepEnabled = false;
            sweepNegate = false;
            sweepShift = 0;
            sweepPeriod = 0;
            sweepValue = 0;
            envelopeEnabled = false;
            envelopeLoop = false;
            envelopeStart = false;
            envelopePeriod = 0;
            envelopeValue = 0;
            envelopeVolume = 0;
            constantVolume = 0;
        }

        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                lengthValue = 0;
            }
        }

        public bool IsEnabled()
        {
            return enabled;
        }

        public void WriteControl(byte value)
        {
            dutyMode = (byte)((value >> 6) & 0x03);
            lengthEnabled = (value & 0x20) == 0;
            envelopeLoop = (value & 0x20) != 0;
            envelopeEnabled = (value & 0x10) == 0;
            envelopePeriod = (byte)(value & 0x0F);
            constantVolume = (byte)(value & 0x0F);
            envelopeStart = true;
        }

        public void WriteSweep(byte value)
        {
            sweepEnabled = (value & 0x80) != 0;
            sweepPeriod = (byte)(((value >> 4) & 0x07) + 1);
            sweepNegate = (value & 0x08) != 0;
            sweepShift = (byte)(value & 0x07);
            sweepReload = true;
        }

        public void WriteTimerLow(byte value)
        {
            timerPeriod = (ushort)((timerPeriod & 0xFF00) | value);
        }

        public void WriteTimerHigh(byte value)
        {
            timerPeriod = (ushort)((timerPeriod & 0x00FF) | ((value & 0x07) << 8));
            timerValue = timerPeriod;
            dutyValue = 0;
            envelopeStart = true;
            if (enabled)
            {
                lengthValue = APUConstants.LengthTable[(value >> 3) & 0x1F];
            }
        }

        public void StepTimer()
        {
            if (timerValue == 0)
            {
                timerValue = timerPeriod;
                dutyValue = (byte)((dutyValue + 1) & 0x07);
            }
            else
            {
                timerValue--;
            }
        }

        public void StepEnvelope()
        {
            if (envelopeStart)
            {
                envelopeStart = false;
                envelopeVolume = 15;
                envelopeValue = envelopePeriod;
                return;
            }

            if (envelopeValue > 0)
            {
                envelopeValue--;
                return;
            }

            if (envelopeVolume > 0)
            {
                envelopeVolume--;
            }
            else if (envelopeLoop)
            {
                envelopeVolume = 15;
            }

            envelopeValue = envelopePeriod;
        }

        public void StepSweep()
        {
            if (sweepReload)
            {
                if (sweepEnabled && sweepValue == 0)
                {
                    ApplySweep();
                }
                sweepValue = sweepPeriod;
                sweepReload = false;
                return;
            }

            if (sweepValue > 0)
            {
                sweepValue--;
                return;
            }

            if (sweepEnabled)
            {
                ApplySweep();
            }
            sweepValue = sweepPeriod;
        }

        public void StepLength()
        {
            if (lengthEnabled && lengthValue > 0)
            {
                lengthValue--;
            }
        }

        public int GetOutput()
        {
            if (!enabled || lengthValue == 0)
            {
                return 0;
            }

            if (APUConstants.DutyTable[dutyMode][dutyValue] == 0)
            {
                return 0;
            }

            if (timerPeriod < 8 || timerPeriod > 0x07FF || IsSweepMuted())
            {
                return 0;
            }

            return envelopeEnabled ? envelopeVolume : constantVolume;
        }

        private void ApplySweep()
        {
            if (sweepShift == 0)
            {
                return;
            }

            int target = GetSweepTarget();
            if (target < 0)
            {
                target = 0;
            }
            timerPeriod = (ushort)target;
        }

        private int GetSweepTarget()
        {
            int delta = timerPeriod >> sweepShift;
            if (sweepNegate)
            {
                return channelNumber == 1 ? timerPeriod - delta - 1 : timerPeriod - delta;
            }

            return timerPeriod + delta;
        }

        private bool IsSweepMuted()
        {
            if (timerPeriod < 8)
            {
                return true;
            }

            if (!sweepEnabled || sweepShift == 0)
            {
                return false;
            }

            return GetSweepTarget() > 0x07FF;
        }
    }

    internal sealed class TriangleChannel
    {
        private bool enabled;
        private bool lengthEnabled;
        private byte lengthValue;
        private ushort timerPeriod;
        private ushort timerValue;
        private byte dutyValue;
        private byte counterPeriod;
        private byte counterValue;
        private bool counterReload;

        public byte LengthValue => lengthValue;

        public void Reset()
        {
            enabled = false;
            lengthEnabled = false;
            lengthValue = 0;
            timerPeriod = 0;
            timerValue = 0;
            dutyValue = 0;
            counterPeriod = 0;
            counterValue = 0;
            counterReload = false;
        }

        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                lengthValue = 0;
            }
        }

        public bool IsEnabled()
        {
            return enabled;
        }

        public void WriteControl(byte value)
        {
            lengthEnabled = (value & 0x80) == 0;
            counterPeriod = (byte)(value & 0x7F);
        }

        public void WriteTimerLow(byte value)
        {
            timerPeriod = (ushort)((timerPeriod & 0xFF00) | value);
        }

        public void WriteTimerHigh(byte value)
        {
            timerPeriod = (ushort)((timerPeriod & 0x00FF) | ((value & 0x07) << 8));
            timerValue = timerPeriod;
            counterReload = true;
            if (enabled)
            {
                lengthValue = APUConstants.LengthTable[(value >> 3) & 0x1F];
            }
        }

        public void StepTimer()
        {
            if (timerValue == 0)
            {
                timerValue = timerPeriod;
                if (lengthValue > 0 && counterValue > 0)
                {
                    dutyValue = (byte)((dutyValue + 1) & 0x1F);
                }
            }
            else
            {
                timerValue--;
            }
        }

        public void StepLength()
        {
            if (lengthEnabled && lengthValue > 0)
            {
                lengthValue--;
            }
        }

        public void StepCounter()
        {
            if (counterReload)
            {
                counterValue = counterPeriod;
            }
            else if (counterValue > 0)
            {
                counterValue--;
            }

            if (lengthEnabled)
            {
                counterReload = false;
            }
        }

        public int GetOutput()
        {
            if (!enabled || timerPeriod < 3 || lengthValue == 0 || counterValue == 0)
            {
                return 0;
            }

            return APUConstants.TriangleTable[dutyValue];
        }
    }

    internal sealed class NoiseChannel
    {
        private bool enabled;
        private bool mode;
        private ushort shiftRegister = 1;
        private bool lengthEnabled;
        private byte lengthValue;
        private ushort timerPeriod;
        private ushort timerValue;
        private bool envelopeEnabled;
        private bool envelopeLoop;
        private bool envelopeStart;
        private byte envelopePeriod;
        private byte envelopeValue;
        private byte envelopeVolume;
        private byte constantVolume;

        public byte LengthValue => lengthValue;

        public void Reset()
        {
            enabled = false;
            mode = false;
            shiftRegister = 1;
            lengthEnabled = false;
            lengthValue = 0;
            timerPeriod = 0;
            timerValue = 0;
            envelopeEnabled = false;
            envelopeLoop = false;
            envelopeStart = false;
            envelopePeriod = 0;
            envelopeValue = 0;
            envelopeVolume = 0;
            constantVolume = 0;
        }

        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                lengthValue = 0;
            }
        }

        public bool IsEnabled()
        {
            return enabled;
        }

        public void WriteControl(byte value)
        {
            lengthEnabled = (value & 0x20) == 0;
            envelopeLoop = (value & 0x20) != 0;
            envelopeEnabled = (value & 0x10) == 0;
            envelopePeriod = (byte)(value & 0x0F);
            constantVolume = (byte)(value & 0x0F);
            envelopeStart = true;
        }

        public void WritePeriod(byte value)
        {
            mode = (value & 0x80) != 0;
            timerPeriod = APUConstants.NoisePeriodTable[value & 0x0F];
        }

        public void WriteLength(byte value)
        {
            envelopeStart = true;
            if (enabled)
            {
                lengthValue = APUConstants.LengthTable[(value >> 3) & 0x1F];
            }
        }

        public void StepTimer()
        {
            if (timerValue == 0)
            {
                timerValue = timerPeriod;
                int shift = mode ? 6 : 1;
                int bit0 = shiftRegister & 0x01;
                int bit1 = (shiftRegister >> shift) & 0x01;
                shiftRegister >>= 1;
                shiftRegister |= (ushort)((bit0 ^ bit1) << 14);
            }
            else
            {
                timerValue--;
            }
        }

        public void StepEnvelope()
        {
            if (envelopeStart)
            {
                envelopeStart = false;
                envelopeVolume = 15;
                envelopeValue = envelopePeriod;
                return;
            }

            if (envelopeValue > 0)
            {
                envelopeValue--;
                return;
            }

            if (envelopeVolume > 0)
            {
                envelopeVolume--;
            }
            else if (envelopeLoop)
            {
                envelopeVolume = 15;
            }

            envelopeValue = envelopePeriod;
        }

        public void StepLength()
        {
            if (lengthEnabled && lengthValue > 0)
            {
                lengthValue--;
            }
        }

        public int GetOutput()
        {
            if (!enabled || lengthValue == 0 || (shiftRegister & 0x01) != 0)
            {
                return 0;
            }

            return envelopeEnabled ? envelopeVolume : constantVolume;
        }
    }

    internal sealed class DMCChannel
    {
        private MemoryBus? memoryBus;
        private CPU6502? cpu;
        private bool enabled;
        private bool irqEnabled;
        private bool loop;
        private ushort tickPeriod;
        private ushort tickValue;
        private byte value;
        private ushort sampleAddress;
        private ushort sampleLength;
        private ushort currentAddress;
        private ushort currentLength;
        private byte shiftRegister;
        private byte bitCount;
        private byte sampleBuffer;
        private bool sampleBufferEmpty;
        private bool silence;
        private bool irqPending;

        public ushort CurrentLength => currentLength;
        public bool IrqPending => irqPending;

        public void Connect(MemoryBus? memoryBus, CPU6502? cpu)
        {
            this.memoryBus = memoryBus;
            this.cpu = cpu;
        }

        public void Reset()
        {
            enabled = false;
            irqEnabled = false;
            loop = false;
            tickPeriod = APUConstants.DmcRateTable[0];
            tickValue = 0;
            value = 0;
            sampleAddress = 0xC000;
            sampleLength = 1;
            currentAddress = sampleAddress;
            currentLength = 0;
            shiftRegister = 0;
            bitCount = 0;
            sampleBuffer = 0;
            sampleBufferEmpty = true;
            silence = true;
            irqPending = false;
        }

        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                currentLength = 0;
                sampleBufferEmpty = true;
            }
            else if (currentLength == 0)
            {
                Restart();
            }
        }

        public bool IsEnabled()
        {
            return enabled;
        }

        public void ClearInterrupt()
        {
            irqPending = false;
        }

        public void WriteControl(byte value)
        {
            irqEnabled = (value & 0x80) != 0;
            if (!irqEnabled)
            {
                irqPending = false;
            }
            loop = (value & 0x40) != 0;
            tickPeriod = APUConstants.DmcRateTable[value & 0x0F];
        }

        public void WriteValue(byte value)
        {
            this.value = (byte)(value & 0x7F);
        }

        public void WriteAddress(byte value)
        {
            sampleAddress = (ushort)(0xC000 | (value << 6));
        }

        public void WriteLength(byte value)
        {
            sampleLength = (ushort)((value << 4) | 0x0001);
        }

        public void StepTimer()
        {
            StepReader();

            if (tickValue == 0)
            {
                tickValue = tickPeriod;
                StepShifter();
            }
            else
            {
                tickValue--;
            }
        }

        public int GetOutput()
        {
            return value;
        }

        private void Restart()
        {
            currentAddress = sampleAddress;
            currentLength = sampleLength;
        }

        private void StepReader()
        {
            if (currentLength == 0 || !sampleBufferEmpty || memoryBus == null)
            {
                return;
            }

            cpu?.AddStallCycles(4);
            sampleBuffer = memoryBus.Read(currentAddress);
            sampleBufferEmpty = false;
            currentAddress++;
            if (currentAddress == 0)
            {
                currentAddress = 0x8000;
            }

            currentLength--;
            if (currentLength == 0)
            {
                if (loop)
                {
                    Restart();
                }
                else if (irqEnabled)
                {
                    irqPending = true;
                    cpu?.TriggerIRQ();
                }
            }
        }

        private void StepShifter()
        {
            if (bitCount == 0)
            {
                ReloadShiftRegister();
            }

            if (!silence)
            {
                if ((shiftRegister & 0x01) != 0)
                {
                    if (value <= 125)
                    {
                        value += 2;
                    }
                }
                else if (value >= 2)
                {
                    value -= 2;
                }
            }

            shiftRegister >>= 1;
            bitCount--;
            if (bitCount == 0)
            {
                ReloadShiftRegister();
            }
        }

        private void ReloadShiftRegister()
        {
            bitCount = 8;
            if (sampleBufferEmpty)
            {
                silence = true;
                return;
            }

            silence = false;
            shiftRegister = sampleBuffer;
            sampleBufferEmpty = true;
        }
    }
}
