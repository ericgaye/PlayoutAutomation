﻿using System.Xml.Serialization;
using Newtonsoft.Json;
using TAS.Common;
using TAS.Database;
using TAS.Common.Interfaces;

namespace TAS.Server.Security
{
    public class Group: SecurityObjectBase, IGroup
    {
        public Group():base(null) { }
        public Group(IAuthenticationService authenticationService): base(authenticationService) { }

        [JsonProperty, XmlIgnore]
        public override SecurityObjectType SecurityObjectTypeType { get; } = SecurityObjectType.Group;

        public override void Save()
        {
            if (Id == default(ulong))
            {
                AuthenticationService.AddGroup(this);
                this.DbInsertSecurityObject();
            }
            else
                this.DbUpdateSecurityObject();
        }

        public override void Delete()
        {
            AuthenticationService.RemoveGroup(this);
            this.DbDeleteSecurityObject();
            Dispose();
        }
    }
}
