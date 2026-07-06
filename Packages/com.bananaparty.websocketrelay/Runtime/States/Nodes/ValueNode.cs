using System;

namespace BananaParty.WebSocketRelay
{
    public class ValueNode<T> : IStateNode
    {
        public string Name { get; }
        public T Value { get; }

        public ValueNode(string name, T value)
        {
            Name = name;
            Value = value;
        }
    }
}
