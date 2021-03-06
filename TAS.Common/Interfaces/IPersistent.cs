﻿namespace TAS.Common.Interfaces
{
    public interface IPersistent
    {
        ulong Id { get; set; }

        void Save();

        void Delete();
    }
}
