// 효과음 파형을 코드로 합성한다(UnityEngine 없이 순수 계산 → 헤드리스로 WAV 미리듣기 가능).
// 카드 게임 UI처럼 '여러 겹'으로 쌓은 소리: 몸통 음 + 짧은 잡음(질감) + 맑은 종소리(반짝임) + 짧은 울림(공간감).
// 특정 게임의 소리를 베끼지 않고, 기본 파형(사인·잡음)만 조합해 만든 독자적인 소리다.
using System;

namespace BlackWhale.UI.View
{
    public static class UiSoundSynth
    {
        public const int SampleRate = 44100;

        /// <summary>효과음 종류별 파형(-1~1, 모노). variant로 같은 소리를 살짝 다르게(피치) 만든다.</summary>
        public static float[] Render(UiSound.Kind kind, int variant = 0)
        {
            double p = variant == 1 ? 0.96 : variant == 2 ? 1.04 : 1.0; // 반복해서 눌러도 덜 기계적으로
            switch (kind)
            {
                case UiSound.Kind.Buy: return Buy();
                case UiSound.Kind.Sell: return Sell();
                case UiSound.Kind.Coin: return Coin(variant);
                case UiSound.Kind.Turn: return Turn();
                case UiSound.Kind.Warning: return Warning();
                case UiSound.Kind.ResultGood: return ResultGood();
                case UiSound.Kind.ResultBad: return ResultBad();
                default: return Click(p);
            }
        }

        // 버튼: 맑고 부드러운 '뽁'. 음이 살짝 올라가는 짧은 물방울 소리 + 아주 작은 반짝임(딱딱한 '틱' 대신).
        private static float[] Click(double p)
        {
            var b = new float[Len(0.18)];
            Pop(b, 0, 520 * p, 980 * p, 0.06, 0.6);             // 몸통: 위로 튀는 둥근 음
            Bell(b, 0.004, 1975 * p, 0.10, 0.10);                // 살짝 맑은 윗소리(B6)
            Room(b, 0.22);
            return Finish(b, 0.7);
        }

        // 매수: 지갑에서 동전 '찰랑' → 위로 올라가는 반짝 아르페지오(얻었다는 느낌).
        private static float[] Buy()
        {
            var b = new float[Len(0.75)];
            Thump(b, 0, 180, 95, 0.09, 0.5);
            Noise(b, 0, 0.02, 0.25, 0.6, 2);
            Bell(b, 0.01, 2093, 0.35, 0.28);                   // C7 동전
            Bell(b, 0.05, 2637, 0.30, 0.22);                   // E7 동전
            double[] up = { 523.25, 659.25, 783.99, 1046.5 };   // C5 E5 G5 C6 (장화음, 밝음)
            for (int i = 0; i < up.Length; i++)
                Pluck(b, 0.07 + i * 0.055, up[i], 0.32, 0.22 + i * 0.03);
            Sparkle(b, 0.28, 0.25, 0.10, 7);
            Room(b, 0.32);
            return Finish(b, 0.8);
        }

        // 매도: '휙' 하고 카드가 빠지는 바람 소리 → 내려가는 부드러운 음 → 마지막 '툭'.
        private static float[] Sell()
        {
            var b = new float[Len(0.7)];
            Whoosh(b, 0, 0.18, 0.35);
            double[] down = { 783.99, 659.25, 523.25 };         // G5 E5 C5
            for (int i = 0; i < down.Length; i++)
                Pluck(b, 0.06 + i * 0.06, down[i], 0.30, 0.26);
            Thump(b, 0.22, 160, 85, 0.10, 0.5);
            Bell(b, 0.22, 1568, 0.25, 0.12);
            Room(b, 0.28);
            return Finish(b, 0.8);
        }

        // 코인 사용: 동전 여러 개가 '짤랑짤랑'(살짝 엇갈린 금속 종소리).
        private static float[] Coin(int variant)
        {
            var b = new float[Len(0.6)];
            var rng = new Random(1234 + variant);
            double[] f = { 2349, 2794, 3136, 2637 };
            for (int i = 0; i < f.Length; i++)
                Bell(b, i * 0.045 + rng.NextDouble() * 0.012, f[i] * (0.98 + rng.NextDouble() * 0.04), 0.22, 0.24 - i * 0.03);
            Noise(b, 0, 0.015, 0.2, 0.8, 3);
            Room(b, 0.3);
            return Finish(b, 0.75);
        }

