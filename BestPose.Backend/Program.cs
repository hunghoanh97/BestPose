using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

var builder = WebApplication.CreateBuilder(args);

// Enable CORS for local Expo web (http://localhost:8081) and mobile (any origin during dev)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

// Local helper
static byte[]? DecodeBase64(string? data)
{
    if (string.IsNullOrWhiteSpace(data)) return null;
    try
    {
        var idx = data.IndexOf(",");
        var base64 = idx >= 0 ? data[(idx + 1)..] : data;
        return Convert.FromBase64String(base64);
    }
    catch
    {
        return null;
    }
}

// Endpoint: person pose
app.MapPost("/pose/analyze", async (AnalyzeRequest req) =>
{
    var bytes = DecodeBase64(req.imageBase64);

    var tips = new List<string>();
    var orientation = req.meta?.orientation?.ToLowerInvariant();
    var facing = req.meta?.facing?.ToLowerInvariant();
    var roll = req.meta?.deviceRoll ?? 0;
    var pitch = req.meta?.devicePitch ?? 0;

    if (Math.Abs(roll) > 5) tips.Add($"Máy hơi nghiêng ~{roll:F1}°, cân lại");
    if (pitch > 8) tips.Add("Góc nhìn hơi cao, hạ máy xuống một chút");
    else if (pitch < -8) tips.Add("Góc nhìn hơi thấp, nâng máy lên một chút");

    if (orientation == "landscape") tips.Add("Đặt chủ thể gần giao điểm thirds");
    else tips.Add("Giữ bố cục dọc cân đối, tránh chia đôi khung hình");

    if (facing == "front") tips.Add("Giữ vai thẳng, khoanh tay tự nhiên");
    else tips.Add("Xoay hông nhẹ theo hướng camera để tạo đường cong");

    var lines = new List<ShapeLine>();
    double yTop = 0.33, yBottom = 0.66, xLeft = 0.33, xRight = 0.66;
    lines.Add(new ShapeLine(0.05, yTop, 0.95, yTop));
    lines.Add(new ShapeLine(0.05, yBottom, 0.95, yBottom));
    lines.Add(new ShapeLine(xLeft, 0.05, xLeft, 0.95));
    lines.Add(new ShapeLine(xRight, 0.05, xRight, 0.95));

    double horizonY = pitch > 0 ? 0.25 : (pitch < 0 ? 0.75 : 0.33);
    lines.Add(new ShapeLine(0.05, horizonY, 0.95, horizonY));

    List<AiKeypoint>? keypoints = null;
    try
    {
        var apiKey = app.Configuration["DEEPSEEK_API_KEY"] ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey) && bytes != null)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var baseUrl = app.Configuration["DEEPSEEK_BASE_URL"] ?? "https://api.deepseek.com";
            var model = app.Configuration["DEEPSEEK_MODEL"] ?? "deepseek-chat";

            var b64 = Convert.ToBase64String(bytes);
            var payload = new
            {
                model,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "Phân tích ảnh người và trả về JSON keypoints với tên và toạ độ chuẩn hoá trong [0,1]. Các keypoints: nose,left_shoulder,right_shoulder,left_elbow,right_elbow,left_hip,right_hip,left_knee,right_knee. Chỉ trả JSON, không văn bản." },
                            new { type = "input_image", image_url = $"data:image/jpeg;base64,{b64}" }
                        }
                    }
                },
                stream = false
            };

            var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
            };
            var resp = await http.SendAsync(reqMsg);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<AiKeypoint>>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                keypoints = parsed;
            }
        }
    }
    catch
    {
    }

    var result = new AiSuggestionResult(lines, tips, keypoints, null);
    return Results.Json(result, new System.Text.Json.JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

// Endpoint: scenery
app.MapPost("/scenery/analyze", (AnalyzeRequest req) =>
{
    var _ = DecodeBase64(req.imageBase64);

    var tips = new List<string>();
    var orientation = req.meta?.orientation?.ToLowerInvariant();
    var roll = req.meta?.deviceRoll ?? 0;
    var pitch = req.meta?.devicePitch ?? 0;

    if (Math.Abs(roll) > 4) tips.Add($"Chỉnh máy bớt nghiêng ~{roll:F1}° để đường chân trời thẳng");
    tips.Add("Ưu tiên đặt đường chân trời ở 1/3 khung");
    if (orientation == "landscape") tips.Add("Tìm các đường dẫn hội tụ vào chủ thể");
    else tips.Add("Giữ khoảng thở phía trên chủ thể trong khung dọc");

    var lines = new List<ShapeLine>();
    double yTop = 0.33, yBottom = 0.66, xLeft = 0.33, xRight = 0.66;
    lines.Add(new ShapeLine(0.05, yTop, 0.95, yTop));
    lines.Add(new ShapeLine(0.05, yBottom, 0.95, yBottom));
    lines.Add(new ShapeLine(xLeft, 0.05, xLeft, 0.95));
    lines.Add(new ShapeLine(xRight, 0.05, xRight, 0.95));

    // Gợi ý horizon (demo) tùy theo pitch
    double horizonY = pitch > 0 ? 0.25 : (pitch < 0 ? 0.75 : 0.33);
    lines.Add(new ShapeLine(0.05, horizonY, 0.95, horizonY));

    var result = new AiSuggestionResult(lines, tips, null, null);
    return Results.Json(result, new System.Text.Json.JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

// Endpoint: auto tune brightness/contrast
app.MapPost("/image/tune", (AnalyzeRequest req) =>
{
    var bytes = DecodeBase64(req.imageBase64);
    if (bytes == null) return Results.BadRequest();

    using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(bytes);
    double sum = 0;
    double cnt = 0;
    img.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                var p = row[x];
                var lum = (0.2126 * p.R + 0.7152 * p.G + 0.0722 * p.B);
                sum += lum;
                cnt += 1;
            }
        }
    });
    var mean = sum / Math.Max(1, cnt);
    float bAdj = mean < 110 ? 0.20f : (mean > 150 ? -0.08f : 0.0f);
    float cAdj = mean < 120 ? 0.18f : 0.06f;

    img.Mutate(ctx => ctx.Brightness(bAdj).Contrast(cAdj));
    using var ms = new MemoryStream();
    img.SaveAsJpeg(ms);
    var tuned = Convert.ToBase64String(ms.ToArray());

    var result = new AiSuggestionResult(null, null, null, tuned);
    return Results.Json(result, new System.Text.Json.JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

app.MapGet("/", () => "BestPose Backend running");

app.Run();
