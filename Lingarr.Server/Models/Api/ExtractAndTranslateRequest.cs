using System.ComponentModel.DataAnnotations;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

public class ExtractAndTranslateRequest
{
    public required int MediaId { get; set; }

    [EnumDataType(typeof(MediaType))]
    public required MediaType MediaType { get; set; }

    public required int TrackIndex { get; set; }

    public string? SourceLanguage { get; set; }

    public required string TargetLanguage { get; set; }
}
