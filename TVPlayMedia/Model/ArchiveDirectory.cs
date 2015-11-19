﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Common;
using TAS.Server.Interfaces;

namespace TAS.Client.Model
{
    public class ArchiveDirectory : MediaDirectory, IArchiveDirectory
    {
        public TMediaCategory? SearchMediaCategory
        {
            get
            {
                return Get<TMediaCategory?>();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public string SearchString
        {
            get
            {
                return Get<string>();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public void ArchiveRestore(IArchiveMedia media, IServerMedia mediaPGM, bool toTop)
        {
            Invoke(parameters: new object[] { media, mediaPGM, toTop });
        }

        public void ArchiveSave(IMedia media, TVideoFormat outputFormat, bool deleteAfterSuccess)
        {
            Invoke(parameters: new object[] { media, outputFormat, deleteAfterSuccess});
        }

        public IArchiveMedia Find(IMedia media)
        {
            return Query<ArchiveMedia>(parameters: new object[] { media });
        }

        public IArchiveMedia GetArchiveMedia(IMedia media, bool searchExisting = true)
        {
            return Query<ArchiveMedia>(parameters: new object[] { media, searchExisting });
        }

        public void Search()
        {
            Invoke();
        }
    }
}