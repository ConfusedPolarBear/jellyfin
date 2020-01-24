using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Services;

namespace MediaBrowser.Api.Playback
{
    /// <summary>
    /// Class InitiateConversionRequest
    /// </summary>
    public class InitiateConversionRequest : VideoStreamRequest
    {
        [ApiMember(Name = "Output Version", Description = "If set, will move the transcoded file to the original folder with this string as the version identifier", IsRequired = false, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string OutputVersion { get; set; }
    }

    /// <summary>
    /// Class ConversionStatusRequest
    /// </summary>
    public class ConversionStatusRequest
    {
        public string JobId { get; set; }
    }

    public class ConversionStatusReport
    {
        public bool Error { get; set; }
        public string Message { get; set; }
        public string JobId { get; set; }
        public double PercentComplete { get; set; }
        public TranscodingJobType Type { get; set; } = TranscodingJobType.Download;
        public bool IsComplete { get; set; }
    }
}
