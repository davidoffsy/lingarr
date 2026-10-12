using System.ComponentModel.DataAnnotations;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

public class ProbeEmbeddedRequest
{
    public required int MediaId { get; set; }

    [EnumDataType(typeof(MediaType))]
    public required MediaType MediaType { get; set; }
}
