namespace DetectFromAPI.Models
{
    public class DetectionResponse
    {
        public List<Detection> Detections { get; set; }
    }

    public class Detection
    {
        public string Class { get; set; }
        public float Confidence { get; set; }
        public List<float> Box { get; set; }
    }
}

    

