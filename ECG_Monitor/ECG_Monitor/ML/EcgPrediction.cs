namespace ESP32StreamManager.ML
{
    public class EcgPrediction
    {
        public SegmentType Type { get; set; }
        public float Probability { get; set; }
    }
}