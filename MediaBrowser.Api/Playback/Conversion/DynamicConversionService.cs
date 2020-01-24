using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Api.Playback.Conversion
{
    /// <summary>
    /// Initialize a transcode session that will eventually create a fully transcoded file.
    /// </summary>
    [Route("/Conversion/{Id}/Initiate", "GET", Summary = "Begins transcoding media with the provided id")]
    public class GetInitializeConversion : InitiateConversionRequest
    {
    }

    /// <summary>
    /// Returns JSON with the current transcode status for the provided identifier.
    /// </summary>
    [Route("/Conversion/{JobId}/Status", "GET", Summary = "Gets current transcode info for the provided id")]
    public class GetConversionStatus : ConversionStatusRequest
    {
    }

    /// <summary>
    /// Cancels an actively running transcode and deletes the output file.
    /// </summary>
    [Route("/Conversion/{JobId}/Cancel", "GET", Summary = "Stops a currently running transcode for the provided id")]
    public class GetDeleteConversion : ConversionStatusRequest
    {
    }

    /// <summary>
    /// Returns the transcoded media file with the provided identifier.
    /// </summary>
    [Route("/Conversion/{JobId}/Download", "GET", Summary = "Download a transcoded media file")]
    public class GetConversion : ConversionStatusRequest
    {
    }

    /// <summary>
    /// Playback API that allows for the transcoding and downloading or local storage of transcoded media
    /// </summary>
    [Authenticated]
    public class DynamicConversionService : BaseConversionService
    {
        /// <summary>
        /// Base initializer for the conversion service
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
        public DynamicConversionService(
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

        /// <summary>
        /// Transcode type indiciating that this is a file that will be downloaded
        /// </summary>
        protected override TranscodingJobType TranscodingJobType => TranscodingJobType.Download;

        public Task<object> Get(GetInitializeConversion request)
        {
            return GetInitializeConversionInternal(request);
        }

        public async Task<object> Get(GetConversionStatus request)
        {
            return GetConversionStatus(request.JobId);
        }

        public async Task<object> Get(GetDeleteConversion request)
        {
            return DeleteConversionInternal(request.JobId);
        }

        public Task<object> Get(GetConversion request)
        {
            return GetFileResult(request.JobId);
        }

        private async Task<object> GetInitializeConversionInternal(InitiateConversionRequest request)
        {
            request.PlaySessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var output = request.OutputVersion;

            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            var state = await GetState(request, cancellationToken).ConfigureAwait(false);

            var downloadFile = state.OutputFilePath;

            if(!string.IsNullOrEmpty(output))
            {
                // Sanitize the output version variable to prevent path traversal. This code is from DenNukem at https://stackoverflow.com/a/13617375
                var invalids = Path.GetInvalidFileNameChars();
                output = string.Join('_', output.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

                var source = state.MediaSource;

                var modified = Path.GetFileNameWithoutExtension(source.Path);
                var dir = Path.GetDirectoryName(source.Path);

                // TODO: bug: if the original filename is similar to "Original Media Name - Dashed String", the converted name will be "Original Media Name - Dashed String - Sanitized Output Version" which seems wrong
                modified = string.Format("{0} - {1}.mp4", modified, output);

                state.ConversionOutputFilePath = Path.Combine(dir, modified);

                if(File.Exists(state.ConversionOutputFilePath))
                {
                    var message = string.Format("Media version {0} already exists", output);

                    Logger.LogWarning(message);
                    return new ConversionStatusReport { Error = true, Message = message };
                }

                state.TranscodingType = TranscodingJobType.Conversion;
            }

            TranscodingJob job = null;

            var transcodingLock = ApiEntryPoint.Instance.GetTranscodingLock(downloadFile);
            await transcodingLock.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
            var released = false;

            try
            {
                job = await StartFfMpeg(state, downloadFile, cancellationTokenSource).ConfigureAwait(false);
                Logger.LogDebug("Successfully started conversion session with id {0}", request.PlaySessionId);
            }
            catch
            {
                state.Dispose();
            }
            finally
            {
                if (!released)
                {
                    transcodingLock.Release();
                }
            }

            job ??= ApiEntryPoint.Instance.OnTranscodeBeginRequest(downloadFile, TranscodingJobType);
            return GetConversionStatus(job.PlaySessionId);
        }

        private Task<object> GetFileResult(string sessionId)
        {
            var transcodingJob = ApiEntryPoint.Instance.GetTranscodingJob(sessionId);
            if (transcodingJob == null || !transcodingJob.HasExited || transcodingJob.Type != TranscodingJobType.Download)
            {
                Logger.LogWarning("Unable to return result for conversion session {0}", sessionId);
                return null;
            }

            return ResultFactory.GetStaticFileResult(Request, new StaticFileResultOptions
            {
                Path = transcodingJob.Path,
                FileShare = FileShare.ReadWrite,
                OnComplete = () =>
                {
                    Logger.LogDebug("finished serving {0}", transcodingJob.Path);
                    if (transcodingJob != null)
                    {
                        transcodingJob.DownloadPositionTicks = transcodingJob.DownloadPositionTicks;
                        ApiEntryPoint.Instance.OnTranscodeEndRequest(transcodingJob);
                    }
                }
            });
        }

        private ConversionStatusReport DeleteConversionInternal(string sessionId)
        {
            var job = ApiEntryPoint.Instance.GetTranscodingJob(sessionId);
            if (job == null)
            {
                var message = "Cannot find job";
                Logger.LogWarning(message);
                return new ConversionStatusReport { Error = true, Message = message };
            }

            ApiEntryPoint.Instance.KillTranscodingJobs("", sessionId, p => true);

            return new ConversionStatusReport { Error = false, Message = "Job deleted" };
        }

        private ConversionStatusReport GetConversionStatus(string sessionId) {
            var job = ApiEntryPoint.Instance.GetTranscodingJob(sessionId);
            if (job == null)
            {
                var message = "Cannot find job";
                Logger.LogWarning(message);
                return new ConversionStatusReport { Error = true, Message = message };
            }

            var percent = job.HasExited ? 100 : Math.Round(job.CompletionPercentage ?? 0, 0);

            return new ConversionStatusReport {
                Error = false,
                Message = "",
                IsComplete = job.HasExited,
                JobId = job.PlaySessionId,
                PercentComplete = percent,
                Type = job.Type
            };
        }

        /// <summary>
        /// Construct the arguments for FFmpeg regarding audio
        /// </summary>
        /// <param name="state"></param>
        /// <param name="encodingOptions"></param>
        /// <returns></returns>
        protected override string GetAudioArguments(StreamState state, EncodingOptions encodingOptions)
        {
            var audioCodec = EncodingHelper.GetAudioEncoder(state);

            if (!state.IsOutputVideo)
            {
                if (string.Equals(audioCodec, "copy", StringComparison.OrdinalIgnoreCase))
                {
                    return "-acodec copy";
                }

                var audioTranscodeParams = new List<string>();

                audioTranscodeParams.Add("-acodec " + audioCodec);

                if (state.OutputAudioBitrate.HasValue)
                {
                    audioTranscodeParams.Add("-ab " + state.OutputAudioBitrate.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (state.OutputAudioChannels.HasValue)
                {
                    audioTranscodeParams.Add("-ac " + state.OutputAudioChannels.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (state.OutputAudioSampleRate.HasValue)
                {
                    audioTranscodeParams.Add("-ar " + state.OutputAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture));
                }

                audioTranscodeParams.Add("-vn");
                return string.Join(" ", audioTranscodeParams.ToArray());
            }

            if (string.Equals(audioCodec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                var videoCodec = EncodingHelper.GetVideoEncoder(state, encodingOptions);

                if (string.Equals(videoCodec, "copy", StringComparison.OrdinalIgnoreCase) && state.EnableBreakOnNonKeyFrames(videoCodec))
                {
                    return "-codec:a:0 copy -copypriorss:a:0 0";
                }

                return "-codec:a:0 copy";
            }

            var args = "-codec:a:0 " + audioCodec;

            var channels = state.OutputAudioChannels;

            if (channels.HasValue)
            {
                args += " -ac " + channels.Value;
            }

            var bitrate = state.OutputAudioBitrate;

            if (bitrate.HasValue)
            {
                args += " -ab " + bitrate.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (state.OutputAudioSampleRate.HasValue)
            {
                args += " -ar " + state.OutputAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture);
            }

            args += " " + EncodingHelper.GetAudioFilterParam(state, encodingOptions, true);

            return args;
        }

        /// <summary>
        /// Construct the arguments for FFmpeg regarding video
        /// </summary>
        /// <param name="state"></param>
        /// <param name="encodingOptions"></param>
        /// <returns></returns>
        protected override string GetVideoArguments(StreamState state, EncodingOptions encodingOptions)
        {
            if (!state.IsOutputVideo)
            {
                return string.Empty;
            }

            var codec = EncodingHelper.GetVideoEncoder(state, encodingOptions);

            var args = "-codec:v:0 " + codec;

            // if (state.EnableMpegtsM2TsMode)
            // {
            //     args += " -mpegts_m2ts_mode 1";
            // }

            // See if we can save come cpu cycles by avoiding encoding
            if (string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase))
            {
                if (state.VideoStream != null && !string.Equals(state.VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
                {
                    string bitStreamArgs = EncodingHelper.GetBitStreamArgs(state.VideoStream);
                    if (!string.IsNullOrEmpty(bitStreamArgs))
                    {
                        args += " " + bitStreamArgs;
                    }
                }

                //args += " -flags -global_header";
            }
            else
            {
                var hasGraphicalSubs = state.SubtitleStream != null && !state.SubtitleStream.IsTextSubtitleStream && state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode;

                args += " " + EncodingHelper.GetVideoQualityParam(state, codec, encodingOptions, GetDefaultEncoderPreset());

                //args += " -mixed-refs 0 -refs 3 -x264opts b_pyramid=0:weightb=0:weightp=0";

                // Add resolution params, if specified
                if (!hasGraphicalSubs)
                {
                    args += EncodingHelper.GetOutputSizeParam(state, encodingOptions, codec, true);
                }

                // This is for internal graphical subs
                if (hasGraphicalSubs)
                {
                    args += EncodingHelper.GetGraphicalSubtitleParam(state, encodingOptions, codec);
                }

                //args += " -flags -global_header";
            }

            if (args.IndexOf("-copyts", StringComparison.OrdinalIgnoreCase) == -1)
            {
                args += " -copyts";
            }

            if (!string.IsNullOrEmpty(state.OutputVideoSync))
            {
                args += " -vsync " + state.OutputVideoSync;
            }

            args += EncodingHelper.GetOutputFFlags(state);

            return args;
        }

        /// <summary>
        /// Construct the actual stringified arguments for FFmpeg
        /// </summary>
        /// <param name="outputPath"></param>
        /// <param name="encodingOptions"></param>
        /// <param name="state"></param>
        /// <param name="isEncoding"></param>
        /// <returns></returns>
        protected override string GetCommandLineArguments(string outputPath, EncodingOptions encodingOptions, StreamState state, bool isEncoding)
        {
            var videoCodec = EncodingHelper.GetVideoEncoder(state, encodingOptions);

            var threads = EncodingHelper.GetNumberOfThreads(state, encodingOptions, videoCodec);

            var inputModifier = EncodingHelper.GetInputModifier(state, encodingOptions);

            var mapArgs = state.IsOutputVideo ? EncodingHelper.GetMapArgs(state) : string.Empty;

            return string.Format(
                "{0} {1} -map_metadata -1 -map_chapters -1 -threads {2} {3} {4} {5} -f mp4 -max_delay 5000000 -avoid_negative_ts disabled -start_at_zero -y \"{6}\"",
                inputModifier,
                EncodingHelper.GetInputArgument(state, encodingOptions),
                threads,
                mapArgs,
                GetVideoArguments(state, encodingOptions),
                GetAudioArguments(state, encodingOptions),
                outputPath
            ).Trim();
        }
    }
}
