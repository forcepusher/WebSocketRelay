using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ObjectNode : IStateNode
    {
        public string Name { get; }
        public Dictionary<string, IStateNode> Properties { get; } = new();

        public ObjectNode(string name = null)
        {
            Name = name;
        }
    }
}
