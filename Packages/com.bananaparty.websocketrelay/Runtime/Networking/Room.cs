namespace BananaParty.WebSocketRelay
{
    public class Room
    {
        private readonly Network _network;
        public string RoomName { get; private set; }

        public Room(Network network, string roomName)
        {
            RoomName = roomName;
            _network = network;
        }

        public void Send(byte[] data)
        {
            _network.Send(RoomName, data);
        }
    }
}
