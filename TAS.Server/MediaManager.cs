﻿using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Remoting.Messaging;
using System.IO;
using System.Configuration;
using System.Xml.Serialization;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.ServiceModel;
using TAS.Common;
using TAS.Data;
using TAS.Server.Interfaces;
using TAS.Server.Common;
using Newtonsoft.Json;
using TAS.Remoting.Server;

namespace TAS.Server
{

    [JsonObject(MemberSerialization.OptIn)]
    public class MediaManager: DtoBase, IMediaManager
    {
        readonly Engine _engine;
        readonly FileManager _fileManager;
        public IFileManager FileManager { get { return _fileManager; } }
        public IEngine getEngine() { return _engine; }
        public IServerDirectory MediaDirectoryPGM { get; private set; }
        public IServerDirectory MediaDirectoryPRV { get; private set; }
        public IAnimationDirectory AnimationDirectoryPGM { get; private set; }
        public IAnimationDirectory AnimationDirectoryPRV { get; private set; }
        public IArchiveDirectory ArchiveDirectory { get; private set; }
        public readonly ObservableSynchronizedCollection<ITemplate> _templates = new ObservableSynchronizedCollection<ITemplate>();
        [JsonProperty]
        public VideoFormatDescription FormatDescription { get { return _engine.FormatDescription; } }
        [JsonProperty]
        public TVideoFormat VideoFormat { get { return _engine.VideoFormat; } }
        public double VolumeReferenceLoudness { get { return _engine.VolumeReferenceLoudness; } }

