﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TAS.Client.Common;
using TAS.Common;
using TAS.Common.Interfaces;

namespace TAS.Client.ViewModels
{
    public class ExportViewmodel : ViewmodelBase
    {
        private readonly IMediaManager _mediaManager;
        private MediaDirectoryViewmodel _selectedDirectory;
        private Views.ExportView _view;
        private bool _concatMedia;
        private string _concatMediaName;
        private TmXFAudioExportFormat _mXFAudioExportFormat;
        private TmXFVideoExportFormat _mXFVideoExportFormat;

        public ExportViewmodel(IMediaManager mediaManager, IEnumerable<MediaExportDescription> exportList)
        {
            _mediaManager = mediaManager;
            Items = new ObservableCollection<ExportMediaViewmodel>(exportList.Select(media => new ExportMediaViewmodel(mediaManager, media)));
            Directories = mediaManager.IngestDirectories.Where(d => d.ContainsExport()).Select(d => new MediaDirectoryViewmodel(d, false, true)).ToList();
            SelectedDirectory = Directories.FirstOrDefault();
            CommandExport = new UICommand { ExecuteDelegate = _export, CanExecuteDelegate = _canExport };
            _view = new Views.ExportView { DataContext = this, Owner = System.Windows.Application.Current.MainWindow, ShowInTaskbar=false };
            _view.ShowDialog();
        }

        public ICommand CommandExport { get; }

        public List<MediaDirectoryViewmodel> Directories { get; }
        
        public MediaDirectoryViewmodel SelectedDirectory
        {
            get { return _selectedDirectory; }
            set
            {
                if (SetField(ref _selectedDirectory, value))
                {
                    NotifyPropertyChanged(nameof(IsConcatMediaNameVisible));
                    NotifyPropertyChanged(nameof(IsXDCAM));
                    NotifyPropertyChanged(nameof(IsMXF));
                    if (value?.ExportContainerFormat == TMovieContainerFormat.mxf 
                        || value?.IsXdcam == true)
                    {
                        MXFAudioExportFormat = value.MXFAudioExportFormat;
                        MXFVideoExportFormat = value.MXFVideoExportFormat;
                    }
                    InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<ExportMediaViewmodel> Items { get; }

        public bool IsXDCAM => _selectedDirectory?.IsXdcam == true;

        public bool IsMXF => _selectedDirectory?.ExportContainerFormat == TMovieContainerFormat.mxf || _selectedDirectory?.IsXdcam == true;

        public bool ConcatMedia
        {
            get { return _concatMedia; }
            set
            {
                if (SetField(ref _concatMedia, value))
                {
                    NotifyPropertyChanged(nameof(IsConcatMediaNameVisible));
                }
            }
        }

        public string ConcatMediaName
        {
            get { return _concatMediaName; }
            set
            {
                if (SetField(ref _concatMediaName, value))
                    InvalidateRequerySuggested();
            }
        }

        public bool IsConcatMediaNameVisible => _concatMedia && !IsXDCAM;

        public Array MXFVideoExportFormats { get; } = Enum.GetValues(typeof(TmXFVideoExportFormat));

        public Array MXFAudioExportFormats { get; } = Enum.GetValues(typeof(TmXFAudioExportFormat));

        public TmXFAudioExportFormat MXFAudioExportFormat { get { return _mXFAudioExportFormat; } set { SetField(ref _mXFAudioExportFormat, value); } }

        public TmXFVideoExportFormat MXFVideoExportFormat { get { return _mXFVideoExportFormat; } set { SetField(ref _mXFVideoExportFormat, value); } }

        public bool CanConcatMedia => Items.Count > 1;

        public int ExportMediaCount => Items.Count;

        public TimeSpan TotalTime { get { return TimeSpan.FromTicks(Items.Sum(m => m.Duration.Ticks)); } }


        private void _export (object o)
        {
            _checking = true;
            InvalidateRequerySuggested();
            try
            {
                //TODO: check if exporting files fit in device free space
            }
            finally
            {
                _checking = false;
                InvalidateRequerySuggested();
            }
            _mediaManager.Export(Items.Select(mevm => mevm.MediaExport).ToArray(), _concatMedia, _concatMediaName, (IIngestDirectory)SelectedDirectory.Directory, _mXFAudioExportFormat, _mXFVideoExportFormat);
            _view.Close();
        }

        private bool _checking;
        private bool _canExport(object o)
        {
            return !_checking && Items.Count > 0
                && SelectedDirectory.IsExport
                && (!IsConcatMediaNameVisible || !string.IsNullOrWhiteSpace(_concatMediaName));
        }
        
        protected override void OnDispose()
        {
            _view = null;
        }
    }
}
