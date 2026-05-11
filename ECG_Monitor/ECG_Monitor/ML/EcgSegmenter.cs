using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ESP32StreamManager.ML
{
    public class EcgSegmenter : IDisposable
    {
        private const int WindowSize = 512;
        private const int Channels = 12;

        private readonly InferenceSession _session;
        private readonly Queue<float> _buffer = new();

        public EcgSegmenter(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public bool AddSample(float value, out EcgPrediction prediction)
        {
            prediction = new EcgPrediction
            {
                Type = SegmentType.Background,
                Probability = 0
            };

            _buffer.Enqueue(value);

            if (_buffer.Count > WindowSize)
                _buffer.Dequeue();

            if (_buffer.Count < WindowSize)
                return false;

            prediction = PredictCurrentPoint();
            return true;
        }

        private EcgPrediction PredictCurrentPoint()
        {
            float[] signal = _buffer.ToArray();
            Normalize(signal);

            var input = new DenseTensor<float>(
                new[] { 1, Channels, WindowSize });

            for (int ch = 0; ch < Channels; ch++)
            {
                for (int i = 0; i < WindowSize; i++)
                {
                    input[0, ch, i] = signal[i];
                }
            }

            string inputName = _session.InputMetadata.Keys.First();

            using var results = _session.Run(new[]
            {
        NamedOnnxValue.CreateFromTensor(inputName, input)
    });

            var output = results.First().AsTensor<float>();

            int lastIndex = WindowSize - 1;

            float backgroundLogit = output[0, 0, lastIndex];
            float qrsLogit = output[0, 1, lastIndex];
            float spikeLogit = output[0, 2, lastIndex];
            float qrsAfterLogit = output[0, 3, lastIndex];

            float maxLogit = MathF.Max(
                MathF.Max(backgroundLogit, qrsLogit),
                MathF.Max(spikeLogit, qrsAfterLogit));

            float backgroundExp = MathF.Exp(backgroundLogit - maxLogit);
            float qrsExp = MathF.Exp(qrsLogit - maxLogit);
            float spikeExp = MathF.Exp(spikeLogit - maxLogit);
            float qrsAfterExp = MathF.Exp(qrsAfterLogit - maxLogit);

            float sumExp = backgroundExp + qrsExp + spikeExp + qrsAfterExp;

            float background = backgroundExp / sumExp;
            float qrs = qrsExp / sumExp;
            float spike = spikeExp / sumExp;
            float qrsAfter = qrsAfterExp / sumExp;

            SegmentType type = SegmentType.Background;
            float probability = background;


            if (spike >= 0.15f &&
                spike >= qrs * 0.45f &&
                spike >= background * 0.50f)
            {
                type = SegmentType.Spike;
                probability = spike;
            }
            else if (qrsAfter >= 0.45f &&
                     qrsAfter >= background &&
                     qrsAfter >= qrs * 0.70f)
            {
                type = SegmentType.QrsAfterSpike;
                probability = qrsAfter;
            }
            else if (qrs >= 0.45f &&
                     qrs >= background)
            {
                type = SegmentType.Qrs;
                probability = qrs;
            }

            return new EcgPrediction
            {
                Type = type,
                Probability = probability
            };
        }

        private void Normalize(float[] signal)
        {
            float mean = signal.Average();

            float std = MathF.Sqrt(
                signal.Select(x => (x - mean) * (x - mean)).Average());

            if (std < 1e-6f)
                std = 1f;

            for (int i = 0; i < signal.Length; i++)
                signal[i] = (signal[i] - mean) / std;
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}