using System.Text.Json.Serialization;

public record ShapeLine(double x1, double y1, double x2, double y2);
public record AiKeypoint(string name, double x, double y, double? score);
public record AiSuggestionResult(
    List<ShapeLine>? lines,
    List<string>? tips,
    List<AiKeypoint>? keypoints,
    string? tunedImageBase64
);

public record AnalyzeMeta(
    string? orientation,
    string? facing,
    double? deviceRoll,
    double? devicePitch,
    double? deviceYaw,
    int? frameWidth,
    int? frameHeight,
    double? fov
);

public record AnalyzeRequest(string imageBase64, string mode, AnalyzeMeta? meta);