        // 새 단계 시작: 부드럽게 차오르는 소리 위로 '띠리링~' 올라가는 종소리(알림 느낌, 웅장한 화음 대신).
        private static float[] Turn()
        {
            var b = new float[Len(1.1)];
            Swell(b, 0, 0.35, new[] { 523.25, 783.99 }, 0.18);  // C5+G5 잔잔한 바탕음이 서서히 커짐
            double[] up = { 783.99, 1046.5, 1318.5, 1568.0 };   // G5 C6 E6 G6
            for (int i = 0; i < up.Length; i++)
                Bell(b, 0.06 + i * 0.07, up[i], 0.6, 0.22 - i * 0.02);
            Sparkle(b, 0.32, 0.35, 0.07, 11);
            Room(b, 0.45);
            return Finish(b, 0.72);
        }

        private static float[] Warning()
        {
            var b = new float[Len(0.16)];
            Bell(b, 0.01, 880, 0.12, 0.28);
            Bell(b, 0.08, 660, 0.08, 0.22);
            return Finish(b, 0.55);
        }

        private static float[] ResultGood()
        {
            var b = new float[Len(0.85)];
            double[] up = { 523.25, 659.25, 783.99, 1046.5 };
            for (int i = 0; i < up.Length; i++) Bell(b, 0.08 + i * 0.10, up[i], 0.35, 0.22);
            Sparkle(b, 0.32, 0.28, 0.14, 19);
            return Finish(b, 0.72);
        }

        private static float[] ResultBad()
        {
            var b = new float[Len(0.55)];
            Thump(b, 0, 180, 72, 0.20, 0.42);
            Pluck(b, 0.04, 196, 0.32, 0.28);
            Pluck(b, 0.16, 146.83, 0.28, 0.24);
            return Finish(b, 0.68);
        }

        // ---------------------------------------------------------------- 재료

        private static int Len(double seconds) => (int)(seconds * SampleRate);
        private static int At(double seconds) => (int)(seconds * SampleRate);

        /// <summary>사인 + 배음. decay가 클수록 짧게 끝난다.</summary>
        private static void Tone(float[] b, double start, double freq, double dur, double amp, double decay, double harmonicRatio)
        {
            int s0 = At(start), n = Len(dur);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Min(1, i / (SampleRate * 0.002)) * Math.Exp(-t * decay);
                double w = Math.Sin(2 * Math.PI * freq * t) + 0.3 * Math.Sin(2 * Math.PI * freq * harmonicRatio * t);
                b[s0 + i] += (float)(w * env * amp);
            }
        }

