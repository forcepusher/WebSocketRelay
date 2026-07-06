using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ObjectStateNode
    {
        public string Name { get; }
        public Dictionary<string, object> Properties { get; } = new();

        public ObjectStateNode(string name = null)
        {
            Name = name;
        }

        public void SetProperty(string name, object value) => Properties[name] = value;
    }
}
