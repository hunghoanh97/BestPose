using System.Text.Json.Serialization;

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
app.MapPost("/pose/analyze", (AnalyzeRequest req) =>
{
    var _ = DecodeBase64(req.imageBase64); // hiện chưa dùng ảnh

    // Tips động theo meta
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

    // Lines thirds theo orientation
    var lines = new List<ShapeLine>();
    double yTop = 0.33, yBottom = 0.66, xLeft = 0.33, xRight = 0.66;
    lines.Add(new ShapeLine(0.05, yTop, 0.95, yTop));
    lines.Add(new ShapeLine(0.05, yBottom, 0.95, yBottom));
    lines.Add(new ShapeLine(xLeft, 0.05, xLeft, 0.95));
    lines.Add(new ShapeLine(xRight, 0.05, xRight, 0.95));

    // Gợi ý horizon (demo) tùy theo pitch
    double horizonY = pitch > 0 ? 0.25 : (pitch < 0 ? 0.75 : 0.33);
    lines.Add(new ShapeLine(0.05, horizonY, 0.95, horizonY));

    var result = new AiSuggestionResult(lines, tips, null);
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

    var result = new AiSuggestionResult(lines, tips, null);
    return Results.Json(result, new System.Text.Json.JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

app.MapGet("/", () => "BestPose Backend running");

app.Run();