        /// <summary>음이 from→to로 빠르게 내려가는 낮은 '툭'(킥 드럼처럼 몸통이 있는 소리).</summary>
        private static void Thump(float[] b, double start, double from, double to, double dur, double amp)
        {
            int s0 = At(start), n = Len(dur);
            double phase = 0;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate, k = t / dur;
                double f = to + (from - to) * Math.Exp(-k * 6);
                phase += 2 * Math.PI * f / SampleRate;
                double env = Math.Min(1, i / (SampleRate * 0.0015)) * Math.Pow(1 - k, 2.2);
                b[s0 + i] += (float)(Math.Sin(phase) * env * amp);
            }
        }

        /// <summary>짧은 잡음. smooth(0~1)가 클수록 둥글고 낮은(먹먹한) 질감.</summary>
        private static void Noise(float[] b, double start, double dur, double amp, double smooth, int seed)
        {
            var rng = new Random(seed);
            int s0 = At(start), n = Len(dur);
            double y = 0;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double k = (double)i / n;
                y = y * smooth + (rng.NextDouble() * 2 - 1) * (1 - smooth);
                b[s0 + i] += (float)(y * amp * (1 - k) * (1 - k) * (1 + smooth * 2));
            }
        }

        /// <summary>금속 종소리: 정수배가 아닌 배음(1, 2.76, 5.40)이라 '쨍'하고 반짝인다.</summary>
        private static void Bell(float[] b, double start, double freq, double dur, double amp)
        {
            double[] ratio = { 1, 2.76, 5.40 }, gain = { 1, 0.45, 0.2 }, decay = { 9, 16, 26 };
            int s0 = At(start), n = Len(dur);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate, v = 0;
                for (int h = 0; h < ratio.Length; h++) v += gain[h] * Math.Sin(2 * Math.PI * freq * ratio[h] * t) * Math.Exp(-t * decay[h]);
                b[s0 + i] += (float)(v * amp * Math.Min(1, i / (SampleRate * 0.001)));
            }
        }

        /// <summary>따뜻하게 '퉁기는' 음(하프·마림바 느낌): 기본음 + 2·3배음이 빨리 사라진다.</summary>
        private static void Pluck(float[] b, double start, double freq, double dur, double amp)
        {
            int s0 = At(start), n = Len(dur);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate;
                double v = Math.Sin(2 * Math.PI * freq * t) * Math.Exp(-t * 7)
                         + 0.35 * Math.Sin(4 * Math.PI * freq * t) * Math.Exp(-t * 14)
                         + 0.15 * Math.Sin(6 * Math.PI * freq * t) * Math.Exp(-t * 22);
                b[s0 + i] += (float)(v * amp * Math.Min(1, i / (SampleRate * 0.003)));
            }
        }

        /// <summary>위로 흩어지는 작은 반짝임(높은 종소리 여러 개).</summary>
        private static void Sparkle(float[] b, double start, double dur, double amp, int seed)
        {
            var rng = new Random(seed);
            for (int i = 0; i < 6; i++)
            {
                double at = start + dur * i / 6 + rng.NextDouble() * 0.02;
                Bell(b, at, 3000 + i * 260 + rng.NextDouble() * 200, 0.12, amp * (1 - i * 0.12));
            }
        }

        /// <summary>'휙' 바람 소리: 잡음이 점점 커졌다 작아지며, 먹먹함이 바뀐다.</summary>
        private static void Whoosh(float[] b, double start, double dur, double amp)
        {
            var rng = new Random(77);
            int s0 = At(start), n = Len(dur);
            double y = 0, y2 = 0;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double k = (double)i / n;
                double smooth = 0.6 + 0.35 * k; // 처음엔 밝고 뒤로 갈수록 먹먹하게(멀어지는 느낌)
                y = y * smooth + (rng.NextDouble() * 2 - 1) * (1 - smooth);
                y2 = y2 * 0.5 + y * 0.5;
                double env = Math.Sin(Math.PI * k);
                b[s0 + i] += (float)((y - y2) * 4 * env * amp);
            }
        }

        /// <summary>'뽁' 물방울 소리: 음이 from→to로 빠르게 올라가며 짧게 사라진다(배음 없는 둥근 사인).</summary>
        private static void Pop(float[] b, double start, double from, double to, double dur, double amp)
        {
            int s0 = At(start), n = Len(dur);
            double phase = 0;
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate, k = t / dur;
                double f = to + (from - to) * Math.Exp(-k * 8);
                phase += 2 * Math.PI * f / SampleRate;
                double env = Math.Min(1, i / (SampleRate * 0.003)) * Math.Exp(-k * 5);
                b[s0 + i] += (float)(Math.Sin(phase) * env * amp);
            }
        }

        /// <summary>잔잔한 바탕음: 천천히 커졌다가 서서히 사라지는 부드러운 화음(살짝 떨림).</summary>
        private static void Swell(float[] b, double start, double rise, double[] freqs, double amp)
        {
            int s0 = At(start);
            for (int i = 0; s0 + i < b.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = t < rise ? Math.Sin(Math.PI * 0.5 * t / rise) : Math.Exp(-(t - rise) * 4);
                double v = 0;
                foreach (double f in freqs)
                    v += Math.Sin(2 * Math.PI * f * t + 0.4 * Math.Sin(2 * Math.PI * 5 * t)) + 0.2 * Math.Sin(4 * Math.PI * f * t);
                b[s0 + i] += (float)(v / freqs.Length * env * amp);
            }
        }

        /// <summary>작은 방 울림: 짧게 늦은 복사본 몇 개를 작게 더한다(소리가 덜 건조해짐).</summary>
        private static void Room(float[] b, double wet)
        {
            int[] delays = { At(0.023), At(0.037), At(0.053), At(0.071) };
            double[] gains = { 0.5, 0.38, 0.28, 0.2 };
            var src = (float[])b.Clone();
            for (int d = 0; d < delays.Length; d++)
                for (int i = delays[d]; i < b.Length; i++)
                    b[i] += (float)(src[i - delays[d]] * gains[d] * wet);
        }

        /// <summary>최대 크기를 peak에 맞추고 부드럽게 눌러 찢어지는 소리를 막는다. 끝은 살짝 페이드.</summary>
        private static float[] Finish(float[] b, double peak)
        {
            double max = 1e-6;
            foreach (float v in b) max = Math.Max(max, Math.Abs(v));
            int fade = Math.Min(b.Length, At(0.02));
            for (int i = 0; i < b.Length; i++)
            {
                double v = Math.Tanh(b[i] / max * 1.2) / Math.Tanh(1.2) * peak;
                if (i >= b.Length - fade) v *= (double)(b.Length - i) / fade;
                b[i] = (float)v;
            }
            return b;
        }
    }
}
