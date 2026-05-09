using OxyPlot;
using OxyPlot.Annotations;

namespace ESP32StreamManager.ML
{
    public class EcgPredictionState
    {
        private readonly PlotModel _plotModel;

        private readonly double _minSpikeToQrsAfterDelaySec;
        private readonly double _maxSpikeToQrsAfterDelaySec;

        private readonly double _minSpikeGapSec;
        private readonly double _minQrsGapSec;
        private readonly double _minQrsAfterGapSec;

        private const double QrsHalfWidth = 0.09;
        private const double SpikeHalfWidth = 0.24;
        private const double QrsAfterWidth = 0.40;

        private const double MarkerGapAfterSpike = 0.015;

        private const double MergeGapSec = 0.08;

        private double? _lastSpikeTime = null;
        private double? _lastSpikeMarkerTime = null;

        private double? _lastQrsMarkerTime = null;
        private double? _lastQrsAfterMarkerTime = null;

        private RectangleAnnotation? _lastQrsAnnotation = null;
        private RectangleAnnotation? _lastSpikeAnnotation = null;
        private RectangleAnnotation? _lastQrsAfterAnnotation = null;

        private readonly List<RectangleAnnotation> _annotations = new();

        public EcgPredictionState(
            PlotModel plotModel,
            double sampleRate = 500.0,
            double minSpikeToQrsAfterDelaySec = 0.12,
            double maxSpikeToQrsAfterDelaySec = 1.05,
            double minSpikeGapSec = 0.20,
            double minQrsGapSec = 0.20,
            double minQrsAfterGapSec = 0.25)
        {
            _plotModel = plotModel;

            _minSpikeToQrsAfterDelaySec = minSpikeToQrsAfterDelaySec;
            _maxSpikeToQrsAfterDelaySec = maxSpikeToQrsAfterDelaySec;

            _minSpikeGapSec = minSpikeGapSec;
            _minQrsGapSec = minQrsGapSec;
            _minQrsAfterGapSec = minQrsAfterGapSec;
        }

        public SegmentType Update(double timestamp, EcgPrediction prediction)
        {
            var type = prediction.Type;

            if (type == SegmentType.Background)
                return SegmentType.Background;

            if (type == SegmentType.Spike)
            {
                if (IsTooClose(_lastSpikeMarkerTime, timestamp, _minSpikeGapSec))
                    return SegmentType.Background;

                AddMarker(timestamp, SegmentType.Spike);

                _lastSpikeTime = timestamp;
                _lastSpikeMarkerTime = timestamp;

                return SegmentType.Spike;
            }

            if (_lastSpikeTime.HasValue)
            {
                double dt = timestamp - _lastSpikeTime.Value;

                if (dt > _maxSpikeToQrsAfterDelaySec)
                {
                    _lastSpikeTime = null;
                }
                else
                {

                    if (type == SegmentType.Qrs || type == SegmentType.QrsAfterSpike)
                    {
                        if (dt < _minSpikeToQrsAfterDelaySec)
                            return SegmentType.Background;

                        if (IsTooClose(_lastQrsAfterMarkerTime, timestamp, _minQrsAfterGapSec))
                            return SegmentType.Background;

                        AddMarker(timestamp, SegmentType.QrsAfterSpike);
                        _lastQrsAfterMarkerTime = timestamp;

                        return SegmentType.QrsAfterSpike;
                    }

                    return SegmentType.Background;
                }
            }


            if (type == SegmentType.QrsAfterSpike)
            {
                if (IsTooClose(_lastQrsMarkerTime, timestamp, _minQrsGapSec))
                    return SegmentType.Background;

                AddMarker(timestamp, SegmentType.Qrs);
                _lastQrsMarkerTime = timestamp;

                return SegmentType.Qrs;
            }


            if (type == SegmentType.Qrs)
            {
                if (IsTooClose(_lastQrsMarkerTime, timestamp, _minQrsGapSec))
                    return SegmentType.Background;

                AddMarker(timestamp, SegmentType.Qrs);
                _lastQrsMarkerTime = timestamp;

                return SegmentType.Qrs;
            }

            return SegmentType.Background;
        }

        private static bool IsTooClose(double? lastTime, double currentTime, double minGap)
        {
            return lastTime.HasValue && (currentTime - lastTime.Value) < minGap;
        }

