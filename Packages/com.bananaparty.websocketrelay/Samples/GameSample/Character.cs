using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    [RequireComponent(typeof(CharacterController))]
    public class Character : MonoBehaviour, INetworkState
    {
        private const float Gravity = 9.81f;

        [SerializeField]
        private float _moveSpeed = 5f;

        [SerializeField]
        private float _rotationSpeed = 10f;

        [SerializeField]
        private float _jumpHeight = 2f;

        private CharacterController _characterController;
        private ICharacterInput _characterInput;
        private NetworkIdentity _networkIdentity;

        private float _verticalVelocity;
        private float _health = 100f;
        private Vector3 _position = Vector3.zero;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _networkIdentity = GetComponent<NetworkIdentity>();
            _characterInput = GetComponent<ICharacterInput>();
        }

        private void Update()
        {
            _characterInput.PollInput();

            if (_networkIdentity.NetworkAuthority)
                Move();
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteFloat(nameof(_health), _health);
            stateOutput.WriteVector3(nameof(_position), transform.position);
        }

        public void ReadNetworkState(IStateInput stateInput)
        {
            float health = stateInput.ReadFloat(nameof(_health));
            Vector3 position = stateInput.ReadVector3(nameof(_position));

            if (_networkIdentity.NetworkAuthority)
                return;

            _health = health;
            _position = position;
            transform.position = position;
        }

        private void Move()
        {
            Vector3 moveDirection = new Vector3(_characterInput.MovementInput.x, 0f, _characterInput.MovementInput.y).normalized;
            if (moveDirection != Vector3.zero)
            {
                _characterController.Move(moveDirection * (_moveSpeed * Time.deltaTime));

                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
            }

            if (_characterInput.JumpInput && _characterController.isGrounded)
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * 2f * Gravity);

            if (_characterController.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity -= Gravity * Time.deltaTime;

            _characterController.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
        }
    }
}
