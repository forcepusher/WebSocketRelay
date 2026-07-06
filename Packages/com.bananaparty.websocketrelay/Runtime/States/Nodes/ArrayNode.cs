using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ArrayNode : IStateNode
    {
        public string Name { get; }
        public List<IStateNode> Elements { get; } = new();

        public ArrayNode(string name = null)
        {
            Name = name;
        }
    }
}