        private void AddMarker(double timestamp, SegmentType type)
        {
            double minX;
            double maxX;

            if (type == SegmentType.QrsAfterSpike)
            {
                double startX = timestamp;

                if (_lastSpikeMarkerTime.HasValue)
                {
                    double spikeRightEdge = _lastSpikeMarkerTime.Value + SpikeHalfWidth;
                    startX = Math.Max(startX, spikeRightEdge + MarkerGapAfterSpike);
                }

                minX = startX;
                maxX = startX + QrsAfterWidth;
            }
            else
            {
                double halfWidth = type switch
                {
                    SegmentType.Qrs => QrsHalfWidth,
                    SegmentType.Spike => SpikeHalfWidth,
                    _ => 0.08
                };

                minX = timestamp - halfWidth;
                maxX = timestamp + halfWidth;
            }

            if (TryMergeWithPrevious(type, minX, maxX))
                return;

            var annotation = new RectangleAnnotation
            {
                MinimumX = minX,
                MaximumX = maxX,
                MinimumY = double.NegativeInfinity,
                MaximumY = double.PositiveInfinity,
                Fill = GetFillColor(type),
                Stroke = OxyColors.Transparent,
                Layer = AnnotationLayer.BelowSeries
            };

            _plotModel.Annotations.Add(annotation);
            _annotations.Add(annotation);

            RememberLastAnnotation(type, annotation);
        }

        private bool TryMergeWithPrevious(
            SegmentType type,
            double minX,
            double maxX)
        {
            RectangleAnnotation? lastAnnotation = GetLastAnnotation(type);

            if (lastAnnotation == null)
                return false;

            bool canMerge = minX <= lastAnnotation.MaximumX + MergeGapSec;

            if (!canMerge)
                return false;

            lastAnnotation.MinimumX = Math.Min(lastAnnotation.MinimumX, minX);
            lastAnnotation.MaximumX = Math.Max(lastAnnotation.MaximumX, maxX);

            return true;
        }

        private RectangleAnnotation? GetLastAnnotation(SegmentType type)
        {
            return type switch
            {
                SegmentType.Qrs => _lastQrsAnnotation,
                SegmentType.Spike => _lastSpikeAnnotation,
                SegmentType.QrsAfterSpike => _lastQrsAfterAnnotation,
                _ => null
            };
        }

        private void RememberLastAnnotation(
            SegmentType type,
            RectangleAnnotation annotation)
        {
            switch (type)
            {
                case SegmentType.Qrs:
                    _lastQrsAnnotation = annotation;
                    break;

                case SegmentType.Spike:
                    _lastSpikeAnnotation = annotation;
                    break;

                case SegmentType.QrsAfterSpike:
                    _lastQrsAfterAnnotation = annotation;
                    break;
            }
        }

        public void Trim(double minTime)
        {
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                if (_annotations[i].MaximumX < minTime)
                {
                    var annotation = _annotations[i];

                    _plotModel.Annotations.Remove(annotation);
                    _annotations.RemoveAt(i);

                    if (_lastQrsAnnotation == annotation)
                        _lastQrsAnnotation = null;

                    if (_lastSpikeAnnotation == annotation)
                        _lastSpikeAnnotation = null;

                    if (_lastQrsAfterAnnotation == annotation)
                        _lastQrsAfterAnnotation = null;
                }
            }
        }

        public void Clear()
        {
            foreach (var annotation in _annotations)
                _plotModel.Annotations.Remove(annotation);

            _annotations.Clear();

            _lastSpikeTime = null;
            _lastSpikeMarkerTime = null;
            _lastQrsMarkerTime = null;
            _lastQrsAfterMarkerTime = null;

            _lastQrsAnnotation = null;
            _lastSpikeAnnotation = null;
            _lastQrsAfterAnnotation = null;
        }

        private static OxyColor GetFillColor(SegmentType type)
        {
            return type switch
            {
                SegmentType.Qrs =>
                    OxyColor.FromAColor(70, OxyColors.Red),

                SegmentType.Spike =>
                    OxyColor.FromAColor(65, OxyColors.Green),

                SegmentType.QrsAfterSpike =>
                    OxyColor.FromAColor(85, OxyColors.Orange),

                _ => OxyColors.Transparent
            };
        }
    }
}