using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Notifications.Trakt.Resource;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Trakt
{
    public class Trakt : NotificationBase<TraktSettings>
    {
        private readonly ITraktProxy _proxy;
        private readonly INotificationRepository _notificationRepository;
        private readonly Logger _logger;

        public Trakt(ITraktProxy proxy, INotificationRepository notificationRepository, Logger logger)
        {
            _proxy = proxy;
            _notificationRepository = notificationRepository;
            _logger = logger;
        }

        public override string Link => "https://trakt.tv/";
        public override string Name => "Trakt";

        public override void OnDownload(DownloadMessage message)
        {
            RefreshTokenIfNecessary();
            AddMovieToCollection(Settings, message.Movie, message.MovieFile);
        }

        public override void OnMovieFileDelete(MovieFileDeleteMessage deleteMessage)
        {
            RefreshTokenIfNecessary();
            RemoveMovieFromCollection(Settings, deleteMessage.Movie);
        }

        public override void OnMovieDelete(MovieDeleteMessage deleteMessage)
        {
            RefreshTokenIfNecessary();
            RemoveMovieFromCollection(Settings, deleteMessage.Movie);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            RefreshTokenIfNecessary();

            try
            {
                _proxy.GetUserName(Settings.AccessToken);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Error(ex, "Access Token is invalid: " + ex.Message);

                    failures.Add(new ValidationFailure("Token", "Access Token is invalid"));
                }
                else
                {
                    _logger.Error(ex, "Unable to send test message: " + ex.Message);

                    failures.Add(new ValidationFailure("Token", "Unable to send test message"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to send test message: " + ex.Message);

                failures.Add(new ValidationFailure("", "Unable to send test message"));
            }

            return new ValidationResult(failures);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                var request = _proxy.GetOAuthRequest(query["callbackUrl"]);

                return new
                {
                    OauthUrl = request.Url.ToString()
                };
            }
            else if (action == "getOAuthToken")
            {
                return new
                {
                    accessToken = query["access_token"],
                    expires = DateTime.UtcNow.AddSeconds(int.Parse(query["expires_in"])),
                    refreshToken = query["refresh_token"],
                    authUser = _proxy.GetUserName(query["access_token"])
                };
            }

            return new { };
        }

        private void RefreshTokenIfNecessary()
        {
            if (Settings.Expires < DateTime.UtcNow.AddMinutes(5))
            {
                RefreshToken();
            }
        }

        private void RefreshToken()
        {
            _logger.Trace("Refreshing Token");

            Settings.Validate().Filter("RefreshToken").ThrowOnError();

            try
            {
                var response = _proxy.RefreshAuthToken(Settings.RefreshToken);

                if (response != null)
                {
                    var token = response;

                    Settings.AccessToken = token.AccessToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
                    Settings.RefreshToken = token.RefreshToken ?? Settings.RefreshToken;

                    if (Definition.Id > 0)
                    {
                        _notificationRepository.UpdateSettings((NotificationDefinition)Definition);
                    }
                }
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Error refreshing trakt access token");
            }
        }

        private void AddMovieToCollection(TraktSettings settings, Movie movie, MovieFile movieFile)
        {
            var payload = new TraktCollectMoviesResource
            {
                Movies = new List<TraktCollectMovie>()
            };

            var traktResolution = MapResolution(movieFile.Quality.Quality.Resolution, movieFile.MediaInfo?.ScanType);
            var hdr = MapHdr(movieFile);
            var mediaType = MapMediaType(movieFile.Quality.Quality.Source);
            var audio = MapAudio(movieFile);
            var audioChannels = MapAudioChannels(movieFile);

            payload.Movies.Add(new TraktCollectMovie
            {
                Title = movie.Title,
                Year = movie.Year,
                CollectedAt = DateTime.Now,
                Resolution = traktResolution,
                Hdr = hdr,
                MediaType = mediaType,
                AudioChannels = audioChannels,
                Audio = audio,
                Is3D = movieFile.MediaInfo?.VideoMultiViewCount > 1,
                Ids = new TraktMovieIdsResource
                {
                    Tmdb = movie.MovieMetadata.Value.TmdbId,
                    Imdb = movie.MovieMetadata.Value.ImdbId ?? "",
                }
            });

            _proxy.AddToCollection(payload, settings.AccessToken);
        }

        private void RemoveMovieFromCollection(TraktSettings settings, Movie movie)
        {
            var payload = new TraktCollectMoviesResource
            {
                Movies = new List<TraktCollectMovie>()
            };

            payload.Movies.Add(new TraktCollectMovie
            {
                Title = movie.Title,
                Year = movie.Year,
                Ids = new TraktMovieIdsResource
                {
                    Tmdb = movie.MovieMetadata.Value.TmdbId,
                    Imdb = movie.MovieMetadata.Value.ImdbId ?? "",
                }
            });

            _proxy.RemoveFromCollection(payload, settings.AccessToken);
        }

        private string MapMediaType(QualitySource source)
        {
            var traktSource = source switch
            {
                QualitySource.BLURAY => "bluray",
                QualitySource.WEBDL => "digital",
                QualitySource.WEBRIP => "digital",
                QualitySource.DVD => "dvd",
                QualitySource.TV => "dvd",
                _ => string.Empty
            };

            return traktSource;
        }

        private string MapResolution(int resolution, string scanType)
        {
            var scanIdentifier = scanType.IsNotNullOrWhiteSpace() && TraktInterlacedTypes.InterlacedTypes.Contains(scanType) ? "i" : "p";

            var traktResolution = resolution switch
            {
                2160 => "uhd_4k",
                1080 => $"hd_1080{scanIdentifier}",
                720 => "hd_720p",
                576 => $"sd_576{scanIdentifier}",
                480 => $"sd_480{scanIdentifier}",
                _ => string.Empty
            };

            return traktResolution;
        }

        private string MapHdr(MovieFile movieFile)
        {
            var traktHdr = movieFile.MediaInfo?.VideoHdrFormat switch
            {
                HdrFormat.DolbyVision or HdrFormat.DolbyVisionSdr => "dolby_vision",
                HdrFormat.Hdr10 or HdrFormat.DolbyVisionHdr10 => "hdr10",
                HdrFormat.Hdr10Plus or HdrFormat.DolbyVisionHdr10Plus => "hdr10_plus",
                HdrFormat.Hlg10 or HdrFormat.DolbyVisionHlg => "hlg",
                _ => null
            };

            return traktHdr;
        }

        private string MapAudio(MovieFile movieFile)
        {
            var audioCodec = movieFile.MediaInfo != null ? MediaInfoFormatter.FormatAudioCodec(movieFile.MediaInfo, movieFile.SceneName) : string.Empty;

            var traktAudioFormat = audioCodec switch
            {
                "AC3" => "dolby_digital",
                "EAC3" => "dolby_digital_plus",
                "TrueHD" => "dolby_truehd",
                "EAC3 Atmos" => "dolby_digital_plus_atmos",
                "TrueHD Atmos" => "dolby_atmos",
                "DTS" => "dts",
                "DTS-ES" => "dts",
                "DTS-HD MA" => "dts_ma",
                "DTS-HD HRA" => "dts_hr",
                "DTS-X" => "dts_x",
                "MP3" => "mp3",
                "MP2" => "mp2",
                "Vorbis" => "ogg",
                "WMA" => "wma",
                "AAC" => "aac",
                "PCM" => "lpcm",
                "FLAC" => "flac",
                "Opus" => "ogg_opus",
                _ => string.Empty
            };

            return traktAudioFormat;
        }

        private string MapAudioChannels(MovieFile movieFile)
        {
            var audioChannels = movieFile.MediaInfo != null ? MediaInfoFormatter.FormatAudioChannels(movieFile.MediaInfo).ToString("0.0") : string.Empty;

            if (audioChannels == "0.0")
            {
                audioChannels = string.Empty;
            }

            return audioChannels;
        }
    }
}
