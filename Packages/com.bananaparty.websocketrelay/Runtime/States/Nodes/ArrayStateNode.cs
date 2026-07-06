using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class ArrayState
    {
        public List<object> Elements { get; } = new();
        public void AddElement(object element) => Elements.Add(element);
    }
}