        public MediaManager(Engine engine)
        {
            _engine = engine;
            _fileManager = new FileManager() { TempDirectory = new TempDirectory(this) };
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
        public void Initialize()
        {
            MediaDirectoryPGM = (_engine.PlayoutChannelPGM == null) ? null : _engine.PlayoutChannelPGM.OwnerServer.MediaDirectory;
            MediaDirectoryPRV = (_engine.PlayoutChannelPRV == null) ? null : _engine.PlayoutChannelPRV.OwnerServer.MediaDirectory;
            AnimationDirectoryPGM = (_engine.PlayoutChannelPGM == null) ? null : _engine.PlayoutChannelPGM.OwnerServer.AnimationDirectory;
            AnimationDirectoryPRV = (_engine.PlayoutChannelPRV == null) ? null : _engine.PlayoutChannelPRV.OwnerServer.AnimationDirectory;

            ArchiveDirectory = this.LoadArchiveDirectory(_engine.IdArchive);
            Debug.WriteLine(this, "Begin initializing");
            ServerDirectory sdir = MediaDirectoryPGM as ServerDirectory;
            if (sdir != null)
            {
                sdir.MediaPropertyChanged += ServerMediaPropertyChanged;
                sdir.PropertyChanged += _onServerDirectoryPropertyChanged;
                sdir.MediaSaved += _onServerDirectoryMediaSaved;
                sdir.MediaVerified += _mediaPGMVerified;
                sdir.MediaRemoved += _mediaPGMRemoved;
            }
            sdir = MediaDirectoryPRV as ServerDirectory;
            if (MediaDirectoryPGM != MediaDirectoryPRV && sdir != null)
            {
                sdir.MediaPropertyChanged += ServerMediaPropertyChanged;
                sdir.PropertyChanged += _onServerDirectoryPropertyChanged;
            }
            IAnimationDirectory adir = AnimationDirectoryPGM;
            if (adir != null)
                adir.PropertyChanged += _onAnimationDirectoryPropertyChanged;

            LoadIngestDirs(ConfigurationManager.AppSettings["IngestFolders"]);
            _fileManager.VolumeReferenceLoudness =  Convert.ToDecimal(VolumeReferenceLoudness);
            Debug.WriteLine(this, "End initializing");
        }

        private List<IIngestDirectory> _ingestDirectories;
        public List<IIngestDirectory> IngestDirectories
        {
            get
            {
                lock (_ingestDirsSyncObject)
                    return _ingestDirectories.ToList();
            }
        }

        private bool _ingestDirectoriesLoaded = false;
        private object _ingestDirsSyncObject = new object();


        public void ReloadIngestDirs()
        {
            foreach (IngestDirectory d in _ingestDirectories)
                d.Dispose();
            LoadIngestDirs(ConfigurationManager.AppSettings["IngestFolders"]);
            Debug.WriteLine(this, "IngestDirectories reloaded");
        }

        public void LoadIngestDirs(string fileName)
        {
            lock (_ingestDirsSyncObject)
            {
                if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                {
                    if (_ingestDirectoriesLoaded)
                        return;
                    XmlSerializer reader = new XmlSerializer(typeof(List<IngestDirectory>), new XmlRootAttribute("IngestDirectories"));
                    System.IO.StreamReader file = new System.IO.StreamReader(fileName);
                    _ingestDirectories = ((List<IngestDirectory>)reader.Deserialize(file)).Cast<IIngestDirectory>().ToList();
                    file.Close();
                }
                else _ingestDirectories = new List<IIngestDirectory>();
                _ingestDirectoriesLoaded = true;
                foreach (IngestDirectory d in _ingestDirectories)
                {
                    d.MediaManager = this;
                    d.Initialize();
                }
            }
        }

        private void ServerMediaPropertyChanged(object media, PropertyChangedEventArgs e)
        {
            if (media is ServerMedia
                && !string.IsNullOrEmpty(e.PropertyName)
                   && (e.PropertyName == "DoNotArchive"
                    || e.PropertyName == "HasExtraLines"
                    || e.PropertyName == "IdAux"
                    || e.PropertyName == "idFormat"
                    || e.PropertyName == "idProgramme"
                    || e.PropertyName == "KillDate"
                    || e.PropertyName == "OriginalMedia"
                    || e.PropertyName == "AudioVolume"
                    || e.PropertyName == "MediaCategory"
                    || e.PropertyName == "Parental"
                    || e.PropertyName == "MediaEmphasis"
                    || e.PropertyName == "FileName"
                    || e.PropertyName == "MediaName"
                    || e.PropertyName == "Duration"
                    || e.PropertyName == "DurationPlay"
                    || e.PropertyName == "TcStart"
                    || e.PropertyName == "TcPlay"
                    || e.PropertyName == "VideoFormat"
                    || e.PropertyName == "AudioChannelMapping"
                    || e.PropertyName == "AudioLevelIntegrated"
                    || e.PropertyName == "AudioLevelPeak"
                    ))
            {
                ServerMedia compMedia = _findComplementaryMedia(media as ServerMedia);
                if (compMedia != null)
                {
                    if (e.PropertyName == "FileName")
                        compMedia.RenameTo(((ServerMedia)media).FileName);
                    else
                    {
                        PropertyInfo sourcePi = media.GetType().GetProperty(e.PropertyName);
                        PropertyInfo destPi = compMedia.GetType().GetProperty(e.PropertyName);
                        if (sourcePi != null && destPi != null)
                            destPi.SetValue(compMedia, sourcePi.GetValue(media, null), null);
                    }
                }
            }
        }

        private void _onServerDirectoryPropertyChanged(object dir, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsInitialized")
                SynchronizePrvToPgm(false);
        }

        private void _onAnimationDirectoryPropertyChanged(object dir, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsInitialized")
            {
                ThreadPool.QueueUserWorkItem((o) => _syncAnimations());
            }
        }

        private void _onServerDirectoryMediaSaved(object media, MediaDtoEventArgs e)
        {
            ServerMedia pgmMedia = media as ServerMedia;
            if (pgmMedia != null && pgmMedia.MediaStatus != TMediaStatus.Deleted)
            {
                ServerMedia compMedia = _findComplementaryMedia(pgmMedia);
                if (compMedia != null)
                    ThreadPool.QueueUserWorkItem((o) =>
                        {
                            compMedia.Save();
                        }
                    );
            }
        }

        public void CopyMediaToPlayout(IEnumerable<IMedia> mediaList, bool toTop)
        {
            foreach (IMedia sourceMedia in mediaList)
            {
                if (sourceMedia is ArchiveMedia)
                {
                    IServerDirectory destDir = MediaDirectoryPGM != null && MediaDirectoryPGM.DirectoryExists() ? MediaDirectoryPGM :
                                               MediaDirectoryPRV != null && MediaDirectoryPRV.DirectoryExists() ? MediaDirectoryPRV :
                                               null;
                    if (destDir != null)
                    {
                        IMedia destMedia = destDir.GetServerMedia(sourceMedia, true);
                        if (!destMedia.FileExists())
                            _fileManager.Queue(new FileOperation() { Kind = TFileOperationKind.Copy, SourceMedia = sourceMedia, DestMedia = destMedia }, toTop);
                    }
                }
            }
        }
            

        private MediaDeleteDenyReason deleteMedia(IMedia media)
        {
            MediaDeleteDenyReason reason = (media is ServerMedia) ? _engine.CanDeleteMedia(media as ServerMedia) : MediaDeleteDenyReason.NoDeny;
            if (reason.Reason == MediaDeleteDenyReason.MediaDeleteDenyReasonEnum.NoDeny)
                _fileManager.Queue(new FileOperation() { Kind = TFileOperationKind.Delete, SourceMedia = media });
            return reason;
        }

        public IEnumerable<MediaDeleteDenyReason> DeleteMedia(IEnumerable<IMedia> mediaList)
        {
            List<MediaDeleteDenyReason> result = new List<MediaDeleteDenyReason>();
            foreach (var media in mediaList)
                result.Add(deleteMedia(media));
            return result;
        }

        public void GetLoudness(IEnumerable<IMedia> mediaList)
        {
            foreach (IMedia m in mediaList)
                m.GetLoudness();
        }

        public void ArchiveMedia(IEnumerable<IMedia> mediaList, bool deleteAfter)
        {
            if (ArchiveDirectory == null)
                return;
            foreach (IMedia media in mediaList)
                if (media is ServerMedia)
                    ArchiveDirectory.ArchiveSave(media, _engine.VideoFormat, deleteAfter);
        }

        private ServerMedia _findComplementaryMedia(ServerMedia originalMedia)
        {
            if (_engine.PlayoutChannelPGM != null && _engine.PlayoutChannelPRV != null && _engine.PlayoutChannelPGM.OwnerServer != _engine.PlayoutChannelPRV.OwnerServer)
            {
                if ((originalMedia.Directory as ServerDirectory).Server == _engine.PlayoutChannelPGM.OwnerServer && _engine.PlayoutChannelPRV != null)
                    return (ServerMedia)((MediaDirectory)_engine.PlayoutChannelPRV.OwnerServer.MediaDirectory).FindMediaByMediaGuid(originalMedia.MediaGuid);
                if ((originalMedia.Directory as ServerDirectory).Server == _engine.PlayoutChannelPRV.OwnerServer && _engine.PlayoutChannelPGM != null)
                    return (ServerMedia)((MediaDirectory)_engine.PlayoutChannelPGM.OwnerServer.MediaDirectory).FindMediaByMediaGuid(originalMedia.MediaGuid);
            }
            return null;
        }

        private ServerMedia _getComplementaryMedia(ServerMedia originalMedia)
        {
            if (_engine.PlayoutChannelPGM != null && _engine.PlayoutChannelPRV != null && _engine.PlayoutChannelPGM.OwnerServer != _engine.PlayoutChannelPRV.OwnerServer)
            {
                if ((originalMedia.Directory as ServerDirectory).Server == _engine.PlayoutChannelPGM.OwnerServer && _engine.PlayoutChannelPRV != null)
                    return (ServerMedia)_engine.PlayoutChannelPRV.OwnerServer.MediaDirectory.GetServerMedia(originalMedia);
                if ((originalMedia.Directory as ServerDirectory).Server == _engine.PlayoutChannelPRV.OwnerServer && _engine.PlayoutChannelPGM != null)
                    return (ServerMedia)_engine.PlayoutChannelPGM.OwnerServer.MediaDirectory.GetServerMedia(originalMedia);
            }
            return null;
        }

        public void SynchronizePrvToPgm(bool deleteNotExisted)
        {
            if (MediaDirectoryPGM != null
                && MediaDirectoryPRV != null
                && MediaDirectoryPGM != MediaDirectoryPRV
                && MediaDirectoryPGM.IsInitialized
                && MediaDirectoryPRV.IsInitialized)
            {
                ThreadPool.QueueUserWorkItem(o =>
               {
                   Debug.WriteLine(this, "_synchronizePrvToPgm started");
                   var pGMMediaList = MediaDirectoryPGM.GetFiles().ToList();
                   foreach (ServerMedia pGMmedia in pGMMediaList)
                   {
                       if (pGMmedia.MediaStatus == TMediaStatus.Available && pGMmedia.FileExists())
                       {
                           ServerMedia pRVmedia = (ServerMedia)((MediaDirectory)MediaDirectoryPRV).FindMediaByMediaGuid(pGMmedia.MediaGuid);
                           if (pRVmedia == null)
                           {
                               pRVmedia = (ServerMedia)((ServerDirectory)MediaDirectoryPRV).FindMediaFirst(m => m.FileExists() && m.FileSize == pGMmedia.FileSize && m.FileName == pGMmedia.FileName && m.LastUpdated.DateTimeEqualToDays(pGMmedia.LastUpdated));
                               if (pRVmedia != null)
                               {
                                   pRVmedia.CloneMediaProperties(pGMmedia);
                                   pRVmedia.Verify();
                               }
                               else
                               {
                                   pRVmedia = (ServerMedia)MediaDirectoryPRV.GetServerMedia(pGMmedia, true);
                                   _fileManager.Queue(new FileOperation() { Kind = TFileOperationKind.Copy, SourceMedia = pGMmedia, DestMedia = pRVmedia });
                               }
                           }
                       }
                   }
                   if (deleteNotExisted)
                   {
                       var prvMediaList = MediaDirectoryPRV.GetFiles().ToList();
                       foreach (ServerMedia prvMedia in prvMediaList)
                       {
                           if ((ServerMedia)((MediaDirectory)MediaDirectoryPGM).FindMediaByMediaGuid(prvMedia.MediaGuid) == null)
                               _fileManager.Queue(new FileOperation() { Kind = TFileOperationKind.Delete, SourceMedia = prvMedia });
                       }
                       var duplicatesList = prvMediaList.Where(m => prvMediaList.FirstOrDefault(d => d.MediaGuid == m.MediaGuid && ((ServerMedia)d).idPersistentMedia != ((ServerMedia)m).idPersistentMedia) != null).Select(m => m.MediaGuid).Distinct();
                       foreach(var mediaGuid in duplicatesList)
                           ((MediaDirectory)MediaDirectoryPRV)
                           .FindMediaList(m => m.MediaGuid == mediaGuid)
                           .Skip(1).ToList()
                           .ForEach(m => m.Delete());
                   }
               });
            }
        }

        private void _syncAnimations()
        {
            Debug.WriteLine(this, "_syncAnimations");
            if (AnimationDirectoryPGM != null
                && AnimationDirectoryPRV != null
                && AnimationDirectoryPGM != AnimationDirectoryPRV
                && AnimationDirectoryPGM.IsInitialized
                && AnimationDirectoryPRV.IsInitialized)
            {
                foreach (ServerMedia pGMmedia in AnimationDirectoryPGM.GetFiles())
                {
                    if (pGMmedia.MediaStatus == TMediaStatus.Available)
                    {
                        var pRVmedia = (ServerMedia)MediaDirectoryPRV.GetFiles().FirstOrDefault(m => m.Folder == pGMmedia.Folder && m.FileName == pGMmedia.FileName && m.LastUpdated.DateTimeEqualToDays(pGMmedia.LastUpdated));
                        if (pRVmedia != null)
                        {
                            pRVmedia.CloneMediaProperties(pGMmedia);
                            pRVmedia.Save();
                        }
                    }
                }
            }
        }
        
        public override string ToString()
        {
            return _engine.EngineName + ":MediaManager";
        }


        public void Export(IEnumerable<MediaExport> exportList, IIngestDirectory directory)
        {
            foreach (MediaExport e in exportList)
                Export(e, directory);

        }

        private void Export(MediaExport export, IIngestDirectory directory)
        {
            _fileManager.Queue(new XDCAM.ExportOperation() { SourceMedia = export.Media, StartTC = export.StartTC, Duration = export.Duration, AudioVolume = export.AudioVolume, DestDirectory = directory as IngestDirectory });
        }

        public Guid IngestFile(string fileName)
        {
            var nameLowered = fileName.ToLower();
            IServerMedia dest;
            if ((dest  = (ServerMedia)(((MediaDirectory)MediaDirectoryPGM).FindMediaList(m => Path.GetFileNameWithoutExtension(m.FileName).ToLower() == nameLowered).FirstOrDefault())) != null)
                return dest.MediaGuid;
            foreach (IngestDirectory dir in _ingestDirectories)
            {
                Media source = dir.FindMedia(fileName);
                if (source != null)
                {
                    source.Verify();
                    if (source.MediaStatus == TMediaStatus.Available)
                    {
                        dest = MediaDirectoryPGM.GetServerMedia(source, false);
                        _fileManager.Queue(new ConvertOperation()
                        {
                            SourceMedia = source,
                            DestMedia = dest,
                            OutputFormat = _engine.VideoFormat,
                            AudioVolume = dir.AudioVolume,
                            SourceFieldOrderEnforceConversion = dir.SourceFieldOrder,
                            AspectConversion = dir.AspectConversion,
                        });
                        return dest.MediaGuid;
                    }
                }
            }
            return Guid.Empty;            
        }

        private void _mediaPGMVerified(object o, MediaDtoEventArgs e)
        {
            if (MediaDirectoryPRV != null
                && MediaDirectoryPRV != MediaDirectoryPGM
                && MediaDirectoryPRV.IsInitialized)
            {
                IMedia pgmMedia = MediaDirectoryPGM.FindMediaByDto(e.DtoGuid);
                if (pgmMedia == null)
                    return;
                IServerMedia media = MediaDirectoryPRV.GetServerMedia(pgmMedia, true);
                if (media.FileSize == pgmMedia.FileSize
                    && media.FileName == pgmMedia.FileName
                    && media.FileSize == pgmMedia.FileSize
                    && !media.Verified)
                    ((Media)media).Verify();
                if (!(media.MediaStatus == TMediaStatus.Available
                      || media.MediaStatus == TMediaStatus.Copying
                      || media.MediaStatus == TMediaStatus.CopyPending
                      || media.MediaStatus == TMediaStatus.Copied))
                    FileManager.Queue(new FileOperation { Kind = TFileOperationKind.Copy, SourceMedia = pgmMedia, DestMedia = media }, false);
            }
        }

        private void _mediaPGMRemoved(object o, MediaDtoEventArgs e)
        {
            if (MediaDirectoryPRV != null
                && MediaDirectoryPRV != MediaDirectoryPGM
                && MediaDirectoryPRV.IsInitialized)
            {
                IMedia mediaToDelete = ((MediaDirectory)MediaDirectoryPRV).FindMediaByMediaGuid(e.MediaGuid);
                if (mediaToDelete != null && mediaToDelete.FileExists())
                    FileManager.Queue(new FileOperation { Kind = TFileOperationKind.Delete, SourceMedia = mediaToDelete }, false);
            }
        }

        public IMedia GetPRVMedia(IMedia media)
        {
            if (media == null || MediaDirectoryPRV == null)
                return null;
            else
                return ((ServerDirectory)MediaDirectoryPRV).FindMediaByMediaGuid(media.MediaGuid);
        }
    }


}