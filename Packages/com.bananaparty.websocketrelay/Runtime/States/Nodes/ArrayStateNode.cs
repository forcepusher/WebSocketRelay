using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ArrayStateNode
    {
        public string Name { get; }
        public List<object> Elements { get; } = new();

        public ArrayStateNode(string name = null)
        {
            Name = name;
        }

        public void AddElement(object element) => Elements.Add(element);
    }
}
