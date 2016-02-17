﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using TAS.Common;
using TAS.Data;
using TAS.Server.Interfaces;
using TAS.Server.Common;

namespace TAS.Server
{
    public class ServerDirectory : MediaDirectory, IServerDirectory
    {
        public readonly IPlayoutServer Server;
        public ServerDirectory(IPlayoutServer server, MediaManager manager)
            : base(manager)
        {
            Server = server;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
        public override void Initialize()
        {
            this.Load();
            VerifyMedia();
            base.Initialize();
            Debug.WriteLine(this, "Directory initialized");
        }

        public override void Refresh()
        {

        }

        protected override IMedia CreateMedia(string fullPath, Guid guid)
        {
            return new ServerMedia(this, guid)
            {
                FullPath = fullPath,
            };
        }

        public event EventHandler<MediaDtoEventArgs> MediaSaved;
        internal virtual void OnMediaSaved(Media media)
        {
            var handler = MediaSaved;
            if (handler != null)
                handler(media, new MediaDtoEventArgs(media.DtoGuid, media.MediaGuid));
        }

        public override void MediaAdd(IMedia media)
        {
            base.MediaAdd(media);
            media.PropertyChanged += OnMediaPropertyChanged;
            if (media.MediaStatus != TMediaStatus.Required && File.Exists(media.FullPath))
                ThreadPool.QueueUserWorkItem(o => ((Media)media).Verify());
        }

        public override void MediaRemove(IMedia media)
        {
            ServerMedia m = (ServerMedia)media;
            m.MediaStatus = TMediaStatus.Deleted;
            m.Verified = false;
            m.Save();
            media.PropertyChanged -= OnMediaPropertyChanged;
            base.MediaRemove(media);
        }

        public event PropertyChangedEventHandler MediaPropertyChanged;

        internal virtual void OnMediaPropertyChanged(object o, PropertyChangedEventArgs e)
        {
            if (this.MediaPropertyChanged != null)
                this.MediaPropertyChanged(o, e);
        }

        public override void SweepStaleMedia()
        {
            DateTime currentDateTime = DateTime.UtcNow.Date;
            IEnumerable<IMedia> StaleMediaList = FindMediaList(m => (m is ServerMedia) && currentDateTime > (m as ServerMedia).KillDate);
            foreach (Media m in StaleMediaList)
                m.Delete();
        }

        public IServerMedia GetServerMedia(IMedia media, bool searchExisting = true)
        {
            if (media == null)
                return null;
            ServerMedia fm = (ServerMedia)FindMediaByMediaGuid(media.MediaGuid); // check if need to select new Guid
            if (fm == null || !searchExisting)
            {
                fm = (new ServerMedia(this, fm == null ? media.MediaGuid : Guid.NewGuid()) // in case file with the same GUID already exists and we need to get new one
                {
                    MediaName = media.MediaName,
                    FullPath = (media is IngestMedia) ? 
                            Path.Combine(Folder, (FileUtils.VideoFileTypes.Any(ext => ext == Path.GetExtension(media.FileName).ToLower()) ? Path.GetFileNameWithoutExtension(media.FileName) : media.FileName) + FileUtils.DefaultFileExtension(media.MediaType)) 
                            : 
                            Path.Combine(Folder, media.FileName),
                    MediaType = (media.MediaType == TMediaType.Unknown) ? (FileUtils.StillFileTypes.Any(ve => ve == Path.GetExtension(media.FullPath).ToLowerInvariant()) ? TMediaType.Still : TMediaType.Movie) : media.MediaType,
                    MediaStatus = TMediaStatus.Required,
                    TcStart = media.TcStart,
                    TcPlay = media.TcPlay,
                    Duration = media.Duration,
                    DurationPlay = media.DurationPlay,
                    VideoFormat = media.VideoFormat,
                    AudioChannelMapping = media.AudioChannelMapping,
                    AudioVolume = media.AudioVolume,
                    AudioLevelIntegrated = media.AudioLevelIntegrated,
                    AudioLevelPeak = media.AudioLevelPeak,
                    KillDate = default(DateTime),
                    DoNotArchive = (media is ServerMedia && (media as ServerMedia).DoNotArchive),
                    MediaCategory = media.MediaCategory,
                    Parental = media.Parental,
                    IdAux = (media is PersistentMedia) ? (media as PersistentMedia).IdAux : string.Empty,
                    idProgramme = (media is PersistentMedia) ? (media as PersistentMedia).idProgramme : 0L,
                    OriginalMedia = media,
                });
                NotifyMediaAdded(fm);
                fm.PropertyChanged += MediaPropertyChanged;
            }
            else
                if (fm.MediaStatus == TMediaStatus.Deleted)
                fm.MediaStatus = TMediaStatus.Required;
            return fm;
        }

        protected override void OnMediaRenamed(Media media, string newName)
        {
            ((ServerMedia)media).Save();
        }

        public void VerifyMedia()
        {
                var unverifiedFiles = _files.Values.Where(mf => ((ServerMedia)mf).Verified == false).Cast<Media>().ToList();
                unverifiedFiles.ForEach(media => media.Verify());
        }

    }
}