namespace BananaParty.WebSocketRelay
{
    public class Room
    {
        private readonly Network _network;
        public string Topic { get; private set; }

        public Room(string topic, Network network)
        {
            Topic = topic;
            _network = network;
        }

        public void Send(byte[] data)
        {
            _network.Send(Topic, data);
        }
    }
}
