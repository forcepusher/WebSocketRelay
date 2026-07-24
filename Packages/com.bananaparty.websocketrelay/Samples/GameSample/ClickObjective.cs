using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class ClickObjective : MonoBehaviour
    {
        private TextMesh _clickCountText;

        private int _clickCount = 0;
        private int ClickCount
        {
            set
            {
                _clickCount = value;
                _clickCountText.text = value.ToString();
            }
            get => _clickCount;
        }

        private void Awake()
        {
            _clickCountText = GetComponentInChildren<TextMesh>();
        }

        private void OnMouseDown()
        {
            ClickCount += 1;
        }
    }
}
