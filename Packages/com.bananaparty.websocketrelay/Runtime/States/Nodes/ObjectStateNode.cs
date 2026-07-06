using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ObjectState
    {
        public Dictionary<string, object> Properties { get; } = new();
        public void SetProperty(string name, object value) => Properties[name] = value;
    }
}
