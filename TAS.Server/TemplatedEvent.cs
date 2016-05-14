﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Common;
using TAS.Server.Common;
using TAS.Server.Interfaces;

namespace TAS.Server
{
    public class TemplatedEvent : Event, ITemplated
    {
        public TemplatedEvent(
                    IEngine engine,
                    UInt64 idRundownEvent,
                    UInt64 idEventBinding,
                    VideoLayer videoLayer,
                    TStartType startType,
                    TPlayState playState,
                    DateTime scheduledTime,
                    TimeSpan duration,
                    TimeSpan scheduledDelay,
                    Guid mediaGuid,
                    string eventName,
                    DateTime startTime,
                    bool isEnabled,
                    EventGPI gpi) : base(
                        engine,
                        idRundownEvent,
                        idEventBinding,
                        videoLayer,
                        TEventType.Template,
                        startType,
                        playState,
                        scheduledTime,
                        duration,
                        scheduledDelay,
                        TimeSpan.Zero,
                        mediaGuid,
                        eventName, 
                        startTime,
                        TimeSpan.Zero,
                        null,
                        TimeSpan.Zero,
                        TTransitionType.Cut,
                        0, 
                        0,
                        string.Empty,
                        isEnabled,
                        false,
                        false, 
                        gpi
                        )
        { }
        public Dictionary<string, string> Fields
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }
    }
}
