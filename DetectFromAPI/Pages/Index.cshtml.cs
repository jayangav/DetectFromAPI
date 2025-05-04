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
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", ImageFile.FileName);

        var apiUrl = _config["FastApiSettings:ApiUrl"];
        var apiKey = _config["FastApiSettings:ApiKey"];
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);

        var response = await client.PostAsync(apiUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"API call failed. StatusCode: {response.StatusCode}");
            return Page();
        }

        var jsonString = await response.Content.ReadAsStringAsync();

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

        return Page();
    }
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
