using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System.Text.Json.Serialization;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public IndexModel(IHttpClientFactory clientFactory, IWebHostEnvironment env, IConfiguration config)
    {
        _clientFactory = clientFactory;
        _env = env;
        _config = config;
    }

    [BindProperty]
    public IFormFile ImageFile { get; set; }

    [BindProperty]
    public string SelectedFeature { get; set; }

    [BindProperty]
    public string Question { get; set; }

    public string Answer { get; set; }

    public Dictionary<string, string> FormFields { get; set; } = new();


    public string DetectedImagePath { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ImageFile == null || ImageFile.Length == 0)
            return Page();

        var uploadPath = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadPath);
        var filePath = Path.Combine(uploadPath, ImageFile.FileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await ImageFile.CopyToAsync(stream);
        }

        var client = _clientFactory.CreateClient();
        var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(System.IO.File.OpenRead(filePath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ImageFile.ContentType);
        content.Add(fileContent, "file", ImageFile.FileName);

        var apiKey = _config["FastApiSettings:ApiKey"];
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);

        string apiUrl;

        if (SelectedFeature == "signature")
        {
            apiUrl = _config["FastApiSettings:SignatureApiUrl"];
        }
        else if (SelectedFeature == "form")
        {
            apiUrl = _config["FastApiSettings:FormApiUrl"];
        }
        else if (SelectedFeature == "qa")
        {
            apiUrl = _config["FastApiSettings:QaApiUrl"];
            content.Add(new StringContent(Question ?? ""), "question");
        }
        else
        {
            return Page();
        }

        var response = await client.PostAsync(apiUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"API call failed. StatusCode: {response.StatusCode}");
            return Page();
        }

        var jsonString = await response.Content.ReadAsStringAsync();

        if (SelectedFeature == "signature")
        {
            var detectionResponse = JsonSerializer.Deserialize<DetectionResponse>(jsonString);
            if (detectionResponse?.Detections == null || detectionResponse.Detections.Count == 0)
            {
                DetectedImagePath = "/uploads/" + ImageFile.FileName;
                return Page();
            }

            var outputImagePath = Path.Combine("uploads", "detected_" + ImageFile.FileName);
            var outputImageFullPath = Path.Combine(_env.WebRootPath, outputImagePath);

            using (var image = await Image.LoadAsync<Rgb24>(filePath))
            {
                foreach (var detection in detectionResponse.Detections)
                {
                    var box = detection.Box;
                    image.Mutate(ctx =>
                    {
                        ctx.DrawPolygon(SixLabors.ImageSharp.Color.Red, 3, new PointF[]
                        {
                        new PointF(box[0], box[1]),
                        new PointF(box[2], box[1]),
                        new PointF(box[2], box[3]),
                        new PointF(box[0], box[3])
                        });

                        ctx.DrawText($"{detection.Class} ({detection.Confidence:P1})",
                            SystemFonts.CreateFont("Arial", 16),
                            SixLabors.ImageSharp.Color.Blue,
                            new PointF(box[0], Math.Max(0, box[1] - 20)));
                    });
                }

                await image.SaveAsync(outputImageFullPath);
            }

            DetectedImagePath = "/" + outputImagePath.Replace("\\", "/");
        }
        else if (SelectedFeature == "form")
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
            if (fields != null)
            {
                FormFields = fields;
            }
        }
        else if (SelectedFeature == "qa")
        {
            var responseObj = JsonSerializer.Deserialize<QaResponse>(jsonString);
            Answer = responseObj?.Answer;
        }

        return Page();
    }

}

public class QaResponse
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; }
}


public class DetectionResponse
{
    [JsonPropertyName("detections")]
    public List<Detection> Detections { get; set; }
}

public class Detection
{
    [JsonPropertyName("class")]
    public string Class { get; set; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    [JsonPropertyName("box")]
    public List<float> Box { get; set; }
}
