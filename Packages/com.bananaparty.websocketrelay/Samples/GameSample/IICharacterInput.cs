using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public interface IICharacterInput
    {
        void PollInput();

        Vector2 MovementInput { get; }

        bool JumpInput { get; }
    }
}
