using OxyPlot;
using OxyPlot.Annotations;

namespace ESP32StreamManager.ML
{
    public class EcgPredictionState
    {
        private readonly PlotModel _plotModel;
        private readonly double _maxSpikeToQrsDelaySec;

        private const double MarkerHalfWidth = 0.18;

        private double? _lastSpikeTime = null;
        private readonly List<RectangleAnnotation> _annotations = new();

        public EcgPredictionState(
            PlotModel plotModel,
            double sampleRate = 500.0,
            double maxSpikeToQrsDelaySec = 0.45)
        {
            _plotModel = plotModel;
            _maxSpikeToQrsDelaySec = maxSpikeToQrsDelaySec;
        }

        public SegmentType Update(double timestamp, EcgPrediction prediction)
        {
            var type = prediction.Type;

            if (type == SegmentType.Spike)
                _lastSpikeTime = timestamp;

            if (type == SegmentType.Qrs && _lastSpikeTime.HasValue)
            {
                double dt = timestamp - _lastSpikeTime.Value;

                if (dt >= -0.10 && dt <= 0.45)
                    type = SegmentType.QrsAfterSpike;
            }

            if (type != SegmentType.Background)
                AddMarker(timestamp, type);

            return type;
        }

        private void AddMarker(double timestamp, SegmentType type)
        {
            var annotation = new RectangleAnnotation
            {
                MinimumX = timestamp - MarkerHalfWidth,
                MaximumX = timestamp + MarkerHalfWidth,
                MinimumY = double.NegativeInfinity,
                MaximumY = double.PositiveInfinity,
                Fill = GetFillColor(type),
                Stroke = OxyColors.Transparent,
                Layer = AnnotationLayer.BelowSeries
            };

            _plotModel.Annotations.Add(annotation);
            _annotations.Add(annotation);
        }

        public void Trim(double minTime)
        {
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                if (_annotations[i].MaximumX < minTime)
                {
                    _plotModel.Annotations.Remove(_annotations[i]);
                    _annotations.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            foreach (var annotation in _annotations)
                _plotModel.Annotations.Remove(annotation);

            _annotations.Clear();
            _lastSpikeTime = null;
        }

        private static OxyColor GetFillColor(SegmentType type)
        {
            return type switch
            {
                SegmentType.Qrs =>
                    OxyColor.FromAColor(45, OxyColor.FromRgb(34, 197, 94)),

                SegmentType.Spike =>
                    OxyColor.FromAColor(55, OxyColor.FromRgb(239, 68, 68)),

                SegmentType.QrsAfterSpike =>
                    OxyColor.FromAColor(55, OxyColor.FromRgb(168, 85, 247)),

                _ => OxyColors.Transparent
            };
        }
    }
}