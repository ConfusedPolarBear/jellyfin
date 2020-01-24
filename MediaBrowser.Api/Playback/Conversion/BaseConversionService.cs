using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;


namespace MediaBrowser.Api.Playback.Conversion
{
    /// <summary>
    /// Class BaseConversionService
    /// </summary>
    public abstract class BaseConversionService : BaseStreamingService
    {
        /// <summary>
        /// Gets the audio arguments.
        /// </summary>
        protected abstract string GetAudioArguments(StreamState state, EncodingOptions encodingOptions);

        /// <summary>
        /// Gets the video arguments.
        /// </summary>
        protected abstract string GetVideoArguments(StreamState state, EncodingOptions encodingOptions);

        /// <summary>
        /// Base class for the conversion service
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="serverConfigurationManager"></param>
        /// <param name="httpResultFactory"></param>
        /// <param name="userManager"></param>
        /// <param name="libraryManager"></param>
        /// <param name="isoManager"></param>
        /// <param name="mediaEncoder"></param>
        /// <param name="fileSystem"></param>
        /// <param name="dlnaManager"></param>
        /// <param name="deviceManager"></param>
        /// <param name="mediaSourceManager"></param>
        /// <param name="jsonSerializer"></param>
        /// <param name="authorizationContext"></param>
        /// <param name="encodingHelper"></param>
        public BaseConversionService(
            ILogger logger,
            IServerConfigurationManager serverConfigurationManager,
            IHttpResultFactory httpResultFactory,
            IUserManager userManager,
            ILibraryManager libraryManager,
            IIsoManager isoManager,
            IMediaEncoder mediaEncoder,
            IFileSystem fileSystem,
            IDlnaManager dlnaManager,
            IDeviceManager deviceManager,
            IMediaSourceManager mediaSourceManager,
            IJsonSerializer jsonSerializer,
            IAuthorizationContext authorizationContext,
            EncodingHelper encodingHelper)
            : base(
                logger,
                serverConfigurationManager,
                httpResultFactory,
                userManager,
                libraryManager,
                isoManager,
                mediaEncoder,
                fileSystem,
                dlnaManager,
                deviceManager,
                mediaSourceManager,
                jsonSerializer,
                authorizationContext,
                encodingHelper)
        {
        }
    }
}
