using UnityEngine;
using UnityEngine.InputSystem;

namespace BananaParty.WebSocketRelay.Samples
{
    public class KeyboardCharacterInput : MonoBehaviour, IICharacterInput
    {
        public Vector2 MovementInput { get; private set; } = Vector2.zero;

        public bool JumpInput { get; private set; } = false;

        public void PollInput()
        {
            Vector2 movementInput = Vector2.zero;
            JumpInput = false;

            if (Keyboard.current == null)
                return;

            if (Keyboard.current.wKey.isPressed) movementInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) movementInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) movementInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) movementInput.x += 1f;

            MovementInput = movementInput;
            JumpInput = Keyboard.current.spaceKey.wasPressedThisFrame;
        }
    }
}
